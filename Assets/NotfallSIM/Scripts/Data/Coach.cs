using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NotfallSIM.Core;

namespace NotfallSIM.Data
{
    /// <summary>Was der Coach weiß: Fall, Datenbanken, Protokoll und aktuelle Vitalwerte.</summary>
    public class CoachContext
    {
        public ScenarioDefinition Scenario;
        public Func<string, InjuryDefinition> Injury;
        public InterventionDefinition[] Interventions;
        public ImagingModality[] Imaging;
        public IReadOnlyList<LogEntry> Events;
        public VitalSigns Vitals;
    }

    /// <summary>Ein Schritt: Phase (x, A, B, C, D, E, Bildgebung, Verlauf), was zu tun ist, warum, Status.</summary>
    public class CoachItem
    {
        public string Phase;        // "x", "A", "B", "C", "D", "E", "Bildgebung", "Verlauf", "Vermeiden"
        public string Key;          // Maßnahmen-ID, "exam:<Region>" oder "imaging:<ID>" (zum Hervorheben des Knopfes)
        public string Action;       // Was zu tun ist
        public string Why;          // Begründung
        public double? Deadline_s;  // Zeitziel (absolut ab Szenariostart)
        public double? Done_s;      // erledigt um
        public bool Missed;         // Zeitziel verpasst bzw. überschritten
        public bool Treats;         // gezielte Behandlung einer erkannten Verletzung
        public override string ToString() => $"{Phase}: {Action}";
    }

    /// <summary>
    /// Untersuchungs- und Behandlungsvorschläge nach xABCDE (ATLS, S3 Polytrauma).
    /// <b>Tipp</b>: der nächste sinnvolle Schritt — nur aus dem, was man im Schockraum wissen kann (Vitalwerte,
    /// Mechanismus, eigene Untersuchungen, Bildgebung, gesetzte Verdachtsdiagnosen, sichtbare Blutungen). Maßnahmen,
    /// die eine bestimmte Verletzung voraussetzen (Beckengurt, Entlastung, Laparotomie …), schlägt er erst vor, wenn
    /// diese Verletzung erkennbar ist.
    /// <b>Strategie</b>: das optimale Vorgehen für diesen Fall mit Zeitzielen und Stand — die Lösung, inklusive Diagnosen.
    /// Reines C#, testbar; gleich in web/engine.js.
    /// </summary>
    public static class Coach
    {
        static readonly string[] PhaseOrder = { "x", "A", "B", "C", "D", "E", "Bildgebung", "Verlauf" };
        public static readonly Dictionary<string, string> PhaseNames = new Dictionary<string, string>
        {
            { "x", "x – kritische Blutung" }, { "A", "A – Atemweg" }, { "B", "B – Atmung" }, { "C", "C – Kreislauf" },
            { "D", "D – Neurologie" }, { "E", "E – Entkleiden, Wärme" }, { "Bildgebung", "Bildgebung und Versorgung" },
            { "Verlauf", "Re-Evaluation" }, { "Vermeiden", "Vermeiden" },
        };
        static readonly string[] SurveyRegions = { "Thorax", "Abdomen", "Becken", "Oberschenkel rechts", "Oberschenkel links", "Kopf" };
        static readonly string[] BodyCheck = { "Unterschenkel rechts", "Unterschenkel links", "Arm rechts", "Arm links" };

        // ------------------------------------------------------------------ Tipp

        public static CoachItem Tip(CoachContext c)
        {
            if (c.Vitals.CardiacArrest)
                return new CoachItem { Phase = "C", Key = "arrest", Action = "Kreislaufstillstand: Reanimation",
                                       Why = "Traumatischer Stillstand: Ursachen behandeln (Hypovolämie, Spannungspneu, Hypoxie, Tamponade). Der Fall endet hier – im Debriefing nachsehen, was gefehlt hat." };
            var open = Candidates(c, onlyKnown: true).Where(i => i.Done_s == null && i.Phase != "Vermeiden").ToList();
            // innerhalb einer Phase: erkannte Verletzung zuerst behandeln (Beckengurt vor dem nächsten Untersuchungsschritt), dann nach Zeitziel
            var best = open.OrderBy(i => Array.IndexOf(PhaseOrder, i.Phase)).ThenBy(i => i.Treats ? 0 : 1)
                           .ThenBy(i => i.Deadline_s ?? double.MaxValue).FirstOrDefault();
            if (best != null) return best;
            return new CoachItem
            {
                Phase = "Verlauf", Key = "nibp", Action = "Re-Evaluation nach xABCDE",
                Why = "Primary Survey abgeschlossen. Vitalwerte im Blick behalten (NIBP messen), BGA in 10–15 min kontrollieren, " +
                      "nach jeder Maßnahme und jeder Verschlechterung wieder bei x/A beginnen."
            };
        }

        /// <summary>Hinweise auf Grenzen, die gleich überschritten werden (z. B. Kristalloide).</summary>
        public static List<string> Cautions(CoachContext c)
        {
            var list = new List<string>();
            foreach (var r in Assessment.Evaluate(c.Scenario.assessment ?? new AssessmentCriterion[0], c.Events))
                if (r.Criterion.type == "maxCount" && r.Criterion.tags.All(t => !t.StartsWith("state:") && !t.StartsWith("warning:")) && r.Count >= r.Criterion.limit)
                    list.Add($"Grenze erreicht: {r.Criterion.description}");
            return list;
        }

        // ------------------------------------------------------------------ Strategie

        public static List<CoachItem> Strategy(CoachContext c) =>
            Candidates(c, onlyKnown: false)
                .OrderBy(i => i.Phase == "Vermeiden" ? 99 : Array.IndexOf(PhaseOrder, i.Phase))
                .ThenBy(i => i.Deadline_s ?? double.MaxValue).ToList();

        // ------------------------------------------------------------------ Kandidaten

        static List<CoachItem> Candidates(CoachContext c, bool onlyKnown)
        {
            var v = c.Vitals;
            double now = v.SimTime_s;
            var ev = c.Events ?? new List<LogEntry>();
            var items = new List<CoachItem>();
            var active = (c.Scenario.injuries ?? new ScenarioInjury[0]).Where(si => onlyKnown ? si.onsetTime_s <= now : true)
                         .Select(si => new { si, def = c.Injury(si.injuryId) }).Where(x => x.def != null).ToList();
            var known = new HashSet<string>(active.Where(x => !onlyKnown || Known(c, x.si, x.def)).Select(x => x.def.id));

            // 1. Bewertungskriterien des Falls (mit Zeitzielen); spezifische Maßnahmen nur bei erkennbarer Verletzung
            foreach (var r in Assessment.Evaluate(c.Scenario.assessment ?? new AssessmentCriterion[0], ev))
            {
                var cr = r.Criterion;
                if (r.Outcome == CriterionOutcome.NotApplicable && onlyKnown) continue;
                string tag = cr.tags[0];
                if (cr.type == "maxCount")
                {
                    items.Add(new CoachItem { Phase = "Vermeiden", Key = tag, Action = cr.description, Why = cr.rationale, Missed = r.Outcome == CriterionOutcome.Exceeded });
                    continue;
                }
                if (onlyKnown && Specific(c, tag) && !Linked(c, tag, cr).Any(id => known.Contains(id))) continue;
                items.Add(new CoachItem
                {
                    Phase = PhaseOf(c, tag), Key = KeyOf(c, tag), Action = ActionText(c, tag, cr.description), Treats = Specific(c, tag),
                    Why = cr.rationale, Deadline_s = r.Outcome == CriterionOutcome.NotApplicable ? (double?)null : r.Deadline_s,
                    Done_s = r.Time_s, Missed = r.Outcome == CriterionOutcome.Missed && now > r.Deadline_s || r.Outcome == CriterionOutcome.Late
                });
            }

            // 2. Allgemeines xABCDE aus dem, was man sieht (ergänzt, was die Kriterien nicht abdecken)
            Func<string, double?> first = prefix => ev.Where(e => e.Tag != null && e.Tag.StartsWith(prefix, StringComparison.Ordinal)).Select(e => (double?)e.Time_s).FirstOrDefault();
            Action<string, string, string, string, string> add = (phase, key, action, why, tagPrefix) =>
            {
                if (items.Any(i => i.Key == key)) return;
                items.Add(new CoachItem { Phase = phase, Key = key, Action = action, Why = why, Done_s = first(tagPrefix) });
            };
            bool shock = !v.CardiacArrest && (v.SystolicBP_mmHg < 90 || v.HeartRate_bpm > 110 || v.BaseExcess < -6);
            // Strategie: Schockmaßnahmen nur, wenn der Fall eine Blutung hat
            bool bleeding = active.Any(x => x.def.effects != null && x.def.effects.Any(e => e.effectType == "Hemorrhage"));
            string vitals = $"HF {v.HeartRate_bpm:0}, RR {v.SystolicBP_mmHg:0}/{v.DiastolicBP_mmHg:0}";

            if (onlyKnown && v.GCS <= 8 && !v.Intubated)
                add("A", "intubation", "Atemweg sichern: Narkose und Intubation",
                    onlyKnown ? $"GCS {v.GCS:0} – Schutzreflexe unsicher (GCS ≤ 8: Intubation, S3 GoR A). Im Schock Einleitung vorsichtig dosieren (Ketamin oder reduziertes Propofol)."
                              : "Bei GCS ≤ 8 oder Ateminsuffizienz (S3 GoR A). Im Schock vorsichtig einleiten.", "intervention:Intubation");
            add("B", "o2", "Sauerstoff geben", "Jeder Schwerverletzte bekommt O2, bis die Oxygenierung gesichert ist.", "intervention:Oxygen");
            add("B", "exam:Thorax", "Thorax untersuchen", "Atemgeräusch, Thoraxbewegung, Trachea, Halsvenen: Spannungspneu und Hämatothorax ausschließen.", "exam:Thorax");
            add("C", "bga", "BGA abnehmen", "Base Excess und Laktat zeigen den Schock früher als Puls und Blutdruck; Hb, Calcium, Kalium.", "intervention:BloodGas::");
            foreach (var region in new[] { "Abdomen", "Becken", "Oberschenkel rechts", "Oberschenkel links" })
                add("C", "exam:" + region, $"{region} untersuchen", "Blutungsquellen suchen: Thorax, Abdomen, Becken, lange Röhrenknochen (und nach außen).", "exam:" + region);
            if (onlyKnown ? shock : bleeding)
            {
                add("C", "efast", "eFAST", "Freie Flüssigkeit im Bauch, Perikard, Pneumothorax – am Bett in 1 min.", "imaging:efast");
                add("C", "ek", "Blutprodukte: EK, dazu FFP etwa 1:1",
                    onlyKnown ? $"Schockzeichen ({vitals}). Früh Blut statt viel Kristalloid; permissive Hypotonie RRsys 80–90 ohne SHT." : "Bei Schockzeichen früh Blutprodukte, Kristalloide restriktiv.", "intervention:FluidInfusion:Blood");
                add("C", "txa", "Tranexamsäure 1 g", "Bei relevanter Blutung so früh wie möglich, spätestens 3 h nach Trauma (CRASH-2).", "intervention:Medication:TXA");
            }
            int units = ev.Count(e => e.Tag != null && e.Tag.StartsWith("intervention:FluidInfusion:Blood", StringComparison.Ordinal));
            if (onlyKnown ? units >= 2 : bleeding)
            {
                add("C", "ffp", "Plasma (FFP) zu den EK", "Nur EK verdünnt die Gerinnungsfaktoren; Massivtransfusion EK:FFP 1:1 bis 2:1.", "intervention:FluidInfusion:Plasma");
                if (!onlyKnown || v.IonizedCalcium < 1.1)
                    add("C", "calcium", "Calcium substituieren", $"Citrat aus Blutprodukten senkt das ionisierte Calcium{(onlyKnown ? $" (aktuell {v.IonizedCalcium:0.00} mmol/L)" : "")}; < 1,0 mmol/L schwächt Herz und Gerinnung.", "intervention:Medication:Calcium");
            }
            add("D", "exam:Kopf", "Kopf untersuchen: GCS, Pupillen", "Bewusstsein, Pupillen, Lateralisation – SHT früh erkennen.", "exam:Kopf");
            add("E", "waerme", "Wärmeerhalt und Infusionswärmer", $"Hypothermie verschlechtert die Gerinnung (letale Trias){(onlyKnown && v.Temperature_C > 0 ? $"; aktuell {v.Temperature_C:0.0} °C" : "")}.", "intervention:Supportive:Warming");
            foreach (var region in BodyCheck)
                add("E", "exam:" + region, $"{region} untersuchen (Bodycheck)", "Komplett entkleiden und alles ansehen: offene Frakturen, Wunden, Durchblutung.", "exam:" + region);

            // 3. Bildgebung bzw. Versorgung: stabil → Ganzkörper-CT, instabil → kein CT, Blutungsquelle stoppen
            bool primaryDone = SurveyRegions.All(r => first("exam:" + r) != null);
            if (!onlyKnown || primaryDone)
            {
                if (onlyKnown && shock)
                    add("Bildgebung", "instabil", "Instabil: nicht ins CT – Blutungsquelle stoppen",
                        $"{vitals}. Erst Blutung kontrollieren (Beckengurt, Not-OP/Angiographie, Laparotomie), dann CT.", "\u0000");
                else
                    add("Bildgebung", "ct_ganzkoerper", "Ganzkörper-CT", "Nach dem Primary Survey beim stabilen Patienten (S3: Polytrauma-Spirale).", "imaging:ct_ganzkoerper");
            }
            return items;
        }

        /// <summary>Kann man die Verletzung erkennen? Untersucht, in der Bildgebung gesehen, als Verdacht gesetzt oder außen sichtbar blutend.</summary>
        static bool Known(CoachContext c, ScenarioInjury si, InjuryDefinition d)
        {
            var ev = c.Events ?? new List<LogEntry>();
            if (d.effects != null && d.effects.Any(e => e.effectType == "Hemorrhage" && (e.lesion == "partial" || e.lesion == "transection")
                                                     && (e.compartment == "Extremity" || e.compartment == "LeftArm" || e.compartment == "RightArm")))
                return true;
            foreach (var e in ev)
            {
                if (e.Tag == null || e.Time_s < si.onsetTime_s) continue;
                if (e.Tag == "exam:" + d.region) return true;
                if (e.Tag == DiagnosisEvaluation.SuspectPrefix + d.id) return true;
                if (e.Tag.StartsWith("result:Imaging:", StringComparison.Ordinal))
                {
                    var m = c.Imaging?.FirstOrDefault(x => x.id == e.Tag.Substring("result:Imaging:".Length));
                    if (m != null && d.imaging != null && d.imaging.Any(f => m.covers.Contains(f.modality))) return true;
                }
            }
            return false;
        }

        /// <summary>Maßnahmen, die eine bestimmte Verletzung voraussetzen.</summary>
        static bool Specific(CoachContext c, string tag) =>
            tag.StartsWith("intervention:HemorrhageControl") || tag.StartsWith("intervention:NeedleDecompression") || tag.StartsWith("intervention:ChestDrain")
            || tag.StartsWith("intervention:NeurosurgicalDecompression") || tag.StartsWith("intervention:Osmotherapy")
            || tag.StartsWith("intervention:Medication:Antibiotic") || tag.StartsWith("imaging:cct");

        /// <summary>Verletzungen des Falls, zu denen ein Kriterium gehört (gleiches Kriterium in injuries.json oder passender Ort).</summary>
        static IEnumerable<string> Linked(CoachContext c, string tag, AssessmentCriterion cr)
        {
            foreach (var si in c.Scenario.injuries ?? new ScenarioInjury[0])
            {
                var d = c.Injury(si.injuryId);
                if (d == null) continue;
                bool same = d.criteria != null && d.criteria.Any(x => x.tags != null && x.tags.Contains(tag));
                var parts = tag.Split(':');
                string target = parts.Length > 2 ? parts[2] : "";
                bool place = d.effects != null && d.effects.Any(e => target.Split(',').Contains(e.compartment));
                bool brain = (tag.StartsWith("intervention:NeurosurgicalDecompression") || tag.StartsWith("intervention:Osmotherapy") || tag.StartsWith("imaging:cct"))
                             && d.effects != null && d.effects.Any(e => e.effectType == "TraumaticBrainInjury");
                if (same || place || brain) yield return d.id;
            }
        }

        static string PhaseOf(CoachContext c, string tag)
        {
            if (tag.StartsWith("exam:"))
            {
                string r = tag.Substring(5);
                return r == "Thorax" ? "B" : r == "Kopf" ? "D" : r == "Abdomen" || r == "Becken" || r.StartsWith("Oberschenkel") ? "C" : "E";
            }
            if (tag.StartsWith("imaging:")) return tag == "imaging:efast" ? "C" : tag == "imaging:cct" ? "D" : "Bildgebung";
            if (tag.StartsWith("intervention:HemorrhageControl:Extremity") || tag.StartsWith("intervention:HemorrhageControl:LeftArm")) return "x";
            if (tag.StartsWith("intervention:Intubation")) return "A";
            if (tag.StartsWith("intervention:Oxygen") || tag.StartsWith("intervention:NeedleDecompression") || tag.StartsWith("intervention:ChestDrain")) return "B";
            if (tag.StartsWith("intervention:NeurosurgicalDecompression") || tag.StartsWith("intervention:Osmotherapy")) return "D";
            if (tag.StartsWith("intervention:HemorrhageControl")) return tag.Contains("laparotomie") || tag.Contains("angio_op") ? "Bildgebung" : "C";
            if (tag.StartsWith("intervention:Medication:Antibiotic") || tag.StartsWith("intervention:Supportive")) return "E";
            return "C";
        }

        static InterventionDefinition Def(CoachContext c, string tag) =>
            c.Interventions?.FirstOrDefault(d => $"intervention:{d.type}:{(string.IsNullOrEmpty(d.target) ? d.compound : d.target)}:{d.id}".StartsWith(tag, StringComparison.Ordinal));

        static string KeyOf(CoachContext c, string tag)
        {
            if (tag.StartsWith("exam:")) return tag;
            if (tag.StartsWith("imaging:")) return tag.Substring(8);
            var d = Def(c, tag);
            return d != null ? d.id : tag;
        }

        static string ActionText(CoachContext c, string tag, string description)
        {
            if (tag.StartsWith("exam:")) return $"{tag.Substring(5)} untersuchen";
            if (tag.StartsWith("imaging:"))
            {
                var m = c.Imaging?.FirstOrDefault(x => x.id == tag.Substring(8));
                return m != null ? m.label : description;
            }
            var d = Def(c, tag);
            return d != null ? d.label : description;
        }

        /// <summary>Zeit als mm:ss.</summary>
        public static string Clock(double s) { int t = Math.Max(0, (int)s); return $"{t / 60:00}:{t % 60:00}"; }

        /// <summary>Statuszeile eines Schritts (für beide Oberflächen gleich).</summary>
        public static string Status(CoachItem i, double now)
        {
            if (i.Phase == "Vermeiden") return i.Missed ? "überschritten" : "eingehalten";
            if (i.Done_s != null) return $"erledigt {Clock(i.Done_s.Value)}" + (i.Missed ? " (zu spät)" : "");
            if (i.Deadline_s != null) return now > i.Deadline_s ? $"überfällig (Ziel {Clock(i.Deadline_s.Value)})" : $"offen, Ziel bis {Clock(i.Deadline_s.Value)}";
            return "offen";
        }
    }
}

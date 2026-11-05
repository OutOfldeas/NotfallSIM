using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Core;
using NotfallSIM.Engines;

namespace NotfallSIM.Data
{
    // ---------- triage.json ----------
    [Serializable]
    public class TriageRules
    {
        public double rrRedAbove, radialPulseMinSbp, followsCommandsMinGcs, walkMinGcs, walkMinSbp, walkMinSpo2;
        /// <summary>Wirksame Verletzung ab diesem AIS: liegt (nicht gehfähig); 0 = ohne diese Regel.</summary>
        [System.Runtime.Serialization.OptionalField] public int nonAmbulatoryMinAis;
        public string[] nonAmbulatoryRegions;
        public string notes;
    }
    [Serializable] public class TriageCategory { public string id, label, meaning, color; }
    [Serializable] public class TriageMinorInjury { public string injuryId; public double severityMin, severityMax; }
    [Serializable]
    public class TriageIncident
    {
        public string label;
        public string[] mechanisms;
        public int minorCount;
        public TriageMinorInjury[] minor;
    }
    [Serializable] public class TriageSource { public string id, citation, url, note; }
    [Serializable]
    public class TriageFile
    {
        public string version, note;
        public TriageRules rules;
        public TriageCategory[] categories;
        public TriageIncident incident;
        public TriageSource[] sources;
    }

    /// <summary>Sichtungskarte einer Person: was man an der Einsatzstelle sieht, und die Kategorie nach mSTaRT.</summary>
    public class TriageCard
    {
        public string Label;
        public ScenarioDefinition Scenario;
        public bool Walks, Breathing, RadialPulse, FollowsCommands;
        public double Rr, Hr, Sbp, Gcs, SpO2;
        public List<string> Findings = new List<string>();
        /// <summary>Kategorie nach mSTaRT (T1, T2, T3, T4) und der Schritt, der entschieden hat.</summary>
        public string Category, Reason;
        /// <summary>Schwerste Verletzung (AIS) und ob Verletzungen erst später auffällig werden (Nachsichtung).</summary>
        public int MaxAis;
        public bool HasDelayed;
    }

    /// <summary>
    /// Vorsichtung beim Massenanfall von Verletzten nach mSTaRT (Kanz 2006). Reines C#, im Browser-Prototyp gleich
    /// (web/engine.js, triage). Befunde aus der Mock-Physiologie 1 s nach Szenariobeginn; die Grenzwerte, ab denen ein Befund
    /// als tastbar/befolgt/gehfähig gilt, stehen in triage.json.
    /// </summary>
    public static class Triage
    {
        /// <summary>Rangfolge der Dringlichkeit: T1 3, T2 2, T3 1, T4 0 (abwartend).</summary>
        public static int Rank(string category) => category == "T1" ? 3 : category == "T2" ? 2 : category == "T3" ? 1 : 0;

        /// <summary>Bewertung einer eigenen Sichtung gegen mSTaRT: richtig, Übertriage, Untertriage.</summary>
        public static string Compare(string chosen, string reference) =>
            chosen == reference ? "richtig" : Rank(chosen) > Rank(reference) ? "Übertriage" : "Untertriage";

        public static TriageCard Assess(ScenarioDefinition s, IDictionary<string, InjuryDefinition> defs, TriageRules r, string label)
        {
            var e = new MockPhysiologyEngine(1, PhysiologyMode.Textbook);
            e.Initialize(s.patient);
            var pending = s.injuries.ToList();
            const double Observe_s = 1.0;
            for (int step = 0; step < 10; step++)
            {
                double t = e.Current.SimTime_s;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var si = pending[i];
                    if (si.onsetTime_s > t) continue;
                    foreach (var ev in Events(defs[si.injuryId], si)) e.ApplyInjury(ev);
                    pending.RemoveAt(i);
                }
                e.Advance(0.1);
            }
            var v = e.Current;
            var active = s.injuries.Where(si => si.onsetTime_s <= Observe_s).Select(si => defs[si.injuryId]).ToList();
            var card = new TriageCard
            {
                Label = label, Scenario = s,
                Rr = v.CardiacArrest ? 0 : ScenarioGenerator.R(v.RespirationRate_bpm), Hr = v.CardiacArrest ? 0 : ScenarioGenerator.R(v.HeartRate_bpm),
                Sbp = v.CardiacArrest ? 0 : ScenarioGenerator.R(v.SystolicBP_mmHg), Gcs = v.CardiacArrest ? 3 : v.GCS, SpO2 = v.CardiacArrest ? 0 : ScenarioGenerator.R(v.SpO2_percent),
                MaxAis = s.injuries.Select(si => defs[si.injuryId].aisSeverity).DefaultIfEmpty(0).Max(),
                HasDelayed = s.injuries.Any(si => si.onsetTime_s > Observe_s)
            };
            bool legs = active.Any(d => (r.nonAmbulatoryRegions != null && r.nonAmbulatoryRegions.Contains(d.region)) || (r.nonAmbulatoryMinAis > 0 && d.aisSeverity >= r.nonAmbulatoryMinAis));
            card.Breathing = !v.CardiacArrest && card.Rr > 0;
            card.RadialPulse = !v.CardiacArrest && card.Sbp >= r.radialPulseMinSbp;
            card.FollowsCommands = !v.CardiacArrest && card.Gcs >= r.followsCommandsMinGcs;
            card.Walks = !v.CardiacArrest && card.Gcs >= r.walkMinGcs && card.Sbp >= r.walkMinSbp && card.SpO2 >= r.walkMinSpo2 && card.Rr <= r.rrRedAbove && !legs;
            foreach (var d in active)
                if (d.clinicalFindings != null && d.clinicalFindings.Length > 0 && card.Findings.Count < 3) card.Findings.Add(d.clinicalFindings[0]);

            // mSTaRT, Vorsichtung Erwachsene
            if (card.Walks) { card.Category = "T3"; card.Reason = "gehfähig"; }
            else if (!card.Breathing) { card.Category = "T4"; card.Reason = "keine Atmung nach Freimachen der Atemwege"; }
            else if (card.Rr > r.rrRedAbove) { card.Category = "T1"; card.Reason = $"Atemfrequenz {card.Rr:0}/min (> {r.rrRedAbove:0})"; }
            else if (!card.RadialPulse) { card.Category = "T1"; card.Reason = "Radialispuls nicht tastbar"; }
            else if (!card.FollowsCommands) { card.Category = "T1"; card.Reason = "befolgt keine Aufforderungen"; }
            else { card.Category = "T2"; card.Reason = "nicht gehfähig, Atmung, Puls und Bewusstsein ausreichend"; }
            return card;
        }

        static IEnumerable<InjuryEvent> Events(InjuryDefinition def, ScenarioInjury si)
        {
            foreach (var e in def.effects)
            {
                InjuryEffectType type;
                if (!Enum.TryParse(e.effectType, out type)) continue;
                yield return new InjuryEvent
                {
                    InjuryId = def.id, Effect = type, Compartment = e.compartment, Severity = si.severity, BaseRate_mL_min = e.baseRate_mL_min,
                    MaxTotalLoss_mL = e.maxTotalLoss_mL, Lesion = e.lesion, ProgressionRate_per_min = e.progressionRate_per_min, InitialLevel = e.initialLevel
                };
            }
        }

        /// <summary>
        /// Zufällige Lage mit mehreren Verletzten: je Mechanismus aus triage.json ein Zufallsfall, dazu Leichtverletzte
        /// (eine kleine Verletzung, kein präklinischer Blutverlust), gemischt. Gleicher Seed → gleiche Lage (auch im Browser).
        /// </summary>
        public static List<TriageCard> Incident(MechanismDatabaseFile mech, IList<InjuryDefinition> injuryDefs, TriageFile tf, uint seed)
        {
            var defs = injuryDefs.ToDictionary(d => d.id);
            var inc = tf.incident;
            var list = new List<ScenarioDefinition>();
            unchecked
            {
                for (int i = 0; i < inc.mechanisms.Length; i++)
                    list.Add(ScenarioGenerator.Generate(mech, inc.mechanisms[i], injuryDefs, seed + 7919u * (uint)(i + 1)));
                var rng = new SeededRandom(seed ^ 0x9E3779B9u);
                for (int k = 0; k < inc.minorCount; k++)
                {
                    var s = ScenarioGenerator.Generate(mech, inc.mechanisms[k % inc.mechanisms.Length], injuryDefs, seed + 104729u * (uint)(k + 1));
                    var pick = inc.minor[(int)Math.Floor(rng.Next() * inc.minor.Length)];
                    double sev = ScenarioGenerator.Round2(rng.Range(pick.severityMin, pick.severityMax));
                    s.injuries = new[] { new ScenarioInjury { injuryId = pick.injuryId, severity = sev, onsetTime_s = 0 } };
                    s.patient.initialBloodLoss_mL = 0;
                    s.description = $"{inc.label}: beteiligte Person, an der Unfallstelle angetroffen.";
                    s.expectedCourse = "Verletzungen: " + defs[pick.injuryId].name + ".";
                    s.assessment = ScenarioGenerator.Criteria(mech, s.injuries, defs).ToArray();
                    list.Add(s);
                }
                // mischen (Fisher–Yates)
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = (int)Math.Floor(rng.Next() * (i + 1));
                    var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
                }
            }
            var cards = new List<TriageCard>();
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                s.id = $"manv_{seed}_{i + 1}";
                s.title = $"MANV: Person {i + 1}";
                cards.Add(Assess(s, defs, tf.rules, $"Person {i + 1}"));
            }
            return cards;
        }
    }
}

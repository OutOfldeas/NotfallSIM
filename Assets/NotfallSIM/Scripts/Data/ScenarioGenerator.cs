using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NotfallSIM.Core;

namespace NotfallSIM.Data
{
    /// <summary>
    /// Kleiner, plattformgleicher Zufallsgenerator (Mulberry32).
    /// Absichtlich nicht System.Random: web/engine.js muss aus demselben Seed
    /// exakt dasselbe Szenario erzeugen (Szenario-Code teilbar zwischen Unity und Browser).
    /// </summary>
    public class SeededRandom
    {
        uint _state;
        public SeededRandom(uint seed) { _state = seed; }
        public double Next()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
        public double Range(double min, double max) => min + Next() * (max - min);
    }

    /// <summary>
    /// Erzeugt aus einem Unfallmechanismus ein plausibles, zufälliges Verletzungsmuster
    /// samt Patient, präklinischem Blutverlust und passenden Bewertungszielen.
    /// Alle Zahlen kommen aus mechanisms.json bzw. injuries.json.
    /// </summary>
    public static class ScenarioGenerator
    {
        const int MaxAttempts = 50;

        public static ScenarioDefinition Generate(MechanismDatabaseFile db, string mechanismId,
                                                  IEnumerable<InjuryDefinition> injuryDefs, uint seed)
        {
            var mech = db.mechanisms.FirstOrDefault(m => m.id == mechanismId)
                       ?? throw new KeyNotFoundException($"Mechanismus '{mechanismId}' nicht gefunden");
            var defs = injuryDefs.ToDictionary(d => d.id);
            var rng = new SeededRandom(seed);
            var a = db.anthropometry;

            // Patient — feste Reihenfolge der Zufallszüge (Parität mit JS).
            bool male = rng.Next() < mech.maleShare;
            int age = (int)R(rng.Range(mech.ageMin, mech.ageMax));
            double weight = R(male ? rng.Range(a.maleWeightMin_kg, a.maleWeightMax_kg)
                                            : rng.Range(a.femaleWeightMin_kg, a.femaleWeightMax_kg));
            double height = R(male ? rng.Range(a.maleHeightMin_cm, a.maleHeightMax_cm)
                                            : rng.Range(a.femaleHeightMin_cm, a.femaleHeightMax_cm));
            bool betaBlocker = rng.Next() < a.betaBlockerShare && age >= a.betaBlockerMinAge;
            bool unrestrained = rng.Next() < mech.unrestrainedShare;
            int speed = (int)R(rng.Range(mech.speedMin, mech.speedMax) / 5) * 5;
            int fallHeight = (int)R(rng.Range(mech.heightMin, mech.heightMax));
            int rescue = (int)R(rng.Range(mech.rescueMin, mech.rescueMax) / 5) * 5;

            // Verletzungen — je Kandidat genau drei Züge (Auswahl, Schweregrad, Beginn).
            // Enthält ein Durchgang keine kreislauf- oder hirnrelevante Verletzung, wird neu gezogen
            // (bedingte Verteilung bleibt proportional zu den Wahrscheinlichkeiten).
            var chosen = new List<ScenarioInjury>();
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                chosen.Clear();
                foreach (var c in mech.injuries)
                {
                    double p = c.probability;
                    if (unrestrained)
                        foreach (var f in mech.unrestrainedMultipliers)
                            if (f.injuryId == c.injuryId) p *= f.factor;
                    double pick = rng.Next(), sev = rng.Range(c.severityMin, c.severityMax), onset = rng.Range(c.onsetMin_s, c.onsetMax_s);
                    if (pick >= Math.Min(1, p) || Conflicts(db, chosen, c.injuryId)) continue;
                    chosen.Add(new ScenarioInjury { injuryId = c.injuryId, severity = Round2(sev), onsetTime_s = R(onset) });
                }
                if (chosen.Any(si => defs[si.injuryId].effects.Length > 0)) break;
            }
            if (!chosen.Any(si => defs[si.injuryId].effects.Length > 0))
            {
                var fallback = mech.injuries.Where(c => defs[c.injuryId].effects.Length > 0 && !Conflicts(db, chosen, c.injuryId))
                                            .OrderByDescending(c => c.probability).First();
                chosen.Add(new ScenarioInjury { injuryId = fallback.injuryId,
                    severity = Round2((fallback.severityMin + fallback.severityMax) / 2), onsetTime_s = fallback.onsetMin_s });
            }

            var ph = db.prehospital;
            double loss = PrehospitalLoss(chosen, defs, rescue, ph);
            var criteria = Criteria(db, chosen, defs);
            string description = FillTemplate(mech.descriptionTemplate, age, male, unrestrained, speed, fallHeight, rescue);
            if (loss >= ph.reportThreshold_mL) description += $" Präklinisch geschätzter Blutverlust ca. {loss.ToString(CultureInfo.InvariantCulture)} mL.";
            if (betaBlocker) description += " Dauermedikation: Betablocker.";

            return new ScenarioDefinition
            {
                id = $"zufall_{mech.id}_{seed}",
                title = $"Zufall: {mech.label}",
                description = description,
                mechanism = mech.label,
                patient = new PatientProfile
                {
                    sex = male ? "male" : "female", age = age, weight_kg = weight, height_cm = height,
                    betaBlocker = betaBlocker, initialBloodLoss_mL = loss
                },
                injuries = chosen.ToArray(),
                learningGoals = db.learningGoals,
                expectedCourse = "Verletzungen: " + string.Join(", ", chosen.Select(si => defs[si.injuryId].name)) + ".",
                defaultTimeScale = 1,
                assessment = criteria.ToArray()
            };
        }

        /// <summary>Präklinischer Blutverlust aus den Blutungsraten und der Rettungszeit.</summary>
        public static double PrehospitalLoss(IEnumerable<ScenarioInjury> chosen, IDictionary<string, InjuryDefinition> defs, double rescue_min, PrehospitalSettings ph)
        {
            double rate = 0;
            foreach (var si in chosen)
                foreach (var e in defs[si.injuryId].effects)
                    if (e.effectType == "Hemorrhage" && si.onsetTime_s <= 0) rate += e.baseRate_mL_min * si.severity;
            double loss = Math.Min(ph.maxLoss_mL, rate * rescue_min * ph.lossFactor);
            return R(loss / ph.roundTo_mL) * ph.roundTo_mL;
        }

        /// <summary>Bewertung: Basisziele + Vorlagen der gewählten Verletzungen, doppelte ids nur einmal.</summary>
        public static List<AssessmentCriterion> Criteria(MechanismDatabaseFile db, IEnumerable<ScenarioInjury> chosen, IDictionary<string, InjuryDefinition> defs)
        {
            var criteria = new List<AssessmentCriterion>();
            var ids = new HashSet<string>();
            foreach (var cr in db.baseCriteria.Concat(chosen.SelectMany(si => defs[si.injuryId].criteria ?? new AssessmentCriterion[0])))
                if (ids.Add(cr.id)) criteria.Add(cr);
            return criteria;
        }

        public static string FillTemplate(string template, int age, bool male, bool unrestrained, int speed, int fallHeight, int rescue) =>
            template
                .Replace("{age}", age.ToString(CultureInfo.InvariantCulture))
                .Replace("{r}", male ? "r" : "")
                .Replace("{in}", male ? "" : "in")
                .Replace("{driver}", male ? "Fahrer" : "Fahrerin")
                .Replace("{belt}", unrestrained ? "nicht angeschnallt" : "angeschnallt")
                .Replace("{speed}", speed.ToString(CultureInfo.InvariantCulture))
                .Replace("{height}", fallHeight.ToString(CultureInfo.InvariantCulture))
                .Replace("{rescue}", rescue.ToString(CultureInfo.InvariantCulture));

        public static bool Conflicts(MechanismDatabaseFile db, List<ScenarioInjury> chosen, string injuryId)
        {
            foreach (var g in db.exclusiveGroups)
                if (g.injuryIds.Contains(injuryId) && chosen.Any(si => g.injuryIds.Contains(si.injuryId)))
                    return true;
            return false;
        }

        public static double Round2(double v) => R(v * 100) / 100;
        // Kaufmännisch runden wie JS Math.round (C# Math.Round rundet sonst zur geraden Zahl).
        public static double R(double v) => Math.Floor(v + 0.5);
    }
}

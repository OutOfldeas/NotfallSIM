using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Core;
using NotfallSIM.Data;

namespace NotfallSIM.Crash
{
    // ---------- Datenmodell (injury_risk.json) ----------

    [Serializable]
    public class RiskCurve
    {
        public string type;   // "lognormal": Φ((ln x − mu)/sigma) · "loglogistic": 1/(1 + (x50/x)^shape)
        public double mu, sigma;
        public double x50, shape;
    }

    /// <summary>Ein Messwert am Ragdoll (z. B. HIC15 am Kopf) mit Risikokurve für eine AIS-Stufe.</summary>
    [Serializable]
    public class RiskMetric
    {
        public string id;          // z. B. "HIC15", "Thorax.a3ms", "Becken.F"
        public string label;
        public string unit;
        public string region;      // Körperregion wie in injuries.json (für Heatmap)
        public string ais;         // z. B. "AIS3+"
        public double limit;       // Grenzwert aus Regelwerken (0 = keiner), nur zur Anzeige
        public string limitSource;
        public RiskCurve curve;
        public SourceRef[] sources;
        public string evidenceStatus;
        public string reviewNotes;
    }

    /// <summary>Welcher Messwert mit welchem Anteil zu einer Verletzung der Datenbank führt.</summary>
    [Serializable]
    public class RiskInjuryLink
    {
        public string injuryId;
        public string metric;
        public double share;              // Anteil des AIS-Risikos, der auf diese Verletzung entfällt (didaktisch)
        public double severityMin, severityMax; // Schweregrad wächst mit dem Risiko des Messwerts
        public double onsetMin_s, onsetMax_s;
        public string conditionMetric;    // optional, z. B. "Abdomen.lateral" (−1 links … +1 rechts)
        public double conditionMin, conditionMax;
    }

    [Serializable]
    public class InjuryRiskDatabaseFile
    {
        public string version;
        public string note;
        public RiskMetric[] metrics;
        public RiskInjuryLink[] injuries;
    }

    [Serializable]
    public class CrashMeasurement
    {
        public string metric;
        public double value;
    }

    public class MetricRisk
    {
        public RiskMetric Metric;
        public double Value;
        public double Probability;
    }

    /// <summary>Eingaben aus der Unfall-Sandbox.</summary>
    public class CrashSetup
    {
        public string MechanismId;  // Schlüssel in mechanisms.json (Beschreibung, Rettungszeit)
        public bool Male = true;
        public int Age = 35;
        public double Weight_kg = 80, Height_cm = 180;
        public bool Unrestrained;
        public int Speed_kmh;
        public int FallHeight_m;
        public int Rescue_min = 30;
    }

    public class CrashResult
    {
        public ScenarioDefinition Scenario;
        public List<MetricRisk> Risks = new List<MetricRisk>();
        public Dictionary<string, double> InjuryProbability = new Dictionary<string, double>();
    }

    public static class InjuryRisk
    {
        public static double Probability(RiskCurve c, double x)
        {
            if (c.type == "lognormal")
                return x <= 0 ? 0 : Biomechanics.NormalCdf((Math.Log(x) - c.mu) / c.sigma);
            if (c.type == "loglogistic") // geht bei kleinen Werten gegen 0, anders als die logistische Kurve
                return x <= 0 ? 0 : 1 / (1 + Math.Pow(c.x50 / x, c.shape));
            throw new ArgumentException("Unbekannter Kurventyp " + c.type);
        }

        public static List<MetricRisk> Evaluate(InjuryRiskDatabaseFile db, IEnumerable<CrashMeasurement> measurements)
        {
            var values = measurements.ToDictionary(m => m.metric, m => m.value);
            return db.metrics.Where(m => values.ContainsKey(m.id))
                .Select(m => new MetricRisk { Metric = m, Value = values[m.id], Probability = Probability(m.curve, values[m.id]) })
                .ToList();
        }

        /// <summary>
        /// Aus Messwerten wird ein Szenario: Risiko je Messwert → Wahrscheinlichkeit je Verletzung
        /// (mehrere Messwerte kombiniert: 1 − Π(1 − p)) → Verletzungen würfeln (Ausschlussgruppen beachten)
        /// → Schweregrad wächst mit dem Risiko → Patient, präklinischer Verlust, Bewertungsziele wie beim Generator.
        /// Anders als beim Zufallsgenerator darf ein leichter Unfall auch ohne Verletzung enden.
        /// </summary>
        public static CrashResult BuildScenario(InjuryRiskDatabaseFile risk, MechanismDatabaseFile mechDb, IEnumerable<InjuryDefinition> injuryDefs,
                                                IList<CrashMeasurement> measurements, CrashSetup setup, uint seed)
        {
            var defs = injuryDefs.ToDictionary(d => d.id);
            var result = new CrashResult { Risks = Evaluate(risk, measurements) };
            var metricP = result.Risks.ToDictionary(r => r.Metric.id, r => r.Probability);
            var values = measurements.ToDictionary(m => m.metric, m => m.value);

            // Wahrscheinlichkeit und Schweregrad je Verletzung
            var severityFactor = new Dictionary<string, double>();
            var link = new Dictionary<string, RiskInjuryLink>();
            foreach (var l in risk.injuries)
            {
                if (!metricP.TryGetValue(l.metric, out double p)) continue;
                if (!string.IsNullOrEmpty(l.conditionMetric))
                {
                    if (!values.TryGetValue(l.conditionMetric, out double c) || c < l.conditionMin || c > l.conditionMax) continue;
                }
                double pi = Math.Min(1, l.share * p);
                double prev = result.InjuryProbability.TryGetValue(l.injuryId, out double q) ? q : 0;
                result.InjuryProbability[l.injuryId] = 1 - (1 - prev) * (1 - pi);
                if (!severityFactor.ContainsKey(l.injuryId) || p > severityFactor[l.injuryId]) { severityFactor[l.injuryId] = p; link[l.injuryId] = l; }
            }

            var rng = new SeededRandom(seed);
            var chosen = new List<ScenarioInjury>();
            // Feste Reihenfolge (wie in injury_risk.json) für reproduzierbare Fälle
            foreach (var id in risk.injuries.Select(l => l.injuryId).Distinct())
            {
                double pick = rng.Next(), onsetDraw = rng.Next();
                if (!result.InjuryProbability.TryGetValue(id, out double p) || pick >= p) continue;
                if (ScenarioGenerator.Conflicts(mechDb, chosen, id)) continue;
                var l = link[id];
                double sev = l.severityMin + (l.severityMax - l.severityMin) * severityFactor[id];
                chosen.Add(new ScenarioInjury
                {
                    injuryId = id, severity = ScenarioGenerator.Round2(sev),
                    onsetTime_s = ScenarioGenerator.R(l.onsetMin_s + (l.onsetMax_s - l.onsetMin_s) * onsetDraw)
                });
            }

            var mech = mechDb.mechanisms.FirstOrDefault(m => m.id == setup.MechanismId);
            double loss = ScenarioGenerator.PrehospitalLoss(chosen, defs, setup.Rescue_min, mechDb.prehospital);
            string description = mech != null
                ? ScenarioGenerator.FillTemplate(mech.descriptionTemplate, setup.Age, setup.Male, setup.Unrestrained, setup.Speed_kmh, setup.FallHeight_m, setup.Rescue_min)
                : "Unfall aus der Crash-Simulation.";
            if (loss >= mechDb.prehospital.reportThreshold_mL) description += $" Präklinisch geschätzter Blutverlust ca. {loss:0} mL.";
            if (chosen.Count == 0) description += " Kaum Beschwerden.";

            result.Scenario = new ScenarioDefinition
            {
                id = $"crash_{setup.MechanismId}_{seed}",
                title = "Crash: " + (mech?.label ?? setup.MechanismId),
                description = description,
                mechanism = (mech?.label ?? setup.MechanismId) + " (physikalisch simuliert)",
                patient = new PatientProfile
                {
                    sex = setup.Male ? "male" : "female", age = setup.Age, weight_kg = setup.Weight_kg, height_cm = setup.Height_cm,
                    betaBlocker = false, initialBloodLoss_mL = loss
                },
                injuries = chosen.ToArray(),
                learningGoals = mechDb.learningGoals,
                expectedCourse = chosen.Count == 0 ? "Keine relevanten Verletzungen."
                    : "Verletzungen: " + string.Join(", ", chosen.Select(si => defs[si.injuryId].name)) + ".",
                defaultTimeScale = 1,
                assessment = ScenarioGenerator.Criteria(mechDb, chosen, defs).ToArray()
            };
            return result;
        }
    }
}

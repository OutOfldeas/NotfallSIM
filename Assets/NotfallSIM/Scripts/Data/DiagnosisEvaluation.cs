using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Core;

namespace NotfallSIM.Data
{
    public enum DiagnosisOutcome { Correct, Missed, FalsePositive }

    public class DiagnosisResult
    {
        public string InjuryId;
        public string Name;
        public string Region;
        public DiagnosisOutcome Outcome;
        public double? Time_s;        // wann der (bis zum Ende bestehende) Verdacht gestellt wurde
        public double Onset_s;        // wann die Verletzung auftrat (0 = von Beginn an)
        public bool Examined;         // Region untersucht?
        public bool ImagingShown;     // Bildgebung gemacht, die diese Verletzung zeigt?
        public bool ClinicalOnly;     // Verletzung zeigt sich in keiner Bildgebung, nur klinisch
        public bool IsDifferential;   // falscher Verdacht auf eine Differenzialdiagnose ohne Physiologie
        public string[] Against = new string[0]; // was gegen einen falschen Verdacht sprach
    }

    /// <summary>
    /// Wertet die Verdachtsdiagnosen aus: Welche Verletzungen wurden erkannt, welche übersehen,
    /// welche Verdachte waren falsch? Zählt der Stand am Ende (zurückgenommene Verdachte zählen nicht).
    /// Gleiche Logik in web/engine.js (evaluateDiagnoses).
    /// </summary>
    public static class DiagnosisEvaluation
    {
        public const string SuspectPrefix = "diagnosis:";
        public const string RetractPrefix = "diagnosis-retracted:";

        public static List<DiagnosisResult> Evaluate(IEnumerable<ScenarioInjury> actual, IReadOnlyList<LogEntry> events,
                                                     Func<string, InjuryDefinition> getInjury, IEnumerable<ImagingModality> modalities,
                                                     DifferentialDatabaseFile differentials = null)
        {
            // Stand der Verdachte am Ende, mit dem Zeitpunkt, seit dem er besteht.
            var suspected = new Dictionary<string, double>();
            var order = new List<string>();
            foreach (var e in events)
            {
                if (e.Tag == null) continue;
                if (e.Tag.StartsWith(SuspectPrefix, StringComparison.Ordinal))
                {
                    string id = e.Tag.Substring(SuspectPrefix.Length);
                    if (!suspected.ContainsKey(id)) { suspected[id] = e.Time_s; order.Add(id); }
                }
                else if (e.Tag.StartsWith(RetractPrefix, StringComparison.Ordinal))
                {
                    string id = e.Tag.Substring(RetractPrefix.Length);
                    suspected.Remove(id); order.Remove(id);
                }
            }

            var tags = new HashSet<string>(events.Where(e => e.Tag != null).Select(e => e.Tag));
            var mods = modalities.ToList();
            var results = new List<DiagnosisResult>();
            var actualIds = new HashSet<string>();
            foreach (var si in actual)
            {
                if (!actualIds.Add(si.injuryId)) continue;
                var def = getInjury(si.injuryId);
                var shownBy = new HashSet<string>((def.imaging ?? new ImagingFinding[0]).Select(f => f.modality));
                double t;
                bool hit = suspected.TryGetValue(si.injuryId, out t);
                results.Add(new DiagnosisResult
                {
                    InjuryId = def.id, Name = def.name, Region = def.region,
                    Outcome = hit ? DiagnosisOutcome.Correct : DiagnosisOutcome.Missed,
                    Time_s = hit ? (double?)t : null,
                    Onset_s = si.onsetTime_s,
                    Examined = tags.Contains("exam:" + def.region),
                    ImagingShown = mods.Any(m => m.covers.Any(shownBy.Contains) && tags.Contains("result:Imaging:" + m.id)),
                    ClinicalOnly = shownBy.Count == 0
                });
            }
            foreach (var id in order.Where(x => !actualIds.Contains(x)))
            {
                var diff = differentials?.differentials?.FirstOrDefault(d => d.id == id);
                if (diff != null)
                {
                    // Spezifische Argumente zu vorliegenden Verletzungen zuerst, sonst das allgemeine.
                    var specific = diff.against.Where(x => !string.IsNullOrEmpty(x.injuryId) && actualIds.Contains(x.injuryId)).Select(x => x.text).ToArray();
                    results.Add(new DiagnosisResult { InjuryId = id, Name = diff.name, Region = diff.region, IsDifferential = true,
                        Outcome = DiagnosisOutcome.FalsePositive, Time_s = suspected[id],
                        Against = specific.Length > 0 ? specific : diff.against.Where(x => string.IsNullOrEmpty(x.injuryId)).Select(x => x.text).ToArray() });
                    continue;
                }
                var def = getInjury(id);
                // Falscher Verdacht auf eine modellierte Verletzung: deren Leitbefunde fehlten.
                var typical = (def.clinicalFindings ?? new string[0]).Concat((def.dynamicFindings ?? new DynamicFinding[0])
                    .Where(d => string.IsNullOrEmpty(d.requiresTag) && d.minLevel <= 0 && d.minLost_mL <= 0).Select(d => d.text)).Take(2).ToArray();
                results.Add(new DiagnosisResult { InjuryId = id, Name = def.name, Region = def.region,
                    Outcome = DiagnosisOutcome.FalsePositive, Time_s = suspected[id],
                    Against = typical.Length > 0 ? new[] { "Typische Befunde fehlten: " + string.Join("; ", typical) + "." } : new string[0] });
            }
            return results;
        }
    }
}

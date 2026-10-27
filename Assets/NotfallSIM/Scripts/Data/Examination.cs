using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Core;

namespace NotfallSIM.Data
{
    /// <summary>Was die Untersuchung über den aktuellen Zustand wissen muss.</summary>
    public class ExamContext
    {
        public double Time_s;
        public double BodyLossFraction;                  // aus VitalSigns
        public Func<string, double> Level;              // IPhysiologyEngine.InjuryLevel
        public Func<string, double> Lost_mL;            // IPhysiologyEngine.InjuryBloodLoss_mL
        public IReadOnlyList<LogEntry> Events;          // bisheriges Protokoll (Maßnahmen-Tags)
    }

    /// <summary>
    /// Körperliche Untersuchung einer Region (ohne den dynamischen Neurostatus, der aus der Physiologie kommt).
    /// Liegt in der Region eine bereits aufgetretene Verletzung, erscheinen ihre festen Befunde und die
    /// dynamischen Befunde, deren Bedingung gerade erfüllt ist; sonst der Normalbefund.
    /// So verrät eine Untersuchung nicht schon durch ihr Format, ob etwas vorliegt.
    /// Gleiche Logik in web/engine.js (examFindings).
    /// </summary>
    public static class Examination
    {
        public static List<string> Findings(string region, ScenarioDefinition scenario, Func<string, InjuryDefinition> getInjury,
                                            FindingsDatabaseFile normal, ExamContext ctx)
        {
            var findings = new List<string>();
            foreach (var si in scenario.injuries)
            {
                if (si.onsetTime_s > ctx.Time_s) continue;
                var def = getInjury(si.injuryId);
                if (def.region != region) continue;
                findings.AddRange(def.clinicalFindings ?? new string[0]);
                foreach (var d in def.dynamicFindings ?? new DynamicFinding[0])
                    if (Applies(d, def.id, ctx)) findings.Add(d.text);
            }
            if (findings.Count == 0 && normal != null)
            {
                var r = normal.regions.FirstOrDefault(x => x.region == region);
                if (r != null) findings.AddRange(r.normal);
            }
            return findings;
        }

        public static bool Applies(DynamicFinding d, string injuryId, ExamContext ctx)
        {
            double level = ctx.Level != null ? ctx.Level(injuryId) : 0;
            if (level < d.minLevel - 1e-9 || level > d.maxLevel + 1e-9) return false;
            if ((ctx.Lost_mL != null ? ctx.Lost_mL(injuryId) : 0) < d.minLost_mL) return false;
            if (ctx.BodyLossFraction < d.minBodyLoss || ctx.BodyLossFraction > d.maxBodyLoss) return false;
            if (!string.IsNullOrEmpty(d.requiresTag))
            {
                double? t = FirstTag(ctx.Events, d.requiresTag);
                if (t == null || ctx.Time_s - t.Value < d.requiresTagDelay_s) return false;
            }
            if (!string.IsNullOrEmpty(d.untilTag))
            {
                double? t = FirstTag(ctx.Events, d.untilTag);
                if (t != null && ctx.Time_s - t.Value >= d.untilTagDelay_s) return false;
            }
            return true;
        }

        static double? FirstTag(IReadOnlyList<LogEntry> events, string prefix)
        {
            if (events == null) return null;
            foreach (var e in events)
                if (e.Tag != null && e.Tag.StartsWith(prefix, StringComparison.Ordinal)) return e.Time_s;
            return null;
        }

        /// <summary>True, wenn in der Region eine aufgetretene Verletzung liegt (für das Protokoll „unauffällig“).</summary>
        public static bool HasInjury(string region, ScenarioDefinition scenario, double time_s, Func<string, InjuryDefinition> getInjury) =>
            scenario.injuries.Any(si => si.onsetTime_s <= time_s && getInjury(si.injuryId).region == region);
    }
}

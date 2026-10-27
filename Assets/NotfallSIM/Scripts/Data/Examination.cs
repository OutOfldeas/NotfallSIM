using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Data
{
    /// <summary>
    /// Körperliche Untersuchung einer Region (ohne den dynamischen Neurostatus, der aus der Physiologie kommt).
    /// Liegt in der Region eine bereits aufgetretene Verletzung, erscheinen deren Befunde, sonst der Normalbefund.
    /// So sieht jede Untersuchung gleich „vollständig“ aus und verrät nicht schon durch ihr Format, ob etwas vorliegt.
    /// Gleiche Logik in web/engine.js (Simulation.examine).
    /// </summary>
    public static class Examination
    {
        public static List<string> Findings(string region, ScenarioDefinition scenario, double time_s,
                                            Func<string, InjuryDefinition> getInjury, FindingsDatabaseFile normal)
        {
            var findings = scenario.injuries
                .Where(si => si.onsetTime_s <= time_s)
                .Select(si => getInjury(si.injuryId))
                .Where(def => def.region == region)
                .SelectMany(def => def.clinicalFindings ?? new string[0])
                .ToList();
            if (findings.Count == 0 && normal != null)
            {
                var r = normal.regions.FirstOrDefault(x => x.region == region);
                if (r != null) findings.AddRange(r.normal);
            }
            return findings;
        }

        /// <summary>True, wenn die Befunde pathologisch sind (für das Protokoll „unauffällig“).</summary>
        public static bool HasInjury(string region, ScenarioDefinition scenario, double time_s, Func<string, InjuryDefinition> getInjury) =>
            scenario.injuries.Any(si => si.onsetTime_s <= time_s && getInjury(si.injuryId).region == region);
    }
}

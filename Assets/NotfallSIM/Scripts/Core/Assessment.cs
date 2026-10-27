using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Data;

namespace NotfallSIM.Core
{
    public enum CriterionOutcome { Met, Late, Missed, Exceeded, NotApplicable }

    public class CriterionResult
    {
        public AssessmentCriterion Criterion;
        public CriterionOutcome Outcome;
        public double? Time_s;   // Zeitpunkt der ersten passenden Maßnahme (deadline)
        public int Count;       // Anzahl passender Ereignisse (maxCount)
        public double Deadline_s; // absolutes Zeitziel ab Szenariostart (deadline)
        public bool Passed => Outcome == CriterionOutcome.Met;
    }

    /// <summary>Wertet Bewertungskriterien gegen das Ereignisprotokoll aus. Reines C#, testbar.</summary>
    public static class Assessment
    {
        public static List<CriterionResult> Evaluate(IEnumerable<AssessmentCriterion> criteria, IReadOnlyList<LogEntry> events)
        {
            var results = new List<CriterionResult>();
            if (criteria == null) return results;

            foreach (var c in criteria)
            {
                var matching = events.Where(e => Matches(e.Tag, c.tags)).ToList();
                var r = new CriterionResult { Criterion = c, Count = matching.Count };

                if (c.type == "maxCount")
                {
                    r.Outcome = matching.Count <= c.limit ? CriterionOutcome.Met : CriterionOutcome.Exceeded;
                }
                else // "deadline"
                {
                    double start = 0;
                    if (!string.IsNullOrEmpty(c.relativeTo))
                    {
                        var refs = events.Where(e => Matches(e.Tag, new[] { c.relativeTo })).ToList();
                        if (refs.Count == 0) { r.Outcome = CriterionOutcome.NotApplicable; results.Add(r); continue; }
                        start = refs.Min(e => e.Time_s);
                    }
                    r.Deadline_s = start + c.limit;
                    // Bei relativen Zielen zählt nur, was ab dem Bezugsereignis passiert (z. B. Re-Evaluation).
                    var relevant = matching.Where(e => e.Time_s >= start).ToList();
                    r.Time_s = relevant.Count > 0 ? relevant.Min(e => e.Time_s) : (double?)null;
                    r.Outcome = r.Time_s == null ? CriterionOutcome.Missed
                              : r.Time_s <= r.Deadline_s ? CriterionOutcome.Met
                              : CriterionOutcome.Late;
                }
                results.Add(r);
            }
            return results;
        }

        static bool Matches(string tag, string[] prefixes) =>
            tag != null && prefixes != null && prefixes.Any(p => tag.StartsWith(p, StringComparison.Ordinal));
    }
}

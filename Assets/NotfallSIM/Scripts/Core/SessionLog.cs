using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NotfallSIM.Core
{
    public class LogEntry
    {
        public double Time_s;
        public string Message;
        public string Tag;   // maschinenlesbar, Grundlage der Bewertung
    }

    /// <summary>
    /// Sammelt Vitalwerte und Ereignisse eines Durchlaufs und erzeugt CSV und Debriefing.
    /// Reines C# ohne Unity-Abhängigkeit, damit es außerhalb von Unity testbar ist.
    /// </summary>
    public class SessionLog
    {
        public readonly List<VitalSigns> Samples = new List<VitalSigns>();
        public readonly List<LogEntry> Events = new List<LogEntry>();

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public void AddSample(VitalSigns v) => Samples.Add(v);
        public void AddEvent(double t, string msg, string tag = null) =>
            Events.Add(new LogEntry { Time_s = t, Message = msg, Tag = tag });

        /// <summary>Semikolon-getrennt, ganze Zahlen — öffnet sich in deutschem Excel direkt korrekt.</summary>
        public string ToVitalsCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Zeit_s;HF;RRsys;RRdia;MAD;AF;SpO2;etCO2;Blutvolumen_mL;Blutverlust_Prozent;Stillstand");
            foreach (var v in Samples)
            {
                sb.AppendLine(string.Join(";", new[]
                {
                    F(v.SimTime_s), F(v.HeartRate_bpm), F(v.SystolicBP_mmHg), F(v.DiastolicBP_mmHg),
                    F(v.MeanArterialPressure_mmHg), F(v.RespirationRate_bpm), F(v.SpO2_percent),
                    F(v.EtCO2_mmHg), F(v.BloodVolume_mL), F(v.BloodLossFraction * 100),
                    v.CardiacArrest ? "1" : "0"
                }));
            }
            return sb.ToString();
        }

        public string ToEventsCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Zeit_s;Ereignis;Tag");
            foreach (var e in Events)
                sb.AppendLine($"{F(e.Time_s)};{e.Message.Replace(";", ",")};{e.Tag}");
            return sb.ToString();
        }

        /// <summary>Kurzes Debriefing mit TextMeshPro-Rich-Text. Mit Kriterien zusätzlich eine Bewertung.</summary>
        public string BuildDebrief(string scenarioTitle, IEnumerable<NotfallSIM.Data.AssessmentCriterion> criteria = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Debriefing: {scenarioTitle}</b>");
            if (Samples.Count == 0)
            {
                sb.AppendLine("Keine Daten aufgezeichnet.");
                return sb.ToString();
            }

            var alive = Samples.Where(s => !s.CardiacArrest).ToList();
            var last = Samples[Samples.Count - 1];
            double maxLoss = Samples.Max(s => s.BloodLossFraction);

            sb.AppendLine($"Dauer: {Clock(last.SimTime_s)}");
            sb.AppendLine(last.CardiacArrest
                ? "Ausgang: <b>Kreislaufstillstand</b>"
                : $"Ausgang: Patient lebt, HF {F(last.HeartRate_bpm)}, RR {F(last.SystolicBP_mmHg)}/{F(last.DiastolicBP_mmHg)}");
            sb.AppendLine($"Maximaler Blutverlust: {F(maxLoss * 100)} % (ATLS-Schockklasse {ShockClass(maxLoss)})");
            if (alive.Count > 0)
                sb.AppendLine($"Max. HF: {F(alive.Max(s => s.HeartRate_bpm))} · Min. RRsys: {F(alive.Min(s => s.SystolicBP_mmHg))} mmHg" +
                              $" · Min. SpO2: {F(alive.Min(s => s.SpO2_percent))} %");

            var results = Assessment.Evaluate(criteria, Events).Where(r => r.Outcome != CriterionOutcome.NotApplicable).ToList();
            if (results.Count > 0)
            {
                int passed = results.Count(r => r.Passed);
                sb.AppendLine();
                sb.AppendLine($"<b>Bewertung: {passed} von {results.Count} Zielen erreicht</b>");
                foreach (var r in results) sb.AppendLine(FormatResult(r));
                if (results.Any(r => r.Criterion.critical && !r.Passed))
                    sb.AppendLine("<color=#FF6060>Mindestens ein kritisches Ziel wurde verfehlt.</color>");
            }

            sb.AppendLine();
            sb.AppendLine("<b>Zeitleiste</b>");
            foreach (var e in Events)
                sb.AppendLine($"{Clock(e.Time_s)}  {e.Message}");
            return sb.ToString();
        }

        static string FormatResult(CriterionResult r)
        {
            var c = r.Criterion;
            string star = c.critical ? " (kritisch)" : "";
            switch (r.Outcome)
            {
                case CriterionOutcome.Met:
                    return c.type == "maxCount"
                        ? $"<color=#5CDB6E>erfüllt</color>  {c.description}{star} ({r.Count}x)"
                        : $"<color=#5CDB6E>erfüllt</color>  {c.description}{star} bei {Clock(r.Time_s.Value)} (Ziel {Clock(r.Deadline_s)})";
                case CriterionOutcome.Late:
                    return $"<color=#F2C14E>verspätet</color>  {c.description}{star} bei {Clock(r.Time_s.Value)} (Ziel {Clock(r.Deadline_s)})";
                case CriterionOutcome.Exceeded:
                    return $"<color=#FF6060>überschritten</color>  {c.description}{star} ({r.Count}x, max. {F(c.limit)}x)";
                default:
                    return $"<color=#FF6060>fehlt</color>  {c.description}{star} (Ziel {Clock(r.Deadline_s)})";
            }
        }

        /// <summary>Schockklasse nach relativem Blutverlust (ATLS: &lt;15 / 15–30 / 30–40 / &gt;40 %).</summary>
        public static string ShockClass(double lossFraction) =>
            lossFraction < 0.15 ? "I" : lossFraction < 0.30 ? "II" : lossFraction < 0.40 ? "III" : "IV";

        static string F(double x) => Math.Round(x).ToString("0", Inv);
        static string Clock(double s) { int t = (int)s; return $"{t / 60:00}:{t % 60:00}"; }
    }
}

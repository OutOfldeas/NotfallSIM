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
        public void AddEvent(double t, string msg) => Events.Add(new LogEntry { Time_s = t, Message = msg });

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
            sb.AppendLine("Zeit_s;Ereignis");
            foreach (var e in Events)
                sb.AppendLine($"{F(e.Time_s)};{e.Message.Replace(";", ",")}");
            return sb.ToString();
        }

        /// <summary>Kurzes Debriefing mit TextMeshPro-Rich-Text (&lt;b&gt;).</summary>
        public string BuildDebrief(string scenarioTitle)
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
                sb.AppendLine($"Max. HF: {F(alive.Max(s => s.HeartRate_bpm))} · Min. RRsys: {F(alive.Min(s => s.SystolicBP_mmHg))} mmHg");

            sb.AppendLine();
            sb.AppendLine("<b>Zeitleiste</b>");
            foreach (var e in Events)
                sb.AppendLine($"{Clock(e.Time_s)}  {e.Message}");
            return sb.ToString();
        }

        /// <summary>Schockklasse nach relativem Blutverlust (ATLS: &lt;15 / 15–30 / 30–40 / &gt;40 %).</summary>
        public static string ShockClass(double lossFraction) =>
            lossFraction < 0.15 ? "I" : lossFraction < 0.30 ? "II" : lossFraction < 0.40 ? "III" : "IV";

        static string F(double x) => Math.Round(x).ToString("0", Inv);
        static string Clock(double s) { int t = (int)s; return $"{t / 60:00}:{t % 60:00}"; }
    }
}

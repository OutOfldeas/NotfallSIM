using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Core
{
    /// <summary>Alarmpriorität wie bei Patientenmonitoren (IEC 60601-1-8): hoch rot, mittel gelb, niedrig türkis.</summary>
    public enum AlarmPriority { Low = 1, Medium = 2, High = 3 }

    public class MonitorAlarm
    {
        public string Parameter;   // "HF", "SpO2", "NIBP", "AF", "etCO2", "Temp"
        public string Text;
        public AlarmPriority Priority;
        public override string ToString() => $"{Priority}: {Text}";
    }

    /// <summary>Alarmgrenzen (typische Voreinstellungen für Erwachsene).</summary>
    public class AlarmLimits
    {
        public double HrLow = 50, HrHigh = 120, HrExtremeLow = 40, HrExtremeHigh = 150;
        public double SysLow = 90, SysHigh = 160, SysExtremeLow = 70, MapLow = 65;
        public double Spo2Low = 90, Spo2ExtremeLow = 85;
        public double RrLow = 8, RrHigh = 30, Apnea = 4;
        public double EtCo2Low = 25, EtCo2High = 50;
        public double TempLow = 35, TempHigh = 38.5;
    }

    /// <summary>
    /// Nicht-invasive Blutdruckmessung (NIBP): misst nicht kontinuierlich, sondern im eingestellten Intervall oder
    /// auf Knopfdruck. Eine oszillometrische Messung dauert etwa 25 s (Annahme); der Wert gilt für den Zeitpunkt ihres
    /// Endes. Bei Stillstand oder systolisch unter 40 mmHg schlägt sie fehl.
    /// </summary>
    public class NibpCycle
    {
        public const double Measure_s = 25;
        public static readonly double[] Intervals_s = { 60, 150, 300, 0 };   // 0 = nur manuell

        public double Interval_s = 150;
        public bool Measuring { get; private set; }
        public bool HasReading => MeasuredAt_s >= 0;
        public bool Failed { get; private set; }
        public double Sys { get; private set; }
        public double Dia { get; private set; }
        public double Map { get; private set; }
        public double MeasuredAt_s { get; private set; } = -1;
        double _start, _next;

        public void Update(VitalSigns v)
        {
            double t = v.SimTime_s;
            if (!Measuring && Interval_s > 0 && t >= _next) StartNow(t);
            if (Measuring && t >= _start + Measure_s)
            {
                Measuring = false;
                MeasuredAt_s = t;
                Failed = v.CardiacArrest || v.SystolicBP_mmHg < 40;
                if (!Failed) { Sys = v.SystolicBP_mmHg; Dia = v.DiastolicBP_mmHg; Map = v.MeanArterialPressure_mmHg; }
                _next = Interval_s > 0 ? _start + Math.Max(Interval_s, Measure_s) : double.MaxValue;
            }
        }

        /// <summary>Messung sofort starten (Taste „NIBP“).</summary>
        public void StartNow(double t)
        {
            if (Measuring) return;
            Measuring = true;
            _start = t;
        }

        /// <summary>Nächstes Intervall der Liste wählen; ein laufender Zyklus richtet sich danach.</summary>
        public void CycleInterval(double t)
        {
            int i = Array.IndexOf(Intervals_s, Interval_s);
            Interval_s = Intervals_s[(i + 1) % Intervals_s.Length];
            _next = Interval_s > 0 ? Math.Max(t, (HasReading ? MeasuredAt_s - Measure_s : t) + Interval_s) : double.MaxValue;
        }

        public static string IntervalLabel(double s) => s <= 0 ? "manuell" : s < 120 ? $"{s / 60:0} min" : $"{s / 60:0.#} min".Replace('.', ',');
    }

    /// <summary>Was ein Patientenmonitor aus den Vitalwerten ableitet: Alarme, Signalqualität, Perfusionsindex.</summary>
    public static class PatientMonitorLogic
    {
        /// <summary>Unter diesem systolischen Druck findet das Pulsoxymeter kein verwertbares Signal mehr (Annahme).</summary>
        public const double PulseOxSignalSys = 50;

        public static bool SpO2Valid(VitalSigns v) => !v.CardiacArrest && v.SystolicBP_mmHg >= PulseOxSignalSys;

        /// <summary>Perfusionsindex in % (Annahme: normal 2–5, im Schock &lt; 1; aus Pulsamplitude und Vasokonstriktion).</summary>
        public static double PerfusionIndex(VitalSigns v)
        {
            if (!SpO2Valid(v)) return 0;
            double amplitude = Math.Max(0, Math.Min(1, (v.SystolicBP_mmHg - 40) / 80));
            double vasoconstriction = Math.Max(0.15, 1 - 2 * v.BloodLossFraction);
            return Math.Round(4 * amplitude * vasoconstriction, 1);
        }

        public static List<MonitorAlarm> Evaluate(VitalSigns v, NibpCycle nibp, AlarmLimits l)
        {
            var a = new List<MonitorAlarm>();
            Action<string, string, AlarmPriority> add = (p, t, pr) => a.Add(new MonitorAlarm { Parameter = p, Text = t, Priority = pr });
            if (v.CardiacArrest)
            {
                add("HF", "ASYSTOLIE", AlarmPriority.High);
                return a;
            }
            double hr = Math.Round(v.HeartRate_bpm);
            if (hr < l.HrExtremeLow) add("HF", $"BRADYKARDIE {hr:0}", AlarmPriority.High);
            else if (hr > l.HrExtremeHigh) add("HF", $"TACHYKARDIE {hr:0}", AlarmPriority.High);
            else if (hr < l.HrLow) add("HF", $"HF {hr:0} < {l.HrLow:0}", AlarmPriority.Medium);
            else if (hr > l.HrHigh) add("HF", $"HF {hr:0} > {l.HrHigh:0}", AlarmPriority.Medium);

            if (!SpO2Valid(v)) add("SpO2", "SpO2 kein Pulssignal", AlarmPriority.Medium);
            else
            {
                double s = Math.Round(v.SpO2_percent);
                if (s < l.Spo2ExtremeLow) add("SpO2", $"DESATURATION {s:0}", AlarmPriority.High);
                else if (s < l.Spo2Low) add("SpO2", $"SpO2 {s:0} < {l.Spo2Low:0}", AlarmPriority.Medium);
            }

            double rr = Math.Round(v.RespirationRate_bpm);
            if (!v.Intubated && rr < l.Apnea) add("AF", "APNOE", AlarmPriority.High);
            else if (rr < l.RrLow) add("AF", $"AF {rr:0} < {l.RrLow:0}", AlarmPriority.Medium);
            else if (rr > l.RrHigh) add("AF", $"AF {rr:0} > {l.RrHigh:0}", AlarmPriority.Medium);

            double co2 = Math.Round(v.EtCO2_mmHg);
            if (co2 < l.EtCo2Low) add("etCO2", $"etCO2 {co2:0} < {l.EtCo2Low:0}", AlarmPriority.Medium);
            else if (co2 > l.EtCo2High) add("etCO2", $"etCO2 {co2:0} > {l.EtCo2High:0}", AlarmPriority.Medium);

            if (nibp != null && nibp.HasReading)
            {
                if (nibp.Failed) add("NIBP", "NIBP Messung fehlgeschlagen", AlarmPriority.Medium);
                else
                {
                    double sys = Math.Round(nibp.Sys);
                    if (sys < l.SysExtremeLow) add("NIBP", $"NIBP SYS {sys:0}", AlarmPriority.High);
                    else if (sys < l.SysLow) add("NIBP", $"NIBP sys {sys:0} < {l.SysLow:0}", AlarmPriority.Medium);
                    else if (sys > l.SysHigh) add("NIBP", $"NIBP sys {sys:0} > {l.SysHigh:0}", AlarmPriority.Medium);
                    else if (nibp.Map < l.MapLow) add("NIBP", $"NIBP MAD {nibp.Map:0} < {l.MapLow:0}", AlarmPriority.Medium);
                }
            }

            if (v.Temperature_C > 0 && v.Temperature_C < l.TempLow) add("Temp", $"Temp {v.Temperature_C:0.0} < {l.TempLow:0}".Replace('.', ','), AlarmPriority.Low);
            else if (v.Temperature_C > l.TempHigh) add("Temp", $"Temp {v.Temperature_C:0.0} > {l.TempHigh:0.0}".Replace('.', ','), AlarmPriority.Low);
            return a.OrderByDescending(x => x.Priority).ToList();
        }
    }
}

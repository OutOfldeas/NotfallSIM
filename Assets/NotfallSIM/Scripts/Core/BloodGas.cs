using System;
using System.Globalization;

namespace NotfallSIM.Core
{
    /// <summary>
    /// Leitet die übrigen BGA-Werte aus dem physiologischen Zustand ab. Reines C#, testbar.
    /// Lehrbuchphysiologie, keine Messdaten: HCO3 ≈ 24 + BE; Kompensation nach der Winters-Formel
    /// (erwarteter pCO2 = 1,5 × HCO3 + 8); pH nach Henderson-Hasselbalch; pO2 aus der Sauerstoffbindungskurve.
    /// </summary>
    public static class BloodGas
    {
        // SpO2 (%) -> pO2 (mmHg), Sauerstoffbindungskurve unter Normalbedingungen (Näherung)
        static readonly double[] SatPoints = { 50, 60, 70, 80, 85, 90, 94, 96, 97, 98 };
        static readonly double[] Po2Values = { 27, 32, 37, 45, 50, 60, 75, 85, 95, 105 };

        public static double Hco3(double baseExcess) => Math.Max(4, 24 + baseExcess);

        /// <param name="airway">0..1 Hypoventilation bei ungeschütztem Atemweg</param>
        /// <param name="tension">0..1 Spannungspneumothorax</param>
        public static double PaCO2(double hco3, bool intubated, double airway, double tension)
        {
            if (intubated) return 38; // kontrollierte Normoventilation
            double compensated = Math.Min(40, 1.5 * hco3 + 8);
            return compensated + 15 * airway + 6 * tension;
        }

        public static double PH(double hco3, double paco2) => 6.1 + Math.Log10(hco3 / (0.03 * paco2));

        public static double PaO2(double spo2, bool oxygen, bool intubated)
        {
            if (spo2 >= 96 && intubated) return 250;
            if (spo2 >= 96 && oxygen) return 140;
            return ShockTables.Interp(spo2, SatPoints, Po2Values);
        }

        /// <summary>Azidose verschiebt Kalium nach extrazellulär (Faustregel ca. 0,6 mmol/L je 0,1 pH).</summary>
        public static double Potassium(double ph) => 4.0 + Math.Max(0, 7.40 - ph) * 6;

        /// <summary>Citrat aus Blutprodukten bindet ionisiertes Calcium (didaktische Setzung: −0,06 mmol/L je 300 mL).</summary>
        public static double IonizedCalcium(double transfused_mL) => Math.Max(0.75, 1.20 - 0.0002 * transfused_mL);

        /// <summary>Stresshyperglykämie, steigt mit der Schockschwere (didaktische Setzung).</summary>
        public static double Glucose(double lossFraction) => 110 + 150 * lossFraction;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string D(double x, string fmt) => x.ToString(fmt, Inv).Replace('.', ',');

        /// <summary>Befundtext wie auf dem Ausdruck eines Blutgasanalysators.</summary>
        public static string Report(VitalSigns v)
        {
            return $"pH {D(v.pH, "0.00")} · pCO2 {D(v.PaCO2, "0")} mmHg · pO2 {D(v.PaO2, "0")} mmHg · SaO2 {D(v.SpO2_percent, "0")} % · " +
                   $"HCO3 {D(v.HCO3, "0.0")} mmol/L · BE {(v.BaseExcess >= 0 ? "+" : "")}{D(v.BaseExcess, "0.0")} mmol/L · Laktat {D(v.Lactate, "0.0")} mmol/L · " +
                   $"Hb {D(v.Hb, "0.0")} g/dL · K {D(v.Potassium, "0.0")} mmol/L · Ca++ {D(v.IonizedCalcium, "0.00")} mmol/L · Glukose {D(v.Glucose, "0")} mg/dL";
        }
    }
}

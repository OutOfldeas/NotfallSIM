using System;

namespace NotfallSIM.Core
{
    /// <summary>
    /// Körperbau aus Gewicht, Größe, Alter und Geschlecht: BMI, Körperfett, Magergewicht, Körperoberfläche,
    /// Blutvolumen und die Folgen für Dosierung, Auskühlung und Entlastungspunktion. Reines C#, im Browser-Prototyp
    /// gleich (web/engine.js, BodyComposition). Quellen in docs/LITERATUR.md („Körperbau und Fettgewebe“).
    /// </summary>
    public static class BodyComposition
    {
        public static double Bmi(double weight_kg, double height_cm)
        {
            double h = height_cm > 100 ? height_cm / 100 : 1.8;
            return weight_kg / (h * h);
        }

        /// <summary>Körperfettanteil (%) nach Deurenberg 1991: 1,2·BMI + 0,23·Alter − 10,8·(Mann) − 5,4.</summary>
        public static double BodyFat_percent(double bmi, int age, bool male) =>
            Math.Max(3, Math.Min(65, 1.2 * bmi + 0.23 * age - 10.8 * (male ? 1 : 0) - 5.4));

        /// <summary>Magergewicht (kg) nach Janmahasatian 2005: Mann 9270·G / (6680 + 216·BMI), Frau 9270·G / (8780 + 244·BMI).</summary>
        public static double LeanBodyWeight_kg(double weight_kg, double bmi, bool male) =>
            male ? 9270 * weight_kg / (6680 + 216 * bmi) : 9270 * weight_kg / (8780 + 244 * bmi);

        /// <summary>Idealgewicht (kg) nach Devine: 50 bzw. 45,5 kg + 0,91 kg je cm über 152,4 cm.</summary>
        public static double IdealBodyWeight_kg(double height_cm, bool male) => Math.Max(30, (male ? 50 : 45.5) + 0.91 * (height_cm - 152.4));

        /// <summary>Körperoberfläche (m²) nach Mosteller 1987: √(Größe[cm] · Gewicht[kg] / 3600).</summary>
        public static double BodySurfaceArea_m2(double weight_kg, double height_cm) => Math.Sqrt(Math.Max(1, height_cm) * Math.Max(1, weight_kg) / 3600);

        /// <summary>
        /// Blutvolumen (mL): 70 bzw. 65 mL/kg, ab BMI 22 nach Lemmens 2006 mit √(22/BMI) verringert —
        /// Fettgewebe ist kaum durchblutet, das Blutvolumen wächst nicht mit dem Gesamtgewicht.
        /// </summary>
        public static double BloodVolume_mL(double weight_kg, double bmi, bool male) =>
            weight_kg * (male ? 70 : 65) * Math.Sqrt(22 / Math.Max(22, bmi));

        /// <summary>Bezugspatient für die Auskühlung (75 kg, 180 cm).</summary>
        public const double RefWeight_kg = 75, RefHeight_cm = 180;

        /// <summary>
        /// Faktor auf die Auskühlung: Wärmeabgabe wächst mit der Oberfläche, der Wärmespeicher mit der Masse
        /// (Abkühlrate ∝ Oberfläche / Masse, bezogen auf 75 kg / 180 cm). Die zusätzliche Isolation durch Unterhautfett
        /// ist nicht eingerechnet (keine belastbare Größe für Luftexposition gefunden).
        /// </summary>
        public static double CoolingFactor(double weight_kg, double height_cm)
        {
            double w = weight_kg > 0 ? weight_kg : RefWeight_kg, h = height_cm > 100 ? height_cm : RefHeight_cm;
            return BodySurfaceArea_m2(w, h) / BodySurfaceArea_m2(RefWeight_kg, RefHeight_cm) * (RefWeight_kg / w);
        }

        /// <summary>
        /// Wirkfaktor für Medikamente, die nach Magergewicht dosiert werden (Fentanyl, Propofol, Ketamin), wenn je kg
        /// Gesamtgewicht gegeben wird: Verhältnis Gesamt-/Magergewicht bezogen auf BMI 22 (gleiches Geschlecht).
        /// Bei BMI 22 genau 1; ein adipöser Patient bekommt bei gleicher Dosis je kg relativ mehr.
        /// </summary>
        public static double LeanDoseFactor(double bmi, bool male) =>
            male ? (6680 + 216 * bmi) / (6680 + 216 * 22.0) : (8780 + 244 * bmi) / (8780 + 244 * 22.0);

        /// <summary>
        /// Brustwanddicke (mm, Mittelwert) nach BMI aus CT-Messungen bei Traumapatienten (Inaba 2012, Mittel je
        /// BMI-Quartil, lineare Anpassung): 2. ICR Medioklavikularlinie −7,8 + 1,926·BMI, 5. ICR vordere Axillarlinie
        /// −29,4 + 2,231·BMI. Untergrenze 15 mm.
        /// </summary>
        public static double ChestWall_mm(double bmi, bool anteriorAxillary) =>
            Math.Max(15, anteriorAxillary ? -29.35 + 2.231 * bmi : -7.8 + 1.926 * bmi);

        /// <summary>Streuung der Brustwanddicke bei gleichem BMI (mm): Annahme aus der Gesamtstreuung bei Inaba 2012 (SD ≈ 14 mm) abzüglich der BMI-Anteile.</summary>
        public const double ChestWallSd_mm = 9;

        /// <summary>Wahrscheinlichkeit, dass eine Kanüle der Länge <paramref name="length_cm"/> die Pleura erreicht.</summary>
        public static double NeedleReachProbability(double bmi, bool anteriorAxillary, double length_cm) =>
            NormalCdf((length_cm * 10 - ChestWall_mm(bmi, anteriorAxillary)) / ChestWallSd_mm);

        /// <summary>Standardnormalverteilung (Abramowitz/Stegun 7.1.26, Fehler &lt; 1,5e-7).</summary>
        public static double NormalCdf(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2), t = 1 / (1 + 0.3275911 * z);
            double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
            return x >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
        }

        /// <summary>Wählbare Körperbauten (BMI); 0 = wie im Fall.</summary>
        public static readonly (string label, double bmi)[] BodyTypes =
        {
            ("wie im Fall", 0), ("schlank (BMI 20)", 20), ("normal (BMI 24)", 24), ("übergewichtig (BMI 28)", 28),
            ("adipös (BMI 34)", 34), ("stark adipös (BMI 42)", 42)
        };

        /// <summary>Gewicht (kg, gerundet) für einen BMI bei gegebener Größe.</summary>
        public static double WeightForBmi(double bmi, double height_cm)
        {
            double h = height_cm > 100 ? height_cm / 100 : 1.8;
            return Math.Round(bmi * h * h, MidpointRounding.AwayFromZero);
        }

        /// <summary>Kurzbeschreibung für Briefing und Dosisdialog, z. B. "BMI 34 (Adipositas Grad I), Körperfett ca. 31 %".</summary>
        public static string Describe(double weight_kg, double height_cm, int age, bool male)
        {
            double bmi = Bmi(weight_kg, height_cm);
            string cls = bmi < 18.5 ? "Untergewicht" : bmi < 25 ? "Normalgewicht" : bmi < 30 ? "Übergewicht"
                       : bmi < 35 ? "Adipositas Grad I" : bmi < 40 ? "Adipositas Grad II" : "Adipositas Grad III";
            return $"BMI {Math.Round(bmi, MidpointRounding.AwayFromZero)} ({cls}), Körperfett ca. {Math.Round(BodyFat_percent(bmi, age, male), MidpointRounding.AwayFromZero)} %";
        }
    }
}

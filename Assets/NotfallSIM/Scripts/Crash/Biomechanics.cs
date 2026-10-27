using System;
using System.Collections.Generic;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Verletzungskriterien aus der Crashtest-Biomechanik, gerechnet auf gleichmäßig abgetasteten Messreihen.
    /// Keine Unity-Abhängigkeit, damit alles außerhalb von Unity testbar ist.
    /// </summary>
    public static class Biomechanics
    {
        /// <summary>
        /// Head Injury Criterion: max über alle Zeitfenster [t1, t2] mit t2 − t1 ≤ window von
        /// (t2 − t1) · (mittlere Beschleunigung in g)^2,5. HIC15: window = 0,015 s.
        /// </summary>
        public static double Hic(IList<double> accel_g, double dt_s, double window_s = 0.015)
        {
            int n = accel_g.Count;
            if (n < 2) return 0;
            var cum = new double[n + 1];
            for (int i = 0; i < n; i++) cum[i + 1] = cum[i] + Math.Abs(accel_g[i]) * dt_s;
            int w = Math.Max(1, (int)Math.Round(window_s / dt_s));
            double best = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j <= Math.Min(n, i + w); j++)
                {
                    double T = (j - i) * dt_s;
                    double avg = (cum[j] - cum[i]) / T;
                    double hic = T * Math.Pow(avg, 2.5);
                    if (hic > best) best = hic;
                }
            return best;
        }

        /// <summary>
        /// „3-ms-Clip“: höchster Wert, der für mindestens 3 ms ununterbrochen überschritten wurde
        /// (Maximum der Fenster-Minima). Robust gegen einzelne Spitzen; für Beschleunigungen und Kräfte.
        /// FMVSS 208 verwendet die kumulative Variante — die zusammenhängende ist eine gängige Näherung.
        /// </summary>
        public static double Clip(IList<double> values, double dt_s, double duration_s = 0.003)
        {
            int n = values.Count;
            int k = Math.Max(1, (int)Math.Round(duration_s / dt_s));
            if (n < k) return 0;
            double best = 0;
            for (int i = 0; i + k <= n; i++)
            {
                double min = double.MaxValue;
                for (int j = i; j < i + k; j++) min = Math.Min(min, Math.Abs(values[j]));
                if (min > best) best = min;
            }
            return best;
        }

        /// <summary>Gleitender Mittelwert (einfacher Tiefpass gegen numerisches Rauschen der Physik-Engine).</summary>
        public static List<double> MovingAverage(IList<double> values, int samples)
        {
            var result = new List<double>(values.Count);
            double sum = 0;
            samples = Math.Max(1, samples);
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                if (i >= samples) sum -= values[i - samples];
                result.Add(sum / Math.Min(i + 1, samples));
            }
            return result;
        }

        /// <summary>Standardnormalverteilung Φ(x) (Abramowitz/Stegun 7.1.26, Fehler &lt; 1,5·10⁻⁷).</summary>
        public static double NormalCdf(double x)
        {
            double z = Math.Abs(x) / Math.Sqrt(2);
            double t = 1 / (1 + 0.3275911 * z);
            double erf = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-z * z);
            return x >= 0 ? 0.5 * (1 + erf) : 0.5 * (1 - erf);
        }
    }
}

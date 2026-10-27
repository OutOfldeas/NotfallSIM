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

        /// <summary>
        /// Kanalfilter nach SAE J211 / ISO 6487 (CFC): zweipoliger Butterworth-Tiefpass, einmal vorwärts und
        /// einmal rückwärts angewandt (vierpolig, ohne Phasenverschiebung). −3 dB bei etwa 1,65 × CFC.
        /// Übliche Klassen: Kopf 1000, Halskräfte 1000, Halsmomente 600, Brustwirbelsäule 180, Deflexion 600, Femur 600.
        /// Die Abtastrate sollte mindestens 10 × CFC betragen.
        /// </summary>
        public static List<double> Cfc(IList<double> x, double dt_s, double cfc)
        {
            double wd = 2 * Math.PI * cfc * 2.0775;
            double wa = Math.Sin(wd * dt_s / 2) / Math.Cos(wd * dt_s / 2);
            double d = 1 + Math.Sqrt(2) * wa + wa * wa;
            double a0 = wa * wa / d, a1 = 2 * a0, a2 = a0;
            double b1 = -2 * (wa * wa - 1) / d, b2 = (-1 + Math.Sqrt(2) * wa - wa * wa) / d;
            Func<IList<double>, double[]> pass = v =>
            {
                int n = v.Count;
                var y = new double[n];
                if (n == 0) return y;
                double x1 = v[0], x2 = v[0], y1 = v[0], y2 = v[0]; // Startwerte: Signal konstant fortgesetzt
                for (int i = 0; i < n; i++)
                {
                    y[i] = a0 * v[i] + a1 * x1 + a2 * x2 + b1 * y1 + b2 * y2;
                    x2 = x1; x1 = v[i]; y2 = y1; y1 = y[i];
                }
                return y;
            };
            var forward = pass(x);
            Array.Reverse(forward);
            var back = pass(forward);
            Array.Reverse(back);
            return new List<double>(back);
        }

        /// <summary>
        /// Viscous Criterion (Lau &amp; Viano 1986): max über t von V(t) · C(t), mit V = Eindrückgeschwindigkeit (m/s)
        /// und C = Eindrückung / Brustkorbtiefe. VC = 1,0 m/s entspricht etwa 25 % Risiko für AIS4+ am Thorax.
        /// </summary>
        public static double ViscousCriterion(IList<double> deflection_m, double dt_s, double chestDepth_m, double scale = 1.0)
        {
            double best = 0;
            for (int i = 1; i < deflection_m.Count - 1; i++)
            {
                double v = (deflection_m[i + 1] - deflection_m[i - 1]) / (2 * dt_s);
                double c = deflection_m[i] / chestDepth_m;
                if (v > 0 && c > 0) best = Math.Max(best, scale * v * c);
            }
            return best;
        }

        /// <summary>
        /// Neck Injury Criterion (FMVSS 208): Nij = Fz/Fzc + My/Myc, Maximum über alle Zeitpunkte und die vier
        /// Kombinationen (Zug/Druck × Flexion/Extension). Fz &gt; 0 = Zug, My &gt; 0 = Flexion.
        /// Intercepts Hybrid III 50. Perzentil Mann: Zug 6806 N, Druck 6160 N, Flexion 310 Nm, Extension 135 Nm.
        /// </summary>
        public static double Nij(IList<double> fz_N, IList<double> my_Nm,
                                 double tension = 6806, double compression = 6160, double flexion = 310, double extension = 135)
        {
            double best = 0;
            int n = Math.Min(fz_N.Count, my_Nm.Count);
            for (int i = 0; i < n; i++)
            {
                double f = fz_N[i] >= 0 ? fz_N[i] / tension : -fz_N[i] / compression;
                double m = my_Nm[i] >= 0 ? my_Nm[i] / flexion : -my_Nm[i] / extension;
                best = Math.Max(best, f + m);
            }
            return best;
        }

        public static double MaxAbs(IList<double> v)
        {
            double best = 0;
            foreach (var x in v) best = Math.Max(best, Math.Abs(x));
            return best;
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

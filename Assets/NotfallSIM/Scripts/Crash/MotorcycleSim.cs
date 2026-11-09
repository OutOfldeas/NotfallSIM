using System;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Motorrad in der Seitenansicht (vor z, hoch y, Nicken θ, positiv = Nase nach unten). Reines C#.
    /// Kräfte: Schwerkraft, Reifen am Boden (Feder-Dämpfer je Rad), Gabel/Vorderrad gegen eine Anprallfläche
    /// (Kraft-Weg-Kennlinie, plastisch). Die Gabelkraft greift auf Höhe der Vorderradachse unter dem Schwerpunkt an —
    /// das Heck hebt ab und das Motorrad kippt über das Vorderrad. Koordinaten wie im Rig: Fahrzeugursprung am Boden,
    /// Räder bei z = ± Radstand/2 (+ <see cref="RootOffsetZ"/>), Schwerpunkt bei (comForward, comHeight).
    /// </summary>
    public class MotorcycleSim
    {
        readonly MotorcycleModel _m;
        public readonly double RootOffsetZ;
        // Schwerpunktlage (Welt) und Geschwindigkeiten
        public double Z, Y, Theta, Vz, Vy, W;
        public double Penetration, MaxCrush, ForkForce, MaxForkForce;
        double _fMaxCurve;
        const double G = 9.81;

        /// <summary>Lage eines Punktes im Fahrzeug (relativ zum Ursprung) in der Welt.</summary>
        public void ToWorld(double ly, double lz, out double wy, out double wz)
        {
            double ry = ly - _m.comHeight_m, rz = lz - (_m.comForward_m + RootOffsetZ);
            double c = Math.Cos(Theta), s = Math.Sin(Theta);
            wy = Y + ry * c - rz * s;
            wz = Z + ry * s + rz * c;
        }

        /// <summary>Ursprung des Fahrzeugs (für MovePosition) aus der Schwerpunktlage.</summary>
        public void Root(out double y, out double z) => ToWorld(0, 0, out y, out z);

        public MotorcycleSim(MotorcycleModel m, double rootZ, double speed_m_s, double rootOffsetZ)
        {
            _m = m; RootOffsetZ = rootOffsetZ;
            Z = rootZ + m.comForward_m + rootOffsetZ; Y = m.comHeight_m; Vz = speed_m_s;
        }

        double FrontAxleZLocal => RootOffsetZ + _m.wheelbase_m / 2;
        double RearAxleZLocal => RootOffsetZ - _m.wheelbase_m / 2;

        /// <summary>
        /// Ein Schritt. <paramref name="obstacleZ"/>: Lage der Anprallfläche entlang der Fahrtrichtung (∞ = keine),
        /// <paramref name="obstacleVz"/>: ihre Geschwindigkeit in z (für die Dämpfung nicht genutzt; Fläche gilt als unendlich schwer).
        /// </summary>
        public void Step(double dt, double obstacleZ)
        {
            double Fz = 0, Fy = -_m.mass_kg * G, tau = 0;
            double r = _m.wheelRadius_m;
            // Reifen am Boden
            foreach (var az in new[] { FrontAxleZLocal, RearAxleZLocal })
            {
                ToWorld(r, az, out double cy, out double cz);
                double pen = r - cy;   // Aufstandspunkt unter dem Boden
                if (pen <= 0) continue;
                double rz = cz - Z;
                double vyPoint = Vy - W * rz;   // Punktgeschwindigkeit in y (Nicken um x, Nase nach unten positiv)
                double N = Math.Max(0, _m.tyreStiffness_N_m * pen - _m.tyreDamping_Ns_m * vyPoint);
                Fy += N;
                tau += -rz * N;     // τ = ry·Fz − rz·Fy
            }
            // Gabel gegen die Anprallfläche (vorderster Punkt des Vorderrads)
            ForkForce = 0;
            if (!double.IsInfinity(obstacleZ))
            {
                ToWorld(r, FrontAxleZLocal, out double ay, out double azW);
                double p = azW + r - obstacleZ;
                Penetration = p;
                if (p > 0)
                {
                    // Gabel (plastisch) und Gegner (elastisch-plastisch, linear) in Reihe: beide tragen dieselbe Kraft
                    double lo = 0, hi = 1e6;
                    for (int it = 0; it < 40; it++)
                    {
                        double mid = 0.5 * (lo + hi);
                        if (ForkDeflection(mid) + CarDeflection(mid) < p) lo = mid; else hi = mid;
                    }
                    double F = 0.5 * (lo + hi);
                    double cf = ForkDeflection(F), cc = CarDeflection(F);
                    if (cf > MaxCrush) { MaxCrush = cf; _fMaxCurve = F; }
                    if (cc > CarCrush) { CarCrush = cc; _fMaxCar = F; }
                    ForkForce = F;
                    MaxForkForce = Math.Max(MaxForkForce, F);
                    double ry = ay - Y;
                    Fz -= F;
                    tau += ry * (-F);
                }
            }
            Vz += Fz / _m.mass_kg * dt; Vy += Fy / _m.mass_kg * dt; W += tau / _m.pitchInertia_kgm2 * dt;
            Z += Vz * dt; Y += Vy * dt; Theta += W * dt;
            // Heck legt sich an den Gegner bzw. kippt in Wirklichkeit seitlich weg (in der Seitenansicht nicht abbildbar):
            // Nicken höchstens 75°, kein Überschlag über das Vorderrad
            if (Theta > MaxPitch_rad) { Theta = MaxPitch_rad; if (W > 0) W = -0.2 * W; }
        }

        /// <summary>Größtes Nicken (rad) — Annahme, siehe oben.</summary>
        public const double MaxPitch_rad = 75 * Math.PI / 180;

        /// <summary>Steifigkeit des Gegners an der Anprallstelle (N/m); 0 = starr (Wand).</summary>
        public double CarStiffness;
        /// <summary>Bleibende Eindrückung des Gegners (m).</summary>
        public double CarCrush;
        double _fMaxCar;

        double ForkDeflection(double F)
        {
            if (F <= _fMaxCurve && MaxCrush > 0)
                return MaxCrush - (_fMaxCurve - F) / (Math.Max(1, _m.unloadFactor) * _fMaxCurve / MaxCrush);
            return _m.fork.Deflection(F);
        }

        double CarDeflection(double F)
        {
            if (CarStiffness <= 0) return 0;
            if (F <= _fMaxCar && CarCrush > 0) return CarCrush - (_fMaxCar - F) / (CarStiffness * Math.Max(1, _m.unloadFactor));
            return F / CarStiffness;
        }

        /// <summary>Sichtbare Stauchung der Gabel (m): bleibende Verformung bzw. aktuelle, wenn größer.</summary>
        public double VisibleCrush => Math.Max(MaxCrush * (1 - 1 / Math.Max(1, _m.unloadFactor)), Math.Max(0, Penetration));
    }
}

using System;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Gelenkwinkel des Ragdolls gegenüber der Ruhelage und Überschreiten der weichen Anschläge (ohne Unity, getestet).
    /// Die Anschläge bremsen nur (<see cref="RagdollBuilder"/>); wie weit sie überschritten werden, wertet
    /// <c>injury_risk.json</c> als Bandverletzung bzw. Luxation aus (Knie: Biegung, Überstreckung; Schulter: Überdehnung).
    /// Gelenkrahmen wie beim ConfigurableJoint: X = Beugeachse (axis), Y = zweite Achse (secondaryAxis), Z = X × Y.
    /// </summary>
    public static class JointStops
    {
        /// <summary>Drehvektor (Grad) einer Einheitsquaternion: Achse × Winkel, Winkel in (−180°, 180°].</summary>
        public static void RotationVector(double qx, double qy, double qz, double qw, out double rx, out double ry, out double rz)
        {
            if (qw < 0) { qx = -qx; qy = -qy; qz = -qz; qw = -qw; }   // kürzerer Weg
            double s = Math.Sqrt(qx * qx + qy * qy + qz * qz);
            if (s < 1e-9) { rx = ry = rz = 0; return; }
            double angle = 2 * Math.Atan2(s, qw) * 180 / Math.PI;
            rx = qx / s * angle; ry = qy / s * angle; rz = qz / s * angle;
        }

        /// <summary>Drehvektor zerlegt in Beugung (um axis), Drehung um secondaryAxis und um die dritte Achse (Grad).</summary>
        public static void Components(double rx, double ry, double rz, double[] axis, double[] secondary,
                                      out double twist, out double swingY, out double swingZ)
        {
            var a = Unit(axis);
            // secondaryAxis senkrecht zu axis machen (wie Unity), dann dritte Achse
            double d = secondary[0] * a[0] + secondary[1] * a[1] + secondary[2] * a[2];
            var b = Unit(new[] { secondary[0] - d * a[0], secondary[1] - d * a[1], secondary[2] - d * a[2] });
            var c = new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
            // Vorzeichen wie die Grenzen des ConfigurableJoint: Unity misst die Beugung entgegen dem Drehvektor (in Unity gesehen:
            // sitzender Fahrer −108° nach dieser Rechnung, innerhalb der Grenze +140°; stehendes Knie −5…+3°)
            twist = -(rx * a[0] + ry * a[1] + rz * a[2]);
            swingY = rx * b[0] + ry * b[1] + rz * b[2];
            swingZ = rx * c[0] + ry * c[1] + rz * c[2];
        }

        /// <summary>Größte Überschreitung eines Anschlags (Grad, ≥ 0). low/high: Beugung nach −/+ (Beträge).</summary>
        public static double Exceedance(double twist, double swingY, double swingZ, double low, double high, double limY, double limZ) =>
            Math.Max(0, Math.Max(Math.Max(twist - high, -low - twist), Math.Max(Math.Abs(swingY) - limY, Math.Abs(swingZ) - limZ)));

        /// <summary>Überstreckung über die gestreckte Stellung hinaus (Grad, ≥ 0). twist: Beugung seit dem Anlegen des Gelenks,
        /// extensionSign: Richtung der Streckung (−1/+1), flexAtRest: Beugung beim Anlegen (sitzend ≈ 90°, stehend 0).</summary>
        public static double Hyperextension(double twist, int extensionSign, double flexAtRest) =>
            Math.Max(0, extensionSign * twist - flexAtRest);

        static double[] Unit(double[] v)
        {
            double n = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            return n < 1e-12 ? new double[] { 1, 0, 0 } : new[] { v[0] / n, v[1] / n, v[2] / n };
        }
    }
}

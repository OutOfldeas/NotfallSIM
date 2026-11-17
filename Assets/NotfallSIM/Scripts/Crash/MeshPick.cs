using System;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Strahl gegen ein Dreiecksnetz (Möller–Trumbore), ohne Unity: zum Zielen auf die sichtbare Haut statt auf die
    /// Kapseln des Ragdolls. Die Kapseln liegen teils innerhalb, teils außerhalb der Haut — ein Klick auf die Haut traf
    /// daneben den Boden oder ein anderes Körperteil.
    /// </summary>
    public static class MeshPick
    {
        /// <summary>Achsparalleler Rahmen um die Punkte (xyz hintereinander).</summary>
        public static void Bounds(float[] v, out float[] min, out float[] max)
        {
            min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i + 2 < v.Length; i += 3)
                for (int k = 0; k < 3; k++) { min[k] = Math.Min(min[k], v[i + k]); max[k] = Math.Max(max[k], v[i + k]); }
        }

        /// <summary>Trifft der Strahl den Rahmen (mit Rand)?</summary>
        public static bool HitsBox(float[] min, float[] max, float margin, double ox, double oy, double oz, double dx, double dy, double dz)
        {
            double t0 = 0, t1 = double.MaxValue;
            double[] o = { ox, oy, oz }, d = { dx, dy, dz };
            for (int k = 0; k < 3; k++)
            {
                double lo = min[k] - margin, hi = max[k] + margin;
                if (Math.Abs(d[k]) < 1e-12) { if (o[k] < lo || o[k] > hi) return false; continue; }
                double a = (lo - o[k]) / d[k], b = (hi - o[k]) / d[k];
                if (a > b) { var s = a; a = b; b = s; }
                t0 = Math.Max(t0, a); t1 = Math.Min(t1, b);
                if (t0 > t1) return false;
            }
            return true;
        }

        /// <summary>
        /// Nächster Schnittpunkt des Strahls (Ursprung o, Richtung d, nicht normiert nötig) mit dem Netz. Liefert den
        /// Strahlparameter t (Punkt = o + t·d), das Dreieck und die Flächennormale zum Strahlursprung hin.
        /// </summary>
        public static bool Raycast(float[] v, int[] tris, double ox, double oy, double oz, double dx, double dy, double dz,
                                   out double t, out int tri, out double nx, out double ny, out double nz)
        {
            t = double.MaxValue; tri = -1; nx = ny = nz = 0;
            const double eps = 1e-12;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i] * 3, b = tris[i + 1] * 3, c = tris[i + 2] * 3;
                double e1x = v[b] - v[a], e1y = v[b + 1] - v[a + 1], e1z = v[b + 2] - v[a + 2];
                double e2x = v[c] - v[a], e2y = v[c + 1] - v[a + 1], e2z = v[c + 2] - v[a + 2];
                double px = dy * e2z - dz * e2y, py = dz * e2x - dx * e2z, pz = dx * e2y - dy * e2x;
                double det = e1x * px + e1y * py + e1z * pz;
                if (det > -eps && det < eps) continue;          // parallel
                double inv = 1 / det;
                double sx = ox - v[a], sy = oy - v[a + 1], sz = oz - v[a + 2];
                double u = (sx * px + sy * py + sz * pz) * inv;
                if (u < 0 || u > 1) continue;
                double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
                double w = (dx * qx + dy * qy + dz * qz) * inv;
                if (w < 0 || u + w > 1) continue;
                double tt = (e2x * qx + e2y * qy + e2z * qz) * inv;
                if (tt <= 1e-6 || tt >= t) continue;
                t = tt; tri = i / 3;
                // Normale (Kreuzprodukt der Kanten), zum Ursprung hin gedreht
                double cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
                double len = Math.Sqrt(cx * cx + cy * cy + cz * cz);
                if (len > 0) { cx /= len; cy /= len; cz /= len; }
                if (cx * dx + cy * dy + cz * dz > 0) { cx = -cx; cy = -cy; cz = -cz; }
                nx = cx; ny = cy; nz = cz;
            }
            return tri >= 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Anatomy
{
    [Serializable]
    public class RivuletParams
    {
        public double muLow_mPas, shearLow_per_s, muHigh_mPas, shearHigh_per_s; // scherverdünnend (Potenzgesetz zwischen zwei Messpunkten)
        public string viscositySourceId;
        public double volumePerRivulet_mL;   // je so viel Verlust ein weiteres Rinnsal
        public double widthMin_mm, widthMax_mm, widthAtRate_mL_min;
        public double maxLength_m;
        public string note;
    }

    /// <summary>Ein Rinnsal: Pfad über die Haut (Hautvertices) und daraus ein Band zum Zeichnen.</summary>
    public class Rivulet
    {
        public int[] Path;                // Hautvertices von der Quelle abwärts
        public bool Drips;                // endet an einer nach unten zeigenden Stelle: tropft ab
        public float Length;              // Modelleinheiten
        public float[] EdgeSlope;         // Gefälle (Sinus) je Pfadkante
        public float[] EdgeEnd;           // Bogenlänge am Ende jeder Kante, 0..1

        /// <summary>Gefälle an der Stelle along (0..1) des Pfads.</summary>
        public float SlopeAt(float along)
        {
            if (EdgeSlope == null || EdgeSlope.Length == 0) return 0;
            for (int i = 0; i < EdgeEnd.Length; i++) if (along <= EdgeEnd[i]) return EdgeSlope[i];
            return EdgeSlope[EdgeSlope.Length - 1];
        }
        public float[] Pos, Nrm;          // Band: 2 Vertices je Pfadpunkt (links/rechts)
        public float[] Across, Along;     // quer 0 links … 1 rechts; Bogenlänge 0..1
        public int[] Src;                 // Hautvertex je Bandvertex (Gewichte, Blendshapes)
        public int[] Triangles;
    }

    /// <summary>
    /// Blut auf der Haut (ohne Unity, getestet): Rinnsale folgen dem steilsten Abstieg entlang der Schwerkraft über das
    /// Hautnetz (mit leichter, reproduzierbarer Streuung), bis die Haut nach unten zeigt (Abtropfen) oder kein Weg mehr
    /// abwärts führt. Breite aus dem Blutfluss.
    /// </summary>
    public static class BloodFlow
    {
        /// <summary>Mindestgefälle (Sinus der Neigung) für weiteres Fließen; darunter sammelt sich das Blut (Annahme ≈ 5°).</summary>
        public const double MinSlope = 0.08;

        /// <param name="gravity">Schwerkraft je Hautvertex in Modellkoordinaten (Einheitsvektor), z. B. je Segment aus der Lagerung.</param>
        public static Rivulet Trace(float[] pos, float[] normals, List<int>[] adj, int start, Func<int, float[]> gravity,
                                    float maxLength, int seed, bool[] allowed = null)
        {
            var rng = new Random(seed);
            var path = new List<int> { start };
            var slopes = new List<float>(); var ends = new List<float>();
            var visited = new HashSet<int> { start };
            float length = 0;
            bool drips = false;
            int v = start;
            while (length < maxLength)
            {
                var g = gravity(v);
                // nach unten zeigende Haut: hier löst sich der Tropfen
                if (normals[3 * v] * g[0] + normals[3 * v + 1] * g[1] + normals[3 * v + 2] * g[2] > 0.6f) { drips = true; break; }
                int best = -1; double bestScore = 0;
                foreach (int w in adj[v])
                {
                    if (visited.Contains(w) || (allowed != null && !allowed[w])) continue;
                    float dx = pos[3 * w] - pos[3 * v], dy = pos[3 * w + 1] - pos[3 * v + 1], dz = pos[3 * w + 2] - pos[3 * v + 2];
                    float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (len <= 0) continue;
                    double down = (dx * g[0] + dy * g[1] + dz * g[2]) / len;   // Gefälle entlang der Kante
                    if (down <= MinSlope) continue;                                // fast eben: Blut bleibt stehen
                    double score = down * (0.75 + 0.5 * rng.NextDouble());     // leichtes Mäandern
                    if (score > bestScore) { bestScore = score; best = w; }
                }
                if (best < 0) break;                                           // Mulde: Blut sammelt sich
                {
                    float dx = pos[3 * best] - pos[3 * v], dy = pos[3 * best + 1] - pos[3 * v + 1], dz = pos[3 * best + 2] - pos[3 * v + 2];
                    float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    slopes.Add((dx * g[0] + dy * g[1] + dz * g[2]) / len);
                }
                length += Dist(pos, v, best);
                ends.Add(length);
                visited.Add(best); path.Add(best); v = best;
            }
            return new Rivulet { Path = path.ToArray(), Drips = drips, Length = length, EdgeSlope = slopes.ToArray(),
                                 EdgeEnd = ends.Select(x => length > 0 ? x / length : 1).ToArray() };
        }

        /// <summary>Viskosität (Pa·s) bei der Scherrate: Potenzgesetz durch zwei Messpunkte (scherverdünnendes Blut), außerhalb begrenzt.</summary>
        public static double Viscosity_Pa_s(RivuletParams p, double shear_per_s)
        {
            double n = Math.Log(p.muHigh_mPas / p.muLow_mPas) / Math.Log(p.shearHigh_per_s / p.shearLow_per_s);
            double mu = p.muLow_mPas * Math.Pow(Math.Max(1e-9, shear_per_s) / p.shearLow_per_s, n);
            return Math.Max(p.muHigh_mPas, Math.Min(p.muLow_mPas, mu)) / 1000;
        }

        /// <summary>
        /// Mittlere Geschwindigkeit eines Blutfilms auf geneigter Haut (laminarer Rieselfilm nach Nußelt):
        /// h = (3 μ q / (ρ g sinθ))^(1/3), u = q / h, q = Q / Breite; Wandscherrate ρ g sinθ h / μ, μ daraus iterativ.
        /// Die Filmdicke ist durch die Dicke einer ruhenden Lache begrenzt (dort bleibt das Blut stehen).
        /// </summary>
        public static double FilmSpeed_m_s(RivuletParams p, double flow_m3_s, double width_m, double sinSlope, double density, double maxThickness_m, out double thickness_m)
        {
            thickness_m = 0;
            if (flow_m3_s <= 0 || width_m <= 0) return 0;
            double q = flow_m3_s / width_m, s = Math.Max(0, sinSlope);
            if (s < 1e-4) { thickness_m = maxThickness_m; return 0; }
            double mu = p.muHigh_mPas / 1000, h = 0;
            for (int i = 0; i < 30; i++)
            {
                h = Math.Pow(3 * mu * q / (density * 9.81 * s), 1.0 / 3);
                mu = Viscosity_Pa_s(p, density * 9.81 * s * h / mu);
            }
            h = Math.Min(h, maxThickness_m);
            thickness_m = h;
            return h > 0 ? q / h : 0;
        }

        /// <summary>Breite eines Rinnsals (mm) aus dem Blutfluss: wächst mit der dritten Wurzel des Flusses.</summary>
        public static double Width_mm(RivuletParams p, double rate_mL_min)
        {
            if (rate_mL_min <= 0 || p.widthAtRate_mL_min <= 0) return p.widthMin_mm;
            double w = p.widthMax_mm * Math.Pow(rate_mL_min / p.widthAtRate_mL_min, 1.0 / 3);
            return Math.Max(p.widthMin_mm, Math.Min(p.widthMax_mm, w));
        }

        /// <summary>Band entlang des Pfads (geglättet), Breite in Modelleinheiten, sich zur Front hin verjüngend.</summary>
        public static void BuildStrip(Rivulet r, float[] pos, float[] normals, float width)
        {
            var p = r.Path;
            int n = p.Length;
            if (n < 2) { r.Pos = new float[0]; r.Nrm = new float[0]; r.Across = new float[0]; r.Along = new float[0]; r.Src = new int[0]; r.Triangles = new int[0]; return; }
            // Pfad glätten: Chaikin (zwei Durchgänge) — weiche Kurven statt Kanten des Hautnetzes; Quelle je Punkt = nächster Pfadvertex
            var pts = new List<float[]>(); var srcOf = new List<int>();
            for (int i = 0; i < n; i++) { pts.Add(new[] { pos[3 * p[i]], pos[3 * p[i] + 1], pos[3 * p[i] + 2] }); srcOf.Add(p[i]); }
            for (int it = 0; it < 2; it++)
            {
                var np = new List<float[]> { pts[0] }; var ns = new List<int> { srcOf[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    var a = pts[i]; var b = pts[i + 1];
                    np.Add(new[] { 0.75f * a[0] + 0.25f * b[0], 0.75f * a[1] + 0.25f * b[1], 0.75f * a[2] + 0.25f * b[2] }); ns.Add(srcOf[i]);
                    np.Add(new[] { 0.25f * a[0] + 0.75f * b[0], 0.25f * a[1] + 0.75f * b[1], 0.25f * a[2] + 0.75f * b[2] }); ns.Add(srcOf[i + 1]);
                }
                np.Add(pts[pts.Count - 1]); ns.Add(srcOf[srcOf.Count - 1]);
                pts = np; srcOf = ns;
            }
            n = pts.Count;
            var c = new float[3 * n];
            for (int i = 0; i < n; i++) for (int j = 0; j < 3; j++) c[3 * i + j] = pts[i][j];
            p = srcOf.ToArray();
            var arc = new float[n];
            for (int i = 1; i < n; i++)
            {
                float dx = c[3 * i] - c[3 * i - 3], dy = c[3 * i + 1] - c[3 * i - 2], dz = c[3 * i + 2] - c[3 * i - 1];
                arc[i] = arc[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            float total = Math.Max(1e-6f, arc[n - 1]);
            r.Pos = new float[6 * n]; r.Nrm = new float[6 * n]; r.Across = new float[2 * n]; r.Along = new float[2 * n]; r.Src = new int[2 * n];
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                var t = new[] { c[3 * b] - c[3 * a], c[3 * b + 1] - c[3 * a + 1], c[3 * b + 2] - c[3 * a + 2] };
                var nv = new[] { normals[3 * p[i]], normals[3 * p[i] + 1], normals[3 * p[i] + 2] };
                var side = Cross(nv, t);
                float sl = (float)Math.Sqrt(side[0] * side[0] + side[1] * side[1] + side[2] * side[2]);
                if (sl > 0) for (int j = 0; j < 3; j++) side[j] /= sl;
                float along = arc[i] / total;
                float half = width / 2 * (1 - 0.6f * along * along) * (i == 0 ? 0.6f : 1);   // an der Quelle schmaler, zur Front spitz
                for (int k = 0; k < 2; k++)
                {
                    float sgn = k == 0 ? -1 : 1;
                    for (int j = 0; j < 3; j++)
                    {
                        r.Pos[6 * i + 3 * k + j] = c[3 * i + j] + sgn * half * side[j];
                        r.Nrm[6 * i + 3 * k + j] = nv[j];
                    }
                    r.Across[2 * i + k] = k;      // quer 0 links … 1 rechts; der Shader formt daraus das Profil (Mitte voll)
                    r.Along[2 * i + k] = along;
                    r.Src[2 * i + k] = p[i];
                }
            }
            var tris = new List<int>();
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = 2 * i, r0 = 2 * i + 1, l1 = 2 * i + 2, r1 = 2 * i + 3;
                tris.AddRange(new[] { l0, l1, r0, r0, l1, r1 });
            }
            r.Triangles = tris.ToArray();
            // Ausrichtung: Dreiecke zur Hautnormalen hin drehen
            for (int t = 0; t < r.Triangles.Length; t += 3)
            {
                int i0 = r.Triangles[t], i1 = r.Triangles[t + 1], i2 = r.Triangles[t + 2];
                var e1 = new[] { r.Pos[3 * i1] - r.Pos[3 * i0], r.Pos[3 * i1 + 1] - r.Pos[3 * i0 + 1], r.Pos[3 * i1 + 2] - r.Pos[3 * i0 + 2] };
                var e2 = new[] { r.Pos[3 * i2] - r.Pos[3 * i0], r.Pos[3 * i2 + 1] - r.Pos[3 * i0 + 1], r.Pos[3 * i2 + 2] - r.Pos[3 * i0 + 2] };
                var fn = Cross(e1, e2);
                if (fn[0] * r.Nrm[3 * i0] + fn[1] * r.Nrm[3 * i0 + 1] + fn[2] * r.Nrm[3 * i0 + 2] < 0) { r.Triangles[t + 1] = i2; r.Triangles[t + 2] = i1; }
            }
        }

        static float Dist(float[] p, int a, int b)
        {
            float dx = p[3 * a] - p[3 * b], dy = p[3 * a + 1] - p[3 * b + 1], dz = p[3 * a + 2] - p[3 * b + 2];
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static float[] Cross(float[] a, float[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }
}

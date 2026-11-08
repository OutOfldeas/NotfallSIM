using System;
using System.Collections.Generic;

namespace NotfallSIM.Crash
{
    /// <summary>Teile eines erzeugten Fahrzeugmodells (je ein Material).</summary>
    public enum ShapePart { Lack = 0, Glas = 1, Reifen = 2, Scheinwerfer = 3, Ruecklicht = 4, Metall = 5, Sitz = 6 }

    /// <summary>Dreiecksnetz ohne Unity-Typen: Ecken (x, y, z hintereinander), Dreiecke je Teil, Ecken-Merkmale für die Verformung.</summary>
    public class ShapeMesh
    {
        public readonly List<float> V = new List<float>();
        public readonly List<int>[] Tri = new List<int>[7];
        /// <summary>Je Ecke: starr (Räder, Lampenfassungen der Hinterachse …) — wird nicht verformt.</summary>
        public readonly List<bool> Rigid = new List<bool>();
        /// <summary>Je Ecke: Anteil an der Gabel (Motorrad, 0–1) — staucht beim Frontaufprall mit.</summary>
        public readonly List<float> Fork = new List<float>();
        public float[] Rest;
        public ShapeMesh() { for (int i = 0; i < Tri.Length; i++) Tri[i] = new List<int>(); }
        public int Count => V.Count / 3;

        public int Add(float x, float y, float z, bool rigid = false, float fork = 0)
        {
            V.Add(x); V.Add(y); V.Add(z); Rigid.Add(rigid); Fork.Add(fork);
            return Count - 1;
        }
        public void Quad(ShapePart p, int a, int b, int c, int d) { var t = Tri[(int)p]; t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
        public void Triangle(ShapePart p, int a, int b, int c) { var t = Tri[(int)p]; t.Add(a); t.Add(b); t.Add(c); }
        public void Freeze() { Rest = V.ToArray(); }

        /// <summary>Quader (Mitte, Größe), achsparallel.</summary>
        public void Box(ShapePart p, float cx, float cy, float cz, float sx, float sy, float sz, bool rigid = false, float fork = 0)
        {
            float hx = sx / 2, hy = sy / 2, hz = sz / 2;
            int[] v = new int[8];
            for (int i = 0; i < 8; i++)
                v[i] = Add(cx + ((i & 1) != 0 ? hx : -hx), cy + ((i & 2) != 0 ? hy : -hy), cz + ((i & 4) != 0 ? hz : -hz), rigid, fork);
            // je Fläche eigene Ecken wären für harte Kanten nötig; weiche Normalen genügen hier
            Quad(p, v[0], v[2], v[3], v[1]); Quad(p, v[4], v[5], v[7], v[6]);
            Quad(p, v[0], v[1], v[5], v[4]); Quad(p, v[2], v[6], v[7], v[3]);
            Quad(p, v[0], v[4], v[6], v[2]); Quad(p, v[1], v[3], v[7], v[5]);
        }

        /// <summary>Zylinder von a nach b mit Radius r (n Segmente), mit Deckeln.</summary>
        public void Cylinder(ShapePart p, float ax, float ay, float az, float bx, float by, float bz, float r, int n = 16, bool rigid = false, float forkA = 0, float forkB = 0)
        {
            float dx = bx - ax, dy = by - ay, dz = bz - az, len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            dx /= len; dy /= len; dz /= len;
            // zwei Senkrechte zur Achse
            float ux, uy, uz;
            if (Math.Abs(dy) < 0.9f) { ux = dz; uy = 0; uz = -dx; } else { ux = 1; uy = 0; uz = 0; ux -= dx * dx; uy -= dx * dy; uz -= dx * dz; }
            float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            float wx = dy * uz - dz * uy, wy = dz * ux - dx * uz, wz = dx * uy - dy * ux;
            int[] ra = new int[n], rb = new int[n];
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                float c = (float)Math.Cos(a) * r, s = (float)Math.Sin(a) * r;
                ra[i] = Add(ax + c * ux + s * wx, ay + c * uy + s * wy, az + c * uz + s * wz, rigid, forkA);
                rb[i] = Add(bx + c * ux + s * wx, by + c * uy + s * wy, bz + c * uz + s * wz, rigid, forkB);
            }
            int ca = Add(ax, ay, az, rigid, forkA), cb = Add(bx, by, bz, rigid, forkB);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Quad(p, ra[i], ra[j], rb[j], rb[i]);
                Triangle(p, ca, ra[j], ra[i]);
                Triangle(p, cb, rb[i], rb[j]);
            }
        }
    }

    /// <summary>
    /// Umriss eines PKW in Fahrzeugkoordinaten (x rechts, y oben, z vorn; Mitte der Länge bei z = 0, Boden y = 0).
    /// Oberkante als Punkte (z, y) von hinten nach vorn; dazwischen linear.
    /// </summary>
    public class CarProfile
    {
        public float Length = 4.6f, Width = 1.8f, Wheelbase = 2.75f, WheelRadius = 0.32f, Sill = 0.25f, Belt = 0.95f;
        /// <summary>Oberkante (z, y), hinten → vorn.</summary>
        public List<float[]> Top = new List<float[]>();
        /// <summary>Glasflächen: Heckscheibe [deckZ, roofRearZ], Seitenfenster [deckZ, cowlZ], Frontscheibe [roofFrontZ, cowlZ].</summary>
        public float DeckZ, RoofRearZ, RoofFrontZ, CowlZ, RoofY;
        public bool Wheels = true;

        /// <summary>Üblicher Umriss aus Eckmaßen.</summary>
        public static CarProfile Make(float L, float W, float wheelbase, float sill, float belt, float tailY, float deckZ, float deckY,
                                      float roofRearZ, float roofFrontZ, float roofY, float cowlZ, float cowlY, float hoodFrontY, bool wheels = true)
        {
            var p = new CarProfile { Length = L, Width = W, Wheelbase = wheelbase, Sill = sill, Belt = belt, DeckZ = deckZ, RoofRearZ = roofRearZ, RoofFrontZ = roofFrontZ, CowlZ = cowlZ, RoofY = roofY, Wheels = wheels };
            p.Top.Add(new[] { -L / 2, tailY - 0.1f });
            p.Top.Add(new[] { -L / 2 + 0.12f, tailY });
            p.Top.Add(new[] { deckZ, deckY });
            p.Top.Add(new[] { roofRearZ, roofY });
            p.Top.Add(new[] { roofFrontZ, roofY });
            p.Top.Add(new[] { cowlZ, cowlY });
            p.Top.Add(new[] { L / 2 - 0.18f, hoodFrontY });
            p.Top.Add(new[] { L / 2, hoodFrontY - 0.12f });
            return p;
        }

        public float TopAt(float z)
        {
            if (z <= Top[0][0]) return Top[0][1];
            for (int i = 1; i < Top.Count; i++)
                if (z <= Top[i][0]) return Top[i - 1][1] + (Top[i][1] - Top[i - 1][1]) * (z - Top[i - 1][0]) / Math.Max(1e-4f, Top[i][0] - Top[i - 1][0]);
            return Top[Top.Count - 1][1];
        }
    }

    /// <summary>Sichtbare Verformung eines Fahrzeugs: Front, Heck, linke und rechte Seite (m) und der Ort entlang der Seite.</summary>
    public struct DeformState
    {
        public float Front, FrontPos, Rear, RearPos, Left, LeftPos, Right, RightPos;
        public static DeformState Lerp(DeformState a, DeformState b, float u)
        {
            Func<float, float, float> l = (x, y) => x + (y - x) * u;
            return new DeformState
            {
                Front = l(a.Front, b.Front), FrontPos = l(a.FrontPos, b.FrontPos), Rear = l(a.Rear, b.Rear), RearPos = l(a.RearPos, b.RearPos),
                Left = l(a.Left, b.Left), LeftPos = l(a.LeftPos, b.LeftPos), Right = l(a.Right, b.Right), RightPos = l(a.RightPos, b.RightPos)
            };
        }
    }

    /// <summary>
    /// Fahrzeugmodelle aus Code (ohne fremde Dateien): PKW als Querschnitte entlang der Länge mit Fenstern, Rädern und Lampen,
    /// Motorrad aus Rädern, Gabel, Tank, Sitzbank, Motor und Lenker. Die berechnete Verformung wird auf die Ecken übertragen:
    /// Front/Heck knautschen von vorn bzw. hinten (über mindestens 1,2 m, Haube wölbt sich), die Seite beult am Kontaktort ein
    /// (1 m daneben nicht mehr), beim Motorrad staucht die Gabel. Reines C#; Unity-Anbindung in <see cref="VehicleVisual"/>.
    /// </summary>
    public static class VehicleShape
    {
        public static ShapeMesh Car(CarProfile p)
        {
            var m = new ShapeMesh();
            float L = p.Length, hwBase = p.Width / 2;
            // Stationen alle 10 cm plus Eckpunkte des Umrisses
            var zs = new List<float>();
            for (float z = -L / 2; z < L / 2 - 1e-3f; z += 0.1f) zs.Add(z);
            zs.Add(L / 2);
            foreach (var t in p.Top) if (!zs.Exists(z => Math.Abs(z - t[0]) < 0.02f)) zs.Add(t[0]);
            zs.Sort();
            const int K = 10;
            var rings = new List<int[]>();
            foreach (var z in zs)
            {
                float top = p.TopAt(z);
                float end = Clamp01((Math.Abs(z) - (L / 2 - 0.5f)) / 0.5f);
                float hw = hwBase * (1 - 0.07f * end);
                float belt = Math.Min(p.Belt, top - 0.08f);
                float upper = Math.Max(0.1f, p.RoofY - belt);
                float hwTop = hw * (1 - 0.16f * Clamp01((top - belt) / upper));
                float sill = p.Sill;
                var pts = new[]
                {
                    new[] { -hw + 0.05f, sill }, new[] { -hw, sill + 0.07f }, new[] { -hw, belt }, new[] { -hwTop, top - 0.06f }, new[] { -hwTop + 0.08f, top },
                    new[] { hwTop - 0.08f, top }, new[] { hwTop, top - 0.06f }, new[] { hw, belt }, new[] { hw, sill + 0.07f }, new[] { hw - 0.05f, sill }
                };
                var ring = new int[K];
                for (int k = 0; k < K; k++) ring[k] = m.Add(pts[k][0], pts[k][1], z);
                rings.Add(ring);
            }
            float bPillar = (p.RoofFrontZ + p.RoofRearZ) / 2;
            for (int s = 0; s + 1 < rings.Count; s++)
            {
                float zm = (zs[s] + zs[s + 1]) / 2;
                bool sideGlass = zm > p.DeckZ + 0.08f && zm < p.CowlZ - 0.06f && Math.Abs(zm - bPillar) > 0.07f;
                bool shield = zm > p.RoofFrontZ && zm < p.CowlZ;
                bool rearGlass = zm > p.DeckZ && zm < p.RoofRearZ;
                for (int k = 0; k < K; k++)
                {
                    int k2 = (k + 1) % K;
                    ShapePart part = ShapePart.Lack;
                    if ((k == 2 || k == 6) && sideGlass) part = ShapePart.Glas;                      // Seitenfenster
                    if ((k == 3 || k == 4 || k == 5) && (shield || rearGlass)) part = ShapePart.Glas;   // Front- und Heckscheibe
                    if (k == 9) part = ShapePart.Reifen;                                                // Unterboden dunkel
                    // Umlauf links unten → oben → rechts → unten; Reihenfolge so, dass die Flächen nach außen zeigen (Unity: im Uhrzeigersinn)
                    m.Quad(part, rings[s][k], rings[s + 1][k], rings[s + 1][k2], rings[s][k2]);
                }
            }
            // Deckel vorn und hinten
            Cap(m, rings[0], -L / 2, false);
            Cap(m, rings[rings.Count - 1], L / 2, true);
            // Lampen
            float hood = p.TopAt(L / 2 - 0.25f), tail = p.TopAt(-L / 2 + 0.2f);
            foreach (var sx in new[] { -1f, 1f })
            {
                m.Box(ShapePart.Scheinwerfer, sx * (hwBase - 0.3f), hood - 0.12f, L / 2 - 0.02f, 0.32f, 0.09f, 0.05f);
                m.Box(ShapePart.Ruecklicht, sx * (hwBase - 0.22f), tail - 0.12f, -L / 2 + 0.01f, 0.3f, 0.1f, 0.04f);
            }
            if (p.Wheels)
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                    {
                        float x = sx * (hwBase - 0.13f), z = sz * p.Wheelbase / 2, r = p.WheelRadius;
                        m.Cylinder(ShapePart.Reifen, x - 0.11f, r, z, x + 0.11f, r, z, r, 18, rigid: true);
                        m.Cylinder(ShapePart.Metall, x + sx * 0.112f, r, z, x + sx * 0.116f, r, z, r * 0.6f, 12, rigid: true);
                    }
            m.Freeze();
            return m;
        }

        static void Cap(ShapeMesh m, int[] ring, float z, bool front)
        {
            float cx = 0, cy = 0;
            foreach (var i in ring) { cx += m.V[3 * i]; cy += m.V[3 * i + 1]; }
            int c = m.Add(cx / ring.Length, cy / ring.Length, z);
            for (int k = 0; k < ring.Length; k++)
            {
                int k2 = (k + 1) % ring.Length;
                if (front) m.Triangle(ShapePart.Lack, c, ring[k2], ring[k]); else m.Triangle(ShapePart.Lack, c, ring[k], ring[k2]);
            }
        }

        /// <summary>Maße eines Motorrads in Fahrzeugkoordinaten (Sitzbank, Lenker aus dem Fahrer-Ragdoll).</summary>
        public class MotoProfile
        {
            public float Wheelbase = 1.45f, WheelRadius = 0.31f, SeatTop = 0.8f, SeatZ = -0.05f, BarY = 1.0f, BarZ = 0.45f, BarWidth = 0.7f;
        }

        public static ShapeMesh Motorcycle(MotoProfile p)
        {
            var m = new ShapeMesh();
            float r = p.WheelRadius, zf = p.Wheelbase / 2, zr = -p.Wheelbase / 2;
            // Räder (Vorderrad gehört zur Gabel)
            m.Cylinder(ShapePart.Reifen, -0.07f, r, zf, 0.07f, r, zf, r, 20, forkA: 1, forkB: 1);
            m.Cylinder(ShapePart.Metall, -0.075f, r, zf, 0.075f, r, zf, r * 0.55f, 12, forkA: 1, forkB: 1);
            m.Cylinder(ShapePart.Reifen, -0.09f, r, zr, 0.09f, r, zr, r, 20, rigid: true);
            m.Cylinder(ShapePart.Metall, -0.095f, r, zr, 0.095f, r, zr, r * 0.55f, 12, rigid: true);
            // Gabel vom Vorderrad zum Lenkkopf (Lenkkopfwinkel ca. 25°)
            float headY = p.BarY - 0.08f, headZ = p.BarZ - 0.05f;
            foreach (var sx in new[] { -0.09f, 0.09f })
                m.Cylinder(ShapePart.Metall, sx, r, zf, sx, headY, headZ, 0.025f, 10, forkA: 1, forkB: 0.25f);
            // Lenker, Scheinwerfer
            m.Cylinder(ShapePart.Metall, -p.BarWidth / 2, p.BarY, p.BarZ, p.BarWidth / 2, p.BarY, p.BarZ, 0.016f, 8, forkA: 0.2f, forkB: 0.2f);
            m.Box(ShapePart.Scheinwerfer, 0, headY - 0.06f, headZ + 0.1f, 0.18f, 0.14f, 0.08f, fork: 0.35f);
            m.Box(ShapePart.Lack, 0, headY + 0.02f, headZ + 0.04f, 0.32f, 0.22f, 0.16f, fork: 0.25f);   // Verkleidung
            // Tank (Lack), Sitzbank, Heck, Motor, Auspuff
            m.Box(ShapePart.Lack, 0, p.SeatTop + 0.06f, p.SeatZ + 0.42f, 0.26f, 0.2f, 0.42f);
            m.Box(ShapePart.Sitz, 0, p.SeatTop - 0.04f, p.SeatZ - 0.1f, 0.27f, 0.08f, 0.55f);
            m.Box(ShapePart.Lack, 0, p.SeatTop - 0.02f, p.SeatZ - 0.52f, 0.22f, 0.12f, 0.35f);
            m.Box(ShapePart.Ruecklicht, 0, p.SeatTop - 0.02f, p.SeatZ - 0.71f, 0.12f, 0.05f, 0.03f);
            m.Box(ShapePart.Metall, 0, 0.45f, 0.05f, 0.34f, 0.32f, 0.5f);
            m.Cylinder(ShapePart.Metall, 0.17f, 0.3f, 0.1f, 0.2f, 0.42f, zr - 0.1f, 0.045f, 10);
            // Rahmen vom Lenkkopf zur Schwinge
            m.Cylinder(ShapePart.Metall, 0, headY - 0.05f, headZ - 0.02f, 0, 0.5f, -0.25f, 0.035f, 10);
            m.Cylinder(ShapePart.Metall, 0.1f, r, zr, 0.1f, 0.4f, -0.2f, 0.025f, 8);
            m.Cylinder(ShapePart.Metall, -0.1f, r, zr, -0.1f, 0.4f, -0.2f, 0.025f, 8);
            m.Freeze();
            return m;
        }

        /// <summary>Verformung auf die Ruhelage anwenden (schreibt <see cref="ShapeMesh.V"/>).</summary>
        public static void DeformCar(ShapeMesh m, CarProfile p, DeformState d)
        {
            float L = p.Length, hw = p.Width / 2;
            for (int i = 0; i < m.Count; i++)
            {
                float x = m.Rest[3 * i], y = m.Rest[3 * i + 1], z = m.Rest[3 * i + 2];
                if (m.Rigid[i]) { m.V[3 * i] = x; m.V[3 * i + 1] = y; m.V[3 * i + 2] = z; continue; }
                // Front: Bereich [z0, L/2] auf [z0, L/2 − c] stauchen, seitlich zum Kontaktort hin stärker; Haube wölbt sich
                // Knautschweg höchstens bis kurz vor die Hinterachse (schützt vor unsinnigen Eingaben)
                float cf = Math.Min(d.Front, L / 2 - 0.6f) * Lateral(x, d.FrontPos, hw);
                if (cf > 1e-4f)
                {
                    float z0 = Math.Max(-L / 2 + 0.3f, L / 2 - Math.Max(1.2f, 1.6f * cf));
                    if (z > z0)
                    {
                        float u = (z - z0) / (L / 2 - z0);
                        z = z0 + u * (L / 2 - cf - z0);
                        if (y > p.Belt - 0.25f) y += 0.12f * Math.Min(cf, 0.8f) * (float)Math.Sin(Math.PI * u);
                    }
                }
                float cr = Math.Min(d.Rear, L / 2 - 0.6f) * Lateral(x, d.RearPos, hw);
                if (cr > 1e-4f)
                {
                    float z0 = Math.Min(L / 2 - 0.3f, -L / 2 + Math.Max(1.0f, 1.6f * cr));
                    if (z < z0)
                    {
                        float u = (z0 - z) / (z0 + L / 2);
                        z = z0 - u * (z0 + L / 2 - cr);
                        if (y > p.Sill + 0.3f) y += 0.08f * cr * (float)Math.Sin(Math.PI * u);
                    }
                }
                // Seiten: Delle am Kontaktort, nach oben (Dach) und unten (Schweller) schwächer
                float hy = y < p.Sill + 0.15f ? 0.6f : y > p.Belt + 0.1f ? Math.Max(0.35f, 1 - (y - p.Belt) / 0.8f) : 1f;
                if (x < 0 && d.Left > 1e-4f) x += d.Left * Fall(m.Rest[3 * i + 2], d.LeftPos) * hy * Clamp01(-x / hw);
                if (x > 0 && d.Right > 1e-4f) x -= d.Right * Fall(m.Rest[3 * i + 2], d.RightPos) * hy * Clamp01(x / hw);
                m.V[3 * i] = x; m.V[3 * i + 1] = y; m.V[3 * i + 2] = z;
            }
        }

        /// <summary>Motorrad: Gabel staucht um <paramref name="crush"/> (Radstandverkürzung), unten voll, am Lenkkopf weniger.</summary>
        public static void DeformMotorcycle(ShapeMesh m, float crush)
        {
            for (int i = 0; i < m.Count; i++)
            {
                m.V[3 * i] = m.Rest[3 * i]; m.V[3 * i + 1] = m.Rest[3 * i + 1];
                m.V[3 * i + 2] = m.Rest[3 * i + 2] - crush * m.Fork[i];
            }
        }

        /// <summary>Anteil der Frontverformung quer zur Fahrzeugachse: um den Kontaktort ±0,5 m voll, zum anderen Rand hin weniger (≥ 35 %).</summary>
        static float Lateral(float x, float pos, float hw) => Math.Max(0.35f, 1 - Math.Max(0, Math.Abs(x - pos) - 0.5f) / (2 * hw));
        static float Fall(float z, float pos) => Math.Max(0, 1 - Math.Abs(z - pos) / 1.0f);
        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}

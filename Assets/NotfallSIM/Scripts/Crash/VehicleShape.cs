using System;
using System.Collections.Generic;

namespace NotfallSIM.Crash
{
    /// <summary>Teile eines erzeugten Fahrzeugmodells (je ein Material).</summary>
    public enum ShapePart { Lack = 0, Glas = 1, Reifen = 2, Scheinwerfer = 3, Ruecklicht = 4, Metall = 5, Sitz = 6 }

    /// <summary>Scheiben eines PKW: Frontscheibe (Verbundglas, VSG), alle anderen Einscheibensicherheitsglas (ESG).</summary>
    public enum Pane { Front = 0, Rear = 1, LeftFront = 2, LeftRear = 3, RightFront = 4, RightRear = 5 }

    /// <summary>Dreiecksnetz ohne Unity-Typen: Ecken (x, y, z hintereinander), Dreiecke je Teil, Ecken-Merkmale für die Verformung.</summary>
    public class ShapeMesh
    {
        public readonly List<float> V = new List<float>();
        public readonly List<int>[] Tri = new List<int>[7];
        /// <summary>Je Ecke: starr (Räder, Lampenfassungen der Hinterachse …) — wird nicht verformt.</summary>
        public readonly List<bool> Rigid = new List<bool>();
        /// <summary>Je Ecke: Anteil an der Gabel (Motorrad, 0–1) — staucht beim Frontaufprall mit.</summary>
        public readonly List<float> Fork = new List<float>();
        /// <summary>Je Dreieck des Glas-Teils: welche Scheibe (<see cref="Pane"/>), für Bruch und Risse.</summary>
        public readonly List<int> GlassPane = new List<int>();
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
        /// <summary>Wölbung der Frontscheibe am Anprallort (m, Betrag; Richtung und Ort stehen im Fahrzeugmodell).</summary>
        public float Shield;
        public static DeformState Lerp(DeformState a, DeformState b, float u)
        {
            Func<float, float, float> l = (x, y) => x + (y - x) * u;
            return new DeformState
            {
                Shield = l(a.Shield, b.Shield),
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
            const int TopMid = 8, K = 10 + TopMid;
            const int SideL = 2, SideR = 6 + TopMid, Under = 9 + TopMid;   // Seitenfenster links/rechts, Unterboden
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
                // Querschnitt: Flanke links, Dach mit Zwischenpunkten (für Dellen und die Wölbung der Frontscheibe), Flanke rechts
                var pts = new List<float[]>
                {
                    new[] { -hw + 0.05f, sill }, new[] { -hw, sill + 0.07f }, new[] { -hw, belt }, new[] { -hwTop, top - 0.06f }, new[] { -hwTop + 0.08f, top }
                };
                for (int j = 1; j <= TopMid; j++) pts.Add(new[] { -hwTop + 0.08f + (2 * hwTop - 0.16f) * j / (TopMid + 1), top });
                pts.Add(new[] { hwTop - 0.08f, top }); pts.Add(new[] { hwTop, top - 0.06f }); pts.Add(new[] { hw, belt });
                pts.Add(new[] { hw, sill + 0.07f }); pts.Add(new[] { hw - 0.05f, sill });
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
                    int pane = -1;
                    if (k == SideL && sideGlass) pane = (int)(zm > bPillar ? Pane.LeftFront : Pane.LeftRear);
                    if (k == SideR && sideGlass) pane = (int)(zm > bPillar ? Pane.RightFront : Pane.RightRear);
                    if (k >= 3 && k <= SideR - 1 && shield) pane = (int)Pane.Front;
                    if (k >= 3 && k <= SideR - 1 && rearGlass) pane = (int)Pane.Rear;
                    if (pane >= 0) part = ShapePart.Glas;
                    if (k == Under) part = ShapePart.Reifen;                                            // Unterboden dunkel
                    // Umlauf links unten → oben → rechts → unten; Reihenfolge so, dass die Flächen nach außen zeigen (Unity: im Uhrzeigersinn)
                    m.Quad(part, rings[s][k], rings[s + 1][k], rings[s + 1][k2], rings[s][k2]);
                    if (pane >= 0) { m.GlassPane.Add(pane); m.GlassPane.Add(pane); }
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
                if (m.Rigid[i])
                {
                    // Räder bleiben starr, werden aber zurückgeschoben, sobald die Knautschzone vor der Achse aufgebraucht ist
                    float over = L / 2 - p.Wheelbase / 2 - p.WheelRadius;
                    if (z > 0) z -= Math.Max(0, Math.Min(d.Front, L / 2 - 0.6f) - over);
                    else z += Math.Max(0, Math.Min(d.Rear, L / 2 - 0.6f) - over);
                    m.V[3 * i] = x; m.V[3 * i + 1] = y; m.V[3 * i + 2] = z; continue;
                }
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


        // ---------------------------------------------------------------- Scheiben

        /// <summary>Dreiecke (je drei Ecken-Indizes) einer Scheibe.</summary>
        public static List<int> PaneTriangles(ShapeMesh m, Pane pane)
        {
            var t = m.Tri[(int)ShapePart.Glas];
            var list = new List<int>();
            for (int i = 0; i < m.GlassPane.Count; i++)
                if (m.GlassPane[i] == (int)pane) { list.Add(t[3 * i]); list.Add(t[3 * i + 1]); list.Add(t[3 * i + 2]); }
            return list;
        }

        /// <summary>Glas-Dreiecke ohne die zerfallenen Scheiben (Bit je <see cref="Pane"/> in <paramref name="brokenMask"/>).</summary>
        public static List<int> GlassWithout(ShapeMesh m, int brokenMask)
        {
            var t = m.Tri[(int)ShapePart.Glas];
            var list = new List<int>(t.Count);
            for (int i = 0; i < m.GlassPane.Count; i++)
                if ((brokenMask & (1 << m.GlassPane[i])) == 0) { list.Add(t[3 * i]); list.Add(t[3 * i + 1]); list.Add(t[3 * i + 2]); }
            return list;
        }

        /// <summary>Außennormale der Frontscheibe (Seitenansicht: von der Spritzwand zur Dachvorderkante), Fahrzeugkoordinaten.</summary>
        public static void ShieldNormal(CarProfile p, out float ny, out float nz)
        {
            float dz = p.RoofFrontZ - p.CowlZ, dy = p.RoofY - p.TopAt(p.CowlZ);
            float len = (float)Math.Sqrt(dz * dz + dy * dy);
            ny = -dz / len; nz = dy / len;
        }

        /// <summary>Punkt auf der Frontscheibe: Ort (x, z) + Versatz a quer und b entlang der Neigung (nach oben positiv).</summary>
        public static void ShieldPoint(CarProfile p, float x, float z, float a, float b, out float px, out float py, out float pz)
        {
            float dz = p.RoofFrontZ - p.CowlZ, dy = p.RoofY - p.TopAt(p.CowlZ);
            float len = (float)Math.Sqrt(dz * dz + dy * dy);
            pz = z + b * dz / len;
            px = x + a;
            py = p.TopAt(pz);
        }

        /// <summary>Anprallort auf die Frontscheibe begrenzen (mit Abstand zum Rahmen).</summary>
        public static void ClampToShield(CarProfile p, ref float x, ref float z)
        {
            float lo = Math.Min(p.RoofFrontZ, p.CowlZ) + 0.12f, hi = Math.Max(p.RoofFrontZ, p.CowlZ) - 0.12f;
            z = Math.Max(lo, Math.Min(hi, z));
            float half = p.Width / 2 * 0.84f - 0.2f;
            x = Math.Max(-half, Math.Min(half, x));
        }

        /// <summary>
        /// Frontscheibe wölbt sich am Anprallort (cx, cz) um <paramref name="depth"/> (m, + nach außen) mit Kosinusglocke
        /// über <paramref name="radius"/>; am Rahmen (Spritzwand, Dach, A-Säulen) bleibt sie fest. Wirkt auf die schon verformten Ecken.
        /// </summary>
        public static void BulgeShield(ShapeMesh m, CarProfile p, float cx, float cz, float depth, float radius)
        {
            if (Math.Abs(depth) < 1e-4f || radius <= 0) return;
            float ny, nz;
            ShieldNormal(p, out ny, out nz);
            float cy = p.TopAt(cz);
            float zLo = Math.Min(p.RoofFrontZ, p.CowlZ), zHi = Math.Max(p.RoofFrontZ, p.CowlZ);
            var done = new HashSet<int>();
            foreach (var i in PaneTriangles(m, Pane.Front))
            {
                if (!done.Add(i)) continue;
                float x = m.Rest[3 * i], y = m.Rest[3 * i + 1], z = m.Rest[3 * i + 2];
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz));
                if (d >= radius) continue;
                float w = 0.5f * (1 + (float)Math.Cos(Math.PI * d / radius));
                float edgeZ = Math.Min(z - zLo, zHi - z), edgeX = p.Width / 2 * 0.84f - Math.Abs(x);
                w *= Clamp01(edgeZ / 0.1f) * Clamp01(edgeX / 0.12f);
                m.V[3 * i + 1] += depth * w * ny;
                m.V[3 * i + 2] += depth * w * nz;
            }
        }

        /// <summary>Lage eines Punkts auf einem Dreieck (Ecken a, b, c und baryzentrische Gewichte) — folgt der Verformung.</summary>
        public struct Anchor { public int A, B, C; public float U, V, W; }

        /// <summary>Punkt (Ruhelage) auf die nächste der Dreiecke projizieren; false, wenn er außerhalb liegt.</summary>
        public static bool Attach(ShapeMesh m, List<int> tris, float px, float py, float pz, out Anchor anchor)
        {
            anchor = default(Anchor);
            float best = 0.05f;
            bool found = false;
            var r = m.Rest;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                float ax = r[3 * a], ay = r[3 * a + 1], az = r[3 * a + 2];
                float e1x = r[3 * b] - ax, e1y = r[3 * b + 1] - ay, e1z = r[3 * b + 2] - az;
                float e2x = r[3 * c] - ax, e2y = r[3 * c + 1] - ay, e2z = r[3 * c + 2] - az;
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-9f) continue;
                nx /= nl; ny /= nl; nz /= nl;
                float wx = px - ax, wy = py - ay, wz = pz - az;
                float dist = wx * nx + wy * ny + wz * nz;
                if (Math.Abs(dist) >= best) continue;
                wx -= dist * nx; wy -= dist * ny; wz -= dist * nz;
                float d00 = e1x * e1x + e1y * e1y + e1z * e1z, d01 = e1x * e2x + e1y * e2y + e1z * e2z, d11 = e2x * e2x + e2y * e2y + e2z * e2z;
                float d20 = wx * e1x + wy * e1y + wz * e1z, d21 = wx * e2x + wy * e2y + wz * e2z;
                float den = d00 * d11 - d01 * d01;
                if (Math.Abs(den) < 1e-12f) continue;
                float v = (d11 * d20 - d01 * d21) / den, w = (d00 * d21 - d01 * d20) / den, u = 1 - v - w;
                const float tol = -0.02f;
                if (u < tol || v < tol || w < tol) continue;
                best = Math.Abs(dist); found = true;
                anchor = new Anchor { A = a, B = b, C = c, U = u, V = v, W = w };
            }
            return found;
        }

        /// <summary>Lage eines verankerten Punkts auf den aktuellen (verformten) Ecken, dazu die Flächennormale.</summary>
        public static void Evaluate(ShapeMesh m, Anchor k, out float x, out float y, out float z, out float nx, out float ny, out float nz)
        {
            var V = m.V;
            x = k.U * V[3 * k.A] + k.V * V[3 * k.B] + k.W * V[3 * k.C];
            y = k.U * V[3 * k.A + 1] + k.V * V[3 * k.B + 1] + k.W * V[3 * k.C + 1];
            z = k.U * V[3 * k.A + 2] + k.V * V[3 * k.B + 2] + k.W * V[3 * k.C + 2];
            float e1x = V[3 * k.B] - V[3 * k.A], e1y = V[3 * k.B + 1] - V[3 * k.A + 1], e1z = V[3 * k.B + 2] - V[3 * k.A + 2];
            float e2x = V[3 * k.C] - V[3 * k.A], e2y = V[3 * k.C + 1] - V[3 * k.A + 1], e2z = V[3 * k.C + 2] - V[3 * k.A + 2];
            nx = e1y * e2z - e1z * e2y; ny = e1z * e2x - e1x * e2z; nz = e1x * e2y - e1y * e2x;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (l > 1e-9f) { nx /= l; ny /= l; nz /= l; }
        }

        /// <summary>Anteil der Frontverformung quer zur Fahrzeugachse: um den Kontaktort ±0,5 m voll, zum anderen Rand hin weniger (≥ 35 %).</summary>
        static float Lateral(float x, float pos, float hw) => Math.Max(0.35f, 1 - Math.Max(0, Math.Abs(x - pos) - 0.5f) / (2 * hw));
        internal static float Fall(float z, float pos) => Math.Max(0, 1 - Math.Abs(z - pos) / 1.0f);
        internal static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>
    /// Wann eine Scheibe bricht. Frontscheibe: Verbundsicherheitsglas (VSG) — reißt beim Anprall von Kopf/Körper
    /// spinnennetzartig, bleibt im Rahmen und wölbt sich. Seiten- und Heckscheiben: Einscheibensicherheitsglas (ESG) —
    /// zerfallen auf einmal in kleine Würfel, sobald sich der Rahmen verformt. Schwellen: **Annahmen** (Darstellung),
    /// siehe docs/LITERATUR.md.
    /// </summary>
    public static class GlassBreak
    {
        /// <summary>ESG zerfällt, wenn die Tür/der Rahmen an der Scheibe so weit eindrückt (m).</summary>
        public const float SideIntrusion_m = 0.03f;
        /// <summary>Heckscheibe zerfällt ab diesem Heck-Knautschweg (m).</summary>
        public const float RearCrush_m = 0.25f;
        /// <summary>Frontscheibe reißt an den unteren Ecken ab diesem Front-Knautschweg (A-Säulen verformen sich, m).</summary>
        public const float FrontCrush_m = 0.75f;
        /// <summary>Anprall eines Körperteils auf die Frontscheibe ab dieser Geschwindigkeit senkrecht zur Scheibe: Spinnennetz (m/s).</summary>
        public const float CrackSpeed_m_s = 3.0f;

        /// <summary>Radius des Rissbilds (m) aus der Anprallgeschwindigkeit: 7 cm an der Schwelle, +3 cm je m/s, höchstens 35 cm.</summary>
        public static float CrackRadius(float speed_m_s) => Math.Max(0.07f, Math.Min(0.35f, 0.07f + 0.03f * (speed_m_s - CrackSpeed_m_s)));

        public static bool IsSide(Pane p) => p != Pane.Front && p != Pane.Rear;
        public static bool IsLeft(Pane p) => p == Pane.LeftFront || p == Pane.LeftRear;

        /// <summary>Mitte einer Seitenscheibe entlang der Länge (m).</summary>
        public static float PaneCenterZ(CarProfile p, Pane pane)
        {
            float b = (p.RoofFrontZ + p.RoofRearZ) / 2;
            return pane == Pane.LeftFront || pane == Pane.RightFront ? (b + p.CowlZ) / 2 : (p.DeckZ + b) / 2;
        }

        /// <summary>Verformung des Rahmens an einer ESG-Scheibe (m); Seite wie die Delle (voll am Kontaktort, 1 m daneben nicht mehr).</summary>
        public static float Load(DeformState d, CarProfile p, Pane pane)
        {
            switch (pane)
            {
                case Pane.Rear: return d.Rear;
                case Pane.Front: return d.Front;
                case Pane.LeftFront: case Pane.LeftRear: return d.Left * VehicleShape.Fall(PaneCenterZ(p, pane), d.LeftPos);
                default: return d.Right * VehicleShape.Fall(PaneCenterZ(p, pane), d.RightPos);
            }
        }

        public static bool Breaks(DeformState d, CarProfile p, Pane pane) =>
            pane == Pane.Rear ? d.Rear >= RearCrush_m : pane != Pane.Front && Load(d, p, pane) >= SideIntrusion_m;

        public static string Label(Pane p) =>
            p == Pane.Front ? "Frontscheibe" : p == Pane.Rear ? "Heckscheibe" : p == Pane.LeftFront ? "Seitenscheibe vorn links"
            : p == Pane.LeftRear ? "Seitenscheibe hinten links" : p == Pane.RightFront ? "Seitenscheibe vorn rechts" : "Seitenscheibe hinten rechts";
    }

    /// <summary>
    /// Rissbild im Verbundglas um einen Anprallort (2D, Meter, a quer, b entlang der Scheibe): radiale Risse vom Zentrum,
    /// dazu ringförmige Risse zwischen benachbarten Radialen, im Zentrum ein dichter Bruchkern. Deterministisch über den Startwert.
    /// </summary>
    public static class GlassCrack
    {
        /// <summary>Ein Riss-Stück von (A0, B0) nach (A1, B1); <see cref="Reach"/> = Anteil des Radius (wann es beim Wachsen erscheint).</summary>
        public struct Segment { public float A0, B0, A1, B1, Reach; }

        public static List<Segment> Pattern(int seed, float radius)
        {
            var rng = new Random(seed);
            var list = new List<Segment>();
            int n = Math.Max(7, Math.Min(16, 6 + (int)(radius * 30)));
            const int steps = 7;
            var radial = new float[n][];   // je Radiale: Punkte (a, b) bei Bruchteil k/steps
            double start = rng.NextDouble() * Math.PI * 2;
            for (int i = 0; i < n; i++)
            {
                double ang = start + 2 * Math.PI * i / n + (rng.NextDouble() - 0.5) * 0.5;
                float len = radius * (0.7f + 0.45f * (float)rng.NextDouble());
                var pts = new float[2 * (steps + 1)];
                float a = 0, b = 0;
                for (int k = 1; k <= steps; k++)
                {
                    ang += (rng.NextDouble() - 0.5) * 0.35;
                    float l = len / steps;
                    a += l * (float)Math.Cos(ang); b += l * (float)Math.Sin(ang);
                    pts[2 * k] = a; pts[2 * k + 1] = b;
                }
                radial[i] = pts;
                for (int k = 0; k < steps; k++)
                    list.Add(new Segment { A0 = pts[2 * k], B0 = pts[2 * k + 1], A1 = pts[2 * k + 2], B1 = pts[2 * k + 3], Reach = (k + 1) / (float)steps * len / radius });
            }
            // Ringe: zwischen benachbarten Radialen, mit leichter Zacke
            int rings = radius > 0.2f ? 3 : 2;
            for (int r = 1; r <= rings; r++)
            {
                int k = Math.Max(1, (int)Math.Round(steps * (r / (rings + 1.0))));
                for (int i = 0; i < n; i++)
                {
                    if (rng.NextDouble() < 0.2) continue;
                    var p0 = radial[i]; var p1 = radial[(i + 1) % n];
                    float ma = (p0[2 * k] + p1[2 * k]) / 2 * (0.9f + 0.15f * (float)rng.NextDouble());
                    float mb = (p0[2 * k + 1] + p1[2 * k + 1]) / 2 * (0.9f + 0.15f * (float)rng.NextDouble());
                    float reach = k / (float)steps;
                    list.Add(new Segment { A0 = p0[2 * k], B0 = p0[2 * k + 1], A1 = ma, B1 = mb, Reach = reach });
                    list.Add(new Segment { A0 = ma, B0 = mb, A1 = p1[2 * k], B1 = p1[2 * k + 1], Reach = reach });
                }
            }
            // Bruchkern: kurze, dichte Risse
            float core = radius * 0.12f;
            for (int i = 0; i < 14; i++)
            {
                double ang = rng.NextDouble() * Math.PI * 2, ang2 = ang + (rng.NextDouble() - 0.5) * 2;
                float r0 = core * (float)rng.NextDouble(), r1 = core * (float)rng.NextDouble();
                list.Add(new Segment
                {
                    A0 = r0 * (float)Math.Cos(ang), B0 = r0 * (float)Math.Sin(ang), A1 = r1 * (float)Math.Cos(ang2), B1 = r1 * (float)Math.Sin(ang2), Reach = 0.05f
                });
            }
            return list;
        }
    }
}

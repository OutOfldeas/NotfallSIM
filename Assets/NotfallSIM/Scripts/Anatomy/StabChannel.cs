using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Crash;

namespace NotfallSIM.Anatomy
{
    /// <summary>Getroffene Struktur entlang eines Stichkanals.</summary>
    public class ChannelHit
    {
        public string Name, Label, Layer;
        public double Depth_m;   // Abstand vom Einstich bis zum Eintritt in die Struktur (Modellmaß)
    }

    /// <summary>
    /// Welche Strukturen liegen in der Bahn einer Klinge? Strahl vom Einstich (Modellkoordinaten des Anatomiemodells,
    /// Ruhelage) in Stichrichtung gegen Organe, Gefäße, Nerven und Knochen aus body.bin (BodyParts3D). Ohne Unity, getestet.
    /// Grundlage für die Vorschau der Gewalt-Sandbox und die Strukturliste je Stichkanal; die Verletzung selbst ergibt
    /// sich weiter aus Region und Tiefe (<see cref="ImpactModel"/>).
    /// </summary>
    public static class StabChannel
    {
        static readonly string[] Layers = { "organ", "brain", "artery", "vein", "nerve", "bone" };
        static readonly Dictionary<AnatomyMeshData, float[][]> Boxes = new Dictionary<AnatomyMeshData, float[][]>();

        /// <param name="origin">Einstich (Modellkoordinaten)</param>
        /// <param name="dir">Stichrichtung (wird normiert)</param>
        /// <param name="maxDepth">Klingenlänge bzw. erreichte Tiefe im Modellmaß</param>
        public static List<ChannelHit> Trace(AnatomyData data, float[] origin, float[] dir, double maxDepth)
        {
            var hits = new List<ChannelHit>();
            if (data == null || origin == null || dir == null || maxDepth <= 0) return hits;
            double len = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
            if (len < 1e-9) return hits;
            double dx = dir[0] / len, dy = dir[1] / len, dz = dir[2] / len;
            foreach (var m in data.Meshes)
            {
                if (m.Info == null || Array.IndexOf(Layers, m.Info.layer) < 0 || m.Positions == null || m.Indices == null) continue;
                float[][] box;
                lock (Boxes)
                {
                    if (!Boxes.TryGetValue(m, out box))
                    {
                        MeshPick.Bounds(m.Positions, out var mn, out var mx);
                        Boxes[m] = box = new[] { mn, mx };
                    }
                }
                if (!MeshPick.HitsBox(box[0], box[1], 0.002f, origin[0], origin[1], origin[2], dx, dy, dz)) continue;
                if (!MeshPick.Raycast(m.Positions, m.Indices, origin[0], origin[1], origin[2], dx, dy, dz, out double t, out _, out _, out _, out _)) continue;
                if (t > maxDepth) continue;
                hits.Add(new ChannelHit { Name = m.Info.name, Label = string.IsNullOrEmpty(m.Info.label) ? m.Info.name : m.Info.label, Layer = m.Info.layer, Depth_m = t });
            }
            return hits.OrderBy(h => h.Depth_m).ToList();
        }

        /// <summary>
        /// Wie <see cref="Trace"/>, aber vom Hauteintritt aus: Der Kontaktpunkt der Physik liegt auf einer Kapsel, nicht auf der
        /// Haut des Modells. Vom Punkt aus wird rückwärts die Haut (erstes Netz, Ebene „skin“) gesucht und von dort gemessen.
        /// Unterhautfett über dem Modell (BMI) kommt beim Aufrufer dazu.
        /// </summary>
        public static List<ChannelHit> TraceFromSkin(AnatomyData data, float[] point, float[] dir, double maxDepth)
        {
            if (data == null || point == null || dir == null) return new List<ChannelHit>();
            double len = Math.Sqrt(dir[0] * dir[0] + dir[1] * dir[1] + dir[2] * dir[2]);
            if (len < 1e-9) return new List<ChannelHit>();
            float dx = (float)(dir[0] / len), dy = (float)(dir[1] / len), dz = (float)(dir[2] / len);
            var skin = data.Meshes.FirstOrDefault(m => m.Info != null && m.Info.layer == "skin");
            var origin = point;
            if (skin != null)
            {
                // Liegt der Punkt unter der Haut, ist die nächste Hautfläche rückwärts der Eintritt; liegt er davor,
                // die nächste vorwärts (nicht weiter als 10 cm — sonst z. B. der danebenliegende Arm)
                const double reach = 0.1;
                if (MeshPick.Raycast(skin.Positions, skin.Indices, point[0], point[1], point[2], -dx, -dy, -dz, out double tb, out _, out _, out _, out _) && tb < reach)
                    origin = new[] { (float)(point[0] - dx * tb), (float)(point[1] - dy * tb), (float)(point[2] - dz * tb) };
                else if (MeshPick.Raycast(skin.Positions, skin.Indices, point[0], point[1], point[2], dx, dy, dz, out double tf, out _, out _, out _, out _) && tf < reach)
                    origin = new[] { (float)(point[0] + dx * tf), (float)(point[1] + dy * tf), (float)(point[2] + dz * tf) };
            }
            return Trace(data, origin, new[] { dx, dy, dz }, maxDepth);
        }

        /// <summary>Weichteil- oder Hohlraumstrukturen, die beim Stich als Organverletzung zählen (nicht Knochen, nicht Nerv allein).</summary>
        public static bool IsDeep(ChannelHit h) => h.Layer == "organ" || h.Layer == "artery" || h.Layer == "vein" || h.Layer == "brain";

        /// <summary>Kurztext: „Rippe 1,8 cm · Leber 3,5 cm“ (Tiefen mit <paramref name="scale"/> auf den Körper umgerechnet).</summary>
        public static string Describe(IEnumerable<ChannelHit> hits, double scale, int max = 4)
        {
            var list = hits.Take(max).Select(h => $"{h.Label} {h.Depth_m * scale * 100:0.#} cm").ToList();
            int more = hits.Count() - list.Count;
            return list.Count == 0 ? "" : string.Join(" · ", list) + (more > 0 ? $" · +{more}" : "");
        }
    }
}

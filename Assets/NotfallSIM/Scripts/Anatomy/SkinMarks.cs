using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Data;

namespace NotfallSIM.Anatomy
{
    // ---------- Datenmodell (injury_visuals.json) ----------

    [Serializable]
    public class MarkKind
    {
        public string id, label;
        public float[] colorCenter, colorEdge;   // RGB 0..1
        public float[] fragmentColor;            // optional: Knochenfragmente in der Wunde
        public float fragmentShare;              // Anteil der Wundfläche mit Fragmenten
        public float opacity;                    // Deckkraft in der Mitte
        public float wet;                        // 0 matt … 1 feucht glänzend
        public float edge;                       // Randunschärfe im Shader (klein = scharfer Schnittrand)
        public float irregularity;               // Unregelmäßigkeit des Rands (Riss/Quetschung groß, Schnitt klein)
        public float noiseScale;                 // Körnung des Rauschens (Schürfung fein)
        public int layer;                        // Reihenfolge übereinander: 0 Haut (Prellung), 1 Blut auf der Haut, 2 Wunde
        public float depth;                      // Wundtiefe im Shader (0 flach … 1 tief klaffend)
        public bool dries;                       // Blut trocknet nach dem Ende der Blutung ein
        public string evidence, sourceId, note;
    }

    [Serializable]
    public class MarkAnchor
    {
        public string[] meshes;      // FMA-Namen; Ausgangspunkt ist der Mittelpunkt ihrer Schwerpunkte
        public float[] offset;       // Versatz in Metern (Modell: x rechts, y kopfwärts, z ventral)
        public float[] direction;    // Strahl vom Ausgangspunkt nach außen bis zur Haut
    }

    [Serializable]
    public class SkinMark
    {
        public string id, injuryId, kind;
        public string finding;            // Befundtext der Verletzung, den die Darstellung zeigt (Nachvollziehbarkeit)
        public string region;             // Körperregion, in der der Treffpunkt liegen muss (leer: Region der Verletzung)
        public MarkAnchor anchor;
        public string[] segments;         // nur Haut dieser Segmente (leer: alle)
        public float radius_m;
        public float[] stretchAxis;       // optional: längliche Form entlang dieser Achse (leer: Längsachse des Segments) …
        public float stretch;             // … um diesen Faktor (≥ 1)
        public bool wrap;                 // auch um Kanten herum (z. B. ganzer Oberschenkel), sonst nur die zugewandte Seite
        public DynamicFinding when;       // Bedingung wie bei den Untersuchungsbefunden (leer: immer)
        public double growFromLost_mL, growToLost_mL; // wächst mit dem Blutverlust dieser Verletzung
        public double swelling_mL;        // feste Schwellung (Volumen)
        public bool swellingFromLoss;     // Schwellung = Blutverlust dieser Verletzung ins Gewebe
        public string swellingNote;
        public bool bleeding;             // äußere Blutung: Spritzer und Blutlache aus dem Verlust dieser Verletzung
        public bool amputation;           // Haut distal der Ebene (Anker, Richtung = nach distal) in den Segmenten entfernen, Stumpf schließen
    }

    [Serializable]
    public class BloodPoolParams
    {
        public double surfaceTension_N_m; public string surfaceTensionSourceId;
        public double density_kg_m3; public string densitySourceId;
        public double contactAngle_deg; public string contactAngleNote;
        public string formulaSourceId, note;
    }

    [Serializable]
    public class DryingParams
    {
        public double wetToDry_min;        // von frisch bis eingetrocknet nach dem Ende der Blutung
        public double preArrivalAge_min;   // Alter von Wunden ohne laufende Blutung bei Ankunft
        public string sourceId, note;
    }

    [Serializable]
    public class SpurtParams
    {
        public double velocityCoefficient; public string sourceId, note;
    }

    [Serializable]
    public class VisualSource { public string id, citation, url, note; }

    [Serializable]
    public class InjuryVisualsFile
    {
        public string version, note;
        public float surfaceOffset_m;     // Abstand der Auflage über der Haut (gegen Z-Fighting)
        public MarkKind[] kinds;
        public SkinMark[] marks;
        public BloodPoolParams bloodPool;
        public SpurtParams spurt;
        public DryingParams drying;
        public VisualSource[] sources;
    }

    /// <summary>Ein ausgewählter Hautbereich: Hautvertices mit Abfall 1 (Mitte) … 0 (Rand) und die Dreiecke darin.</summary>
    public class SkinPatch
    {
        public int HitTriangle = -1;
        public float[] HitPoint, HitNormal;
        public int[] Vertices;          // Hautvertices im Radius (für die Schwellung)
        public float[] Falloff;         // je Eintrag in Vertices
        // Auflage zum Zeichnen; bei kleinen Zeichen feiner unterteilt als die Haut. Jeder Vertex ist eine Mischung aus
        // bis zu drei Hautvertices (Src/W je 3 Einträge), damit Gewichte und Blendshapes der Haut übernommen werden können.
        public int[] Src; public float[] W;
        public float[] Pos, Nrm;        // Modelleinheiten
        public float[] RFalloff;        // Abfall je Auflage-Vertex
        public int[] Triangles;         // Dreiecke der Auflage
        public int Subdivisions;
        public string HitRegion;
        public bool HiddenByShorts;     // Treffpunkt liegt unter der Unterhose
    }

    /// <summary>
    /// Verletzungszeichen auf der Haut ohne Unity (getestet): Verankerung an der Anatomie, Auswahl des Hautbereichs,
    /// Farbe je Vertex, Schwellung aus einem Volumen, Größe der Blutlache und Geschwindigkeit eines Blutstrahls.
    /// Alle Längen in Modelleinheiten (body.bin), außer wo „_m“ steht.
    /// </summary>
    public static class SkinMarks
    {
        // ---------------------------------------------------------------- Geometrie der Haut

        public static float[] VertexNormals(float[] pos, int[] idx, int triCount)
        {
            var n = new float[pos.Length];
            for (int t = 0; t < triCount; t++)
            {
                int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
                var fn = Cross(Sub(pos, b, a), Sub(pos, c, a)); // flächengewichtet
                for (int k = 0; k < 3; k++) { n[3 * a + k] += fn[k]; n[3 * b + k] += fn[k]; n[3 * c + k] += fn[k]; }
            }
            for (int i = 0; i < n.Length; i += 3)
            {
                float l = (float)Math.Sqrt(n[i] * n[i] + n[i + 1] * n[i + 1] + n[i + 2] * n[i + 2]);
                if (l > 0) { n[i] /= l; n[i + 1] /= l; n[i + 2] /= l; }
            }
            return n;
        }

        /// <summary>Fläche je Vertex (ein Drittel der anliegenden Dreiecke).</summary>
        public static float[] VertexAreas(float[] pos, int[] idx, int triCount)
        {
            var a = new float[pos.Length / 3];
            for (int t = 0; t < triCount; t++)
            {
                int i = idx[3 * t], j = idx[3 * t + 1], k = idx[3 * t + 2];
                float ar = Len(Cross(Sub(pos, j, i), Sub(pos, k, i))) / 6f;
                a[i] += ar; a[j] += ar; a[k] += ar;
            }
            return a;
        }

        /// <summary>Erster Austritt des Strahls aus der Haut (Dreieck zeigt in Strahlrichtung). Möller-Trumbore.</summary>
        /// <param name="accept">optional: nur Dreiecke, für die das gilt (z. B. Segment), sonst trifft ein seitlicher Strahl am Thorax den Arm.</param>
        public static int RayExit(float[] pos, int[] idx, int triCount, float[] o, float[] d, out float tHit, Func<int, bool> accept = null)
        {
            tHit = float.MaxValue; int best = -1;
            for (int t = 0; t < triCount; t++)
            {
                int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
                if (accept != null && !accept(t)) continue;
                var e1 = Sub(pos, b, a); var e2 = Sub(pos, c, a);
                if (Dot(Cross(e1, e2), d) <= 0) continue;          // nur Dreiecke, durch die der Strahl nach außen tritt
                var p = Cross(d, e2); float det = Dot(e1, p);
                if (Math.Abs(det) < 1e-12f) continue;
                float inv = 1 / det;
                var s = new[] { o[0] - pos[3 * a], o[1] - pos[3 * a + 1], o[2] - pos[3 * a + 2] };
                float u = Dot(s, p) * inv; if (u < 0 || u > 1) continue;
                var q = Cross(s, e1); float v = Dot(d, q) * inv; if (v < 0 || u + v > 1) continue;
                float tt = Dot(e2, q) * inv;
                if (tt > 0 && tt < tHit) { tHit = tt; best = t; }
            }
            return best;
        }

        /// <summary>Nachbarschaft der Vertices über die Kanten der Dreiecke.</summary>
        public static List<int>[] Adjacency(int vertexCount, int[] idx, int triCount)
        {
            var adj = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) adj[i] = new List<int>(6);
            for (int t = 0; t < triCount; t++)
                for (int k = 0; k < 3; k++)
                {
                    int a = idx[3 * t + k], b = idx[3 * t + (k + 1) % 3];
                    if (!adj[a].Contains(b)) adj[a].Add(b);
                    if (!adj[b].Contains(a)) adj[b].Add(a);
                }
            return adj;
        }

        public static int DominantSegment(byte[] bones, byte[] w0, int v) => w0[v] >= 128 ? bones[2 * v] : bones[2 * v + 1];

        /// <summary>Ausgangspunkt eines Ankers: Mittelpunkt der Schwerpunkte der genannten Meshes plus Versatz.</summary>
        public static float[] AnchorPoint(AnatomyData data, MarkAnchor a, float scale)
        {
            var c = new float[3]; int k = 0;
            foreach (var name in a.meshes ?? new string[0])
            {
                var m = data.Meshes.FirstOrDefault(x => x.Info.name == name);
                if (m == null) continue;
                var mc = new double[3];
                for (int i = 0; i < m.Positions.Length; i++) mc[i % 3] += m.Positions[i];
                for (int j = 0; j < 3; j++) c[j] += (float)(mc[j] / (m.Positions.Length / 3));
                k++;
            }
            if (k == 0) return null;
            for (int j = 0; j < 3; j++) c[j] = c[j] / k + (a.offset != null && a.offset.Length == 3 ? a.offset[j] / scale : 0);
            return c;
        }

        /// <summary>
        /// Hautbereich eines Zeichens: Strahl vom Anker zur Haut, von dort über die Kanten der Haut ausbreiten, solange
        /// der (ggf. längliche) Abstand unter dem Radius liegt. So greift ein Zeichen am Thorax nicht auf den daneben
        /// liegenden Arm über. triCount: nur die Dreiecke der sichtbaren Haut (ohne Unterhose).
        /// </summary>
        public static SkinPatch Select(AnatomyData data, SkinMark mark, float scale, int triCount, float[] normals, List<int>[] adj)
        {
            var skin = data.Meshes[0];
            var pos = skin.Positions; var idx = skin.Indices;
            var o = AnchorPoint(data, mark.anchor, scale);
            if (o == null) return null;
            HashSet<int> segs = null;
            if (mark.segments != null && mark.segments.Length > 0)
                segs = new HashSet<int>(mark.segments.Select(s => data.SegmentIndex(s)).Where(i => i >= 0));
            var d = Norm(mark.anchor.direction);
            // Strahl gegen die ganze Haut einschließlich Unterhose: liegt der Ort darunter, ist das Zeichen verdeckt (HiddenByShorts)
            int allTris = idx.Length / 3;
            int tri = RayExit(pos, idx, allTris, o, d, out float t,
                              segs == null ? (Func<int, bool>)null : k => segs.Contains(DominantSegment(skin.Bones, skin.Weight0, idx[3 * k])));
            if (tri < 0) return null;
            var hit = new[] { o[0] + d[0] * t, o[1] + d[1] * t, o[2] + d[2] * t };
            int a0 = idx[3 * tri], b0 = idx[3 * tri + 1], c0 = idx[3 * tri + 2];
            var hn = Norm(Cross(Sub(pos, b0, a0), Sub(pos, c0, a0)));
            var patch = new SkinPatch { HitTriangle = tri, HitPoint = hit, HitNormal = hn, HiddenByShorts = tri >= triCount };
            patch.HitRegion = data.Header.segments[DominantSegment(skin.Bones, skin.Weight0, a0)].region;

            float r = mark.radius_m / scale;
            int level = SubdivisionLevel(mark.radius_m / Math.Max(1f, mark.stretch));
            float meanEdge = 0;
            for (int k = 0; k < 3; k++) meanEdge += Len(Sub(pos, idx[3 * tri + k], idx[3 * tri + (k + 1) % 3])) / 3;
            float reach = r + (level > 0 ? 1.5f * meanEdge : 0); // Rand der Auflage, damit die Unterteilung den Radius ganz abdeckt
            float[] axis = null;
            if (mark.stretch > 1)
            {
                if (mark.stretchAxis != null && mark.stretchAxis.Length == 3) axis = Norm(mark.stretchAxis);
                else
                {   // ohne Achse: entlang des getroffenen Segments (Gliedmaße vom Gelenk zum distalen Punkt)
                    var sg = data.Header.segments[DominantSegment(skin.Bones, skin.Weight0, a0)];
                    axis = Norm(new[] { sg.end[0] - sg.joint[0], sg.end[1] - sg.joint[1], sg.end[2] - sg.joint[2] });
                }
            }

            var dist = new Dictionary<int, float>();
            var queue = new Queue<int>();
            foreach (int s in new[] { a0, b0, c0 }) { dist[s] = Metric(pos, s, hit, axis, mark.stretch); queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                foreach (int w in adj[v])
                {
                    if (dist.ContainsKey(w)) continue;
                    // Ausbreitung mit dem einfachen Abstand (sonst reißt sie bei schmalen Zeichen quer zur Achse ab),
                    // die Form entsteht über den gestreckten Abstand
                    if (Metric(pos, w, hit, null, 1) > reach) continue;
                    float m = Metric(pos, w, hit, axis, mark.stretch);
                    if (segs != null && !segs.Contains(DominantSegment(skin.Bones, skin.Weight0, w))) continue;
                    if (!mark.wrap && normals[3 * w] * hn[0] + normals[3 * w + 1] * hn[1] + normals[3 * w + 2] * hn[2] < 0.1f) continue;
                    dist[w] = m; queue.Enqueue(w);
                }
            }
            patch.Vertices = dist.Keys.Where(v => dist[v] <= r).OrderBy(x => x).ToArray();
            patch.Falloff = patch.Vertices.Select(v => Math.Max(0f, 1 - dist[v] / r)).ToArray();
            BuildOverlay(patch, pos, idx, triCount, normals, dist.Keys, hit, axis, mark.stretch, r, level);
            return patch;
        }

        /// <summary>Unterteilungsstufen, damit auch schmale Zeichen (Schnitt, Stich) mehrere Vertices quer haben.</summary>
        public static int SubdivisionLevel(float width_m) => width_m < 0.006f ? 3 : width_m < 0.015f ? 2 : width_m < 0.03f ? 1 : 0;

        static void BuildOverlay(SkinPatch p, float[] pos, int[] idx, int triCount, float[] normals, IEnumerable<int> region,
                                 float[] hit, float[] axis, float stretch, float r, int level)
        {
            var src = new List<int[]>(); var wts = new List<float[]>();
            var local = new Dictionary<int, int>();
            foreach (int v in region) { local[v] = src.Count; src.Add(new[] { v }); wts.Add(new[] { 1f }); }
            var tris = new List<int>();
            for (int k = 0; k < triCount; k++)
            {
                int a = idx[3 * k], b = idx[3 * k + 1], c = idx[3 * k + 2];
                if (local.TryGetValue(a, out int la) && local.TryGetValue(b, out int lb) && local.TryGetValue(c, out int lc))
                { tris.Add(la); tris.Add(lb); tris.Add(lc); }
            }
            for (int l = 0; l < level; l++)
            {
                var mid = new Dictionary<long, int>();
                Func<int, int, int> M = (i, j) =>
                {
                    long key = (long)Math.Min(i, j) << 32 | (uint)Math.Max(i, j);
                    if (mid.TryGetValue(key, out int m)) return m;
                    var acc = new Dictionary<int, float>();
                    for (int q = 0; q < src[i].Length; q++) acc[src[i][q]] = (acc.TryGetValue(src[i][q], out var x) ? x : 0) + wts[i][q] / 2;
                    for (int q = 0; q < src[j].Length; q++) acc[src[j][q]] = (acc.TryGetValue(src[j][q], out var x) ? x : 0) + wts[j][q] / 2;
                    m = src.Count; src.Add(acc.Keys.ToArray()); wts.Add(acc.Values.ToArray()); mid[key] = m;
                    return m;
                };
                var nt = new List<int>(tris.Count * 4);
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    int ab = M(a, b), bc = M(b, c), ca = M(c, a);
                    nt.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                }
                tris = nt;
            }
            int n = src.Count;
            var P = new float[3 * n]; var N = new float[3 * n]; var F = new float[n];
            for (int i = 0; i < n; i++)
            {
                for (int q = 0; q < src[i].Length; q++)
                    for (int j = 0; j < 3; j++) { P[3 * i + j] += pos[3 * src[i][q] + j] * wts[i][q]; N[3 * i + j] += normals[3 * src[i][q] + j] * wts[i][q]; }
                var nn = Norm(new[] { N[3 * i], N[3 * i + 1], N[3 * i + 2] });
                N[3 * i] = nn[0]; N[3 * i + 1] = nn[1]; N[3 * i + 2] = nn[2];
                F[i] = Math.Max(0f, 1 - Metric(P, i, hit, axis, stretch) / r);
            }
            // nur Dreiecke mit sichtbarem Anteil, Vertices verdichten
            var keep = new List<int>();
            for (int t = 0; t < tris.Count; t += 3)
                if (F[tris[t]] > 0 || F[tris[t + 1]] > 0 || F[tris[t + 2]] > 0) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            var map = new Dictionary<int, int>();
            var order = new List<int>();
            for (int k = 0; k < keep.Count; k++)
            {
                if (!map.TryGetValue(keep[k], out int ni)) { ni = order.Count; map[keep[k]] = ni; order.Add(keep[k]); }
                keep[k] = ni;
            }
            p.Subdivisions = level;
            p.Triangles = keep.ToArray();
            p.Src = new int[3 * order.Count]; p.W = new float[3 * order.Count];
            p.Pos = new float[3 * order.Count]; p.Nrm = new float[3 * order.Count]; p.RFalloff = new float[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int o = order[i];
                for (int q = 0; q < 3; q++)
                {
                    p.Src[3 * i + q] = q < src[o].Length ? src[o][q] : src[o][0];
                    p.W[3 * i + q] = q < src[o].Length ? wts[o][q] : 0;
                }
                for (int j = 0; j < 3; j++) { p.Pos[3 * i + j] = P[3 * o + j]; p.Nrm[3 * i + j] = N[3 * o + j]; }
                p.RFalloff[i] = F[o];
            }
        }

        static float Metric(float[] pos, int v, float[] c, float[] axis, float stretch)
        {
            float dx = pos[3 * v] - c[0], dy = pos[3 * v + 1] - c[1], dz = pos[3 * v + 2] - c[2];
            float d2 = dx * dx + dy * dy + dz * dz;
            if (axis != null)
            {
                // Radius = halbe Länge entlang der Achse, quer davon um stretch schmaler
                float along = dx * axis[0] + dy * axis[1] + dz * axis[2];
                d2 = along * along + (d2 - along * along) * stretch * stretch;
            }
            return (float)Math.Sqrt(Math.Max(0, d2));
        }

        // ---------------------------------------------------------------- Amputation

        /// <summary>Ergebnis einer Amputation: verbleibende Hautdreiecke und der Stumpf als eigenes Mesh.</summary>
        public class Amputation
        {
            public float[] PlanePoint, PlaneNormal;
            public int[] KeptTriangles;      // Indizes der sichtbaren Haut (Teilmesh 0) ohne die entfernten Dreiecke
            public int RemovedTriangles;
            public float[] CapPositions;     // 3·n, Modelleinheiten
            public float[] CapFalloff;       // 1 Mitte (Knochen) … 0 Hautrand
            public int[] CapSource;          // Hautvertex, dessen Gewichte der Stumpfvertex übernimmt
            public int[] CapTriangles;
            public int BoundaryEdges;
        }

        /// <summary>Anteil des Stumpfradius, den Knochen bzw. Knochen und Übergang einnehmen (Darstellung).</summary>
        public const float StumpBone = 0.22f, StumpBoneEdge = 0.27f;

        /// <summary>
        /// Entfernt die Hautdreiecke distal der Ebene (alle drei Vertices distal und in den genannten Segmenten) und
        /// schließt den Rand mit einem Stumpf: Ringe vom Rand zur Mitte, innen Knochen, außen Muskulatur.
        /// Jede Randkante wird umgekehrt übernommen, damit der Stumpf nach distal zeigt (geschlossene Fläche).
        /// </summary>
        public static Amputation Amputate(AnatomyData data, SkinMark mark, float scale, int triCount)
        {
            var skin = data.Meshes[0]; var pos = skin.Positions; var idx = skin.Indices;
            var o = AnchorPoint(data, mark.anchor, scale);
            if (o == null) return null;
            var nrm = Norm(mark.anchor.direction);
            HashSet<int> segs = mark.segments != null && mark.segments.Length > 0
                ? new HashSet<int>(mark.segments.Select(x => data.SegmentIndex(x)).Where(i => i >= 0)) : null;
            Func<int, bool> distal = v =>
                (pos[3 * v] - o[0]) * nrm[0] + (pos[3 * v + 1] - o[1]) * nrm[1] + (pos[3 * v + 2] - o[2]) * nrm[2] > 0 &&
                (segs == null || segs.Contains(DominantSegment(skin.Bones, skin.Weight0, v)));
            var removed = new bool[triCount];
            var kept = new List<int>();
            for (int t = 0; t < triCount; t++)
            {
                int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
                removed[t] = distal(a) && distal(b) && distal(c);
                if (!removed[t]) { kept.Add(a); kept.Add(b); kept.Add(c); }
            }
            // Kanten → Dreiecke
            var edgeTris = new Dictionary<long, List<int>>();
            for (int t = 0; t < triCount; t++)
                for (int k = 0; k < 3; k++)
                {
                    int a = idx[3 * t + k], b = idx[3 * t + (k + 1) % 3];
                    long key = (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
                    if (!edgeTris.TryGetValue(key, out var l)) edgeTris[key] = l = new List<int>(2);
                    l.Add(t);
                }
            var boundary = new List<int[]>(); // (a, b) in der Richtung des verbleibenden Dreiecks
            for (int t = 0; t < triCount; t++)
            {
                if (removed[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int a = idx[3 * t + k], b = idx[3 * t + (k + 1) % 3];
                    long key = (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
                    if (edgeTris[key].Any(u => u != t && removed[u])) boundary.Add(new[] { a, b });
                }
            }
            var res = new Amputation { PlanePoint = o, PlaneNormal = nrm, KeptTriangles = kept.ToArray(),
                                       RemovedTriangles = triCount - kept.Count / 3, BoundaryEdges = boundary.Count };
            if (boundary.Count == 0) { res.CapPositions = new float[0]; res.CapFalloff = new float[0]; res.CapSource = new int[0]; res.CapTriangles = new int[0]; return res; }

            var rim = boundary.SelectMany(e => e).Distinct().ToArray();
            var center = new float[3];
            foreach (int v in rim) for (int j = 0; j < 3; j++) center[j] += pos[3 * v + j] / rim.Length;
            float[] rings = { 0f, 1 - StumpBoneEdge, 1 - StumpBone, 1 - StumpBone * 0.5f }; // Anteil Weg vom Rand zur Mitte
            var cp = new List<float>(); var cf = new List<float>(); var cs = new List<int>(); var ct = new List<int>();
            var ringIndex = new Dictionary<int, int>(); // Randvertex → erster Index seiner Ringvertices
            foreach (int v in rim)
            {
                ringIndex[v] = cs.Count;
                foreach (float r in rings)
                {
                    for (int j = 0; j < 3; j++) cp.Add(pos[3 * v + j] + (center[j] - pos[3 * v + j]) * r);
                    cf.Add(r); cs.Add(v);
                }
            }
            int ci = cs.Count;
            cp.AddRange(center); cf.Add(1); cs.Add(rim[0]);
            int R = rings.Length;
            foreach (var e in boundary)
            {
                int a = ringIndex[e[0]], b = ringIndex[e[1]];
                for (int r = 0; r < R - 1; r++)
                {   // umgekehrte Richtung b → a
                    ct.Add(b + r); ct.Add(a + r); ct.Add(a + r + 1);
                    ct.Add(b + r); ct.Add(a + r + 1); ct.Add(b + r + 1);
                }
                ct.Add(b + R - 1); ct.Add(a + R - 1); ct.Add(ci);
            }
            res.CapPositions = cp.ToArray(); res.CapFalloff = cf.ToArray(); res.CapSource = cs.ToArray(); res.CapTriangles = ct.ToArray();
            return res;
        }

        /// <summary>Hautvertices, die nach einer Amputation in keinem verbleibenden Dreieck mehr vorkommen.</summary>
        public static HashSet<int> RemovedVertices(int[] indices, int triCount, int[] keptTriangles)
        {
            var kept = new HashSet<int>(keptTriangles);
            var all = new HashSet<int>();
            for (int i = 0; i < 3 * triCount; i++) all.Add(indices[i]);
            all.ExceptWith(kept);
            return all;
        }

        /// <summary>Entfernt Dreiecke der Auflage, deren Ecken überwiegend auf amputierter Haut liegen.</summary>
        public static void DropRemoved(SkinPatch p, HashSet<int> removed)
        {
            if (removed == null || removed.Count == 0 || p.Triangles == null) return;
            Func<int, bool> gone = i =>
            {
                float w = 0;
                for (int q = 0; q < 3; q++) if (p.W[3 * i + q] > 0 && removed.Contains(p.Src[3 * i + q])) w += p.W[3 * i + q];
                return w > 0.5f;
            };
            var t = new List<int>();
            for (int k = 0; k < p.Triangles.Length; k += 3)
                if (!gone(p.Triangles[k]) && !gone(p.Triangles[k + 1]) && !gone(p.Triangles[k + 2]))
                { t.Add(p.Triangles[k]); t.Add(p.Triangles[k + 1]); t.Add(p.Triangles[k + 2]); }
            p.Triangles = t.ToArray();
        }

        /// <summary>Farben des Stumpfs: innen Knochen (fragmentColor der Art), außen Muskulatur bis zum Hautrand.</summary>
        public static float[] StumpColors(MarkKind k, float[] falloff)
        {
            var col = new float[4 * falloff.Length];
            for (int i = 0; i < falloff.Length; i++)
            {
                float f = falloff[i];
                float[] c = new float[3];
                bool bone = f >= 1 - StumpBone - 1e-4f && k.fragmentColor != null && k.fragmentColor.Length == 3;
                for (int j = 0; j < 3; j++)
                    c[j] = bone ? k.fragmentColor[j] : k.colorEdge[j] + (k.colorCenter[j] - k.colorEdge[j]) * Math.Min(1, f / (1 - StumpBoneEdge));
                col[4 * i] = c[0]; col[4 * i + 1] = c[1]; col[4 * i + 2] = c[2]; col[4 * i + 3] = 1;
            }
            return col;
        }

        /// <summary>Liegt ein Anatomieteil (Schwerpunkt) distal der Amputationsebene in einem der Segmente?</summary>
        public static bool IsDistal(AnatomyMeshData m, Amputation a, HashSet<int> segs)
        {
            if (segs != null && !segs.Contains(m.Info.segment) && !(m.Info.weights && m.Bones != null)) return false;
            var c = new double[3];
            for (int i = 0; i < m.Positions.Length; i++) c[i % 3] += m.Positions[i];
            int n = m.Positions.Length / 3;
            double d = 0;
            for (int j = 0; j < 3; j++) d += (c[j] / n - a.PlanePoint[j]) * a.PlaneNormal[j];
            if (d <= 0) return false;
            if (segs == null || !(m.Info.weights && m.Bones != null)) return true;
            // gewichtete Teile (Muskeln): überwiegend in den Segmenten
            int inside = 0;
            for (int v = 0; v < n; v++) if (segs.Contains(DominantSegment(m.Bones, m.Weight0, v))) inside++;
            return inside * 2 > n;
        }

        // ---------------------------------------------------------------- Aussehen

        /// <summary>Farbe (RGBA) je Patch-Vertex: zur Mitte dunkler; offene Fraktur mit Knochenfragmenten.
        /// Alpha = Deckkraft; der Shader blendet zusätzlich mit dem Wachstum und unregelmäßigem Rand.</summary>
        public static float[] Colors(MarkKind k, SkinPatch p)
        {
            int n = p.RFalloff.Length;
            var col = new float[4 * n];
            for (int i = 0; i < n; i++)
            {
                float f = p.RFalloff[i];
                float s = (float)Math.Pow(f, 0.7);
                float[] c = new float[3];
                for (int j = 0; j < 3; j++) c[j] = k.colorEdge[j] + (k.colorCenter[j] - k.colorEdge[j]) * s;
                if (k.fragmentColor != null && k.fragmentColor.Length == 3 && k.fragmentShare > 0 && f > 0.45f &&
                    Hash(p.Pos[3 * i], p.Pos[3 * i + 1], p.Pos[3 * i + 2]) < k.fragmentShare)
                    c = (float[])k.fragmentColor.Clone();
                col[4 * i] = c[0]; col[4 * i + 1] = c[1]; col[4 * i + 2] = c[2];
                col[4 * i + 3] = k.opacity * Smooth(Math.Min(1, f * 2.5f)); // weicher Rand
            }
            return col;
        }

        /// <summary>Deterministischer Zufallswert 0..1 aus einer Position (Zelle von 4 Modell-mm, damit Fragmente
        /// als kleine Flecken statt als einzelne Punkte erscheinen).</summary>
        public static float Hash(float x, float y, float z)
        {
            unchecked
            {
                uint h = (uint)(int)Math.Floor(x * 250) * 73856093u ^ (uint)(int)Math.Floor(y * 250) * 19349663u ^ (uint)(int)Math.Floor(z * 250) * 83492791u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public static float Smooth(float f) { f = Math.Max(0, Math.Min(1, f)); return f * f * (3 - 2 * f); }

        /// <summary>Wie weit das Zeichen gerade ausgeprägt ist (0 unsichtbar … 1 voll): Bedingung wie ein
        /// Untersuchungsbefund, Wachstum mit dem Blutverlust der Verletzung.</summary>
        public static double Amount(SkinMark m, ExamContext ctx)
        {
            if (m.when != null && !Examination.Applies(m.when, m.injuryId, ctx)) return 0;
            if (m.growToLost_mL > m.growFromLost_mL)
            {
                double lost = ctx.Lost_mL != null ? ctx.Lost_mL(m.injuryId) : 0;
                return Math.Max(0, Math.Min(1, (lost - m.growFromLost_mL) / (m.growToLost_mL - m.growFromLost_mL)));
            }
            return 1;
        }

        // ---------------------------------------------------------------- Schwellung

        /// <summary>
        /// Verschiebung der Haut entlang der Normalen (Modelleinheiten, je Hautvertex) für ein Schwellungsvolumen:
        /// Profil = Smoothstep des Abfalls, so skaliert, dass Σ Fläche · Verschiebung = Volumen (skaliert auf die Körpergröße).
        /// </summary>
        public static float[] SwellingDeltas(SkinPatch p, float[] normals, float[] areas, int vertexCount, double volume_mL, float scale)
        {
            var deltas = new float[3 * vertexCount];
            double sum = 0;
            for (int i = 0; i < p.Vertices.Length; i++) sum += areas[p.Vertices[i]] * Smooth(p.Falloff[i]);
            if (sum <= 0 || volume_mL <= 0) return deltas;
            double vModel = volume_mL * 1e-6 / Math.Pow(scale, 3);
            double k = vModel / sum;
            for (int i = 0; i < p.Vertices.Length; i++)
            {
                int v = p.Vertices[i]; float h = (float)(k * Smooth(p.Falloff[i]));
                deltas[3 * v] = normals[3 * v] * h; deltas[3 * v + 1] = normals[3 * v + 1] * h; deltas[3 * v + 2] = normals[3 * v + 2] * h;
            }
            return deltas;
        }

        /// <summary>Volumen eines Verschiebungsfelds (Σ Fläche · Normalverschiebung) in mL bei Körperskalierung scale.</summary>
        public static double DisplacedVolume_mL(float[] deltas, float[] normals, float[] areas, float scale)
        {
            double s = 0;
            for (int v = 0; v < areas.Length; v++)
                s += areas[v] * (deltas[3 * v] * normals[3 * v] + deltas[3 * v + 1] * normals[3 * v + 1] + deltas[3 * v + 2] * normals[3 * v + 2]);
            return s * Math.Pow(scale, 3) * 1e6;
        }

        // ---------------------------------------------------------------- Blutlache und Blutstrahl

        /// <summary>Dicke einer ausgebreiteten Lache (Pfütze): e = 2·κ⁻¹·sin(θ/2), κ⁻¹ = √(γ/(ρ·g)) (de Gennes 2004).</summary>
        public static double PoolThickness_m(BloodPoolParams p)
        {
            double capillary = Math.Sqrt(p.surfaceTension_N_m / (p.density_kg_m3 * 9.81));
            return 2 * capillary * Math.Sin(p.contactAngle_deg * Math.PI / 360);
        }

        /// <summary>Eintrocknung 0 (frisch) … 1 (trocken) nach der Zeit seit dem letzten frischen Blut.</summary>
        public static double Dryness(DryingParams p, double sinceWet_s) =>
            p == null || p.wetToDry_min <= 0 ? 0 : Math.Max(0, Math.Min(1, sinceWet_s / (p.wetToDry_min * 60)));

        /// <summary>Radius einer runden Lache aus dem Volumen: V = π r² e.</summary>
        public static double PoolRadius_m(double volume_mL, double thickness_m) =>
            volume_mL <= 0 ? 0 : Math.Sqrt(volume_mL * 1e-6 / (Math.PI * thickness_m));

        /// <summary>Austrittsgeschwindigkeit eines Strahls aus dem Druck (Torricelli/Bernoulli): v = Cv·√(2·Δp/ρ).</summary>
        public static double JetSpeed_m_s(double pressure_mmHg, double density_kg_m3, double cv) =>
            pressure_mmHg <= 0 ? 0 : cv * Math.Sqrt(2 * pressure_mmHg * 133.322 / density_kg_m3);

        // ---------------------------------------------------------------- Vektoren

        static float[] Sub(float[] p, int a, int b) => new[] { p[3 * a] - p[3 * b], p[3 * a + 1] - p[3 * b + 1], p[3 * a + 2] - p[3 * b + 2] };
        static float[] Cross(float[] a, float[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
        static float Dot(float[] a, float[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        static float Len(float[] a) => (float)Math.Sqrt(Dot(a, a));
        static float[] Norm(float[] a) { float l = Len(a); return l > 0 ? new[] { a[0] / l, a[1] / l, a[2] / l } : new float[] { 0, 0, 1 }; }
    }
}

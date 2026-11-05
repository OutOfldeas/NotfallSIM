using System;
using System.IO;
using NotfallSIM.Crash;
using UnityEngine;

namespace NotfallSIM.Anatomy
{
    /// <summary>
    /// Sichtbarer Körperbau: Die Haut wird je Region um das Mehr (bzw. Weniger) an Unterhautfett gegenüber dem
    /// Bezugs-BMI entlang der Hautnormalen verschoben (Blendshape „Koerperfett“). Dicken aus impact_model.json
    /// (<see cref="ImpactModel.FatExtra_m"/>, Gibney 2010, Inaba 2012, Mahawongkajit 2026). Organe bleiben, wo sie sind —
    /// im Röntgenblick liegt das Fett zwischen Haut und Muskel.
    /// Darstellungsannahmen: Bauch vorn zusätzlich vorgewölbt (viszerales Fett, Faktor bis 4), Hände und Füße 30 %,
    /// Kopf ohne Fett, Abnahme bei schlanken Patienten höchstens 3 mm.
    /// </summary>
    public static class BodyFatShape
    {
        static ImpactModelFile _model;
        static bool _tried;

        public static ImpactModelFile Model()
        {
            if (_model != null || _tried) return _model;
            _tried = true;
            string path = Path.Combine(Data.InjuryDatabase.Root, "impact_model.json");
            if (File.Exists(path)) _model = JsonUtility.FromJson<ImpactModelFile>(File.ReadAllText(path));
            return _model;
        }

        /// <summary>Fett über dem Bezug (m) für eine Region des Anatomiemodells; 0 ohne Daten.</summary>
        public static double Extra_m(string region, double bmi, bool male)
        {
            var m = Model();
            if (m == null || m.fat == null || bmi <= 0) return 0;
            return Math.Max(-0.003, ImpactModel.FatExtra_m(m, region, bmi, male));
        }

        /// <summary>
        /// Verschiebung je Hautecke (Modellkoordinaten, x rechts, y oben, z vorn). Reines Rechnen, ohne Unity-Objekte.
        /// <paramref name="unitsPerMeter"/>: Modelleinheiten je Meter der Mesh-Ecken.
        /// </summary>
        public static Vector3[] Deltas(AnatomyData data, AnatomyMeshData skin, Vector3[] normals, double bmi, bool male, float unitsPerMeter)
        {
            int n = skin.Info.vertexCount;
            var d = new Vector3[n];
            if (bmi <= 0 || Model()?.fat == null) return d;
            var segs = data.Header.segments;
            var extra = new float[segs.Length];
            for (int i = 0; i < segs.Length; i++)
            {
                double ex = Extra_m(segs[i].region ?? segs[i].name, bmi, male);
                string name = segs[i].name ?? "";
                if (name.StartsWith("Hand") || name.StartsWith("Fuß")) ex *= 0.3;
                extra[i] = (float)ex * unitsPerMeter;
            }
            // Dicke je Ecke aus beiden Gelenkgewichten (weiche Übergänge), Bauch vorn verstärkt, dann über die Nachbarn geglättet
            var e = new float[n];
            for (int v = 0; v < n; v++)
            {
                int b0 = skin.Bones[2 * v], b1 = skin.Bones[2 * v + 1];
                float w0 = skin.Weight0[v] / 255f;
                float x0 = b0 < segs.Length ? extra[b0] : 0, x1 = b1 < segs.Length ? extra[b1] : 0;
                float abd = (b0 < segs.Length && segs[b0].region == "Abdomen" ? w0 : 0) + (b1 < segs.Length && segs[b1].region == "Abdomen" ? 1 - w0 : 0);
                float ev = w0 * x0 + (1 - w0) * x1;
                if (ev > 0) ev *= 1 + 3f * abd * Mathf.Max(0, normals[v].z);   // viszerales Fett wölbt nach vorn (Darstellungsannahme)
                e[v] = ev;
            }
            var adj = SkinMarks.Adjacency(n, skin.Indices, skin.Indices.Length / 3);
            var tmp = new float[n];
            for (int it = 0; it < 12; it++)
            {
                for (int v = 0; v < n; v++)
                {
                    var nb = adj[v];
                    if (nb == null || nb.Count == 0) { tmp[v] = e[v]; continue; }
                    float sum = 0;
                    for (int j = 0; j < nb.Count; j++) sum += e[nb[j]];
                    tmp[v] = 0.5f * e[v] + 0.5f * sum / nb.Count;
                }
                var sw = e; e = tmp; tmp = sw;
            }
            for (int v = 0; v < n; v++) d[v] = normals[v] * e[v];
            return d;
        }

        /// <summary>Blendshape „Koerperfett“ anlegen und voll einschalten (nichts, wenn der BMI fehlt oder kein Fettmodell da ist).</summary>
        public static void Apply(SkinnedMeshRenderer smr, AnatomyData data, AnatomyMeshData skin, double bmi, bool male, float unitsPerMeter)
        {
            if (smr == null || smr.sharedMesh == null || bmi <= 0 || Model()?.fat == null) return;
            var mesh = smr.sharedMesh;
            var deltas = Deltas(data, skin, mesh.normals, bmi, male, unitsPerMeter);
            mesh.AddBlendShapeFrame("Koerperfett", 100f, deltas, null, null);
            mesh.RecalculateBounds();
            smr.SetBlendShapeWeight(mesh.blendShapeCount - 1, 100f);
        }
    }
}

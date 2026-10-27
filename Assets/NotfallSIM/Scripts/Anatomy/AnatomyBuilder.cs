using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace NotfallSIM.Anatomy
{
    /// <summary>Ein aufgebauter anatomischer Körper: Skelett-Transforms je Segment, Haut und optional innere Anatomie.</summary>
    public class AnatomyRig
    {
        public AnatomyData Data;
        public GameObject Root;
        public Transform[] Bones;            // Index wie Header.segments
        public SkinnedMeshRenderer Skin;
        public readonly List<AnatomyPart> Parts = new List<AnatomyPart>();
        public float Scale;

        public Transform Bone(string segment) => Bones[Data.SegmentIndex(segment)];
    }

    public class AnatomyPart
    {
        public AnatomyMeshInfo Info;
        public MeshRenderer Renderer;
        public MeshFilter Filter;
    }

    /// <summary>
    /// Baut aus body.bin (BodyParts3D, aufbereitet von tools/anatomy/build_anatomy.py) Unity-Objekte:
    /// Segment-Transforms in Ruhelage (aufrecht, Arme seitlich), die Haut als SkinnedMesh mit Atmungs-Blendshapes
    /// und die inneren Strukturen als MeshRenderer an ihrem Segment.
    /// </summary>
    public static class AnatomyBuilder
    {
        static AnatomyData _cache;

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, "NotfallSIM", "anatomy", "body.bin");
        public static bool Available => File.Exists(FilePath);

        public static AnatomyData Load()
        {
            if (_cache == null)
                _cache = AnatomyFile.Read(File.ReadAllBytes(FilePath), JsonUtility.FromJson<AnatomyHeader>);
            return _cache;
        }

        public static Vector3 V(float[] a) => new Vector3(a[0], a[1], a[2]);

        /// <param name="height_m">Körpergröße; das Modell wird gleichmäßig skaliert.</param>
        /// <param name="withAnatomy">Innere Strukturen mit aufbauen (Röntgenblick).</param>
        public static AnatomyRig Build(Transform parent, float height_m, bool withAnatomy, Material skin, Material shorts,
                                       System.Func<AnatomyMeshInfo, Material> anatomyMaterial = null)
        {
            var data = Load();
            var h = data.Header;
            var rig = new AnatomyRig { Data = data, Scale = height_m / h.modelHeight, Root = new GameObject("Körper") };
            rig.Root.transform.SetParent(parent, false);
            rig.Root.transform.localScale = Vector3.one * rig.Scale;

            // Skelett in Ruhelage: Ursprung je Segment am proximalen Gelenk, Achsen = Körperachsen
            rig.Bones = new Transform[h.segments.Length];
            for (int i = 0; i < h.segments.Length; i++)
            {
                var s = h.segments[i];
                var t = new GameObject(s.name).transform;
                int p = string.IsNullOrEmpty(s.parent) ? -1 : data.SegmentIndex(s.parent);
                t.SetParent(p < 0 ? rig.Root.transform : rig.Bones[p], false);
                t.localPosition = V(s.joint) - (p < 0 ? Vector3.zero : V(h.segments[p].joint));
                t.localRotation = Quaternion.identity;
                rig.Bones[i] = t;
            }

            foreach (var md in data.Meshes)
            {
                if (md.Info.layer == "skin") rig.Skin = BuildSkin(rig, md, skin, shorts);
                else if (withAnatomy)
                {
                    var seg = rig.Bones[Mathf.Clamp(md.Info.segment, 0, rig.Bones.Length - 1)];
                    var go = new GameObject(md.Info.label);
                    go.transform.SetParent(seg, false);
                    go.transform.localPosition = -V(h.segments[Mathf.Clamp(md.Info.segment, 0, rig.Bones.Length - 1)].joint);
                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = ToMesh(md);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = anatomyMaterial != null ? anatomyMaterial(md.Info) : null;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    rig.Parts.Add(new AnatomyPart { Info = md.Info, Renderer = mr, Filter = mf });
                }
            }
            return rig;
        }

        static Mesh ToMesh(AnatomyMeshData md)
        {
            int n = md.Info.vertexCount;
            var v = new Vector3[n];
            for (int i = 0; i < n; i++) v[i] = new Vector3(md.Positions[3 * i], md.Positions[3 * i + 1], md.Positions[3 * i + 2]);
            var mesh = new Mesh { name = md.Info.name };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v;
            var sub = md.Info.submeshes != null && md.Info.submeshes.Length > 0 ? md.Info.submeshes : new[] { md.Indices.Length };
            mesh.subMeshCount = sub.Length;
            int offset = 0;
            for (int s = 0; s < sub.Length; s++)
            {
                var tri = new int[sub[s]];
                System.Array.Copy(md.Indices, offset, tri, 0, sub[s]);
                mesh.SetTriangles(tri, s);
                offset += sub[s];
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static SkinnedMeshRenderer BuildSkin(AnatomyRig rig, AnatomyMeshData md, Material skin, Material shorts)
        {
            var mesh = ToMesh(md);
            int n = md.Info.vertexCount;
            var w = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                float w0 = md.Weight0[i] / 255f;
                w[i] = new BoneWeight { boneIndex0 = md.Bones[2 * i], weight0 = w0, boneIndex1 = md.Bones[2 * i + 1], weight1 = 1 - w0 };
            }
            mesh.boneWeights = w;
            var bind = new Matrix4x4[rig.Bones.Length];
            var rootM = rig.Root.transform.localToWorldMatrix;
            for (int i = 0; i < bind.Length; i++) bind[i] = rig.Bones[i].worldToLocalMatrix * rootM;
            mesh.bindposes = bind;
            for (int s = 0; s < md.BlendShapes.Count; s++)
            {
                var d = md.BlendShapes[s];
                var deltas = new Vector3[n];
                for (int i = 0; i < n; i++) deltas[i] = new Vector3(d[3 * i], d[3 * i + 1], d[3 * i + 2]);
                mesh.AddBlendShapeFrame(md.Info.blendShapes[s], 100f, deltas, null, null);
            }

            var go = new GameObject("Haut");
            go.transform.SetParent(rig.Root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = rig.Bones;
            smr.rootBone = rig.Bones[0];
            smr.sharedMaterials = mesh.subMeshCount > 1 ? new[] { skin, shorts } : new[] { skin };
            smr.updateWhenOffscreen = true;
            return smr;
        }

        /// <summary>
        /// Haut für fremde Knochen (z. B. die Ragdoll-Segmente): Ecken auf Körpergröße skaliert, Bindposen = Ruhelage
        /// (Segmentursprung am Gelenk, Körperachsen). Die Knochen müssen in der Reihenfolge von Header.segments vorliegen.
        /// </summary>
        public static SkinnedMeshRenderer BuildSkinForBones(Transform parent, Transform[] bones, float scale, Material skin, Material shorts)
        {
            var data = Load();
            var md = data.Meshes.Find(m => m.Info.layer == "skin");
            var mesh = ToMesh(md);
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i] *= scale;
            mesh.vertices = v;
            int n = md.Info.vertexCount;
            var w = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                float w0 = md.Weight0[i] / 255f;
                w[i] = new BoneWeight { boneIndex0 = md.Bones[2 * i], weight0 = w0, boneIndex1 = md.Bones[2 * i + 1], weight1 = 1 - w0 };
            }
            mesh.boneWeights = w;
            var bind = new Matrix4x4[bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-V(data.Header.segments[i].joint) * scale);
            mesh.bindposes = bind;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("Haut");
            go.transform.SetParent(parent, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterials = mesh.subMeshCount > 1 ? new[] { skin, shorts } : new[] { skin };
            smr.updateWhenOffscreen = true;
            return smr;
        }

        /// <summary>URP-Lit-Material (zur Laufzeit), Fallback Standard-Shader.</summary>
        public static Material Lit(Color color, float smoothness, bool doubleSided = false)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = color };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (doubleSided && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0);
            return m;
        }
    }
}

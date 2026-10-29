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
        public Renderer Renderer;               // MeshRenderer (starr am Segment) oder SkinnedMeshRenderer (Muskeln)
        public MeshFilter Filter;               // nur bei starren Teilen
        public SkinnedMeshRenderer Skinned;     // Muskeln über Gelenke: wie die Haut gewichtet
        public MeshCollider Collider;   // nur im Röntgenblick, für den Namen unter der Maus
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
                else if (withAnatomy && md.Info.weights && md.Bones != null)
                {
                    var smr = BuildWeighted(rig, md, md.Info.label, anatomyMaterial != null ? anatomyMaterial(md.Info) : null);
                    smr.shadowCastingMode = ShadowCastingMode.Off;
                    rig.Parts.Add(new AnatomyPart { Info = md.Info, Renderer = smr, Skinned = smr });
                }
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

        /// <summary>Gewichtetes Teil (z. B. Muskel über ein Gelenk) als SkinnedMeshRenderer an den Segmenten des Rigs.</summary>
        static SkinnedMeshRenderer BuildWeighted(AnatomyRig rig, AnatomyMeshData md, string name, Material material)
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
            var go = new GameObject(name);
            go.transform.SetParent(rig.Root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = rig.Bones;
            smr.rootBone = rig.Bones[0];
            smr.sharedMaterial = material;
            smr.updateWhenOffscreen = true;
            return smr;
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
            mesh.SetUVs(0, new List<Vector3>(mesh.vertices)); // Ruhelage für den Haut-Shader (Poren, Marmorierung)

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
            mesh.SetUVs(0, new List<Vector3>(v)); // Ruhelage in Modelleinheiten für den Haut-Shader
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
        static SkinParams _skinParams;
        static bool _skinTried;

        /// <summary>Hautdaten (StreamingAssets/NotfallSIM/skin.json); null, wenn die Datei fehlt.</summary>
        public static SkinParams SkinData
        {
            get
            {
                if (_skinTried) return _skinParams;
                _skinTried = true;
                string path = Path.Combine(Application.streamingAssetsPath, "NotfallSIM", "skin.json");
                try { if (File.Exists(path)) _skinParams = JsonUtility.FromJson<SkinParams>(File.ReadAllText(path)); }
                catch (System.Exception e) { Debug.LogWarning("[Haut] skin.json nicht lesbar: " + e.Message); }
                return _skinParams;
            }
        }

        static Color C(float[] c, Color fallback) => c != null && c.Length >= 3 ? new Color(c[0], c[1], c[2]) : fallback;

        /// <summary>Haut-Material mit Streuung, Poren und Durchblutung (Shader NotfallSIM/Skin); ohne Shader URP/Lit.</summary>
        public static Material SkinMaterial(Color fallbackTone)
        {
            var sh = Resources.Load<Shader>("Skin") ?? Shader.Find("NotfallSIM/Skin");
            var p = SkinData;
            if (sh == null || !sh.isSupported || p == null) return Lit(fallbackTone, 0.38f);
#if UNITY_EDITOR
            // Shader mit Kompilierfehler würde pink erscheinen: dann lieber URP/Lit in Hautfarbe (Fehler steht in der Konsole)
            if (UnityEditor.ShaderUtil.ShaderHasError(sh)) { Debug.LogWarning("[Haut] Shader NotfallSIM/Skin hat Fehler — Ersatz URP/Lit."); return Lit(fallbackTone, 0.38f); }
#endif
            var m = new Material(sh);
            m.SetColor("_BaseColor", C(p.toneColor, fallbackTone));
            m.SetColor("_PaleColor", C(p.paleColor, fallbackTone));
            m.SetColor("_CyanColor", C(p.cyanoticColor, fallbackTone));
            m.SetColor("_MottleColor", C(p.mottleColor, fallbackTone));
            if (p.wrap != null && p.wrap.Length >= 3) m.SetVector("_Wrap", new Vector4(p.wrap[0], p.wrap[1], p.wrap[2], 0));
            if (p.roughness != null && p.roughness.Length >= 2) m.SetVector("_Rough", new Vector4(p.roughness[0], p.roughness[1], 0, 0));
            if (p.skinF0 > 0) m.SetFloat("_F0", p.skinF0);
            if (p.poreScale > 0) m.SetFloat("_PoreScale", p.poreScale);
            m.SetFloat("_PoreStrength", p.poreStrength);
            // Bezugspunkte in Modellkoordinaten: Knie (Marmorierung), Gesicht und Hände (Zyanose)
            var h = Load().Header;
            System.Func<string, Vector3> J = name => { foreach (var j in h.joints) if (j.name == name) return V(j.pos); return Vector3.zero; };
            m.SetVector("_KneeR", J("Knie rechts")); m.SetVector("_KneeL", J("Knie links"));
            var top = J("Scheitel");
            m.SetVector("_Head", top + new Vector3(0, -0.12f, 0.08f));
            m.SetVector("_HandR", (J("Handgelenk rechts") + J("Fingerspitze rechts")) / 2);
            m.SetVector("_HandL", (J("Handgelenk links") + J("Fingerspitze links")) / 2);
            return m;
        }

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

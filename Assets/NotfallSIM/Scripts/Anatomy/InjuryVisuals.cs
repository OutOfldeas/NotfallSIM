using System.Collections.Generic;
using System.IO;
using System.Linq;
using NotfallSIM.Data;
using NotfallSIM.Simulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace NotfallSIM.Anatomy
{
    /// <summary>
    /// Sichtbare Verletzungszeichen am Patienten (injury_visuals.json): Prellmarken, Hämatome, Wunden als Auflagen auf der
    /// Haut, Schwellungen als Blendshapes der Haut (Volumen aus dem Blutverlust), Amputationen (Haut distal entfernt,
    /// Stumpf), äußere Blutungen mit Blutstrahl im Herzrhythmus und wachsender Blutlache auf Trage und Boden.
    /// Geometrie und Mengen kommen aus <see cref="SkinMarks"/> (ohne Unity getestet).
    /// </summary>
    public class InjuryVisuals : MonoBehaviour
    {
        // Liegefläche der Trage (TraumaRoom: 0,68 m breit, 2,05 m lang, Mitte im Ursprung)
        const float MattressHalfWidth = 0.34f, MattressHalfLength = 1.025f;

        class Mark
        {
            public SkinMark Def; public MarkKind Kind;
            public SkinnedMeshRenderer Overlay; public MaterialPropertyBlock Mpb;
            public int Shape = -1; public double ShapeVolume_mL;
            public Bleeding Bleed;
            public double LastWet_s = double.NaN, LastLost;  // Eintrocknung: Zeitpunkt des letzten frischen Bluts
            public readonly List<Stream> Streams = new List<Stream>();
        }

        /// <summary>Ein Rinnsal über die Haut (BloodFlow), erscheint ab einem Verlust und wächst mit der Blutung.</summary>
        class Stream
        {
            public SkinnedMeshRenderer Renderer; public MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
            public double AppearsAt_mL; public float Length_m; public float Reveal;
        }

        class Bleeding
        {
            public string InjuryId;
            public bool Arterial;   // spritzt im Pulsrhythmus (seitlicher Einriss oder Durchtrennung einer Arterie)
            public Transform Emitter; public ParticleSystem Particles;
            public Transform Pool, FloorPool; public Vector3 PoolCenter;
            public double LastLost, LastT, Rate_mL_s; public float BeatPhase;
            public MaterialPropertyBlock PoolMpb; public double PoolWet_s, PoolLost;
        }

        SimulationManager _sim;
        AnatomyRig _rig;
        System.Func<bool> _xray;
        InjuryVisualsFile _file;
        readonly List<Mark> _marks = new List<Mark>();
        readonly List<SkinnedMeshRenderer> _overlays = new List<SkinnedMeshRenderer>();
        readonly Dictionary<string, double> _onset = new Dictionary<string, double>();
        double _poolThickness;
        float _mattressTop;

        /// <summary>Strukturen distal einer Amputation (im Röntgenblick ausgeblendet).</summary>
        public readonly HashSet<AnatomyPart> HiddenParts = new HashSet<AnatomyPart>();

        public static string FilePath => Path.Combine(Application.streamingAssetsPath, "NotfallSIM", "injury_visuals.json");

        /// <summary>Nach dem Aufbau und der Lagerung des Körpers, vor den Klickflächen (Amputation ändert die Haut).</summary>
        public void Init(SimulationManager sim, AnatomyRig rig, float mattressTop, System.Func<bool> xrayOn)
        {
            _sim = sim; _rig = rig; _mattressTop = mattressTop; _xray = xrayOn;
            if (sim == null || sim.Scenario == null || !File.Exists(FilePath)) return;
            try { _file = JsonUtility.FromJson<InjuryVisualsFile>(File.ReadAllText(FilePath)); }
            catch (System.Exception e) { Debug.LogWarning("[Verletzungen] injury_visuals.json nicht lesbar: " + e.Message); return; }
            foreach (var si in sim.Scenario.injuries) _onset[si.injuryId] = si.onsetTime_s;
            _poolThickness = SkinMarks.PoolThickness_m(_file.bloodPool);

            var data = rig.Data; var skin = data.Meshes[0];
            int visibleTris = skin.Info.submeshes[0] / 3;
            var normals = SkinMarks.VertexNormals(skin.Positions, skin.Indices, skin.Indices.Length / 3);
            var areas = SkinMarks.VertexAreas(skin.Positions, skin.Indices, skin.Indices.Length / 3);
            var adj = SkinMarks.Adjacency(skin.Info.vertexCount, skin.Indices, visibleTris);
            var mesh = rig.Skin.sharedMesh;
            var marks = _file.marks.Where(m => _onset.ContainsKey(m.injuryId)).ToList();

            // 1) Amputationen: Haut kürzen, Stumpf, innere Teile distal ausblenden
            HashSet<int> removed = null;
            foreach (var m in marks.Where(x => x.amputation))
            {
                var a = SkinMarks.Amputate(data, m, rig.Scale, visibleTris);
                if (a == null || a.RemovedTriangles == 0) { Debug.LogWarning("[Verletzungen] Amputation ohne Wirkung: " + m.id); continue; }
                mesh.SetTriangles(a.KeptTriangles, 0);
                removed = SkinMarks.RemovedVertices(skin.Indices, visibleTris, a.KeptTriangles);
                var kind = KindOf(m);
                // Stumpf ganz deckend (uv1 = 1), sonst sähe man am Rand in die hohle Gliedmaße
                var stump = BuildSkinned("Stumpf " + m.id, a.CapPositions, null, a.CapFalloff.Select(_ => 1f).ToArray(),
                                         SkinMarks.StumpColors(kind, a.CapFalloff), a.CapSource, null, a.CapTriangles, mesh, kind, 0);
                var segs = m.segments != null && m.segments.Length > 0
                    ? new HashSet<int>(m.segments.Select(data.SegmentIndex).Where(i => i >= 0)) : null;
                foreach (var p in rig.Parts)
                {
                    var md = data.Meshes.FirstOrDefault(x => x.Info == p.Info);
                    if (md != null && SkinMarks.IsDistal(md, a, segs)) HiddenParts.Add(p);
                }
                var mk = new Mark { Def = m, Kind = kind, Overlay = stump, Mpb = new MaterialPropertyBlock() };
                if (m.bleeding) mk.Bleed = MakeBleeding(m, a.PlanePoint, a.PlaneNormal, SegmentAt(skin, a.CapSource.Length > 0 ? a.CapSource[0] : 0));
                _marks.Add(mk);
            }

            // 2) Schwellungen als Blendshapes der Haut (vor den Auflagen, damit diese sie übernehmen)
            var patches = new Dictionary<SkinMark, SkinPatch>();
            foreach (var m in marks.Where(x => !x.amputation))
            {
                var p = SkinMarks.Select(data, m, rig.Scale, visibleTris, normals, adj);
                if (p == null || p.HiddenByShorts) { Debug.LogWarning("[Verletzungen] " + m.id + ": kein sichtbarer Hautort"); continue; }
                SkinMarks.DropRemoved(p, removed);
                patches[m] = p;
                double vol = m.swellingFromLoss ? NominalLoss(m.injuryId) : m.swelling_mL;
                if (vol <= 0) continue;
                var d = SkinMarks.SwellingDeltas(p, normals, areas, skin.Info.vertexCount, vol, rig.Scale);
                var deltas = new Vector3[skin.Info.vertexCount];
                for (int i = 0; i < deltas.Length; i++) deltas[i] = new Vector3(d[3 * i], d[3 * i + 1], d[3 * i + 2]);
                mesh.AddBlendShapeFrame("Schwellung " + m.id, 100f, deltas, null, null);
            }
            rig.Skin.sharedMesh = mesh; // neue Blendshapes übernehmen

            // 3) Auflagen (Farbe) und Blutungen
            foreach (var kv in patches)
            {
                var m = kv.Key; var p = kv.Value;
                var mk = new Mark { Def = m, Kind = KindOf(m), Mpb = new MaterialPropertyBlock() };
                if (m.swellingFromLoss || m.swelling_mL > 0)
                {
                    mk.Shape = mesh.GetBlendShapeIndex("Schwellung " + m.id);
                    mk.ShapeVolume_mL = m.swellingFromLoss ? NominalLoss(m.injuryId) : m.swelling_mL;
                }
                if (mk.Kind != null && p.Triangles.Length > 0)
                    mk.Overlay = BuildSkinned("Verletzung " + m.id, p.Pos, p.Nrm, p.RFalloff, SkinMarks.Colors(mk.Kind, p), p.Src, p.W,
                                              p.Triangles, mesh, mk.Kind, (_file.surfaceOffset_m > 0 ? _file.surfaceOffset_m : 0.0006f) * (1 + mk.Kind.layer) / rig.Scale);
                if (m.bleeding) mk.Bleed = MakeBleeding(m, p.HitPoint, p.HitNormal, SegmentAt(skin, p.Vertices.Length > 0 ? p.Vertices[0] : 0));
                if (m.rivulets > 0 && _file.rivulets != null) MakeStreams(mk, p, normals, adj, removed);
                _marks.Add(mk);
            }
        }

        /// <summary>Rinnsale vom Wundort abwärts. Schwerkraft je Segment aus der Lagerung (Knochen in Ruhelage achsparallel).</summary>
        void MakeStreams(Mark mk, SkinPatch p, float[] normals, List<int>[] adj, HashSet<int> removed)
        {
            var skin = _rig.Data.Meshes[0];
            var gSeg = new float[_rig.Bones.Length][];
            for (int i = 0; i < gSeg.Length; i++)
            {
                var g = _rig.Bones[i].InverseTransformDirection(Vector3.down).normalized;
                gSeg[i] = new[] { g.x, g.y, g.z };
            }
            bool[] allowed = null;
            if (removed != null && removed.Count > 0)
            {
                allowed = new bool[skin.Info.vertexCount];
                for (int i = 0; i < allowed.Length; i++) allowed[i] = !removed.Contains(i);
            }
            var rp = _file.rivulets;
            var def = _sim.Database != null ? _sim.Database.Get(mk.Def.injuryId) : null;
            double rate = def?.effects != null ? def.effects.Where(e => e.effectType == "Hemorrhage").Sum(e => e.baseRate_mL_min) : 0;
            float width = (float)(BloodFlow.Width_mm(rp, rate) / 1000 / _rig.Scale);
            // Quellen: Wundvertices nahe der Mitte
            var sources = Enumerable.Range(0, p.Vertices.Length).Where(i => p.Falloff[i] > 0.4f).Select(i => p.Vertices[i]).ToList();
            if (sources.Count == 0) sources.Add(skin.Indices[3 * p.HitTriangle]);
            var mat = RivuletMaterial();
            var kind = _file.kinds.FirstOrDefault(k => k.id == "blutspur");
            for (int r = 0; r < mk.Def.rivulets; r++)
            {
                int start = sources[(r * 7919) % sources.Count];
                var riv = BloodFlow.Trace(skin.Positions, normals, adj, start, v => gSeg[SegmentAt(skin, v)],
                                          (float)(rp.maxLength_m / _rig.Scale), 1000 * r + mk.Def.id.Length, allowed);
                BloodFlow.BuildStrip(riv, skin.Positions, normals, width * (1 - 0.15f * r));
                if (riv.Triangles.Length == 0) continue;
                int n = riv.Across.Length;
                var col = new float[4 * n];
                var c0 = kind != null ? kind.colorCenter : new[] { 0.42f, 0.02f, 0.04f };
                for (int i = 0; i < n; i++) { col[4 * i] = c0[0] * 0.8f; col[4 * i + 1] = c0[1] * 0.8f; col[4 * i + 2] = c0[2] * 0.8f; col[4 * i + 3] = 0.92f; } // im Rinnsal dicker, dunkler
                var smr = BuildSkinned("Rinnsal " + mk.Def.id + " " + r, riv.Pos, riv.Nrm, riv.Across, col, riv.Src, null, riv.Triangles,
                                       _rig.Skin.sharedMesh, kind, (_file.surfaceOffset_m > 0 ? _file.surfaceOffset_m : 0.0006f) * 2.5f / _rig.Scale, riv.Along, mat);
                smr.sortingOrder = 1;
                mk.Streams.Add(new Stream { Renderer = smr, AppearsAt_mL = r * rp.volumePerRivulet_mL, Length_m = riv.Length * _rig.Scale });
            }
        }

        Material _rivuletMaterial;

        Material RivuletMaterial()
        {
            if (_rivuletMaterial != null) return _rivuletMaterial;
            var src = WoundMaterial(_file.kinds.FirstOrDefault(k => k.id == "blutspur"));
            _rivuletMaterial = new Material(src);
            if (_rivuletMaterial.HasProperty("_Strip")) _rivuletMaterial.SetFloat("_Strip", 1);
            if (_rivuletMaterial.HasProperty("_Edge")) _rivuletMaterial.SetFloat("_Edge", 0.12f);
            if (_rivuletMaterial.HasProperty("_Irregular")) _rivuletMaterial.SetFloat("_Irregular", 0.35f);
            if (_rivuletMaterial.HasProperty("_Wet")) _rivuletMaterial.SetFloat("_Wet", 0.8f);
            return _rivuletMaterial;
        }

        /// <summary>Rinnsale: erscheinen nacheinander mit dem Verlust, die Front läuft mit frontSpeed, solange es blutet.
        /// Ohne Blutverlust der Verletzung (z. B. Kopfplatzwunde) sind sie bei Ankunft schon ganz da.</summary>
        void UpdateStreams(Mark m, double lost, bool bleedsNow, bool hasHemorrhage, float dry, bool visible)
        {
            foreach (var st in m.Streams)
            {
                bool on = visible && (!hasHemorrhage || lost >= st.AppearsAt_mL);
                if (!hasHemorrhage) st.Reveal = 1.2f;
                else if (on && bleedsNow && st.Length_m > 0)
                    st.Reveal = Mathf.Min(1.2f, st.Reveal + (float)(_file.rivulets.frontSpeed_m_s * Time.deltaTime / st.Length_m));
                st.Renderer.enabled = on && st.Reveal > 0;
                st.Mpb.SetFloat("_Reveal", st.Reveal);
                st.Mpb.SetFloat("_Dry", dry);
                st.Mpb.SetFloat("_Amount", 1);
                st.Renderer.SetPropertyBlock(st.Mpb);
            }
        }

        MarkKind KindOf(SkinMark m) => _file.kinds.FirstOrDefault(k => k.id == m.kind);

        /// <summary>Volumen, für das die Schwellung gebaut wird: größter möglicher Verlust der Verletzung.</summary>
        double NominalLoss(string injuryId)
        {
            var def = _sim.Database != null ? _sim.Database.Get(injuryId) : null;
            double v = def?.effects != null && def.effects.Length > 0 ? def.effects.Max(e => e.maxTotalLoss_mL) : 0;
            return v > 0 ? v : 1000;
        }

        static int SegmentAt(AnatomyMeshData skin, int v) => SkinMarks.DominantSegment(skin.Bones, skin.Weight0, v);

        // ---------------------------------------------------------------- Meshes

        /// <summary>Gewichtetes Mesh an den Knochen des Körpers; Vertices als Mischung aus Hautvertices (src/w, je 3 oder 1),
        /// mit Gewichten und Blendshapes der Haut, damit Atmung, Schwellung und Lagerung mitgehen.</summary>
        SkinnedMeshRenderer BuildSkinned(string name, float[] pos, float[] nrm, float[] falloff, float[] col, int[] src, float[] w,
                                         int[] tris, Mesh skinMesh, MarkKind kind, float offset, float[] uv1y = null, Material material = null)
        {
            int n = falloff.Length;
            int per = w == null ? 1 : 3;
            var skinWeights = skinMesh.boneWeights;
            var v = new Vector3[n]; var nn = new Vector3[n]; var c = new Color[n];
            var uv0 = new List<Vector3>(n); var uv1 = new List<Vector2>(n); var bw = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                var nv = nrm != null ? new Vector3(nrm[3 * i], nrm[3 * i + 1], nrm[3 * i + 2]) : Vector3.zero;
                v[i] = new Vector3(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]) + nv * offset;
                nn[i] = nv;
                c[i] = new Color(col[4 * i], col[4 * i + 1], col[4 * i + 2], col[4 * i + 3]);
                uv0.Add(new Vector3(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]));
                uv1.Add(new Vector2(falloff[i], uv1y != null ? uv1y[i] : 0));
                var acc = new Dictionary<int, float>();
                for (int q = 0; q < per; q++)
                {
                    float wq = w == null ? 1 : w[per * i + q];
                    if (wq <= 0) continue;
                    var sw = skinWeights[src[per * i + q]];
                    Add(acc, sw.boneIndex0, sw.weight0 * wq); Add(acc, sw.boneIndex1, sw.weight1 * wq);
                }
                bw[i] = ToWeight(acc);
            }
            var mesh = new Mesh { name = name };
            if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = v; mesh.colors = c;
            mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            if (nrm != null) mesh.normals = nn; else mesh.RecalculateNormals();
            mesh.boneWeights = bw;
            mesh.bindposes = skinMesh.bindposes;
            // Blendshapes der Haut übernehmen (Atmung, Schwellungen)
            var full = new Vector3[skinMesh.vertexCount];
            for (int s = 0; s < skinMesh.blendShapeCount; s++)
            {
                skinMesh.GetBlendShapeFrameVertices(s, 0, full, null, null);
                var d = new Vector3[n];
                for (int i = 0; i < n; i++)
                    for (int q = 0; q < per; q++)
                    {
                        float wq = w == null ? 1 : w[per * i + q];
                        if (wq > 0) d[i] += full[src[per * i + q]] * wq;
                    }
                mesh.AddBlendShapeFrame(skinMesh.GetBlendShapeName(s), skinMesh.GetBlendShapeFrameWeight(s, 0), d, null, null);
            }
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(_rig.Root.transform, false);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = _rig.Skin.bones;
            smr.rootBone = _rig.Skin.rootBone;
            smr.sharedMaterial = material ?? WoundMaterial(kind);
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.updateWhenOffscreen = true;
            smr.sortingOrder = kind != null ? kind.layer : 0; // Wunde über Blut über Prellung
            _overlays.Add(smr);
            return smr;
        }

        static void Add(Dictionary<int, float> acc, int b, float w) { if (w > 0) acc[b] = (acc.TryGetValue(b, out var x) ? x : 0) + w; }

        static BoneWeight ToWeight(Dictionary<int, float> acc)
        {
            var top = acc.OrderByDescending(kv => kv.Value).Take(4).ToArray();
            float sum = top.Sum(kv => kv.Value);
            var b = new BoneWeight();
            if (sum <= 0) { b.weight0 = 1; return b; }
            if (top.Length > 0) { b.boneIndex0 = top[0].Key; b.weight0 = top[0].Value / sum; }
            if (top.Length > 1) { b.boneIndex1 = top[1].Key; b.weight1 = top[1].Value / sum; }
            if (top.Length > 2) { b.boneIndex2 = top[2].Key; b.weight2 = top[2].Value / sum; }
            if (top.Length > 3) { b.boneIndex3 = top[3].Key; b.weight3 = top[3].Value / sum; }
            return b;
        }

        readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();

        Material WoundMaterial(MarkKind k)
        {
            string key = k != null ? k.id : "";
            if (_materials.TryGetValue(key, out var m)) return m;
            var sh = Resources.Load<Shader>("Wound") ?? Shader.Find("NotfallSIM/Wound");
            m = sh != null ? new Material(sh) : AnatomyBuilder.Lit(new Color(0.5f, 0.05f, 0.06f), 0.5f, true);
            if (k != null)
            {
                if (m.HasProperty("_Wet")) m.SetFloat("_Wet", k.wet);
                if (m.HasProperty("_Edge") && k.edge > 0) m.SetFloat("_Edge", k.edge);
                if (m.HasProperty("_Irregular")) m.SetFloat("_Irregular", k.irregularity);
                if (m.HasProperty("_NoiseScale") && k.noiseScale > 0) m.SetFloat("_NoiseScale", k.noiseScale);
                if (m.HasProperty("_Depth")) m.SetFloat("_Depth", k.depth);
                if (m.HasProperty("_AgeColor") && k.ageColor != null && k.ageColor.Length >= 3) m.SetColor("_AgeColor", new Color(k.ageColor[0], k.ageColor[1], k.ageColor[2]));
            }
            _materials[key] = m;
            return m;
        }

        // ---------------------------------------------------------------- Blutung

        Bleeding MakeBleeding(SkinMark m, float[] modelPoint, float[] modelNormal, int segment)
        {
            var bone = _rig.Bones[segment];
            var bind = _rig.Skin.sharedMesh.bindposes[segment];
            var p = new Vector3(modelPoint[0], modelPoint[1], modelPoint[2]);
            var nrm = new Vector3(modelNormal[0], modelNormal[1], modelNormal[2]).normalized;
            var def = _sim.Database != null ? _sim.Database.Get(m.injuryId) : null;
            bool arterial = def?.effects != null && def.effects.Any(e => e.lesion == "partial" || e.lesion == "transection");
            var b = new Bleeding { InjuryId = m.injuryId, Arterial = arterial };

            var em = new GameObject("Blutung " + m.id).transform;
            em.SetParent(bone, false);
            em.localPosition = bind.MultiplyPoint3x4(p + nrm * (0.015f / _rig.Scale));
            em.localRotation = Quaternion.LookRotation(bind.MultiplyVector(nrm).normalized);
            b.Emitter = em;
            b.Particles = MakeParticles(em.gameObject);

            var world = em.position;
            b.PoolCenter = new Vector3(Mathf.Clamp(world.x, -MattressHalfWidth + 0.05f, MattressHalfWidth - 0.05f), _mattressTop + 0.002f,
                                       Mathf.Clamp(world.z, -MattressHalfLength + 0.05f, MattressHalfLength - 0.05f));
            var mat = WoundMaterial(_file.kinds.FirstOrDefault(k => k.id == "blutspur") ?? KindOf(m));
            b.Pool = Disc("Blutlache Trage", b.PoolCenter, mat);
            b.FloorPool = Disc("Blutlache Boden", new Vector3(Mathf.Sign(world.x == 0 ? 1 : world.x) * (MattressHalfWidth + 0.12f), 0.002f, b.PoolCenter.z), mat);
            b.LastLost = double.NaN;
            return b;
        }

        static ParticleSystem MakeParticles(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false;
            main.startLifetime = 1.2f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.004f, 0.009f);
            main.startColor = new Color(0.42f, 0.02f, 0.03f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            var emission = ps.emission; emission.rateOverTime = 0;
            var shape = ps.shape; shape.enabled = false;
            var col = ps.collision;
            col.enabled = true; col.type = ParticleSystemCollisionType.World; col.lifetimeLoss = 1f; col.bounce = 0f;
            col.collidesWith = ~(1 << 2); // nicht mit den Röntgen-Kollidern
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = sh != null ? new Material(sh) : AnatomyBuilder.Lit(new Color(0.42f, 0.02f, 0.03f), 0.6f);
            m.color = new Color(0.42f, 0.02f, 0.03f);
            r.sharedMaterial = m;
            ps.Play();
            return ps;
        }

        /// <summary>Runde Lache (Radius 1, über die Skalierung vergrößert), unregelmäßiger Rand über den Shader.</summary>
        Transform Disc(string name, Vector3 center, Material mat)
        {
            const int seg = 48, rings = 6;
            var v = new List<Vector3>(); var uv0 = new List<Vector3>(); var uv1 = new List<Vector2>(); var c = new List<Color>(); var t = new List<int>();
            v.Add(Vector3.zero); uv0.Add(Vector3.zero); uv1.Add(new Vector2(1, 0)); c.Add(new Color(0.40f, 0.02f, 0.04f, 0.95f));
            for (int r = 1; r <= rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    float a = 2 * Mathf.PI * s / seg, rr = r / (float)rings;
                    var p = new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
                    v.Add(p); uv0.Add(p * 0.05f); uv1.Add(new Vector2(1 - rr, 0));
                    c.Add(Color.Lerp(new Color(0.40f, 0.02f, 0.04f, 0.95f), new Color(0.50f, 0.04f, 0.05f, 0.9f), rr));
                }
            for (int s = 0; s < seg; s++) { t.Add(0); t.Add(1 + (s + 1) % seg); t.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a0 = 1 + (r - 1) * seg + s, a1 = 1 + (r - 1) * seg + (s + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                    t.AddRange(new[] { a0, a1, b0, a1, b1, b0 });
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetUVs(0, uv0); mesh.SetUVs(1, uv1); mesh.SetColors(c); mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(0.0001f, 1, 0.0001f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            go.SetActive(false);
            return go.transform;
        }

        // ---------------------------------------------------------------- Laufzeit

        void LateUpdate()
        {
            if (_file == null || _sim == null || _sim.Engine == null) return;
            var eng = _sim.Engine;
            double t = eng.Current.SimTime_s;
            var ctx = new ExamContext
            {
                Time_s = t, BodyLossFraction = _sim.Vitals.BloodLossFraction,
                Level = eng.InjuryLevel, Lost_mL = eng.InjuryBloodLoss_mL, Events = _sim.Events
            };
            bool xray = _xray != null && _xray();
            var skin = _rig.Skin;

            foreach (var m in _marks)
            {
                bool started = _onset.TryGetValue(m.Def.injuryId, out var on) && on <= t;
                double amount = started ? SkinMarks.Amount(m.Def, ctx) : 0;
                if (m.Def.amputation) amount = 1; // Stumpf ist von Anfang an da
                if (m.Shape >= 0)
                {
                    double w = !started ? 0 : m.Def.swellingFromLoss
                        ? 100 * eng.InjuryBloodLoss_mL(m.Def.injuryId) / m.ShapeVolume_mL : 100 * amount;
                    skin.SetBlendShapeWeight(m.Shape, Mathf.Clamp((float)w, 0, 100));
                }
                if (m.Overlay != null)
                {
                    m.Overlay.enabled = amount > 0 && (!xray || m.Def.amputation);
                    m.Mpb.SetFloat("_Amount", (float)amount);
                    double lostNow = eng.InjuryBloodLoss_mL(m.Def.injuryId);
                    double prevLost = m.LastLost;
                    float dry = m.Kind != null && m.Kind.dries ? (float)UpdateDryness(m, lostNow, t) : 0f;
                    m.Mpb.SetFloat("_Dry", dry);
                    // Alter seit dem Unfall (präklinische Zeit + Szenariozeit; bei späterem Beginn ab diesem)
                    double age = t - on + (on <= 0 && _file.drying != null ? _file.drying.preArrivalAge_min * 60 : 0);
                    m.Mpb.SetFloat("_Age", (float)SkinMarks.ColorAge(m.Kind, age));
                    m.Overlay.SetPropertyBlock(m.Mpb);
                    if (m.Streams.Count > 0)
                    {
                        var def = _sim.Database != null ? _sim.Database.Get(m.Def.injuryId) : null;
                        bool hasHem = def?.effects != null && def.effects.Any(e => e.effectType == "Hemorrhage");
                        bool bleedsNow = lostNow > prevLost + 1e-4 && !_sim.Paused;
                        if (m.Kind == null || !m.Kind.dries) dry = (float)SkinMarks.Dryness(_file.drying, t - (double.IsNaN(m.LastWet_s) ? t : m.LastWet_s));
                        UpdateStreams(m, lostNow, bleedsNow, hasHem, dry, amount > 0 && !xray);
                    }
                }
                if (m.Bleed != null && started) UpdateBleeding(m.Bleed, eng.InjuryBloodLoss_mL(m.Bleed.InjuryId), t);
            }
            // Atmung und Schwellungen auf die Auflagen übertragen
            int count = skin.sharedMesh.blendShapeCount;
            foreach (var o in _overlays)
                for (int s = 0; s < count && s < o.sharedMesh.blendShapeCount; s++) o.SetBlendShapeWeight(s, skin.GetBlendShapeWeight(s));
        }

        /// <summary>Blut wird frisch gehalten, solange die Verletzung blutet; danach trocknet es (drying in injury_visuals.json).
        /// Wunden ohne Blutverlust sind bei Ankunft schon so alt wie die präklinische Zeit.</summary>
        double UpdateDryness(Mark m, double lost, double t)
        {
            if (double.IsNaN(m.LastWet_s))
            {
                var def = _sim.Database != null ? _sim.Database.Get(m.Def.injuryId) : null;
                bool bleeds = def?.effects != null && def.effects.Any(e => e.effectType == "Hemorrhage");
                m.LastWet_s = bleeds ? t : t - (_file.drying != null ? _file.drying.preArrivalAge_min * 60 : 0);
                m.LastLost = lost;
            }
            if (lost > m.LastLost + 1e-3) { m.LastWet_s = t; m.LastLost = lost; }
            return SkinMarks.Dryness(_file.drying, t - m.LastWet_s);
        }

        void UpdateBleeding(Bleeding b, double lost, double t)
        {
            // Blutfluss aus der Zunahme des Verlusts je Sekunde Simulationszeit, geglättet
            float dt = Time.deltaTime;
            if (!double.IsNaN(b.LastLost) && t > b.LastT)
            {
                double rate = System.Math.Max(0, lost - b.LastLost) / (t - b.LastT);
                b.Rate_mL_s += (rate - b.Rate_mL_s) * System.Math.Min(1, (t - b.LastT) / 1.5);
            }
            if (double.IsNaN(b.LastLost) || t > b.LastT) { b.LastLost = lost; b.LastT = t; }

            // Lache: Fläche = Volumen / Dicke; was über den Rand der Trage geht, auf den Boden
            double r = SkinMarks.PoolRadius_m(lost, _poolThickness);
            float maxR = Mathf.Min(MattressHalfWidth - Mathf.Abs(b.PoolCenter.x), MattressHalfLength - Mathf.Abs(b.PoolCenter.z)) + 0.03f;
            float onMattress = Mathf.Min((float)r, maxR);
            SetPool(b.Pool, onMattress);
            if (b.PoolMpb == null) b.PoolMpb = new MaterialPropertyBlock();
            if (lost > b.PoolLost + 1e-3) { b.PoolWet_s = t; b.PoolLost = lost; }
            b.PoolMpb.SetFloat("_Dry", (float)SkinMarks.Dryness(_file.drying, t - b.PoolWet_s));
            b.Pool.GetComponent<Renderer>().SetPropertyBlock(b.PoolMpb);
            b.FloorPool.GetComponent<Renderer>().SetPropertyBlock(b.PoolMpb);
            double over = lost - System.Math.PI * maxR * maxR * _poolThickness * 1e6;
            SetPool(b.FloorPool, over > 0 ? (float)SkinMarks.PoolRadius_m(over, _poolThickness) : 0);

            // Strahl: je Herzschlag ein Schwall, Geschwindigkeit aus dem systolischen Druck
            if (_sim.Paused || b.Particles == null) return;
            var v = _sim.Vitals;
            if (v.CardiacArrest || b.Rate_mL_s < 0.05 || !b.Arterial) return; // venös/parenchymatös: sickert, kein Strahl
            b.BeatPhase += dt * (float)v.HeartRate_bpm / 60f;
            if (b.BeatPhase < 1) return;
            b.BeatPhase -= Mathf.Floor(b.BeatPhase);
            float speed = (float)SkinMarks.JetSpeed_m_s(v.SystolicBP_mmHg, _file.bloodPool.density_kg_m3, _file.spurt.velocityCoefficient);
            int count = Mathf.Clamp((int)(b.Rate_mL_s * 6), 4, 40);
            var dir = b.Emitter.forward;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = b.Emitter.position;
                ep.velocity = (dir + Random.insideUnitSphere * 0.12f).normalized * speed * Random.Range(0.55f, 1f);
                ep.applyShapeToPosition = false;
                b.Particles.Emit(ep, 1);
            }
        }

        static void SetPool(Transform pool, float radius)
        {
            bool on = radius > 0.005f;
            if (pool.gameObject.activeSelf != on) pool.gameObject.SetActive(on);
            if (on) pool.localScale = new Vector3(radius, 1, radius);
        }
    }
}

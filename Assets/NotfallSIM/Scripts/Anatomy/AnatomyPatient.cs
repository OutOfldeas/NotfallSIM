using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NotfallSIM.Interaction;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;

namespace NotfallSIM.Anatomy
{
    /// <summary>
    /// Der Patient im Schockraum: anatomisches Modell in Rückenlage auf der Trage, anklickbare Körperregionen,
    /// Atmung und Hautfarbe aus der Physiologie, Röntgenblick mit Knochen, Organen, Gefäßen und Nerven.
    /// Nach Szenarioende werden die tatsächlichen Verletzungen im Röntgenblick rot hervorgehoben.
    /// </summary>
    [DefaultExecutionOrder(100)] // nach SimulationManager.Start (Szenario und Körpergröße stehen dann fest)
    public class AnatomyPatient : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] float mattressTop = 0.84f;
        [SerializeField] TMP_Text hoverLabel;
        [SerializeField] TMP_Text xrayButtonLabel;
        [SerializeField] string[] layerButtonIds = new string[0];
        [SerializeField] UnityEngine.UI.Image[] layerButtons = new UnityEngine.UI.Image[0];

        static readonly Color SkinTone = new Color(0.86f, 0.67f, 0.55f), Pale = new Color(0.86f, 0.80f, 0.76f), Cyanotic = new Color(0.60f, 0.64f, 0.78f);

        AnatomyRig _rig;
        Material _skin, _shorts, _xray, _highlight;
        readonly Dictionary<string, Material> _layerMats = new Dictionary<string, Material>();
        readonly Dictionary<string, bool> _layerOn = new Dictionary<string, bool> { { "bone", true }, { "organ", true }, { "vessel", true }, { "nerve", true }, { "muscle", false } }; // Muskeln verdecken sonst die Organe
        readonly HashSet<AnatomyPart> _injured = new HashSet<AnatomyPart>();
        bool _xrayOn, _injuriesShown, _collidersBuilt;
        float _breathPhase;
        int _bsRight = -1, _bsLeft = -1, _bsAbd = -1;
        Camera _cam;
        InjuryVisuals _visuals;
        Transform _regionsRoot;
        string _positionId = "flach";
        int _eventsSeen;

        public bool XRay => _xrayOn;
        /// <summary>Hat die Szene Bedienelemente (Röntgenblick-Knopf, Hover-Text) angeschlossen?</summary>
        public bool HasUi => hoverLabel != null || xrayButtonLabel != null;

        public void Configure(SimulationManager simulation, TMP_Text hover, TMP_Text xrayLabel, string[] layerIds = null, UnityEngine.UI.Image[] layerImages = null)
        {
            sim = simulation; hoverLabel = hover; xrayButtonLabel = xrayLabel;
            layerButtonIds = layerIds ?? new string[0]; layerButtons = layerImages ?? new UnityEngine.UI.Image[0];
        }

        void Start()
        {
            _cam = Camera.main;
            if (!AnatomyBuilder.Available)
            {
                Debug.LogWarning("[Patient] anatomy/body.bin fehlt — einfacher Platzhalter-Patient.");
                BuildFallback();
                return;
            }
            float height = 1.75f;
            if (sim != null && sim.Scenario != null && sim.Scenario.patient != null && sim.Scenario.patient.height_cm > 100)
                height = (float)sim.Scenario.patient.height_cm / 100f;

            _skin = AnatomyBuilder.SkinMaterial(SkinTone);
            _shorts = AnatomyBuilder.Lit(new Color(0.12f, 0.15f, 0.22f), 0.15f);
            var xs = Resources.Load<Shader>("XRay") ?? Shader.Find("NotfallSIM/XRay");
            _xray = xs != null ? new Material(xs) : AnatomyBuilder.Lit(new Color(0.4f, 0.7f, 1f, 0.2f), 0.5f);
            _highlight = AnatomyBuilder.Lit(new Color(0.95f, 0.1f, 0.1f), 0.5f, true);
            _highlight.EnableKeyword("_EMISSION");
            if (_highlight.HasProperty("_EmissionColor")) _highlight.SetColor("_EmissionColor", new Color(1.2f, 0.05f, 0.05f));

            _rig = AnatomyBuilder.Build(transform, height, true, _skin, _shorts, LayerMaterial);
            Pose();
            foreach (var p in _rig.Parts) p.Renderer.enabled = false;
            // Verletzungszeichen vor den Klickflächen: eine Amputation verändert die Haut
            _visuals = gameObject.AddComponent<InjuryVisuals>();
            _visuals.Init(sim, _rig, mattressTop, () => _xrayOn);

            var mesh = _rig.Skin.sharedMesh;
            _bsRight = mesh.GetBlendShapeIndex("AtmungThoraxRechts");
            _bsLeft = mesh.GetBlendShapeIndex("AtmungThoraxLinks");
            _bsAbd = mesh.GetBlendShapeIndex("AtmungAbdomen");
            BuildRegions();
            MarkInjuries();
            if (sim != null) sim.OnScenarioEnded += () => { if (!_xrayOn) ToggleXRay(); ShowInjuries(true); };
            StartCoroutine(BuildPartCollidersGradually());
        }

        Material LayerMaterial(AnatomyMeshInfo info)
        {
            string key = info.layer + "|" + string.Join(",", info.color.Select(c => c.ToString("0.00")));
            if (!_layerMats.TryGetValue(key, out var m))
            {
                var c = new Color(info.color[0], info.color[1], info.color[2]);
                m = AnatomyBuilder.Lit(c, info.layer == "bone" ? 0.25f : 0.55f, true);
                _layerMats[key] = m;
            }
            return m;
        }

        /// <summary>Rückenlage: Kopf Richtung +z, Bauch nach oben, Arme neben dem Körper auf der Liegefläche.</summary>
        void Pose()
        {
            var root = _rig.Root.transform;
            root.localRotation = Quaternion.Euler(0, 0, 180) * Quaternion.Euler(90, 0, 0);
            // Arme nach dorsal (auf die Matratze) und leicht adduziert, damit sie innerhalb der Liegefläche liegen
            // (geprüft außerhalb von Unity: Arme bis 0,31 m seitlich, Rückseite auf der Matratze, Hände 3 cm neben dem Becken)
            _rig.Bone("Oberarm rechts").localRotation = Quaternion.Euler(14, 0, -4);
            _rig.Bone("Oberarm links").localRotation = Quaternion.Euler(14, 0, 4);
            // Auf die Matratze legen. Nach der Drehung gilt: Welt-y = Modell-z (vorn oben), Welt-z = Modell-y (Kopf).
            // Der hinterste Hautpunkt (Rücken/Gesäß) liegt damit auf der Liegefläche.
            var pos = _rig.Data.Meshes[0].Positions;
            float minZ = float.MaxValue;
            for (int i = 2; i < pos.Length; i += 3) minZ = Mathf.Min(minZ, pos[i]);
            root.localPosition = new Vector3(0, mattressTop - minZ * _rig.Scale, 0);
        }

        /// <summary>Lagerung sichtbar machen — mit denselben Winkeln, mit denen die Physiologie die Höhe der Blutungsorte rechnet
        /// (MockPhysiologyEngine.Positions). Oberkörper um die Lendenwirbelsäule, Beine um die Hüfte, Arme um die Schulter.
        /// Ohne Rückenlehne/Beinstütze an der Trage: der angehobene Teil liegt frei.</summary>
        void ApplyPosition(string id)
        {
            if (!NotfallSIM.Engines.MockPhysiologyEngine.Positions.TryGetValue(id ?? "", out var a)) return;
            _positionId = id;
            // Modellachsen: x rechts, y kopfwärts, z ventral. Positiver x-Winkel kippt kopfwärts nach ventral (= nach oben).
            _rig.Bone("Abdomen").localRotation = Quaternion.Euler((float)a[0], 0, 0);
            _rig.Bone("Oberschenkel rechts").localRotation = Quaternion.Euler(-(float)a[1], 0, 0);
            _rig.Bone("Oberschenkel links").localRotation = Quaternion.Euler(-(float)a[2], 0, 0);
            _rig.Bone("Oberarm rechts").localRotation = a[3] > 0 ? Quaternion.Euler(-(float)a[3], 0, -4) : Quaternion.Euler(14, 0, -4);
            _rig.Bone("Oberarm links").localRotation = a[4] > 0 ? Quaternion.Euler(-(float)a[4], 0, 4) : Quaternion.Euler(14, 0, 4);
        }

        /// <summary>Neue Lagerung aus dem Protokoll übernehmen: Pose, Klickflächen, Kollider gewichteter Teile, Blut neu verlegen.</summary>
        void FollowPositioning()
        {
            var ev = sim.Events;
            if (ev == null || ev.Count == _eventsSeen) return;
            string latest = null;
            for (int i = _eventsSeen; i < ev.Count; i++)
                if (ev[i].Tag != null && ev[i].Tag.StartsWith("intervention:Positioning:"))
                    latest = ev[i].Tag.Split(':')[2];
            _eventsSeen = ev.Count;
            if (latest == null || latest == _positionId) return;
            ApplyPosition(latest);
            BuildRegions();
            foreach (var p in _rig.Parts)
                if (p.Skinned != null && p.Collider != null)
                {
                    var baked = new Mesh { name = p.Info.name + " (Lagerung)" };
                    p.Skinned.BakeMesh(baked);
                    p.Collider.sharedMesh = baked;
                }
            if (_visuals != null) _visuals.Repose();
        }

        /// <summary>
        /// Klickflächen genau auf der sichtbaren Haut: Die gelagerte Haut wird einmal „eingefroren“ (BakeMesh), jedes
        /// Hautdreieck gehört zur Region seines dominanten Segments, je Region ein MeshCollider mit BodyRegion.
        /// (Vorher Kapseln/Quader je Segment — die überlappten sich, z. B. traf ein Klick auf die Hand das Becken.)
        /// </summary>
        void BuildRegions()
        {
            var h = _rig.Data.Header;
            var md = _rig.Data.Meshes[0];
            var baked = new Mesh();
            _rig.Skin.BakeMesh(baked, true);              // Haut in Lagerung, Skalierung eingerechnet
            var v = baked.vertices;
            var tris = _rig.Skin.sharedMesh.triangles;    // alle Teilmeshes
            var byRegion = new Dictionary<string, List<int>>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int vi = tris[t];
                int seg = md.Weight0[vi] >= 128 ? md.Bones[2 * vi] : md.Bones[2 * vi + 1];
                string region = h.segments[seg].region;
                if (!byRegion.TryGetValue(region, out var list)) byRegion[region] = list = new List<int>();
                list.Add(tris[t]); list.Add(tris[t + 1]); list.Add(tris[t + 2]);
            }
            if (_regionsRoot != null) { _regionsRoot.gameObject.SetActive(false); Destroy(_regionsRoot.gameObject); }
            var parent = new GameObject("Klickflächen").transform;
            _regionsRoot = parent;
            parent.SetParent(transform, false);
            parent.SetPositionAndRotation(_rig.Skin.transform.position, _rig.Skin.transform.rotation);
            foreach (var kv in byRegion)
            {
                var map = new Dictionary<int, int>();
                var verts = new List<Vector3>();
                var idx = new int[kv.Value.Count];
                for (int k = 0; k < idx.Length; k++)
                {
                    int o = kv.Value[k];
                    if (!map.TryGetValue(o, out int ni)) { ni = verts.Count; map[o] = ni; verts.Add(v[o]); }
                    idx[k] = ni;
                }
                var mesh = new Mesh { name = "Klickfläche " + kv.Key };
                if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetTriangles(idx, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("Region " + kv.Key);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                go.AddComponent<BodyRegion>().Configure(sim, kv.Key);
            }
            Destroy(baked);
        }

        void MarkInjuries()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "NotfallSIM", "anatomy", "anatomy.json");
            if (sim == null || sim.Scenario == null || !File.Exists(path)) return;
            var map = JsonUtility.FromJson<AnatomyInjuryMapFile>(File.ReadAllText(path));
            var present = new HashSet<string>(sim.Scenario.injuries.Select(i => i.injuryId));
            foreach (var e in map.injuries.Where(x => present.Contains(x.injuryId)))
                foreach (var pat in e.meshes)
                    foreach (var p in _rig.Parts.Where(p => Regex.IsMatch(p.Info.name, pat)))
                        _injured.Add(p);
        }

        // ---------------------------------------------------------------- Bedienung (Buttons)

        public void ToggleXRay()
        {
            if (_rig == null) return;
            _xrayOn = !_xrayOn;
            _rig.Skin.sharedMaterials = _xrayOn ? new[] { _xray, _xray } : new[] { _skin, _shorts };
            _rig.Skin.shadowCastingMode = _xrayOn ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            if (_xrayOn && !_collidersBuilt) BuildPartColliders();
            ApplyLayers();
            if (xrayButtonLabel) xrayButtonLabel.text = _xrayOn ? "Röntgenblick aus" : "Röntgenblick";
            if (!_xrayOn && hoverLabel) hoverLabel.text = "";
        }

        /// <summary>„bone“, „organ“ (inkl. Gehirn), „vessel“ (Arterien und Venen), „nerve“, „muscle“.</summary>
        public void ToggleLayer(string layer)
        {
            if (!_layerOn.ContainsKey(layer)) return;
            _layerOn[layer] = !_layerOn[layer];
            ApplyLayers();
        }

        public void ToggleInjuries() => ShowInjuries(!_injuriesShown);

        public void ShowInjuries(bool on)
        {
            _injuriesShown = on && sim != null && sim.Ended; // erst nach Szenarioende — sonst wäre die Diagnose verraten
            foreach (var p in _injured) p.Renderer.sharedMaterial = _injuriesShown ? _highlight : LayerMaterial(p.Info);
            ApplyLayers();
        }

        static string Group(string layer) => layer == "artery" || layer == "vein" ? "vessel" : layer == "brain" ? "organ" : layer;

        void ApplyLayers()
        {
            foreach (var p in _rig.Parts)
            {
                bool amputated = _visuals != null && _visuals.HiddenParts.Contains(p);
                p.Renderer.enabled = _xrayOn && !amputated && (_layerOn[Group(p.Info.layer)] || (_injuriesShown && _injured.Contains(p)));
                if (p.Collider) p.Collider.enabled = p.Renderer.enabled; // ausgeblendete Ebenen verdecken den Mauszeiger nicht
            }
            // Ebenen-Knöpfe: ausgeschaltete Ebene abgedunkelt
            for (int i = 0; i < layerButtons.Length && i < layerButtonIds.Length; i++)
                if (layerButtons[i] && _layerOn.TryGetValue(layerButtonIds[i], out var on))
                    layerButtons[i].color = on ? new Color(0.25f, 0.27f, 0.30f) : new Color(0.10f, 0.11f, 0.12f);
        }

        /// <summary>Kollider für den Namen unter der Maus (Ebene „Ignore Raycast“, stören die Untersuchung nicht).
        /// Werden nach dem Start verteilt über mehrere Bilder erzeugt, damit der erste Röntgenblick nicht ruckelt;
        /// ein vorzeitiges Einschalten baut den Rest sofort.</summary>
        void BuildPartColliders()
        {
            foreach (var p in _rig.Parts) AddPartCollider(p);
            _collidersBuilt = true;
        }

        void AddPartCollider(AnatomyPart p)
        {
            if (p.Collider) return;
            p.Renderer.gameObject.layer = 2;
            p.Collider = p.Renderer.gameObject.AddComponent<MeshCollider>();
            if (p.Skinned != null)
            {
                // Muskeln: Form in der aktuellen Lagerung (Arme auf der Trage), ohne Transform-Skalierung wie ein MeshFilter
                var baked = new Mesh { name = p.Info.name + " (Lagerung)" };
                p.Skinned.BakeMesh(baked);
                p.Collider.sharedMesh = baked;
            }
            else p.Collider.sharedMesh = p.Filter.sharedMesh;
            p.Collider.enabled = p.Renderer.enabled;
        }

        System.Collections.IEnumerator BuildPartCollidersGradually()
        {
            int k = 0;
            foreach (var p in _rig.Parts)
            {
                if (_collidersBuilt) yield break;
                AddPartCollider(p);
                if (++k % 12 == 0) yield return null;
            }
            _collidersBuilt = true;
        }

        // ---------------------------------------------------------------- Laufzeit

        void Update()
        {
            if (_rig == null || sim == null || sim.Engine == null) return;
            if (_xrayOn && hoverLabel && _cam) Hover();
            FollowPositioning();
            if (sim.Paused) return; // Pause: Atmung steht
            var v = sim.Vitals;

            // Atmung: Brustkorb rechts/links und Bauch; beim Spannungspneu hebt sich die betroffene Seite kaum
            float rr = v.CardiacArrest ? 0 : (float)v.RespirationRate_bpm;
            _breathPhase = (_breathPhase + Time.deltaTime * rr / 60f) % 1f;
            float b = (1 - Mathf.Cos(2 * Mathf.PI * _breathPhase)) / 2;
            float depth = v.Intubated ? 1f : Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((rr - 20) / 20));
            float tp = Mathf.Clamp01((float)sim.Engine.InjuryLevel("spannungspneu_links"));
            if (_bsRight >= 0) _rig.Skin.SetBlendShapeWeight(_bsRight, 100 * b * depth);
            if (_bsLeft >= 0) _rig.Skin.SetBlendShapeWeight(_bsLeft, 100 * b * depth * (1 - 0.9f * tp));
            if (_bsAbd >= 0) _rig.Skin.SetBlendShapeWeight(_bsAbd, 100 * b * depth * 0.8f);

            // Hautfarbe aus der Durchblutung (skin.json): Blässe, Schweiß, Marmorierung, Zyanose aus kapillärem Desoxy-Hb
            if (_skin.HasProperty("_Pallor") && AnatomyBuilder.SkinData != null)
            {
                var st = SkinPerfusion.Evaluate(AnatomyBuilder.SkinData, v);
                _skin.SetFloat("_Pallor", st.Pallor);
                _skin.SetFloat("_Sweat", st.Sweat);
                _skin.SetFloat("_Mottling", st.Mottling);
                _skin.SetFloat("_Cyanosis", st.Cyanosis);
                return;
            }
            float pale = Mathf.Clamp01(((float)v.BloodLossFraction - 0.15f) / 0.3f);
            float cyan = Mathf.Clamp01((92f - (float)v.SpO2_percent) / 20f);
            if (v.CardiacArrest) { pale = 1; cyan = Mathf.Max(cyan, 0.5f); }
            _skin.color = Color.Lerp(Color.Lerp(SkinTone, Pale, pale * 0.8f), Cyanotic, cyan * 0.7f);
        }

        void Hover()
        {
            Vector2 mouse;
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return;
            mouse = m.position.ReadValue();
#else
            mouse = Input.mousePosition;
#endif
            var ray = _cam.ScreenPointToRay(mouse);
            if (Physics.Raycast(ray, out var hit, 10f, 1 << 2))
            {
                var part = _rig.Parts.FirstOrDefault(p => p.Renderer.gameObject == hit.collider.gameObject);
                hoverLabel.text = part != null && part.Renderer.enabled ? HoverText(part) : "";
            }
            else hoverLabel.text = "";
        }

        // ---------------------------------------------------------------- Gewebeeigenschaften

        static TissueDatabase _tissues;
        static bool _tissuesTried;
        readonly Dictionary<AnatomyPart, string> _hoverText = new Dictionary<AnatomyPart, string>();

        /// <summary>Gewebedatenbank (StreamingAssets/NotfallSIM/tissues.json); null, wenn die Datei fehlt.</summary>
        public static TissueDatabase Tissues
        {
            get
            {
                if (_tissuesTried) return _tissues;
                _tissuesTried = true;
                string path = Path.Combine(Application.streamingAssetsPath, "NotfallSIM", "tissues.json");
                try
                {
                    if (File.Exists(path)) _tissues = new TissueDatabase(JsonUtility.FromJson<TissueDatabaseFile>(File.ReadAllText(path)));
                    else Debug.LogWarning("[Patient] tissues.json fehlt — keine Gewebeeigenschaften im Röntgenblick.");
                }
                catch (System.Exception e) { Debug.LogWarning("[Patient] tissues.json nicht lesbar: " + e.Message); }
                return _tissues;
            }
        }

        /// <summary>Gewebe eines Anatomieteils (null ohne Datenbank oder Zuordnung).</summary>
        public static TissueEntry TissueOf(AnatomyPart part) => Tissues?.For(part.Info.name, part.Info.layer);

        /// <summary>Name der Struktur, darunter klein die belegten mechanischen Eigenschaften des Gewebes.</summary>
        string HoverText(AnatomyPart part)
        {
            if (_hoverText.TryGetValue(part, out var text)) return text;
            var tissue = TissueOf(part);
            text = tissue == null ? part.Info.label : part.Info.label + "\n<size=72%><color=#C8D2DC>" + TissueDatabase.Summary(tissue) + "</color></size>";
            _hoverText[part] = text;
            return text;
        }

        // ---------------------------------------------------------------- Platzhalter ohne body.bin

        void Region(string name, PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = _skin;
            go.AddComponent<BodyRegion>().Configure(sim, name);
        }

        void BuildFallback()
        {
            _skin = AnatomyBuilder.Lit(SkinTone, 0.3f);
            float y = mattressTop + 0.1f;
            var limb = new Vector3(90, 0, 0);
            Region("Kopf", PrimitiveType.Sphere, new Vector3(0, y + 0.03f, 0.82f), Vector3.zero, new Vector3(0.21f, 0.21f, 0.24f));
            Region("Thorax", PrimitiveType.Cube, new Vector3(0, y, 0.46f), Vector3.zero, new Vector3(0.44f, 0.20f, 0.42f));
            Region("Abdomen", PrimitiveType.Cube, new Vector3(0, y, 0.13f), Vector3.zero, new Vector3(0.38f, 0.17f, 0.26f));
            Region("Becken", PrimitiveType.Cube, new Vector3(0, y, -0.14f), Vector3.zero, new Vector3(0.40f, 0.19f, 0.28f));
            Region("Oberschenkel rechts", PrimitiveType.Capsule, new Vector3(-0.11f, y, -0.52f), limb, new Vector3(0.15f, 0.22f, 0.15f));
            Region("Oberschenkel links", PrimitiveType.Capsule, new Vector3(0.11f, y, -0.52f), limb, new Vector3(0.15f, 0.22f, 0.15f));
            Region("Unterschenkel rechts", PrimitiveType.Capsule, new Vector3(-0.11f, y, -0.95f), limb, new Vector3(0.12f, 0.21f, 0.12f));
            Region("Unterschenkel links", PrimitiveType.Capsule, new Vector3(0.11f, y, -0.95f), limb, new Vector3(0.12f, 0.21f, 0.12f));
            Region("Arm rechts", PrimitiveType.Capsule, new Vector3(-0.29f, y, 0.32f), limb, new Vector3(0.10f, 0.30f, 0.10f));
            Region("Arm links", PrimitiveType.Capsule, new Vector3(0.29f, y, 0.32f), limb, new Vector3(0.10f, 0.30f, 0.10f));
        }
    }
}

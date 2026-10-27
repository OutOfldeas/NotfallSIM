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

        static readonly Color SkinTone = new Color(0.86f, 0.67f, 0.55f), Pale = new Color(0.86f, 0.80f, 0.76f), Cyanotic = new Color(0.60f, 0.64f, 0.78f);

        AnatomyRig _rig;
        Material _skin, _shorts, _xray, _highlight;
        readonly Dictionary<string, Material> _layerMats = new Dictionary<string, Material>();
        readonly Dictionary<string, bool> _layerOn = new Dictionary<string, bool> { { "bone", true }, { "organ", true }, { "vessel", true }, { "nerve", true } };
        readonly HashSet<AnatomyPart> _injured = new HashSet<AnatomyPart>();
        bool _xrayOn, _injuriesShown, _collidersBuilt;
        float _breathPhase;
        int _bsRight = -1, _bsLeft = -1, _bsAbd = -1;
        Camera _cam;

        public bool XRay => _xrayOn;

        public void Configure(SimulationManager simulation, TMP_Text hover, TMP_Text xrayLabel)
        {
            sim = simulation; hoverLabel = hover; xrayButtonLabel = xrayLabel;
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

            _skin = AnatomyBuilder.Lit(SkinTone, 0.38f);
            _shorts = AnatomyBuilder.Lit(new Color(0.12f, 0.15f, 0.22f), 0.15f);
            var xs = Resources.Load<Shader>("XRay") ?? Shader.Find("NotfallSIM/XRay");
            _xray = xs != null ? new Material(xs) : AnatomyBuilder.Lit(new Color(0.4f, 0.7f, 1f, 0.2f), 0.5f);
            _highlight = AnatomyBuilder.Lit(new Color(0.95f, 0.1f, 0.1f), 0.5f, true);
            _highlight.EnableKeyword("_EMISSION");
            if (_highlight.HasProperty("_EmissionColor")) _highlight.SetColor("_EmissionColor", new Color(1.2f, 0.05f, 0.05f));

            _rig = AnatomyBuilder.Build(transform, height, true, _skin, _shorts, LayerMaterial);
            Pose();
            foreach (var p in _rig.Parts) p.Renderer.enabled = false;

            var mesh = _rig.Skin.sharedMesh;
            _bsRight = mesh.GetBlendShapeIndex("AtmungThoraxRechts");
            _bsLeft = mesh.GetBlendShapeIndex("AtmungThoraxLinks");
            _bsAbd = mesh.GetBlendShapeIndex("AtmungAbdomen");
            BuildRegions();
            MarkInjuries();
            if (sim != null) sim.OnScenarioEnded += () => { if (!_xrayOn) ToggleXRay(); ShowInjuries(true); };
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

        /// <summary>Rückenlage: Kopf Richtung +z, Bauch nach oben, Arme leicht abgespreizt neben dem Körper.</summary>
        void Pose()
        {
            var root = _rig.Root.transform;
            root.localRotation = Quaternion.Euler(0, 0, 180) * Quaternion.Euler(90, 0, 0);
            _rig.Bone("Oberarm rechts").localRotation = Quaternion.Euler(10, 0, 5);
            _rig.Bone("Oberarm links").localRotation = Quaternion.Euler(10, 0, -5);
            _rig.Bone("Unterarm rechts").localRotation = Quaternion.Euler(4, 0, 0);
            _rig.Bone("Unterarm links").localRotation = Quaternion.Euler(4, 0, 0);
            // Auf die Matratze legen. Nach der Drehung gilt: Welt-y = Modell-z (vorn oben), Welt-z = Modell-y (Kopf).
            // Der hinterste Hautpunkt (Rücken/Gesäß) liegt damit auf der Liegefläche.
            var pos = _rig.Data.Meshes[0].Positions;
            float minZ = float.MaxValue;
            for (int i = 2; i < pos.Length; i += 3) minZ = Mathf.Min(minZ, pos[i]);
            root.localPosition = new Vector3(0, mattressTop - minZ * _rig.Scale, 0);
        }

        /// <summary>Unsichtbare Kollider je Segment mit BodyRegion — so funktionieren Untersuchung und Befunde wie bisher.</summary>
        void BuildRegions()
        {
            var h = _rig.Data.Header;
            for (int i = 0; i < h.segments.Length; i++)
            {
                var s = h.segments[i];
                var go = new GameObject("Region " + s.name);
                go.transform.SetParent(_rig.Bones[i], false);
                if (s.capsule)
                {
                    Vector3 axis = AnatomyBuilder.V(s.end) - AnatomyBuilder.V(s.joint);
                    var c = go.AddComponent<CapsuleCollider>();
                    go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
                    go.transform.localPosition = axis / 2;
                    c.direction = 1;
                    c.radius = s.radius * 1.15f;
                    c.height = axis.magnitude + s.radius;
                }
                else
                {
                    var c = go.AddComponent<BoxCollider>();
                    c.center = AnatomyBuilder.V(s.boxCenter);
                    c.size = AnatomyBuilder.V(s.boxSize);
                }
                go.AddComponent<BodyRegion>().Configure(sim, s.region);
            }
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
            var mats = _rig.Skin.sharedMaterials;
            _rig.Skin.sharedMaterials = _xrayOn ? new[] { _xray, _xray } : new[] { _skin, _shorts };
            _rig.Skin.shadowCastingMode = _xrayOn ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
            ApplyLayers();
            if (_xrayOn && !_collidersBuilt) BuildPartColliders();
            if (xrayButtonLabel) xrayButtonLabel.text = _xrayOn ? "Röntgenblick aus" : "Röntgenblick";
            if (!_xrayOn && hoverLabel) hoverLabel.text = "";
        }

        /// <summary>„bone“, „organ“ (inkl. Gehirn), „vessel“ (Arterien und Venen), „nerve“.</summary>
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
                p.Renderer.enabled = _xrayOn && (_layerOn[Group(p.Info.layer)] || (_injuriesShown && _injured.Contains(p)));
        }

        void BuildPartColliders()
        {
            _collidersBuilt = true;
            foreach (var p in _rig.Parts)
            {
                var mc = p.Renderer.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = p.Filter.sharedMesh;
                p.Renderer.gameObject.layer = 2; // Ignore Raycast: stört die Untersuchungsklicks nicht
            }
        }

        // ---------------------------------------------------------------- Laufzeit

        void Update()
        {
            if (_rig == null || sim == null || sim.Engine == null) return;
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

            // Hautfarbe: Blässe mit dem Blutverlust, Zyanose bei niedriger Sättigung
            float pale = Mathf.Clamp01(((float)v.BloodLossFraction - 0.15f) / 0.3f);
            float cyan = Mathf.Clamp01((92f - (float)v.SpO2_percent) / 20f);
            if (v.CardiacArrest) { pale = 1; cyan = Mathf.Max(cyan, 0.5f); }
            _skin.color = Color.Lerp(Color.Lerp(SkinTone, Pale, pale * 0.8f), Cyanotic, cyan * 0.7f);

            if (_xrayOn && hoverLabel && _cam) Hover();
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
                hoverLabel.text = part != null && part.Renderer.enabled ? part.Info.label : "";
            }
            else hoverLabel.text = "";
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

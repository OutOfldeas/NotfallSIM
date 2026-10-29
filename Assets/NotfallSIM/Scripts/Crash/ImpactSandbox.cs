using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NotfallSIM.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Gewalt-Sandbox: Der Körper liegt am Boden. Mit der linken Maus lässt er sich an jedem Körperteil packen, ziehen und schleudern;
    /// alternativ wird ein Gegenstand gewählt und per Klick geworfen, geschlagen bzw. gestochen (Ziel = Klickpunkt) oder
    /// (Wand, Tischkante, Bordstein, Bewehrungsstab) aufgestellt, um den Körper dagegen zu schleudern.
    ///
    /// Hybrid: Ziehen läuft live (normale Physik, starre Kontakte); sobald der Körper losgelassen wird oder ein Gegenstand fliegt,
    /// wird der Aufprall mit nachgiebigem Kontakt und 10 kHz berechnet und abgespielt (<see cref="CrashRig.RunImpact"/>).
    /// Verletzungen sammeln sich über alle Würfe (<see cref="ImpactModel.Evaluate"/>) und gehen in den Schockraum.
    /// </summary>
    public class ImpactSandbox : MonoBehaviour
    {
        CrashRig _rig;
        CrashSandbox _host;
        Camera _cam;
        ImpactModelFile _model;
        SandboxObjectFile _objects;
        CrashParams _p;

        public bool Active { get; private set; }
        SandboxObject _tool;                       // null = Körper ziehen
        readonly Dictionary<string, float> _speed = new Dictionary<string, float>();
        readonly List<BluntContact> _blunt = new List<BluntContact>();
        readonly List<string> _actions = new List<string>();
        readonly List<Anatomy.CrashMark> _marks = new List<Anatomy.CrashMark>();
        int _run, _stabMarks;
        float _savedFixedDt = -1;
        string _lastSummary = "";

        // Ziehen
        Rigidbody _grabbed, _hand;
        ConfigurableJoint _grabJoint;
        Vector3 _handTarget, _handVel;
        float _grabDepth;
        const float MaxHandSpeed = 25f;

        // UI
        RectTransform _panel;
        readonly Dictionary<string, Image> _toolButtons = new Dictionary<string, Image>();
        static readonly Color ToolColor = new Color(0.22f, 0.27f, 0.33f), ToolSelected = new Color(0.15f, 0.5f, 0.75f), SharpColor = new Color(0.42f, 0.24f, 0.24f);

        public void Init(CrashRig rig, CrashSandbox host, Camera cam) { _rig = rig; _host = host; _cam = cam; }

        static T LoadJson<T>(string file) where T : class
        {
            string path = Path.Combine(InjuryDatabase.Root, file);
            return File.Exists(path) ? JsonUtility.FromJson<T>(File.ReadAllText(path)) : null;
        }

        /// <summary>Gewalt-Modus einschalten: Daten laden, Körper hinlegen, Werkzeugleiste zeigen.</summary>
        public bool Activate(CrashParams p)
        {
            _model = _model ?? LoadJson<ImpactModelFile>("impact_model.json");
            _objects = _objects ?? LoadJson<SandboxObjectFile>("objects.json");
            if (_model == null || _objects == null) { Debug.LogWarning("[Gewalt] impact_model.json oder objects.json fehlt."); return false; }
            _p = p;
            Active = true;
            if (_savedFixedDt < 0) { _savedFixedDt = Time.fixedDeltaTime; Time.fixedDeltaTime = 0.01f; }   // ruhigeres Ziehen
            foreach (var o in _objects.objects) if (!_speed.ContainsKey(o.id)) _speed[o.id] = (float)o.defaultSpeed_m_s;
            ResetBody();
            BuildPanel();
            if (_panel) _panel.gameObject.SetActive(true);
            return true;
        }

        public void Deactivate()
        {
            if (!Active) return;
            Release(false);
            Active = false;
            _rig.EndGewalt();
            if (_panel) _panel.gameObject.SetActive(false);
            if (_savedFixedDt > 0) { Time.fixedDeltaTime = _savedFixedDt; _savedFixedDt = -1; }
        }

        void OnDisable() { if (_savedFixedDt > 0) { Time.fixedDeltaTime = _savedFixedDt; _savedFixedDt = -1; } }

        /// <summary>Neuer, unverletzter Körper; alle Gegenstände weg.</summary>
        public void ResetBody()
        {
            if (_rig.Running || _rig.Playing) return;
            Release(false);
            _p.Seed = UnityEngine.Random.Range(1, 100000);
            _rig.BuildGewalt(_p, _model);
            _blunt.Clear(); _actions.Clear(); _marks.Clear(); _allPenetrations.Clear(); _run = 0; _stabMarks = 0; _lastSummary = "";
            _host.SetGewaltResult(null, null, "Körper liegt bereit.\n\n" + Help());
        }

        public void RemoveObjects()
        {
            if (_rig.Running || _rig.Playing) return;
            if (_rig.Penetrations != null) _allPenetrations.AddRange(_rig.Penetrations.Results);
            _rig.ClearObjects();
            _stabMarks = 0;   // Stichwunden bleiben in der Liste, nur die Zählung für neue Kanäle beginnt neu
        }

        static string Help() =>
            "<size=85%><b>Bedienung</b>\n" +
            "· Werkzeug „Körper ziehen“: Körperteil mit der linken Maus packen, ziehen, schwungvoll loslassen.\n" +
            "· Gegenstand wählen, dann auf ein Körperteil klicken: werfen, schlagen oder stechen (Geschwindigkeit mit –/+).\n" +
            "· Wand, Tischkante, Bordstein, Bewehrungsstab: auf den Boden klicken zum Aufstellen, dann den Körper dagegen schleudern.\n" +
            "· Verletzungen sammeln sich über alle Würfe. „Neuer Körper“ beginnt von vorn.</size>";

        public string SettingsText()
        {
            string tool = _tool == null ? "Körper ziehen und schleudern" : _tool.label;
            string speed = _tool != null && _tool.mode != "statisch" ? $"\nGeschwindigkeit: {_speed[_tool.id]:0.#} m/s ({_speed[_tool.id] * 3.6f:0} km/h)" : "";
            string mass = _tool != null && _tool.mode != "statisch" ? $" · {_tool.mass_kg:0.#} kg{(_tool.mode == "gefuehrt" ? " wirksame Masse" : "")}" : "";
            return $"<b>Stumpfe/spitze Gewalt</b>\nWerkzeug: {tool}{mass}{speed}\nEinwirkungen bisher: {_run}";
        }

        public void ChangeSpeed(float direction)
        {
            if (_tool == null || _tool.mode == "statisch") return;
            float v = _speed[_tool.id], step = v >= 10 ? 2 : 1;
            _speed[_tool.id] = Mathf.Clamp(v + Mathf.Sign(direction) * step, (float)_tool.minSpeed_m_s, (float)_tool.maxSpeed_m_s);
        }

        void SelectTool(SandboxObject o)
        {
            _tool = o;
            foreach (var kv in _toolButtons)
                kv.Value.color = kv.Key == (o != null ? o.id : "") ? ToolSelected : (_objects.objects.FirstOrDefault(x => x.id == kv.Key)?.Sharp == true ? SharpColor : ToolColor);
        }

        // ---------------------------------------------------------------- Werkzeugleiste

        void BuildPanel()
        {
            if (_panel != null) return;
            var canvas = FindCanvas();
            if (canvas == null) return;
            _panel = UI.RuntimeUi.Panel(canvas.transform, "Gewalt-Werkzeuge", new Color(0.08f, 0.1f, 0.13f, 0.9f));
            UI.RuntimeUi.Place(_panel, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-470, 16), new Vector2(470, 16));
            var fit = _panel.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _panel.pivot = new Vector2(0.5f, 0);
            UI.RuntimeUi.Column(_panel, 6, 10);
            UI.RuntimeUi.Label(_panel, "<b>WERKZEUG</b>  <size=80%>stumpf grau · spitz rot</size>", 16, new Color(0.7f, 0.75f, 0.8f), 22);
            var items = new List<ToolItem> { new ToolItem { id = "", label = "Körper ziehen", tool = null } };
            foreach (var o in _objects.objects) items.Add(new ToolItem { id = o.id, label = o.label, tool = o, sharp = o.Sharp });
            const int perRow = 5;
            for (int i = 0; i < items.Count; i += perRow)
            {
                var row = Row(_panel);
                foreach (var it in items.Skip(i).Take(perRow))
                {
                    var tool = it.tool;
                    var b = UI.RuntimeUi.Button(row, it.label, it.sharp ? SharpColor : ToolColor, () => SelectTool(tool), 40, 15);
                    _toolButtons[it.id] = b.GetComponent<Image>();
                }
            }
            var actions = Row(_panel);
            UI.RuntimeUi.Button(actions, "Gegenstände entfernen", ToolColor, RemoveObjects, 40, 15);
            UI.RuntimeUi.Button(actions, "Neuer Körper", ToolColor, ResetBody, 40, 15);
            SelectTool(null);
        }

        class ToolItem { public string id, label; public SandboxObject tool; public bool sharp; }

        static Transform Row(Transform parent)
        {
            var go = new GameObject("Reihe", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            go.AddComponent<LayoutElement>().preferredHeight = 40;
            return go.transform;
        }

        Canvas FindCanvas()
        {
            var t = _host != null ? _host.GetComponentInChildren<Canvas>() : null;
            if (t != null) return t;
            var all = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            return all.FirstOrDefault(c => c.isRootCanvas);
        }

        // ---------------------------------------------------------------- Eingabe

        void Update()
        {
            if (!Active || _rig == null || _rig.Ragdoll == null || _cam == null) return;
            if (_rig.Running || _rig.Playing) return;
            bool down, held, up; Vector2 mouse;
#if ENABLE_INPUT_SYSTEM
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) return;
            down = m.leftButton.wasPressedThisFrame; held = m.leftButton.isPressed; up = !held && _grabbed != null; mouse = m.position.ReadValue();
#else
            down = Input.GetMouseButtonDown(0); held = Input.GetMouseButton(0); up = !held && _grabbed != null; mouse = Input.mousePosition;
#endif
            var ray = _cam.ScreenPointToRay(mouse);
            if (_grabbed != null)
            {
                if (up) { Release(true); return; }
                _handTarget = ray.origin + ray.direction * _grabDepth;
                return;
            }
            if (!down || OverUi()) return;
            if (!Physics.Raycast(ray, out var hit, 200f, ~0)) return;
            if (_tool == null) Grab(hit, ray);
            else Use(_tool, hit, ray);
        }

        static bool OverUi()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        Rigidbody Segment(Collider c)
        {
            var rb = c != null ? c.attachedRigidbody : null;
            return rb != null && _rig.Ragdoll.Segments.ContainsValue(rb) ? rb : null;
        }

        void Grab(RaycastHit hit, Ray ray)
        {
            var seg = Segment(hit.collider);
            if (seg == null) return;
            var handGo = new GameObject("Hand");
            handGo.transform.position = hit.point;
            _hand = handGo.AddComponent<Rigidbody>();
            _hand.isKinematic = true;
            _grabbed = seg;
            _grabJoint = seg.gameObject.AddComponent<ConfigurableJoint>();
            _grabJoint.connectedBody = _hand;
            _grabJoint.autoConfigureConnectedAnchor = false;
            _grabJoint.anchor = seg.transform.InverseTransformPoint(hit.point);
            _grabJoint.connectedAnchor = Vector3.zero;
            _grabJoint.xMotion = _grabJoint.yMotion = _grabJoint.zMotion = ConfigurableJointMotion.Locked;
            _grabJoint.angularXMotion = _grabJoint.angularYMotion = _grabJoint.angularZMotion = ConfigurableJointMotion.Free;
            _grabDepth = Vector3.Dot(hit.point - ray.origin, ray.direction);
            _handTarget = hit.point;
            _handVel = Vector3.zero;
            foreach (var rb in _rig.Ragdoll.Segments.Values) rb.WakeUp();
        }

        void FixedUpdate()
        {
            if (_hand == null) return;
            Vector3 cur = _hand.position;
            Vector3 step = Vector3.ClampMagnitude(_handTarget - cur, MaxHandSpeed * Time.fixedDeltaTime);
            if (cur.y + step.y < 0.05f) step.y = 0.05f - cur.y;   // nicht in den Boden
            _hand.MovePosition(cur + step);
            _handVel = Vector3.Lerp(_handVel, step / Time.fixedDeltaTime, 0.5f);
        }

        /// <summary>Loslassen; mit Schwung oder aus der Höhe wird der Aufprall berechnet.</summary>
        void Release(bool compute)
        {
            if (_grabJoint != null)
            {
                // sofort wirkungslos (Destroy greift erst am Frame-Ende, die Berechnung beginnt sofort)
                _grabJoint.xMotion = _grabJoint.yMotion = _grabJoint.zMotion = ConfigurableJointMotion.Free;
                Destroy(_grabJoint);
            }
            if (_hand != null) Destroy(_hand.gameObject);
            var seg = _grabbed;
            _grabJoint = null; _hand = null; _grabbed = null;
            if (!compute || seg == null) return;
            float speed = CrashPhysics.Velocity(seg).magnitude;
            float height = _rig.Ragdoll.Segments.Values.Min(rb => rb.position.y);
            if (speed < 1.5f && height < 0.35f) return;   // nur abgelegt
            string seg0 = SegmentName(seg);
            StartImpact($"Körper an {seg0} gepackt und mit {speed:0.#} m/s geschleudert" + (height >= 0.35f ? $" (aus {height:0.#} m Höhe)" : ""), null);
        }

        string SegmentName(Rigidbody rb)
        {
            var s = rb != null ? rb.GetComponent<CrashSensor>() : null;
            return s != null ? (s.ModelSegment ?? s.Region) : rb != null ? rb.name : "?";
        }

        void Use(SandboxObject o, RaycastHit hit, Ray ray)
        {
            float s = _speed[o.id];
            double skin = o.Sharp ? ImpactModel.SkinPenetrationForce(_model, o, true) : 0;
            var seg = Segment(hit.collider);
            string where = seg != null ? SegmentName(seg) : "Boden";
            Vector3 toCam = (_cam.transform.position - hit.point).normalized;
            switch (o.mode)
            {
                case "statisch":
                {
                    // auf den Boden stellen, neben den Körper, wenn er getroffen wurde
                    Vector3 p = new Vector3(hit.point.x, 0, hit.point.z);
                    if (seg != null) p += Vector3.ProjectOnPlane(toCam, Vector3.up).normalized * 0.6f;
                    var size = new Vector3(o.size_m[0], o.size_m[1], o.size_m[2]);
                    Quaternion yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(-toCam, Vector3.up).normalized + new Vector3(0, 0, 1e-4f));
                    if (o.id == "tischkante") p.y = 0.75f;   // Tischplatte in Hüfthöhe (frei schwebend, vereinfacht)
                    else if (size.y > size.z && size.y >= size.x) { p.y = size.y / 2; yaw = Quaternion.LookRotation(Vector3.up, Vector3.forward); }   // Pfahl: Spitze nach oben
                    else p.y = size.y / 2;
                    _rig.SpawnObject(o, p, yaw, Vector3.zero, false, skin);
                    _actions.Add($"{o.label} aufgestellt");
                    SelectTool(null);
                    return;
                }
                case "werfen":
                {
                    Vector3 start = hit.point + toCam * 2.2f + Vector3.up * 0.4f;
                    float T = Vector3.Distance(start, hit.point) / Mathf.Max(1, s);
                    Vector3 v = (hit.point - start) / T + 0.5f * 9.81f * T * Vector3.up;   // ballistisch auf den Klickpunkt
                    v = v.normalized * Mathf.Max(s, v.magnitude * 0.98f);
                    _rig.SpawnObject(o, start, Quaternion.LookRotation(v), v, true, skin);
                    StartImpact($"{o.label} mit {s:0.#} m/s auf {where} geworfen", null);
                    return;
                }
                default: // gefuehrt: Schlag bzw. Stich aus 35 cm, wirksame Masse von Arm und Gegenstand
                {
                    Vector3 dir = -toCam;
                    Vector3 start = hit.point - dir * 0.35f;
                    Quaternion rot = o.Sharp || o.shape != "capsule" ? Quaternion.LookRotation(dir)
                                   : Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(dir, Vector3.up) : Vector3.right, -dir); // Schläger quer
                    var info = _rig.SpawnObject(o, start, rot, dir * s, false, skin);
                    float contactAt = -1;
                    bool released = false;
                    Action<float, float> perStep = (t, dt) =>
                    {
                        if (released || info == null || info.Collider == null) return;
                        if (contactAt < 0 && _rig.CrashStart > 0) contactAt = _rig.CrashStart;
                        bool done = (contactAt >= 0 && t > contactAt + (o.Sharp ? 0.15f : 0.03f)) || t > 0.6f;
                        if (!done) return;
                        released = true;
                        if (o.Sharp && info.Body != null && _rig.Penetrations != null && _rig.Penetrations.IsActive(info))
                        {
                            // Hand lässt los: Klinge allein (Masse ~150 g) bleibt im Stichkanal
                            info.Body.mass = 0.15f; info.Mass_kg = 0.15f;
                            info.Body.useGravity = true;
                            return;
                        }
                        // zurückziehen: kein Kontakt mehr, in der Wiedergabe sichtbar; nach der Berechnung entfernt
                        info.Collider.enabled = false;
                        if (info.Body != null) { CrashPhysics.SetVelocity(info.Body, -dir * 2.5f); info.Body.angularVelocity = Vector3.zero; }
                        _retracted.Add(info);
                    };
                    StartImpact($"{o.label} mit {s:0.#} m/s gegen {where}", perStep);
                    return;
                }
            }
        }

        void StartImpact(string action, Action<float, float> perStep)
        {
            _run++;
            _actions.Add(action);
            _host.SetGewaltResult(null, null, $"Aufprall wird berechnet …\n<size=80%>{action}</size>");
            StartCoroutine(_rig.RunImpact(_host.SlowMotion, rec => OnImpact(rec, action),
                f => _host.SetGewaltResult(null, null, $"Aufprall wird berechnet … {f:P0}\n<size=80%>{action}\n0,1-ms-Schritte während jedes Kontakts, sonst 1 ms.</size>"),
                perStep));
        }

        // ---------------------------------------------------------------- Auswertung

        readonly List<SoftContact.ObjectInfo> _retracted = new List<SoftContact.ObjectInfo>();

        void OnImpact(ImpactRecord rec, string action)
        {
            foreach (var r in _retracted) _rig.RemoveObject(r);
            _retracted.Clear();
            _blunt.AddRange(rec.Blunt);
            var rng = new System.Random(_p.Seed);
            var pens = _rig.Penetrations != null ? _rig.Penetrations.Results.ToList() : new List<PenetrationResult>();
            var direct = ImpactModel.Evaluate(_model, _blunt, _allPenetrations.Concat(pens), () => rng.NextDouble());

            // sichtbare Zeichen: Prellmarken/Platzwunden aus den Kontakten dieses Wurfs, Stichwunden aus neuen Stichkanälen
            var marks = _host.GenerateContactMarks();
            foreach (var m in marks) { m.Mark.id = $"g{_run}_{m.Mark.id}"; _marks.Add(m); }
            for (int i = _stabMarks; i < pens.Count; i++)
            {
                var pr = pens[i];
                if (pr.Depth_m < 0.003) continue;
                var label = _objects.objects.FirstOrDefault(x => x.id == pr.ObjectId)?.label ?? "Gegenstand";
                var sm = Anatomy.CrashMarks.Stab(pr.Segment, pr.Region, pr.ModelPoint, pr.ModelInside, label, pr.Depth_m, pr.Embedded, _run * 100 + i);
                if (sm != null) _marks.Add(sm);
            }
            _stabMarks = pens.Count;

            var db = InjuryDatabase.Load();
            var mech = InjuryDatabase.LoadMechanisms();
            var risk = JsonUtility.FromJson<InjuryRiskDatabaseFile>(File.ReadAllText(Path.Combine(InjuryDatabase.Root, "injury_risk.json")));
            var setup = new CrashSetup { MechanismId = "gewalt", Male = _p.Male, Age = 35, Weight_kg = _p.Mass_kg, Height_cm = _p.Height_m * 100, Rescue_min = 15 };
            string description = $"{(_p.Male ? "Mann" : "Frau")}, ca. 35 Jahre, nach Gewalteinwirkung liegend aufgefunden: " +
                                 string.Join("; ", _actions) + ".";
            var result = InjuryRisk.BuildScenario(risk, mech, db.All, new List<CrashMeasurement>(), setup, (uint)_p.Seed, direct,
                                                  "Stumpfe/spitze Gewalt", description);

            var sb = new StringBuilder();
            sb.AppendLine($"<b>Einwirkung {_run}</b>: {action}");
            sb.AppendLine($"<size=80%>{rec.Duration_s:0.00} s berechnet ({rec.FineSteps} feine, {rec.CoarseSteps} grobe Schritte)</size>");
            var top = rec.Blunt.GroupBy(b => b.Segment).Select(g => g.OrderByDescending(x => x.PeakForce_N).First())
                               .OrderByDescending(b => b.PeakForce_N).Take(6).ToList();
            if (top.Count > 0)
            {
                sb.AppendLine("<b>Stärkste Kontakte</b>");
                foreach (var b in top)
                    sb.AppendLine($"{b.Segment}: {b.PeakForce_N / 1000:0.0} kN auf {b.Area_cm2:0} cm² ({Surface(b.Surface)})" +
                                  (b.MaxIndentation_m > 0.002 ? $", eingedrückt {b.MaxIndentation_m * 1000:0} mm" : ""));
            }
            if (pens.Count > 0)
            {
                sb.AppendLine("<b>Stichkanäle</b>");
                foreach (var pr in pens)
                    sb.AppendLine($"{pr.Segment}: {pr.Depth_m * 100:0.#} cm tief" + (pr.HitBone ? ", auf Knochen gestoppt" : "") + (pr.Embedded ? ", steckt" : ""));
            }
            sb.AppendLine();
            sb.AppendLine("<b>Direkte Verletzungen (alle Einwirkungen)</b>");
            if (direct.Count == 0) sb.AppendLine("keine über der Schwelle");
            foreach (var d in direct.OrderByDescending(x => x.Probability))
                sb.AppendLine($"{db.Get(d.InjuryId)?.name ?? d.InjuryId}: {d.Probability:P0} <size=80%>({d.Reason})</size>");
            if (_marks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>Sichtbare Zeichen</b>");
                foreach (var cm in _marks.Take(8)) sb.AppendLine(cm.Finding);
                if (_marks.Count > 8) sb.AppendLine($"… und {_marks.Count - 8} weitere");
            }
            sb.AppendLine();
            sb.AppendLine("<size=80%>Kontaktkraft aus nachgiebigem Gewebe (Allsop, Melvin, Hardy); Frakturen nach Kraft/Fläche bzw. Biegemoment, Stiche nach Ní Annaidh. Qualitativ — siehe docs/LITERATUR.md.</size>");
            _lastSummary = sb.ToString();
            _host.SetGewaltResult(result, _marks.ToList(), _lastSummary);
        }

        // Stichkanäle früherer Gegenstände (nach „Gegenstände entfernen“) behalten
        readonly List<PenetrationResult> _allPenetrations = new List<PenetrationResult>();

        string Surface(string s) =>
            s == null ? "?" : s == "ground" ? "Boden"
            : s.StartsWith("object:") ? (_objects.objects.FirstOrDefault(x => x.id == s.Substring(7))?.label ?? s.Substring(7)) : s;
    }
}

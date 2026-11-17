using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Zielen und Rückmeldung der Gewalt-Sandbox:
    /// · Ziel ist die sichtbare Haut (Strahl gegen die gebackene Haut, <see cref="MeshPick"/>), nicht die Kapseln des Ragdolls.
    /// · Vorschau unter der Maus: Zielring auf der Haut, der Gegenstand halb durchsichtig in der Ausgangslage, Bahn bis zum
    ///   Ziel und ein Hinweis am Mauszeiger (was passiert, welches Körperteil, wie schnell).
    /// · Nach jeder Einwirkung eine Marke am getroffenen Körperteil mit dem Ergebnis (Stichtiefe, Kraft, neue Verletzungen);
    ///   sie wandert mit dem Körperteil, auch in der Wiedergabe.
    /// </summary>
    public partial class ImpactSandbox
    {
        /// <summary>Getroffene Stelle: auf der Haut (Körperteil gesetzt) oder auf Boden/Gegenstand.</summary>
        class AimHit
        {
            public Vector3 Point, Normal;
            public Rigidbody Segment;
            public float Distance;
        }

        /// <summary>Wie eine Einwirkung ausgeführt wird — dieselbe Rechnung für Vorschau und Ausführung.</summary>
        class Plan
        {
            public string Verb;              // „Stich“, „Schlag“, „Wurf“, „Schnitt“, „Aufstellen“
            public Vector3 Start, Velocity, Dir, Tangent;
            public Quaternion Rot;
            public readonly List<Vector3> Path = new List<Vector3>();
        }

        // ---------------------------------------------------------------- Haut als Ziel

        Mesh _bake;
        float[] _skinVerts, _boxMin, _boxMax;
        int[] _skinTris;
        Vector3 _bakedPelvis, _bakedHead;
        bool _skinDirty = true;
        readonly Dictionary<Rigidbody, Collider[]> _segColliders = new Dictionary<Rigidbody, Collider[]>();

        void SkinChanged() { _skinDirty = true; _segColliders.Clear(); _lastAimValid = false; }

        bool BakeSkin()
        {
            var rd = _rig.Ragdoll;
            var skin = rd != null ? rd.Skin : null;
            if (skin == null || !skin.enabled || !skin.gameObject.activeInHierarchy) return false;
            bool moved = (rd.Pelvis.position - _bakedPelvis).sqrMagnitude > 1e-4f || (rd.Head.position - _bakedHead).sqrMagnitude > 1e-4f;
            if (!_skinDirty && !moved && _skinVerts != null) return true;
            if (_bake == null) _bake = new Mesh();
            skin.BakeMesh(_bake, true);
            var v = _bake.vertices;
            var tr = skin.transform;
            Vector3 pos = tr.position; Quaternion rot = tr.rotation;
            if (_skinVerts == null || _skinVerts.Length != v.Length * 3) _skinVerts = new float[v.Length * 3];
            for (int i = 0; i < v.Length; i++)
            {
                var w = rot * v[i] + pos;
                _skinVerts[3 * i] = w.x; _skinVerts[3 * i + 1] = w.y; _skinVerts[3 * i + 2] = w.z;
            }
            _skinTris = _bake.triangles;
            MeshPick.Bounds(_skinVerts, out _boxMin, out _boxMax);
            _bakedPelvis = rd.Pelvis.position; _bakedHead = rd.Head.position;
            _skinDirty = false;
            return true;
        }

        Ray _lastRay;
        AimHit _lastAim;
        bool _lastAimValid;

        /// <summary>Wie <see cref="AimAtUncached"/>, aber nur neu gerechnet, wenn sich Maus, Kamera oder Körper bewegt haben
        /// (60 000 Dreiecke je Bild wären unnötig).</summary>
        AimHit AimAt(Ray ray)
        {
            bool same = _lastAimValid && !_skinDirty && (ray.origin - _lastRay.origin).sqrMagnitude < 1e-8f && (ray.direction - _lastRay.direction).sqrMagnitude < 1e-10f
                        && _rig.Ragdoll != null && (_rig.Ragdoll.Pelvis.position - _bakedPelvis).sqrMagnitude <= 1e-4f;
            if (same) return _lastAim;
            _lastAim = AimAtUncached(ray); _lastRay = ray; _lastAimValid = true;
            return _lastAim;
        }

        /// <summary>Haut zuerst; Boden und Gegenstände nur, wenn sie näher liegen (z. B. eine Wand vor dem Körper).</summary>
        AimHit AimAtUncached(Ray ray)
        {
            AimHit best = null;
            if (BakeSkin() && MeshPick.HitsBox(_boxMin, _boxMax, 0.05f, ray.origin.x, ray.origin.y, ray.origin.z, ray.direction.x, ray.direction.y, ray.direction.z)
                && MeshPick.Raycast(_skinVerts, _skinTris, ray.origin.x, ray.origin.y, ray.origin.z, ray.direction.x, ray.direction.y, ray.direction.z,
                                    out double t, out _, out double nx, out double ny, out double nz))
            {
                var p = ray.origin + ray.direction * (float)t;
                best = new AimHit { Point = p, Normal = new Vector3((float)nx, (float)ny, (float)nz), Segment = NearestSegment(p), Distance = (float)t };
            }
            if (Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            {
                var seg = Segment(hit.collider);
                bool closerThing = seg == null && (best == null || hit.distance < best.Distance - 0.01f);
                if (best == null && seg != null || closerThing)
                    best = new AimHit { Point = hit.point, Normal = hit.normal, Segment = seg, Distance = hit.distance };
            }
            return best;
        }

        /// <summary>Körperteil, dessen Kollider dem Hautpunkt am nächsten liegt.</summary>
        Rigidbody NearestSegment(Vector3 p)
        {
            Rigidbody best = null; float bestD = float.MaxValue;
            foreach (var rb in _rig.Ragdoll.Segments.Values)
            {
                if (rb == null) continue;
                if (!_segColliders.TryGetValue(rb, out var cols)) _segColliders[rb] = cols = rb.GetComponentsInChildren<Collider>().Where(c => c.attachedRigidbody == rb).ToArray();
                foreach (var c in cols)
                {
                    if (c == null || !c.enabled) continue;
                    float d = (c.ClosestPoint(p) - p).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = rb; }
                }
            }
            return best;
        }

        // ---------------------------------------------------------------- Ausführung planen

        Plan MakePlan(SandboxObject o, AimHit a, bool slash)
        {
            var plan = new Plan();
            float s = _speed[o.id];
            Vector3 toCam = (_cam.transform.position - a.Point).normalized;
            if (slash && o.HasEdge)
            {
                // Schneide liegt auf der Haut (lokale x-Achse in den Körper) und wird quer zur Blickrichtung gezogen
                const float stroke_m = 0.2f;
                float sv = Mathf.Clamp(s, 0.5f, 3f);
                Vector3 dir = a.Segment != null ? -a.Normal : -toCam;
                Vector3 tan = Vector3.Cross(dir, Vector3.up);
                if (tan.sqrMagnitude < 1e-4f) tan = Vector3.Cross(dir, Vector3.forward);
                tan = tan.normalized;
                plan.Verb = "Schnitt";
                plan.Dir = dir; plan.Tangent = tan;
                plan.Rot = Quaternion.LookRotation(tan, Vector3.Cross(tan, dir));
                plan.Start = a.Point - tan * (stroke_m / 2) - dir * 0.01f;
                plan.Velocity = tan * sv + dir * 0.3f;
                plan.Path.Add(plan.Start); plan.Path.Add(a.Point + tan * (stroke_m / 2));
                return plan;
            }
            switch (o.mode)
            {
                case "statisch":
                {
                    Vector3 p = new Vector3(a.Point.x, 0, a.Point.z);
                    if (a.Segment != null) p += Vector3.ProjectOnPlane(toCam, Vector3.up).normalized * 0.6f;   // neben den Körper
                    var size = o.size_m != null && o.size_m.Length == 3 ? new Vector3(o.size_m[0], o.size_m[1], o.size_m[2]) : Vector3.one * 0.3f;
                    Quaternion yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(-toCam, Vector3.up).normalized + new Vector3(0, 0, 1e-4f));
                    if (o.supportHeight_m > 0) p.y = (float)o.supportHeight_m - size.y / 2;
                    else if (size.y > size.z && size.y >= size.x) { p.y = size.y / 2; yaw = Quaternion.LookRotation(Vector3.up, Vector3.forward); }
                    else p.y = size.y / 2;
                    plan.Verb = "Aufstellen"; plan.Start = p; plan.Rot = yaw;
                    return plan;
                }
                case "werfen":
                {
                    Vector3 start = a.Point + toCam * 2.2f + Vector3.up * 0.4f;
                    float T = Vector3.Distance(start, a.Point) / Mathf.Max(1, s);
                    Vector3 v = (a.Point - start) / T + 0.5f * 9.81f * T * Vector3.up;   // ballistisch auf den Zielpunkt
                    v = v.normalized * Mathf.Max(s, v.magnitude * 0.98f);
                    plan.Verb = "Wurf"; plan.Start = start; plan.Velocity = v; plan.Dir = v.normalized; plan.Rot = Quaternion.LookRotation(v);
                    // Flugbahn für die Vorschau (bis zum Ziel)
                    float tEnd = T * 1.05f;
                    for (int i = 0; i <= 16; i++)
                    {
                        float tt = tEnd * i / 16f;
                        plan.Path.Add(start + v * tt + 0.5f * Physics.gravity * tt * tt);
                    }
                    return plan;
                }
                default:   // gefuehrt: Schlag bzw. Stich aus 35 cm
                {
                    // Stich überwiegend senkrecht in die Haut (Flächennormale der sichtbaren Haut), etwas aus Blickrichtung
                    Vector3 dir = o.Sharp && a.Segment != null ? -(a.Normal * 0.75f + toCam * 0.25f).normalized : -toCam;
                    plan.Verb = o.Sharp ? "Stich" : "Schlag";
                    plan.Dir = dir;
                    plan.Start = a.Point - dir * 0.35f;
                    plan.Rot = o.Sharp || o.shape != "capsule" ? Quaternion.LookRotation(dir)
                             : Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(dir, Vector3.up) : Vector3.right, -dir);
                    plan.Velocity = dir * s;
                    plan.Path.Add(plan.Start); plan.Path.Add(a.Point);
                    return plan;
                }
            }
        }

        // ---------------------------------------------------------------- Vorschau

        GameObject _ring, _ghost;
        string _ghostFor;
        LineRenderer _path;
        RectTransform _tipBox;
        TextMeshProUGUI _tip;
        Canvas _canvas;

        static GameObject Visual(PrimitiveType type, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>();
            if (c != null) { c.enabled = false; Destroy(c); }
            go.GetComponent<Renderer>().material = CrashPhysics.Mat(color);
            return go;
        }

        void EnsurePreview()
        {
            if (_ring == null)
            {
                _ring = Visual(PrimitiveType.Cylinder, new Color(1f, 0.85f, 0.2f, 0.75f));
                _ring.name = "Zielring";
            }
            if (_path == null)
            {
                var go = new GameObject("Bahn");
                _path = go.AddComponent<LineRenderer>();
                _path.useWorldSpace = true;
                _path.widthMultiplier = 0.012f;
                _path.material = CrashPhysics.Mat(new Color(1f, 0.85f, 0.2f, 0.8f));
            }
            if (_tipBox == null)
            {
                _canvas = FindCanvas();
                if (_canvas != null)
                {
                    _tipBox = UI.RuntimeUi.Panel(_canvas.transform, "Zielhinweis", new Color(0.05f, 0.07f, 0.1f, 0.88f));
                    _tipBox.GetComponent<Image>().raycastTarget = false;
                    _tipBox.anchorMin = _tipBox.anchorMax = Vector2.zero;
                    _tipBox.pivot = Vector2.zero;
                    UI.RuntimeUi.Column(_tipBox, 2, 8);
                    var fit = _tipBox.gameObject.AddComponent<ContentSizeFitter>();
                    fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    _tip = UI.RuntimeUi.Label(_tipBox, "", 15, Color.white);
                }
            }
        }

        void Ghost(SandboxObject o)
        {
            if (_ghost != null && _ghostFor == o.id) return;
            if (_ghost != null) Destroy(_ghost);
            _ghostFor = o.id;
            var size = o.size_m != null && o.size_m.Length == 3 ? new Vector3(o.size_m[0], o.size_m[1], o.size_m[2]) : Vector3.one * 0.1f;
            if (o.mode != "statisch" && size.y > size.z && size.y >= size.x) size = new Vector3(size.x, size.z, size.y);   // wie SpawnObject
            var col = o.color != null && o.color.Length >= 3 ? new Color(o.color[0], o.color[1], o.color[2], 0.45f) : new Color(0.6f, 0.6f, 0.6f, 0.45f);
            _ghost = new GameObject("Vorschau " + o.label);
            GameObject vis;
            switch (o.shape)
            {
                case "sphere": vis = Visual(PrimitiveType.Sphere, col); vis.transform.localScale = Vector3.one * size.x; break;
                case "capsule": vis = Visual(PrimitiveType.Capsule, col); vis.transform.localScale = new Vector3(size.x, size.z / 2, size.x); vis.transform.localRotation = Quaternion.Euler(90, 0, 0); break;
                default: vis = Visual(PrimitiveType.Cube, col); vis.transform.localScale = size; break;
            }
            vis.transform.SetParent(_ghost.transform, false);
        }

        void HidePreview()
        {
            if (_ring) _ring.SetActive(false);
            if (_ghost) _ghost.SetActive(false);
            if (_path) _path.enabled = false;
            if (_tipBox) _tipBox.gameObject.SetActive(false);
        }

        void ShowPreview(AimHit a, bool shift, Vector2 mouse)
        {
            EnsurePreview();
            if (_tool == null) { HidePreview(); return; }
            if (a == null) { ShowTip(mouse, $"<b>{_tool.label}</b>\n<color=#aaa>Kein Ziel unter dem Mauszeiger</color>"); _ring.SetActive(false); if (_ghost) _ghost.SetActive(false); _path.enabled = false; return; }
            bool slash = shift && _tool.HasEdge;
            var plan = MakePlan(_tool, a, slash);
            var color = _tool.Sharp ? new Color(0.95f, 0.25f, 0.2f, 0.8f) : new Color(1f, 0.85f, 0.2f, 0.8f);

            // Zielring flach auf der Haut, so groß wie die Auftreffstelle des Gegenstands
            float d = _tool.Sharp ? 0.035f : Mathf.Clamp(_tool.size_m != null && _tool.size_m.Length > 0 ? _tool.size_m.Min() : 0.06f, 0.04f, 0.15f);
            _ring.SetActive(_tool.mode != "statisch");
            _ring.transform.SetPositionAndRotation(a.Point + a.Normal * 0.004f, Quaternion.FromToRotation(Vector3.up, a.Normal));
            _ring.transform.localScale = new Vector3(d, 0.002f, d);
            _ring.GetComponent<Renderer>().material.color = color;

            Ghost(_tool);
            _ghost.SetActive(true);
            _ghost.transform.SetPositionAndRotation(plan.Start, plan.Rot);

            _path.enabled = plan.Path.Count > 1;
            if (_path.enabled)
            {
                _path.positionCount = plan.Path.Count;
                for (int i = 0; i < plan.Path.Count; i++) _path.SetPosition(i, plan.Path[i]);
                _path.material.color = color;
            }

            string where = a.Segment != null ? SegmentName(a.Segment) : "Boden/Gegenstand";
            string speed = _tool.mode == "statisch" ? "" : slash ? $" · {Mathf.Clamp(_speed[_tool.id], 0.5f, 3f):0.#} m/s" : $" · {_speed[_tool.id]:0.#} m/s";
            string angle = "";
            if (_tool.Sharp && a.Segment != null && !slash)
            {
                // Spitze dringt nur ein, wenn sie bis ca. 45° schräg auftrifft (SoftContact): sonst gleitet sie ab
                float ang = Vector3.Angle(-plan.Dir, a.Normal);
                angle = ang <= 40 ? $" · {ang:0}° zur Haut" : $" · <color=#fc6>{ang:0}° zur Haut — gleitet eher ab</color>";
            }
            var hints = new List<string> { "Klick: ausführen" };
            if (_tool.mode != "statisch") hints.Add("+/−: Tempo");
            if (_tool.HasEdge) hints.Add(slash ? "Umschalt loslassen: Stich" : "Umschalt: Schnitt");
            if (_tool.mode == "statisch") hints.Add("dann den Körper dagegen schleudern");
            string path = "";
            if (_tool.Sharp && !slash && a.Segment != null && _tool.mode != "statisch")
            {
                string along = ChannelPreview(a, plan.Dir, _tool);
                if (along != null) path = "\n" + along;
            }
            ShowTip(mouse, $"<b>{plan.Verb}: {_tool.label}</b>{speed}{angle}\nZiel: {where}{path}\n<size=85%><color=#bbb>{string.Join(" · ", hints)}</color></size>");
        }

        void ShowTip(Vector2 mouse, string text)
        {
            if (_tipBox == null) return;
            _tipBox.gameObject.SetActive(true);
            _tip.text = text;
            float k = _canvas != null && _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1;
            _tipBox.anchoredPosition = (mouse + new Vector2(22, 18)) / k;
        }

        // ---------------------------------------------------------------- Strukturen in der Bahn (Anatomiemodell)

        AimHit _channelFor;
        Vector3 _channelDir;
        string _channelText;

        /// <summary>Was läge in der Bahn der Klinge (bis zur Klingenlänge)? Aus dem Anatomiemodell in Ruhelage.</summary>
        string ChannelPreview(AimHit a, Vector3 dir, SandboxObject o)
        {
            if (a == _channelFor && (dir - _channelDir).sqrMagnitude < 1e-8f) return _channelText;
            _channelFor = a; _channelDir = dir; _channelText = null;
            var sensor = a.Segment != null ? a.Segment.GetComponent<CrashSensor>() : null;
            if (sensor == null || !sensor.HasModelFrame || sensor.ModelScale <= 0) return null;
            if (!sensor.ToModel(a.Point, out var mp, out _) || !sensor.ToModelDirection(dir, out var md)) return null;
            Anatomy.AnatomyData data;
            try { data = Anatomy.AnatomyBuilder.Load(); } catch (Exception) { return null; }
            double blade = o.bladeLength_m > 0 ? o.bladeLength_m : 0.1;
            var hits = Anatomy.StabChannel.Trace(data, new[] { mp.x, mp.y, mp.z }, new[] { md.x, md.y, md.z }, blade / sensor.ModelScale);
            string list = Anatomy.StabChannel.Describe(hits, sensor.ModelScale);
            _channelText = $"<size=90%>In der Bahn (Klinge {blade * 100:0} cm): {(list.Length > 0 ? list : "nur Weichteile")}</size>";
            return _channelText;
        }

        /// <summary>Strukturen, die ein Stichkanal bis zu seiner Tiefe durchquert (leer ohne Modellbezug).</summary>
        static string ChannelThrough(PenetrationResult pr)
        {
            if (pr?.ModelPoint == null || pr.ModelDir == null || pr.ModelScale <= 0 || pr.Depth_m <= 0) return "";
            Anatomy.AnatomyData data;
            try { data = Anatomy.AnatomyBuilder.Load(); } catch (Exception) { return ""; }
            var hits = Anatomy.StabChannel.Trace(data, pr.ModelPoint, pr.ModelDir, pr.Depth_m / pr.ModelScale);
            return Anatomy.StabChannel.Describe(hits, pr.ModelScale);
        }

        // ---------------------------------------------------------------- Marken nach der Einwirkung

        class HitMarker
        {
            public Rigidbody Segment;
            public Vector3 Local, World;
            public GameObject Dot;
            public RectTransform Box;
            public TextMeshProUGUI Label;
            public float Born;
            public bool Sharp;            // spitzer Gegenstand geführt (Stich erwartet)
            public double SkinForce_N;    // Durchstichkraft der Haut für diesen Gegenstand
        }

        readonly List<HitMarker> _markers = new List<HitMarker>();
        HitMarker _pending;
        Dictionary<string, double> _directBefore = new Dictionary<string, double>();
        const int MaxMarkers = 4;

        /// <summary>Vor dem Start einer Einwirkung: wo sie den Körper treffen soll (für die Marke danach).</summary>
        void ExpectHit(AimHit a, SandboxObject o = null, bool slash = false)
        {
            if (_pending != null && _pending.Dot) Destroy(_pending.Dot);
            _pending = new HitMarker { Segment = a.Segment, World = a.Point, Local = a.Segment != null ? a.Segment.transform.InverseTransformPoint(a.Point) : a.Point, Born = Time.time,
                                       Sharp = o != null && o.Sharp && !slash && o.mode != "statisch",
                                       SkinForce_N = o != null && o.Sharp ? ImpactModel.SkinPenetrationForce(_model, o, true) : 0 };
            // Ziel bleibt während Berechnung und Wiedergabe als weißer Punkt sichtbar
            _pending.Dot = Visual(PrimitiveType.Sphere, new Color(1, 1, 1, 0.8f));
            _pending.Dot.name = "Ziel";
            _pending.Dot.transform.localScale = Vector3.one * 0.025f;
            _pending.Dot.transform.position = a.Point;
            HidePreview();
        }

        void ClearMarkers()
        {
            foreach (var m in _markers) { if (m.Dot) Destroy(m.Dot); if (m.Box) Destroy(m.Box.gameObject); }
            _markers.Clear();
            if (_pending != null && _pending.Dot) Destroy(_pending.Dot);
            _pending = null;
            _directBefore = new Dictionary<string, double>();
        }

        /// <summary>Marke mit dem Ergebnis dieser Einwirkung an die getroffene Stelle setzen.</summary>
        void PlaceMarker(ImpactRecord rec, List<PenetrationResult> newPens, List<DirectInjury> direct, InjuryDatabase db)
        {
            var m = _pending; _pending = null;
            if (m == null) return;
            var lines = new List<string>();
            foreach (var pr in newPens)
            {
                lines.Add($"Stich {pr.Segment}: {pr.Depth_m * 100:0.#} cm" + (pr.BonePierced ? ", durch Knochen" : pr.HitBone ? ", auf Knochen" : "") + (pr.Embedded ? ", steckt" : ""));
                string through = ChannelThrough(pr);
                if (through.Length > 0) lines.Add($"<size=90%>durch: {through}</size>");
            }
            foreach (var c in rec.Cuts)
                lines.Add($"Schnitt {c.Segment}: {c.Length_m * 100:0.#} cm, {c.Depth_m * 1000:0} mm tief");
            var top = rec.Blunt.OrderByDescending(b => b.PeakForce_N).FirstOrDefault();
            if (top != null && top.PeakForce_N > 200) lines.Add($"{top.Segment}: {top.PeakForce_N / 1000:0.0} kN");
            if (rec.Hic15 >= 50) lines.Add($"HIC {rec.Hic15:0}");

            double worst = 0;
            foreach (var d in direct.OrderByDescending(x => x.Probability))
            {
                _directBefore.TryGetValue(d.InjuryId, out double before);
                if (d.Probability - before < 0.05) continue;
                worst = Math.Max(worst, d.Probability);
                lines.Add($"<b>{db.Get(d.InjuryId)?.name ?? d.InjuryId}</b> {d.Probability:P0}");
            }
            _directBefore = direct.ToDictionary(x => x.InjuryId, x => x.Probability);

            bool contact = rec.Blunt.Count > 0 || newPens.Count > 0 || rec.Cuts.Count > 0;
            if (!contact) lines.Add("<color=#bbb>kein Kontakt — verfehlt</color>");
            else if (m.Sharp && newPens.Count == 0)
                lines.Add($"<color=#fc6>nicht eingedrungen</color> <size=85%>(Spitze {(top != null ? top.PeakForce_N : 0):0} N, Haut braucht ca. {m.SkinForce_N:0} N — steiler oder schneller)</size>");
            else if (worst < 0.05) lines.Add("<color=#bbb>keine Verletzung über der Schwelle</color>");
            var color = worst >= 0.5 ? new Color(0.95f, 0.25f, 0.2f) : worst >= 0.1 ? new Color(1f, 0.6f, 0.15f) : new Color(0.4f, 0.85f, 0.45f);

            if (m.Dot == null) m.Dot = Visual(PrimitiveType.Sphere, color);
            else m.Dot.GetComponent<Renderer>().material.color = color;
            m.Dot.name = "Treffer";
            m.Dot.transform.localScale = Vector3.one * 0.03f;
            if (_canvas == null) _canvas = FindCanvas();
            if (_canvas != null)
            {
                m.Box = UI.RuntimeUi.Panel(_canvas.transform, "Treffer", new Color(0.05f, 0.07f, 0.1f, 0.85f));
                m.Box.GetComponent<Image>().raycastTarget = false;
                m.Box.anchorMin = m.Box.anchorMax = Vector2.zero; m.Box.pivot = new Vector2(0, 0);
                UI.RuntimeUi.Column(m.Box, 1, 6);
                var fit = m.Box.gameObject.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                m.Label = UI.RuntimeUi.Label(m.Box, $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>●</color> <b>{_run}</b>  " + string.Join("\n", lines), 14, Color.white);
            }
            m.Born = Time.time;
            _markers.Add(m);
            while (_markers.Count > MaxMarkers)
            {
                var old = _markers[0];
                if (old.Dot) Destroy(old.Dot);
                if (old.Box) Destroy(old.Box.gameObject);
                _markers.RemoveAt(0);
            }
        }

        void LateUpdate()
        {
            if (!Active || _cam == null) return;
            float k = _canvas != null && _canvas.scaleFactor > 0 ? _canvas.scaleFactor : 1;
            if (_pending != null && _pending.Dot)
                _pending.Dot.transform.position = _pending.Segment != null ? _pending.Segment.transform.TransformPoint(_pending.Local) : _pending.World;
            for (int i = 0; i < _markers.Count; i++)
            {
                var m = _markers[i];
                Vector3 w = m.Segment != null ? m.Segment.transform.TransformPoint(m.Local) : m.World;
                if (m.Dot)
                {
                    m.Dot.transform.position = w;
                    float age = Time.time - m.Born;
                    m.Dot.transform.localScale = Vector3.one * (0.03f + (age < 1.2f ? 0.03f * Mathf.Abs(Mathf.Sin(age * 8)) : 0));   // kurz pulsieren
                }
                if (m.Box)
                {
                    var sp = _cam.WorldToScreenPoint(w);
                    bool visible = sp.z > 0;
                    m.Box.gameObject.SetActive(visible);
                    // ältere Marken etwas versetzt, damit sie sich nicht überdecken
                    if (visible) m.Box.anchoredPosition = new Vector2(sp.x + 14, sp.y + 10 + (_markers.Count - 1 - i) * 6) / k;
                    if (m.Label) m.Label.alpha = i == _markers.Count - 1 ? 1f : 0.7f;
                }
            }
        }
    }
}

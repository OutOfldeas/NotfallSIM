using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Scheibenbruch (siehe <see cref="GlassBreak"/>):
    /// – Während der Rechnung prüft jedes aufgezeichnete Bild, ob ein Körperteil die Frontscheibe berührt. Ab der
    ///   Schwellengeschwindigkeit senkrecht zur Scheibe reißt sie spinnennetzartig (Größe aus der Geschwindigkeit) und wölbt
    ///   sich in Bewegungsrichtung, so tief wie der Körper in die Scheibe einsinkt; danach bleibt sie halb so tief durchgebogen.
    /// – Nach der Rechnung: Seiten- und Heckscheiben zerfallen in dem Bild, in dem ihr Rahmen die Schwelle überschreitet;
    ///   die Würfel fliegen in den Innenraum. Wer auf dieser Seite sitzt, bekommt viele kleine Schnittwunden an Kopf und Arm.
    /// </summary>
    public partial class CrashRig
    {
        class ShieldWatch
        {
            public Collider Shield;
            public VehicleVisual Vis;
            public readonly Dictionary<Collider, Vector3> Prev = new Dictionary<Collider, Vector3>();
            public float PrevTime = float.NaN, MaxDepth, Speed;
            public bool Cracked;
        }

        class GlassExposure { public Ragdoll Rd; public CrashSensor Sensor; public Vector3 Local; public string Pane; }

        /// <summary>Was mit den Scheiben passiert ist (für den Bericht).</summary>
        public class GlassEvent { public float Time; public string Text; }

        List<ShieldWatch> _shields;
        List<Collider> _bodyColliders;
        readonly List<GlassEvent> _glassEvents = new List<GlassEvent>();
        readonly List<GlassExposure> _glassExposure = new List<GlassExposure>();
        public IReadOnlyList<GlassEvent> GlassEvents => _glassEvents;

        void BeginGlass()
        {
            _shields = null;
            _bodyColliders = null;
            _glassEvents.Clear();
            _glassExposure.Clear();
            foreach (var v in _visuals) v.ShieldDepthNow = 0;
        }

        void InitShieldWatches()
        {
            _shields = new List<ShieldWatch>();
            _bodyColliders = AllRagdolls().Where(rd => rd != null && rd.Root != null)
                .SelectMany(rd => rd.Root.GetComponentsInChildren<Collider>()).Where(c => c != null && !c.isTrigger).ToList();
            var cars = _visuals.Where(v => !v.Motorcycle && v.Profile != null && v.Go != null).ToList();
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                {
                    if (c.name != "Windschutzscheibe") continue;
                    // zugehöriges Modell: gleicher Elternknoten, sonst das nächste
                    var vis = cars.FirstOrDefault(v => v.Go.transform.parent == c.transform.parent)
                              ?? cars.OrderBy(v => (v.Go.transform.position - c.transform.position).sqrMagnitude).FirstOrDefault();
                    if (vis != null) _shields.Add(new ShieldWatch { Shield = c, Vis = vis });
                }
            }
        }

        /// <summary>Je aufgezeichnetem Bild: Kontakt mit der Frontscheibe, Riss, Wölbung.</summary>
        void StepGlass(float time)
        {
            if (_shields == null) InitShieldWatches();
            if (_shields.Count == 0) return;
            foreach (var w in _shields)
            {
                if (w.Shield == null || !w.Shield.enabled) continue;
                var st = w.Shield.transform;
                var b = w.Shield.bounds;
                b.Expand(0.05f);
                float depth = 0, dtPrev = time - w.PrevTime;
                foreach (var col in _bodyColliders)
                {
                    if (col == null || !col.enabled) continue;
                    var local = st.InverseTransformPoint(col.bounds.center);
                    Vector3 prev;
                    bool hadPrev = w.Prev.TryGetValue(col, out prev);
                    w.Prev[col] = local;
                    if (!b.Intersects(col.bounds)) continue;
                    Vector3 dir; float dist;
                    if (!Physics.ComputePenetration(w.Shield, st.position, st.rotation, col, col.transform.position, col.transform.rotation, out dir, out dist)) continue;
                    depth = Mathf.Max(depth, dist);
                    if (w.Cracked || !hadPrev || !(dtPrev > 1e-5f)) continue;
                    // Geschwindigkeit senkrecht zur Scheibe (Dicke = lokale y-Achse), relativ zum Fahrzeug
                    var vLocal = (local - prev) / dtPrev;
                    float vn = Mathf.Abs(vLocal.y);
                    if (vn < GlassBreak.CrackSpeed_m_s) continue;
                    w.Cracked = true; w.Speed = vn;
                    var c = col.bounds.center;
                    var n = st.up;
                    var onPlane = c - n * Vector3.Dot(c - st.position, n);
                    var m = w.Vis.Go.transform.InverseTransformPoint(onPlane);
                    float ny, nz;
                    VehicleShape.ShieldNormal(w.Vis.Profile, out ny, out nz);
                    var outward = w.Vis.Go.transform.TransformDirection(new Vector3(0, ny, nz));
                    var motion = st.TransformDirection(new Vector3(0, vLocal.y, 0));
                    float radius = GlassBreak.CrackRadius(vn);
                    float x = m.x, z = m.z;
                    VehicleShape.ClampToShield(w.Vis.Profile, ref x, ref z);
                    w.Vis.ShieldX = x; w.Vis.ShieldZ = z; w.Vis.ShieldSign = Vector3.Dot(motion, outward) >= 0 ? 1 : -1;
                    w.Vis.ShieldRadius = Mathf.Max(0.18f, radius * 1.3f);
                    w.Vis.HasShieldImpact = true;
                    w.Vis.AddCrack(x, z, radius, time, 7919 * (_glassEvents.Count + 1) + Mathf.RoundToInt(time * 1000));
                    string who = BodyPartLabel(col);
                    _glassEvents.Add(new GlassEvent
                    {
                        Time = time,
                        Text = $"Frontscheibe: Spinnennetz Ø {radius * 200:0} cm nach Anprall {who} mit {vn * 3.6:0} km/h, " +
                               (w.Vis.ShieldSign > 0 ? "nach außen gewölbt (Insasse)" : "nach innen gewölbt (von außen)") + " — an SHT und HWS-Verletzung denken"
                    });
                }
                w.MaxDepth = Mathf.Max(w.MaxDepth, depth);
                // Verbundglas: elastisch bis zum Riss, danach bleibt es durchgebogen (Annahme: halbe größte Tiefe, höchstens 12 cm)
                float shown = w.Cracked ? Mathf.Max(depth, 0.5f * w.MaxDepth) : depth;
                w.Vis.ShieldDepthNow = Mathf.Min(0.12f, shown);
                w.PrevTime = time;
            }
        }

        static string BodyPartLabel(Collider c)
        {
            var rb = c.attachedRigidbody;
            string n = rb != null ? rb.name : c.name;
            return n.StartsWith("Kopf") ? "des Kopfes" : n.StartsWith("Thorax") || n.StartsWith("Sternum") ? "des Brustkorbs" : n.StartsWith("Becken") ? "des Beckens"
                 : n.StartsWith("Oberarm") || n.StartsWith("Unterarm") ? "eines Arms" : n.StartsWith("Ober") || n.StartsWith("Unter") || n.StartsWith("Fuß") ? "eines Beins" : "des Körpers";
        }

        /// <summary>Nach der Rechnung: ESG-Scheiben, Würfel, Risse der Frontscheibe durch die Front-Verformung, Glasverletzungen.</summary>
        void PrepareGlass()
        {
            if (_frameTimes.Count < 2) return;
            var panes = new[] { Pane.LeftFront, Pane.LeftRear, Pane.RightFront, Pane.RightRear, Pane.Rear };
            var breaks = new List<(VehicleVisual vis, Pane pane, float t)>();
            int vi = 0;
            foreach (var vis in _visuals.ToList())
            {
                vi++;
                if (vis.Motorcycle || vis.Profile == null || vis.Frames.Count == 0) continue;
                int n = Mathf.Min(vis.Frames.Count, _frameTimes.Count);
                foreach (var pane in panes)
                    for (int i = 0; i < n; i++)
                        if (GlassBreak.Breaks(vis.Frames[i], vis.Profile, pane)) { vis.PaneBreak[(int)pane] = _frameTimes[i]; breaks.Add((vis, pane, _frameTimes[i])); break; }
                // Front stark verformt: A-Säulen verziehen die Frontscheibe, Risse von den unteren Ecken
                for (int i = 0; i < n; i++)
                    if (vis.Frames[i].Front >= GlassBreak.FrontCrush_m)
                    {
                        float hw = vis.Profile.Width / 2 * 0.84f - 0.25f, z = vis.Profile.CowlZ - 0.15f;
                        vis.AddCrack(-hw, z, 0.22f, _frameTimes[i], 31 * vi + 1);
                        vis.AddCrack(hw, z, 0.22f, _frameTimes[i] + 0.002f, 31 * vi + 2);
                        _glassEvents.Add(new GlassEvent { Time = _frameTimes[i], Text = "Frontscheibe: Risse von den unteren Ecken (Front stark verformt)" });
                        break;
                    }
            }
            foreach (var br in breaks)
            {
                // Lage und Geschwindigkeit des Fahrzeugs beim Bruch aus der Aufzeichnung
                const float h = 0.004f;
                ShowPose(br.t + h);
                var m1 = br.vis.Go.transform.localToWorldMatrix;
                ShowPose(br.t);
                var m0 = br.vis.Go.transform.localToWorldMatrix;
                int seed = 1009 * (int)br.pane + 17 * _glassEvents.Count + 3;
                var rng = new System.Random(seed);
                var local = br.vis.PanePoints(br.pane, 220, rng);
                bool left = GlassBreak.IsLeft(br.pane);
                var inward = br.pane == Pane.Rear ? new Vector3(0, -0.3f, 1) : new Vector3(left ? 1 : -1, -0.1f, 0);
                var starts = new List<Vector3>(); var vels = new List<Vector3>();
                foreach (var p in local)
                {
                    Vector3 w0 = m0.MultiplyPoint3x4(p), w1 = m1.MultiplyPoint3x4(p);
                    var spread = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * 0.8f, (float)rng.NextDouble() - 0.5f);
                    starts.Add(w0);
                    vels.Add((w1 - w0) / h + m0.MultiplyVector(inward.normalized * (1.5f + 3f * (float)rng.NextDouble()) + spread * 1.6f));
                }
                var crumbs = GlassCrumbs.Create("Glaswürfel " + GlassBreak.Label(br.pane), starts, vels, br.t, seed);
                if (crumbs != null) { _spawned.Add(crumbs.Go); br.vis.AddCrumbs(crumbs); }
                bool exposed = ExposeToGlass(br.vis, br.pane);
                _glassEvents.Add(new GlassEvent
                {
                    Time = br.t,
                    Text = $"{GlassBreak.Label(br.pane)} in Glaswürfel zerfallen" + (exposed ? " — Würfel treffen den Insassen (viele kleine Schnittwunden)" : "")
                });
            }
            _glassEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            ShowPose(RecordedDuration);   // Endlage (Wiedergabe beginnt ohnehin bei 0)
        }

        /// <summary>Insassen neben der zerfallenen Scheibe: Kopf, Ober- und Unterarm dieser Seite (Lage beim Bruch).</summary>
        bool ExposeToGlass(VehicleVisual vis, Pane pane)
        {
            if (!GlassBreak.IsSide(pane)) return false;
            bool left = GlassBreak.IsLeft(pane);
            var tr = vis.Go.transform;
            float zc = GlassBreak.PaneCenterZ(vis.Profile, pane), hw = vis.Profile.Width / 2;
            var paneWorld = tr.TransformPoint(new Vector3((left ? -1 : 1) * (hw + 0.3f), vis.Profile.Belt + 0.2f, zc));
            bool any = false;
            string side = left ? "links" : "rechts";
            foreach (var rd in AllRagdolls())
            {
                if (rd == null || !rd.Segments.ContainsKey("Kopf")) continue;
                var head = tr.InverseTransformPoint(rd.Segments["Kopf"].position);
                if (Mathf.Abs(head.z - zc) > 0.75f || Mathf.Abs(head.x) > hw + 0.2f || (left ? head.x > -0.05f : head.x < 0.05f)) continue;
                foreach (var seg in new[] { "Kopf", "Oberarm " + side, "Unterarm " + side })
                {
                    Rigidbody rb;
                    if (!rd.Segments.TryGetValue(seg, out rb) || rb == null) continue;
                    var sensor = rb.GetComponent<CrashSensor>();
                    var col = rb.GetComponentInChildren<Collider>();
                    if (sensor == null || col == null || !sensor.HasModelFrame) continue;
                    var pt = col.ClosestPoint(paneWorld);
                    _glassExposure.Add(new GlassExposure { Rd = rd, Sensor = sensor, Local = sensor.transform.InverseTransformPoint(pt), Pane = GlassBreak.Label(pane) });
                    any = true;
                }
            }
            return any;
        }

        /// <summary>Verletzungszeichen durch Glaswürfel für diese Person (Modellraum).</summary>
        public List<Anatomy.CrashMark> GlassMarks(Ragdoll rd)
        {
            var list = new List<Anatomy.CrashMark>();
            int k = 0;
            foreach (var e in _glassExposure.Where(x => x.Rd == rd))
            {
                Vector3 pt, inside;
                if (e.Sensor == null || !e.Sensor.ToModel(e.Sensor.transform.TransformPoint(e.Local), out pt, out inside)) continue;
                var m = Anatomy.CrashMarks.GlassDicing(e.Sensor.ModelSegment, e.Sensor.Region, new[] { pt.x, pt.y, pt.z }, new[] { inside.x, inside.y, inside.z }, e.Pane, k++);
                if (m != null) list.Add(m);
            }
            return list;
        }

        /// <summary>Bericht: was mit den Scheiben passiert ist, Zeit ab Aufprallbeginn.</summary>
        public string GlassReport()
        {
            if (_glassEvents.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var e in _glassEvents) sb.AppendLine($"{(e.Time - CrashStart) * 1000:0} ms: {e.Text}");
            return sb.ToString();
        }
    }
}

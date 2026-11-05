using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>Ergebnis eines berechneten Aufpralls in der Gewalt-Sandbox.</summary>
    public class ImpactRecord
    {
        public readonly List<BluntContact> Blunt = new List<BluntContact>();
        public readonly List<CutResult> Cuts = new List<CutResult>();
        /// <summary>HIC15 aus der Kopfbeschleunigung (gleichmäßig auf 10 kHz umgerechnet, CFC 1000); 0 ohne Kopfbewegung.</summary>
        public double Hic15;
        /// <summary>Bauchanprall: Organbelastung (Milz, Leber, Nieren) aus der Kontaktkraft am Abdomen, sonst null.</summary>
        public AbdomenLoad Abdomen;
        public readonly List<CrashMeasurement> Measurements = new List<CrashMeasurement>();
        public float Duration_s;
        public int FineSteps, CoarseSteps;
    }

    /// <summary>
    /// Gewalt-Sandbox (stumpfe und spitze Gewalt): liegender Körper, Gegenstände, nachgiebiger Kontakt (<see cref="SoftContact"/>)
    /// und Stichkanäle (<see cref="PenetrationSystem"/>).
    ///
    /// Ablauf wie beim Unfall, aber mit wechselndem Schritt: 1 ms, solange nichts aufprallt; sobald sich ein Körperkontakt
    /// schneller als 0,5 m/s schließt, 0,1 ms für mindestens 30 ms (Kontaktdauern 2–20 ms). Ende, wenn alles ruht (max. 4 s).
    /// Danach wird wie beim Unfall abgespielt.
    /// </summary>
    public partial class CrashRig
    {
        [Tooltip("Physikschritt der Gewalt-Sandbox, solange kein Aufprall läuft (s).")]
        [SerializeField] float impactCoarseStep_s = 0.001f;

        public ImpactModelFile Impact { get; private set; }
        float _gewaltHeight_m = 1.8f;
        public PenetrationSystem Penetrations { get; private set; }

        /// <summary>Ground, liegender Körper (Rückenlage), Kontaktmodell für alle Segmente.</summary>
        public void BuildGewalt(CrashParams p, ImpactModelFile model)
        {
            Clear();
            SoftContact.Clear();
            CrashSensor.ClearLookup();
            Impact = model;
            _gewaltHeight_m = p.Height_m;
            Ground();
            float H = p.Height_m;
            // Rückenlage: aufrecht gebaut und um 90° nach hinten gekippt (Vorderseite nach oben); Hüftmitte etwa 12 cm über dem Boden
            Ragdoll = RagdollBuilder.Build(new Vector3(0, 0.12f * H / 1.75f, 0), Quaternion.Euler(-90, 0, 0), RagdollPose.Standing, H, p.Mass_kg, p.Male, invertKneeFlexion);
            RegisterSoftBody(p, model);
            Penetrations = new PenetrationSystem(model, p.Seed);
            SoftContact.Enabled = false;   // zwischen den Würfen normale (starre) Kontakte: stabil beim Ziehen
        }

        /// <summary>Alle Körperkollider beim nachgiebigen Kontakt anmelden (Region, Unterhautfett nach BMI). false ohne Modell.</summary>
        bool RegisterSoftBody(CrashParams p, ImpactModelFile model)
        {
            if (model == null || !AllRagdolls().Any()) return false;
            SoftContact.Clear();
            SoftContact.Model = model;
            double bmi = Core.BodyComposition.Bmi(p.Mass_kg, p.Height_m * 100);
            foreach (var rd in AllRagdolls())
            foreach (var s in rd.Sensors)
            {
                var region = ImpactModel.Region(model, s.Region);
                var rb = s.GetComponent<Rigidbody>();
                // Unterhautfett je Region nach BMI und Geschlecht: in Reihe im Kontakt, verlängert den Stichkanal
                double fat = ImpactModel.FatThickness_m(model, s.Region, bmi, p.Male), extra = ImpactModel.FatExtra_m(model, s.Region, bmi, p.Male);
                foreach (var c in s.GetComponentsInChildren<Collider>())
                {
                    if (c.attachedRigidbody != rb || c.isTrigger) continue;
                    c.contactOffset = 0.02f;   // Kontakte früh melden
                    SoftContact.RegisterBody(c, region, s, fat, extra);
                }
            }
            return true;
        }

        /// <summary>
        /// Gegenstand erzeugen. Die Längsachse (größtes Maß) liegt immer auf der lokalen z-Achse — bei spitzen Gegenständen
        /// die Klingenrichtung. <paramref name="rot"/> richtet z aus. Statische Gegenstände bekommen keinen Rigidbody.
        /// </summary>
        public SoftContact.ObjectInfo SpawnObject(SandboxObject def, Vector3 pos, Quaternion rot, Vector3 velocity, bool gravity, double skinForce_N)
        {
            var go = new GameObject("Gegenstand " + def.label);
            go.transform.SetPositionAndRotation(pos, rot);
            var size = def.size_m != null && def.size_m.Length == 3 ? new Vector3(def.size_m[0], def.size_m[1], def.size_m[2]) : Vector3.one * 0.1f;
            if (size.y > size.z && size.y >= size.x) size = new Vector3(size.x, size.z, size.y);   // Längsachse nach z
            var color = def.color != null && def.color.Length >= 3 ? new Color(def.color[0], def.color[1], def.color[2]) : Color.gray;

            Collider col;
            PrimitiveType visual;
            Vector3 visualScale = size; Quaternion visualRot = Quaternion.identity;
            switch (def.shape)
            {
                case "sphere":
                    var sph = go.AddComponent<SphereCollider>(); sph.radius = size.x / 2; col = sph; visual = PrimitiveType.Sphere;
                    break;
                case "capsule":
                    var cap = go.AddComponent<CapsuleCollider>(); cap.direction = 2; cap.radius = Mathf.Max(size.x, size.y) / 2; cap.height = Mathf.Max(size.z, 2 * cap.radius); col = cap;
                    visual = PrimitiveType.Capsule; visualScale = new Vector3(size.x, size.z / 2, size.x); visualRot = Quaternion.Euler(90, 0, 0);
                    break;
                default:
                    var box = go.AddComponent<BoxCollider>(); box.size = size; col = box; visual = PrimitiveType.Cube;
                    break;
            }
            var vis = GameObject.CreatePrimitive(visual);
            var vc = vis.GetComponent<Collider>();
            if (vc != null) { vc.enabled = false; Destroy(vc); }
            vis.name = "Ansicht";
            vis.transform.SetParent(go.transform, false);
            vis.transform.localRotation = visualRot;
            vis.transform.localScale = visualScale;
            vis.GetComponent<Renderer>().material = CrashPhysics.Mat(color);
            go.AddComponent<CrashSurface>().kind = "object:" + def.id;
            if (def.mode == "statisch" && def.supportHeight_m > 0) Legs(go.transform, size, pos.y - size.y / 2, color);

            Rigidbody rb = null;
            if (def.mode != "statisch")
            {
                rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Max(0.05f, (float)def.mass_kg);
                rb.useGravity = gravity;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;   // schnelle, kleine Gegenstände
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.maxAngularVelocity = 50;
                CrashPhysics.SetVelocity(rb, velocity);
            }
            _spawned.Add(go);
            SoftContact.RegisterObject(col, def, rb, skinForce_N, size, Impact != null ? ImpactModel.CutLoad(Impact, def) : 0);
            return SoftContact.Object(col.GetEntityId());
        }

        /// <summary>Vier Beine unter einer Platte (Tisch), vom Plattenboden bis zum Boden; Kontakt starr, zählt als derselbe Gegenstand.</summary>
        static void Legs(Transform top, Vector3 size, float height, Color color)
        {
            if (height <= 0.02f) return;
            const float leg = 0.045f, inset = 0.06f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * (size.x / 2 - inset), z = (i < 2 ? -1 : 1) * (size.z / 2 - inset);
                var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
                l.name = "Bein";
                l.transform.SetParent(top, false);
                l.transform.localPosition = new Vector3(x, -size.y / 2 - height / 2, z);
                l.transform.localScale = new Vector3(leg, height, leg);
                l.GetComponent<Renderer>().material = CrashPhysics.Mat(color * 0.8f);
            }
        }

        /// <summary>Einen Gegenstand wieder entfernen (z. B. Faust nach dem Schlag).</summary>
        public void RemoveObject(SoftContact.ObjectInfo o)
        {
            if (o == null || o.Collider == null) return;
            var go = o.Collider.gameObject;
            _spawned.Remove(go);
            go.SetActive(false);
            Destroy(go);
        }

        /// <summary>Alle Gegenstände entfernen (Körper bleibt liegen).</summary>
        public void ClearObjects()
        {
            Penetrations?.Clear();
            foreach (var go in _spawned.ToList())
                if (go != null && go.GetComponent<CrashSurface>() != null && go.GetComponent<CrashSurface>().kind.StartsWith("object:"))
                {
                    _spawned.Remove(go); go.SetActive(false); Destroy(go);
                }
        }

        /// <summary>
        /// Berechnet einen Aufprall ab dem aktuellen Zustand (Geschwindigkeiten bleiben erhalten) und spielt ihn ab.
        /// <paramref name="perStep"/>(t, dt) läuft vor jedem Physikschritt (z. B. Faust zurückziehen).
        /// </summary>
        public IEnumerator RunImpact(float slowMotion, Action<ImpactRecord> done, Action<float> progress = null,
                                     Action<float, float> perStep = null, float maxDuration_s = 4f)
        {
            Running = true;
            var record = new ImpactRecord();
            _savedMode = Physics.simulationMode; _modeSaved = true;
            Physics.simulationMode = SimulationMode.Script;
            CrashSensor.ClearLookup();
            BeginRecording();
            SetVisible(false);
            Physics.SyncTransforms();
            SoftContact.ResetIndentation();
            SoftContact.TakeActivity();
            SoftContact.Enabled = true;
            foreach (var s in Ragdoll.Sensors) s.StartRecording(crashStep_s);
            var bodies = _trackedBodies.Where(rb => rb != null && !rb.isKinematic).ToList();
            var watch = new System.Diagnostics.Stopwatch();

            // Kopfgeschwindigkeit je Schritt für HIC (Schrittweite wechselt, daher später gleichmäßig umgerechnet)
            var head = Ragdoll.Head;
            var hT = new List<double>(); var hX = new List<double>(); var hY = new List<double>(); var hZ = new List<double>();
            var abdSensors = Ragdoll.Sensors.Where(x => x.Region == "Abdomen").ToList();
            var abdF = new List<double>();   // Kontaktkraft am Bauch je Schritt (N), gleiche Zeitpunkte wie hT
            float t = 0, fineUntil = -1, lastFrame = 0, rest = 0, nextRestCheck = 0;
            bool started = false;
            CrashStart = 0;
            RecordFrame(0);
            while (t < maxDuration_s)
            {
                watch.Restart();
                bool finished = false;
                do
                {
                    if (SoftContact.TakeActivity())
                    {
                        if (!started) { started = true; CrashStart = t; }
                        fineUntil = t + 0.03f;
                    }
                    bool fine = t < fineUntil;
                    float dt = fine ? crashStep_s : impactCoarseStep_s;
                    if (fine) record.FineSteps++; else record.CoarseSteps++;
                    SoftContact.StepDt = dt;
                    _simTime = t;
                    perStep?.Invoke(t, dt);
                    if (head != null) { var hv = CrashPhysics.Velocity(head); hT.Add(t); hX.Add(hv.x); hY.Add(hv.y); hZ.Add(hv.z); }
                    foreach (var s in Ragdoll.Sensors) s.Step(dt);
                    if (head != null)
                    {
                        double fa = 0;
                        foreach (var s in abdSensors) if (s.Force_kN.Count > 0) fa += s.Force_kN[s.Force_kN.Count - 1] * 1000;
                        abdF.Add(fa);
                    }
                    Penetrations?.Step(dt);
                    Physics.Simulate(dt);
                    Penetrations?.ProcessRequests();
                    t += dt;
                    if (t - lastFrame >= recordInterval_s - 1e-6f) { RecordFrame(t); lastFrame = t; }

                    // Ruhe: alle Körper langsamer als 0,15 m/s für 0,3 s (alle 10 ms prüfen)
                    if (t >= nextRestCheck)
                    {
                        nextRestCheck = t + 0.01f;
                        float vmax = 0;
                        foreach (var rb in bodies) if (rb) vmax = Mathf.Max(vmax, CrashPhysics.Velocity(rb).magnitude);
                        rest = vmax < 0.15f && !fine ? rest + 0.01f : 0;
                        if (rest >= 0.3f && t > 0.2f) finished = true;
                    }
                } while (!finished && t < maxDuration_s && watch.Elapsed.TotalMilliseconds < computeBudget_ms);
                progress?.Invoke(Mathf.Clamp01(t / maxDuration_s));
                if (finished) break;
                yield return null;
            }
            if (_frameTimes[_frameTimes.Count - 1] < t) RecordFrame(t);
            foreach (var s in Ragdoll.Sensors) s.StopRecording();
            SoftContact.Enabled = false;
            record.Duration_s = t;
            CollectBlunt(record);
            CollectCuts(record);
            if (hT.Count > 10)
            {
                var acc = Biomechanics.ResampleAccel_g(hT, hX, hY, hZ, crashStep_s);
                record.Hic15 = acc.Count > 10 ? Biomechanics.Hic(Biomechanics.Cfc(acc, crashStep_s, 1000), crashStep_s, 0.015) : 0;
                if (record.Hic15 > 0) record.Measurements.Add(new CrashMeasurement { metric = "HIC15", value = record.Hic15 });
                // Bauch: Kontaktkraft gleichmäßig auf 10 kHz, CFC 600, dann Organmodell wie beim Fahrzeugunfall
                var organs = LoadOrganModel();
                var tissues = Anatomy.AnatomyPatient.Tissues;
                if (organs != null && tissues != null && abdF.Count == hT.Count && abdF.Max() > 200)
                {
                    var force = Biomechanics.Cfc(Biomechanics.Resample(hT, abdF, crashStep_s), crashStep_s, 600);
                    double lat = 0, tot = 0;
                    foreach (var s in Ragdoll.Sensors) if (s.Region == "Thorax" || s.Region == "Abdomen" || s.Region == "Becken") { lat += s.LateralImpulse; tot += s.TotalImpulse; }
                    double lateral = tot > 0 ? lat / tot : 0;
                    record.Abdomen = OrganMechanics.Evaluate(organs, tissues, force, crashStep_s, lateral, _gewaltHeight_m);
                    record.Measurements.AddRange(OrganMechanics.Measurements(record.Abdomen));
                    record.Measurements.Add(new CrashMeasurement { metric = "Abdomen.lateral", value = lateral });
                }
            }
            Running = false;
            yield return Replay(slowMotion);
            done?.Invoke(record);
        }

        /// <summary>Stärkster Kontakt je Segment und Fläche → Kraft, Fläche, Eindrücktiefe, Segmentlänge, Seite.</summary>
        void CollectBlunt(ImpactRecord record)
        {
            foreach (var s in Ragdoll.Sensors)
            {
                var rb = s.GetComponent<Rigidbody>();
                var colliders = s.GetComponentsInChildren<Collider>().Where(c => c.attachedRigidbody == rb).ToList();
                float indent = colliders.Count > 0 ? colliders.Max(c => SoftContact.MaxIndentation(c)) : 0;
                float length = colliders.Count > 0 ? colliders.Max(SegmentLength) : 0.3f;
                var region = ImpactModel.Region(Impact, s.Region);
                string seg = s.ModelSegment ?? s.name;
                foreach (var kv in s.Contacts)
                {
                    var c = kv.Value;
                    if (c.PeakForce_N < 300) continue;   // Liegen, Abrollen: keine Verletzungsrelevanz
                    double area = region.maxArea_cm2;
                    if (kv.Key.StartsWith("object:"))
                    {
                        // Fläche im Moment der größten Kraft (aus der Geometrie, siehe SoftContact)
                        string id = kv.Key.Substring(7);
                        var peak = colliders.Select(cc => SoftContact.PeakWith(cc, id)).OrderByDescending(x => x.Force_N).FirstOrDefault();
                        if (peak.Area_cm2 > 0) area = Math.Min(peak.Area_cm2, region.maxArea_cm2);
                        else
                        {
                            var def = SoftContact.ObjectDef(id);
                            if (def != null && def.contactArea_cm2 > 0) area = Math.Min(def.contactArea_cm2, region.maxArea_cm2);
                        }
                    }
                    int side = seg.EndsWith("rechts") ? 1 : seg.EndsWith("links") ? -1 : 0;
                    if (side == 0 && s.HasModelFrame && s.ToModel(s.transform.TransformPoint(c.BestPointLocal), out var mp, out _))
                        side = mp.x > 0.02f ? 1 : mp.x < -0.02f ? -1 : 0;
                    record.Blunt.Add(new BluntContact
                    {
                        Region = s.Region, Segment = seg, PeakForce_N = c.PeakForce_N, Area_cm2 = area,
                        MaxIndentation_m = indent, SegmentLength_m = length, Side = side, Surface = kv.Key
                    });
                }
            }
        }

        /// <summary>Schnitte aus dem Kontaktmodell: Segment, Seite, Länge, Tiefe aus dem Andruck, Anfang/Ende im Modellraum.</summary>
        void CollectCuts(ImpactRecord record)
        {
            foreach (var ct in SoftContact.TakeCuts())
            {
                var body = SoftContact.Body(ct.BodyCollider);
                var obj = SoftContact.Object(ct.ObjectCollider);
                if (body == null || obj == null || ct.Length_m < 0.005) continue;
                var s = body.Sensor;
                string seg = s != null ? (s.ModelSegment ?? s.name) : "";
                var cut = new CutResult
                {
                    Region = s != null ? s.Region : "", Segment = seg, ObjectId = obj.Def.id, Length_m = ct.Length_m, PeakLoad_N = ct.PeakLoad_N,
                    Depth_m = ImpactModel.CutDepth(Impact, ct.PeakLoad_N, obj.CutLoad_N),
                    Side = seg.EndsWith("rechts") ? 1 : seg.EndsWith("links") ? -1 : 0
                };
                var brb = body.Collider.attachedRigidbody;
                Vector3 w0 = brb != null ? brb.position + brb.rotation * ct.Start : ct.Start, w1 = brb != null ? brb.position + brb.rotation * ct.End : ct.End;
                if (s != null && s.HasModelFrame && s.ToModel(w0, out var a, out var inA) && s.ToModel(w1, out var b, out _))
                {
                    cut.ModelStart = new[] { a.x, a.y, a.z }; cut.ModelEnd = new[] { b.x, b.y, b.z }; cut.ModelInside = new[] { inA.x, inA.y, inA.z };
                    if (cut.Side == 0) cut.Side = a.x > 0.02f ? 1 : a.x < -0.02f ? -1 : 0;
                }
                record.Cuts.Add(cut);
            }
        }

        static float SegmentLength(Collider c)
        {
            var sc = c.transform.lossyScale;
            if (c is CapsuleCollider cap)
                return cap.height * (cap.direction == 0 ? sc.x : cap.direction == 1 ? sc.y : sc.z);
            var e = c.bounds.size;
            return Mathf.Max(e.x, Mathf.Max(e.y, e.z));
        }

        /// <summary>Gewalt-Sandbox verlassen: zurück zu starren Kontakten.</summary>
        public void EndGewalt()
        {
            SoftContact.Enabled = false;
            SoftContact.Clear();
            Penetrations?.Clear();
            Penetrations = null;
        }
    }
}

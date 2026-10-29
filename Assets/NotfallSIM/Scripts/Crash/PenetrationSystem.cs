using System.Collections.Generic;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Spitze Gegenstände im Gewebe. Nach dem Hautdurchstich (<see cref="SoftContact"/>) gilt:
    /// - Klinge und Segment stoßen sich nicht mehr ab; ein Gelenk führt die Klinge im Stichkanal (nur längs der Klinge beweglich).
    /// - Gegen die Bewegung in die Tiefe wirkt der Gewebewiderstand der jeweiligen Schicht (<see cref="ImpactModel.ResistanceAt"/>):
    ///   Weichteil, ggf. Knochen (Rippe, Schädel — stoppt meist), dahinter Organ. Kommt die Klinge zur Ruhe, steckt sie (Gelenk fest).
    /// - Zieht sie sich zurück (Tiefe ≤ 0), wird der Kanal geschlossen und die Kollision wieder zugelassen.
    /// Ergebnis je Stich: <see cref="PenetrationResult"/> (Region, Seite, größte Tiefe, Knochen getroffen, steckt).
    /// Läuft auf dem Hauptthread vor jedem Physikschritt.
    /// </summary>
    public class PenetrationSystem
    {
        class Active
        {
            public SoftContact.ObjectInfo Obj;
            public SoftContact.BodyInfo Body;
            public Rigidbody BodyRb;
            public Vector3 EntryLocal;      // Einstichpunkt im Segment
            public Vector3 AxisLocal;       // Stichrichtung (in die Tiefe) im Segment
            public ConfigurableJoint Joint;
            public float Depth, MaxDepth, Length;
            public bool BoneHit, Embedded;
            public PenetrationResult Result;
        }

        readonly ImpactModelFile _model;
        readonly System.Random _rng;
        readonly List<Active> _active = new List<Active>();
        public readonly List<PenetrationResult> Results = new List<PenetrationResult>();
        public bool Clothed = true;

        public PenetrationSystem(ImpactModelFile model, int seed) { _model = model; _rng = new System.Random(seed); }

        public int ActiveCount => _active.Count;
        public bool IsActive(SoftContact.ObjectInfo o) => _active.Exists(a => a.Obj == o);

        /// <summary>Durchstichmeldungen der Physik übernehmen (nach jedem Physikschritt).</summary>
        public void ProcessRequests()
        {
            while (SoftContact.PenetrationRequests.TryDequeue(out var r))
            {
                var body = SoftContact.Body(r.BodyCollider);
                var obj = SoftContact.Object(r.ObjectCollider);
                if (body == null || obj == null || body.Collider == null || obj.Collider == null) continue;
                if (_active.Exists(a => a.Obj == obj && a.Body == body)) continue;
                Begin(obj, body, r.Point);
            }
        }

        void Begin(SoftContact.ObjectInfo obj, SoftContact.BodyInfo body, Vector3 point)
        {
            var bodyRb = body.Collider.attachedRigidbody;
            if (bodyRb == null) return;
            // Stichrichtung: Klingenachse, so orientiert, dass sie in den Körper zeigt (vom Einstich zur Segmentmitte)
            Vector3 axis = obj.Collider.transform.forward;
            if (Vector3.Dot(axis, body.Collider.bounds.center - point) < 0) axis = -axis;
            var rule = ImpactModel.PenetrationRule(_model, body.Sensor != null ? body.Sensor.Region : "");
            var a = new Active
            {
                Obj = obj, Body = body, BodyRb = bodyRb,
                EntryLocal = bodyRb.transform.InverseTransformPoint(point),
                AxisLocal = bodyRb.transform.InverseTransformDirection(axis),
                Length = (float)obj.Def.bladeLength_m,
                BoneHit = rule != null && _rng.NextDouble() < rule.boneHitProbability,
            };
            SoftContact.SetIgnored(body.Collider.GetInstanceID(), obj.Collider.GetInstanceID(), true);
            Physics.IgnoreCollision(obj.Collider, body.Collider, true);

            // Führung im Stichkanal: nur längs der Klinge beweglich, keine Drehung (Gelenk am Gegenstand bzw. am Körper, wenn er statisch ist)
            Rigidbody owner = obj.Body != null ? obj.Body : bodyRb;
            Rigidbody other = obj.Body != null ? bodyRb : null;
            var j = owner.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = other;
            j.anchor = owner.transform.InverseTransformPoint(point);
            j.autoConfigureConnectedAnchor = true;
            j.axis = owner.transform.InverseTransformDirection(axis);
            j.secondaryAxis = owner.transform.InverseTransformDirection(Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right));
            j.xMotion = ConfigurableJointMotion.Free;
            j.yMotion = j.zMotion = ConfigurableJointMotion.Locked;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
            a.Joint = j;

            var sensor = body.Sensor;
            a.Result = new PenetrationResult
            {
                Region = sensor != null ? sensor.Region : "", Segment = sensor != null ? sensor.ModelSegment ?? sensor.name : "",
                Side = Side(sensor, point), ObjectId = obj.Def.id, HitBone = false
            };
            if (sensor != null && sensor.HasModelFrame && sensor.ToModel(point, out var mp, out var inside))
            { a.Result.ModelPoint = new[] { mp.x, mp.y, mp.z }; a.Result.ModelInside = new[] { inside.x, inside.y, inside.z }; }
            Results.Add(a.Result);
            _active.Add(a);
        }

        static int Side(CrashSensor s, Vector3 world)
        {
            string seg = s != null ? (s.ModelSegment ?? s.name) : "";
            if (seg.EndsWith("rechts")) return 1;
            if (seg.EndsWith("links")) return -1;
            if (s != null && s.HasModelFrame && s.ToModel(world, out var p, out _)) return p.x > 0.01f ? 1 : p.x < -0.01f ? -1 : 0;
            return 0;
        }

        /// <summary>Gewebewiderstand anwenden (vor jedem Physikschritt).</summary>
        public void Step(float dt)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var a = _active[i];
                if (a.Obj.Collider == null || a.BodyRb == null) { _active.RemoveAt(i); continue; }
                var bt = a.BodyRb.transform;
                Vector3 entry = bt.TransformPoint(a.EntryLocal), axis = bt.TransformDirection(a.AxisLocal);
                // Spitze: vorderes Ende des Gegenstands in Stichrichtung (statischer Pfahl: Endpunkt in Richtung Körper)
                var ot = a.Obj.Collider.transform;
                Vector3 tipDir = Vector3.Dot(ot.forward, axis) >= 0 ? ot.forward : -ot.forward;
                Vector3 tip = a.Obj.Collider.bounds.center + tipDir * HalfLength(a.Obj.Collider);
                a.Depth = Vector3.Dot(tip - entry, axis);
                a.MaxDepth = Mathf.Max(a.MaxDepth, a.Depth);
                a.Result.Depth_m = a.MaxDepth;

                Vector3 vObj = a.Obj.Body != null ? CrashPhysics.Velocity(a.Obj.Body) : Vector3.zero;
                Vector3 vBody = a.BodyRb.GetPointVelocity(entry);
                float vRel = Vector3.Dot(vObj - vBody, axis);   // > 0: tiefer

                if (a.Depth <= -0.002f)
                {
                    // herausgezogen: Kanal schließen, Kollision wieder zulassen
                    End(a, embedded: false);
                    _active.RemoveAt(i);
                    continue;
                }
                if (a.Embedded) continue;

                string region = a.Result.Region;
                float resistance = a.Depth >= a.Length ? 20000f   // Heft am Einstich: stoppt
                                 : (float)ImpactModel.ResistanceAt(_model, region, a.Depth, a.BoneHit);
                var rule = ImpactModel.PenetrationRule(_model, region);
                if (a.BoneHit && rule != null && a.Depth >= rule.wall_m - 0.005f) a.Result.HitBone = true;

                if (vRel > 0.02f)
                {
                    // Klinge wird gebremst, Körper in Gegenrichtung belastet (bei statischem Pfahl nur der Körper)
                    float f = resistance;
                    if (a.Obj.Body != null) a.Obj.Body.AddForce(-axis * f, ForceMode.Force);
                    a.BodyRb.AddForceAtPosition(axis * f, entry, ForceMode.Force);
                }
                else if (a.Depth > 0.005f && Mathf.Abs(vRel) < 0.05f)
                {
                    // zur Ruhe gekommen: steckt
                    a.Joint.xMotion = ConfigurableJointMotion.Locked;
                    a.Embedded = true;
                    a.Result.Embedded = true;
                }
            }
        }

        static float HalfLength(Collider c)
        {
            if (c is CapsuleCollider cap) return cap.height * 0.5f * c.transform.lossyScale.z;
            if (c is BoxCollider box) return box.size.z * 0.5f * c.transform.lossyScale.z;
            if (c is SphereCollider sph) return sph.radius * c.transform.lossyScale.z;
            return c.bounds.extents.z;
        }

        void End(Active a, bool embedded)
        {
            if (a.Joint != null)
            {
                a.Joint.xMotion = ConfigurableJointMotion.Free;   // sofort lösen, Destroy greift erst am Frame-Ende
                Object.Destroy(a.Joint);
            }
            if (a.Obj.Collider != null && a.Body.Collider != null)
            {
                Physics.IgnoreCollision(a.Obj.Collider, a.Body.Collider, false);
                SoftContact.SetIgnored(a.Body.Collider.GetInstanceID(), a.Obj.Collider.GetInstanceID(), false);
            }
            a.Result.Embedded = embedded;
        }

        /// <summary>Alles zurücksetzen (neuer Körper).</summary>
        public void Clear()
        {
            foreach (var a in _active) if (a.Joint != null) Object.Destroy(a.Joint);
            _active.Clear();
            Results.Clear();
        }
    }
}

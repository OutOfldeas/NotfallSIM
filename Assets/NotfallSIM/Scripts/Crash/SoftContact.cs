using System.Collections.Concurrent;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Nachgiebiger Kontakt zwischen Körper und Umgebung über die Kontaktmodifikation von PhysX
    /// (<see cref="Physics.ContactModifyEvent"/>): Der Löser darf pro Schritt höchstens den Impuls F·dt aufbringen,
    /// F = k(δ)·δ + c·δ̇ aus <see cref="ImpactModel.Force"/> (Region, Eindrücktiefe δ, Schließgeschwindigkeit, Kontaktfläche,
    /// Steifigkeit und Masse des Gegenstands). So dringen Gegenstand und Körper so weit ineinander ein, wie es das
    /// Gewebe tut, und die Kraft hängt vom Gewebe ab statt von der Schrittweite des Lösers.
    ///
    /// Spitze Gegenstände: Trifft die Spitze (Kontaktnormale längs der Klinge) mit mehr als der Durchstichkraft der Haut,
    /// wird ein Durchstich gemeldet (<see cref="PenetrationRequests"/>); <see cref="PenetrationSystem"/> übernimmt.
    ///
    /// Die Ereignisse laufen auf Arbeitsthreads: Registrierung nur auf dem Hauptthread zwischen Physikschritten,
    /// im Ereignis wird nur gelesen; Messwerte über threadsichere Sammlungen.
    /// </summary>
    public static class SoftContact
    {
        public class BodyInfo
        {
            public RegionContact Region;
            public Collider Collider;
            public CrashSensor Sensor;
        }

        public class ObjectInfo
        {
            public SandboxObject Def;
            public Collider Collider;
            public Rigidbody Body;          // null bei statischen Gegenständen
            public double SkinForce_N;      // Durchstichkraft (nur spitz)
            public double Area_cm2, Stiffness_N_m, Mass_kg;
        }

        public struct PenetrationRequest
        {
            public int BodyCollider, ObjectCollider;
            public Vector3 Point, Normal;
            public float Force_N;
        }

        static readonly Dictionary<int, BodyInfo> Bodies = new Dictionary<int, BodyInfo>();
        static readonly Dictionary<int, ObjectInfo> Objects = new Dictionary<int, ObjectInfo>();
        static readonly HashSet<long> Ignored = new HashSet<long>();     // laufende Durchstiche (Klinge ↔ Segment)
        static readonly ConcurrentDictionary<int, float> MaxIndent = new ConcurrentDictionary<int, float>();
        public static readonly ConcurrentQueue<PenetrationRequest> PenetrationRequests = new ConcurrentQueue<PenetrationRequest>();

        /// <summary>Physikschritt (s), vor jedem Physics.Simulate bzw. beim Wechsel der festen Schrittweite setzen.</summary>
        public static volatile float StepDt = 0.02f;
        public static bool Enabled = true;
        static bool _hooked;
        static int _activity;

        /// <summary>Gab es seit dem letzten Aufruf einen Körperkontakt, der sich mit mehr als 0,5 m/s schließt (oder bald schließt)?
        /// Dann rechnet der Aufprall mit dem feinen Schritt (<see cref="CrashRig.RunImpact"/>).</summary>
        public static bool TakeActivity() => System.Threading.Interlocked.Exchange(ref _activity, 0) != 0;

        static void Hook()
        {
            if (_hooked) return;
            Physics.ContactModifyEvent += Modify;
            Physics.ContactModifyEventCCD += Modify;
            _hooked = true;
        }

        public static void Clear()
        {
            Bodies.Clear(); Objects.Clear(); Ignored.Clear(); MaxIndent.Clear();
            while (PenetrationRequests.TryDequeue(out _)) { }
        }

        public static void RegisterBody(Collider c, RegionContact region, CrashSensor sensor)
        {
            if (c == null || region == null) return;
            Hook();
            c.hasModifiableContacts = true;
            Bodies[c.GetInstanceID()] = new BodyInfo { Region = region, Collider = c, Sensor = sensor };
        }

        public static void RegisterObject(Collider c, SandboxObject def, Rigidbody body, double skinForce_N)
        {
            if (c == null || def == null) return;
            Hook();
            c.hasModifiableContacts = true;
            Objects[c.GetInstanceID()] = new ObjectInfo
            {
                Def = def, Collider = c, Body = body, SkinForce_N = skinForce_N,
                Area_cm2 = def.contactArea_cm2, Stiffness_N_m = def.stiffness_N_m, Mass_kg = body != null ? body.mass : 0
            };
        }

        /// <summary>Definition eines registrierten Gegenstands nach Id (für Kontaktfläche in der Auswertung).</summary>
        public static SandboxObject ObjectDef(string id)
        {
            foreach (var o in Objects.Values) if (o.Def != null && o.Def.id == id) return o.Def;
            return null;
        }

        public static BodyInfo Body(int colliderId) => Bodies.TryGetValue(colliderId, out var b) ? b : null;
        public static ObjectInfo Object(int colliderId) => Objects.TryGetValue(colliderId, out var o) ? o : null;

        static long PairKey(int a, int b) => ((long)Mathf.Min(a, b) << 32) ^ (uint)Mathf.Max(a, b);

        /// <summary>Kontakt zwischen Klinge und Segment abschalten bzw. wieder zulassen (Hauptthread).</summary>
        public static void SetIgnored(int bodyCollider, int objectCollider, bool ignored)
        {
            long k = PairKey(bodyCollider, objectCollider);
            if (ignored) Ignored.Add(k); else Ignored.Remove(k);
        }

        /// <summary>Größte Eindrücktiefe an einem Kollider seit dem letzten <see cref="ResetIndentation"/>.</summary>
        public static float MaxIndentation(Collider c) => c != null && MaxIndent.TryGetValue(c.GetInstanceID(), out float d) ? d : 0;
        public static void ResetIndentation() => MaxIndent.Clear();

        static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            if (!Enabled) return;
            float dt = StepDt;
            for (int p = 0; p < pairs.Length; p++)
            {
                var pair = pairs[p];
                BodyInfo body; bool bodyFirst;
                if (Bodies.TryGetValue(pair.colliderInstanceID, out body)) bodyFirst = true;
                else if (Bodies.TryGetValue(pair.otherColliderInstanceID, out body)) bodyFirst = false;
                else continue;
                int otherId = bodyFirst ? pair.otherColliderInstanceID : pair.colliderInstanceID;
                if (Bodies.ContainsKey(otherId)) continue;               // Selbstkontakt des Ragdolls bleibt starr
                int bodyId = bodyFirst ? pair.colliderInstanceID : pair.otherColliderInstanceID;
                if (Ignored.Contains(PairKey(bodyId, otherId)))
                {
                    for (int i = 0; i < pair.contactCount; i++) pair.IgnoreContact(i);
                    continue;
                }
                Objects.TryGetValue(otherId, out var obj);

                Vector3 vB = bodyFirst ? pair.bodyVelocity : pair.otherBodyVelocity, wB = bodyFirst ? pair.bodyAngularVelocity : pair.otherBodyAngularVelocity;
                Vector3 vO = bodyFirst ? pair.otherBodyVelocity : pair.bodyVelocity, wO = bodyFirst ? pair.otherBodyAngularVelocity : pair.bodyAngularVelocity;
                Vector3 pB = bodyFirst ? pair.position : pair.otherPosition, pO = bodyFirst ? pair.otherPosition : pair.position;

                var region = body.Region;
                double area = obj != null && obj.Area_cm2 > 0 ? System.Math.Min(obj.Area_cm2, region.maxArea_cm2) : region.maxArea_cm2;
                double kObj = obj != null ? obj.Stiffness_N_m : 0, mObj = obj != null ? obj.Mass_kg : 0;
                int n = pair.contactCount;
                float maxDelta = 0;
                for (int i = 0; i < n; i++)
                {
                    float sep = pair.GetSeparation(i);
                    float delta = Mathf.Max(0, -sep);
                    Vector3 point = pair.GetPoint(i), normal = pair.GetNormal(i);
                    // VERIFY in Unity: Normale zeigt vom zweiten zum ersten Kollider; Schließgeschwindigkeit positiv, wenn sie sich nähern
                    Vector3 velB = vB + Vector3.Cross(wB, point - pB), velO = vO + Vector3.Cross(wO, point - pO);
                    float closing = bodyFirst ? Vector3.Dot(velO - velB, normal) : Vector3.Dot(velB - velO, normal);
                    if (closing > 0.5f && sep < 0.02f) _activity = 1;
                    double f = ImpactModel.Force(region, delta, closing, area, kObj, mObj);
                    pair.SetMaxImpulse(i, (float)(f * dt / n));
                    maxDelta = Mathf.Max(maxDelta, delta);

                    // Spitze trifft auf: Normale längs der Klinge (lokale z-Achse des Gegenstands) und Kraft über der Durchstichkraft
                    if (obj != null && obj.Def.Sharp && f >= obj.SkinForce_N && closing > 0.3f)
                    {
                        Quaternion rotO = bodyFirst ? pair.otherRotation : pair.rotation;
                        Vector3 axis = rotO * Vector3.forward;
                        if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.8f)
                            PenetrationRequests.Enqueue(new PenetrationRequest { BodyCollider = bodyId, ObjectCollider = otherId, Point = point, Normal = normal, Force_N = (float)f });
                    }
                }
                if (maxDelta > 0) MaxIndent.AddOrUpdate(bodyId, maxDelta, (_, old) => Mathf.Max(old, maxDelta));
            }
        }
    }
}

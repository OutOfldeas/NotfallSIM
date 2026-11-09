using System;
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
            public double Fat_m, FatExtra_m;   // Unterhautfett (Dicke; Mehr gegenüber dem Bezugs-BMI)
        }

        public class ObjectInfo
        {
            public SandboxObject Def;
            public Collider Collider;
            public Rigidbody Body;          // null bei statischen Gegenständen
            public double SkinForce_N;      // Durchstichkraft (nur spitz)
            public double Area_cm2, Stiffness_N_m, Mass_kg;
            public string Shape;            // "sphere" | "box" | "capsule" — Kontaktfläche aus der Geometrie
            public Vector3 Size;            // Maße mit Längsachse z (m)
            public double CutLoad_N;        // Andruck, ab dem die Schneide beim Ziehen schneidet (0 = keine Schneide)
            public double Clearance_m;      // Federweg bis zum starren Anschlag (Motorhaube → Motorblock), 0 = keiner
        }

        /// <summary>Gleitweg einer Schneide über ein Segment unter Andruck (für Schnittwunden).</summary>
        public class CutTrack
        {
            public EntityId BodyCollider, ObjectCollider;
            public double Length_m, PeakLoad_N;
            public Vector3 Start, End;   // relativ zum Körpersegment (Lage des Rigidbody im Moment des Kontakts)
        }

        /// <summary>Größte Kontaktkraft je Paar (Körperkollider, Gegenstand) und die Fläche in diesem Moment.</summary>
        public struct PeakContact { public float Force_N, Area_cm2; }

        public struct PenetrationRequest
        {
            public EntityId BodyCollider, ObjectCollider;
            public Vector3 Point, Normal;
            public float Force_N;
        }

        static readonly Dictionary<EntityId, BodyInfo> Bodies = new Dictionary<EntityId, BodyInfo>();
        static readonly Dictionary<EntityId, ObjectInfo> Objects = new Dictionary<EntityId, ObjectInfo>();
        static readonly HashSet<PairId> Ignored = new HashSet<PairId>();     // laufende Durchstiche (Klinge ↔ Segment)
        static readonly ConcurrentDictionary<EntityId, float> MaxIndent = new ConcurrentDictionary<EntityId, float>();
        static readonly ConcurrentDictionary<PairId, PeakContact> Peaks = new ConcurrentDictionary<PairId, PeakContact>();
        static readonly Dictionary<PairId, CutTrack> Cuts = new Dictionary<PairId, CutTrack>();   // Zugriff nur unter lock(Cuts)
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
            Bodies.Clear(); Objects.Clear(); Ignored.Clear(); MaxIndent.Clear(); Peaks.Clear();
            lock (Cuts) Cuts.Clear();
            while (PenetrationRequests.TryDequeue(out _)) { }
        }

        public static void RegisterBody(Collider c, RegionContact region, CrashSensor sensor, double fat_m = 0, double fatExtra_m = 0)
        {
            if (c == null || region == null) return;
            Hook();
            c.hasModifiableContacts = true;
            Bodies[c.GetEntityId()] = new BodyInfo { Region = region, Collider = c, Sensor = sensor, Fat_m = fat_m, FatExtra_m = fatExtra_m };
        }

        /// <summary>Aufprallmodell für die Fettschicht (Elastizitätsmodul); ohne Modell kein Fett im Kontakt.</summary>
        public static ImpactModelFile Model;

        public static void RegisterObject(Collider c, SandboxObject def, Rigidbody body, double skinForce_N, Vector3 size, double cutLoad_N)
        {
            if (c == null || def == null) return;
            Hook();
            c.hasModifiableContacts = true;
            Objects[c.GetEntityId()] = new ObjectInfo
            {
                Def = def, Collider = c, Body = body, SkinForce_N = skinForce_N,
                Area_cm2 = def.contactArea_cm2, Stiffness_N_m = def.stiffness_N_m, Mass_kg = body != null ? body.mass : 0,
                Shape = def.shape, Size = size, CutLoad_N = cutLoad_N, Clearance_m = def.clearance_m
            };
        }

        /// <summary>Definition eines registrierten Gegenstands nach Id (für Kontaktfläche in der Auswertung).</summary>
        public static SandboxObject ObjectDef(string id)
        {
            foreach (var o in Objects.Values) if (o.Def != null && o.Def.id == id) return o.Def;
            return null;
        }

        public static BodyInfo Body(EntityId colliderId) => Bodies.TryGetValue(colliderId, out var b) ? b : null;
        public static ObjectInfo Object(EntityId colliderId) => Objects.TryGetValue(colliderId, out var o) ? o : null;

        /// <summary>Schlüssel eines Paares Körper ↔ Gegenstand (immer Körper zuerst).</summary>
        public struct PairId : IEquatable<PairId>
        {
            public readonly EntityId Body, Other;
            public PairId(EntityId body, EntityId other) { Body = body; Other = other; }
            public bool Equals(PairId o) => Body.Equals(o.Body) && Other.Equals(o.Other);
            public override bool Equals(object o) => o is PairId p && Equals(p);
            public override int GetHashCode() => Body.GetHashCode() * 486187739 ^ Other.GetHashCode();
        }
        static PairId PairKey(EntityId body, EntityId other) => new PairId(body, other);

        /// <summary>Kontakt zwischen Klinge und Segment abschalten bzw. wieder zulassen (Hauptthread).</summary>
        public static void SetIgnored(EntityId bodyCollider, EntityId objectCollider, bool ignored)
        {
            var k = PairKey(bodyCollider, objectCollider);
            if (ignored) Ignored.Add(k); else Ignored.Remove(k);
        }

        /// <summary>Größte Eindrücktiefe an einem Kollider seit dem letzten <see cref="ResetIndentation"/>.</summary>
        public static float MaxIndentation(Collider c) => c != null && MaxIndent.TryGetValue(c.GetEntityId(), out float d) ? d : 0;
        public static void ResetIndentation() { MaxIndent.Clear(); Peaks.Clear(); lock (Cuts) Cuts.Clear(); }

        /// <summary>Fläche im Moment der größten Kraft zwischen einem Körperkollider und Gegenständen mit dieser Id (cm²; 0 = kein Kontakt).</summary>
        public static PeakContact PeakWith(Collider body, string objectDefId)
        {
            var best = new PeakContact();
            if (body == null) return best;
            var bid = body.GetEntityId();
            foreach (var o in Objects)
            {
                if (o.Value.Def == null || o.Value.Def.id != objectDefId) continue;
                if (Peaks.TryGetValue(PairKey(bid, o.Key), out var pc) && pc.Force_N > best.Force_N) best = pc;
            }
            return best;
        }

        /// <summary>Alle Schnitte seit dem letzten <see cref="ResetIndentation"/> (Kopie).</summary>
        public static List<CutTrack> TakeCuts()
        {
            lock (Cuts) { var l = new List<CutTrack>(Cuts.Values); Cuts.Clear(); return l; }
        }

        static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            if (!Enabled) return;
            float dt = StepDt;
            for (int p = 0; p < pairs.Length; p++)
            {
                var pair = pairs[p];
                BodyInfo body; bool bodyFirst;
                if (Bodies.TryGetValue(pair.colliderEntityId, out body)) bodyFirst = true;
                else if (Bodies.TryGetValue(pair.otherColliderEntityId, out body)) bodyFirst = false;
                else continue;
                var otherId = bodyFirst ? pair.otherColliderEntityId : pair.colliderEntityId;
                if (Bodies.ContainsKey(otherId)) continue;               // Selbstkontakt des Ragdolls bleibt starr
                var bodyId = bodyFirst ? pair.colliderEntityId : pair.otherColliderEntityId;
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
                Quaternion rotObj = bodyFirst ? pair.otherRotation : pair.rotation;
                Quaternion invObj = Quaternion.Inverse(rotObj);
                var key = PairKey(bodyId, otherId);
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
                    // Kontaktfläche: aus der Geometrie des Gegenstands (Lage zur Normale, Eindrücktiefe), sonst flach (Boden, Wand)
                    double area = region.maxArea_cm2;
                    if (obj != null && !string.IsNullOrEmpty(obj.Shape) && obj.Size.sqrMagnitude > 0)
                    {
                        Vector3 ln = invObj * normal;
                        area = ImpactModel.GeometricArea(obj.Shape, obj.Size.x, obj.Size.y, obj.Size.z, ln.x, ln.y, ln.z, delta);
                    }
                    else if (obj != null && obj.Area_cm2 > 0) area = obj.Area_cm2;
                    area = System.Math.Min(area, region.maxArea_cm2);
                    double f = ImpactModel.Force(Model, region, delta, closing, area, kObj, mObj, body.Fat_m, obj != null ? obj.Clearance_m : 0, out double tissueDelta);
                    pair.SetMaxImpulse(i, (float)(f * dt / n));
                    maxDelta = Mathf.Max(maxDelta, (float)tissueDelta);   // Gewebe unter dem Fett (Thoraxkompression)
                    if (obj != null)
                    {
                        float ff = (float)f, aa = (float)area;
                        Peaks.AddOrUpdate(key, new PeakContact { Force_N = ff, Area_cm2 = aa }, (_, old) => ff > old.Force_N ? new PeakContact { Force_N = ff, Area_cm2 = aa } : old);
                    }

                    // Schneide gleitet unter Andruck über die Haut: Schnittweg sammeln
                    if (obj != null && obj.CutLoad_N > 0 && f >= obj.CutLoad_N)
                    {
                        Vector3 rel = bodyFirst ? velO - velB : velB - velO;
                        float slide = (rel - Vector3.Dot(rel, normal) * normal).magnitude;
                        if (slide >= 0.2f)
                        {
                            lock (Cuts)
                            {
                                Quaternion rotB = bodyFirst ? pair.rotation : pair.otherRotation;
                                Vector3 local = Quaternion.Inverse(rotB) * (point - pB);   // Körper bewegt sich weiter: im Segment merken
                                if (!Cuts.TryGetValue(key, out var ct)) Cuts[key] = ct = new CutTrack { BodyCollider = bodyId, ObjectCollider = otherId, Start = local };
                                ct.Length_m += slide * dt / n;
                                ct.PeakLoad_N = System.Math.Max(ct.PeakLoad_N, f);
                                ct.End = local;
                            }
                        }
                    }

                    // Spitze trifft auf: Normale längs der Klinge (lokale z-Achse des Gegenstands) und Kraft über der Durchstichkraft
                    if (obj != null && obj.Def.Sharp && f >= obj.SkinForce_N && closing > 0.3f)
                    {
                        Vector3 axis = rotObj * Vector3.forward;
                        if (Mathf.Abs(Vector3.Dot(axis, normal)) > 0.7f)   // Spitze voran (bis ca. 45° schräg)
                            PenetrationRequests.Enqueue(new PenetrationRequest { BodyCollider = bodyId, ObjectCollider = otherId, Point = point, Normal = normal, Force_N = (float)f });
                    }
                }
                if (maxDelta > 0) MaxIndent.AddOrUpdate(bodyId, maxDelta, (_, old) => Mathf.Max(old, maxDelta));
            }
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>Eine Person im frei gestellten Unfall: Insasse eines Fahrzeugs oder Fußgänger.</summary>
    public class ScenarioHuman
    {
        public string Label, VehicleId, Seat;
        public bool Pedestrian, Belted;
        public Ragdoll Rd;
        public int Vehicle = -1, Walker = -1;           // Index in TrafficSim.Bodies bzw. Walkers
        public Dictionary<string, ConfigurableJoint> Belts = new Dictionary<string, ConfigurableJoint>();
        public double ShoulderBeltImpulse_Ns, LapBeltImpulse_Ns;
        public CrashGauges Gauges;
        public MuscleToneDriver Muscles;
        public List<CrashMeasurement> Measurements;
        public AbdomenLoad Abdomen;
        public double ReleaseAt = double.PositiveInfinity;
        /// <summary>Ab hier angespannt (Insassen, wenn der Fahrer reagiert — sie spüren Bremsen bzw. Lenken); ∞ = nie.</summary>
        public double BraceAt = double.PositiveInfinity;
        public bool Released;
        public readonly List<HeldBody> Held = new List<HeldBody>();
        /// <summary>Mechanismus für Text und Rettungszeit (mechanisms.json): pkw_frontal, pkw_seite, fussgaenger.</summary>
        public string MechanismId = "pkw_frontal";
        public string Summary;
        /// <summary>Sitzplatz im Fahrzeug (lokal x, z) und Mitte der eigenen Tür (z) — für Einklemmung.</summary>
        public float SeatX, SeatZ, DoorZ;
    }

    /// <summary>Kinematisch mitgeführter Körper (bis zur Freigabe), Lage im Fahrzeug- bzw. Fußgängersystem.</summary>
    public class HeldBody { public Rigidbody Rb; public Vector3 LocalPos; public Quaternion LocalRot; }

    /// <summary>Ein Fahrzeug der Szene: kinematische Karosserie und eindrückbare Teile (Türen, Armaturenbrett, Lenkrad, Airbags).</summary>
    class TrafficVehicleRig
    {
        public int Index;
        public Rigidbody Root;
        public readonly List<MovingPart> Parts = new List<MovingPart>();
        public readonly List<Action<float>> Airbags = new List<Action<float>>();
        public float AirbagAt = float.PositiveInfinity;
    }

    /// <summary>Eindrückbares Teil: eigener kinematischer Körper, verschoben um das Eindringen der Seite <see cref="Face"/>.</summary>
    class MovingPart
    {
        public Rigidbody Rb;
        public int Face = -1;              // TrafficSim.Front/Left/Right; −1 bewegt sich nicht gegen die Karosserie
        public float Center;               // Ort entlang der Seite (Front: x, Seite: z), lokal
        public float Share = 1;            // Anteil (Lenkrad 0,6)
    }

    /// <summary>
    /// Frei gestellter Unfall („Szenario“): mehrere Fahrzeuge mit Insassen, Fußgänger, Hindernisse aus traffic_scenarios.json.
    /// Die Fahrzeugbewegung kommt aus <see cref="TrafficSim"/> (Fahrdynamik, Bremsen/Ausweichen, Stoß mit Kennlinien), die
    /// Karosserien folgen ihr kinematisch; Türen und Armaturenbrett dringen um die berechnete Verformung ein. Die Personen sind
    /// Ragdolls: bis zum Manöver bzw. kurz vor dem Aufprall werden sie mitgeführt, danach bewegen sie sich frei (Bremsen
    /// verlagert sie schon vor dem Stoß nach vorn). Jede Person wird wie beim Einzelunfall gemessen.
    /// </summary>
    public partial class CrashRig
    {
        public readonly List<ScenarioHuman> Humans = new List<ScenarioHuman>();
        readonly List<TrafficVehicleRig> _trafficVehicles = new List<TrafficVehicleRig>();
        TrafficSim _traffic;
        public TrafficSim.Prediction TrafficPrediction { get; private set; }
        public TrafficSim Traffic => _traffic;
        public string TrafficNote { get; private set; }

        static TrafficScenarioFile _trafficFile;
        static bool _trafficTried;
        public static TrafficScenarioFile LoadTrafficScenarios()
        {
            if (_trafficFile != null || _trafficTried) return _trafficFile;
            _trafficTried = true;
            string path = Path.Combine(Data.InjuryDatabase.Root, "traffic_scenarios.json");
            if (File.Exists(path)) _trafficFile = JsonUtility.FromJson<TrafficScenarioFile>(File.ReadAllText(path));
            return _trafficFile;
        }

        /// <summary>Person als Patient wählen (Kamera, Färbung, Zeichen, Organbelastung).</summary>
        public void SelectHuman(int i)
        {
            if (i < 0 || i >= Humans.Count) return;
            var h = Humans[i];
            Ragdoll = h.Rd;
            LastAbdomenLoad = h.Abdomen;
            ShoulderBeltImpulse_Ns = h.ShoulderBeltImpulse_Ns;
            LapBeltImpulse_Ns = h.LapBeltImpulse_Ns;
            BeltJoints.Clear();
            foreach (var kv in h.Belts) BeltJoints[kv.Key] = kv.Value;
        }

        static Vector3 V3(double x, double z, float y = 0) => new Vector3((float)x, y, (float)z);
        static Quaternion Yaw(double psi) => Quaternion.Euler(0, (float)(psi * 180 / Math.PI), 0);

        // ------------------------------------------------------------------ Aufbau

        float BuildTraffic(CrashParams p, out CrashApproach approach)
        {
            approach = null;
            var vm = LoadVehicleModel();
            var types = vm?.vehicleTypes;
            if (p.Traffic == null || types == null || types.Length == 0)
            {
                TrafficNote = "Szenario oder Fahrzeugtypen fehlen (traffic_scenarios.json, vehicle_model.json).";
                Ragdoll = RagdollBuilder.Build(new Vector3(0, 1, 0), Quaternion.identity, RagdollPose.Standing, p.Height_m, p.Mass_kg, p.Male, invertKneeFlexion);
                return 0;
            }
            _traffic = new TrafficSim(p.Traffic, types);
            TrafficPrediction = TrafficSim.Predict(p.Traffic, types);
            TrafficNote = TrafficPrediction.FirstContact < 0 ? "Kein Zusammenstoß: die Fahrzeuge (bzw. Fußgänger) verfehlen sich." : null;

            // Hindernisse: feste Kollider (Köpfe und Arme können sie treffen)
            foreach (var b in _traffic.Bodies.Where(x => x.Static))
            {
                var def = p.Traffic.obstacles.First(o => o.id == b.Id);
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = b.Label;
                go.transform.SetPositionAndRotation(V3(b.X, b.Z, (float)def.height_m / 2), Yaw(b.Psi));
                go.transform.localScale = new Vector3((float)b.W, (float)def.height_m, (float)b.L);
                go.AddComponent<CrashSurface>().kind = "hindernis";
                var c = def.color != null && def.color.Length >= 3 ? new Color(def.color[0], def.color[1], def.color[2]) : new Color(0.5f, 0.5f, 0.5f);
                go.GetComponent<Renderer>().material = CrashPhysics.Mat(c);
                _spawned.Add(go);
            }

            for (int i = 0; i < _traffic.VehicleCount; i++) BuildTrafficVehicle(p, i);
            for (int i = 0; i < _traffic.Walkers.Count; i++) BuildPedestrian(p, i);
            DrawPaths();

            // Freigabe: Insassen beim Beginn des eigenen Manövers bzw. 0,1 s vor dem ersten Kontakt, Fußgänger 0,15 s vor ihrem Kontakt
            double first = TrafficPrediction.FirstContact;
            foreach (var h in Humans)
            {
                if (h.Pedestrian)
                {
                    double c = TrafficPrediction.WalkerContact[h.Walker];
                    h.ReleaseAt = c >= 0 ? Math.Max(0, c - 0.15) : double.PositiveInfinity;
                }
                else
                {
                    var b = _traffic.Bodies[h.Vehicle];
                    h.ReleaseAt = Math.Min(b.ReactAt, first >= 0 ? Math.Max(0, first - 0.1) : double.PositiveInfinity);
                    h.BraceAt = b.ReactAt;
                }
            }
            int patient = Mathf.Clamp(p.Patient, 0, Math.Max(0, Humans.Count - 1));
            if (Humans.Count > 0) SelectHuman(patient);
            else Ragdoll = null;
            return 0;
        }

        /// <summary>Innenraum, Front (für Fußgänger), Karosserie und Insassen eines Fahrzeugs am Startort.</summary>
        void BuildTrafficVehicle(CrashParams p, int index)
        {
            var b = _traffic.Bodies[index];
            var t = b.Type;
            float L = (float)b.L, W = (float)b.W, H = p.Height_m;
            var d = RagdollBuilder.Dims(H);
            const float floorY = 0.30f;
            float hipY = floorY + d.KneeToSole + 0.05f, seatTop = hipY - d.ThighRadius;
            float dashZ = 0.84f, zFront = dashZ - d.HipToKnee - 0.2f, zRear = zFront - 0.9f;
            var pos = V3(b.X, b.Z); var rot = Yaw(b.Psi);
            var color = t.color != null && t.color.Length >= 3 ? new Color(t.color[0], t.color[1], t.color[2]) : new Color(0.3f, 0.3f, 0.35f);
            var dark = new Color(0.15f, 0.15f, 0.17f);

            var vr = new TrafficVehicleRig { Index = index };
            var car = Vehicle(b.Label ?? b.Id, pos);
            car.transform.rotation = rot;
            vr.Root = car.GetComponent<Rigidbody>();
            var tr = car.transform;
            _trafficVehicles.Add(vr);

            // Karosserie (nur sichtbar): Seitenwände bis zur Gürtellinie, Motorraum, Kofferraum, Räder
            float belt = hipY + 0.15f;
            Visual(tr, "Seitenwand links", new Vector3(-W / 2 + 0.02f, (floorY - 0.1f + belt) / 2, 0), new Vector3(0.04f, belt - floorY + 0.1f, L - 0.3f), color);
            Visual(tr, "Seitenwand rechts", new Vector3(W / 2 - 0.02f, (floorY - 0.1f + belt) / 2, 0), new Vector3(0.04f, belt - floorY + 0.1f, L - 0.3f), color);
            float engineFrom = dashZ + 0.1f;
            Visual(tr, "Motorraum", new Vector3(0, 0.55f, (engineFrom + L / 2) / 2), new Vector3(W - 0.1f, 0.5f, L / 2 - engineFrom), color);
            float trunkTo = zRear - 0.35f;
            Visual(tr, "Kofferraum", new Vector3(0, (floorY + belt) / 2, (trunkTo - L / 2) / 2), new Vector3(W - 0.1f, belt - floorY, trunkTo + L / 2), color);
            float wb = (float)t.wheelbase_m / 2;
            foreach (var wx in new[] { -W / 2 + 0.15f, W / 2 - 0.15f })
                foreach (var wz in new[] { wb, -wb })
                    Visual(tr, "Rad", new Vector3(wx, 0.32f, wz), new Vector3(0.22f, 0.64f, 0.64f), new Color(0.08f, 0.08f, 0.08f));

            // Innenraum (Kollider)
            Part(tr, "Boden", new Vector3(0, floorY - 0.025f, (zFront + zRear) / 2), new Vector3(W - 0.2f, 0.05f, 2.4f), dark);
            Part(tr, "Dach", new Vector3(0, hipY + 1.0f, zFront - 0.3f), new Vector3(W - 0.1f, 0.05f, 2.0f), color, visible: false);
            var shield = Part(tr, "Windschutzscheibe", new Vector3(0, hipY + 0.82f, zFront + 0.62f), new Vector3(W - 0.3f, 0.04f, 0.9f), Color.clear, visible: false, tiltX: -55);
            Part(tr, "Mittelkonsole", new Vector3(0, hipY - 0.05f, zFront + 0.2f), new Vector3(0.12f, 0.3f, 0.8f), dark, visible: false);
            // Front für Fußgänger (beim nachgiebigen Kontakt mit Steifigkeit und Federweg, vehicle_model.json › pedestrianFront)
            _softParts.Add(Part(tr, "Stoßfänger", new Vector3(0, 0.48f, L / 2 - 0.1f), new Vector3(W, 0.22f, 0.2f), color));
            _softParts.Add(Part(tr, "Haubenvorderkante", new Vector3(0, 0.72f, L / 2 - 0.25f), new Vector3(W - 0.1f, 0.2f, 0.5f), color));
            float hoodLen = L / 2 - 0.3f - engineFrom;
            _softParts.Add(Part(tr, "Motorhaube", new Vector3(0, 0.82f, engineFrom + hoodLen / 2), new Vector3(W - 0.1f, 0.08f, hoodLen), color));
            _softParts.Add(shield);   // eine Scheibe für Insassen (innen) und Fußgänger (außen)

            // Eindrückbare Teile: Armaturenbrett (links/rechts), Türen
            float dashW = (W - 0.2f) / 2;
            var dashL = MovingBody(vr, "Armaturenbrett links", pos, rot, TrafficSim.Front, -dashW / 2);
            Part(dashL.transform, "Armaturenbrett", new Vector3(-dashW / 2, hipY + 0.08f, dashZ), new Vector3(dashW, 0.3f, 0.16f), new Color(0.25f, 0.25f, 0.28f));
            var dashR = MovingBody(vr, "Armaturenbrett rechts", pos, rot, TrafficSim.Front, dashW / 2);
            Part(dashR.transform, "Armaturenbrett", new Vector3(dashW / 2, hipY + 0.08f, dashZ), new Vector3(dashW, 0.3f, 0.16f), new Color(0.25f, 0.25f, 0.28f));
            float doorX = W / 2 - 0.08f, doorY = hipY + 0.15f;
            var doors = new[]
            {
                (name: "Tür vorn links", face: TrafficSim.Left, x: -doorX, z: zFront + 0.1f, len: 1.1f),
                (name: "Tür hinten links", face: TrafficSim.Left, x: -doorX, z: zRear, len: 0.9f),
                (name: "Tür vorn rechts", face: TrafficSim.Right, x: doorX, z: zFront + 0.1f, len: 1.1f),
                (name: "Tür hinten rechts", face: TrafficSim.Right, x: doorX, z: zRear, len: 0.9f),
            };
            foreach (var dd in doors)
            {
                var door = MovingBody(vr, dd.name, pos, rot, dd.face, dd.z);
                Part(door.transform, dd.name, new Vector3(dd.x, doorY, dd.z), new Vector3(0.06f, 0.9f, dd.len), color);
            }

            // Sitze und Insassen
            var occupants = b.Def.occupants ?? new TrafficOccupant[0];
            foreach (var o in occupants)
            {
                bool front = o.seat == "fahrer" || o.seat == "beifahrer";
                float sx = (o.seat == "fahrer" || o.seat == "hinten_links" ? -1 : 1) * (front ? 0.37f : 0.38f);
                float sz = front ? zFront : zRear;
                Part(tr, "Sitzpolster", new Vector3(sx, seatTop - 0.06f, sz - 0.02f), new Vector3(0.5f, 0.12f, 0.52f), dark, counts: false);
                float backZ = sz - 0.08f * H - 0.06f;
                Part(tr, "Lehne", new Vector3(sx, hipY + 0.3f, backZ), new Vector3(0.5f, 0.75f, 0.1f), dark, counts: false);
                if (front) Part(tr, "Lehne Rückseite", new Vector3(sx, hipY + 0.3f, backZ - 0.07f), new Vector3(0.5f, 0.75f, 0.04f), dark);   // Anprall der hinteren Insassen
                else Part(tr, "Rückbank", new Vector3(sx, hipY + 0.3f, backZ), new Vector3(0.5f, 0.75f, 0.1f), dark, counts: false);

                var hip = pos + rot * new Vector3(sx, hipY, sz);
                var rd = RagdollBuilder.Build(hip, rot, RagdollPose.Seated, H, p.Mass_kg, p.Male, invertKneeFlexion);
                var h = new ScenarioHuman
                {
                    Label = $"{b.Label}: {SeatLabel(o.seat)}", VehicleId = b.Id, Seat = o.seat, Belted = o.belted, Rd = rd, Vehicle = index,
                    SeatX = sx, SeatZ = sz, DoorZ = front ? zFront + 0.1f : zRear
                };
                if (o.belted) h.Belts = BeltFor(rd, vr.Root);
                Hold(h, pos, rot);
                Humans.Add(h);

                if (o.seat == "fahrer")
                {
                    // Lenkrad dort, wo die Hände sind; folgt dem linken Armaturenbrett zu 60 %
                    Vector3 hr = tr.InverseTransformPoint(rd.HandTipRight), hl = tr.InverseTransformPoint(rd.HandTipLeft);
                    var wheel = new Vector3((hr.x + hl.x) / 2, (hr.y + hl.y) / 2, Mathf.Max(hr.z, hl.z) - 0.02f);
                    float wheelD = Mathf.Clamp(Mathf.Abs(hr.x - hl.x) - 0.08f, 0.32f, 0.42f);
                    var wb2 = MovingBody(vr, "Lenkrad", pos, rot, TrafficSim.Front, wheel.x, 0.6f);
                    Part(wb2.transform, "Lenkrad", wheel, new Vector3(wheelD, wheelD, 0.05f), new Color(0.1f, 0.1f, 0.1f), tiltX: -20);
                    if (p.Airbag) vr.Airbags.Add(TrafficAirbag(vr, wheel, new Vector3(0.5f, 0.5f, 0.2f), 0.25f));
                }
                else if (o.seat == "beifahrer" && p.Airbag)
                    vr.Airbags.Add(TrafficAirbag(vr, new Vector3(sx, hipY + 0.3f, dashZ - 0.15f), new Vector3(0.55f, 0.5f, 0.3f), 0.3f));
            }
        }

        static string SeatLabel(string seat) =>
            seat == "fahrer" ? "Fahrer/in" : seat == "beifahrer" ? "Beifahrer/in" : seat == "hinten_links" ? "hinten links" : seat == "hinten_rechts" ? "hinten rechts" : seat;

        GameObject MovingBody(TrafficVehicleRig vr, string name, Vector3 pos, Quaternion rot, int face, float center, float share = 1)
        {
            var go = Vehicle(name, pos);
            go.transform.rotation = rot;
            vr.Parts.Add(new MovingPart { Rb = go.GetComponent<Rigidbody>(), Face = face, Center = center, Share = share });
            return go;
        }

        /// <summary>Airbag im Fahrzeugsystem: entfaltet sich in 25 ms Richtung Insasse (−z), danach federndes Polster am Fahrzeug.</summary>
        Action<float> TrafficAirbag(TrafficVehicleRig vr, Vector3 local, Vector3 size, float travel)
        {
            const float deployTime = 0.025f;
            var carRb = vr.Root;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Airbag";
            go.transform.SetPositionAndRotation(carRb.transform.TransformPoint(local), carRb.transform.rotation);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material = CrashPhysics.Mat(new Color(0.92f, 0.92f, 0.88f));
            go.AddComponent<CrashSurface>().kind = "airbag";
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var col = go.GetComponent<Collider>();
            foreach (var c in carRb.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
            foreach (var part in vr.Parts) foreach (var c in part.Rb.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
            _spawned.Add(go);
            bool deployed = false;
            return time =>
            {
                var carPos = carRb.position; var carRot = carRb.rotation;
                if (deployed) return;
                if (time < vr.AirbagAt) { rb.MovePosition(carPos + carRot * local); rb.MoveRotation(carRot); return; }
                float u = Mathf.Clamp01((time - vr.AirbagAt) / deployTime);
                rb.MovePosition(carPos + carRot * (local + new Vector3(0, 0, -travel * u)));
                rb.MoveRotation(carRot);
                if (u < 1) return;
                deployed = true;
                rb.isKinematic = false;
                var j = go.AddComponent<ConfigurableJoint>();
                j.connectedBody = carRb;
                j.autoConfigureConnectedAnchor = true;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Locked;
                var drive = new JointDrive { positionSpring = 25000, positionDamper = 1500, maximumForce = float.MaxValue };
                j.xDrive = j.yDrive = j.zDrive = drive;
            };
        }

        void BuildPedestrian(CrashParams p, int index)
        {
            var w = _traffic.Walkers[index];
            var d = RagdollBuilder.Dims(p.Height_m);
            float hipY = d.KneeToSole + d.HipToKnee + 0.01f;
            double psi = w.Def.heading_deg * Math.PI / 180;
            var pos = V3(w.X, w.Z); var rot = Yaw(psi);
            var rd = RagdollBuilder.Build(pos + new Vector3(0, hipY, 0), rot, RagdollPose.Standing, p.Height_m, p.Mass_kg, p.Male, invertKneeFlexion);
            var h = new ScenarioHuman { Label = w.Label ?? "Fußgänger", Pedestrian = true, Rd = rd, Walker = index, MechanismId = "fussgaenger" };
            Hold(h, pos, rot);
            Humans.Add(h);
        }

        static void Hold(ScenarioHuman h, Vector3 framePos, Quaternion frameRot)
        {
            var inv = Quaternion.Inverse(frameRot);
            foreach (var rb in h.Rd.Root.GetComponentsInChildren<Rigidbody>())
            {
                rb.isKinematic = true;
                h.Held.Add(new HeldBody { Rb = rb, LocalPos = inv * (rb.position - framePos), LocalRot = inv * rb.rotation });
            }
        }

        /// <summary>Vorausberechnete Bahnen als Linien am Boden (Fahrzeuge in ihrer Farbe, Fußgänger weiß).</summary>
        void DrawPaths()
        {
            var pr = TrafficPrediction;
            for (int i = 0; i < pr.Paths.Count; i++)
            {
                var t = _traffic.Bodies[i].Type;
                var c = t.color != null && t.color.Length >= 3 ? new Color(t.color[0], t.color[1], t.color[2]) : Color.gray;
                Line("Bahn " + _traffic.Bodies[i].Label, pr.Paths[i], c);
            }
            for (int i = 0; i < pr.WalkerPaths.Count; i++) Line("Weg " + _traffic.Walkers[i].Label, pr.WalkerPaths[i], Color.white);
        }

        void Line(string name, double[] xz, Color c)
        {
            if (xz == null || xz.Length < 4) return;
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.08f;
            var pts = new Vector3[xz.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector3((float)xz[2 * i], 0.02f, (float)xz[2 * i + 1]);
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.material = CrashPhysics.Mat(c);
            _spawned.Add(go);
        }

        // ------------------------------------------------------------------ Bewegung

        /// <summary>Karosserien, eindrückbare Teile, Airbags und gehaltene Personen auf den Stand der Fahrdynamik bringen.</summary>
        void MoveTraffic(float time)
        {
            foreach (var vr in _trafficVehicles)
            {
                var b = _traffic.Bodies[vr.Index];
                var pos = V3(b.X, b.Z); var rot = Yaw(b.Psi);
                vr.Root.MovePosition(pos); vr.Root.MoveRotation(rot);
                foreach (var part in vr.Parts)
                {
                    Vector3 off = Vector3.zero;
                    if (part.Face >= 0)
                    {
                        var fs = b.Faces[part.Face];
                        // Eindringen am Kontaktort voll, 1 m daneben nicht mehr (Annahme: Last verteilt sich über Tür bzw. Querträger)
                        float fall = Mathf.Clamp01(1 - Mathf.Abs(part.Center - (float)fs.Position) / 1.0f);
                        float intr = (float)fs.Intrusion * fall * part.Share;
                        off = part.Face == TrafficSim.Front ? new Vector3(0, 0, -intr) : part.Face == TrafficSim.Left ? new Vector3(intr, 0, 0) : new Vector3(-intr, 0, 0);
                    }
                    part.Rb.MovePosition(pos + rot * off); part.Rb.MoveRotation(rot);
                }
                // Airbag-Auslösung: Frontaufprall (Verformung der Front > 3 cm), 15 ms Sensorlaufzeit
                if (float.IsPositiveInfinity(vr.AirbagAt) && b.Faces[TrafficSim.Front].Crush > 0.03) vr.AirbagAt = time + 0.015f;
                foreach (var a in vr.Airbags) a(time);
            }
            foreach (var h in Humans)
            {
                if (h.Released) continue;
                Vector3 fp; Quaternion fr;
                FramePose(h, out fp, out fr);
                if (time >= h.ReleaseAt)
                {
                    h.Released = true;
                    foreach (var hb in h.Held)
                    {
                        hb.Rb.isKinematic = false;
                        CrashPhysics.SetVelocity(hb.Rb, FrameVelocity(h, hb.Rb.position));
                    }
                    continue;
                }
                foreach (var hb in h.Held) { hb.Rb.MovePosition(fp + fr * hb.LocalPos); hb.Rb.MoveRotation(fr * hb.LocalRot); }
            }
        }

        void FramePose(ScenarioHuman h, out Vector3 pos, out Quaternion rot)
        {
            if (h.Pedestrian)
            {
                var w = _traffic.Walkers[h.Walker];
                pos = V3(w.X, w.Z); rot = Yaw(w.Def.heading_deg * Math.PI / 180);
            }
            else
            {
                var b = _traffic.Bodies[h.Vehicle];
                pos = V3(b.X, b.Z); rot = Yaw(b.Psi);
            }
        }

        Vector3 FrameVelocity(ScenarioHuman h, Vector3 world)
        {
            if (h.Pedestrian) { var w = _traffic.Walkers[h.Walker]; return V3(w.Vx, w.Vz); }
            var b = _traffic.Bodies[h.Vehicle];
            double vx, vz;
            b.PointVelocity(world.x, world.z, out vx, out vz);
            return V3(vx, vz);
        }

        // ------------------------------------------------------------------ Ablauf

        /// <summary>
        /// Szenario rechnen: Vorlauf mit 1 ms (Fahrzeuge fahren, bremsen, weichen aus; gehaltene Personen fahren mit),
        /// ab 30 ms vor dem ersten Kontakt 0,1 ms mit Messung bis 0,5 s nach dem letzten Kontakt (höchstens 2,5 s).
        /// </summary>
        IEnumerator RunTraffic(CrashParams p, float slowMotion, Action<List<CrashMeasurement>> done, Action<float> progress, bool playback)
        {
            Running = true;
            CrashApproach unused; Action<float, float> noDrive;   // vorab deklariert (mcs: out in Iteratoren)
            Build(p, out noDrive, out unused);
            if (_traffic == null || Humans.Count == 0)
            {
                Running = false;
                done(new List<CrashMeasurement>());
                yield break;
            }
            var pr = TrafficPrediction;
            double first = pr.FirstContact, last = pr.LastContact;
            float fineStart = first >= 0 ? (float)Math.Max(0, first - 0.03) : float.PositiveInfinity;
            float end = first >= 0 ? (float)Math.Min(last + 0.5, fineStart + 2.5) : (float)_traffic.PlannedImpact + 1.5f;
            float dtFine = crashStep_s, dtCoarse = approachStep_s;
            int fineSteps = first >= 0 ? Mathf.CeilToInt((end - fineStart) / dtFine) : 0;

            var savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _savedMode = savedMode; _modeSaved = true;
            CrashSensor.ClearLookup();
            BeginRecording();
            SetVisible(false);
            Physics.SyncTransforms();
            bool soft = p.SoftContact && RegisterSoftBody(p, Anatomy.BodyFatShape.Model());
            if (soft) RegisterSoftVehicleParts();
            SoftContact.Enabled = soft;
            var tone = LoadMuscleTone();
            foreach (var h in Humans) h.Muscles = tone != null ? new MuscleToneDriver(h.Rd, tone, p.Occupant, p.Male) : null;
            var watch = new System.Diagnostics.Stopwatch();

            CrashStart = 0;
            RecordFrame(0);
            float t = 0, lastFrame = 0;
            bool fine = false;
            int fineDone = 0;
            while (t < end - 1e-6f)
            {
                watch.Restart();
                do
                {
                    if (!fine && t >= fineStart - 1e-6f)
                    {
                        fine = true;
                        CrashStart = t;
                        foreach (var h in Humans)
                        {
                            foreach (var s in h.Rd.Sensors) s.StartRecording(dtFine, fineSteps + 1);
                            h.Gauges = gameObject.AddComponent<CrashGauges>();
                            h.Gauges.Begin(h.Rd);
                            h.ShoulderBeltImpulse_Ns = h.LapBeltImpulse_Ns = 0;
                        }
                    }
                    float dt = fine ? dtFine : dtCoarse;
                    _simTime = t - CrashStart;
                    _traffic.Step(dt);
                    MoveTraffic(t + dt);
                    foreach (var h in Humans)
                    {
                        // Wer die Gefahr bemerkt (Fahrer reagiert), spannt sich an — außer bewusstlos
                        if (h.Muscles != null && t >= h.BraceAt && p.Occupant == OccupantState.Entspannt) h.Muscles.SetState(OccupantState.Angespannt);
                        if (h.Released) h.Muscles?.Apply(fine ? t - CrashStart : 0);
                        if (!fine) continue;
                        foreach (var s in h.Rd.Sensors) s.Step(dt);
                        ConfigurableJoint j;
                        if (h.Belts.TryGetValue("Sternum", out j) && j) h.ShoulderBeltImpulse_Ns += j.currentForce.magnitude * dt;
                        if (h.Belts.TryGetValue("Becken", out j) && j) h.LapBeltImpulse_Ns += j.currentForce.magnitude * dt;
                        h.Gauges.Step();
                    }
                    SoftContact.StepDt = dt;
                    Physics.Simulate(dt);
                    t += dt;
                    if (fine) fineDone++;
                    if (t - lastFrame >= recordInterval_s - 1e-6f) { RecordFrame(t); lastFrame = t; }
                } while (t < end - 1e-6f && watch.Elapsed.TotalMilliseconds < computeBudget_ms);
                progress?.Invoke(Mathf.Clamp01(fine ? 0.1f + 0.9f * fineDone / Mathf.Max(1, fineSteps) : 0.1f * t / Mathf.Max(0.01f, fineStart)));
                yield return null;
            }
            if (_frameTimes[_frameTimes.Count - 1] < t) RecordFrame(t);
            SoftContact.Enabled = false;

            // Messung je Person
            foreach (var h in Humans)
            {
                if (h.Gauges == null) { h.Measurements = new List<CrashMeasurement>(); h.Summary = "kein Kontakt"; continue; }
                foreach (var s in h.Rd.Sensors) s.StopRecording();
                h.Gauges.Stop();
                AbdomenLoad ab;
                h.Measurements = Measure(p, h.Gauges, h.Rd, out ab);
                h.Abdomen = ab;
                Destroy(h.Gauges);
                h.Gauges = null;
                if (!h.Pedestrian) h.MechanismId = MechanismFor(_traffic.Bodies[h.Vehicle]);
            }
            Running = false;
            SelectHuman(Mathf.Clamp(p.Patient, 0, Humans.Count - 1));
            var measured = Humans[Mathf.Clamp(p.Patient, 0, Humans.Count - 1)].Measurements;
            if (playback) yield return Replay(slowMotion, () => done(measured));
            else { SetVisible(true); RestorePhysics(); done(measured); }
        }

        /// <summary>
        /// Verformung am Sitzplatz einer Person: Tür der eigenen Seite (Eindringen am Sitz und Verformung an der Tür)
        /// und Fußraum (Front, auf der Sitzseite). Gleiche Verteilung entlang der Seite wie bei den Türen (voll am Kontaktort,
        /// 1 m daneben nicht mehr).
        /// </summary>
        public void SeatDeformation(ScenarioHuman h, out double side_m, out double footwell_m, out double door_m)
        {
            side_m = footwell_m = door_m = 0;
            if (_traffic == null || h.Pedestrian || h.Vehicle < 0) return;
            var b = _traffic.Bodies[h.Vehicle];
            Data.Rescue.SeatDeformation(b.Faces[h.SeatX < 0 ? TrafficSim.Left : TrafficSim.Right], b.Faces[TrafficSim.Front],
                                        h.Seat == "fahrer" || h.Seat == "beifahrer", h.SeatX, h.SeatZ, h.DoorZ, out side_m, out footwell_m, out door_m);
        }

        /// <summary>Mechanismus für Text/Rettungszeit nach der Hauptstoßrichtung: Front/Heck → frontal, Seite → seitlich.</summary>
        static string MechanismFor(TrafficSim.Body b)
        {
            int f = b.MainFace();
            return f == TrafficSim.Left || f == TrafficSim.Right ? "pkw_seite" : "pkw_frontal";
        }

        /// <summary>Kurzer Unfallbericht: je Fahrzeug Δv, Stoßrichtung, Verformung und Eindringen.</summary>
        public string TrafficReport()
        {
            if (_traffic == null) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _traffic.VehicleCount; i++)
            {
                var b = _traffic.Bodies[i];
                int f = b.MainFace();
                if (f < 0) { sb.AppendLine($"{b.Label}: kein Anprall"); continue; }
                string face = f == TrafficSim.Front ? "Front" : f == TrafficSim.Rear ? "Heck" : f == TrafficSim.Left ? "links" : "rechts";
                var fs = b.Faces[f];
                sb.AppendLine($"{b.Label}: Δv {b.MaxDeltaV * 3.6:0} km/h, Stoß aus {b.PdofClock()} Uhr, {face} {fs.MaxCrush * 100:0} cm verformt" +
                              (fs.Intrusion > 0.01 ? $", Innenraum {fs.Intrusion * 100:0} cm eingedrückt" : ""));
            }
            foreach (var w in _traffic.Walkers)
                sb.AppendLine(w.FirstContact >= 0 ? $"{w.Label}: erfasst mit {w.ContactSpeed * 3.6:0} km/h" : $"{w.Label}: nicht erfasst");
            return sb.ToString();
        }
    }
}

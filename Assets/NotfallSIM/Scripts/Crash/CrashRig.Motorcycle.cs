using System;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Motorradunfall mit den Anprallkonfigurationen nach ISO 13232-2 (vehicle_model.json › motorcycleConfigs) und Alleinsturz.
    /// Das Motorrad folgt <see cref="MotorcycleSim"/> (Seitenansicht: Gabel staucht, Heck hebt ab, Nicken); der PKW fährt
    /// kinematisch, seine Seite bzw. Front gibt nach (in Reihe mit der Gabel). Der Fahrer ist ein Ragdoll und prallt
    /// gegen Tank, Lenker und den PKW. Vereinfachungen: kein Rollen/Gieren des Motorrads beim Anprall, Anprallfläche quer zur Fahrtrichtung
    /// (schräge Winkel wirken nur über die Bewegung des PKW), Rückwirkung des Fahrers auf das Motorrad fehlt.
    /// </summary>
    public partial class CrashRig
    {
        const float BikeWheelZ = 0.1f;      // Mitte zwischen den Rädern relativ zum Fahrzeugursprung
        float _faceShift;

        public static MotorcycleConfig MotorcycleConfigFor(string id)
        {
            var list = LoadVehicleModel()?.motorcycleConfigs;
            if (list == null || list.Length == 0) return null;
            return list.FirstOrDefault(c => c.id == id) ?? list[0];
        }

        float BuildMotorcycle(CrashParams p, CrashApproach approach, out Action<float, float> drive)
        {
            drive = null;
            var vm = LoadVehicleModel();
            var mm = vm?.motorcycle;
            var cfg = MotorcycleConfigFor(p.MotoConfig) ?? new MotorcycleConfig { id = "alt", contact = "side", heading_deg = 90 };
            float H = p.Height_m;
            bool bikeSide = cfg.contact == "bikeside", alone = cfg.contact == "none";
            float vBike = bikeSide ? 0 : p.Speed_kmh / 3.6f;
            float vCar = bikeSide ? p.Speed_kmh / 3.6f : (float)cfg.carSpeed_m_s;

            // ---------- Motorrad und Fahrer ----------
            const float seatY = 0.85f;
            var bike = Vehicle("Motorrad", Vector3.zero);
            var t = bike.transform;
            var dm = RagdollBuilder.Dims(H);
            float seatTop = seatY - dm.ThighRadius;
            Part(t, "Rumpf", new Vector3(0, 0.45f, BikeWheelZ), new Vector3(0.2f, 0.46f, 1.4f), Color.gray, visible: false);
            Part(t, "Sitzbank", new Vector3(0, seatTop - 0.04f, -0.05f), new Vector3(0.24f, 0.08f, 0.5f), Color.gray, counts: false, visible: false);
            // Tank: Becken und Bauch prallen dagegen (Beine streifen ihn nur — mit den Beinen keine Kollision)
            var tank = Part(t, "Tank", new Vector3(0, seatTop + 0.09f, 0.32f), new Vector3(0.12f, 0.18f, 0.3f), Color.gray, visible: false);
            Ragdoll = RagdollBuilder.Build(new Vector3(0, seatY, 0), Quaternion.identity, RagdollPose.Straddling, H, p.Mass_kg, p.Male, invertKneeFlexion, headScale: 1.25f);
            var tankCol = tank.GetComponent<Collider>();
            foreach (var kv in Ragdoll.Segments.Where(k => k.Key.StartsWith("Oberschenkel") || k.Key.StartsWith("Unterschenkel") || k.Key.StartsWith("Fuß")))
                foreach (var c in kv.Value.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(tankCol, c);
            Vector3 mr = Ragdoll.HandTipRight, ml = Ragdoll.HandTipLeft;
            var bar = new Vector3(0, (mr.y + ml.y) / 2 - 0.03f, Mathf.Max(mr.z, ml.z) + 0.03f);
            float barW = Mathf.Abs(mr.x - ml.x) + 0.15f;
            Part(t, "Lenker", bar, new Vector3(barW, 0.04f, 0.04f), Color.gray, visible: false);
            float bikeCrush = 0, carDent = 0, carDentPos = 0;
            var moto = VehicleVisual.Moto(t, new VehicleShape.MotoProfile { SeatTop = seatTop, SeatZ = -0.05f, BarY = bar.y, BarZ = bar.z - BikeWheelZ, BarWidth = barW },
                                          new Color(0.1f, 0.25f, 0.55f), new Vector3(0, 0, BikeWheelZ));
            moto.Source = () => new DeformState { Front = bikeCrush };
            _visuals.Add(moto);
            var rbBike = bike.GetComponent<Rigidbody>();
            if (vBike > 0) Drive(approach, new Vector3(0, 0, vBike), true);
            else approach.RagdollVelocity = Vector3.zero;

            if (alone)
            {
                // Rutscht weg: Motorrad kippt in 0,35 s auf die linke Seite und schlittert mit µ·g aus; der Fahrer stürzt und rutscht
                foreach (var c in bike.GetComponentsInChildren<Collider>())
                    foreach (var kv in Ragdoll.Segments.Where(k => k.Key.StartsWith("Unterschenkel") || k.Key.StartsWith("Fuß") || k.Key.StartsWith("Oberschenkel")))
                        foreach (var rc in kv.Value.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(c, rc);
                float mu = mm != null ? (float)mm.slideFriction : 0.45f, vNow = vBike;
                const float tSlip = 0.05f, tipTime = 0.35f;
                drive = (time, dt) =>
                {
                    float u = Mathf.Clamp01((time - tSlip) / tipTime);
                    float roll = 85f * u * u * (3 - 2 * u);
                    if (u >= 1) vNow = Mathf.Max(0, vNow - mu * 9.81f * dt);
                    var pos = rbBike.position + new Vector3(0, 0, vNow * dt);
                    rbBike.MovePosition(pos);
                    rbBike.MoveRotation(Quaternion.Euler(0, 0, roll));
                };
                return 2.0f;
            }

            // ---------- Unfallgegner (PKW) ----------
            const float L = 4.4f, W = 1.75f, gap = 0.4f;
            float phi = (float)cfg.heading_deg * Mathf.Deg2Rad;
            var f = new Vector2(Mathf.Sin(phi), Mathf.Cos(phi));        // Fahrtrichtung (x, z)
            var r = new Vector2(Mathf.Cos(phi), -Mathf.Sin(phi));       // rechts
            var u3 = new Vector3(f.x, 0, f.y) * vCar;
            Vector2 n;                                                   // Normale der getroffenen Fläche (zum Motorrad)
            Vector2 center;                                              // Mitte des PKW beim Kontakt
            float tHit;
            bool hitRight = false;
            float bikeFrontZ = BikeWheelZ + 0.725f + 0.31f;
            if (bikeSide)
            {
                // PKW-Front trifft die rechte Seite des stehenden Motorrads (Höhe der Beine)
                tHit = gap / Mathf.Max(0.1f, vCar);
                n = f;
                center = new Vector2(0.2f, 0.15f) - f * (L / 2);
            }
            else
            {
                tHit = gap / Mathf.Max(0.1f, vBike);
                var contact = new Vector2(0, bikeFrontZ + gap);
                if (cfg.contact == "front")
                {
                    n = f;
                    center = contact - f * (L / 2) + r * (float)cfg.offset_m;
                }
                else
                {
                    hitRight = r.y < 0;
                    n = hitRight ? r : -r;
                    center = contact - n * (W / 2) + f * (float)cfg.offset_m;
                }
            }
            var car = Vehicle("Unfallgegner (PKW)", new Vector3(center.x, 0, center.y) - u3 * tHit);
            car.transform.rotation = Quaternion.Euler(0, (float)cfg.heading_deg, 0);
            var ct = car.transform;
            var red = new Color(0.55f, 0.1f, 0.1f);
            Part(ct, "PKW Karosserie", new Vector3(0, 0.6f, -0.6f), new Vector3(W, 0.7f, L - 1.2f), red, visible: false);
            Part(ct, "PKW Motorraum", new Vector3(0, 0.45f, L / 2 - 0.85f), new Vector3(W, 0.5f, 0.7f), red, visible: false);
            Part(ct, "PKW Kabine", new Vector3(0, 1.15f, -0.45f), new Vector3(W - 0.15f, 0.42f, 1.3f), red, visible: false);
            _softParts.Add(Part(ct, "Stoßfänger", new Vector3(0, 0.48f, L / 2 - 0.1f), new Vector3(W, 0.22f, 0.2f), red, visible: false));
            _softParts.Add(Part(ct, "Haubenvorderkante", new Vector3(0, 0.72f, L / 2 - 0.3f), new Vector3(W - 0.1f, 0.2f, 0.5f), red, visible: false));
            _softParts.Add(Part(ct, "Motorhaube", new Vector3(0, 0.84f, L / 2 - 1.0f), new Vector3(W - 0.1f, 0.08f, 1.0f), red, visible: false));
            _softParts.Add(Part(ct, "Windschutzscheibe", new Vector3(0, 1.15f, 0.45f), new Vector3(W - 0.3f, 0.05f, 0.76f), red, visible: false, tiltX: 37.5f));
            bool frontFace = cfg.contact == "front" || bikeSide;
            CarModel(ct, SedanProfile(L, W), red, Vector3.zero, 0, () => frontFace
                ? new DeformState { Front = carDent, FrontPos = carDentPos }
                : hitRight ? new DeformState { Right = carDent, RightPos = carDentPos } : new DeformState { Left = carDent, LeftPos = carDentPos });
            var rbCar = car.GetComponent<Rigidbody>();
            if (vCar > 0)
            {
                approach.Carry.Add((rbCar, u3));
                Shift(new[] { rbCar }, -u3 * approachTime_s);
            }

            if (bikeSide)
            {
                // Motorrad wird mitgerissen (in 40 ms auf 90 % der PKW-Geschwindigkeit) und kippt vom PKW weg
                float brakeAt = tHit + 0.25f, mu = mm != null ? (float)mm.slideFriction : 0.45f, vb = 0;
                drive = (time, dt) =>
                {
                    float vel = time < brakeAt ? vCar : Mathf.Max(0, vCar - 8f * (time - brakeAt));
                    rbCar.MovePosition(rbCar.position + u3.normalized * (vel * dt));
                    if (time >= tHit)
                    {
                        float k = Mathf.Clamp01((time - tHit) / 0.04f);
                        vb = time < tHit + 0.04f ? 0.9f * vCar * k : Mathf.Max(0, vb - mu * 9.81f * dt);
                        float roll = 80f * Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - tHit) / 0.5f));
                        rbBike.MovePosition(rbBike.position + new Vector3(f.x, 0, f.y) * (vb * dt));
                        rbBike.MoveRotation(Quaternion.Euler(0, 0, roll * Mathf.Sign(-f.x)));
                    }
                };
                return 1.5f;
            }

            // Anprallfläche entlang der Fahrlinie des Motorrads (x = 0): z(t) = z_c + (n·u)/n_z · (t − tHit), solange die Fläche die Linie schneidet
            float zc = bikeFrontZ + gap;
            float rate = Mathf.Abs(n.y) > 1e-3f ? Vector2.Dot(n, f * vCar) / n.y : 0;
            float halfFace = frontFace ? W / 2 : L / 2;
            Vector2 along = frontFace ? r : f;
            MotorcycleSim sim = null;
            _faceShift = 0;
            float lastContact = -1, fallStart = -1, theta0 = 0, y0 = 0;
            drive = (time, dt) =>
            {
                if (sim == null && mm != null)
                    sim = new MotorcycleSim(mm, rbBike.position.z, vBike, BikeWheelZ) { CarStiffness = frontFace ? 3.0e5 : 4.4e5 };
                // PKW bremst 0,25 s nach dem Anprall mit 7 m/s² (Reaktion des Fahrers), bleibt im Bild
                float vNow = time < tHit + 0.25f ? vCar : Mathf.Max(0, vCar - 7f * (time - tHit - 0.25f));
                var carPos = rbCar.position + (vCar > 0 ? u3 / vCar * vNow : Vector3.zero) * dt;
                rbCar.MovePosition(carPos);
                if (sim == null) return;
                // Fläche bewegt sich mit dem PKW (beim Bremsen langsamer): Weg aufsummieren
                if (time >= tHit) _faceShift += rate * (vCar > 0 ? vNow / vCar : 0) * dt;
                float zo = zc + _faceShift + (time < tHit ? rate * (time - tHit) : 0);
                // Schnittpunkt noch auf der Fläche? (Ort entlang der Fläche relativ zur PKW-Mitte)
                float s = Vector2.Dot(new Vector2(0, zo) - new Vector2(carPos.x, carPos.z), along);
                bool active = Mathf.Abs(s) <= halfFace - 0.05f;
                if (fallStart < 0) sim.Step(dt, active ? zo : double.PositiveInfinity);
                if (sim.ForkForce > 0) lastContact = time;
                double ry, rz;
                sim.Root(out ry, out rz);
                // Nach dem Anprall (keine Kraft mehr, Heck hoch) kippt das Motorrad in Wirklichkeit seitlich weg — die
                // Seitenansicht kann das nicht: in 0,5 s auf die Seite legen und das Nicken zurücknehmen
                if (fallStart < 0 && lastContact >= 0 && time - lastContact > 0.08f && sim.Theta > 0.35)
                { fallStart = time; theta0 = (float)sim.Theta; y0 = (float)ry; }
                if (fallStart >= 0)
                {
                    float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - fallStart) / 0.5f));
                    rbBike.MovePosition(new Vector3(rbBike.position.x, Mathf.Lerp(y0, 0.15f, u), rbBike.position.z));
                    rbBike.MoveRotation(Quaternion.Euler(theta0 * (1 - u) * Mathf.Rad2Deg, 0, 85f * u));
                    bikeCrush = (float)sim.VisibleCrush;
                    return;
                }
                rbBike.MovePosition(new Vector3(rbBike.position.x, (float)ry, (float)rz));
                rbBike.MoveRotation(Quaternion.Euler((float)(sim.Theta * Mathf.Rad2Deg), 0, 0));
                bikeCrush = (float)sim.VisibleCrush;
                if (sim.CarCrush > carDent) { carDent = (float)sim.CarCrush; carDentPos = frontFace ? -s * Mathf.Sign(Vector2.Dot(along, r)) : s; }
            };
            return 1.2f;
        }
    }
}

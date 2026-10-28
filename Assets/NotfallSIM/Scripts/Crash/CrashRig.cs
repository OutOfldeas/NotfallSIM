using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>Was die Sandbox einstellt.</summary>
    public class CrashParams
    {
        public string MechanismId = "pkw_frontal"; // pkw_frontal, pkw_seite, motorrad, fussgaenger, sturz_hoehe
        public float Speed_kmh = 50;              // beim Sturz: Höhe in m
        public bool Belted = true;
        public bool Airbag = true;                // nur PKW frontal
        public float Height_m = 1.80f, Mass_kg = 80;
        public bool Male = true;
        public int Seed = 1;
        public OccupantState Occupant = OccupantState.Entspannt;   // Muskeltonus (muscle_tone.json)
    }

    /// <summary>
    /// Baut eine Unfallsituation (Fahrzeug bzw. Untergrund + Ragdoll), lässt sie mit feinem Zeitschritt
    /// laufen und liefert die Messwerte für <see cref="InjuryRisk"/>.
    ///
    /// Vereinfachungen (didaktisch, siehe docs/LITERATUR.md):
    /// - PKW frontal/seitlich als Schlittentest: das Fahrzeug folgt einem vorgegebenen Crashpuls (Halbsinus),
    ///   statt sich selbst zu verformen. So arbeiten auch reale Schlittenversuche.
    /// - Fußgänger: Fahrzeugfront mit konstanter Geschwindigkeit, danach Bremsung.
    /// - Sturz: Aufprallgeschwindigkeit √(2gh) wird direkt vorgegeben (spart die Fallzeit).
    /// - Motorrad: Maschine stoppt am Hindernis, Fahrer fliegt weiter. Helm dämpft die Kopfbeschleunigung pauschal.
    /// </summary>
    public class CrashRig : MonoBehaviour
    {
        [Tooltip("Physikschritt während des Aufpralls (s). Crashtests messen mit ≥ 10 kHz; 0,5 ms ist ein Kompromiss.")]
        [SerializeField] float crashStep_s = 0.0001f; // 10 kHz: nötig für den Kopffilter CFC 1000 (SAE J211)
        [Tooltip("VERIFY: umschalten, falls die Knie nach hinten durchknicken.")]
        [SerializeField] bool invertKneeFlexion = false;
        [Tooltip("Pauschale Dämpfung der Kopfbeschleunigung durch den Helm (didaktische Setzung).")]
        [SerializeField] float helmetHeadFactor = 0.6f;

        public Ragdoll Ragdoll { get; private set; }
        public bool Running { get; private set; }
        public float SimTime => Time.time - _t0;
        public Transform CameraTarget => Ragdoll?.Pelvis.transform;

        readonly List<GameObject> _spawned = new List<GameObject>();
        float _t0;
        float _savedStep;

        // ------------------------------------------------------------------ Aufbau

        public void Clear()
        {
            // Sofort deaktivieren: Destroy greift erst am Ende des Frames, die neuen Teile entstehen am selben Ort.
            foreach (var go in _spawned) if (go) { go.SetActive(false); Destroy(go); }
            _spawned.Clear();
            if (Ragdoll != null && Ragdoll.Root) { Ragdoll.Root.SetActive(false); Destroy(Ragdoll.Root); }
            Ragdoll = null;
        }

        GameObject Part(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, bool counts = true, bool visible = true, float tiltX = 0, float tiltZ = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(tiltX, 0, tiltZ);
            go.transform.localScale = size;
            go.AddComponent<CrashSurface>().countsForInjury = counts;
            var r = go.GetComponent<Renderer>();
            if (visible) r.material = CrashPhysics.Mat(color); else r.enabled = false;
            return go;
        }

        GameObject Vehicle(string name, Vector3 pos)
        {
            var root = new GameObject(name);
            root.transform.position = pos;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            _spawned.Add(root);
            return root;
        }

        void Ground()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Boden";
            g.transform.position = new Vector3(0, -0.25f, 0);
            g.transform.localScale = new Vector3(200, 0.5f, 200);
            g.AddComponent<CrashSurface>();
            g.GetComponent<Renderer>().material = CrashPhysics.Mat(new Color(0.32f, 0.34f, 0.36f));
            _spawned.Add(g);
        }

        /// <summary>Baut die Szene für die gewählten Parameter und gibt die Dauer der Simulation zurück.</summary>
        public float Build(CrashParams p, out Action<float> drive)
        {
            Clear();
            Ground();
            float H = p.Height_m;
            float v = p.Speed_kmh / 3.6f;
            drive = null;
            var rng = new System.Random(p.Seed);

            switch (p.MechanismId)
            {
                case "pkw_frontal":
                case "pkw_seite":
                {
                    var d = RagdollBuilder.Dims(H);
                    float hipY = d.KneeToSole + 0.05f;       // Füße (Haut inkl. Ferse) knapp über dem Fahrzeugboden
                    float seatTop = hipY - d.ThighRadius;    // Oberschenkel liegen auf der Sitzfläche
                    bool frontal = p.MechanismId == "pkw_frontal";
                    var car = Vehicle("Fahrzeug", Vector3.zero);
                    var t = car.transform;
                    Part(t, "Boden", new Vector3(0, -0.025f, 0.2f), new Vector3(1.5f, 0.05f, 2f), new Color(0.2f, 0.2f, 0.22f));
                    Part(t, "Sitzpolster", new Vector3(0, seatTop - 0.06f, -0.02f), new Vector3(0.52f, 0.12f, 0.52f), new Color(0.15f, 0.15f, 0.17f), counts: false);
                    Part(t, "Lehne", new Vector3(0, hipY + 0.3f, -0.08f * H - 0.06f), new Vector3(0.52f, 0.75f, 0.1f), new Color(0.15f, 0.15f, 0.17f), counts: false);
                    Part(t, "Armaturenbrett", new Vector3(0, hipY + 0.08f, d.HipToKnee + 0.2f), new Vector3(1.3f, 0.3f, 0.16f), new Color(0.25f, 0.25f, 0.28f));
                    Part(t, "Windschutzscheibe", new Vector3(0, hipY + 0.82f, 0.62f), new Vector3(1.4f, 0.04f, 0.9f), Color.clear, visible: false, tiltX: -55);
                    Part(t, "Dach", new Vector3(0, hipY + 1.0f, -0.1f), new Vector3(1.4f, 0.05f, 1.4f), Color.clear, visible: false);
                    Part(t, "Fahrertür", new Vector3(-0.42f, hipY + 0.15f, 0.1f), new Vector3(0.06f, 0.9f, 1.3f), new Color(0.55f, 0.1f, 0.1f), visible: !frontal);
                    Part(t, "Mittelkonsole", new Vector3(0.38f, hipY - 0.05f, 0.2f), new Vector3(0.12f, 0.3f, 0.8f), new Color(0.2f, 0.2f, 0.22f), visible: false);
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, hipY, 0), Quaternion.identity, RagdollPose.Seated, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    // Lenkrad dort, wo die Hände sind
                    Vector3 hr = Ragdoll.HandTipRight, hl = Ragdoll.HandTipLeft;
                    var wheel = new Vector3(0, (hr.y + hl.y) / 2, Mathf.Max(hr.z, hl.z) - 0.02f);
                    float wheelD = Mathf.Clamp(Mathf.Abs(hr.x - hl.x) - 0.08f, 0.32f, 0.42f);
                    Part(t, "Lenkrad", wheel, new Vector3(wheelD, wheelD, 0.05f), new Color(0.1f, 0.1f, 0.1f), tiltX: -20);
                    if (p.Belted) Belt(car.GetComponent<Rigidbody>(), H);

                    var rbCar = car.GetComponent<Rigidbody>();
                    float t0 = 0.02f;
                    if (frontal)
                    {
                        // Halbsinus-Verzögerung, Δv = v, Dauer 100 ms (typischer Frontalcrashpuls)
                        const float T = 0.1f;
                        Ragdoll.SetVelocity(new Vector3(0, 0, v));
                        Action<float> airbag = p.Airbag ? Airbag(car, wheel, t0 + 0.015f) : null;
                        drive = time =>
                        {
                            float u = Mathf.Clamp01((time - t0) / T);
                            float vel = v - v * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                            rbCar.MovePosition(rbCar.position + new Vector3(0, 0, vel * Time.fixedDeltaTime));
                            airbag?.Invoke(time);
                        };
                        return 0.6f;
                    }
                    else
                    {
                        // Seitenaufprall links: getroffenes Fahrzeug wird nach rechts (+x) beschleunigt,
                        // Δv ≈ halbe Aufprallgeschwindigkeit (gleich schwere Fahrzeuge, plastischer Stoß), Dauer 60 ms
                        const float T = 0.06f;
                        float dv = v / 2;
                        drive = time =>
                        {
                            float u = Mathf.Clamp01((time - t0) / T);
                            float vel = dv * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                            rbCar.MovePosition(rbCar.position + new Vector3(vel * Time.fixedDeltaTime, 0, 0));
                        };
                        return 0.5f;
                    }
                }

                case "motorrad":
                {
                    float seatY = 0.85f;
                    var bike = Vehicle("Motorrad", Vector3.zero);
                    var t = bike.transform;
                    Part(t, "Rumpf", new Vector3(0, 0.45f, 0.1f), new Vector3(0.2f, 0.46f, 1.4f), new Color(0.1f, 0.25f, 0.55f));
                    var dm = RagdollBuilder.Dims(H);
                    Part(t, "Sitzbank", new Vector3(0, seatY - dm.ThighRadius - 0.04f, -0.05f), new Vector3(0.24f, 0.08f, 0.5f), new Color(0.1f, 0.1f, 0.1f), counts: false);
                    // Hindernis: Seite eines querenden PKW
                    float gap = 0.4f;
                    var obstacle = Part(null, "Hindernis (PKW-Seite)", new Vector3(0, 0.6f, 0.8f + gap + 0.25f), new Vector3(3f, 1.2f, 0.5f), new Color(0.55f, 0.1f, 0.1f));
                    _spawned.Add(obstacle);
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, seatY, 0), Quaternion.identity, RagdollPose.Straddling, H, p.Mass_kg, p.Male, invertKneeFlexion, headScale: 1.25f);
                    Vector3 mr = Ragdoll.HandTipRight, ml = Ragdoll.HandTipLeft;
                    Part(t, "Lenker", new Vector3(0, (mr.y + ml.y) / 2 - 0.03f, Mathf.Max(mr.z, ml.z) + 0.03f), new Vector3(Mathf.Abs(mr.x - ml.x) + 0.15f, 0.04f, 0.04f), new Color(0.6f, 0.6f, 0.6f));
                    Ragdoll.SetVelocity(new Vector3(0, 0, v));
                    var rbBike = bike.GetComponent<Rigidbody>();
                    float tHit = gap / Mathf.Max(0.1f, v);
                    const float T = 0.04f;
                    drive = time =>
                    {
                        float u = Mathf.Clamp01((time - tHit) / T);
                        float vel = v - v * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                        rbBike.MovePosition(rbBike.position + new Vector3(0, 0, vel * Time.fixedDeltaTime));
                    };
                    return 1.2f;
                }

                case "fussgaenger":
                {
                    var dp = RagdollBuilder.Dims(H);
                    float hipY = dp.KneeToSole + dp.HipToKnee + 0.01f; // Füße knapp über dem Boden
                    // Fahrzeug kommt von rechts (+x) und fährt nach −x
                    var car = Vehicle("Fahrzeug", new Vector3(0.13f * H + 0.12f, 0, 0));
                    var t = car.transform;
                    var c = new Color(0.55f, 0.1f, 0.1f);
                    Part(t, "Stoßfänger", new Vector3(0.1f, 0.48f, 0), new Vector3(0.2f, 0.22f, 1.7f), c);
                    Part(t, "Haubenvorderkante", new Vector3(0.3f, 0.72f, 0), new Vector3(0.5f, 0.2f, 1.7f), c);
                    Part(t, "Motorhaube", new Vector3(1.0f, 0.82f, 0), new Vector3(1.1f, 0.08f, 1.7f), c, tiltZ: 6);
                    Part(t, "Windschutzscheibe", new Vector3(1.75f, 1.08f, 0), new Vector3(0.7f, 0.05f, 1.5f), new Color(0.6f, 0.75f, 0.85f), tiltZ: 32);
                    Part(t, "Dach", new Vector3(2.6f, 1.35f, 0), new Vector3(1.2f, 0.05f, 1.5f), c);
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, hipY, 0), Quaternion.identity, RagdollPose.Standing, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    var rbCar = car.GetComponent<Rigidbody>();
                    const float brakeAt = 0.25f, decel = 8f;
                    drive = time =>
                    {
                        float vel = time < brakeAt ? v : Mathf.Max(0, v - decel * (time - brakeAt));
                        rbCar.MovePosition(rbCar.position + new Vector3(-vel * Time.fixedDeltaTime, 0, 0));
                    };
                    return 2.0f;
                }

                default: // sturz_hoehe — Speed_kmh bedeutet hier Höhe in m
                {
                    float h = Mathf.Max(1, p.Speed_kmh);
                    float vImpact = Mathf.Sqrt(2 * 9.81f * h);
                    int orientation = rng.Next(3); // 0 Füße voran, 1 Seite, 2 Rücken
                    Quaternion rot = orientation == 0 ? Quaternion.Euler(0, 0, 8)
                                   : orientation == 1 ? Quaternion.Euler(0, 0, 80)
                                   : Quaternion.Euler(-75, 0, 0);
                    var df = RagdollBuilder.Dims(H);
                    float lowest = orientation == 0 ? df.KneeToSole + df.HipToKnee + 0.02f : 0.15f * H; // Hüfthöhe über dem tiefsten Punkt
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, lowest + 0.25f, 0), rot, RagdollPose.Standing, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    Ragdoll.SetVelocity(new Vector3(0, -vImpact, 0));
                    return 1.5f;
                }
            }
        }

        /// <summary>
        /// Fahrerairbag vereinfacht: entfaltet sich in 25 ms kinematisch aus dem Lenkrad Richtung Fahrer und
        /// wird danach zu einem federnd-gedämpften Polster (Gasaustritt ≈ Dämpfung). Werte didaktisch.
        /// </summary>
        Action<float> Airbag(GameObject car, Vector3 wheelLocal, float deployAt)
        {
            const float deployTime = 0.025f, travel = 0.25f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Airbag";
            go.transform.position = car.transform.TransformPoint(wheelLocal);
            go.transform.localScale = new Vector3(0.5f, 0.5f, 0.2f);
            go.GetComponent<Renderer>().material = CrashPhysics.Mat(new Color(0.92f, 0.92f, 0.88f));
            go.AddComponent<CrashSurface>();
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var col = go.GetComponent<Collider>();
            foreach (var c in car.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c);
            _spawned.Add(go);
            var carRb = car.GetComponent<Rigidbody>();
            Vector3 start = wheelLocal;
            bool deployed = false;
            return time =>
            {
                if (deployed || time < deployAt) { if (!deployed) rb.MovePosition(carRb.position + start); return; }
                float u = Mathf.Clamp01((time - deployAt) / deployTime);
                rb.MovePosition(carRb.position + start + new Vector3(0, 0, -travel * u));
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

        /// <summary>Dreipunktgurt vereinfacht: Sternum (Schultergurt) und Becken (Beckengurt) elastisch am Fahrzeug.</summary>
        void Belt(Rigidbody car, float H)
        {
            foreach (var name in new[] { "Sternum", "Becken" })
            {
                var seg = Ragdoll.Segments[name];
                var j = seg.gameObject.AddComponent<ConfigurableJoint>();
                j.connectedBody = car;
                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
                j.linearLimit = new SoftJointLimit { limit = name == "Sternum" ? 0.06f : 0.04f }; // Gurtlose und Dehnung
                // Gurtband: ca. 140 kN/m (Dehnung ~7 % bei 10 kN auf 1 m) — didaktische Größenordnung
                j.linearLimitSpring = new SoftJointLimitSpring { spring = 140000, damper = 3000 };
            }
        }

        // ------------------------------------------------------------------ Ablauf

        /// <summary>Startet den Crash. <paramref name="slowMotion"/> = Zeitlupenfaktor (1 = Echtzeit).</summary>
        public IEnumerator Run(CrashParams p, float slowMotion, Action<List<CrashMeasurement>> done)
        {
            Running = true;
            float duration = Build(p, out Action<float> drive);
            _savedStep = Time.fixedDeltaTime;
            Time.fixedDeltaTime = crashStep_s;
            Time.timeScale = Mathf.Clamp(slowMotion, 0.02f, 1f);
            yield return new WaitForFixedUpdate();
            foreach (var s in Ragdoll.Sensors) s.StartRecording();
            var gauges = gameObject.AddComponent<CrashGauges>();
            gauges.Begin(Ragdoll);
            _t0 = Time.time;
            var driver = gameObject.AddComponent<CrashDriver>();
            driver.Init(drive, _t0);
            var tone = LoadMuscleTone();
            MuscleToneDriver muscles = null;
            if (tone != null)
            {
                muscles = gameObject.AddComponent<MuscleToneDriver>();
                muscles.Begin(Ragdoll, tone, p.Occupant, p.Male, _t0);
            }
            while (Time.time - _t0 < duration) yield return null;
            foreach (var s in Ragdoll.Sensors) s.StopRecording();
            gauges.Stop();
            Destroy(driver);
            if (muscles != null) Destroy(muscles);
            Time.timeScale = 1;
            Time.fixedDeltaTime = _savedStep;
            Running = false;
            var measured = Measure(p, gauges);
            Destroy(gauges);
            done(measured);
        }

        /// <summary>
        /// Wertet die Sensoren aus — gefiltert nach SAE J211 (CFC) wie bei Dummy-Messungen:
        /// HIC15 (Kopf, CFC 1000), Brustwirbelsäule 3 ms (CFC 180), Deflexion und VC (CFC 600),
        /// Nij (Kraft CFC 1000, Moment CFC 600), axiale Femurkraft und Kontaktkräfte (CFC 600).
        /// Rohwerte ohne Kalibrierung — die wendet die Sandbox an.
        /// </summary>
        List<CrashMeasurement> Measure(CrashParams p, CrashGauges g)
        {
            var result = new List<CrashMeasurement>();
            var byRegion = Ragdoll.Sensors.GroupBy(s => s.Region).ToDictionary(x => x.Key, x => x.ToList());
            double dt = Ragdoll.Sensors[0].Dt_s;
            Action<string, double> add = (id, v) => result.Add(new CrashMeasurement { metric = id, value = v });

            var head = byRegion["Kopf"][0];
            var headAcc = Biomechanics.Cfc(head.Accel_g, dt, 1000);
            if (p.MechanismId == "motorrad") for (int i = 0; i < headAcc.Count; i++) headAcc[i] *= helmetHeadFactor;
            add("HIC15", Biomechanics.Hic(headAcc, dt, 0.015));

            var spine = Ragdoll.Segments["Thorax"].GetComponent<CrashSensor>();
            add("Thorax.a3ms", Biomechanics.Clip(Biomechanics.Cfc(spine.Accel_g, dt, 180), dt));

            var defl = Biomechanics.Cfc(g.ChestDeflection_m, dt, 600);
            add("Thorax.D", Biomechanics.MaxAbs(defl) * 1000);
            add("Thorax.VC", Biomechanics.ViscousCriterion(defl, dt, 0.229));
            add("Nij", Biomechanics.Nij(Biomechanics.Cfc(g.NeckFz_N, dt, 1000), Biomechanics.Cfc(g.NeckMy_Nm, dt, 600)));
            add("Oberschenkel rechts.Fax", Biomechanics.MaxAbs(Biomechanics.Cfc(g.FemurAxialR_N, dt, 600)) / 1000);

            foreach (var region in new[] { "Thorax", "Abdomen", "Becken", "Oberschenkel rechts", "Unterschenkel rechts" })
                add(region + ".F", byRegion[region].Max(s => Biomechanics.Clip(Biomechanics.Cfc(s.Force_kN, dt, 600), dt)));

            // Seite des Anpralls am Rumpf: −1 links … +1 rechts
            double lat = 0, tot = 0;
            foreach (var r in new[] { "Thorax", "Abdomen", "Becken" })
                foreach (var s in byRegion[r]) { lat += s.LateralImpulse; tot += s.TotalImpulse; }
            double lateral = tot > 0 ? lat / tot : 0;
            add("Abdomen.lateral", lateral);

            // Organmodell: Kontaktkraft am Bauch → Kompression → Dehnung von Leber, Milz, Nieren gegen ihre Versagensdehnung
            var organs = LoadOrganModel();
            var tissues = Anatomy.AnatomyPatient.Tissues;
            if (organs != null && tissues != null)
            {
                var abd = byRegion["Abdomen"];
                var force_N = new List<double>();
                for (int i = 0; i < abd[0].Force_kN.Count; i++) force_N.Add(abd.Sum(s => i < s.Force_kN.Count ? s.Force_kN[i] : 0) * 1000);
                LastAbdomenLoad = OrganMechanics.Evaluate(organs, tissues, Biomechanics.Cfc(force_N, dt, 600), dt, lateral, p.Height_m);
                result.AddRange(OrganMechanics.Measurements(LastAbdomenLoad));
            }
            return result;
        }

        /// <summary>Organbelastung des letzten Crashs (für die Ergebnisanzeige); null ohne organ_model.json/tissues.json.</summary>
        public AbdomenLoad LastAbdomenLoad { get; private set; }

        static MuscleToneFile _muscleTone;
        static MuscleToneFile LoadMuscleTone()
        {
            if (_muscleTone != null) return _muscleTone;
            string path = System.IO.Path.Combine(Data.InjuryDatabase.Root, "muscle_tone.json");
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[Crash] muscle_tone.json fehlt — Ragdoll ohne Muskeltonus."); return null; }
            return _muscleTone = JsonUtility.FromJson<MuscleToneFile>(System.IO.File.ReadAllText(path));
        }

        static OrganModelFile _organModel;
        static OrganModelFile LoadOrganModel()
        {
            if (_organModel != null) return _organModel;
            string path = System.IO.Path.Combine(Data.InjuryDatabase.Root, "organ_model.json");
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[Crash] organ_model.json fehlt — Milz/Leber ohne Organmodell."); return null; }
            return _organModel = JsonUtility.FromJson<OrganModelFile>(System.IO.File.ReadAllText(path));
        }

        void OnDisable()
        {
            if (Running) { Time.timeScale = 1; Time.fixedDeltaTime = _savedStep > 0 ? _savedStep : 0.02f; }
        }
    }

    /// <summary>
    /// Muskeltonus während des Crashs: Gelenkantriebe halten die Ausgangshaltung mit einem Moment bis
    /// Maximalmoment × Aktivierung (MuscleTone). Der passive Tonus aus dem RagdollBuilder bleibt erhalten.
    /// </summary>
    public class MuscleToneDriver : MonoBehaviour
    {
        MuscleToneFile _f;
        OccupantState _state;
        bool _male;
        float _t0;
        class Driven { public string Segment; public ConfigurableJoint Joint; public float Passive; }
        readonly List<Driven> _joints = new List<Driven>();

        public void Begin(Ragdoll rd, MuscleToneFile f, OccupantState state, bool male, float t0)
        {
            _f = f; _state = state; _male = male; _t0 = t0;
            foreach (var kv in rd.Segments)
            {
                var j = kv.Value.GetComponent<ConfigurableJoint>();
                if (j == null || f.joints.All(x => x.segment != kv.Key)) continue;
                _joints.Add(new Driven { Segment = kv.Key, Joint = j, Passive = j.slerpDrive.positionSpring });
            }
            Apply();
        }

        void FixedUpdate() => Apply();

        void Apply()
        {
            double a = MuscleTone.Activation(_f.activation, _state, Time.time - _t0);
            foreach (var d in _joints)
            {
                var drive = MuscleTone.Drive(_f, d.Segment, _male, a);
                double tmax = drive.Item1, k = drive.Item2;
                float passive = d.Passive;
                float spring = passive + (float)k;
                d.Joint.slerpDrive = new JointDrive
                {
                    positionSpring = spring,
                    positionDamper = 0.1f * spring,
                    // aktiv: Moment begrenzt (Muskelkraft sättigt); passiv wie bisher unbegrenzt
                    maximumForce = a > 0 ? passive + (float)tmax : float.MaxValue
                };
            }
        }
    }

    /// <summary>Bewegt das (kinematische) Fahrzeug in jedem Physikschritt nach dem Crashpuls.</summary>
    public class CrashDriver : MonoBehaviour
    {
        Action<float> _drive;
        float _t0;
        public void Init(Action<float> drive, float t0) { _drive = drive; _t0 = t0; }
        void FixedUpdate() { _drive?.Invoke(Time.time - _t0); }
    }
}

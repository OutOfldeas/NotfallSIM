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
        /// <summary>Fahrzeuge als verformbares Gerüst (SoftBody/SoftImpact): frontal und seitlich rechnet das Gerüst den Stoß,
        /// in den übrigen Modi formt es die Delle, deren Tiefe das jeweilige Stoßmodell liefert.</summary>
        public bool SoftVehicle = true;   // wieder Standard: HIC 236 statt > 20 000, seit Gelenkanschläge, Airbag und Gurt nachgeben (Unity 14.11.2026)
        public string MotoConfig = "413-6,7/13,4";  // ISO-13232-Anprallkonfiguration bzw. "alleinsturz" (vehicle_model.json › motorcycleConfigs)
        public int Seed = 1;
        public OccupantState Occupant = OccupantState.Entspannt;   // Muskeltonus (muscle_tone.json)
        public bool SoftContact = true;           // nachgiebiger Kontakt am Körper (SoftContact, impact_model.json) statt starrer Kollision
        /// <summary>Frei gestellter Unfall (MechanismId "szenario"): Fahrzeuge, Insassen, Fußgänger, Hindernisse.</summary>
        public TrafficScenario Traffic;
        /// <summary>Welche Person des Szenarios Patient wird (Index in <see cref="CrashRig.Humans"/>).</summary>
        public int Patient;
    }

    /// <summary>
    /// Vorlauf vor dem Aufprall: Fahrzeuge fahren einige Sekunden (Insasse bzw. stehender Fußgänger wird bis zum
    /// Aufprall mitgeführt bzw. gehalten), beim Sturz fällt der Körper frei aus der eingestellten Höhe (mit Luftwiderstand).
    /// Gerechnet mit grobem Schritt (1 ms) ohne Messung; der Aufprall danach wie bisher mit 0,1 ms.
    /// </summary>
    public class CrashApproach
    {
        public float Duration_s;                                   // Fahrzeuge: Dauer des Vorlaufs
        public readonly List<(Rigidbody rb, Vector3 v)> Carry = new List<(Rigidbody, Vector3)>();  // kinematisch bewegt
        public bool HoldRagdoll;                                   // Ragdoll kinematisch mitführen, am Ende freigeben
        public Vector3 RagdollVelocity;                            // Geschwindigkeit des Ragdolls im Vorlauf und bei Freigabe
        public bool Fall;                                          // freier Fall bis FallReleaseY (Becken)
        public float FallReleaseY, TerminalVelocity_m_s = 55;
    }

    /// <summary>
    /// Baut eine Unfallsituation (Fahrzeug bzw. Untergrund + Ragdoll), lässt sie mit feinem Zeitschritt
    /// laufen und liefert die Messwerte für <see cref="InjuryRisk"/>.
    ///
    /// Vereinfachungen (didaktisch, siehe docs/LITERATUR.md):
    /// - PKW frontal als Schlittentest: das Fahrzeug folgt einem Halbsinus-Puls (Dauer aus Masse und Frontsteifigkeit);
    ///   über die Knautschzone hinaus drücken Armaturenbrett und Lenkrad ein (vehicle_model.json › frontal).
    /// - PKW seitlich: Feder-Masse-Modell (<see cref="SideImpactState"/>), die Fahrertür dringt ein und bleibt eingedrückt.
    /// - Fußgänger: Fahrzeugfront mit konstanter Geschwindigkeit, danach Bremsung; Frontteile nachgiebig mit Federweg.
    /// - Sturz: freier Fall aus der Höhe mit Luftwiderstand (Endgeschwindigkeit 55 bzw. 75 m/s, Füße voran).
    /// - Vorlauf: Fahrzeuge fahren 3 s; Insasse und Fußgänger werden dabei kinematisch gehalten (keine Haltungsänderung).
    /// - Motorrad: Maschine stoppt am Hindernis, Fahrer fliegt weiter. Helm dämpft die Kopfbeschleunigung pauschal.
    /// </summary>
    public partial class CrashRig : MonoBehaviour
    {
        [Tooltip("Physikschritt während des Aufpralls (s). Crashtests messen mit ≥ 10 kHz; 0,5 ms ist ein Kompromiss.")]
        [SerializeField] float crashStep_s = 0.0001f; // 10 kHz: nötig für den Kopffilter CFC 1000 (SAE J211)
        [Tooltip("VERIFY: umschalten, falls die Knie nach hinten durchknicken.")]
        [SerializeField] bool invertKneeFlexion = false;
        [Tooltip("Pauschale Dämpfung der Kopfbeschleunigung durch den Helm (didaktische Setzung).")]
        [SerializeField] float helmetHeadFactor = 0.6f;

        public Ragdoll Ragdoll { get; private set; }
        public bool Running { get; private set; }
        /// <summary>Zeit im berechneten Crash (s) — während der Berechnung bzw. der Wiedergabe.</summary>
        public float SimTime => Playing ? PlaybackTime : _simTime;
        public Transform CameraTarget => Ragdoll?.Pelvis.transform;
        /// <summary>Fahrtrichtung eines Fahrzeugs, das auf die Person zufährt (Fußgänger, PKW trifft Motorrad) — die Kamera stellt sich seitlich dazu; sonst null.</summary>
        public Vector3 OpponentTravel { get; private set; }

        readonly List<GameObject> _spawned = new List<GameObject>();
        /// <summary>Fahrzeugteile, die beim nachgiebigen Kontakt als Gegenstand mit eigener Steifigkeit angemeldet werden.</summary>
        readonly List<GameObject> _softParts = new List<GameObject>();

        /// <summary>Nachgiebige Fahrzeugteile anmelden (nach <see cref="RegisterSoftBody"/>, das die Anmeldungen leert). Anzahl.</summary>
        /// <summary>
        /// Der Airbag stützt den Brustkorb flächig; das Sternum ist hier ein eigener leichter Körper für die Brustkorbdeflexion
        /// (Gurt, Lenkrad). Träfe der Airbag ihn allein, drückte er ihn bis zum Anschlag ein (90 mm, 1 700 g am Sternum).
        /// </summary>
        void AirbagSparesSternum()
        {
            var bags = _spawned.Where(g => g != null && g.name == "Airbag").SelectMany(g => g.GetComponentsInChildren<Collider>()).ToList();
            if (bags.Count == 0) return;
            foreach (var rd in AllRagdolls())
            {
                if (rd == null || rd.Sternum == null) continue;
                foreach (var sc in rd.Sternum.GetComponentsInChildren<Collider>())
                    foreach (var b in bags) Physics.IgnoreCollision(b, sc);
            }
        }

        int RegisterSoftVehicleParts()
        {
            var vm = LoadVehicleModel();
            if (vm == null) return 0;
            // Fußgängerfront (nur die dafür vorgesehenen Teile) und Innenraum/Außenhaut (alle Fahrzeugteile nach Namen):
            // ohne Anmeldung wäre jedes Teil starr — ein Kopfanprall an Tür, Dach oder Lenkrad ergäbe HIC > 20 000
            var done = new HashSet<Collider>();
            int n = 0;
            Action<Collider, VehiclePart> reg = (col, part) =>
            {
                if (col == null || part == null || col.isTrigger || !done.Add(col)) return;
                var def = new SandboxObject
                {
                    id = "fahrzeug:" + part.part, label = part.part, kind = "stumpf", mode = "statisch", shape = "box",
                    stiffness_N_m = part.stiffness_N_m, clearance_m = part.clearance_m, sourceId = part.sourceId
                };
                // Fahrzeug ist kinematisch: unendlich schwer (keine Masse im Kontakt), Kontaktfläche aus der Geometrie
                SoftContact.RegisterObject(col, def, null, 0, col.transform.lossyScale, 0);
                n++;
            };
            if (vm.pedestrianFront != null)
                foreach (var go in _softParts)
                    if (go != null) reg(go.GetComponent<Collider>(), Array.Find(vm.pedestrianFront, x => x.part == go.name));
            if (vm.interior != null)
            {
                var bodies = new HashSet<Transform>(AllRagdolls().Where(r => r != null && r.Root != null).Select(r => r.Root.transform));
                foreach (var go in _spawned)
                {
                    if (go == null || bodies.Contains(go.transform)) continue;
                    foreach (var col in go.GetComponentsInChildren<Collider>())
                    {
                        if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;   // nur Fahrzeugteile (kinematisch)
                        string name = col.name;
                        var part = Array.Find(vm.interior, x => x.part == name)
                                   ?? vm.interior.Where(x => name.StartsWith(x.part)).OrderByDescending(x => x.part.Length).FirstOrDefault();
                        // Frontscheibe von innen wie von außen
                        if (part == null && name == "Windschutzscheibe" && vm.pedestrianFront != null) part = Array.Find(vm.pedestrianFront, x => x.part == name);
                        reg(col, part);
                    }
                }
            }
            return n;
        }

        // ------------------------------------------------------------------ Aufbau

        public void Clear()
        {
            _softParts.Clear();
            _pedVisual = null;
            OpponentTravel = Vector3.zero;
            _visuals.Clear();
            VehicleReport = null;
            // Sofort deaktivieren: Destroy greift erst am Ende des Frames, die neuen Teile entstehen am selben Ort.
            foreach (var go in _spawned) if (go) { go.SetActive(false); Destroy(go); }
            _spawned.Clear();
            if (Ragdoll != null && Ragdoll.Root) { Ragdoll.Root.SetActive(false); Destroy(Ragdoll.Root); }
            foreach (var h in Humans) if (h.Rd != null && h.Rd.Root) { h.Rd.Root.SetActive(false); Destroy(h.Rd.Root); }
            Humans.Clear();
            _trafficVehicles.Clear();
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

        [Tooltip("Vorlauf der Fahrzeuge vor dem Aufprall (s).")]
        [SerializeField] float approachTime_s = 3f;
        [Tooltip("Physikschritt im Vorlauf (s) — grob, ohne Messung.")]
        [SerializeField] float approachStep_s = 0.001f;

        /// <summary>Erzeugte Fahrzeugmodelle (sichtbar, verformbar; Verformung wird mit aufgezeichnet).</summary>
        readonly List<VehicleVisual> _visuals = new List<VehicleVisual>();

        /// <summary>Verformbares Gerüst für alle Fahrzeuge dieses Aufbaus (<see cref="CrashParams.SoftVehicle"/>).</summary>
        bool _softVehicles;

        VehicleVisual CarModel(Transform parent, CarProfile p, Color paint, Vector3 localPos, float yaw, Func<DeformState> source = null, double mass_kg = 1500)
        {
            var v = VehicleVisual.Car(parent, p, paint, localPos, Quaternion.Euler(0, yaw, 0));
            v.Source = source;
            // Gerüst statt berechneter Form: die Tiefe kommt weiter aus dem Stoßmodell (source), die Form aus Punkten und Balken
            if (_softVehicles && source != null) AttachDent(v, mass_kg);
            _visuals.Add(v);
            return v;
        }

        // ---- Fußgänger: Dellen in Stoßfänger und Haube aus der größten Kontaktkraft je Körperteil (Kraft / Steifigkeit, höchstens
        //      der Federweg des Teils, vehicle_model.json › pedestrianFront), Ort = Lage des Körperteils in diesem Moment
        VehicleVisual _pedVisual;
        float _pedBumper, _pedBumperX;
        readonly Dictionary<string, float> _pedTop = new Dictionary<string, float>();

        void StepPedestrianDents()
        {
            if (_pedVisual == null || _pedVisual.Dent == null || Ragdoll == null || _pedVisual.Go == null) return;
            var vm = LoadVehicleModel();
            if (vm == null || vm.pedestrianFront == null) return;
            var tr = _pedVisual.Go.transform;
            int id = 0;
            foreach (var kv in Ragdoll.Segments)
            {
                id++;
                var col = kv.Value.GetComponent<Collider>();
                if (col == null) continue;
                foreach (var part in vm.pedestrianFront)
                {
                    bool bumper = part.part == "Stoßfänger", hood = part.part == "Motorhaube" || part.part == "Haubenvorderkante";
                    if (!bumper && !hood) continue;
                    float f = SoftContact.PeakWith(col, "fahrzeug:" + part.part).Force_N;
                    if (f <= 0) continue;
                    float depth = Mathf.Min((float)part.clearance_m, f / (float)part.stiffness_N_m);
                    var local = tr.InverseTransformPoint(kv.Value.worldCenterOfMass);
                    if (bumper)
                    {
                        if (depth > _pedBumper) { _pedBumper = depth; _pedBumperX = local.x; }
                    }
                    else
                    {
                        string key = kv.Key + part.part;
                        float old; _pedTop.TryGetValue(key, out old);
                        if (depth <= old + 0.002f) continue;
                        _pedTop[key] = depth;
                        // Kopf drückt kleiner und tiefer ein als Rumpf oder Becken
                        _pedVisual.Dent.SetTop(id * 4 + (part.part == "Motorhaube" ? 0 : 1), local.x, local.z, kv.Key == "Kopf" ? 0.12 : 0.22, depth);
                    }
                }
            }
        }

        SoftDent AttachDent(VehicleVisual v, double mass_kg = 1500)
        {
            var car = SoftCar.Build(v.Profile, mass_kg);
            v.SoftSkin = new SoftSkin(v.Shape, car.Body);
            v.Dent = new SoftDent(car);
            return v.Dent;
        }

        /// <summary>Normaler PKW-Umriss (Fußgänger-Front, Unfallgegner), Länge L, Front bei z = L/2.</summary>
        static CarProfile SedanProfile(float L = 4.4f, float W = 1.75f, bool wheels = true) =>
            CarProfile.Make(L, W, 2.65f, 0.25f, 0.95f, 0.95f, -L / 2 + 0.7f, 1.0f, -1.0f, 0.15f, 1.38f, L / 2 - 1.45f, 0.92f, 0.84f, wheels);

        /// <summary>
        /// PKW-Umriss um den Innenraum eines Insassen (Sitzhöhe des Ragdolls), Koordinaten wie das Fahrzeug:
        /// vordere Hüfte bei zFront, Boden bei floorY, Armaturenbrett bei dashZ. Die Mitte der Länge liegt bei <paramref name="centerZ"/>.
        /// </summary>
        static CarProfile CabinProfile(float L, float W, float wheelbase, float floorY, float hipY, float zFront, float dashZ, float centerZ, bool rearRow, bool wheels)
        {
            float belt = hipY + 0.15f, roof = hipY + 1.05f;
            Func<float, float> z = v => v - centerZ;   // Fahrzeug- → Modellkoordinaten
            return CarProfile.Make(L, W, wheelbase, Mathf.Max(0.02f, floorY - 0.03f), belt, belt - 0.05f,
                z(rearRow ? zFront - 1.6f : zFront - 1.05f), belt + 0.05f, z(rearRow ? zFront - 1.05f : zFront - 0.5f), z(zFront + 0.36f), roof,
                z(dashZ + 0.14f), hipY + 0.42f, Mathf.Min(0.95f, hipY + 0.05f), wheels);
        }

        /// <summary>Nur sichtbares Teil (ohne Kollider), z. B. Fahrzeugfront, Barriere, Unfallgegner.</summary>
        GameObject Visual(Transform parent, string name, Vector3 localPos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.GetComponent<Collider>().enabled = false;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().material = CrashPhysics.Mat(color);
            if (parent == null) _spawned.Add(go);
            return go;
        }

        /// <summary>Körper (und ihre Kinder) vor dem Start verschieben.</summary>
        static void Shift(IEnumerable<Rigidbody> bodies, Vector3 offset)
        {
            foreach (var rb in bodies) rb.transform.position += offset;
        }

        /// <summary>Fahrzeug samt Insasse fährt im Vorlauf mit v auf den Aufprallort zu; die Szene startet um v·t zurück.</summary>
        void Drive(CrashApproach a, Vector3 v, bool withRagdoll)
        {
            var bodies = _spawned.Where(g => g != null).Select(g => g.GetComponent<Rigidbody>()).Where(rb => rb != null && rb.isKinematic).ToList();
            foreach (var rb in bodies) a.Carry.Add((rb, v));
            a.RagdollVelocity = v;
            Shift(bodies, -v * approachTime_s);
            if (withRagdoll) Ragdoll.Root.transform.position += -v * approachTime_s;
        }

        void Ground()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Boden";
            g.transform.position = new Vector3(0, -0.25f, 0);
            g.transform.localScale = new Vector3(1000, 0.5f, 1000);   // Anfahrt bis 100 m
            g.AddComponent<CrashSurface>().kind = "ground";
            g.GetComponent<Renderer>().material = CrashPhysics.Mat(new Color(0.32f, 0.34f, 0.36f));
            _spawned.Add(g);
        }

        /// <summary>Baut die Szene für die gewählten Parameter und gibt die Dauer der Simulation zurück.</summary>
        public float Build(CrashParams p, out Action<float, float> drive) => Build(p, out drive, out _);

        /// <summary>Wie oben, dazu der Vorlauf (Anfahrt bzw. freier Fall). Die Szene steht danach am Anfang des Vorlaufs.</summary>
        public float Build(CrashParams p, out Action<float, float> drive, out CrashApproach approach)
        {
            Clear();
            _softVehicles = p.SoftVehicle;
            Ground();
            float H = p.Height_m;
            float v = p.Speed_kmh / 3.6f;
            drive = null;
            approach = new CrashApproach { Duration_s = approachTime_s, HoldRagdoll = true };
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
                    // Armaturenbrett, Lenkrad und Fahrertür sind eigene (kinematische) Körper: sie können eindrücken
                    var dash = Vehicle("Armaturenbrett", Vector3.zero);
                    Part(dash.transform, "Armaturenbrett", new Vector3(0, hipY + 0.08f, d.HipToKnee + 0.2f), new Vector3(1.3f, 0.3f, 0.16f), new Color(0.25f, 0.25f, 0.28f));
                    Part(t, "Windschutzscheibe", new Vector3(0, hipY + 0.82f, 0.62f), new Vector3(1.4f, 0.04f, 0.9f), Color.clear, visible: false, tiltX: -55);
                    Part(t, "Dach", new Vector3(0, hipY + 1.0f, -0.1f), new Vector3(1.4f, 0.05f, 1.4f), Color.clear, visible: false);
                    var door = Vehicle("Fahrertür", Vector3.zero);
                    Part(door.transform, "Fahrertür", new Vector3(-0.42f, hipY + 0.15f, 0.1f), new Vector3(0.06f, 0.9f, 1.3f), new Color(0.55f, 0.1f, 0.1f), visible: false);
                    // Karosserie um den Innenraum (Front bei z = 2,1 wie bisher, linke Seite an der Fahrertür); Verformung siehe unten
                    const float sledL = 4.6f, sledW = 1.8f, sledCenterZ = 2.1f - sledL / 2, sledCenterX = -0.45f + sledW / 2;
                    var sledProfile = CabinProfile(sledL, sledW, 2.75f, 0f, hipY, 0f, d.HipToKnee + 0.2f, sledCenterZ, false, false);
                    float sledFront = 0, sledLeft = 0;
                    var sledVisual = CarModel(t, sledProfile, new Color(0.2f, 0.32f, 0.55f), new Vector3(sledCenterX, 0, sledCenterZ), 0,
                             () => new DeformState { Front = sledFront, FrontPos = 0, Left = sledLeft, LeftPos = 0.1f - sledCenterZ });
                    Part(t, "Mittelkonsole", new Vector3(0.38f, hipY - 0.05f, 0.2f), new Vector3(0.12f, 0.3f, 0.8f), new Color(0.2f, 0.2f, 0.22f), visible: false);
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, hipY, 0), Quaternion.identity, RagdollPose.Seated, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    // Lenkrad dort, wo die Hände sind
                    Vector3 hr = Ragdoll.HandTipRight, hl = Ragdoll.HandTipLeft;
                    var wheel = new Vector3(0, (hr.y + hl.y) / 2, Mathf.Max(hr.z, hl.z) - 0.02f);
                    float wheelD = Mathf.Clamp(Mathf.Abs(hr.x - hl.x) - 0.08f, 0.32f, 0.42f);
                    var wheelBody = Vehicle("Lenkrad", Vector3.zero);
                    Part(wheelBody.transform, "Lenkrad", wheel, new Vector3(wheelD, wheelD, 0.05f), new Color(0.1f, 0.1f, 0.1f), tiltX: -20);
                    var rbDash = dash.GetComponent<Rigidbody>(); var rbWheel = wheelBody.GetComponent<Rigidbody>(); var rbDoor = door.GetComponent<Rigidbody>();
                    var vm = LoadVehicleModel();
                    if (p.Belted) Belt(car.GetComponent<Rigidbody>(), H);

                    var rbCar = car.GetComponent<Rigidbody>();
                    float t0 = 0.02f;
                    if (frontal)
                    {
                        // Halbsinus-Verzögerung (linearer Federstoß gegen die Barriere): Dauer π·√(m/k), Δv = v
                        float T = vm != null ? (float)FrontalImpact.Pulse_s(vm.frontal) : 0.1f;
                        float crush = 0;
                        // Verformbares Fahrzeug (Punkte und Balken, SoftBody.cs): Bewegung der Fahrgastzelle und Eindringen der
                        // Spritzwand kommen aus der mitgerechneten Struktur statt aus dem vorgegebenen Halbsinus
                        SoftCrashRun soft = null;
                        if (p.SoftVehicle)
                        {
                            var sc = SoftCar.Build(sledProfile, vm != null ? vm.frontal.mass_kg : 1500);
                            sc.SetSeat(-sledCenterZ);
                            soft = new SoftCrashRun(sc, v);
                            sledVisual.Dent = null;
                            sledVisual.SoftSkin = new SoftSkin(sledVisual.Shape, sc.Body);
                            sledVisual.SoftSource = soft.Snapshot;
                            var run = soft;
                            VehicleReport = () => $"Fahrzeugstruktur (verformbar, {sc.Body.Count} Punkte, {sc.Body.Beams} Balken): Knautschweg {run.MaxCrush * 100:0} cm, " +
                                                  $"Verzögerung der Zelle bis {run.PeakDecel_g:0} g (10-ms-Mittel), Spritzwand {run.MaxIntrusion * 100:0.0} cm eingedrungen";
                        }
                        float softVel = v;
                        Action<float> airbag = p.Airbag ? Airbag(car, wheel, t0 + 0.015f) : null;
                        // Betonbarriere (ohne Kollider: der Puls ersetzt die Verformung); die Front knautscht sichtbar mit
                        Visual(null, "Barriere", new Vector3(0, 0.75f, 2.1f + v * t0 + 1f), new Vector3(3.5f, 1.5f, 2f), new Color(0.55f, 0.55f, 0.52f));
                        Drive(approach, new Vector3(0, 0, v), true);
                        drive = (time, dt) =>
                        {
                            float u = Mathf.Clamp01((time - t0) / T);
                            float vel = v - v * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                            float intr;
                            if (soft != null)
                            {
                                if (time < t0) vel = v;
                                else if (soft.Time < 0.25)
                                {
                                    soft.Advance(dt);
                                    softVel = vel = (float)soft.Speed;
                                }
                                else vel = softVel = Mathf.MoveTowards(softVel, 0, 6f * dt);   // Rückprall rollt mit 6 m/s² aus (Annahme)
                                crush = (float)soft.Crush;
                                intr = (float)soft.Intrusion;
                            }
                            else
                            {
                                if (time >= t0) crush += vel * dt;   // Knautschweg = Weg des Fahrzeugs nach dem Kontakt (endet bei v·T/2)
                                // Über die Knautschzone hinaus drücken Armaturenbrett/Fußraum und Lenkrad nach hinten (−z)
                                intr = vm != null ? (float)FrontalImpact.Intrusion_m(vm.frontal, crush) : 0;
                            }
                            sledFront = crush;
                            var carPos = rbCar.position + new Vector3(0, 0, vel * dt);
                            rbCar.MovePosition(carPos);
                            rbDash.MovePosition(carPos + new Vector3(0, 0, -intr));
                            rbWheel.MovePosition(carPos + new Vector3(0, 0, -intr * 0.6f));
                            rbDoor.MovePosition(carPos);
                            airbag?.Invoke(time);
                        };
                        return 0.6f;
                    }
                    else
                    {
                        // Seitenaufprall links: getroffenes Fahrzeug wird nach rechts (+x) beschleunigt.
                        // Ohne vehicle_model.json: Halbsinus, Δv = halbe Aufprallgeschwindigkeit, Dauer 60 ms.
                        // Das stoßende Fahrzeug (nur sichtbar) kommt von links und trifft bei t0 die Fahrertür.
                        const float T = 0.06f;
                        float dv = v / 2;
                        const float doorX = -0.45f, bulletLen = 4.4f;
                        var bullet = Vehicle("Unfallgegner", new Vector3(doorX - v * t0 - bulletLen / 2, 0, 0.1f));
                        float bulletCrush = 0;
                        // Unfallgegner als PKW, fährt nach +x (Modell um 90° gedreht); seine Front knautscht um ihren Anteil
                        var bulletVisual = CarModel(bullet.transform, SedanProfile(bulletLen), new Color(0.25f, 0.45f, 0.3f), Vector3.zero, 90,
                                 () => new DeformState { Front = bulletCrush });
                        var rbBullet = bullet.GetComponent<Rigidbody>();
                        approach.Carry.Add((rbBullet, new Vector3(v, 0, 0)));
                        approach.RagdollVelocity = Vector3.zero;   // Fahrzeug steht (Kreuzung)
                        Shift(new[] { rbBullet }, new Vector3(-v * approachTime_s, 0, 0));
                        if (vm != null && p.SoftVehicle)
                        {
                            // Beide Fahrzeuge als Gerüst (SoftImpact.cs › SoftSideRun): Front des Gegners gegen die Seite; Bewegung
                            // des getroffenen Fahrzeugs und Eindringen der Tür am Sitz kommen aus der gerechneten Struktur
                            var struckCar = SoftCar.Build(sledProfile, vm.side.struckMass_kg);
                            var bulletCar = SoftCar.Build(bulletVisual.Profile, vm.side.bulletMass_kg);
                            var run = new SoftSideRun(struckCar, bulletCar, v, 0.1f - sledCenterZ, -sledCenterZ, true);
                            sledVisual.Dent = null; bulletVisual.Dent = null;
                            sledVisual.SoftSkin = new SoftSkin(sledVisual.Shape, struckCar.Body);
                            sledVisual.SoftSource = run.StruckSnapshot;
                            bulletVisual.SoftSkin = new SoftSkin(bulletVisual.Shape, bulletCar.Body);
                            bulletVisual.SoftSource = run.BulletSnapshot;
                            VehicleReport = () => $"Fahrzeugstruktur (verformbar, je {struckCar.Body.Count} Punkte): Tür am Sitz {run.MaxDoor * 100:0} cm eingedrungen, " +
                                                  $"Front des Gegners {run.BulletCrush * 100:0} cm, Δv getroffen {run.StruckVelocity * 3.6:0} km/h, Spitze {run.PeakStruckAccel_g:0} g (10-ms-Mittel)";
                            double vS = 0, vB = v;
                            drive = (time, dt) =>
                            {
                                if (time >= t0)
                                {
                                    if (run.Time < 0.25) { run.Advance(dt); vS = run.StruckVelocity; vB = run.BulletVelocity; }
                                    else { vS = Mathf.MoveTowards((float)vS, 0, 6f * dt); vB = Mathf.MoveTowards((float)vB, 0, 6f * dt); }   // ausrollen (Annahme)
                                }
                                double doorIn = run.Door;
                                sledLeft = (float)doorIn;
                                bulletCrush = (float)run.BulletCrush;
                                var carPos = rbCar.position + new Vector3((float)(vS * dt), 0, 0);
                                rbCar.MovePosition(carPos);
                                rbBullet.MovePosition(rbBullet.position + new Vector3((float)((time < t0 ? v : vB) * dt), 0, 0));
                                rbDoor.MovePosition(carPos + new Vector3((float)doorIn, 0, 0));   // Tür nach innen (+x)
                                rbDash.MovePosition(carPos); rbWheel.MovePosition(carPos);
                            };
                        }
                        else if (vm != null)
                        {
                            // Feder-Masse-Modell (vehicle_model.json › side): Front des Gegners und Seitenstruktur in Reihe, plastisch;
                            // die Tür dringt um den Anteil der Seite am Knautschweg ein und bleibt eingedrückt.
                            var side = new SideImpactState(vm.side, v);
                            drive = (time, dt) =>
                            {
                                if (time >= t0) side.Step(dt);
                                double vS = side.StruckVelocity, vB = side.BulletVelocity, doorIn = side.Door;
                                sledLeft = (float)doorIn;
                                bulletCrush = (float)(side.MaxCrush * (1 - side.SideShare));
                                var carPos = rbCar.position + new Vector3((float)(vS * dt), 0, 0);
                                rbCar.MovePosition(carPos);
                                rbBullet.MovePosition(rbBullet.position + new Vector3((float)((time < t0 ? v : vB) * dt), 0, 0));
                                rbDoor.MovePosition(carPos + new Vector3((float)doorIn, 0, 0));   // Tür nach innen (+x)
                                rbDash.MovePosition(carPos); rbWheel.MovePosition(carPos);
                            };
                        }
                        else
                        drive = (time, dt) =>
                        {
                            float u = Mathf.Clamp01((time - t0) / T);
                            float vel = dv * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                            var carPos = rbCar.position + new Vector3(vel * dt, 0, 0);
                            rbCar.MovePosition(carPos);
                            float vb = time < t0 ? v : v - (v - dv) * (1 - Mathf.Cos(Mathf.PI * u)) / 2;
                            rbBullet.MovePosition(rbBullet.position + new Vector3(vb * dt, 0, 0));
                            rbDoor.MovePosition(carPos); rbDash.MovePosition(carPos); rbWheel.MovePosition(carPos);
                        };
                        return 0.5f;
                    }
                }

                case "motorrad":
                    return BuildMotorcycle(p, approach, out drive);

                case "fussgaenger":
                {
                    var dp = RagdollBuilder.Dims(H);
                    float hipY = dp.KneeToSole + dp.HipToKnee + 0.01f; // Füße knapp über dem Boden
                    // Fahrzeug kommt von rechts (+x) und fährt nach −x
                    var car = Vehicle("Fahrzeug", new Vector3(0.13f * H + 0.12f, 0, 0));
                    var t = car.transform;
                    var c = new Color(0.55f, 0.1f, 0.1f);
                    // Nachgiebige Front (vehicle_model.json › pedestrianFront): beim nachgiebigen Kontakt mit Steifigkeit und Federweg
                    _softParts.Add(Part(t, "Stoßfänger", new Vector3(0.1f, 0.48f, 0), new Vector3(0.2f, 0.22f, 1.7f), c, visible: false));
                    _softParts.Add(Part(t, "Haubenvorderkante", new Vector3(0.3f, 0.72f, 0), new Vector3(0.5f, 0.2f, 1.7f), c, visible: false));
                    _softParts.Add(Part(t, "Motorhaube", new Vector3(1.0f, 0.82f, 0), new Vector3(1.1f, 0.08f, 1.7f), c, tiltZ: 6, visible: false));
                    _softParts.Add(Part(t, "Windschutzscheibe", new Vector3(1.75f, 1.08f, 0), new Vector3(0.7f, 0.05f, 1.5f), new Color(0.6f, 0.75f, 0.85f), tiltZ: 32, visible: false));
                    Part(t, "Dach", new Vector3(2.6f, 1.35f, 0), new Vector3(1.2f, 0.05f, 1.5f), c, visible: false);
                    // Sichtbares Modell: fährt nach −x (um −90° gedreht), Front bei x = 0
                    // Dellen: Stoßfänger (Beine) als Front, Haube von oben — Tiefe aus Kontaktkraft / Steifigkeit des Teils
                    _pedBumper = 0; _pedBumperX = 0; _pedTop.Clear();
                    _pedVisual = CarModel(t, SedanProfile(), c, new Vector3(2.2f, 0, 0), -90,
                                          () => new DeformState { Front = _pedBumper, FrontPos = _pedBumperX });
                    if (_pedVisual.Dent != null) _pedVisual.Dent.EndWidth = 0.6;
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, hipY, 0), Quaternion.identity, RagdollPose.Standing, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    var rbCar = car.GetComponent<Rigidbody>();
                    // Fahrzeug fährt einige Sekunden auf den stehenden Fußgänger zu (der bis kurz vorher gehalten wird)
                    OpponentTravel = Vector3.left;
                    approach.Carry.Add((rbCar, new Vector3(-v, 0, 0)));
                    approach.RagdollVelocity = Vector3.zero;
                    Shift(new[] { rbCar }, new Vector3(v * approachTime_s, 0, 0));
                    const float brakeAt = 0.25f, decel = 8f;
                    drive = (time, dt) =>
                    {
                        float vel = time < brakeAt ? v : Mathf.Max(0, v - decel * (time - brakeAt));
                        rbCar.MovePosition(rbCar.position + new Vector3(-vel * dt, 0, 0));
                    };
                    return 2.0f;
                }

                case "szenario":
                    return BuildTraffic(p, out approach);

                default: // sturz_hoehe — Speed_kmh bedeutet hier Höhe in m (tiefster Körperpunkt über dem Boden)
                {
                    float h = Mathf.Max(1, p.Speed_kmh);
                    int orientation = rng.Next(3); // 0 Füße voran, 1 Seite, 2 Rücken
                    Quaternion rot = orientation == 0 ? Quaternion.Euler(0, 0, 8)
                                   : orientation == 1 ? Quaternion.Euler(0, 0, 80)
                                   : Quaternion.Euler(-75, 0, 0);
                    var df = RagdollBuilder.Dims(H);
                    float lowest = orientation == 0 ? df.KneeToSole + df.HipToKnee + 0.02f : 0.15f * H; // Hüfthöhe über dem tiefsten Punkt
                    // Freier Fall aus der Höhe h; die Messung beginnt 25 cm über dem Boden (wie bisher der Start).
                    Ragdoll = RagdollBuilder.Build(new Vector3(0, lowest + h, 0), rot, RagdollPose.Standing, H, p.Mass_kg, p.Male, invertKneeFlexion);
                    approach.HoldRagdoll = false;
                    approach.Fall = true;
                    approach.FallReleaseY = lowest + 0.25f;
                    // Endgeschwindigkeit: Bauch-/Seitenlage ca. 55 m/s, Füße voran schlanker ca. 75 m/s
                    approach.TerminalVelocity_m_s = orientation == 0 ? 75 : 55;
                    approach.Duration_s = Mathf.Sqrt(2 * h / 9.81f) + 1;   // Obergrenze
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
            go.AddComponent<CrashSurface>().kind = "airbag";
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
        /// <summary>Gurtkraft über die Zeit (Schulter- und Beckengurt) für Gurtmarken.</summary>
        public readonly Dictionary<string, ConfigurableJoint> BeltJoints = new Dictionary<string, ConfigurableJoint>();
        public double ShoulderBeltImpulse_Ns, LapBeltImpulse_Ns;

        void Belt(Rigidbody car, float H)
        {
            BeltJoints.Clear();
            foreach (var kv in BeltFor(Ragdoll, car)) BeltJoints[kv.Key] = kv.Value;
        }

        /// <summary>Gurt für ein beliebiges Ragdoll (auch mehrere Insassen im Szenario).</summary>
        /// <summary>Anteil der Schultergurtkraft, der das Brustbein eindrückt (Rest: Schlüsselbein, Rippen) — Annahme.</summary>
        public const float SternumBeltShare = 0.2f;

        static Dictionary<string, ConfigurableJoint> BeltFor(Ragdoll rd, Rigidbody car)
        {
            var joints = new Dictionary<string, ConfigurableJoint>();
            // Schultergurt: liegt über Schlüsselbein, Rippen und Brustbein. Nur ein Teil der Gurtkraft drückt das Brustbein
            // ein (hier 20 %, Annahme: 5 kN Gurtkraft → ca. 40 mm Deflexion wie am Hybrid III), der Rest trägt der Brustkorb.
            // Vorher hing der ganze Gurt am 0,45 kg leichten Sternum: Deflexion immer am Anschlag (90 mm).
            foreach (var name in new[] { "Sternum", "Thorax", "Becken" })
            {
                var seg = rd.Segments[name];
                var j = seg.gameObject.AddComponent<ConfigurableJoint>();
                joints[name] = j;
                j.connectedBody = car;
                j.autoConfigureConnectedAnchor = true;
                j.anchor = Vector3.zero;
                // Der Gurt hält nach vorn und oben, kaum zur Seite (Schultergurt rutscht ab).
                j.xMotion = ConfigurableJointMotion.Free;
                j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
                j.linearLimit = new SoftJointLimit { limit = name == "Becken" ? 0.04f : 0.06f }; // Gurtlose und Dehnung
                // Gurtband: ca. 140 kN/m (Dehnung ~7 % bei 10 kN auf 1 m) — didaktische Größenordnung
                float share = name == "Sternum" ? SternumBeltShare : name == "Thorax" ? 1 - SternumBeltShare : 1;
                j.linearLimitSpring = new SoftJointLimitSpring { spring = 140000 * share, damper = 3000 * share };
            }
            return joints;
        }

        // ------------------------------------------------------------------ Ablauf

        /// <summary>
        /// Startet den Crash in zwei Phasen:
        /// 1. <b>Berechnen</b>: Die Physik läuft mit <see cref="crashStep_s"/> (10 kHz) als manuelle Simulation
        ///    (<see cref="SimulationMode.Script"/>), verteilt auf Frames mit höchstens <see cref="computeBudget_ms"/> Rechenzeit.
        ///    Echtzeit mit 10 000 Physikschritten pro Sekunde schafft kein Rechner — so ruckelt nichts, und die Messung bleibt gleich.
        /// 2. <b>Abspielen</b>: Die aufgezeichneten Posen laufen flüssig in Echtzeit oder Zeitlupe (<see cref="Replay"/>).
        /// </summary>
        /// <param name="slowMotion">Zeitlupenfaktor der Wiedergabe (1 = Echtzeit).</param>
        /// <param name="progress">Fortschritt der Berechnung 0..1 (für die Anzeige).</param>
        /// <param name="playback">false = nur rechnen (Kalibrierung).</param>
        public IEnumerator Run(CrashParams p, float slowMotion, Action<List<CrashMeasurement>> done, Action<float> progress = null, bool playback = true)
        {
            if (p.MechanismId == "szenario") { yield return RunTraffic(p, slowMotion, done, progress, playback); yield break; }
            Running = true;
            Action<float, float> drive; CrashApproach approach;   // vorab deklariert (mcs-Fehler bei out var in Iteratoren)
            float duration = Build(p, out drive, out approach);
            float dt = crashStep_s;
            int steps = Mathf.CeilToInt(duration / dt);

            var savedMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            _savedMode = savedMode; _modeSaved = true;
            CrashSensor.ClearLookup();
            BeginRecording();
            SetVisible(false);   // Zwischenstände der Berechnung nicht zeigen
            Physics.SyncTransforms();
            // Nachgiebiger Kontakt auch beim Fahrzeugunfall: Körper mit Gewebesteifigkeit und Fettschicht statt starr
            bool soft = p.SoftContact && RegisterSoftBody(p, Anatomy.BodyFatShape.Model());
            AirbagSparesSternum();
            if (soft) RegisterSoftVehicleParts();
            SoftContact.Enabled = soft;
            var tone = LoadMuscleTone();
            var muscles = tone != null ? new MuscleToneDriver(Ragdoll, tone, p.Occupant, p.Male) : null;
            var watch = new System.Diagnostics.Stopwatch();

            // ---- Phase 1: Vorlauf (Anfahrt bzw. freier Fall), grober Schritt, keine Messung
            CrashStart = 0;
            RecordFrame(0);
            if (approach != null)
            {
                var ragdollBodies = Ragdoll.Root.GetComponentsInChildren<Rigidbody>();
                var carry = new List<(Rigidbody rb, Vector3 start, Vector3 v)>();
                foreach (var c in approach.Carry) carry.Add((c.rb, c.rb.position, c.v));
                if (approach.HoldRagdoll)
                    foreach (var rb in ragdollBodies) { rb.isKinematic = true; carry.Add((rb, rb.position, approach.RagdollVelocity)); }
                float da = approachStep_s, ta = 0, lastFrame = 0, vt = approach.TerminalVelocity_m_s;
                int aSteps = Mathf.RoundToInt(approach.Duration_s / da), k = 0;
                bool arrived = aSteps <= 0;
                while (!arrived)
                {
                    watch.Restart();
                    do
                    {
                        k++; ta = k * da;
                        foreach (var c in carry) c.rb.MovePosition(c.start + c.v * ta);   // ohne Rundungsdrift
                        if (approach.Fall)
                            foreach (var rb in ragdollBodies)
                            {
                                // Luftwiderstand: a = −g·(|v|/v_end)² in Bewegungsrichtung (je Masse gleich, also kein Drehmoment)
                                var vel = CrashPhysics.Velocity(rb);
                                float sp = vel.magnitude;
                                if (sp > 0.01f) rb.AddForce(-vel / sp * 9.81f * (sp / vt) * (sp / vt), ForceMode.Acceleration);
                            }
                        SoftContact.StepDt = da;
                        Physics.Simulate(da);
                        if (ta - lastFrame >= recordInterval_s - 1e-6f) { RecordFrame(ta); lastFrame = ta; }
                        arrived = k >= aSteps || (approach.Fall && Ragdoll.Pelvis.position.y <= approach.FallReleaseY);
                    } while (!arrived && watch.Elapsed.TotalMilliseconds < computeBudget_ms);
                    progress?.Invoke(0.1f * Mathf.Clamp01((float)k / Mathf.Max(1, aSteps)));
                    yield return null;
                }
                if (approach.HoldRagdoll)
                    foreach (var rb in ragdollBodies) { rb.isKinematic = false; CrashPhysics.SetVelocity(rb, approach.RagdollVelocity); }
                CrashStart = ta;
                if (_frameTimes[_frameTimes.Count - 1] < ta) RecordFrame(ta);
            }

            foreach (var s in Ragdoll.Sensors) s.StartRecording(dt, steps + 1);
            DebugTrace = new List<string>(steps + 1);
            ShoulderBeltImpulse_Ns = LapBeltImpulse_Ns = 0;
            var gauges = gameObject.AddComponent<CrashGauges>();
            gauges.Begin(Ragdoll);
            ConfigurableJoint beltShoulder, beltLap, beltRibs;
            BeltJoints.TryGetValue("Sternum", out beltShoulder);
            BeltJoints.TryGetValue("Thorax", out beltRibs);
            BeltJoints.TryGetValue("Becken", out beltLap);

            // ---- Phase 2: Aufprall mit 0,1 ms und Messung (Zeit t ab Ende des Vorlaufs, wie früher ab Start)
            int step = 0, everyFrame = Mathf.Max(1, Mathf.RoundToInt(recordInterval_s / dt));
            while (step < steps)
            {
                watch.Restart();
                // mindestens ein Schritt je Frame, sonst so viele, wie ins Zeitbudget passen
                do
                {
                    float t = step * dt;
                    _simTime = t;
                    // Reihenfolge wie früher in FixedUpdate: Antrieb, Muskeln, Messung des letzten Schritts, dann Physik
                    drive?.Invoke(t, dt);
                    muscles?.Apply(t);
                    foreach (var s in Ragdoll.Sensors) s.Step(dt);
                    if (beltShoulder) ShoulderBeltImpulse_Ns += beltShoulder.currentForce.magnitude * dt;
                    if (beltRibs) ShoulderBeltImpulse_Ns += beltRibs.currentForce.magnitude * dt;
                    if (beltLap) LapBeltImpulse_Ns += beltLap.currentForce.magnitude * dt;
                    gauges.Step();
                    SoftContact.StepDt = dt;
                    Physics.Simulate(dt);
                    if (DebugTrace != null)
                    {
                        var hv = CrashPhysics.Velocity(Ragdoll.Head); var tv = CrashPhysics.Velocity(Ragdoll.Segments["Thorax"]);
                        var sbt = new System.Text.StringBuilder($"{t * 1000:0.0} ms Kopf v=({hv.x:0.00},{hv.y:0.00},{hv.z:0.00}) p=({Ragdoll.Head.position.x:0.000},{Ragdoll.Head.position.y:0.000},{Ragdoll.Head.position.z:0.000}) Thorax v=({tv.x:0.00},{tv.y:0.00},{tv.z:0.00})");
                        foreach (var go in _spawned) if (go) { var rbk = go.GetComponent<Rigidbody>(); if (rbk) sbt.Append($" {go.name} x={rbk.position.x:0.000} z={rbk.position.z:0.000}"); }
                        if (beltShoulder) sbt.Append($" Gurt S {beltShoulder.currentForce.magnitude:0} N");
                        if (beltLap) sbt.Append($" B {beltLap.currentForce.magnitude:0} N");
                        if (Ragdoll.NeckJoint) sbt.Append($" Hals {Ragdoll.NeckJoint.currentForce.magnitude:0} N {Ragdoll.NeckJoint.currentTorque.magnitude:0} Nm");
                        DebugTrace.Add(sbt.ToString());
                    }
                    step++;
                    if (step % everyFrame == 0 || step == steps) RecordFrame(CrashStart + step * dt);
                } while (step < steps && watch.Elapsed.TotalMilliseconds < computeBudget_ms);
                progress?.Invoke(0.1f + 0.9f * step / steps);
                yield return null;
            }
            foreach (var s in Ragdoll.Sensors) s.StopRecording();
            SoftContact.Enabled = false;
            gauges.Stop();
            AbdomenLoad abdomen;
            var measured = Measure(p, gauges, Ragdoll, out abdomen);
            LastAbdomenLoad = abdomen;
            Destroy(gauges);
            PrepareGlass();
            PrepareFractures(Ragdoll, measured);
            Running = false;

            if (playback) yield return Replay(slowMotion, () => done(measured));
            else
            {
                SetVisible(true);
                RestorePhysics();
                done(measured);
            }
        }

        // ------------------------------------------------------------------ Aufzeichnung und Wiedergabe

        [Tooltip("Maximale Rechenzeit für die Crashphysik pro Frame (ms). Kleiner = flüssigere Anzeige, längere Berechnung.")]
        [SerializeField] float computeBudget_ms = 12f;
        [Tooltip("Abstand der aufgezeichneten Posen (s). 2 ms reichen für flüssige Zeitlupe bis ×0,05.")]
        [SerializeField] float recordInterval_s = 0.002f;

        readonly List<Transform> _tracked = new List<Transform>();
        readonly List<RigidbodyInterpolation> _savedInterp = new List<RigidbodyInterpolation>();
        readonly List<Rigidbody> _trackedBodies = new List<Rigidbody>();
        readonly List<float> _frameTimes = new List<float>();
        readonly List<Vector3[]> _framePos = new List<Vector3[]>();
        readonly List<Quaternion[]> _frameRot = new List<Quaternion[]>();
        readonly List<(Renderer r, bool on)> _renderers = new List<(Renderer, bool)>();
        SimulationMode _savedMode; bool _modeSaved;
        float _simTime;

        /// <summary>Beginn der Aufprallphase in der Aufzeichnung (s): davor Anfahrt bzw. freier Fall.</summary>
        public float CrashStart { get; private set; }

        /// <summary>Ist ein berechneter Crash zum (erneuten) Abspielen vorhanden?</summary>
        public bool HasRecording => _frameTimes.Count > 1 && Ragdoll != null;
        public bool Playing { get; private set; }
        public bool PlaybackPaused { get; set; }
        /// <summary>Wiedergabezeit bzw. Dauer des aufgezeichneten Crashs (s).</summary>
        public float PlaybackTime { get; private set; }
        public float RecordedDuration => _frameTimes.Count > 0 ? _frameTimes[_frameTimes.Count - 1] : 0;
        /// <summary>Zeitlupenfaktor der laufenden Wiedergabe (änderbar).</summary>
        public float PlaybackSpeed { get; set; } = 1;

        void BeginRecording()
        {
            _tracked.Clear(); _trackedBodies.Clear(); _savedInterp.Clear();
            _frameTimes.Clear(); _framePos.Clear(); _frameRot.Clear();
            foreach (var v in _visuals) v.ClearRecording();
            BeginGlass();
            var bodies = new List<Rigidbody>();
            foreach (var rd in AllRagdolls()) bodies.AddRange(rd.Root.GetComponentsInChildren<Rigidbody>(true));
            foreach (var go in _spawned) if (go) bodies.AddRange(go.GetComponentsInChildren<Rigidbody>(true));
            foreach (var rb in bodies.Distinct())
            {
                _trackedBodies.Add(rb); _tracked.Add(rb.transform); _savedInterp.Add(rb.interpolation);
                rb.interpolation = RigidbodyInterpolation.None;   // Interpolation würde die Wiedergabe überschreiben
            }
        }

        void RecordFrame(float time)
        {
            var pos = new Vector3[_tracked.Count];
            var rot = new Quaternion[_tracked.Count];
            for (int i = 0; i < _tracked.Count; i++)
            {
                if (!_tracked[i]) continue;
                pos[i] = _tracked[i].position; rot[i] = _tracked[i].rotation;
            }
            _frameTimes.Add(time); _framePos.Add(pos); _frameRot.Add(rot);
            StepGlass(time);
            StepPedestrianDents();
            foreach (var v in _visuals) v.Record(time);
        }

        /// <summary>Pose zum Zeitpunkt t (s) aus den Aufzeichnungen, linear bzw. sphärisch interpoliert.</summary>
        public void ShowPose(float t)
        {
            if (_frameTimes.Count == 0) return;
            t = Mathf.Clamp(t, 0, RecordedDuration);
            int hi = _frameTimes.BinarySearch(t);
            if (hi < 0) hi = ~hi;
            hi = Mathf.Clamp(hi, 1, _frameTimes.Count - 1);
            int lo = hi - 1;
            float span = _frameTimes[hi] - _frameTimes[lo];
            float u = span > 0 ? Mathf.Clamp01((t - _frameTimes[lo]) / span) : 1;
            var p0 = _framePos[lo]; var p1 = _framePos[hi]; var r0 = _frameRot[lo]; var r1 = _frameRot[hi];
            for (int i = 0; i < _tracked.Count; i++)
                if (_tracked[i]) _tracked[i].SetPositionAndRotation(Vector3.LerpUnclamped(p0[i], p1[i], u), Quaternion.SlerpUnclamped(r0[i], r1[i], u));
            foreach (var v in _visuals) { v.Show(lo, hi, u); v.ShowEffects(t); }
            PlaybackTime = t;
        }

        /// <summary>Spielt den berechneten Crash ab (auch erneut, mit anderer Zeitlupe). Pause und Spulen über
        /// <see cref="PlaybackPaused"/> und <see cref="Seek"/>.</summary>
        public IEnumerator Replay(float slowMotion, Action finished = null)
        {
            if (!HasRecording) { finished?.Invoke(); yield break; }
            if (!_modeSaved) { _savedMode = Physics.simulationMode; _modeSaved = true; }
            Physics.simulationMode = SimulationMode.Script;   // keine Physik während der Wiedergabe
            Playing = true; PlaybackPaused = false;
            PlaybackSpeed = Mathf.Clamp(slowMotion, 0.02f, 1f);
            ShowPose(0);
            SetVisible(true);
            float t = 0;
            _stopPlayback = false;
            while (t < RecordedDuration && !_stopPlayback)
            {
                // Vorlauf in Echtzeit, Zeitlupe erst kurz vor dem Aufprall
                if (!PlaybackPaused) t += Time.deltaTime * (t < CrashStart - 0.15f || t > CrashStart + SlowMotionWindow_s ? 1f : PlaybackSpeed);
                else t = PlaybackTime;    // Seek während der Pause übernehmen
                ShowPose(t);
                yield return null;
            }
            ShowPose(RecordedDuration);
            Playing = false;
            RestorePhysics();
            finished?.Invoke();
        }

        /// <summary>Kurzbericht der Fahrzeugstruktur für das Ergebnis (null, wenn das Fahrzeug nicht verformbar gerechnet wurde).</summary>
        public Func<string> VehicleReport;
        /// <summary>Diagnose: je Physikschritt eine Zeile (Kopf, Thorax, Fahrzeugteile, Gurt, Hals); null = aus.</summary>
        public List<string> DebugTrace;

        bool _stopPlayback;

        /// <summary>Zeitlupe nur bis so lange nach dem Aufprall, danach Echtzeit (Gewalt: kurze Einwirkung, langes Ausrollen).</summary>
        public float SlowMotionWindow_s = float.PositiveInfinity;

        /// <summary>Wiedergabe beenden: springt im nächsten Bild in die Endlage, Abschluss wie sonst.</summary>
        public void StopPlayback() { if (Playing) _stopPlayback = true; }

        /// <summary>Während der Wiedergabe springen (s, relativ).</summary>
        public void Seek(float delta_s) { if (Playing) ShowPose(PlaybackTime + delta_s); }

        void SetVisible(bool on)
        {
            if (!on)
            {
                _renderers.Clear();
                // nur Bewegtes ausblenden (Ragdoll, Haut, Fahrzeuge, Airbag); Boden und Hindernisse bleiben sichtbar
                var all = new List<Renderer>();
                foreach (var rd in AllRagdolls()) all.AddRange(rd.Root.GetComponentsInChildren<Renderer>(true));
                foreach (var tr in _tracked) if (tr) all.AddRange(tr.GetComponentsInChildren<Renderer>(true));
                foreach (var r in all.Distinct()) { _renderers.Add((r, r.enabled)); r.enabled = false; }
            }
            else
            {
                foreach (var e in _renderers) if (e.r) e.r.enabled = e.on;
                _renderers.Clear();
            }
        }

        /// <summary>Nach Berechnung/Wiedergabe: normale Physik und Interpolation wieder an (Ragdoll liegt in der Endlage).</summary>
        void RestorePhysics()
        {
            for (int i = 0; i < _trackedBodies.Count; i++)
                if (_trackedBodies[i]) _trackedBodies[i].interpolation = _savedInterp[i];
            if (_modeSaved) { Physics.simulationMode = _savedMode; _modeSaved = false; }
        }

        /// <summary>
        /// Wertet die Sensoren aus — gefiltert nach SAE J211 (CFC) wie bei Dummy-Messungen:
        /// HIC15 (Kopf, CFC 1000), Brustwirbelsäule 3 ms (CFC 180), Deflexion und VC (CFC 600),
        /// Nij (Kraft CFC 1000, Moment CFC 600), axiale Femurkraft und Kontaktkräfte (CFC 600).
        /// Rohwerte ohne Kalibrierung — die wendet die Sandbox an.
        /// </summary>
        List<CrashMeasurement> Measure(CrashParams p, CrashGauges g, Ragdoll rd, out AbdomenLoad abdomenLoad)
        {
            abdomenLoad = null;
            var result = new List<CrashMeasurement>();
            var byRegion = rd.Sensors.GroupBy(s => s.Region).ToDictionary(x => x.Key, x => x.ToList());
            double dt = rd.Sensors[0].Dt_s;
            Action<string, double> add = (id, v) => result.Add(new CrashMeasurement { metric = id, value = v });

            var head = byRegion["Kopf"][0];
            var headAcc = Biomechanics.Cfc(head.Accel_g, dt, 1000);
            if (p.MechanismId == "motorrad") for (int i = 0; i < headAcc.Count; i++) headAcc[i] *= helmetHeadFactor;
            add("HIC15", Biomechanics.Hic(headAcc, dt, 0.015));

            var spine = rd.Segments["Thorax"].GetComponent<CrashSensor>();
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
                // Der Airbag stützt den Rumpf flächig ab; die Bauchsteifigkeit des Organmodells stammt aus örtlichem Eindrücken
                // (Stange, Gurt, Lenkradkranz). Seine Kraft zählt deshalb nicht als Organkompression (Annahme) — sonst
                // ergaben 6,7 kN Airbagkraft 25 cm „Eindrückung“ und sichere Leber- und Nierenrisse beim angegurteten Fahrer.
                for (int i = 0; i < abd[0].Force_kN.Count; i++)
                    force_N.Add(abd.Sum(s => i < s.Force_kN.Count ? Math.Max(0, s.Force_kN[i] - (i < s.CushionForce_kN.Count ? s.CushionForce_kN[i] : 0)) : 0) * 1000);
                abdomenLoad = OrganMechanics.Evaluate(organs, tissues, Biomechanics.Cfc(force_N, dt, 600), dt, lateral, p.Height_m);
                result.AddRange(OrganMechanics.Measurements(abdomenLoad));
            }
            return result;
        }

        /// <summary>Organbelastung des letzten Crashs (für die Ergebnisanzeige); null ohne organ_model.json/tissues.json.</summary>
        public AbdomenLoad LastAbdomenLoad { get; private set; }

        /// <summary>Alle Ragdolls der Szene (Szenario: alle Personen, sonst das eine).</summary>
        IEnumerable<Ragdoll> AllRagdolls()
        {
            if (Humans.Count > 0) { foreach (var h in Humans) if (h.Rd != null) yield return h.Rd; }
            else if (Ragdoll != null) yield return Ragdoll;
        }

        static MuscleToneFile _muscleTone;
        static MuscleToneFile LoadMuscleTone()
        {
            if (_muscleTone != null) return _muscleTone;
            string path = System.IO.Path.Combine(Data.InjuryDatabase.Root, "muscle_tone.json");
            if (!System.IO.File.Exists(path)) { Debug.LogWarning("[Crash] muscle_tone.json fehlt — Ragdoll ohne Muskeltonus."); return null; }
            return _muscleTone = JsonUtility.FromJson<MuscleToneFile>(System.IO.File.ReadAllText(path));
        }

        static VehicleModelFile _vehicleModel;
        static bool _vehicleTried;
        /// <summary>Fahrzeugmodelle (vehicle_model.json); null ohne Datei — dann die alten festen Pulse.</summary>
        /// <summary>Bei jedem Spielstart neu laden — sonst bleibt bei abgeschaltetem „Domain Reload“ die alte Datei im Speicher.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetModelCache() { _vehicleModel = null; _vehicleTried = false; _organModel = null; }

        public static VehicleModelFile LoadVehicleModel()
        {
            if (_vehicleModel != null || _vehicleTried) return _vehicleModel;
            _vehicleTried = true;
            string path = System.IO.Path.Combine(Data.InjuryDatabase.Root, "vehicle_model.json");
            if (System.IO.File.Exists(path)) _vehicleModel = JsonUtility.FromJson<VehicleModelFile>(System.IO.File.ReadAllText(path));
            return _vehicleModel;
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
            if (_modeSaved) Physics.simulationMode = _savedMode;   // Szenenwechsel mitten im Crash
            _modeSaved = false;
        }
    }

    /// <summary>
    /// Muskeltonus während des Crashs: Gelenkantriebe halten die Ausgangshaltung mit einem Moment bis
    /// Maximalmoment × Aktivierung (MuscleTone). Der passive Tonus aus dem RagdollBuilder bleibt erhalten.
    /// </summary>
    public class MuscleToneDriver
    {
        readonly MuscleToneFile _f;
        OccupantState _state;
        /// <summary>Zustand wechseln (z. B. Insasse sieht die Gefahr und spannt sich an).</summary>
        public void SetState(OccupantState state) { if (state == _state) return; _state = state; _lastActivation = double.NaN; }
        public OccupantState State => _state;
        readonly bool _male;
        double _lastActivation = double.NaN;
        class Driven { public string Segment; public ConfigurableJoint Joint; public float Passive; }
        readonly List<Driven> _joints = new List<Driven>();

        public MuscleToneDriver(Ragdoll rd, MuscleToneFile f, OccupantState state, bool male)
        {
            _f = f; _state = state; _male = male;
            foreach (var kv in rd.Segments)
            {
                var j = kv.Value.GetComponent<ConfigurableJoint>();
                if (j == null || f.joints.All(x => x.segment != kv.Key)) continue;
                _joints.Add(new Driven { Segment = kv.Key, Joint = j, Passive = j.slerpDrive.positionSpring });
            }
            Apply(0);
        }

        /// <summary>Gelenkantriebe für die Aktivierung zum Zeitpunkt t (s). Nur neu setzen, wenn sich die Aktivierung ändert —
        /// meist ist sie konstant, und 10 000 Antriebe pro Sekunde neu zu setzen kostet viel.</summary>
        public void Apply(float t)
        {
            double a = MuscleTone.Activation(_f.activation, _state, t);
            if (a == _lastActivation) return;
            _lastActivation = a;
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
}

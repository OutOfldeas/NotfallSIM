using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace NotfallSIM.Crash
{
    // ---------- vehicle_model.json › vehicleTypes ----------

    /// <summary>
    /// Kraft-Weg-Kennlinie einer Fahrzeugseite für Kontakt über die ganze Breite (stückweise linear, danach mit der letzten
    /// Steigung weiter). Bei schmalerem Kontakt anteilig weniger (Kraft je Meter Kontaktbreite, wie in CRASH3).
    /// </summary>
    [Serializable]
    public class FaceCurve
    {
        public double[] deflection_m, force_N;
        /// <summary>Kontaktbreite, für die die Kennlinie gilt (m); 0 = ganze Seite. Seite: Breite der Barriere im Versuch.</summary>
        [OptionalField] public double refWidth_m;

        public double Force(double c)
        {
            if (deflection_m == null || deflection_m.Length < 2) return 0;
            if (c <= 0) return 0;
            int n = deflection_m.Length;
            for (int i = 1; i < n; i++)
                if (c <= deflection_m[i])
                    return force_N[i - 1] + (force_N[i] - force_N[i - 1]) * (c - deflection_m[i - 1]) / (deflection_m[i] - deflection_m[i - 1]);
            double slope = (force_N[n - 1] - force_N[n - 2]) / (deflection_m[n - 1] - deflection_m[n - 2]);
            return force_N[n - 1] + slope * (c - deflection_m[n - 1]);
        }

        /// <summary>Umkehrung: Verformung bei Kraft F (Kennlinie muss steigen).</summary>
        public double Deflection(double f)
        {
            if (deflection_m == null || deflection_m.Length < 2 || f <= 0) return 0;
            int n = deflection_m.Length;
            for (int i = 1; i < n; i++)
                if (f <= force_N[i])
                    return deflection_m[i - 1] + (deflection_m[i] - deflection_m[i - 1]) * (f - force_N[i - 1]) / Math.Max(1e-9, force_N[i] - force_N[i - 1]);
            double slope = (force_N[n - 1] - force_N[n - 2]) / (deflection_m[n - 1] - deflection_m[n - 2]);
            return deflection_m[n - 1] + (f - force_N[n - 1]) / Math.Max(1e-9, slope);
        }

        /// <summary>Anfangssteifigkeit (N/m).</summary>
        public double InitialSlope => deflection_m != null && deflection_m.Length >= 2 ? (force_N[1] - force_N[0]) / Math.Max(1e-9, deflection_m[1] - deflection_m[0]) : 1e6;
    }

    /// <summary>Fahrzeugtyp: Masse, Maße, Reifen, Kennlinien von Front, Seite und Heck, Eindringen in den Innenraum.</summary>
    [Serializable]
    public class VehicleType
    {
        public string id, label;
        public double mass_kg, length_m, width_m, wheelbase_m, yawInertia_kgm2, corneringStiffness_N_rad;
        public FaceCurve front, side, rear;
        /// <summary>Entlastung so viel steifer als die Sekante bis zur größten Verformung (plastisch).</summary>
        public double unloadFactor;
        /// <summary>Kleinste mittragende Breite (m): Querträger verteilen die Last auch bei Pfahl oder Ecke.</summary>
        public double minContactWidth_m;
        /// <summary>Front: Eindringen von Armaturenbrett/Fußraum über intrusionStart_m Knautschweg hinaus, je m mal intrusionPerCrush.</summary>
        public double intrusionStart_m, intrusionPerCrush, maxFrontIntrusion_m;
        /// <summary>Seite: die Verformung der Seite ist das Eindringen der Tür (keine Knautschzone), höchstens maxSideIntrusion_m.</summary>
        public double maxSideIntrusion_m, doorLag_s;
        public float[] color;
        public string sourceId, evidence, note;
    }

    // ---------- traffic_scenarios.json ----------

    [Serializable]
    public class TrafficOccupant
    {
        /// <summary>fahrer, beifahrer, hinten_links, hinten_rechts</summary>
        public string seat;
        public bool belted = true;
    }

    /// <summary>
    /// Fahrmanöver vor dem Aufprall. Zeitpunkte gelten relativ zum geplanten (ungebremsten) Aufprall: „1 s vorher bremsen“ heißt,
    /// der Fahrer hat 1 s vor dem Zeitpunkt reagiert, an dem es ohne Bremsung gekracht hätte.
    /// </summary>
    [Serializable]
    public class TrafficManeuver
    {
        public bool brake;
        public double brakeLead_s;
        /// <summary>Gewünschte Verzögerung in g; 0 = so stark wie die Fahrbahn erlaubt (Vollbremsung mit ABS).</summary>
        [OptionalField] public double brakeDecel_g;
        /// <summary>Ausweichen: −1 links, 0 nicht, +1 rechts.</summary>
        public int steer;
        public double steerLead_s;
        /// <summary>Querbeschleunigung beim Ausweichen in g; 0 = 70 % der Haftung.</summary>
        [OptionalField] public double steerAccel_g;
    }

    [Serializable]
    public class TrafficVehicle
    {
        public string id, label, type;
        public double speed_kmh, heading_deg;
        /// <summary>Schleudern: Bewegungsrichtung gegen die Fahrzeuglängsachse gedreht (Grad, + nach rechts).</summary>
        [OptionalField] public double drift_deg;
        /// <summary>Ziel: anderes Fahrzeug, Hindernis oder leer (dann x/z = Mitte zum Aufprallzeitpunkt).</summary>
        [OptionalField] public string target;
        /// <summary>Getroffene Seite des Ziels: front, rear, left, right.</summary>
        [OptionalField] public string targetFace;
        /// <summary>Versatz entlang der getroffenen Seite (m): Front/Heck + nach rechts, Seiten + nach vorn.</summary>
        [OptionalField] public double targetOffset_m;
        /// <summary>Treffzeitpunkt gegenüber dem geplanten Aufprall (s), z. B. Folgeauffahrt 0,6 s später.</summary>
        [OptionalField] public double aimTime_s;
        [OptionalField] public double x, z;
        public TrafficOccupant[] occupants;
        [OptionalField] public TrafficManeuver maneuver;
    }

    [Serializable]
    public class TrafficPedestrian
    {
        public string id, label;
        [OptionalField] public string target, targetFace;
        [OptionalField] public double targetOffset_m, aimTime_s, x, z;
        public double heading_deg, speed_kmh;
    }

    [Serializable]
    public class TrafficObstacle
    {
        public string id, label;
        public double x, z, heading_deg, length_m, width_m, height_m;
        /// <summary>Steifigkeit (N/m), elastisch; Beton/Baum praktisch starr.</summary>
        public double stiffness_N_m;
        [OptionalField] public float[] color;
    }

    [Serializable]
    public class TrafficScenario
    {
        public string id, label, description;
        /// <summary>Haftreibung Reifen–Fahrbahn (trocken 0,8, nass 0,5, Schnee 0,2).</summary>
        public double friction;
        /// <summary>Vorlauf bis zum geplanten Aufprall (s).</summary>
        public double approach_s;
        public TrafficVehicle[] vehicles;
        [OptionalField] public TrafficPedestrian[] pedestrians;
        [OptionalField] public TrafficObstacle[] obstacles;
    }

    [Serializable]
    public class TrafficScenarioFile
    {
        public string version, note;
        public TrafficScenario[] scenarios;
    }

    /// <summary>
    /// Fahrdynamik und Fahrzeug-Kollision in der Draufsicht (x rechts, z vorn wie in Unity; Gierwinkel ψ im Uhrzeigersinn
    /// von oben, ψ = 0 heißt Fahrt nach +z). Reines C# — im Crash-Rig und im Test gleich.
    ///
    /// - Reifen: Einspurmodell (Schräglaufsteifigkeit je Achse, gesättigt am Kammschen Kreis µ·N), Bremse mit ABS (bis µ·g),
    ///   Ausweichen über die Lenkung (Lenkwinkel aus der gewünschten Querbeschleunigung). Rollwiderstand 0,015.
    /// - Kollision: zwei Quader (bzw. Fahrzeug und Hindernis) — Überdeckung (Trennachsen-Verfahren) ergibt Richtung und
    ///   gesamte Verformung; sie teilt sich auf die beiden getroffenen Seiten so auf, dass beide dieselbe Kraft tragen
    ///   (Federn in Reihe, Kennlinien aus vehicle_model.json, Kraft anteilig zur Kontaktbreite). Plastisch: Entlastung steif,
    ///   die Verformung bleibt. Reibung zwischen den Karosserien µ = 0,4 (Annahme). Kraft am Mittelpunkt der Überdeckung → Drehung.
    /// - Fußgänger bewegen sich hier nur geradlinig (75 kg gegen 1500 kg: Rückwirkung auf das Fahrzeug vernachlässigt);
    ///   ihr Aufprall läuft im Ragdoll.
    /// Grenzen: kein Wanken/Nicken/Überschlag, keine Verformung der Form (Quader bleiben Quader), feste Schwerpunktlage.
    /// </summary>
    public class TrafficSim
    {
        public const int Front = 0, Rear = 1, Left = 2, Right = 3;
        public static readonly string[] FaceNames = { "front", "rear", "left", "right" };
        public static int FaceIndex(string name) => name == "rear" ? Rear : name == "left" ? Left : name == "right" ? Right : Front;
        const double G = 9.81, VehicleFriction = 0.4, RollingResistance = 0.015;

        public class FaceState
        {
            public double MaxCrush, MaxCurveForce;   // größte Verformung und die Kennlinienkraft dort (volle Breite)
            public double Crush;                     // aktuelle Verformung
            public double Position;                  // Ort des Kontakts entlang der Seite (lokal x bzw. z)
            public double Intrusion;                 // Innenraum-Eindringen (verzögert)
            public double PeakForce_N;
        }

        public class Body
        {
            public string Id, Label;
            public VehicleType Type;
            public TrafficVehicle Def;
            public bool Static;                      // Hindernis
            public double Stiffness = 5e7;           // Hindernis
            public double M, Iz, L, W;
            public double X, Z, Psi, Vx, Vz, R;      // Lage, Geschwindigkeit (Welt), Gierrate
            public double Delta, BrakeForce;         // aktuell
            public double BrakeStart = double.PositiveInfinity, SteerStart = double.PositiveInfinity;
            public int SteerDir;
            public double BrakeDecel_g, SteerAccel_g;
            public double KinematicUntil = -1;       // Schleudern: bis dahin ohne Reifenkräfte (gleitet mit fester Geschwindigkeit)
            public readonly FaceState[] Faces = { new FaceState(), new FaceState(), new FaceState(), new FaceState() };
            public double FirstContact = -1, LastContact = -1;
            public double PreVx, PreVz;              // Geschwindigkeit beim ersten Kontakt
            /// <summary>Größte Geschwindigkeitsänderung durch Stöße (m/s): Betrag des aufsummierten Kollisionsimpulses / Masse (ohne Bremsen und Reifen).</summary>
            public double MaxDeltaV;
            public double PdofX, PdofZ;              // Kollisionsimpuls (Welt, N·s) — Hauptstoßrichtung
            public double Fx, Fz, Tau;               // Kräfte dieses Schritts

            public double SinPsi => Math.Sin(Psi);
            public double CosPsi => Math.Cos(Psi);
            /// <summary>Vorwärtsrichtung (x, z).</summary>
            public void Forward(out double fx, out double fz) { fx = Math.Sin(Psi); fz = Math.Cos(Psi); }
            /// <summary>Rechts (x, z).</summary>
            public void RightDir(out double rx, out double rz) { rx = Math.Cos(Psi); rz = -Math.Sin(Psi); }
            public double Speed => Math.Sqrt(Vx * Vx + Vz * Vz);
            public void ToWorld(double lx, double lz, out double wx, out double wz)
            { wx = X + lx * Math.Cos(Psi) + lz * Math.Sin(Psi); wz = Z - lx * Math.Sin(Psi) + lz * Math.Cos(Psi); }
            public void ToLocal(double wx, double wz, out double lx, out double lz)
            { double dx = wx - X, dz = wz - Z; lx = dx * Math.Cos(Psi) - dz * Math.Sin(Psi); lz = dx * Math.Sin(Psi) + dz * Math.Cos(Psi); }
            /// <summary>Geschwindigkeit eines Weltpunkts: v + ω × r (ω um +y, im Uhrzeigersinn).</summary>
            public void PointVelocity(double wx, double wz, out double vx, out double vz) { vx = Vx + R * (wz - Z); vz = Vz - R * (wx - X); }
            /// <summary>Hauptstoßrichtung als Uhrzeit (12 = von vorn, 3 = von rechts, 9 = von links, 6 = von hinten); 0 ohne Kontakt.</summary>
            public int PdofClock()
            {
                double m = Math.Sqrt(PdofX * PdofX + PdofZ * PdofZ);
                if (m < 1e-6) return 0;
                // Kraft auf das Fahrzeug zeigt vom Stoßort weg; Herkunft = Gegenrichtung
                ToLocalDir(-PdofX / m, -PdofZ / m, out double lx, out double lz);
                double ang = Math.Atan2(lx, lz) * 180 / Math.PI;   // 0 vorn, 90 rechts
                int h = (int)Math.Round(ang / 30);
                h = ((h % 12) + 12) % 12;
                return h == 0 ? 12 : h;
            }
            public void ToLocalDir(double dx, double dz, out double lx, out double lz) { lx = dx * Math.Cos(Psi) - dz * Math.Sin(Psi); lz = dx * Math.Sin(Psi) + dz * Math.Cos(Psi); }
            /// <summary>Am stärksten verformte Seite (−1 ohne Kontakt).</summary>
            public int MainFace()
            {
                int best = -1; double f = 0;
                for (int i = 0; i < 4; i++) if (Faces[i].PeakForce_N > f) { f = Faces[i].PeakForce_N; best = i; }
                return best;
            }
        }

        public class Walker
        {
            public string Id, Label;
            public TrafficPedestrian Def;
            public double X, Z, Vx, Vz, Radius = 0.25;
            public double FirstContact = -1, ContactSpeed;
            public int ContactBody = -1;
        }

        public class Contact
        {
            public int A, B, FaceA, FaceB;
            public double Force, Width, Px, Pz, Nx, Nz, Depth;
            public double Time;
        }

        public readonly List<Body> Bodies = new List<Body>();
        public readonly List<Walker> Walkers = new List<Walker>();
        public readonly List<Contact> Contacts = new List<Contact>();   // Kontakte dieses Schritts
        public double Time, Friction = 0.8, PlannedImpact;
        public double FirstContact = -1, LastContact = -1;
        public readonly TrafficScenario Scenario;

        public int VehicleCount { get; private set; }

        /// <summary>Szenario aufbauen: Startlagen so, dass jedes Fahrzeug ungebremst zum geplanten Zeitpunkt sein Ziel trifft.</summary>
        public TrafficSim(TrafficScenario sc, VehicleType[] types)
        {
            Scenario = sc;
            Friction = sc.friction > 0 ? sc.friction : 0.8;
            PlannedImpact = sc.approach_s > 0 ? sc.approach_s : 3;
            // Hindernisse zuerst als Ziele verfügbar
            var obstacles = new List<Body>();
            foreach (var o in sc.obstacles ?? new TrafficObstacle[0])
            {
                var b = new Body
                {
                    Id = o.id, Label = o.label, Static = true, Stiffness = o.stiffness_N_m > 0 ? o.stiffness_N_m : 5e7,
                    L = o.length_m, W = o.width_m, X = o.x, Z = o.z, Psi = o.heading_deg * Math.PI / 180, M = double.PositiveInfinity, Iz = double.PositiveInfinity
                };
                obstacles.Add(b);
            }
            // Fahrzeuge: Lage zum Treffzeitpunkt bestimmen (der Reihe nach; Ziele müssen vorher stehen)
            var aimed = new Dictionary<string, (double x, double z, double psi, double vx, double vz, double t)>();
            foreach (var o in obstacles) aimed[o.Id] = (o.X, o.Z, o.Psi, 0, 0, 0);
            var dims = new Dictionary<string, (double L, double W)>();
            foreach (var o in obstacles) dims[o.Id] = (o.L, o.W);

            foreach (var v in sc.vehicles ?? new TrafficVehicle[0])
            {
                var type = Array.Find(types, t => t.id == v.type) ?? types[0];
                double psi = v.heading_deg * Math.PI / 180, move = psi + v.drift_deg * Math.PI / 180;
                double sp = v.speed_kmh / 3.6, dx = Math.Sin(move), dz = Math.Cos(move);
                double cx = v.x, cz = v.z;
                if (!string.IsNullOrEmpty(v.target) && aimed.TryGetValue(v.target, out var tg))
                {
                    // Treffpunkt auf der Seite des Ziels zu dessen Treffzeit (Ziel fährt bis dahin geradeaus weiter)
                    var td = dims[v.target];
                    double t = v.aimTime_s - tg.t;
                    double tx = tg.x + tg.vx * t, tz = tg.z + tg.vz * t;
                    FacePoint(td.L, td.W, FaceIndex(v.targetFace), v.targetOffset_m, out double qlx, out double qlz);
                    double qx = tx + qlx * Math.Cos(tg.psi) + qlz * Math.Sin(tg.psi);
                    double qz = tz - qlx * Math.Sin(tg.psi) + qlz * Math.Cos(tg.psi);
                    // Vorderster Punkt des eigenen Quaders in Bewegungsrichtung berührt den Treffpunkt
                    double h = sp > 0.01 ? HalfExtent(psi, type.length_m, type.width_m, dx, dz) : 0;
                    cx = qx - dx * h; cz = qz - dz * h;
                }
                var b = new Body
                {
                    Id = v.id, Label = v.label, Type = type, Def = v, M = type.mass_kg, Iz = type.yawInertia_kgm2,
                    L = type.length_m, W = type.width_m, Psi = psi, Vx = sp * dx, Vz = sp * dz
                };
                double tAim = PlannedImpact + v.aimTime_s;
                b.X = cx - b.Vx * tAim; b.Z = cz - b.Vz * tAim;
                if (Math.Abs(v.drift_deg) > 0.5) b.KinematicUntil = tAim - 0.02;   // Vorgeschichte des Schleuderns nicht gerechnet
                var man = v.maneuver;
                if (sp < 0.1) { b.BrakeStart = 0; b.BrakeDecel_g = 0; }          // steht: Bremse getreten
                if (man != null)
                {
                    if (man.brake) { b.BrakeStart = Math.Max(0, tAim - man.brakeLead_s); b.BrakeDecel_g = man.brakeDecel_g; }
                    if (man.steer != 0) { b.SteerStart = Math.Max(0, tAim - man.steerLead_s); b.SteerDir = Math.Sign(man.steer); b.SteerAccel_g = man.steerAccel_g; }
                }
                aimed[v.id] = (cx, cz, psi, b.Vx, b.Vz, v.aimTime_s);
                dims[v.id] = (b.L, b.W);
                Bodies.Add(b);
            }
            VehicleCount = Bodies.Count;
            Bodies.AddRange(obstacles);

            foreach (var p in sc.pedestrians ?? new TrafficPedestrian[0])
            {
                double psi = p.heading_deg * Math.PI / 180, sp = p.speed_kmh / 3.6;
                var w = new Walker { Id = p.id, Label = p.label, Def = p, Vx = sp * Math.Sin(psi), Vz = sp * Math.Cos(psi) };
                double cx = p.x, cz = p.z;
                if (!string.IsNullOrEmpty(p.target) && aimed.TryGetValue(p.target, out var tg))
                {
                    var td = dims[p.target];
                    double t = p.aimTime_s - tg.t;
                    double tx = tg.x + tg.vx * t, tz = tg.z + tg.vz * t;
                    FacePoint(td.L, td.W, FaceIndex(p.targetFace), p.targetOffset_m, out double qlx, out double qlz);
                    // Fußgänger steht zum Treffzeitpunkt knapp vor der Seite (eigener Radius nach außen)
                    double nlx = 0, nlz = 0;
                    switch (FaceIndex(p.targetFace)) { case Front: nlz = 1; break; case Rear: nlz = -1; break; case Left: nlx = -1; break; default: nlx = 1; break; }
                    qlx += nlx * (w.Radius + 0.02); qlz += nlz * (w.Radius + 0.02);
                    cx = tx + qlx * Math.Cos(tg.psi) + qlz * Math.Sin(tg.psi);
                    cz = tz - qlx * Math.Sin(tg.psi) + qlz * Math.Cos(tg.psi);
                }
                double tAim = PlannedImpact + p.aimTime_s;
                w.X = cx - w.Vx * tAim; w.Z = cz - w.Vz * tAim;
                Walkers.Add(w);
            }
        }

        /// <summary>
        /// Punkt auf einer Seite eines Quaders (lokal): Front/Heck Versatz nach rechts, Seiten Versatz nach vorn. Nicht begrenzt —
        /// ein Versatz über die Ecke hinaus ergibt eine Teilüberdeckung (z. B. 40 % beim versetzten Frontalaufprall).
        /// </summary>
        static void FacePoint(double L, double W, int face, double offset, out double lx, out double lz)
        {
            switch (face)
            {
                case Rear: lx = offset; lz = -L / 2; break;
                case Left: lx = -W / 2; lz = offset; break;
                case Right: lx = W / 2; lz = offset; break;
                default: lx = offset; lz = L / 2; break;
            }
        }

        /// <summary>Abstand von der Mitte bis zum Rand des Quaders in Richtung (dx, dz).</summary>
        static double HalfExtent(double psi, double L, double W, double dx, double dz)
        {
            double fx = Math.Sin(psi), fz = Math.Cos(psi), rx = Math.Cos(psi), rz = -Math.Sin(psi);
            double along = Math.Abs(dx * fx + dz * fz), across = Math.Abs(dx * rx + dz * rz);
            double a = along > 1e-9 ? L / 2 / along : double.PositiveInfinity, b = across > 1e-9 ? W / 2 / across : double.PositiveInfinity;
            return Math.Min(a, b);
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        // ------------------------------------------------------------------ Schritt

        public void Step(double dt)
        {
            Contacts.Clear();
            foreach (var b in Bodies) { b.Fx = b.Fz = b.Tau = 0; if (!b.Static) foreach (var f in b.Faces) f.Crush = 0; }
            for (int i = 0; i < VehicleCount; i++) Tires(Bodies[i]);
            for (int i = 0; i < Bodies.Count; i++)
                for (int j = i + 1; j < Bodies.Count; j++)
                    if (!(Bodies[i].Static && Bodies[j].Static)) Collide(i, j, dt);

            for (int i = 0; i < VehicleCount; i++)
            {
                var b = Bodies[i];
                if (Time < b.KinematicUntil && b.FirstContact < 0) { b.Fx = b.Fz = b.Tau = 0; }
                b.Vx += b.Fx / b.M * dt; b.Vz += b.Fz / b.M * dt; b.R += b.Tau / b.Iz * dt;
                b.X += b.Vx * dt; b.Z += b.Vz * dt; b.Psi += b.R * dt;
                if (b.FirstContact >= 0) b.MaxDeltaV = Math.Max(b.MaxDeltaV, Math.Sqrt(b.PdofX * b.PdofX + b.PdofZ * b.PdofZ) / b.M);
                // Innenraum-Eindringen folgt der Verformung verzögert
                double lag = b.Type.doorLag_s > 0 ? b.Type.doorLag_s : 0.003, k = Math.Min(1, dt / lag);
                for (int f = 0; f < 4; f++)
                {
                    var fs = b.Faces[f];
                    double target = f == Front ? Math.Min(b.Type.maxFrontIntrusion_m, Math.Max(0, fs.MaxCrush - b.Type.intrusionStart_m) * b.Type.intrusionPerCrush)
                                  : f == Rear ? 0
                                  : Math.Min(b.Type.maxSideIntrusion_m, fs.MaxCrush);
                    fs.Intrusion += (target - fs.Intrusion) * k;
                }
            }
            foreach (var w in Walkers)
            {
                w.X += w.Vx * dt; w.Z += w.Vz * dt;
                if (w.FirstContact >= 0) continue;
                for (int i = 0; i < VehicleCount; i++)
                    if (CircleOverlaps(Bodies[i], w.X, w.Z, w.Radius)) { w.FirstContact = Time; w.ContactBody = i; w.ContactSpeed = Bodies[i].Speed; NoteContact(Time); break; }
            }
            Time += dt;
        }

        void NoteContact(double t)
        {
            if (FirstContact < 0) FirstContact = t;
            LastContact = t;
        }

        static bool CircleOverlaps(Body b, double x, double z, double r)
        {
            b.ToLocal(x, z, out double lx, out double lz);
            double cx = Clamp(lx, -b.W / 2, b.W / 2), cz = Clamp(lz, -b.L / 2, b.L / 2);
            double dx = lx - cx, dz = lz - cz;
            return dx * dx + dz * dz < r * r;
        }

        // ------------------------------------------------------------------ Reifen und Fahrer

        void Tires(Body b)
        {
            var t = b.Type;
            b.Forward(out double fx, out double fz);
            b.RightDir(out double rx, out double rz);
            double vx = b.Vx * fx + b.Vz * fz, vy = b.Vx * rx + b.Vz * rz;
            double mu = Friction, N = b.M * G, Naxle = N / 2;
            // Bremse: Aufbau in 0,2 s, mit ABS höchstens µ·g
            double brake = 0;
            bool steering = Time >= b.SteerStart && b.SteerDir != 0;
            double steerG = (b.SteerAccel_g > 0 ? b.SteerAccel_g : 0.7 * mu);
            if (Time >= b.BrakeStart)
            {
                // Mit ABS bleibt das Fahrzeug lenkbar: beim Ausweichen nur so viel Bremskraft, wie der Kammsche Kreis neben der Querkraft zulässt
                double maxG = steering ? Math.Sqrt(Math.Max(0, mu * mu - steerG * steerG)) : mu;
                double target = (b.BrakeDecel_g > 0 ? Math.Min(b.BrakeDecel_g, maxG) : maxG) * G * b.M;
                brake = target * Math.Min(1, (Time - b.BrakeStart) / 0.2);
                if (b.BrakeStart <= 0) brake = target;
            }
            b.BrakeForce = brake;
            double a = t.wheelbase_m / 2, bb = t.wheelbase_m / 2;
            // Lenkung: Lenkwinkel aus der gewünschten Querbeschleunigung (Ackermann), Aufbau in 0,3 s
            double delta = 0;
            if (steering && Math.Abs(vx) > 2)
            {
                double ay = steerG * G * b.SteerDir * Math.Min(1, (Time - b.SteerStart) / 0.3);
                delta = Clamp(t.wheelbase_m * ay / (vx * vx), -0.5, 0.5);
            }
            b.Delta = delta;
            double sgn = Clamp(vx / 0.3, -1, 1);
            double Fx = -sgn * (brake + RollingResistance * N);
            double ux = Math.Max(Math.Abs(vx), 0.5);
            double af = Math.Atan2(vy + b.R * a, ux) - delta * Math.Sign(vx);
            double ar = Math.Atan2(vy - b.R * bb, ux);
            double C = t.corneringStiffness_N_rad;
            double fyMaxF = Math.Sqrt(Math.Max(0, (mu * Naxle) * (mu * Naxle) - (Fx / 2) * (Fx / 2)));
            double fyMaxR = fyMaxF;
            double Fyf = Clamp(-C * af, -fyMaxF, fyMaxF), Fyr = Clamp(-C * ar, -fyMaxR, fyMaxR);
            b.Fx += Fx * fx + (Fyf + Fyr) * rx;
            b.Fz += Fx * fz + (Fyf + Fyr) * rz;
            b.Tau += a * Fyf - bb * Fyr;
        }

        // ------------------------------------------------------------------ Kollision

        static void Corners(Body b, double[] xs, double[] zs)
        {
            double hw = b.W / 2, hl = b.L / 2;
            b.ToWorld(-hw, hl, out xs[0], out zs[0]);
            b.ToWorld(hw, hl, out xs[1], out zs[1]);
            b.ToWorld(hw, -hl, out xs[2], out zs[2]);
            b.ToWorld(-hw, -hl, out xs[3], out zs[3]);
        }

        readonly double[] _ax = new double[4], _az = new double[4], _bx = new double[4], _bz = new double[4];

        /// <summary>
        /// Laufender Kontakt eines Paares: Richtung (im System von A) und getroffene Seiten bleiben, bis sich die Quader trennen.
        /// Sonst kippt das Trennachsen-Verfahren bei tiefer Verformung auf eine andere Achse (z. B. beim versetzten Frontalaufprall
        /// quer statt längs), und die Fahrzeuge glitten durcheinander hindurch.
        /// </summary>
        class PairState { public double Nlx, Nlz; public int FaceA, FaceB; }
        readonly Dictionary<long, PairState> _pairs = new Dictionary<long, PairState>();

        void Collide(int ia, int ib, double dt)
        {
            Body A = Bodies[ia], B = Bodies[ib];
            double dcx = B.X - A.X, dcz = B.Z - A.Z, rA = 0.5 * Math.Sqrt(A.L * A.L + A.W * A.W), rB = 0.5 * Math.Sqrt(B.L * B.L + B.W * B.W);
            if (dcx * dcx + dcz * dcz > (rA + rB) * (rA + rB)) return;
            Corners(A, _ax, _az); Corners(B, _bx, _bz);
            long key = (long)ia * 4096 + ib;
            // Trennachsen: Kanten-Normalen beider Quader
            double best = double.PositiveInfinity, nx = 0, nz = 0;
            for (int k = 0; k < 4; k++)
            {
                Body src = k < 2 ? A : B;
                double ux, uz;
                if (k % 2 == 0) src.Forward(out ux, out uz); else src.RightDir(out ux, out uz);
                Project(_ax, _az, ux, uz, out double a0, out double a1);
                Project(_bx, _bz, ux, uz, out double b0, out double b1);
                double ov = Math.Min(a1 - b0, b1 - a0);
                if (ov <= 0) { _pairs.Remove(key); return; }   // getrennt
                if (ov < best) { best = ov; nx = ux; nz = uz; }
            }
            if (dcx * nx + dcz * nz < 0) { nx = -nx; nz = -nz; }   // von A nach B
            int faceA, faceB;
            if (_pairs.TryGetValue(key, out var pair))
            {
                // laufender Kontakt: alte Richtung (mit A gedreht), Tiefe längs dieser Richtung
                double cs = Math.Cos(A.Psi), sn = Math.Sin(A.Psi);
                nx = pair.Nlx * cs + pair.Nlz * sn; nz = -pair.Nlx * sn + pair.Nlz * cs;
                Project(_ax, _az, nx, nz, out double a0, out double a1);
                Project(_bx, _bz, nx, nz, out double b0, out double b1);
                best = a1 - b0;
                if (best <= 0) { _pairs.Remove(key); return; }
                faceA = pair.FaceA; faceB = pair.FaceB;
            }
            else
            {
                faceA = HitFace(A, nx, nz); faceB = HitFace(B, -nx, -nz);
                A.ToLocalDir(nx, nz, out double lx, out double lz);
                _pairs[key] = new PairState { Nlx = lx, Nlz = lz, FaceA = faceA, FaceB = faceB };
            }
            // Überdeckungsfläche: Schnittvieleck → Mitte und Breite quer zur Normalen
            var poly = Clip(_ax, _az, _bx, _bz);
            if (poly.Count < 3) return;
            double px = 0, pz = 0;
            foreach (var p in poly) { px += p.x; pz += p.z; }
            px /= poly.Count; pz /= poly.Count;
            double tx = -nz, tz = nx, tmin = double.PositiveInfinity, tmax = double.NegativeInfinity;
            foreach (var p in poly) { double s = p.x * tx + p.z * tz; tmin = Math.Min(tmin, s); tmax = Math.Max(tmax, s); }
            double width = tmax - tmin, depth = best;

            double force = SeriesForce(A, faceA, B, faceB, depth, width, out double cA, out double cB);
            if (force <= 0) return;
            // Zustand der Seiten fortschreiben (plastisch)
            Update(A, faceA, cA, width, px, pz, force);
            Update(B, faceB, cB, width, px, pz, force);

            // Reibung zwischen den Karosserien (Gleiten entlang der Kontaktfläche)
            A.PointVelocity(px, pz, out double vax, out double vaz);
            B.PointVelocity(px, pz, out double vbx, out double vbz);
            double rvx = vbx - vax, rvz = vbz - vaz, vt = rvx * tx + rvz * tz;
            double ft = -VehicleFriction * force * vt / Math.Sqrt(vt * vt + 0.05 * 0.05);
            double Fx = force * nx + ft * tx, Fz = force * nz + ft * tz;   // auf B
            Apply(B, Fx, Fz, px, pz);
            Apply(A, -Fx, -Fz, px, pz);
            foreach (var body in new[] { A, B })
            {
                if (body.Static) continue;
                if (body.FirstContact < 0) { body.FirstContact = Time; body.PreVx = body.Vx; body.PreVz = body.Vz; }
                body.LastContact = Time;
            }
            A.PdofX -= Fx * dt; A.PdofZ -= Fz * dt; B.PdofX += Fx * dt; B.PdofZ += Fz * dt;
            NoteContact(Time);
            Contacts.Add(new Contact { A = ia, B = ib, FaceA = faceA, FaceB = faceB, Force = force, Width = width, Px = px, Pz = pz, Nx = nx, Nz = nz, Depth = depth, Time = Time });
        }

        static void Apply(Body b, double fx, double fz, double px, double pz)
        {
            if (b.Static) return;
            b.Fx += fx; b.Fz += fz;
            b.Tau += (pz - b.Z) * fx - (px - b.X) * fz;
        }

        /// <summary>Seite, deren Außennormale am besten zu (nx, nz) passt.</summary>
        static int HitFace(Body b, double nx, double nz)
        {
            b.ToLocalDir(nx, nz, out double lx, out double lz);
            if (Math.Abs(lz) >= Math.Abs(lx)) return lz > 0 ? Front : Rear;
            return lx > 0 ? Right : Left;
        }

        static double FaceWidth(Body b, int face) => face == Front || face == Rear ? b.W : b.L;

        static FaceCurve Curve(Body b, int face) => face == Front ? b.Type.front : face == Rear ? b.Type.rear : b.Type.side;

        /// <summary>Anteil der Kennlinie, der bei dieser Kontaktbreite trägt.</summary>
        static double WidthFactor(Body b, int face, double width)
        {
            double fw = FaceWidth(b, face), min = b.Type.minContactWidth_m > 0 ? b.Type.minContactWidth_m : 0.6;
            double rw = Curve(b, face).refWidth_m > 0 ? Curve(b, face).refWidth_m : fw;
            return Clamp(Math.Max(width, min) / rw, 0.05, fw / rw);
        }

        /// <summary>Verformung einer Seite, die die Kraft F trägt (Belasten auf der Kennlinie, Entlasten steif).</summary>
        static double DeflectionAt(Body b, int face, double F, double wf)
        {
            if (b.Static) return F / b.Stiffness;
            var fs = b.Faces[face];
            var curve = Curve(b, face);
            double fc = F / wf;   // in Einheiten der vollen Breite
            if (fc <= fs.MaxCurveForce && fs.MaxCrush > 0)
            {
                double kU = Math.Max(1, b.Type.unloadFactor) * fs.MaxCurveForce / fs.MaxCrush;
                return fs.MaxCrush - (fs.MaxCurveForce - fc) / kU;
            }
            return curve.Deflection(fc);
        }

        /// <summary>Kraft, bei der beide Seiten zusammen die Überdeckung aufnehmen (Halbierungsverfahren).</summary>
        static double SeriesForce(Body A, int fa, Body B, int fb, double depth, double width, out double cA, out double cB)
        {
            double wa = A.Static ? 1 : WidthFactor(A, fa, width), wb = B.Static ? 1 : WidthFactor(B, fb, width);
            cA = DeflectionAt(A, fa, 0, wa); cB = DeflectionAt(B, fb, 0, wb);
            if (cA + cB >= depth) return 0;   // nur bleibende Verformung, kein Druck
            double lo = 0, hi = 1e5;
            while (DeflectionAt(A, fa, hi, wa) + DeflectionAt(B, fb, hi, wb) < depth && hi < 1e9) hi *= 2;
            for (int it = 0; it < 50; it++)
            {
                double mid = 0.5 * (lo + hi);
                if (DeflectionAt(A, fa, mid, wa) + DeflectionAt(B, fb, mid, wb) < depth) lo = mid; else hi = mid;
            }
            double F = 0.5 * (lo + hi);
            cA = DeflectionAt(A, fa, F, wa); cB = DeflectionAt(B, fb, F, wb);
            return F;
        }

        static void Update(Body b, int face, double c, double width, double px, double pz, double force)
        {
            if (b.Static) return;
            var fs = b.Faces[face];
            fs.Crush = Math.Max(fs.Crush, c);
            fs.PeakForce_N = Math.Max(fs.PeakForce_N, force);
            if (c > fs.MaxCrush)
            {
                fs.MaxCrush = c;
                fs.MaxCurveForce = Curve(b, face).Force(c);
                b.ToLocal(px, pz, out double lx, out double lz);
                fs.Position = face == Front || face == Rear ? lx : lz;
            }
        }

        static void Project(double[] xs, double[] zs, double ux, double uz, out double lo, out double hi)
        {
            lo = double.PositiveInfinity; hi = double.NegativeInfinity;
            for (int i = 0; i < 4; i++) { double s = xs[i] * ux + zs[i] * uz; if (s < lo) lo = s; if (s > hi) hi = s; }
        }

        struct P2 { public double x, z; public P2(double x, double z) { this.x = x; this.z = z; } }

        /// <summary>Schnitt zweier konvexer Vierecke (Sutherland–Hodgman).</summary>
        static List<P2> Clip(double[] ax, double[] az, double[] bx, double[] bz)
        {
            var poly = new List<P2>();
            for (int i = 0; i < 4; i++) poly.Add(new P2(ax[i], az[i]));
            // Umlaufsinn von B bestimmen
            double area = 0;
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; area += bx[i] * bz[j] - bx[j] * bz[i]; }
            double s = area >= 0 ? 1 : -1;
            for (int e = 0; e < 4 && poly.Count > 0; e++)
            {
                int f = (e + 1) % 4;
                double ex = bx[f] - bx[e], ez = bz[f] - bz[e];
                var input = poly; poly = new List<P2>();
                for (int i = 0; i < input.Count; i++)
                {
                    var cur = input[i]; var prev = input[(i + input.Count - 1) % input.Count];
                    double dc = s * (ex * (cur.z - bz[e]) - ez * (cur.x - bx[e]));
                    double dp = s * (ex * (prev.z - bz[e]) - ez * (prev.x - bx[e]));
                    if (dc >= 0)
                    {
                        if (dp < 0) poly.Add(Lerp(prev, cur, dp / (dp - dc)));
                        poly.Add(cur);
                    }
                    else if (dp >= 0) poly.Add(Lerp(prev, cur, dp / (dp - dc)));
                }
            }
            return poly;
        }

        static P2 Lerp(P2 a, P2 b, double t) => new P2(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);

        // ------------------------------------------------------------------ Vorausrechnung

        /// <summary>Ergebnis einer Vorausrechnung (für Zeitplan und Vorschau).</summary>
        public class Prediction
        {
            public double FirstContact = -1, LastContact = -1;
            public readonly List<double[]> Paths = new List<double[]>();   // je Fahrzeug x0,z0,x1,z1,… alle 0,1 s
            public readonly List<double[]> WalkerPaths = new List<double[]>();
            public double[] DeltaV_kmh, Clock;
            public double[] WalkerContact;
        }

        /// <summary>Szenario ohne Unity bis <paramref name="until_s"/> rechnen (feiner Schritt nur rund um Kontakte).</summary>
        public static Prediction Predict(TrafficScenario sc, VehicleType[] types, double until_s = 0)
        {
            var sim = new TrafficSim(sc, types);
            double end = until_s > 0 ? until_s : sim.PlannedImpact + 2.5;
            var pr = new Prediction();
            var paths = new List<List<double>>();
            for (int i = 0; i < sim.VehicleCount; i++) paths.Add(new List<double>());
            var wpaths = new List<List<double>>();
            foreach (var _ in sim.Walkers) wpaths.Add(new List<double>());
            double nextSample = 0;
            while (sim.Time < end)
            {
                if (sim.Time >= nextSample - 1e-9)
                {
                    for (int i = 0; i < sim.VehicleCount; i++) { paths[i].Add(sim.Bodies[i].X); paths[i].Add(sim.Bodies[i].Z); }
                    for (int i = 0; i < sim.Walkers.Count; i++) { wpaths[i].Add(sim.Walkers[i].X); wpaths[i].Add(sim.Walkers[i].Z); }
                    nextSample += 0.1;
                }
                bool near = sim.Contacts.Count > 0 || sim.AnyClose(0.6);
                sim.Step(near ? 1e-4 : 1e-3);
                if (sim.LastContact >= 0 && sim.Time > sim.LastContact + 1.5 && sim.AllSlow()) break;
            }
            pr.FirstContact = sim.FirstContact; pr.LastContact = sim.LastContact;
            foreach (var p in paths) pr.Paths.Add(p.ToArray());
            foreach (var p in wpaths) pr.WalkerPaths.Add(p.ToArray());
            pr.DeltaV_kmh = new double[sim.VehicleCount]; pr.Clock = new double[sim.VehicleCount];
            for (int i = 0; i < sim.VehicleCount; i++) { pr.DeltaV_kmh[i] = sim.Bodies[i].MaxDeltaV * 3.6; pr.Clock[i] = sim.Bodies[i].PdofClock(); }
            pr.WalkerContact = new double[sim.Walkers.Count];
            for (int i = 0; i < sim.Walkers.Count; i++) pr.WalkerContact[i] = sim.Walkers[i].FirstContact;
            return pr;
        }

        /// <summary>Sind zwei Körper (oder Fußgänger und Fahrzeug) einander näher als <paramref name="margin"/> (grob, Hüllkreise)?</summary>
        public bool AnyClose(double margin)
        {
            for (int i = 0; i < Bodies.Count; i++)
            {
                var a = Bodies[i];
                double ra = 0.5 * Math.Sqrt(a.L * a.L + a.W * a.W);
                for (int j = i + 1; j < Bodies.Count; j++)
                {
                    var b = Bodies[j];
                    if (a.Static && b.Static) continue;
                    double rb = 0.5 * Math.Sqrt(b.L * b.L + b.W * b.W), dx = a.X - b.X, dz = a.Z - b.Z, r = ra + rb + margin;
                    if (dx * dx + dz * dz < r * r && GapBetween(a, b) < margin) return true;
                }
            }
            return false;
        }

        /// <summary>Grober Abstand zweier Quader (Trennachsen; negativ bei Überdeckung).</summary>
        double GapBetween(Body A, Body B)
        {
            Corners(A, _ax, _az); Corners(B, _bx, _bz);
            double gap = double.NegativeInfinity;
            for (int k = 0; k < 4; k++)
            {
                Body src = k < 2 ? A : B;
                double ux, uz;
                if (k % 2 == 0) src.Forward(out ux, out uz); else src.RightDir(out ux, out uz);
                Project(_ax, _az, ux, uz, out double a0, out double a1);
                Project(_bx, _bz, ux, uz, out double b0, out double b1);
                gap = Math.Max(gap, Math.Max(b0 - a1, a0 - b1));
            }
            return gap;
        }

        public bool AllSlow()
        {
            for (int i = 0; i < VehicleCount; i++) if (Bodies[i].Speed > 0.5) return false;
            return true;
        }
    }
}

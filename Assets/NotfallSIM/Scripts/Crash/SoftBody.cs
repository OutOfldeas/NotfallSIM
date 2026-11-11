using System;
using System.Collections.Generic;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Verformbarer Körper aus Massepunkten und Balken (Feder-Dämpfer mit Fließgrenze): Jeder Balken trägt elastisch bis zu
    /// seiner Fließkraft, darüber verformt er sich bleibend (ideal plastisch, getrennt für Zug und Druck) und reißt ab einer
    /// bleibenden Dehnung. Stark gestauchte Balken blockieren (Material liegt aufeinander). Daraus entstehen Knautschen,
    /// Falten und Abreißen ohne vorgegebene Form — das Prinzip von Soft-Body-Fahrzeugsimulationen.
    /// Rein rechnerisch (kein Unity), explizit integriert (halb-implizites Euler-Verfahren); der Zeitschritt muss klein gegen
    /// die Eigenschwingung der steifsten Balken sein (siehe <see cref="StableStep"/>).
    /// </summary>
    public class SoftBody
    {
        // Punkte
        public int Count;
        public double[] X, Y, Z, Vx, Vy, Vz, M, X0, Y0, Z0;
        double[] _fx, _fy, _fz;
        // Balken
        public int Beams;
        public int[] A, B;
        public double[] L0, Lp, K, C, YieldC, YieldT, BreakStrain;
        public bool[] Broken;
        /// <summary>Gruppe des Balkens (frei belegbar, z. B. Zone des Fahrzeugs).</summary>
        public int[] Group;

        /// <summary>Starre Wand: Punkte mit z &gt; WallZ (und x im Bereich, y unter der Höhe) werden angehalten.</summary>
        public double WallZ = double.PositiveInfinity, WallXMin = double.NegativeInfinity, WallXMax = double.PositiveInfinity, WallHeight = double.PositiveInfinity;
        public double WallFriction = 0.3;
        /// <summary>Fahrbahn: Punkte bleiben über dieser Höhe (ohne Reibung; −∞ = kein Boden).</summary>
        public double GroundY = double.NegativeInfinity;
        /// <summary>Kraft auf die Wand im letzten Schritt (N) und aufgenommene Arbeit der Balken (J, bleibend).</summary>
        public double WallForce, PlasticWork;
        /// <summary>Unter diesem Anteil der Ausgangslänge blockiert ein Balken.</summary>
        public double LockRatio = 0.25;

        readonly List<double[]> _nodes = new List<double[]>();
        readonly List<double[]> _beams = new List<double[]>();
        readonly List<int[]> _beamIdx = new List<int[]>();

        public int AddNode(double x, double y, double z, double mass)
        {
            _nodes.Add(new[] { x, y, z, mass });
            return _nodes.Count - 1;
        }

        /// <summary>Balken zwischen a und b: Fließkraft (N) für Druck und Zug, Fließdehnung (legt die Steifigkeit fest), Bruchdehnung.</summary>
        public void AddBeam(int a, int b, double yieldC_N, double yieldT_N, double yieldStrain = 0.02, double breakStrain = 0.6, int group = 0)
        {
            if (a == b) return;
            _beamIdx.Add(new[] { a, b, group });
            _beams.Add(new[] { yieldC_N, yieldT_N, yieldStrain, breakStrain });
        }

        /// <summary>Aufbau abschließen: Felder anlegen, Steifigkeit und Dämpfung je Balken bestimmen.</summary>
        public void Build(double dampingRatio = 0.05)
        {
            Count = _nodes.Count;
            X = new double[Count]; Y = new double[Count]; Z = new double[Count]; M = new double[Count];
            Vx = new double[Count]; Vy = new double[Count]; Vz = new double[Count];
            X0 = new double[Count]; Y0 = new double[Count]; Z0 = new double[Count];
            _fx = new double[Count]; _fy = new double[Count]; _fz = new double[Count];
            for (int i = 0; i < Count; i++) { X[i] = X0[i] = _nodes[i][0]; Y[i] = Y0[i] = _nodes[i][1]; Z[i] = Z0[i] = _nodes[i][2]; M[i] = _nodes[i][3]; }
            Beams = _beams.Count;
            A = new int[Beams]; B = new int[Beams]; Group = new int[Beams];
            L0 = new double[Beams]; Lp = new double[Beams]; K = new double[Beams]; C = new double[Beams];
            YieldC = new double[Beams]; YieldT = new double[Beams]; BreakStrain = new double[Beams]; Broken = new bool[Beams];
            for (int i = 0; i < Beams; i++)
            {
                int a = A[i] = _beamIdx[i][0], b = B[i] = _beamIdx[i][1];
                Group[i] = _beamIdx[i][2];
                double dx = X[b] - X[a], dy = Y[b] - Y[a], dz = Z[b] - Z[a];
                double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                L0[i] = Lp[i] = Math.Max(l, 1e-4);
                YieldC[i] = _beams[i][0]; YieldT[i] = _beams[i][1]; BreakStrain[i] = _beams[i][3];
                K[i] = Math.Max(YieldC[i], YieldT[i]) / (_beams[i][2] * L0[i]);
                C[i] = 2 * dampingRatio * Math.Sqrt(K[i] * Math.Min(M[a], M[b]));
            }
        }

        /// <summary>Größter stabiler Zeitschritt (s) mit Sicherheitsfaktor: klein gegen die schnellste Eigenschwingung eines Punkts.</summary>
        public double StableStep(double safety = 0.25)
        {
            var sumK = new double[Count];
            for (int i = 0; i < Beams; i++) { sumK[A[i]] += K[i] * 6; sumK[B[i]] += K[i] * 6; }   // ×6: Blockieren (5·K) eingerechnet
            double w = 0;
            for (int i = 0; i < Count; i++) if (sumK[i] > 0) w = Math.Max(w, Math.Sqrt(sumK[i] / M[i]));
            return w > 0 ? safety * 2 / w : 1e-3;
        }

        public void SetVelocity(double vx, double vy, double vz)
        {
            for (int i = 0; i < Count; i++) { Vx[i] = vx; Vy[i] = vy; Vz[i] = vz; }
        }

        public void Step(double dt)
        {
            Array.Clear(_fx, 0, Count); Array.Clear(_fy, 0, Count); Array.Clear(_fz, 0, Count);
            for (int i = 0; i < Beams; i++)
            {
                if (Broken[i]) continue;
                int a = A[i], b = B[i];
                double dx = X[b] - X[a], dy = Y[b] - Y[a], dz = Z[b] - Z[a];
                double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (l < 1e-9) continue;
                double nx = dx / l, ny = dy / l, nz = dz / l;
                double f = K[i] * (l - Lp[i]);   // > 0 Zug
                if (f > YieldT[i])
                {
                    double lp = l - YieldT[i] / K[i];
                    PlasticWork += YieldT[i] * (lp - Lp[i]);
                    Lp[i] = lp; f = YieldT[i];
                    if ((Lp[i] - L0[i]) / L0[i] > BreakStrain[i]) { Broken[i] = true; continue; }
                }
                else if (f < -YieldC[i])
                {
                    double lp = l + YieldC[i] / K[i];
                    PlasticWork += YieldC[i] * (Lp[i] - lp);
                    Lp[i] = lp; f = -YieldC[i];
                }
                // Dämpfung nur innerhalb der Fließgrenzen (sonst trüge der Balken geschwindigkeitsabhängig mehr als seine Fließkraft)
                double rel = (Vx[b] - Vx[a]) * nx + (Vy[b] - Vy[a]) * ny + (Vz[b] - Vz[a]) * nz;
                f = Math.Max(-YieldC[i], Math.Min(YieldT[i], f + C[i] * rel));
                double lockAt = LockRatio * L0[i];
                if (l < lockAt) f -= 5 * K[i] * (lockAt - l);
                _fx[a] += f * nx; _fy[a] += f * ny; _fz[a] += f * nz;
                _fx[b] -= f * nx; _fy[b] -= f * ny; _fz[b] -= f * nz;
            }
            double wall = 0;
            for (int i = 0; i < Count; i++)
            {
                double im = 1 / M[i];
                Vx[i] += _fx[i] * im * dt; Vy[i] += _fy[i] * im * dt; Vz[i] += _fz[i] * im * dt;
                X[i] += Vx[i] * dt; Y[i] += Vy[i] * dt; Z[i] += Vz[i] * dt;
                if (Y[i] < GroundY) { Y[i] = GroundY; if (Vy[i] < 0) Vy[i] = 0; }
                if (Z[i] > WallZ && X[i] >= WallXMin && X[i] <= WallXMax && Y[i] <= WallHeight)
                {
                    Z[i] = WallZ;
                    if (Vz[i] > 0)
                    {
                        double jn = M[i] * Vz[i];
                        wall += jn / dt;
                        Vz[i] = 0;
                        // Reibung an der Wand (Coulomb, über den Normalimpuls)
                        double vt = Math.Sqrt(Vx[i] * Vx[i] + Vy[i] * Vy[i]);
                        if (vt > 1e-9)
                        {
                            double s = Math.Max(0, 1 - WallFriction * jn / (M[i] * vt));
                            Vx[i] *= s; Vy[i] *= s;
                        }
                    }
                }
            }
            WallForce = wall;
        }

        public double KineticEnergy()
        {
            double e = 0;
            for (int i = 0; i < Count; i++) e += 0.5 * M[i] * (Vx[i] * Vx[i] + Vy[i] * Vy[i] + Vz[i] * Vz[i]);
            return e;
        }

        public int BrokenCount() { int n = 0; for (int i = 0; i < Beams; i++) if (Broken[i]) n++; return n; }
    }

    /// <summary>Stellgrößen des Fahrzeuggerüsts. Fließkräfte in N je Balken; Werte für den PKW an der Front-Kennlinie eingestellt.</summary>
    public class SoftCarParams
    {
        /// <summary>Knautschzone vorn: Fließkraft der Blech-Balken; Längsträger tragen das <see cref="RailFactor"/>-Fache.</summary>
        public double FrontYield_N = 21000, RearYield_N = 7000, CabinYield_N = 120000, RailFactor = 4;
        /// <summary>Zug hält mehr aus als Druck (Blech knickt unter Druck, reißt erst spät).</summary>
        public double TensionFactor = 2.0;
        public double EngineMass_kg = 250, EngineYield_N = 600000, MountYield_N = 15000;
        /// <summary>Streben vom Motorblock zur Spritzwand: tragen, sobald der Motor an der Wand steht (zweite Stufe der Kennlinie).</summary>
        public double FirewallStrutYield_N = 150000;
        /// <summary>Abstand Stoßfänger–Motorblock und Länge des Blocks (m): ab diesem Knautschweg steht der Motor an der Wand.</summary>
        public double EngineGap_m = 0.60, EngineLength_m = 0.40;
        public double StationSpacing_m = 0.2;
    }

    /// <summary>
    /// PKW als Punkt-Balken-Gerüst aus dem Umriss (<see cref="CarProfile"/>): je Station zehn Punkte (Schweller, Gürtellinie,
    /// Dach, Bodenmitte, zwei Längsträger), dazwischen Längs-, Quer- und Diagonalbalken. Vorn und hinten weiche Knautschzonen,
    /// dazwischen die steife Fahrgastzelle; der Motorblock ist ein schwerer, steifer Würfel vor der Spritzwand.
    /// Vereinfachungen des Prototyps: keine Schwerkraft/Räder/Aufhängung, Teile durchdringen einander nicht-kollidierend
    /// (nur das Blockieren gestauchter Balken begrenzt), Insassen fehlen.
    /// </summary>
    public class SoftCar
    {
        public const int PerStation = 10;
        public const int GroupFront = 1, GroupCabin = 2, GroupRear = 3, GroupEngine = 4;
        public SoftBody Body;
        public CarProfile Profile;
        public int Stations;
        public double[] StationZ;
        /// <summary>Erste und letzte Station der Fahrgastzelle (Spritzwand, Heckwand) und die Motorpunkte.</summary>
        public int CowlStation, DeckStation;
        public List<int> EngineNodes = new List<int>();
        double _cabinZ0, _cabinLen0;

        public int Node(int station, int k) => station * PerStation + k;

        public static SoftCar Build(CarProfile p, double mass_kg, SoftCarParams prm = null)
        {
            prm = prm ?? new SoftCarParams();
            var car = new SoftCar { Profile = p, Body = new SoftBody() };
            var b = car.Body;
            double L = p.Length, hwBase = p.Width / 2;
            int n = Math.Max(8, (int)Math.Round(L / prm.StationSpacing_m));
            car.Stations = n + 1;
            car.StationZ = new double[n + 1];
            double structMass = mass_kg - prm.EngineMass_kg;
            double nodeMass = structMass / ((n + 1) * PerStation);
            for (int s = 0; s <= n; s++)
            {
                double z = -L / 2 + L * s / n;
                car.StationZ[s] = z;
                float top = p.TopAt((float)z);
                double end = Math.Max(0, Math.Min(1, (Math.Abs(z) - (L / 2 - 0.5)) / 0.5));
                double hw = hwBase * (1 - 0.07 * end);
                double belt = Math.Min(p.Belt, top - 0.08);
                double upper = Math.Max(0.1, p.RoofY - belt);
                double hwTop = hw * (1 - 0.16 * Math.Max(0, Math.Min(1, (top - belt) / upper)));
                double sill = p.Sill;
                b.AddNode(-hw, sill, z, nodeMass);            // 0 Schweller links
                b.AddNode(-hw, belt, z, nodeMass);            // 1 Gürtellinie links
                b.AddNode(-hwTop, top - 0.03, z, nodeMass);   // 2 Dachkante links
                b.AddNode(0, top, z, nodeMass);               // 3 Dachmitte / Haube
                b.AddNode(hwTop, top - 0.03, z, nodeMass);    // 4 Dachkante rechts
                b.AddNode(hw, belt, z, nodeMass);             // 5 Gürtellinie rechts
                b.AddNode(hw, sill, z, nodeMass);             // 6 Schweller rechts
                b.AddNode(0, sill, z, nodeMass);              // 7 Bodenmitte
                b.AddNode(-0.5 * hw, sill + 0.22, z, nodeMass);   // 8 Längsträger links
                b.AddNode(0.5 * hw, sill + 0.22, z, nodeMass);    // 9 Längsträger rechts
            }
            // Zonen
            car.CowlStation = 0; car.DeckStation = 0;
            for (int s = 0; s <= n; s++)
            {
                if (car.StationZ[s] <= p.CowlZ + 1e-6) car.CowlStation = s;
                if (car.StationZ[s] <= p.DeckZ + 1e-6) car.DeckStation = s;
            }
            Func<int, int> zoneOfBay = s => s >= car.CowlStation ? GroupFront : s < car.DeckStation ? GroupRear : GroupCabin;   // Feld zwischen s und s+1
            Func<int, double> yieldOf = g => g == GroupFront ? prm.FrontYield_N : g == GroupRear ? prm.RearYield_N : prm.CabinYield_N;
            Action<int, int, double, int> beam = (i, j, fy, g) => b.AddBeam(i, j, fy, fy * prm.TensionFactor, 0.02, 0.6, g);

            int[][] ring = { new[] { 0, 1 }, new[] { 1, 2 }, new[] { 2, 3 }, new[] { 3, 4 }, new[] { 4, 5 }, new[] { 5, 6 }, new[] { 6, 7 }, new[] { 7, 0 } };
            int[][] cross = { new[] { 8, 0 }, new[] { 8, 7 }, new[] { 8, 1 }, new[] { 9, 6 }, new[] { 9, 7 }, new[] { 9, 5 }, new[] { 8, 9 }, new[] { 8, 3 }, new[] { 9, 3 }, new[] { 1, 5 }, new[] { 0, 2 }, new[] { 6, 4 } };
            for (int s = 0; s <= n; s++)
            {
                // Querschnitt (Spant): Zone der Station = die steifere der angrenzenden Felder
                int g = s == 0 ? zoneOfBay(0) : s == n ? zoneOfBay(n - 1) : (zoneOfBay(s) == GroupCabin || zoneOfBay(s - 1) == GroupCabin ? GroupCabin : zoneOfBay(s));
                double fy = yieldOf(g);
                foreach (var e in ring) beam(car.Node(s, e[0]), car.Node(s, e[1]), fy, g);
                foreach (var e in cross) beam(car.Node(s, e[0]), car.Node(s, e[1]), fy, g);
            }
            for (int s = 0; s < n; s++)
            {
                int g = zoneOfBay(s);
                double fy = yieldOf(g);
                for (int k = 0; k < PerStation; k++)
                    beam(car.Node(s, k), car.Node(s + 1, k), fy * (k >= 8 ? prm.RailFactor : 1), g);   // längs; Träger stärker
                foreach (var e in ring)
                {
                    beam(car.Node(s, e[0]), car.Node(s + 1, e[1]), fy * 0.7, g);   // Diagonalen der Außenflächen (Schubfelder)
                    beam(car.Node(s, e[1]), car.Node(s + 1, e[0]), fy * 0.7, g);
                }
                beam(car.Node(s, 8), car.Node(s + 1, 7), fy * 0.7, g); beam(car.Node(s, 9), car.Node(s + 1, 7), fy * 0.7, g);
                beam(car.Node(s, 8), car.Node(s + 1, 0), fy * 0.7, g); beam(car.Node(s, 9), car.Node(s + 1, 6), fy * 0.7, g);
                beam(car.Node(s, 8), car.Node(s + 1, 9), fy * 0.7, g); beam(car.Node(s, 9), car.Node(s + 1, 8), fy * 0.7, g);
            }

            // Motorblock: acht Punkte, steif verstrebt, an den Längsträgern und der Spritzwand gelagert
            if (prm.EngineMass_kg > 0)
            {
                double zf = L / 2 - prm.EngineGap_m, zr = zf - prm.EngineLength_m, y0 = p.Sill + 0.08, y1 = p.Sill + 0.48, hx = 0.28;
                zr = Math.Max(zr, p.CowlZ + 0.15);
                foreach (double z in new[] { zr, zf })
                    foreach (double y in new[] { y0, y1 })
                        foreach (double x in new[] { -hx, hx })
                            car.EngineNodes.Add(b.AddNode(x, y, z, prm.EngineMass_kg / 8));
                for (int i = 0; i < 8; i++)
                    for (int j = i + 1; j < 8; j++)
                        b.AddBeam(car.EngineNodes[i], car.EngineNodes[j], prm.EngineYield_N, prm.EngineYield_N, 0.02, 10, GroupEngine);
                for (int i = 0; i < 8; i++)
                {
                    int e = car.EngineNodes[i];
                    bool left = (i & 1) == 0, rear = i < 4;
                    // nächste Station des Längsträgers und die Spritzwand
                    double ez = rear ? zr : zf;
                    int s = Math.Max(0, Math.Min(n, (int)Math.Round((ez + L / 2) / L * n)));
                    b.AddBeam(e, car.Node(s, left ? 8 : 9), prm.MountYield_N, prm.MountYield_N, 0.02, 0.5, GroupEngine);
                    b.AddBeam(e, car.Node(Math.Min(n, s + 1), left ? 8 : 9), prm.MountYield_N, prm.MountYield_N, 0.02, 0.5, GroupEngine);
                    if (rear)
                    {
                        b.AddBeam(e, car.Node(car.CowlStation, left ? 8 : 9), prm.FirewallStrutYield_N, prm.MountYield_N, 0.02, 0.5, GroupEngine);
                        b.AddBeam(e, car.Node(car.CowlStation, 7), prm.FirewallStrutYield_N, prm.MountYield_N, 0.02, 0.5, GroupEngine);
                    }
                }
            }
            b.Build();
            car._cabinZ0 = car.CabinZ();
            car._cabinLen0 = car.CabinLength();
            return car;
        }

        double StationMeanZ(int s)
        {
            double z = 0;
            for (int k = 0; k < PerStation; k++) z += Body.Z[Node(s, k)];
            return z / PerStation;
        }

        /// <summary>Lage der Fahrgastzelle (Mittel aus Spritzwand und Heckwand, m).</summary>
        public double CabinZ() => 0.5 * (StationMeanZ(CowlStation) + StationMeanZ(DeckStation));
        public double CabinLength() => StationMeanZ(CowlStation) - StationMeanZ(DeckStation);
        /// <summary>Weg der Fahrgastzelle seit dem Start (m) und ihre Verkürzung (Eindringen in den Innenraum, m).</summary>
        public double CabinTravel => CabinZ() - _cabinZ0;
        public double CabinShortening => _cabinLen0 - CabinLength();

        public double CabinSpeed()
        {
            double v = 0; int c = 0;
            for (int s = DeckStation; s <= CowlStation; s++)
                for (int k = 0; k < PerStation; k++) { v += Body.Vz[Node(s, k)]; c++; }
            return v / c;
        }
    }

    /// <summary>Ergebnis eines Anpralls gegen die starre Wand.</summary>
    public class SoftCrashResult
    {
        public double Speed_m_s, MaxCrush_m, CabinShortening_m, PeakDecel_g, MeanDecel_g, PeakWallForce_N, Duration_s, Rebound_m_s, Step_s;
        public int BrokenBeams;
        /// <summary>Verlauf alle 1 ms: Zeit, Weg der Zelle, Geschwindigkeit der Zelle, Wandkraft (über 1 ms gemittelt).</summary>
        public List<double[]> Samples = new List<double[]>();
    }

    public static class SoftCrash
    {
        /// <summary>
        /// Frontal gegen die starre Wand. <paramref name="overlap"/> = Anteil der Fahrzeugbreite, den die Wand überdeckt
        /// (1 = volle Breite, 0,4 = versetzt auf der linken Seite).
        /// </summary>
        public static SoftCrashResult IntoWall(SoftCar car, double speed_m_s, double overlap = 1.0, double duration_s = 0.2)
        {
            var b = car.Body;
            double L = car.Profile.Length, W = car.Profile.Width;
            const double gap = 0.01;
            b.WallZ = L / 2 + gap;
            b.GroundY = 0.12;   // Bodenfreiheit: Blech faltet sich nicht durch die Fahrbahn
            if (overlap < 0.999) { b.WallXMin = double.NegativeInfinity; b.WallXMax = -W / 2 + W * overlap; }
            b.SetVelocity(0, 0, speed_m_s);
            double dt = Math.Min(5e-5, b.StableStep());
            var r = new SoftCrashResult { Speed_m_s = speed_m_s, Step_s = dt };
            double t = 0, next = 0.001, fSum = 0; int fN = 0;
            var speeds = new List<double>();
            while (t < duration_s)
            {
                b.Step(dt);
                t += dt;
                fSum += b.WallForce; fN++;
                if (t >= next)
                {
                    double travel = car.CabinTravel - gap, v = car.CabinSpeed(), f = fSum / Math.Max(1, fN);
                    r.Samples.Add(new[] { t, travel, v, f });
                    speeds.Add(v);
                    r.MaxCrush_m = Math.Max(r.MaxCrush_m, travel);
                    r.CabinShortening_m = Math.Max(r.CabinShortening_m, car.CabinShortening);
                    r.PeakWallForce_N = Math.Max(r.PeakWallForce_N, f);
                    fSum = 0; fN = 0; next += 0.001;
                    if (v < 0 && r.Duration_s == 0) r.Duration_s = t;
                }
            }
            if (r.Duration_s == 0) r.Duration_s = duration_s;
            // Verzögerung der Zelle: Steigung über 10 ms (entspricht grob CFC 60 für die Fahrzeugstruktur)
            for (int i = 10; i < speeds.Count; i++) r.PeakDecel_g = Math.Max(r.PeakDecel_g, (speeds[i - 10] - speeds[i]) / 0.010 / 9.81);
            r.MeanDecel_g = r.MaxCrush_m > 0 ? speed_m_s * speed_m_s / (2 * r.MaxCrush_m) / 9.81 : 0;
            r.Rebound_m_s = speeds.Count > 0 ? -Math.Min(0, speeds[speeds.Count - 1]) : 0;
            r.BrokenBeams = b.BrokenCount();
            return r;
        }
    }

    /// <summary>
    /// Haut: Jede Ecke des sichtbaren Fahrzeugnetzes hängt an den vier nächsten Punkten des Gerüsts (Gewichte nach Abstand) und
    /// folgt deren Verschiebung. Starre Teile (Räder) bewegen sich als Ganzes mit den Punkten um ihre Mitte.
    /// </summary>
    public class SoftSkin
    {
        const int Per = 4;
        readonly ShapeMesh _mesh;
        readonly SoftBody _body;
        readonly int[] _idx;
        readonly float[] _w;

        public SoftSkin(ShapeMesh mesh, SoftBody body)
        {
            _mesh = mesh; _body = body;
            int n = mesh.Count;
            _idx = new int[n * Per]; _w = new float[n * Per];
            // Räder: starre Ecken je Fahrzeugecke zusammenfassen
            var groupCenter = new Dictionary<int, double[]>();
            for (int i = 0; i < n; i++)
            {
                if (!mesh.Rigid[i]) continue;
                int g = (mesh.Rest[3 * i] > 0 ? 1 : 0) + (mesh.Rest[3 * i + 2] > 0 ? 2 : 0);
                double[] c;
                if (!groupCenter.TryGetValue(g, out c)) groupCenter[g] = c = new double[4];
                c[0] += mesh.Rest[3 * i]; c[1] += mesh.Rest[3 * i + 1]; c[2] += mesh.Rest[3 * i + 2]; c[3] += 1;
            }
            for (int i = 0; i < n; i++)
            {
                double x = mesh.Rest[3 * i], y = mesh.Rest[3 * i + 1], z = mesh.Rest[3 * i + 2];
                if (mesh.Rigid[i])
                {
                    var c = groupCenter[(x > 0 ? 1 : 0) + (z > 0 ? 2 : 0)];
                    x = c[0] / c[3]; y = c[1] / c[3]; z = c[2] / c[3];
                }
                Bind(i, x, y, z);
            }
        }

        void Bind(int v, double x, double y, double z)
        {
            var best = new int[Per]; var bd = new double[Per];
            for (int k = 0; k < Per; k++) { best[k] = -1; bd[k] = double.MaxValue; }
            for (int j = 0; j < _body.Count; j++)
            {
                double dx = _body.X0[j] - x, dy = _body.Y0[j] - y, dz = _body.Z0[j] - z;
                double d = dx * dx + dy * dy + dz * dz;
                for (int k = 0; k < Per; k++)
                    if (d < bd[k])
                    {
                        for (int m = Per - 1; m > k; m--) { bd[m] = bd[m - 1]; best[m] = best[m - 1]; }
                        bd[k] = d; best[k] = j;
                        break;
                    }
            }
            double sum = 0;
            var w = new double[Per];
            for (int k = 0; k < Per; k++) { w[k] = 1 / (bd[k] + 1e-4); sum += w[k]; }
            for (int k = 0; k < Per; k++) { _idx[v * Per + k] = best[k]; _w[v * Per + k] = (float)(w[k] / sum); }
        }

        /// <summary>Aktuelle Lage der Gerüstpunkte auf das Netz übertragen (schreibt <see cref="ShapeMesh.V"/>).</summary>
        public void Apply()
        {
            var b = _body;
            for (int i = 0; i < _mesh.Count; i++)
            {
                double dx = 0, dy = 0, dz = 0;
                for (int k = 0; k < Per; k++)
                {
                    int j = _idx[i * Per + k]; float w = _w[i * Per + k];
                    dx += w * (b.X[j] - b.X0[j]); dy += w * (b.Y[j] - b.Y0[j]); dz += w * (b.Z[j] - b.Z0[j]);
                }
                _mesh.V[3 * i] = _mesh.Rest[3 * i] + (float)dx;
                _mesh.V[3 * i + 1] = _mesh.Rest[3 * i + 1] + (float)dy;
                _mesh.V[3 * i + 2] = _mesh.Rest[3 * i + 2] + (float)dz;
            }
        }
    }
}

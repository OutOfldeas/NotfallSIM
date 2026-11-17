using System;
using System.Collections.Generic;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Dellen am verformbaren Fahrzeug außerhalb des Frontalcrashs gegen die Wand: Eine starre Druckfläche je getroffener
    /// Seite (Front, Heck, links, rechts) schiebt das Gerüst so weit ein, wie das jeweilige Stoßmodell an Knautschweg
    /// liefert (<see cref="DeformState"/>: Seitenaufprall, Motorrad, Verkehrsszenario); dazu örtliche Druckflächen von
    /// oben (Fußgänger auf der Haube). **Wie tief** bestimmt also weiter das Stoßmodell — **wie** sich das Blech dabei
    /// legt (Falten, Mitnehmen der Nachbarbereiche, Reißen von Balken) rechnet das Gerüst. Die abgewandten Punkte werden
    /// festgehalten, das Fahrzeug selbst bewegt weiter der Aufrufer.
    /// </summary>
    public class SoftDent
    {
        public readonly SoftCar Car;
        /// <summary>Breite der Druckfläche an der Seite (m; PKW-Front ca. 1,7, Vorderrad ca. 0,4) und an Front/Heck (0 = volle Breite).</summary>
        public double SideWidth = 1.7, EndWidth = 0;
        /// <summary>Breite je Seite (0 Front, 1 Heck, 2 links, 3 rechts) aus dem Stoßmodell, z. B. Überdeckung im Szenario;
        /// gilt, wenn größer als 0, beim ersten Kontakt der Seite statt <see cref="SideWidth"/>/<see cref="EndWidth"/>.</summary>
        public Func<int, double> FaceWidth;
        public readonly double Step_s;
        /// <summary>Größte Geschwindigkeit einer Druckfläche (m/s) — etwa die Annäherung bei einem Stadtunfall.</summary>
        public double MaxPlateSpeed = 15;
        double Clamp(double v) => Math.Max(-MaxPlateSpeed, Math.Min(MaxPlateSpeed, v));
        readonly SoftBody _b;
        readonly SoftPlate[] _face = new SoftPlate[4];        // Front, Heck, links, rechts
        readonly double[] _facePos = new double[4];
        readonly double[] _target = new double[4];
        readonly Dictionary<int, SoftPlate> _top = new Dictionary<int, SoftPlate>();
        readonly Dictionary<int, double> _topTarget = new Dictionary<int, double>();
        float[] _snapshot;
        bool _moved = true;
        double _settle;

        public SoftDent(SoftCar car)
        {
            Car = car; _b = car.Body;
            _b.GroundY = car.Profile.Sill - 0.13;
            _b.Damping_per_s = 40;
            Step_s = Math.Min(5e-5, _b.StableStep());
        }

        /// <summary>Rechnet <paramref name="dt"/> weiter; die Druckflächen fahren in dieser Zeit auf die neuen Tiefen.</summary>
        public void Advance(DeformState d, double dt)
        {
            SetFace(0, d.Front, d.FrontPos); SetFace(1, d.Rear, d.RearPos); SetFace(2, d.Left, d.LeftPos); SetFace(3, d.Right, d.RightPos);
            Run(dt);
        }

        /// <summary>Örtliche Druckfläche von oben (Haube, Dach): Mitte (x, z) in Fahrzeugkoordinaten, halbe Kantenlänge, Tiefe (m).</summary>
        public void SetTop(int id, double x, double z, double half, double depth)
        {
            SoftPlate pl;
            if (!_top.TryGetValue(id, out pl))
            {
                if (depth < 0.003) return;
                // Das Gerüst ist grob (Haube: drei Punkte quer, 20 cm längs): findet die Fläche keinen Punkt, wird sie
                // vergrößert, bis sie den nächsten trifft
                for (double h = half; ; h *= 1.5)
                {
                    pl = new SoftPlate { Sticky = true, Nx = 0, Ny = -1, Nz = 0, MinX = x - h, MaxX = x + h, MinZ = z - h, MaxZ = z + h, MinY = Car.Profile.Sill + 0.3, Friction = 0 };
                    if (pl.Bind(_b) > 0) break;
                    if (h > 0.6) return;
                }
                _b.Plates.Add(pl); _top[id] = pl;
                Repin();
            }
            _topTarget[id] = pl.Rest + Math.Max(0, depth);
        }

        void SetFace(int f, double depth, double pos)
        {
            var pl = _face[f];
            if (pl == null)
            {
                if (depth < 0.005) return;
                double L = Car.Profile.Length, hw = Car.Profile.Width / 2;
                double given = FaceWidth != null ? FaceWidth(f) : 0;
                double endW = given > 0 ? Math.Max(0.3, given) : EndWidth, sideW = given > 0 ? Math.Max(0.3, given) : SideWidth;
                pl = new SoftPlate { Sticky = true };
                if (f < 2)
                {
                    pl.Nz = f == 0 ? -1 : 1;
                    if (endW > 0 && endW < Car.Profile.Width) { pl.MinX = pos - endW / 2; pl.MaxX = pos + endW / 2; }
                    // nur das vordere bzw. hintere Viertel bindet an (sonst zählte die ganze Länge als „vor der Fläche“)
                    if (f == 0) pl.MinZ = L / 4; else pl.MaxZ = -L / 4;
                }
                else
                {
                    pl.Nx = f == 2 ? 1 : -1;
                    pl.MinZ = pos - sideW / 2; pl.MaxZ = pos + sideW / 2;
                    pl.MaxY = Car.Profile.Belt + 0.05;                       // Stoßfänger bis Haubenkante: unter der Gürtellinie
                    if (f == 2) pl.MaxX = -hw / 4; else pl.MinX = hw / 4;
                }
                if (pl.Bind(_b) == 0) return;
                _b.Plates.Add(pl); _face[f] = pl; _facePos[f] = pos;
                Repin();
            }
            _target[f] = pl.Rest + Math.Max(0, depth);
        }

        /// <summary>Abgewandte Punkte festhalten: weiter als 60 % der Fahrzeugabmessung von jeder Druckfläche entfernt.</summary>
        void Repin()
        {
            double L = Car.Profile.Length, W = Car.Profile.Width;
            bool anyFace = false;
            for (int f = 0; f < 4; f++) anyFace |= _face[f] != null;
            for (int i = 0; i < _b.Count; i++)
            {
                bool pin;
                if (anyFace)
                {
                    pin = true;
                    for (int f = 0; f < 4 && pin; f++)
                    {
                        var pl = _face[f];
                        if (pl == null) continue;
                        double dist = pl.Nx * _b.X0[i] + pl.Nz * _b.Z0[i] - pl.Rest;
                        pin = dist > 0.6 * (f < 2 ? L : W);
                    }
                }
                else pin = _b.Y0[i] <= Car.Profile.Sill + 0.25;   // nur Dellen von oben: Boden und Längsträger halten
                _b.Fixed[i] = pin;
            }
        }

        void Run(double dt)
        {
            if (_b.Plates.Count == 0) return;
            dt = Math.Min(dt, 0.01);   // lange Pausen (Anfahrt) nicht nachrechnen
            if (dt <= 0) return;
            bool moving = false;
            // Druckflächen höchstens mit MaxPlateSpeed: springt die Tiefe zwischen zwei Bildern, holt das Gerüst sie über
            // mehrere Bilder nach (sonst würden Punkte mit Hunderten m/s losgeschickt)
            for (int f = 0; f < 4; f++)
                if (_face[f] != null) { _face[f].V = Clamp((_target[f] - _face[f].S) / dt); moving |= Math.Abs(_target[f] - _face[f].S) > 1e-5; }
            foreach (var kv in _top) { kv.Value.V = Clamp((_topTarget[kv.Key] - kv.Value.S) / dt); moving |= Math.Abs(_topTarget[kv.Key] - kv.Value.S) > 1e-5; }
            if (moving) _settle = 0.06;                     // nach der letzten Bewegung noch 60 ms ausschwingen lassen
            else if (_settle <= 0) return;
            else _settle -= dt;
            int n = Math.Max(1, (int)Math.Ceiling(dt / Step_s - 1e-9));
            double h = dt / n;
            for (int i = 0; i < n; i++)
            {
                foreach (var pl in _b.Plates) pl.S += pl.V * h;
                _b.Step(h);
            }
            foreach (var pl in _b.Plates) pl.V = 0;
            _moved = true;
        }

        /// <summary>Lage aller Punkte im Fahrzeug (x, y, z je Punkt); dasselbe Feld, solange sich nichts bewegt hat.</summary>
        public float[] Snapshot()
        {
            if (_snapshot != null && !_moved) return _snapshot;
            var a = new float[3 * _b.Count];
            for (int i = 0; i < _b.Count; i++) { a[3 * i] = (float)_b.X[i]; a[3 * i + 1] = (float)_b.Y[i]; a[3 * i + 2] = (float)_b.Z[i]; }
            _snapshot = a; _moved = false;
            return a;
        }

        /// <summary>Größte Verschiebung eines Punktes aus der Ausgangslage (m) — für Tests und Berichte.</summary>
        public double MaxDisplacement()
        {
            double m = 0;
            for (int i = 0; i < _b.Count; i++)
            {
                double dx = _b.X[i] - _b.X0[i], dy = _b.Y[i] - _b.Y0[i], dz = _b.Z[i] - _b.Z0[i];
                m = Math.Max(m, Math.Sqrt(dx * dx + dy * dy + dz * dz));
            }
            return m;
        }
    }

    /// <summary>
    /// **Noch nicht abgeglichen, in der Sandbox nicht benutzt** (zu elastisch: Δv des getroffenen Fahrzeugs über dem
    /// unelastischen Wert, Türeindringen kaum von der Geschwindigkeit abhängig). Seitenaufprall mit zwei verformbaren Fahrzeugen: Die Front des stoßenden Gerüsts und die Seite des getroffenen
    /// begegnen sich an einer gemeinsamen, masselosen Fläche (so breit wie die Front, Stoßfänger bis Gürtellinie). In jedem
    /// Schritt bekommen alle Punkte, die an der Fläche anliegen und gegen sie drücken, dieselbe Geschwindigkeit — der Impuls
    /// bleibt erhalten, die Energie nehmen die fließenden Balken beider Fahrzeuge auf. Für die Kopplung an den Insassen
    /// liefert der Lauf Geschwindigkeit des getroffenen Fahrzeugs (abgewandte Seite der Zelle) und das Eindringen der Tür
    /// am Sitz, beides geglättet wie in <see cref="SoftCrashRun"/>.
    /// Vereinfachungen: ebene Fläche (wie eine Barriere), keine Reifenhaftung quer, kein Abgleiten längs der Seite; die
    /// Fahrzeuge kippen und gieren nicht (starre Drehung wird je Schritt herausgenommen, wie beim Fahrzeug der Sandbox).
    /// </summary>
    public class SoftSideRun
    {
        const double Gap = 0.01, PlateMass = 5;
        public readonly SoftCar Struck, Bullet;
        public readonly double Speed0_m_s, Step_s;
        public double Time { get; private set; }
        /// <summary>Geschwindigkeit des getroffenen Fahrzeugs quer (m/s) und des stoßenden längs (m/s), geglättet.</summary>
        public double StruckVelocity { get; private set; }
        public double BulletVelocity { get; private set; }
        /// <summary>Eindringen der Tür am Sitz gegenüber der abgewandten Seite (m, geglättet) und der größte Wert.</summary>
        public double Door { get; private set; }
        public double MaxDoor { get; private set; }
        /// <summary>Annäherung der beiden Fahrzeuge seit dem Kontakt (m) und ihr größter Wert; Knautschweg der Front des Gegners.</summary>
        public double Crush { get; private set; }
        public double MaxCrush { get; private set; }
        public double BulletCrush { get; private set; }
        public double PeakStruckAccel_g { get; private set; }
        /// <summary>Kraft an der gemeinsamen Fläche (N, über 1 ms gemittelt), größter Wert.</summary>
        public double PeakForce_N { get; private set; }

        readonly SoftBody _a, _b;
        readonly double _nx;                 // Schubrichtung im getroffenen Fahrzeug: +1 = von links, −1 = von rechts
        readonly bool[] _maskB, _maskA;
        readonly List<int> _ref = new List<int>(), _door = new List<int>();
        readonly double _sB0, _u0;
        double _u, _vP, _shownA, _shownB, _nextSample, _fSum; int _fN;
        readonly List<double> _speeds = new List<double>();
        float[] _snapA, _snapB; double _snapTimeA = -1, _snapTimeB = -1;

        /// <param name="contactZ">Mitte der Front des Gegners längs des getroffenen Fahrzeugs (dessen z, m).</param>
        /// <param name="seatZ">Sitz im getroffenen Fahrzeug (z, m): dort wird das Eindringen der Tür gemessen.</param>
        public SoftSideRun(SoftCar struck, SoftCar bullet, double speed_m_s, double contactZ, double seatZ, bool fromLeft = true)
        {
            Struck = struck; Bullet = bullet; Speed0_m_s = speed_m_s;
            _a = bullet.Body; _b = struck.Body;
            _nx = fromLeft ? 1 : -1;
            _a.GroundY = bullet.Profile.Sill - 0.13; _b.GroundY = struck.Profile.Sill - 0.13;
            _a.SetVelocity(0, 0, speed_m_s);
            BulletVelocity = speed_m_s;
            double hwA = bullet.Profile.Width / 2, top = Math.Min(struck.Profile.Belt + 0.05, bullet.Profile.Belt + 0.05);
            _maskA = new bool[_a.Count]; _u0 = double.NegativeInfinity;
            for (int i = 0; i < _a.Count; i++)
                if (_a.Z0[i] > bullet.Profile.Length / 4) { _maskA[i] = true; _u0 = Math.Max(_u0, _a.Z0[i]); }
            _u = _u0; _vP = speed_m_s;
            _maskB = new bool[_b.Count]; _sB0 = double.PositiveInfinity;
            double hwB = struck.Profile.Width / 2;
            for (int i = 0; i < _b.Count; i++)
            {
                // Schweller (Bodenhöhe) liegt unter dem Stoßfänger des Gegners: getroffen wird die Tür darüber
                if (_b.Z0[i] < contactZ - hwA || _b.Z0[i] > contactZ + hwA || _b.Y0[i] > top || _b.Y0[i] < struck.Profile.Sill + 0.15 || _nx * _b.X0[i] > -hwB / 4) continue;
                _maskB[i] = true; _sB0 = Math.Min(_sB0, _nx * _b.X0[i]);
            }
            // Bezug: abgewandte Hälfte der Fahrgastzelle. Tür: Gürtellinie der getroffenen Seite am Sitz (± 0,3 m) — dort liegen
            // Becken und Brustkorb des Insassen
            int nearBelt = fromLeft ? 1 : 5;
            for (int s = struck.DeckStation; s <= struck.CowlStation; s++)
            {
                for (int k = 0; k < SoftCar.PerStation; k++)
                    if (_nx * _b.X0[struck.Node(s, k)] > 0.2 * hwB) _ref.Add(struck.Node(s, k));
                if (Math.Abs(struck.StationZ[s] - seatZ) <= 0.3) _door.Add(struck.Node(s, nearBelt));
            }
            if (_door.Count == 0) _door.Add(struck.Node((struck.DeckStation + struck.CowlStation) / 2, nearBelt));
            Step_s = Math.Min(5e-5, Math.Min(_a.StableStep(), _b.StableStep()));
        }

        double RefDisplacement()
        {
            double d = 0;
            foreach (int i in _ref) d += _nx * (_b.X[i] - _b.X0[i]);
            return d / _ref.Count;
        }

        public void Advance(double dt)
        {
            int n = Math.Max(1, (int)Math.Ceiling(dt / Step_s - 1e-9));
            double h = dt / n;
            for (int step = 0; step < n; step++)
            {
                _a.Step(h); _b.Step(h);
                double sB = _sB0 + (_u - _u0 - Gap);
                // gemeinsame Geschwindigkeit aller Punkte, die anliegen und gegen die Fläche drücken (unelastisch, Impuls bleibt)
                double v = _vP;
                for (int it = 0; it < 4; it++)
                {
                    double num = PlateMass * _vP, den = PlateMass;
                    for (int i = 0; i < _a.Count; i++)
                        if (_maskA[i] && _a.Z[i] > _u && _a.Vz[i] > v) { num += _a.M[i] * _a.Vz[i]; den += _a.M[i]; }
                    for (int i = 0; i < _b.Count; i++)
                        if (_maskB[i] && _nx * _b.X[i] < sB && _nx * _b.Vx[i] < v) { num += _b.M[i] * _nx * _b.Vx[i]; den += _b.M[i]; }
                    v = num / den;
                }
                double force = 0;
                for (int i = 0; i < _a.Count; i++)
                    if (_maskA[i] && _a.Z[i] > _u && _a.Vz[i] > v) { force += _a.M[i] * (_a.Vz[i] - v) / h; _a.Vz[i] = v; }
                for (int i = 0; i < _b.Count; i++)
                    if (_maskB[i] && _nx * _b.X[i] < sB && _nx * _b.Vx[i] < v) _b.Vx[i] = _nx * v;
                _vP = v;
                _u += v * h;
                _a.RemoveRotation(); _b.RemoveRotation();
                sB = _sB0 + (_u - _u0 - Gap);
                for (int i = 0; i < _a.Count; i++) if (_maskA[i] && _a.Z[i] > _u) _a.Z[i] = _u;
                for (int i = 0; i < _b.Count; i++) if (_maskB[i] && _nx * _b.X[i] < sB) _b.X[i] = _nx * sB;
                Time += h;

                double vRef = 0;
                foreach (int i in _ref) vRef += _nx * _b.Vx[i];
                vRef /= _ref.Count;
                StruckVelocity += (vRef - StruckVelocity) * Math.Min(1, h / 0.002);
                BulletVelocity += (Bullet.CabinSpeed() - BulletVelocity) * Math.Min(1, h / 0.002);
                _shownA += BulletVelocity * h; _shownB += StruckVelocity * h;
                double door = 0;
                foreach (int i in _door) door += _nx * (_b.X[i] - _b.X0[i]);
                door = door / _door.Count - RefDisplacement();
                Door += (Math.Max(0, door) - Door) * Math.Min(1, h / 0.003);
                _fSum += force; _fN++;
                if (Time >= _nextSample)
                {
                    _nextSample += 0.001;
                    PeakForce_N = Math.Max(PeakForce_N, _fSum / Math.Max(1, _fN)); _fSum = 0; _fN = 0;
                    _speeds.Add(vRef);
                    int c = _speeds.Count;
                    if (c > 10) PeakStruckAccel_g = Math.Max(PeakStruckAccel_g, (_speeds[c - 1] - _speeds[c - 11]) / 0.010 / 9.81);
                }
            }
            Crush = Math.Max(0, Bullet.CabinTravel - RefDisplacement() - Gap);
            MaxCrush = Math.Max(MaxCrush, Crush);
            MaxDoor = Math.Max(MaxDoor, Door);
            BulletCrush = Math.Max(BulletCrush, Bullet.CabinTravel - (_u - _u0));
        }

        /// <summary>Gesamtimpuls längs der Stoßrichtung (kg·m/s) — bleibt erhalten (Test).</summary>
        public double Momentum()
        {
            double p = PlateMass * _vP;
            for (int i = 0; i < _a.Count; i++) p += _a.M[i] * _a.Vz[i];
            for (int i = 0; i < _b.Count; i++) p += _b.M[i] * _nx * _b.Vx[i];
            return p;
        }

        /// <summary>Punkte des getroffenen Fahrzeugs relativ zum mitbewegten Fahrzeug des Aufrufers (quer um den gezeigten Weg zurück).
        /// Das Fahrzeug des Aufrufers dreht sich nicht: die Gierdrehung der Zelle (außermittiger Stoß) wird herausgerechnet.</summary>
        public float[] StruckSnapshot()
        {
            if (_snapB != null && _snapTimeB == Time) return _snapB;
            _snapB = WithoutYaw(Struck, _nx * _shownB, 0);
            _snapTimeB = Time;
            return _snapB;
        }

        /// <summary>Punkte des stoßenden Fahrzeugs relativ zu seinem mitbewegten Fahrzeug (längs um den gezeigten Weg zurück).</summary>
        public float[] BulletSnapshot()
        {
            if (_snapA != null && _snapTimeA == Time) return _snapA;
            _snapA = WithoutYaw(Bullet, 0, _shownA);
            _snapTimeA = Time;
            return _snapA;
        }

        /// <summary>Gierwinkel der Fahrgastzelle (rad, um die Hochachse; beste Drehung der Zellpunkte gegen die Ausgangslage).</summary>
        public static double CabinYaw(SoftCar car, out double cx, out double cz)
        {
            var b = car.Body;
            double sx = 0, sz = 0, sx0 = 0, sz0 = 0; int n = 0;
            for (int s = car.DeckStation; s <= car.CowlStation; s++)
                for (int k = 0; k < SoftCar.PerStation; k++) { int i = car.Node(s, k); sx += b.X[i]; sz += b.Z[i]; sx0 += b.X0[i]; sz0 += b.Z0[i]; n++; }
            cx = sx / n; cz = sz / n; double cx0 = sx0 / n, cz0 = sz0 / n;
            double num = 0, den = 0;
            for (int s = car.DeckStation; s <= car.CowlStation; s++)
                for (int k = 0; k < SoftCar.PerStation; k++)
                {
                    int i = car.Node(s, k);
                    double x0 = b.X0[i] - cx0, z0 = b.Z0[i] - cz0, x = b.X[i] - cx, z = b.Z[i] - cz;
                    num += z0 * x - x0 * z; den += x0 * x + z0 * z;
                }
            return Math.Atan2(num, den);
        }

        static float[] WithoutYaw(SoftCar car, double shiftX, double shiftZ)
        {
            var b = car.Body;
            double cx, cz, th = CabinYaw(car, out cx, out cz);
            double c = Math.Cos(-th), sn = Math.Sin(-th);
            var a = new float[3 * b.Count];
            for (int i = 0; i < b.Count; i++)
            {
                double x = b.X[i] - cx, z = b.Z[i] - cz;
                // Drehung um die Hochachse zurück (x' = x·cos + z·sin, z' = −x·sin + z·cos für den Winkel −θ)
                double xr = x * c + z * sn, zr = -x * sn + z * c;
                a[3 * i] = (float)(xr + cx - shiftX); a[3 * i + 1] = (float)b.Y[i]; a[3 * i + 2] = (float)(zr + cz - shiftZ);
            }
            return a;
        }
    }
}

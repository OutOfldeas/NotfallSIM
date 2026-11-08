using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Sichtbares Fahrzeug aus <see cref="VehicleShape"/>: ein Netz mit sieben Materialien (Lack, Glas, Reifen, Lampen,
    /// Metall, Sitz) als Kind eines Fahrzeugs, ohne Kollider. Die Verformung (<see cref="DeformState"/>) wird je aufgezeichnetem
    /// Bild gespeichert und bei der Wiedergabe auf die Ecken übertragen.
    /// </summary>
    public class VehicleVisual
    {
        public ShapeMesh Shape;
        public CarProfile Profile;
        public bool Motorcycle;
        public Mesh Mesh;
        public GameObject Go;
        /// <summary>Liefert die aktuelle Verformung (null = starr).</summary>
        public Func<DeformState> Source;
        public readonly List<DeformState> Frames = new List<DeformState>();

        // ---- Scheiben (siehe GlassBreak): Zeitpunkt des Zerfalls je ESG-Scheibe, Risse und Wölbung der Frontscheibe, Glaswürfel
        /// <summary>Zeitpunkt (Aufzeichnungszeit, s), ab dem die Scheibe zerfallen ist; ∞ = heil.</summary>
        public readonly float[] PaneBreak = { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
        /// <summary>Anprallort auf der Frontscheibe (Modellkoordinaten), Richtung der Wölbung (+1 nach außen, −1 nach innen).</summary>
        public float ShieldX, ShieldZ, ShieldSign = 1, ShieldRadius = 0.25f;
        public bool HasShieldImpact;
        /// <summary>Wölbung im laufenden Schritt (vom Rig gesetzt, wird mit aufgezeichnet).</summary>
        public float ShieldDepthNow;
        readonly List<CrackLine> _cracks = new List<CrackLine>();
        readonly List<GlassCrumbs> _crumbs = new List<GlassCrumbs>();
        GameObject _crackGo; Mesh _crackMesh; Vector3[] _crackBuf;
        int _brokenMask = -1;
        float _crackShown = -1;
        bool _deformChanged;

        struct CrackLine { public VehicleShape.Anchor P0, P1; public float Appear, Width; }

        Vector3[] _buf;
        DeformState _shown;
        bool _any;

        public static VehicleVisual Car(Transform parent, CarProfile p, Color paint, Vector3 localPos, Quaternion localRot) =>
            Create(parent, VehicleShape.Car(p), paint, localPos, localRot, p, false);

        public static VehicleVisual Moto(Transform parent, VehicleShape.MotoProfile p, Color paint, Vector3 localPos) =>
            Create(parent, VehicleShape.Motorcycle(p), paint, localPos, Quaternion.identity, null, true);

        static VehicleVisual Create(Transform parent, ShapeMesh shape, Color paint, Vector3 localPos, Quaternion localRot, CarProfile car, bool moto)
        {
            var go = new GameObject(moto ? "Motorrad (Modell)" : "Karosserie (Modell)");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var mesh = new Mesh { name = go.name };
            mesh.MarkDynamic();
            var v = new Vector3[shape.Count];
            for (int i = 0; i < v.Length; i++) v[i] = new Vector3(shape.Rest[3 * i], shape.Rest[3 * i + 1], shape.Rest[3 * i + 2]);
            mesh.vertices = v;
            mesh.subMeshCount = shape.Tri.Length;
            for (int s = 0; s < shape.Tri.Length; s++) mesh.SetTriangles(shape.Tri[s], s);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(paint);
            return new VehicleVisual { Shape = shape, Profile = car, Motorcycle = moto, Mesh = mesh, Go = go, _buf = v };
        }

        static Material[] Materials(Color paint) => new[]
        {
            CrashPhysics.Mat(paint),
            CrashPhysics.GlassMat(new Color(0.55f, 0.7f, 0.8f, 0.28f)),
            CrashPhysics.Mat(new Color(0.07f, 0.07f, 0.08f)),
            CrashPhysics.Mat(new Color(1f, 0.96f, 0.8f)),
            CrashPhysics.Mat(new Color(0.8f, 0.08f, 0.08f)),
            CrashPhysics.Mat(new Color(0.6f, 0.62f, 0.65f)),
            CrashPhysics.Mat(new Color(0.12f, 0.12f, 0.13f)),
        };

        /// <summary>Verformung zeigen (nur wenn sie sich geändert hat).</summary>
        public void Apply(DeformState d)
        {
            if (_any && d.Equals(_shown)) return;
            _shown = d; _any = true;
            if (Motorcycle) VehicleShape.DeformMotorcycle(Shape, d.Front);
            else
            {
                VehicleShape.DeformCar(Shape, Profile, d);
                if (HasShieldImpact && d.Shield > 1e-4f) VehicleShape.BulgeShield(Shape, Profile, ShieldX, ShieldZ, ShieldSign * d.Shield, ShieldRadius);
            }
            _deformChanged = true;
            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Vector3(Shape.V[3 * i], Shape.V[3 * i + 1], Shape.V[3 * i + 2]);
            Mesh.vertices = _buf;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
        }

        public void Record()
        {
            var d = Source != null ? Source() : default(DeformState);
            d.Shield = ShieldDepthNow;
            Frames.Add(d);
        }


        // ---------------------------------------------------------------- Scheiben

        /// <summary>Spinnennetz in der Frontscheibe um (x, z) (Modellkoordinaten), wächst ab <paramref name="start"/> in 4 ms auf.</summary>
        public void AddCrack(float x, float z, float radius, float start, int seed)
        {
            if (Profile == null) return;
            VehicleShape.ClampToShield(Profile, ref x, ref z);
            var tris = VehicleShape.PaneTriangles(Shape, Pane.Front);
            foreach (var seg in GlassCrack.Pattern(seed, radius))
            {
                float x0, y0, z0, x1, y1, z1;
                VehicleShape.ShieldPoint(Profile, x, z, seg.A0, seg.B0, out x0, out y0, out z0);
                VehicleShape.ShieldPoint(Profile, x, z, seg.A1, seg.B1, out x1, out y1, out z1);
                VehicleShape.Anchor a0, a1;
                if (!VehicleShape.Attach(Shape, tris, x0, y0, z0, out a0) || !VehicleShape.Attach(Shape, tris, x1, y1, z1, out a1)) continue;
                _cracks.Add(new CrackLine { P0 = a0, P1 = a1, Appear = start + 0.004f * Mathf.Clamp01(seg.Reach), Width = seg.Reach < 0.1f ? 0.005f : 0.0035f });
            }
            _crackShown = -1;
            if (_crackGo == null)
            {
                _crackGo = new GameObject("Risse (Frontscheibe)");
                _crackGo.transform.SetParent(Go.transform, false);
                _crackMesh = new Mesh { name = "Risse" };
                _crackMesh.MarkDynamic();
                _crackGo.AddComponent<MeshFilter>().sharedMesh = _crackMesh;
                var cr = _crackGo.AddComponent<MeshRenderer>();
                cr.sharedMaterial = CrashPhysics.GlassMat(new Color(0.93f, 0.96f, 1f, 0.85f));
                cr.enabled = Go.GetComponent<MeshRenderer>().enabled;   // während der Rechnung ist das Fahrzeug ausgeblendet
            }
            // je Riss zwei Vierecke (außen und innen sichtbar)
            int n = _cracks.Count;
            _crackBuf = new Vector3[8 * n];
            var tri = new int[12 * n];
            for (int i = 0; i < n; i++)
            {
                int v = 8 * i, t = 12 * i;
                tri[t] = v; tri[t + 1] = v + 1; tri[t + 2] = v + 2; tri[t + 3] = v; tri[t + 4] = v + 2; tri[t + 5] = v + 3;
                tri[t + 6] = v + 4; tri[t + 7] = v + 6; tri[t + 8] = v + 5; tri[t + 9] = v + 4; tri[t + 10] = v + 7; tri[t + 11] = v + 6;
            }
            _crackMesh.Clear();
            _crackMesh.vertices = _crackBuf;
            _crackMesh.triangles = tri;
        }

        /// <summary>ESG-Scheibe zerfällt bei <paramref name="time"/>: Würfel starten an Punkten der Scheibe (Welt), Geschwindigkeit je Punkt.</summary>
        public void AddCrumbs(GlassCrumbs c) { if (c != null) _crumbs.Add(c); }

        /// <summary>Zufällige Punkte (Modellkoordinaten) auf einer Scheibe, flächengleich verteilt.</summary>
        public List<Vector3> PanePoints(Pane pane, int count, System.Random rng)
        {
            var tris = VehicleShape.PaneTriangles(Shape, pane);
            var pts = new List<Vector3>();
            if (tris.Count == 0) return pts;
            var area = new float[tris.Count / 3];
            float sum = 0;
            Func<int, Vector3> P = i => new Vector3(Shape.V[3 * i], Shape.V[3 * i + 1], Shape.V[3 * i + 2]);
            for (int t = 0; t < area.Length; t++)
            {
                sum += Vector3.Cross(P(tris[3 * t + 1]) - P(tris[3 * t]), P(tris[3 * t + 2]) - P(tris[3 * t])).magnitude / 2;
                area[t] = sum;
            }
            for (int k = 0; k < count; k++)
            {
                float pick = (float)rng.NextDouble() * sum;
                int t = Array.FindIndex(area, cum => cum >= pick);
                if (t < 0) t = area.Length - 1;
                float u = (float)rng.NextDouble(), w = (float)rng.NextDouble();
                if (u + w > 1) { u = 1 - u; w = 1 - w; }
                Vector3 a = P(tris[3 * t]), b = P(tris[3 * t + 1]), c = P(tris[3 * t + 2]);
                pts.Add(a + u * (b - a) + w * (c - a));
            }
            return pts;
        }

        /// <summary>Scheiben, Risse und Würfel zum Zeitpunkt t (Aufzeichnungszeit) zeigen — nach <see cref="Show"/>.</summary>
        public void ShowEffects(float t)
        {
            if (Motorcycle) return;
            int mask = 0;
            for (int i = 0; i < PaneBreak.Length; i++) if (PaneBreak[i] <= t) mask |= 1 << i;
            if (mask != _brokenMask)
            {
                _brokenMask = mask;
                Mesh.SetTriangles(VehicleShape.GlassWithout(Shape, mask), (int)ShapePart.Glas);
            }
            if (_crackGo != null) _crackGo.GetComponent<MeshRenderer>().enabled = Go.GetComponent<MeshRenderer>().enabled;
            if (_crackMesh != null && (_deformChanged || Mathf.Abs(t - _crackShown) > 1e-5f)) UpdateCracks(t);
            _deformChanged = false;
            foreach (var c in _crumbs) c.Show(t);
        }

        void UpdateCracks(float t)
        {
            _crackShown = t;
            for (int i = 0; i < _cracks.Count; i++)
            {
                var c = _cracks[i];
                int v = 8 * i;
                if (t < c.Appear) { for (int k = 0; k < 8; k++) _crackBuf[v + k] = Vector3.zero; continue; }
                float x0, y0, z0, nx, ny, nz, x1, y1, z1, mx, my, mz;
                VehicleShape.Evaluate(Shape, c.P0, out x0, out y0, out z0, out nx, out ny, out nz);
                VehicleShape.Evaluate(Shape, c.P1, out x1, out y1, out z1, out mx, out my, out mz);
                Vector3 p0 = new Vector3(x0, y0, z0), p1 = new Vector3(x1, y1, z1), n = new Vector3(nx + mx, ny + my, nz + mz).normalized;
                var side = Vector3.Cross(p1 - p0, n);
                side = side.sqrMagnitude > 1e-12f ? side.normalized * (c.Width / 2) : Vector3.zero;
                Vector3 o = n * 0.004f;
                _crackBuf[v] = p0 - side + o; _crackBuf[v + 1] = p0 + side + o; _crackBuf[v + 2] = p1 + side + o; _crackBuf[v + 3] = p1 - side + o;
                _crackBuf[v + 4] = p0 - side - o; _crackBuf[v + 5] = p0 + side - o; _crackBuf[v + 6] = p1 + side - o; _crackBuf[v + 7] = p1 - side - o;
            }
            _crackMesh.vertices = _crackBuf;
            _crackMesh.RecalculateNormals();
            _crackMesh.RecalculateBounds();
        }

        /// <summary>Aufgezeichnete Verformung zwischen zwei Bildern zeigen.</summary>
        public void Show(int lo, int hi, float u)
        {
            if (Frames.Count == 0) return;
            lo = Mathf.Clamp(lo, 0, Frames.Count - 1); hi = Mathf.Clamp(hi, 0, Frames.Count - 1);
            Apply(DeformState.Lerp(Frames[lo], Frames[hi], u));
        }
    }

    /// <summary>
    /// Glaswürfel einer zerfallenen ESG-Scheibe (ein Netz, ohne Physik): Flugbahn mit Schwerkraft, auf dem Boden Rutschen mit
    /// µ = 0,6 bis zum Stillstand — als Funktion der Zeit, damit Wiedergabe und Spulen immer dasselbe Bild zeigen.
    /// </summary>
    public class GlassCrumbs
    {
        const float G = 9.81f, Mu = 0.6f;
        public GameObject Go;
        Mesh _mesh;
        Vector3[] _p0, _v, _axis, _buf;
        float[] _size, _spin;
        float _t0, _shown = float.NaN;
        static readonly Vector3[] Corner =
        {
            new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
            new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1)
        };

        public static GlassCrumbs Create(string name, List<Vector3> starts, List<Vector3> velocities, float t0, int seed)
        {
            int n = starts.Count;
            if (n == 0) return null;
            var rng = new System.Random(seed);
            var c = new GlassCrumbs { _t0 = t0, _p0 = starts.ToArray(), _v = velocities.ToArray(), _axis = new Vector3[n], _size = new float[n], _spin = new float[n], _buf = new Vector3[8 * n] };
            for (int i = 0; i < n; i++)
            {
                c._size[i] = 0.004f + 0.004f * (float)rng.NextDouble();          // halbe Kantenlänge: Würfel 8–16 mm
                c._axis[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
                c._spin[i] = 600 + 1800 * (float)rng.NextDouble();               // °/s
            }
            var tri = new int[36 * n];
            int[] faces = { 0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5 };
            for (int i = 0; i < n; i++) for (int k = 0; k < 36; k++) tri[36 * i + k] = 8 * i + faces[k];
            c.Go = new GameObject(name);
            c._mesh = new Mesh { name = name };
            c._mesh.MarkDynamic();
            c._mesh.vertices = c._buf;
            c._mesh.triangles = tri;
            c.Go.AddComponent<MeshFilter>().sharedMesh = c._mesh;
            c.Go.AddComponent<MeshRenderer>().sharedMaterial = CrashPhysics.GlassMat(new Color(0.78f, 0.9f, 0.95f, 0.6f));
            c.Show(0);
            return c;
        }

        public void Show(float t)
        {
            if (t == _shown) return;
            _shown = t;
            float dt = t - _t0;
            var r = Go.GetComponent<MeshRenderer>();
            r.enabled = dt >= 0;
            if (dt < 0) return;
            for (int i = 0; i < _p0.Length; i++)
            {
                Vector3 p0 = _p0[i], v = _v[i];
                float h = _size[i];
                float y0 = Mathf.Max(p0.y, h);
                float tHit = (v.y + Mathf.Sqrt(v.y * v.y + 2 * G * (y0 - h))) / G;
                Vector3 pos; float ang;
                if (dt < tHit) { pos = p0 + v * dt + new Vector3(0, -0.5f * G * dt * dt, 0); ang = _spin[i] * dt; }
                else
                {
                    var hit = p0 + v * tHit; hit.y = h;
                    var vh = new Vector3(v.x, 0, v.z);
                    float sp = vh.magnitude, tau = Mathf.Min(dt - tHit, sp / (Mu * G));
                    float dist = sp * tau - 0.5f * Mu * G * tau * tau;
                    pos = sp > 1e-4f ? hit + vh / sp * dist : hit;
                    ang = _spin[i] * tHit;
                }
                var q = Quaternion.AngleAxis(ang, _axis[i]);
                for (int k = 0; k < 8; k++) _buf[8 * i + k] = pos + q * (Corner[k] * h);
            }
            _mesh.vertices = _buf;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}

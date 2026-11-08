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
            else VehicleShape.DeformCar(Shape, Profile, d);
            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Vector3(Shape.V[3 * i], Shape.V[3 * i + 1], Shape.V[3 * i + 2]);
            Mesh.vertices = _buf;
            Mesh.RecalculateNormals();
            Mesh.RecalculateBounds();
        }

        public void Record() { if (Source != null) Frames.Add(Source()); }

        /// <summary>Aufgezeichnete Verformung zwischen zwei Bildern zeigen.</summary>
        public void Show(int lo, int hi, float u)
        {
            if (Frames.Count == 0) return;
            lo = Mathf.Clamp(lo, 0, Frames.Count - 1); hi = Mathf.Clamp(hi, 0, Frames.Count - 1);
            Apply(DeformState.Lerp(Frames[lo], Frames[hi], u));
        }
    }
}

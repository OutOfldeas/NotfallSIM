using System.Collections.Generic;
using UnityEngine;

namespace NotfallSIM.Rooms
{
    /// <summary>
    /// Verstellbare Liegefläche der Trage: Rückenteil, Sitzteil und Beinteil. Die Teile drehen um dieselben Punkte wie der
    /// Körper (Rückenteil um das Gelenk Becken–Lendenwirbelsäule, Beinteil um die Hüftgelenke), damit der Patient bei jeder
    /// Lagerung aufliegt. Ein einzelnes hochgelagertes Bein liegt auf einem Lagerungskeil.
    /// Ersetzt zur Laufzeit die einteilige „Liegefläche“ (auch in älteren Szenen).
    /// </summary>
    public class Stretcher : MonoBehaviour
    {
        Transform _back, _leg, _wedgeR, _wedgeL;
        float _top, _halfWidth, _halfLength, _thickness;
        Material _mattress, _cushion;
        bool _built;

        /// <summary>Trage in der Szene finden und bei Bedarf umbauen (null, wenn keine „Trage“ mit „Liegefläche“ existiert).</summary>
        public static Stretcher Find()
        {
            var t = GameObject.Find("Trage");
            if (t == null) return null;
            var s = t.GetComponent<Stretcher>();
            if (s == null) s = t.AddComponent<Stretcher>();
            return s;
        }

        /// <summary>Liegefläche in drei Teile zerlegen. Gelenkpunkte in Weltkoordinaten: Rückenteil (Kopfende +z) und Hüfte.</summary>
        public void Build(Vector3 backPivotWorld, Vector3 hipPivotWorld)
        {
            if (_built) return;
            var mattressTr = transform.Find("Liegefläche");
            if (mattressTr == null) return;
            _mattress = mattressTr.GetComponent<Renderer>().sharedMaterial;
            Vector3 size = mattressTr.localScale, center = mattressTr.localPosition;
            _thickness = size.y; _halfWidth = size.x / 2; _halfLength = size.z / 2;
            _top = center.y + size.y / 2;
            float zMin = center.z - _halfLength, zMax = center.z + _halfLength;
            var back = transform.InverseTransformPoint(backPivotWorld);
            var hip = transform.InverseTransformPoint(hipPivotWorld);
            float zBack = Mathf.Clamp(back.z, zMin + 0.3f, zMax - 0.3f), zHip = Mathf.Clamp(hip.z, zMin + 0.3f, zBack - 0.05f);

            // Sitzteil fest zwischen Hüfte und Rückenteil
            Section("Liegefläche Sitzteil", transform, center.x, zHip, zBack, new Vector3(0, 0, 0));
            // Rückenteil und Beinteil drehen um die Gelenkpunkte des Körpers
            _back = Pivot("Gelenk Rückenteil", new Vector3(center.x, back.y, zBack));
            Section("Liegefläche Rückenteil", _back, center.x, zBack, zMax, _back.localPosition);
            _leg = Pivot("Gelenk Beinteil", new Vector3(center.x, hip.y, zHip));
            Section("Liegefläche Beinteil", _leg, center.x, zMin, zHip, _leg.localPosition);
            mattressTr.gameObject.SetActive(false);

            var c = _mattress.color;
            _cushion = new Material(_mattress) { color = new Color(c.r * 1.6f, c.g * 1.6f, c.b * 1.5f) };
            _built = true;
        }

        Transform Pivot(string name, Vector3 local)
        {
            var p = new GameObject(name).transform;
            p.SetParent(transform, false);
            p.localPosition = local;
            return p;
        }

        void Section(string name, Transform parent, float x, float z0, float z1, Vector3 pivotLocal)
        {
            var go = TraumaRoom.Box(parent, name, new Vector3(x, _top - _thickness / 2, (z0 + z1) / 2) - pivotLocal,
                                    new Vector3(_halfWidth * 2, _thickness, z1 - z0), _mattress);
            go.layer = 0;
        }

        /// <summary>Winkel einstellen: Rückenteil (Kopf hoch), Beine rechts/links (Grad gegen die Liegefläche).
        /// Beide Beine gleich: Beinteil; nur eines: Lagerungskeil unter diesem Bein (Hüfte bei x des Beins).</summary>
        public void Apply(float trunkDeg, float legRDeg, float legLDeg, Vector3 hipRWorld, Vector3 hipLWorld)
        {
            if (!_built) return;
            _back.localRotation = Quaternion.Euler(-trunkDeg, 0, 0);
            bool both = legRDeg > 0 && Mathf.Abs(legRDeg - legLDeg) < 0.1f;
            _leg.localRotation = Quaternion.Euler(both ? legRDeg : 0, 0, 0);
            Wedge(ref _wedgeR, "Lagerungskeil rechts", !both && legRDeg > 0 ? legRDeg : 0, hipRWorld);
            Wedge(ref _wedgeL, "Lagerungskeil links", !both && legLDeg > 0 ? legLDeg : 0, hipLWorld);
        }

        /// <summary>Keil (Dreiecksprisma) vom Gelenk bis zum Fußende, Oberseite unter dem Bein im eingestellten Winkel.</summary>
        void Wedge(ref Transform wedge, string name, float angleDeg, Vector3 hipWorld)
        {
            if (angleDeg <= 0) { if (wedge != null) wedge.gameObject.SetActive(false); return; }
            var hip = transform.InverseTransformPoint(hipWorld);
            // Das Bein dreht um das Hüftgelenk (d über der Liegefläche). Seine Rückseite liegt danach auf der Geraden
            // Höhe = d·(1 − cos a) + s·sin a bei z = hip.z − d·sin a − s·cos a (s = Abstand von der Hüfte).
            float a = angleDeg * Mathf.Deg2Rad, d = Mathf.Max(0, hip.y - _top);
            float z0 = hip.z - d * Mathf.Sin(a), zFoot = -_halfLength + 0.02f;
            float len = Mathf.Max(0.2f, z0 - zFoot);
            float h0 = Mathf.Max(0.005f, d * (1 - Mathf.Cos(a))), h1 = h0 + len * Mathf.Tan(a);
            float w = 0.24f;
            if (wedge == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterial = _cushion;
                go.AddComponent<MeshCollider>().convex = true;
                wedge = go.transform;
            }
            wedge.gameObject.SetActive(true);
            wedge.localPosition = new Vector3(hip.x, _top, z0);
            var mesh = new Mesh { name = name };
            // x quer, y hoch, z Richtung Kopf; Keil liegt bei z ∈ [−len, 0]
            var v = new List<Vector3>
            {
                new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(-w / 2, 0, -len), new Vector3(w / 2, 0, -len),
                new Vector3(-w / 2, h0, 0), new Vector3(w / 2, h0, 0), new Vector3(-w / 2, h1, -len), new Vector3(w / 2, h1, -len)
            };
            int[] t =
            {
                0, 2, 1, 1, 2, 3,   // unten
                4, 5, 6, 5, 7, 6,   // oben (schräg)
                2, 6, 3, 3, 6, 7,   // Fußseite
                0, 1, 4, 1, 5, 4,   // Gelenkseite
                0, 4, 2, 2, 4, 6,   // links
                1, 3, 5, 3, 7, 5    // rechts
            };
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            wedge.GetComponent<MeshFilter>().sharedMesh = mesh;
            wedge.GetComponent<MeshCollider>().sharedMesh = mesh;
        }

        /// <summary>Gehört der Kollider zur Trage (Liegefläche, Keil, Rahmen)?</summary>
        public bool Owns(Collider c) => c != null && c.transform.IsChildOf(transform);
    }
}

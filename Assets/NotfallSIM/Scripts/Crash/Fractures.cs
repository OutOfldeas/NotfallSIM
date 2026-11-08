using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Zeigt Frakturen am Ragdoll als Fehlstellung, ohne die Physik anzufassen: Die Haut hängt distal der Bruchstelle an
    /// einem zusätzlichen Knochen; Segmente distal davon (Unterschenkel samt Fuß beim Femurbruch) zeichnet die Haut über Anzeige-Kopien, die der Physik
    /// folgen und um die Bruchstelle mitgedreht werden. Der Knick zeigt vom Anprall weg wie beim Biegebruch (Keil auf der
    /// Gegenseite): das distale Ende weicht zur Anprallseite aus.
    /// </summary>
    public class FractureDisplay : MonoBehaviour
    {
        class Item
        {
            public FractureSpecs.Spec Spec;
            public Transform Real, Shown, Bone;          // Segment (Physik), wie es gezeichnet wird, Knochen am Bruch
            public Vector3 Pivot, RotAxis;                // im Segment (lokal)
            public float Time;
            public List<Transform> DistalShown = new List<Transform>();
        }

        readonly List<Item> _items = new List<Item>();
        readonly List<(Transform real, Transform proxy)> _proxies = new List<(Transform, Transform)>();
        public Func<float> Clock;

        public bool Any => _items.Count > 0;

        /// <summary>Bricht die Segmente (Haut neu gewichtet); <paramref name="when"/> und <paramref name="towardContact"/> je Segment.</summary>
        public static FractureDisplay Attach(Ragdoll rd, List<FractureSpecs.Spec> specs, Func<string, float> when, Func<string, Vector3> towardContact, Func<float> clock)
        {
            if (rd == null || rd.Skin == null || specs.Count == 0 || !Anatomy.AnatomyBuilder.Available) return null;
            var data = Anatomy.AnatomyBuilder.Load();
            var h = data.Header;
            var smr = rd.Skin;
            var mesh = smr.sharedMesh;
            var bones = smr.bones.ToList();
            var bind = mesh.bindposes.ToList();
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            // Maßstab: Bindpose = −Gelenk · k
            int s0 = data.SegmentIndex(h.segments[0].name);
            float k = h.segments[s0].joint.Length > 0 && Mathf.Abs(h.segments[s0].joint[1]) > 1e-4f ? -bind[s0].m13 / h.segments[s0].joint[1] : 1;
            var fd = rd.Root.AddComponent<FractureDisplay>();
            fd.Clock = clock;

            // Anzeige-Kopien für alle Segmente distal einer Fraktur
            var distalNames = new HashSet<string>(specs.SelectMany(s => s.Distal));
            var shown = new Dictionary<string, Transform>();
            foreach (var name in distalNames)
            {
                int i = data.SegmentIndex(name);
                if (i < 0 || i >= bones.Count || !rd.Segments.ContainsKey(name)) continue;
                var real = rd.Segments[name].transform;
                var proxy = new GameObject(name + " (Anzeige)").transform;
                proxy.SetParent(rd.Root.transform, true);
                proxy.SetPositionAndRotation(real.position, real.rotation);
                bones[i] = proxy;
                shown[name] = proxy;
                fd._proxies.Add((real, proxy));
            }

            foreach (var spec in specs)
            {
                int si = data.SegmentIndex(spec.Segment);
                if (si < 0 || !rd.Segments.ContainsKey(spec.Segment)) continue;
                var seg = h.segments[si];
                var real = rd.Segments[spec.Segment].transform;
                Transform segShown;
                if (!shown.TryGetValue(spec.Segment, out segShown)) segShown = real;
                Vector3 axis = (Anatomy.AnatomyBuilder.V(seg.end) - Anatomy.AnatomyBuilder.V(seg.joint)) * k;
                Vector3 pivot = axis * spec.Fraction, dir = axis.normalized;
                Vector3 toward = towardContact(spec.Segment);
                Vector3 perp = toward - dir * Vector3.Dot(toward, dir);
                if (perp.sqrMagnitude < 1e-6f) perp = Vector3.forward - dir * Vector3.Dot(Vector3.forward, dir);
                var item = new Item
                {
                    Spec = spec, Real = real, Shown = segShown, Pivot = pivot, RotAxis = Vector3.Cross(dir, perp.normalized).normalized, Time = when(spec.Segment)
                };
                item.Bone = new GameObject(spec.Segment + " (Bruchstück)").transform;
                item.Bone.SetParent(segShown, false);
                item.Bone.localPosition = pivot;
                item.Bone.localRotation = Quaternion.identity;
                foreach (var d in spec.Distal) { Transform p; if (shown.TryGetValue(d, out p)) item.DistalShown.Add(p); }
                // Haut distal der Bruchstelle an den neuen Knochen
                int fi = bones.Count;
                bones.Add(item.Bone);
                bind.Add(Matrix4x4.Translate(-pivot) * bind[si]);
                for (int v = 0; v < verts.Length; v++)
                {
                    var bw = weights[v];
                    float wS = (bw.boneIndex0 == si ? bw.weight0 : 0) + (bw.boneIndex1 == si ? bw.weight1 : 0) + (bw.boneIndex2 == si ? bw.weight2 : 0);
                    if (wS <= 0) continue;
                    var local = bind[si].MultiplyPoint3x4(verts[v]);
                    float share = FractureSpecs.DistalShare(Vector3.Dot(local - pivot, dir));
                    if (share <= 0) continue;
                    float move = wS * share;
                    if (bw.boneIndex0 == si) bw.weight0 -= move * (bw.weight0 / wS);
                    if (bw.boneIndex1 == si) bw.weight1 -= move * (bw.weight1 / wS);
                    if (bw.boneIndex2 == si) bw.weight2 -= move * (bw.weight2 / wS);
                    // freien Platz suchen (die Haut nutzt zwei Knochen je Ecke), dann absteigend sortieren (Unity nutzt ggf. nur die ersten zwei)
                    if (bw.weight2 <= 0) { bw.boneIndex2 = fi; bw.weight2 = move; }
                    else { bw.boneIndex3 = fi; bw.weight3 = move; }
                    weights[v] = Sorted(bw);
                }
                fd._items.Add(item);
            }
            mesh.bindposes = bind.ToArray();
            mesh.boneWeights = weights;
            smr.bones = bones.ToArray();
            smr.quality = SkinQuality.Bone4;
            fd.LateUpdate();
            return fd;
        }

        static BoneWeight Sorted(BoneWeight b)
        {
            var w = new[] { (b.boneIndex0, b.weight0), (b.boneIndex1, b.weight1), (b.boneIndex2, b.weight2), (b.boneIndex3, b.weight3) }
                .OrderByDescending(x => x.Item2).ToArray();
            return new BoneWeight
            {
                boneIndex0 = w[0].Item1, weight0 = w[0].Item2, boneIndex1 = w[1].Item1, weight1 = w[1].Item2,
                boneIndex2 = w[2].Item1, weight2 = w[2].Item2, boneIndex3 = w[3].Item1, weight3 = w[3].Item2
            };
        }

        public IEnumerable<(string label, float time)> Breaks => _items.Select(i => (i.Spec.Label, i.Time));

        void LateUpdate()
        {
            foreach (var p in _proxies) if (p.real && p.proxy) p.proxy.SetPositionAndRotation(p.real.position, p.real.rotation);
            float t = Clock != null ? Clock() : float.PositiveInfinity;
            foreach (var it in _items)
            {
                if (!it.Shown || !it.Bone) continue;
                float ang = FractureSpecs.AngleAt(t, it.Time, it.Spec.Angle_deg);
                var q = Quaternion.AngleAxis(ang, it.RotAxis);
                it.Bone.localPosition = it.Pivot;
                it.Bone.localRotation = q;
                if (ang <= 0) continue;
                var pivotW = it.Shown.TransformPoint(it.Pivot);
                var rw = it.Shown.rotation * q * Quaternion.Inverse(it.Shown.rotation);
                foreach (var d in it.DistalShown) d.SetPositionAndRotation(pivotW + rw * (d.position - pivotW), rw * d.rotation);
            }
        }
    }
}

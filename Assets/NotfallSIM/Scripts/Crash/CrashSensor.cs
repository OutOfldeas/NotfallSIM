using System.Collections.Generic;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Virtueller Messaufnehmer an einem Ragdoll-Segment, wie beim Crashtest-Dummy:
    /// resultierende Beschleunigung (aus der Geschwindigkeitsänderung je Physikschritt) und
    /// Kontaktkraft (Summe der Kontaktimpulse je Schritt / Schrittweite). Kontakte innerhalb
    /// des eigenen Ragdolls und mit Sitzpolstern zählen nicht.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CrashSensor : MonoBehaviour
    {
        [SerializeField] string region;
        public string Region => region;

        public readonly List<double> Accel_g = new List<double>();
        public readonly List<double> Force_kN = new List<double>();
        /// <summary>Kontaktimpuls nach Seite gewichtet (− links, + rechts) und Summe der Impulse — für Abdomen.lateral.</summary>
        public double LateralImpulse, TotalImpulse;
        public double Dt_s { get; private set; }

        /// <summary>Kontakte je Flächenart (für Verletzungszeichen): Impuls, Spitzenkraft, Gleiten, Ort des stärksten Schritts.</summary>
        public class SurfaceContact
        {
            public double Impulse_Ns, PeakForce_N, MaxSlide_m_s, StepImpulse, BestStep;
            public Vector3 BestPointWorld, StepPointWorld, BestPointLocal;
        }
        public readonly Dictionary<string, SurfaceContact> Contacts = new Dictionary<string, SurfaceContact>();

        // Rückrechnung in die Ruhelage des Anatomiemodells: Modell = Gelenk + frame.InverseTransformPoint(p) / k
        Transform _frame; Vector3 _modelJoint, _modelEnd; float _k;
        public bool HasModelFrame => _frame != null;
        public void SetModelFrame(Transform frame, Vector3 modelJoint, Vector3 modelEnd, float k) { _frame = frame; _modelJoint = modelJoint; _modelEnd = modelEnd; _k = k; }
        public string ModelSegment;

        /// <summary>Punkt in Modellkoordinaten und der nächste Punkt auf der Segmentachse (von dort nach außen).</summary>
        public bool ToModel(Vector3 world, out Vector3 point, out Vector3 inside)
        {
            point = inside = Vector3.zero;
            if (_frame == null || _k <= 0) return false;
            point = _modelJoint + _frame.InverseTransformPoint(world) / _k;
            Vector3 ax = _modelEnd - _modelJoint;
            float t = ax.sqrMagnitude > 1e-9f ? Mathf.Clamp01(Vector3.Dot(point - _modelJoint, ax) / ax.sqrMagnitude) : 0;
            inside = _modelJoint + ax * t;
            return true;
        }

        Rigidbody _rb;
        Transform _body;
        Vector3 _prevV;
        float _stepImpulse;
        bool _recording;

        public void Configure(string regionName, Transform bodyRoot) { region = regionName; _body = bodyRoot; }

        void Awake() { _rb = GetComponent<Rigidbody>(); }

        public void StartRecording()
        {
            Accel_g.Clear(); Force_kN.Clear(); LateralImpulse = TotalImpulse = 0; Contacts.Clear();
            _prevV = CrashPhysics.Velocity(_rb);
            _stepImpulse = 0;
            Dt_s = Time.fixedDeltaTime;
            _recording = true;
        }

        public void StopRecording() => _recording = false;

        void FixedUpdate()
        {
            if (!_recording) return;
            float dt = Time.fixedDeltaTime;
            Vector3 v = CrashPhysics.Velocity(_rb);
            Accel_g.Add(((v - _prevV) / dt).magnitude / 9.81);
            Force_kN.Add(_stepImpulse / dt / 1000.0);
            _prevV = v;
            _stepImpulse = 0;
            foreach (var c in Contacts.Values)
            {
                if (c.StepImpulse <= 0) continue;
                c.PeakForce_N = System.Math.Max(c.PeakForce_N, c.StepImpulse / dt);
                if (c.StepImpulse > c.BestStep) { c.BestStep = c.StepImpulse; c.BestPointWorld = c.StepPointWorld; c.BestPointLocal = transform.InverseTransformPoint(c.StepPointWorld); }
                c.StepImpulse = 0;
            }
        }

        void OnCollisionEnter(Collision c) => Collect(c);
        void OnCollisionStay(Collision c) => Collect(c);

        void Collect(Collision c)
        {
            if (!_recording) return;
            var otherSensor = c.collider.GetComponentInParent<CrashSensor>();
            if (otherSensor != null && otherSensor._body == _body) return; // eigenes Ragdoll
            var surface = c.collider.GetComponentInParent<CrashSurface>();
            if (surface != null && !surface.countsForInjury) return;
            float impulse = c.impulse.magnitude;
            if (impulse <= 0) return;
            _stepImpulse += impulse;
            string kind = surface != null ? surface.kind : "vehicle";
            if (!Contacts.TryGetValue(kind, out var sc)) Contacts[kind] = sc = new SurfaceContact();
            sc.Impulse_Ns += impulse;
            sc.StepImpulse += impulse;
            if (c.contactCount > 0)
            {
                var cp = c.GetContact(0);
                sc.StepPointWorld = cp.point;
                Vector3 rel = c.relativeVelocity;
                float slide = (rel - Vector3.Dot(rel, cp.normal) * cp.normal).magnitude;   // Gleiten entlang der Fläche
                sc.MaxSlide_m_s = System.Math.Max(sc.MaxSlide_m_s, slide);
            }
            if (c.contactCount > 0 && _body != null)
            {
                float side = Vector3.Dot(c.GetContact(0).point - transform.position, _body.right);
                LateralImpulse += Mathf.Sign(side) * impulse;
                TotalImpulse += impulse;
            }
        }
    }
}

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Innere Messgrößen wie bei Dummy-Messdosen: Brustkorbdeflexion (Sternum gegen Wirbelsäule),
    /// Halskraft und -moment (Kopf-Rumpf-Gelenk) sowie axiale Femurkraft (rechtes Hüftgelenk).
    /// </summary>
    public class CrashGauges : MonoBehaviour
    {
        public readonly System.Collections.Generic.List<double> ChestDeflection_m = new System.Collections.Generic.List<double>();
        public readonly System.Collections.Generic.List<double> NeckFz_N = new System.Collections.Generic.List<double>();
        public readonly System.Collections.Generic.List<double> NeckMy_Nm = new System.Collections.Generic.List<double>();
        public readonly System.Collections.Generic.List<double> FemurAxialR_N = new System.Collections.Generic.List<double>();

        Ragdoll _rd;
        bool _recording;

        public void Begin(Ragdoll ragdoll)
        {
            _rd = ragdoll;
            ChestDeflection_m.Clear(); NeckFz_N.Clear(); NeckMy_Nm.Clear(); FemurAxialR_N.Clear();
            _recording = true;
        }

        public void Stop() => _recording = false;

        void FixedUpdate()
        {
            if (!_recording || _rd == null || _rd.Sternum == null) return;
            var thorax = _rd.Thorax.transform;
            // Deflexion: wie weit das Sternum gegenüber der Ruhelage nach hinten gedrückt ist (positiv = eingedrückt)
            double offset = Vector3.Dot(_rd.Sternum.position - _rd.Thorax.position, thorax.forward);
            ChestDeflection_m.Add(System.Math.Max(0, _rd.SternumRestOffset - offset));

            // VERIFY in Unity: Joint.currentForce/currentTorque sind die Kraft bzw. das Moment, das das Gelenk auf den
            // verbundenen Körper (hier Thorax bzw. Becken) ausübt, in Weltkoordinaten. Vorzeichen von Zug/Flexion prüfen.
            var head = _rd.Head.transform;
            NeckFz_N.Add(Vector3.Dot(_rd.NeckJoint.currentForce, head.up));      // > 0: Zug
            NeckMy_Nm.Add(Vector3.Dot(_rd.NeckJoint.currentTorque, head.right)); // > 0: Flexion (Kinn zur Brust)
            var thigh = _rd.Segments["Oberschenkel rechts"].transform;
            FemurAxialR_N.Add(Vector3.Dot(_rd.HipJointRight.currentForce, thigh.up));
        }
    }
}

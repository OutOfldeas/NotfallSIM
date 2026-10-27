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

        Rigidbody _rb;
        Transform _body;
        Vector3 _prevV;
        float _stepImpulse;
        bool _recording;

        public void Configure(string regionName, Transform bodyRoot) { region = regionName; _body = bodyRoot; }

        void Awake() { _rb = GetComponent<Rigidbody>(); }

        public void StartRecording()
        {
            Accel_g.Clear(); Force_kN.Clear(); LateralImpulse = TotalImpulse = 0;
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
            if (c.contactCount > 0 && _body != null)
            {
                float side = Vector3.Dot(c.GetContact(0).point - transform.position, _body.right);
                LateralImpulse += Mathf.Sign(side) * impulse;
                TotalImpulse += impulse;
            }
        }
    }
}

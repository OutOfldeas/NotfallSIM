using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Sichtbare Frakturen: Nach der Rechnung fragt das Rig die Sandbox, welche Verletzungen für diese Person gewürfelt
    /// werden (<see cref="InjuryOracle"/> — derselbe Startwert wie später für den Schockraum, also dieselben Verletzungen).
    /// Für Schaftfrakturen bestimmt die Kontaktkraft am Segment Zeitpunkt (Spitze) und Richtung (Anprallseite);
    /// <see cref="FractureDisplay"/> zeigt die Fehlstellung beim Abspielen.
    /// </summary>
    public partial class CrashRig
    {
        /// <summary>Gewürfelte Verletzungen (Ids) einer Person aus ihren Rohmesswerten; von der Sandbox gesetzt.</summary>
        public Func<Ragdoll, List<CrashMeasurement>, ICollection<string>> InjuryOracle;

        void PrepareFractures(Ragdoll rd, List<CrashMeasurement> measured)
        {
            if (rd == null || InjuryOracle == null || measured == null) return;
            ICollection<string> ids;
            try { ids = InjuryOracle(rd, measured); }
            catch (Exception e) { Debug.LogWarning("[NotfallSIM] Frakturen: " + e.Message); return; }
            var specs = FractureSpecs.For(ids);
            if (specs.Count == 0) return;

            Func<string, CrashSensor> sensor = seg => rd.Segments.ContainsKey(seg) ? rd.Segments[seg].GetComponent<CrashSensor>() : null;
            Func<string, float> when = seg =>
            {
                // Spitze der Kontaktkraft am Segment (beim Femur notfalls am Unterschenkel: Knie gegen Armaturenbrett)
                foreach (var name in new[] { seg, seg.Replace("Oberschenkel", "Unterschenkel") })
                {
                    var s = sensor(name);
                    if (s == null || s.Force_kN.Count == 0) continue;
                    int best = 0;
                    for (int i = 1; i < s.Force_kN.Count; i++) if (s.Force_kN[i] > s.Force_kN[best]) best = i;
                    if (s.Force_kN[best] > 0.2) return CrashStart + (float)(best * s.Dt_s);
                }
                return CrashStart + 0.05f;
            };
            Func<string, Vector3> toward = seg =>
            {
                // Ort des stärksten Kontakts (im Segment) — von dort kam der Stoß
                var s = sensor(seg);
                if (s == null || s.Contacts.Count == 0) return Vector3.zero;
                var c = s.Contacts.Values.OrderByDescending(x => x.Impulse_Ns).First();
                return c.BestPointLocal;
            };
            var fd = FractureDisplay.Attach(rd, specs, when, toward, () => PlaybackTime);
            if (fd == null) return;
            foreach (var b in fd.Breaks)
                _glassEvents.Add(new GlassEvent { Time = b.time, Text = $"Fraktur {b.label}: sichtbare Fehlstellung (Achsenknick)" });
            _glassEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
        }
    }
}

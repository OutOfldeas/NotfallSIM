using System;
using System.Collections.Generic;
using System.Linq;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Welche gewürfelten Verletzungen als Fehlstellung sichtbar werden: Segment, Ort der Bruchstelle (Anteil vom proximalen
    /// Gelenk), größter Achsenknick und die Segmente distal davon. Winkel: **Annahmen** (Darstellung), typische sichtbare
    /// Fehlstellungen geschlossener Schaftfrakturen.
    /// </summary>
    public static class FractureSpecs
    {
        public class Spec { public string InjuryId, Segment, Label; public float Fraction, Angle_deg; public string[] Distal; }

        public static readonly Spec[] All =
        {
            new Spec { InjuryId = "femur_schaft_rechts", Segment = "Oberschenkel rechts", Label = "Femurschaft rechts", Fraction = 0.5f, Angle_deg = 18, Distal = new[] { "Unterschenkel rechts" } },
            new Spec { InjuryId = "femur_schaft_links", Segment = "Oberschenkel links", Label = "Femurschaft links", Fraction = 0.5f, Angle_deg = 18, Distal = new[] { "Unterschenkel links" } },
            new Spec { InjuryId = "unterschenkelfraktur_rechts", Segment = "Unterschenkel rechts", Label = "Unterschenkel rechts", Fraction = 0.55f, Angle_deg = 22, Distal = new string[0] },
            new Spec { InjuryId = "unterschenkelfraktur_links", Segment = "Unterschenkel links", Label = "Unterschenkel links", Fraction = 0.55f, Angle_deg = 22, Distal = new string[0] },
            new Spec { InjuryId = "unterschenkel_offen_rechts", Segment = "Unterschenkel rechts", Label = "Unterschenkel rechts (offen)", Fraction = 0.55f, Angle_deg = 28, Distal = new string[0] },
        };

        /// <summary>Proximal zuerst (Oberschenkel vor Unterschenkel), damit sich Brüche am selben Bein addieren.</summary>
        public static List<Spec> For(IEnumerable<string> injuryIds)
        {
            var ids = new HashSet<string>(injuryIds ?? new string[0]);
            return All.Where(s => ids.Contains(s.InjuryId)).GroupBy(s => s.Segment).Select(g => g.OrderByDescending(s => s.Angle_deg).First())
                      .OrderBy(s => s.Segment.StartsWith("Oberschenkel") ? 0 : 1).ToList();
        }

        /// <summary>Anteil der Haut, die am distalen Fragment hängt: weicher Übergang ±3 cm um die Bruchstelle.</summary>
        public static float DistalShare(float alongFromBreak_m, float halfWidth_m = 0.03f)
        {
            float u = Math.Max(0, Math.Min(1, (alongFromBreak_m + halfWidth_m) / (2 * halfWidth_m)));
            return u * u * (3 - 2 * u);
        }

        /// <summary>Knick wächst in 15 ms auf den vollen Winkel (Bruch und Verschiebung der Fragmente).</summary>
        public static float AngleAt(float t, float breakTime, float angle_deg)
        {
            float u = Math.Max(0, Math.Min(1, (t - breakTime) / 0.015f));
            return angle_deg * u * u * (3 - 2 * u);
        }
    }
}

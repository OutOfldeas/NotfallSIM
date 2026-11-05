using System;
using System.Linq;

namespace NotfallSIM.Data
{
    // ---------- rescue.json ----------
    [Serializable] public class RescueMethod { public string id, label, note; [System.Runtime.Serialization.OptionalField] public string sourceId; public double minutes; }
    [Serializable] public class RescueSource { public string id, citation, url, note; }
    [Serializable]
    public class RescueFile
    {
        public string version, note;
        public double trappedFootwell_m, trappedSide_m, doorJam_m, trappedExtra_min, jammedExtra_min, arrival_min, care_min, transport_min;
        public RescueMethod[] methods;
        public RescueSource[] sources;
    }

    /// <summary>Lage einer Person im Fahrzeug nach dem Crash.</summary>
    public class RescueSituation
    {
        /// <summary>Körperlich eingeklemmt: Verformung hält den Körper fest (Fußraum bzw. Tür am Sitzplatz).</summary>
        public bool Trapped;
        /// <summary>Eingeschlossen: die eigene Tür klemmt (Verformung an der Tür).</summary>
        public bool Jammed;
        /// <summary>„Medizinisch eingeklemmt“: nicht eingeklemmt, kann aber wegen Verletzung/Bewusstsein nicht selbst heraus.</summary>
        public bool CannotSelfExtricate;
        public double Side_m, Footwell_m, Door_m;
        public bool Pedestrian;
        public string Text;
    }

    public class RescuePlan
    {
        public string MethodId, Label, Note;
        public double Extrication_min, Total_min;
        /// <summary>Gewählte Art war nicht möglich (z. B. Sofortrettung bei Einklemmung) und wurde ersetzt.</summary>
        public bool Replaced;
    }

    /// <summary>
    /// Eingeklemmt und technische Rettung (rescue.json): aus der Verformung am Sitzplatz, ob die Person eingeklemmt oder
    /// eingeschlossen ist, welche Rettungsart möglich ist und wie lange es bis zum Schockraum dauert
    /// (Eintreffen + Befreiung + Versorgung + Transport). Reines C#.
    /// </summary>
    public static class Rescue
    {
        public static RescueSituation Situation(RescueFile f, double side_m, double footwell_m, double door_m, bool walks)
        {
            var s = new RescueSituation { Side_m = side_m, Footwell_m = footwell_m, Door_m = door_m };
            s.Trapped = footwell_m >= f.trappedFootwell_m || side_m >= f.trappedSide_m;
            s.Jammed = !s.Trapped && door_m >= f.doorJam_m;
            s.CannotSelfExtricate = !s.Trapped && !walks;
            s.Text = s.Trapped
                ? "eingeklemmt (" + string.Join(", ", new[] { footwell_m >= f.trappedFootwell_m ? $"Fußraum {footwell_m * 100:0} cm eingedrückt" : null,
                                                           side_m >= f.trappedSide_m ? $"Tür {side_m * 100:0} cm eingedrückt" : null }.Where(x => x != null)) + ")"
                : s.Jammed ? $"eingeschlossen (Tür klemmt, {door_m * 100:0} cm verformt)"
                : s.CannotSelfExtricate ? "nicht eingeklemmt, kann aber nicht selbst aussteigen"
                : "nicht eingeklemmt, kann aussteigen";
            return s;
        }

        /// <summary>
        /// Verformung am Sitzplatz aus den Seiten des Fahrzeugmodells: Eindringen der eigenen Seite am Sitz, Verformung an der
        /// eigenen Tür, Fußraum (Front, nur vordere Reihe) — voll am Kontaktort, 1 m daneben nicht mehr (wie die Türen im Rig).
        /// </summary>
        public static void SeatDeformation(Crash.TrafficSim.FaceState near, Crash.TrafficSim.FaceState front, bool frontRow,
                                           double seatX, double seatZ, double doorZ, out double side_m, out double footwell_m, out double door_m)
        {
            Func<double, double, double> fall = (a, c) => Math.Max(0, 1 - Math.Abs(a - c) / 1.0);
            side_m = near.Intrusion * fall(seatZ, near.Position);
            door_m = near.MaxCrush * fall(doorZ, near.Position);
            footwell_m = frontRow ? front.Intrusion * fall(seatX, front.Position) : 0;
        }

        public static RescueSituation PedestrianSituation() => new RescueSituation { Pedestrian = true, Text = "Fußgänger: liegt frei zugänglich" };

        /// <summary>Übliche Wahl: eingeklemmt → schonend; sonst gehfähig → selbst, kritisch (rot) → sofort, sonst schnell.</summary>
        public static string AutoMethod(RescueSituation s, bool walks, bool critical)
        {
            if (s.Pedestrian) return "";
            if (s.Trapped) return "schonend";
            if (walks && !s.Jammed) return "selbst";
            if (critical) return "sofort";
            return "schnell";
        }

        public static RescuePlan Plan(RescueFile f, RescueSituation s, string methodId, bool walks)
        {
            var fixedTime = f.arrival_min + f.care_min + f.transport_min;
            if (s.Pedestrian)
                return new RescuePlan { MethodId = "", Label = "keine Befreiung nötig", Extrication_min = 0, Total_min = fixedTime };
            string id = methodId;
            bool replaced = false;
            // Unmögliches ersetzen: eingeklemmt nur schonend/schnell; Selbstbefreiung nur gehfähig und ohne klemmende Tür
            if (s.Trapped && (id == "sofort" || id == "selbst")) { id = "schnell"; replaced = true; }
            if (id == "selbst" && (!walks || s.Jammed)) { id = "schnell"; replaced = true; }
            var m = f.methods.FirstOrDefault(x => x.id == id) ?? f.methods.Last();
            double extr = m.minutes + (s.Trapped ? f.trappedExtra_min : 0) + (s.Jammed && id != "schonend" ? f.jammedExtra_min : 0);
            return new RescuePlan
            {
                MethodId = m.id, Label = m.label, Note = m.note, Replaced = replaced,
                Extrication_min = extr, Total_min = fixedTime + extr
            };
        }
    }
}

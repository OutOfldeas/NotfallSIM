using System;
using System.Linq;

namespace NotfallSIM.Crash
{
    // ---------- Datenmodell (muscle_tone.json) ----------

    [Serializable]
    public class MuscleActivationParams
    {
        public double baseline;            // Haltetonus (Anteil MVC)
        public string baselineSourceId;
        public double reflexLatency_s;     // Muskelantwort nach Beginn der Belastung
        public string reflexLatencySourceId;
        public double reflexRise_s;        // Anstieg vom Grundtonus auf das Reflexniveau
        public double reflexLevel;         // Aktivierung im Reflex (Anteil MVC)
        public string reflexLevelSourceId;
        public double bracedLevel;         // Aktivierung beim Abstützen (Anteil MVC)
        public string bracedLevelSourceId;
        public string note;
    }

    [Serializable]
    public class MuscleJoint
    {
        public string segment, label;       // Segment des Ragdolls (das Gelenk zum Elternsegment)
        public double male_Nm, female_Nm;   // willkürliches isometrisches Maximalmoment
        public string sourceId, note;
    }

    [Serializable]
    public class MuscleToneSource
    {
        public string id, citation, url, note;
    }

    [Serializable]
    public class MuscleToneFile
    {
        public string version, note;
        public double angleAtMaxTorque_deg;
        public MuscleActivationParams activation;
        public MuscleJoint[] joints;
        public MuscleToneSource[] sources;
    }

    /// <summary>Zustand des Insassen beim Aufprall.</summary>
    public enum OccupantState
    {
        Bewusstlos,   // nur passiver Tonus
        Entspannt,    // unvorbereitet: Grundtonus, Reflex nach der Latenz
        Angespannt    // sieht den Aufprall kommen und stützt sich ab: Aktivierung von Beginn an
    }

    /// <summary>
    /// Muskeltonus im Ragdoll (ohne Unity, getestet): Aktivierung über die Zeit je Zustand und Haltemoment je Gelenk.
    /// </summary>
    public static class MuscleTone
    {
        public static string Label(OccupantState s) =>
            s == OccupantState.Bewusstlos ? "bewusstlos" : s == OccupantState.Entspannt ? "entspannt (unvorbereitet)" : "angespannt (abgestützt)";

        /// <summary>Aktivierung (Anteil MVC) t Sekunden nach Beginn des Crashs.</summary>
        public static double Activation(MuscleActivationParams a, OccupantState s, double t)
        {
            switch (s)
            {
                case OccupantState.Bewusstlos: return 0;
                case OccupantState.Angespannt: return a.bracedLevel;
                default:
                    if (t < a.reflexLatency_s) return a.baseline;
                    double f = a.reflexRise_s > 0 ? Math.Min(1, (t - a.reflexLatency_s) / a.reflexRise_s) : 1;
                    return a.baseline + (a.reflexLevel - a.baseline) * f;
            }
        }

        /// <summary>Maximalmoment eines Gelenks (Nm), junge Erwachsene nach Geschlecht. Keine Skalierung auf Körpergröße
        /// oder -gewicht, weil die Quellen die Bezugswerte ihrer Probanden hier nicht liefern.</summary>
        public static double MaxTorque(MuscleToneFile f, string segment, bool male)
        {
            var j = f.joints.FirstOrDefault(x => x.segment == segment);
            if (j == null) return 0;
            return male ? j.male_Nm : j.female_Nm;
        }

        /// <summary>Haltemoment, Federsteifigkeit (Nm/rad) des Antriebs bei Aktivierung a.</summary>
        public static (double maxTorque_Nm, double stiffness_Nm_per_rad) Drive(MuscleToneFile f, string segment, bool male, double activation)
        {
            double t = MaxTorque(f, segment, male) * activation;
            double angle = f.angleAtMaxTorque_deg * Math.PI / 180;
            return (t, angle > 0 ? t / angle : 0);
        }
    }
}

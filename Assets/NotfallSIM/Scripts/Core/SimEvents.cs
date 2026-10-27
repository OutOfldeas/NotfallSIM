using System;

namespace NotfallSIM.Core
{
    /// <summary>Basisdaten des simulierten Patienten.</summary>
    [Serializable]
    public class PatientProfile
    {
        public string sex = "male"; // "male" | "female" (String statt Enum: lesbarer im JSON)
        public int age = 35;
        public double weight_kg = 75;
        public double height_cm = 180;

        public bool IsMale => !string.Equals(sex, "female", StringComparison.OrdinalIgnoreCase);

        /// <summary>Grobe Schätzung des Blutvolumens (~70 mL/kg Mann, ~65 mL/kg Frau).</summary>
        public double EstimatedBloodVolume_mL => weight_kg * (IsMale ? 70 : 65);
    }

    public enum InjuryEffectType
    {
        Hemorrhage,
        TensionPneumothorax, // noch nicht implementiert (Phase 2)
        TraumaticBrainInjury // noch nicht implementiert (Phase 2)
    }

    /// <summary>
    /// Physiologischer Effekt einer Verletzung, so wie ihn die Engine verarbeitet.
    /// Kommt aus der Verletzungsdatenbank, skaliert mit dem Schweregrad aus dem Szenario.
    /// </summary>
    public class InjuryEvent
    {
        public string InjuryId;
        public InjuryEffectType Effect;
        /// <summary>Interner Name des Blutungsorts (z. B. "Pelvis"). Engines mappen ihn selbst.</summary>
        public string Compartment;
        public double Severity;          // 0..1
        public double BaseRate_mL_min;   // Blutungsrate bei Severity 1 und normalem Blutdruck
        public double MaxTotalLoss_mL;   // Obergrenze (z. B. Tamponade-Effekt), 0 = unbegrenzt
    }

    public enum InterventionType
    {
        HemorrhageControl, // Beckengurt, Tourniquet, Druckverband
        FluidInfusion,     // Kristalloide, Blut
        Oxygen
    }

    public class InterventionEvent
    {
        public InterventionType Type;
        public string TargetCompartment;  // für HemorrhageControl
        public double RateMultiplier = 1; // für HemorrhageControl: 0 = Blutung gestoppt
        public string Compound;           // für FluidInfusion: "Saline", "Blood"
        public double Volume_mL;
        public double Rate_mL_min;
    }
}

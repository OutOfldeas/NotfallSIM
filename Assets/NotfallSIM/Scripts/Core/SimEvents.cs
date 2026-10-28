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
        public bool betaBlocker;    // Dauermedikation: dämpft die Tachykardie
        public double initialBloodLoss_mL; // Blutverlust vor Ankunft im Schockraum (präklinische Phase)

        public bool IsMale => !string.Equals(sex, "female", StringComparison.OrdinalIgnoreCase);

        /// <summary>Grobe Schätzung des Blutvolumens (~70 mL/kg Mann, ~65 mL/kg Frau).</summary>
        public double EstimatedBloodVolume_mL => weight_kg * (IsMale ? 70 : 65);
    }

    public enum InjuryEffectType
    {
        Hemorrhage,
        TensionPneumothorax,
        TraumaticBrainInjury // raumfordernde Blutung, z. B. Epiduralhämatom
    }

    /// <summary>
    /// Physiologischer Effekt einer Verletzung, so wie ihn die Engine verarbeitet.
    /// Kommt aus der Verletzungsdatenbank, skaliert mit dem Schweregrad aus dem Szenario.
    /// </summary>
    public class InjuryEvent
    {
        public string InjuryId;
        public InjuryEffectType Effect;
        /// <summary>Interner Ort, z. B. "Pelvis" oder "LeftLung". Engines mappen ihn selbst.</summary>
        public string Compartment;
        public double Severity;          // 0..1
        public double BaseRate_mL_min;   // Blutung: Rate bei Severity 1 und normalem Blutdruck
        public double MaxTotalLoss_mL;   // Blutung: Tamponadevolumen eines geschlossenen Raums (Becken, Oberschenkel), 0 = ohne
        /// <summary>Art der Gefäßläsion: "partial" (seitlicher Einriss, zieht sich nicht zurück), "transection" (vollständig
        /// durchtrennt: Retraktion, Gerinnsel möglich), "parenchymal" (Organ), "venous"; leer = ohne Gerinnsel.</summary>
        public string Lesion;
        /// <summary>Fortschreitende Verletzungen (Spannungspneu): Anteil pro Minute bei Severity 1 bis zur Dekompensation.</summary>
        public double ProgressionRate_per_min;
        /// <summary>Schwere bei Beginn (0..1), z. B. akutes Subduralhämatom: von Anfang an eingetrübt.</summary>
        public double InitialLevel;
    }

    public enum InterventionType
    {
        HemorrhageControl,   // Beckengurt, Tourniquet, Druckverband
        FluidInfusion,       // Kristalloide, Blut
        Oxygen,
        NeedleDecompression, // Entlastungspunktion, kann versagen
        ChestDrain,          // Thoraxdrainage, definitive Entlastung
        Medication,          // z. B. Tranexamsäure; im Mock ohne physiologischen Effekt
        Intubation,          // Atemweg gesichert, kontrollierte Beatmung
        Osmotherapy,         // hypertone Kochsalzlösung / Mannitol, überbrückend
        NeurosurgicalDecompression, // Kraniotomie; wirkt mit Verzögerung (CT, Transport, OP)
        BloodGas,            // BGA: Ergebnis nach Verzögerung, ohne physiologischen Effekt
        Supportive,          // Wärmeerhalt, Analgesie u. Ä.; im Mock ohne physiologischen Effekt
        Vasopressor          // Noradrenalin: hebt den Blutdruck, erhöht damit aber auch den Blutfluss aus Läsionen
    }

    public class InterventionEvent
    {
        public string Id;                 // ID aus interventions.json, Teil des Protokoll-Tags
        public InterventionType Type;
        public string TargetCompartment;  // HemorrhageControl: Blutungsort; Entlastung: "LeftLung"/"RightLung"
        public double RateMultiplier = 1; // für HemorrhageControl: 0 = Blutung gestoppt
        public double SuccessProbability = 1; // für NeedleDecompression
        public string Compound;           // FluidInfusion: "BalancedCrystalloid", "Blood"; Medication: z. B. "TXA"
        public double Volume_mL;
        public double Rate_mL_min;
    }
}

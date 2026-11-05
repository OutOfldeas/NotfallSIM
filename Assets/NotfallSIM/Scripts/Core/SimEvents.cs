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
        [System.Runtime.Serialization.OptionalField] public double initialTemperature_C; // Kerntemperatur bei Ankunft (0 = 36,5 °C)
        [System.Runtime.Serialization.OptionalField] public bool noPrehospitalAccess;   // true: ohne liegenden Zugang (sonst präklinisch gelegt)

        public bool IsMale => !string.Equals(sex, "female", StringComparison.OrdinalIgnoreCase);

        public double Bmi => BodyComposition.Bmi(weight_kg, height_cm);

        /// <summary>Blutvolumen: ~70 mL/kg Mann, ~65 mL/kg Frau, bei BMI über 22 nach Lemmens 2006 verringert (<see cref="BodyComposition.BloodVolume_mL"/>).</summary>
        public double EstimatedBloodVolume_mL => BodyComposition.BloodVolume_mL(weight_kg, Bmi, IsMale);
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
        Supportive,          // Wärmeerhalt, Infusionswärmer, Blasenkatheter u. Ä. (Compound bestimmt die Wirkung)
        Vasopressor,         // Noradrenalin: hebt den Blutdruck, erhöht damit aber auch den Blutfluss aus Läsionen
        Positioning,         // Lagerung (Ziel = Lagerungs-ID): Höhe der Blutungsorte über dem Herzen, Autotransfusion
        VascularAccess       // Gefäßzugang (Compound "iv" oder "io"): nur im Ablauf, ohne Physiologie; Infusionen und Medikamente brauchen ihn
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
        public double DoseFactor = 1;     // gewählte Dosis / Referenzdosis (Medikamente, Noradrenalin); 1 = Standarddosis
        public double DoseValue;          // gewählter Wert in der Einheit der Dosis (z. B. Kanülenlänge in cm); 0 = Standard
        public string Site;               // Punktionsort, z. B. "aal" (4./5. ICR vordere Axillarlinie); leer = Standard
    }
}

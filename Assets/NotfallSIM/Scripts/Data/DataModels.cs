using System;
using NotfallSIM.Core;

namespace NotfallSIM.Data
{
    // Datenklassen, die 1:1 den JSON-Dateien in StreamingAssets/NotfallSIM entsprechen.
    // Unitys JsonUtility braucht öffentliche Felder und Arrays (keine Dictionaries).

    [Serializable]
    public class SourceRef
    {
        public string citation;   // vollständige Literaturangabe
        public string url;        // DOI-Link oder Fundstelle
        public string note;       // wofür genau die Quelle steht
    }

    [Serializable]
    public class PhysiologyEffect
    {
        public string effectType;         // muss einem InjuryEffectType entsprechen, z. B. "Hemorrhage"
        public string compartment;        // interner Blutungsort, z. B. "Pelvis"
        public double baseRate_mL_min;
        public double maxTotalLoss_mL;
        public double progressionRate_per_min; // nur für fortschreitende Effekte (Spannungspneu)
    }

    [Serializable]
    public class InjuryDefinition
    {
        public string id;
        public string name;
        public string region;
        public string icd10;
        public int aisSeverity;           // 1–6 (nur Schweregrad; AIS-Codes selbst sind lizenzpflichtig)
        public PhysiologyEffect[] effects;
        public string[] clinicalFindings; // für den späteren Diagnose-Modus
        public SourceRef[] sources;
        public string evidenceStatus;     // "draft" | "reviewed" | "validated"
        public string reviewNotes;
    }

    [Serializable]
    public class InjuryDatabaseFile
    {
        public string version;
        public InjuryDefinition[] injuries;
    }

    [Serializable]
    public class ScenarioInjury
    {
        public string injuryId;
        public double onsetTime_s;
        public double severity;
    }

    [Serializable]
    public class ScenarioDefinition
    {
        public string id;
        public string title;
        public string description;
        public string mechanism;
        public PatientProfile patient;
        public ScenarioInjury[] injuries;
        public string[] learningGoals;
        public string expectedCourse;
        public double defaultTimeScale = 1;
    }
}

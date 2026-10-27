using System;
using System.Collections.Generic;
using System.Linq;
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
        public double progressionRate_per_min; // nur für fortschreitende Effekte (Spannungspneu, SHT)
        public double initialLevel;            // Schwere bei Beginn (0..1), z. B. akutes Subduralhämatom
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
        public ImagingFinding[] imaging;  // Befunde je Bildgebungsverfahren
        public SourceRef[] sources;
        public string evidenceStatus;     // "draft" | "reviewed" | "validated"
        public string reviewNotes;
        public AssessmentCriterion[] criteria; // Bewertungsvorlagen für generierte Szenarien
    }

    [Serializable]
    public class ImagingFinding
    {
        public string modality;        // "eFAST", "XrayChest", "XrayPelvis", "CCT", "CTBody"
        public string finding;
        public string resolvedByTag;   // optional: nach diesem Ereignis gilt resolvedFinding (z. B. Drainage gelegt)
        public string resolvedFinding;
        public string image;           // optional: Dateiname eines lizenzierten Bildes
        public string imageCredit;     // Urheber und Lizenz
    }

    [Serializable]
    public class ImagingModality
    {
        public string id;
        public string label;
        public string logLabel;
        public string[] covers;        // welche Befundkategorien das Verfahren zeigt
        public double duration_s;      // Untersuchung bis vorläufiger Befund
        public double transport_s;     // Hin- und Rücktransport, entfällt bei CT im Schockraum oder am Bett
        public bool isCT;
        public string normalFinding;
        public SourceRef[] sources;
        public string evidenceStatus;
        public string reviewNotes;
    }

    [Serializable]
    public class ImagingDatabaseFile
    {
        public string version;
        public ImagingModality[] modalities;
    }

    /// <summary>Stellt den Befundtext eines Verfahrens zusammen. Reines C#, testbar.</summary>
    public static class ImagingReport
    {
        public static List<string> Build(ImagingModality modality, IEnumerable<InjuryDefinition> activeInjuries, IEnumerable<string> occurredTags)
        {
            var tags = occurredTags.Where(t => t != null).ToList();
            var lines = new List<string>();
            foreach (var inj in activeInjuries)
                foreach (var f in inj.imaging ?? new ImagingFinding[0])
                {
                    if (!modality.covers.Contains(f.modality)) continue;
                    bool resolved = !string.IsNullOrEmpty(f.resolvedByTag) && tags.Any(t => t.StartsWith(f.resolvedByTag, StringComparison.Ordinal));
                    lines.Add(resolved && !string.IsNullOrEmpty(f.resolvedFinding) ? f.resolvedFinding : f.finding);
                }
            if (lines.Count == 0) lines.Add(modality.normalFinding);
            return lines;
        }
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
        public AssessmentCriterion[] assessment;
    }

    /// <summary>Eine Maßnahme, wie sie als Button erscheint und an die Engine geht.</summary>
    [Serializable]
    public class InterventionDefinition
    {
        public string id;
        public string label;        // Text auf dem Button
        public string logLabel;     // Text im Protokoll
        public string type;         // muss einem InterventionType entsprechen
        public string target;       // Blutungsort bzw. Lunge
        public string compound;     // Infusion oder Medikament
        public double volume_mL;
        public double rate_mL_min;
        public double rateMultiplier = 1;
        public double successProbability = 1;
        public bool once;           // nur einmal pro Durchlauf möglich
        public double effectDelay_s; // Zeit von Anordnung bis Wirkung (0 = sofort)
        public SourceRef[] sources;
        public string evidenceStatus;
        public string reviewNotes;

        /// <summary>Übersetzt in ein Engine-Ereignis; null bei unbekanntem Typ.</summary>
        public InterventionEvent ToEvent()
        {
            if (!Enum.TryParse(type, out InterventionType t)) return null;
            return new InterventionEvent
            {
                Id = id,
                Type = t,
                TargetCompartment = string.IsNullOrEmpty(target) ? null : target,
                Compound = string.IsNullOrEmpty(compound) ? null : compound,
                Volume_mL = volume_mL,
                Rate_mL_min = rate_mL_min,
                RateMultiplier = rateMultiplier,
                SuccessProbability = successProbability
            };
        }
    }

    [Serializable]
    public class InterventionDatabaseFile
    {
        public string version;
        public InterventionDefinition[] interventions;
    }

    /// <summary>
    /// Ein Bewertungskriterium fürs Debriefing. Bezieht sich auf Ereignis-Tags
    /// (Präfix-Vergleich, z. B. "intervention:FluidInfusion:Blood" oder "exam:Becken").
    /// </summary>
    [Serializable]
    public class AssessmentCriterion
    {
        public string id;
        public string description;
        /// <summary>"deadline": eines der Tags muss bis limit Sekunden auftreten. "maxCount": höchstens limit Mal.</summary>
        public string type;
        public string[] tags;
        public double limit;
        /// <summary>Optional: Tag-Präfix, ab dessen erstem Auftreten "deadline" zählt (z. B. Beginn einer Verletzung).
        /// Tritt es nie auf, ist das Kriterium nicht anwendbar.</summary>
        public string relativeTo;
        /// <summary>Nur bei relativeTo: true = nur Maßnahmen ab dem Bezugsereignis zählen (z. B. Re-Evaluation).
        /// false = frühere Maßnahmen gelten als rechtzeitig (z. B. Neurochirurgie schon vor der Eintrübung gerufen).</summary>
        public bool onlyAfterReference;
        public bool critical;
        public string rationale;
    }

    // ---------- Unfallmechanismen (mechanisms.json) ----------

    [Serializable]
    public class MechanismInjury
    {
        public string injuryId;
        public double probability;      // didaktische Setzung, siehe reviewNotes
        public double severityMin;
        public double severityMax;
        public double onsetMin_s;
        public double onsetMax_s;
    }

    [Serializable]
    public class InjuryFactor
    {
        public string injuryId;
        public double factor;
    }

    [Serializable]
    public class MechanismDefinition
    {
        public string id;
        public string label;
        public string descriptionTemplate; // Platzhalter: {age} {r} {in} {driver} {belt} {speed} {height} {rescue}
        public double speedMin, speedMax;    // km/h (0 = nicht relevant)
        public double heightMin, heightMax;  // m (Sturz)
        public double ageMin, ageMax;
        public double maleShare;
        public double rescueMin, rescueMax;  // min bis Schockraum
        public double unrestrainedShare;     // Anteil nicht angeschnallt (nur PKW)
        public MechanismInjury[] injuries;
        public InjuryFactor[] unrestrainedMultipliers;
        public SourceRef[] sources;
        public string evidenceStatus;
        public string reviewNotes;
    }

    [Serializable]
    public class ExclusiveGroup
    {
        public string[] injuryIds; // höchstens eine dieser Verletzungen pro Szenario
    }

    [Serializable]
    public class Anthropometry
    {
        public double maleWeightMin_kg, maleWeightMax_kg, maleHeightMin_cm, maleHeightMax_cm;
        public double femaleWeightMin_kg, femaleWeightMax_kg, femaleHeightMin_cm, femaleHeightMax_cm;
        public double betaBlockerMinAge, betaBlockerShare;
    }

    [Serializable]
    public class PrehospitalSettings
    {
        public double lossFactor;
        public double maxLoss_mL;
        public double roundTo_mL;
        public double reportThreshold_mL; // ab hier in der Übergabe erwähnt
        public string note;
    }

    [Serializable]
    public class MechanismDatabaseFile
    {
        public string version;
        public string[] learningGoals;
        public Anthropometry anthropometry;
        public PrehospitalSettings prehospital;
        public AssessmentCriterion[] baseCriteria;
        public ExclusiveGroup[] exclusiveGroups;
        public MechanismDefinition[] mechanisms;
    }
}

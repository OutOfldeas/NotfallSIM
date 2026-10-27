// Wird nur kompiliert, wenn das Scripting-Define NOTFALLSIM_PULSE gesetzt ist
// (Project Settings > Player > Scripting Define Symbols) — erst nach Import des Pulse Unity Assets.
#if NOTFALLSIM_PULSE
using System;
using System.Collections.Generic;
using NotfallSIM.Core;
using Pulse;
using Pulse.CDM;
using UnityEngine;

namespace NotfallSIM.Engines
{
    /// <summary>
    /// Brücke zur Pulse Physiology Engine (Kitware).
    ///
    /// WICHTIG: Die Pulse C#-API hat sich zwischen Versionen geändert (u. a. wie der
    /// Blutungsort bei SEHemorrhage angegeben wird). Alle Stellen mit "VERIFY" gegen die
    /// HowTo-Beispiele der importierten Asset-Version prüfen (src/csharp/howto im Pulse-Repo).
    /// </summary>
    public class PulsePhysiologyAdapter : IPhysiologyEngine
    {
        public string Name => "Pulse Physiology Engine";
        public VitalSigns Current { get; private set; }

        PulseEngine _pulse;
        double _baselineVolume;
        readonly Dictionary<string, double> _hemorrhageSeverity = new Dictionary<string, double>();

        // Reihenfolge muss zu ReadVitals() passen. PullData() liefert an Index 0 die Simulationszeit.
        static readonly List<SEDataRequest> Requests = new List<SEDataRequest>
        {
            SEDataRequest.CreatePhysiologyDataRequest("HeartRate", FrequencyUnit.Per_min),
            SEDataRequest.CreatePhysiologyDataRequest("SystolicArterialPressure", PressureUnit.mmHg),
            SEDataRequest.CreatePhysiologyDataRequest("DiastolicArterialPressure", PressureUnit.mmHg),
            SEDataRequest.CreatePhysiologyDataRequest("MeanArterialPressure", PressureUnit.mmHg),
            SEDataRequest.CreatePhysiologyDataRequest("RespirationRate", FrequencyUnit.Per_min),
            SEDataRequest.CreatePhysiologyDataRequest("OxygenSaturation"),
            SEDataRequest.CreatePhysiologyDataRequest("EndTidalCarbonDioxidePressure", PressureUnit.mmHg),
            SEDataRequest.CreatePhysiologyDataRequest("BloodVolume", VolumeUnit.mL),
        };

        // Mapping interner Blutungsorte -> Pulse-Kompartimente.
        // Fachliche Näherung, als Entscheidung dokumentieren und validieren:
        // Pulse kennt kein "Becken"; retroperitoneale Beckenblutung hier vorläufig als VenaCava (intern).
        static readonly Dictionary<string, string> CompartmentMap = new Dictionary<string, string>
        {
            { "Pelvis", "VenaCava" },
            { "LeftThigh", "LeftLeg" },
            { "RightThigh", "RightLeg" },
            { "Spleen", "Spleen" },
            { "Liver", "Liver" },
        };

        public bool Initialize(PatientProfile patient)
        {
            // VERIFY: Je nach Asset-Version erwartet der Konstruktor das Pulse-Datenverzeichnis.
            _pulse = new PulseEngine();

            // Schnellster Start: vorberechneten Zustand laden statt Patient neu zu stabilisieren.
            // Eigene Patientenprofile (Gewicht, Alter) brauchen später InitializeEngine + Stabilisierung.
            string stateFile = patient.IsMale ? "StandardMale@0s.json" : "StandardFemale@0s.json";
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, "states", stateFile); // VERIFY Pfad im Asset
            var dataMgr = new SEDataRequestManager(Requests);
            if (!_pulse.SerializeFromFile(path, dataMgr)) // VERIFY Signatur
            {
                Debug.LogError($"[Pulse] Zustand konnte nicht geladen werden: {path}");
                return false;
            }
            Advance(0.02);
            _baselineVolume = Current.BloodVolume_mL;
            return true;
        }

        public void Advance(double dt_s)
        {
            if (!_pulse.AdvanceTime_s(dt_s)) { Debug.LogError("[Pulse] AdvanceTime fehlgeschlagen"); return; }
            List<double> d = _pulse.PullData();
            double bv = d[8];
            Current = new VitalSigns
            {
                SimTime_s = d[0],
                HeartRate_bpm = d[1],
                SystolicBP_mmHg = d[2],
                DiastolicBP_mmHg = d[3],
                MeanArterialPressure_mmHg = d[4],
                RespirationRate_bpm = d[5],
                SpO2_percent = d[6] * 100.0, // Pulse liefert Anteil 0..1
                EtCO2_mmHg = d[7],
                BloodVolume_mL = bv,
                BloodLossFraction = _baselineVolume > 0 ? Math.Max(0, 1 - bv / _baselineVolume) : 0,
                CardiacArrest = d[1] < 1 // TODO: Pulse-Event "CardiacArrest" auswerten
            };
        }

        public void ApplyInjury(InjuryEvent injury)
        {
            switch (injury.Effect)
            {
                case InjuryEffectType.Hemorrhage:
                    SetHemorrhage(injury.Compartment, injury.Severity);
                    break;
                default:
                    // TODO Phase 2: SETensionPneumothorax, SEBrainInjury
                    Debug.LogWarning($"[Pulse] {injury.Effect} noch nicht angebunden.");
                    break;
            }
        }

        public void ApplyIntervention(InterventionEvent iv)
        {
            switch (iv.Type)
            {
                case InterventionType.HemorrhageControl:
                    // Pulse: Blutung im selben Kompartiment mit neuem Schweregrad überschreibt die alte.
                    if (_hemorrhageSeverity.TryGetValue(iv.TargetCompartment, out var sev))
                        SetHemorrhage(iv.TargetCompartment, sev * iv.RateMultiplier);
                    break;
                case InterventionType.FluidInfusion:
                    var infusion = new SESubstanceCompoundInfusion();
                    infusion.SetCompound(iv.Compound); // "Saline", "Blood" (Namen laut Pulse-Substanzdateien, VERIFY)
                    infusion.GetBagVolume().SetValue(iv.Volume_mL, VolumeUnit.mL);
                    infusion.GetRate().SetValue(iv.Rate_mL_min, VolumePerTimeUnit.mL_Per_min);
                    _pulse.ProcessAction(infusion);
                    break;
                case InterventionType.Oxygen:
                    // TODO: SESupplementalOxygen (Gerät z. B. NonRebreatherMask) — Enum-Namen in Asset-Version prüfen.
                    Debug.LogWarning("[Pulse] Sauerstoffgabe noch nicht angebunden.");
                    break;
            }
        }

        void SetHemorrhage(string compartment, double severity)
        {
            if (!CompartmentMap.TryGetValue(compartment, out var pulseCmpt))
            {
                Debug.LogWarning($"[Pulse] Kein Mapping für Blutungsort '{compartment}'.");
                return;
            }
            var h = new SEHemorrhage();
            h.SetComment($"NotfallSIM: {compartment}");
            // VERIFY: Ältere Versionen nahmen einen String, neuere ein Enum
            // (z. B. eHemorrhage_Compartment / eCompartment). An die importierte Version anpassen.
            h.SetCompartment(pulseCmpt);
            h.GetSeverity().SetValue(Math.Max(0, Math.Min(1, severity)));
            _pulse.ProcessAction(h);
            _hemorrhageSeverity[compartment] = severity;
        }
    }
}
#endif

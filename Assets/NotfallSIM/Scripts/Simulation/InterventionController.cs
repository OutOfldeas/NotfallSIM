using NotfallSIM.Core;
using UnityEngine;

namespace NotfallSIM.Simulation
{
    /// <summary>
    /// Öffentliche Methoden für UI-Buttons (Button > OnClick im Inspector verknüpfen).
    /// Die Effektstärken sind Platzhalter und gehören mittelfristig in die Datenbank
    /// (eigene interventions.json mit Quellen), nicht in den Code.
    /// </summary>
    public class InterventionController : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;

        [Header("Beckengurt")]
        [Tooltip("Faktor auf die Beckenblutungsrate. PLATZHALTER — Evidenz zur Größe des Effekts prüfen.")]
        [SerializeField] float pelvicBinderRateMultiplier = 0.5f;

        [Header("Infusion")]
        [SerializeField] float crystalloidVolume_mL = 500;
        [SerializeField] float crystalloidRate_mL_min = 100;  // ca. Druckinfusion
        [SerializeField] float bloodUnitVolume_mL = 300;      // grobe EK-Größe, prüfen
        [SerializeField] float bloodRate_mL_min = 50;

        bool _binderApplied;

        public void ApplyPelvicBinder()
        {
            if (_binderApplied) return;
            _binderApplied = true;
            sim.ApplyIntervention(new InterventionEvent
            {
                Type = InterventionType.HemorrhageControl,
                TargetCompartment = "Pelvis",
                RateMultiplier = pelvicBinderRateMultiplier
            }, "Beckengurt angelegt");
        }

        public void GiveCrystalloid() => sim.ApplyIntervention(new InterventionEvent
        {
            Type = InterventionType.FluidInfusion,
            Compound = "Saline",
            Volume_mL = crystalloidVolume_mL,
            Rate_mL_min = crystalloidRate_mL_min
        }, $"{crystalloidVolume_mL:F0} mL Kristalloid");

        public void GiveBloodUnit() => sim.ApplyIntervention(new InterventionEvent
        {
            Type = InterventionType.FluidInfusion,
            Compound = "Blood",
            Volume_mL = bloodUnitVolume_mL,
            Rate_mL_min = bloodRate_mL_min
        }, "1 Erythrozytenkonzentrat");

        public void GiveOxygen() => sim.ApplyIntervention(new InterventionEvent
        {
            Type = InterventionType.Oxygen
        }, "O2 über Maske");
    }
}

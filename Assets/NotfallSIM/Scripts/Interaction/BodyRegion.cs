using System.Linq;
using NotfallSIM.Simulation;
using UnityEngine;

namespace NotfallSIM.Interaction
{
    /// <summary>
    /// Auf ein Körperteil des Patientenmodells legen (braucht einen Collider).
    /// Beim Anklicken werden die klinischen Befunde der aktiven Verletzungen in dieser Region
    /// ausgegeben — Grundstein für den späteren Diagnose-Modus.
    ///
    /// Hinweis: OnMouseDown funktioniert nur mit dem alten Input Manager oder
    /// "Active Input Handling = Both" (Project Settings > Player).
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BodyRegion : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [Tooltip("Muss dem Feld 'region' in injuries.json entsprechen, z. B. 'Becken'.")]
        [SerializeField] string regionName = "Becken";

        void OnMouseDown()
        {
            if (sim.Scenario == null) return;
            double t = sim.Vitals.SimTime_s;

            var findings = sim.Scenario.injuries
                .Where(si => si.onsetTime_s <= t)
                .Select(si => sim.Database.Get(si.injuryId))
                .Where(def => def.region == regionName)
                .SelectMany(def => def.clinicalFindings ?? new string[0])
                .ToList();

            Debug.Log(findings.Count == 0
                ? $"[Untersuchung] {regionName}: unauffällig"
                : $"[Untersuchung] {regionName}: " + string.Join("; ", findings));
            // TODO: Befunde in einem UI-Panel statt im Log anzeigen.
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NotfallSIM.Interaction
{
    /// <summary>
    /// Auf ein Körperteil des Patientenmodells legen (braucht einen Collider).
    /// Ein Klick "untersucht" die Region: Die klinischen Befunde aller bereits aktiven
    /// Verletzungen in dieser Region werden über <see cref="OnExamined"/> gemeldet.
    ///
    /// Klicks kommen über das EventSystem (Kamera braucht einen PhysicsRaycaster) und
    /// funktionieren damit mit dem alten und dem neuen Input System.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BodyRegion : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] SimulationManager sim;
        [Tooltip("Muss dem Feld 'region' in injuries.json entsprechen, z. B. 'Becken'.")]
        [SerializeField] string regionName = "Becken";

        /// <summary>Region und gefundene Befunde (leere Liste = unauffällig).</summary>
        public static event Action<string, IReadOnlyList<string>> OnExamined;

        public string RegionName => regionName;

        public void Configure(SimulationManager simulation, string region)
        {
            sim = simulation;
            regionName = region;
        }

        public void OnPointerClick(PointerEventData eventData) => Examine();

        public void Examine()
        {
            if (sim == null || sim.Scenario == null || sim.Ended) return;
            double t = sim.Vitals.SimTime_s;

            List<string> findings = sim.Scenario.injuries
                .Where(si => si.onsetTime_s <= t)
                .Select(si => sim.Database.Get(si.injuryId))
                .Where(def => def.region == regionName)
                .SelectMany(def => def.clinicalFindings ?? new string[0])
                .ToList();

            sim.Note($"Untersuchung: {regionName}" + (findings.Count == 0 ? " (unauffällig)" : ""));
            OnExamined?.Invoke(regionName, findings);
        }
    }
}

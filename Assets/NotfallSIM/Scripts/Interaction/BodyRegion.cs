using System;
using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Data;
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

        /// <summary>Region, bei deren Untersuchung GCS und Pupillen angezeigt werden.</summary>
        public const string NeuroRegion = "Kopf";

        public void Configure(SimulationManager simulation, string region)
        {
            sim = simulation;
            regionName = region;
        }

        public void OnPointerClick(PointerEventData eventData) => Examine();

        public void Examine()
        {
            if (sim == null || sim.Scenario == null || sim.Engine == null || sim.Database == null || sim.Ended || sim.IsAway) return;   // Engine/Database fehlen nach Neukompilieren im Play-Modus
            double t = sim.Vitals.SimTime_s;

            // Verletzungsbefunde oder Normalbefund (Data/Examination.cs, gleich im Browser-Prototyp).
            var ctx = new ExamContext
            {
                Time_s = t, BodyLossFraction = sim.Vitals.BloodLossFraction,
                Level = sim.Engine.InjuryLevel, Lost_mL = sim.Engine.InjuryBloodLoss_mL, Events = sim.Events
            };
            List<string> findings = Examination.Findings(regionName, sim.Scenario, sim.Database.Get, sim.NormalFindings, ctx);
            var crash = Anatomy.InjuryVisuals.ExtraFindings(regionName).ToList();   // Zeichen aus dem Crash-Simulator
            if (crash.Count > 0)
            {
                if (!Examination.HasInjury(regionName, sim.Scenario, t, sim.Database.Get)) findings.Clear(); // statt Normalbefund
                findings.AddRange(crash);
            }
            bool pathological = Examination.HasInjury(regionName, sim.Scenario, t, sim.Database.Get);

            // Neurologischer Status ist dynamisch und kommt aus der Physiologie, nicht aus der Datenbank.
            if (regionName == NeuroRegion)
            {
                var v = sim.Vitals;
                findings.Add($"Aktuell GCS {v.GCS:0}" + (v.Intubated ? " (sediert/beatmet)" : ""));
                findings.Add(v.Anisocoria ? "Pupillen anisokor: eine Pupille weit und lichtstarr" : "Pupillen isokor, prompt lichtreagibel");
            }

            sim.Note($"Untersuchung: {regionName}" + (pathological ? "" : " (unauffällig)"), "exam:" + regionName);
            OnExamined?.Invoke(regionName, findings);
        }
    }
}

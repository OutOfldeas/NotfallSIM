using NotfallSIM.Data;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Beendet das Szenario, exportiert die Aufzeichnung und zeigt das Debriefing.
    /// Liegt auf einem immer aktiven Objekt; <c>panelRoot</c> ist das ein-/ausblendbare Panel.
    /// </summary>
    public class DebriefPanel : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] SessionRecorder recorder;
        [SerializeField] GameObject panelRoot;
        [SerializeField] TMP_Text text;

        public void Configure(SimulationManager simulation, SessionRecorder rec, GameObject root, TMP_Text target)
        {
            sim = simulation; recorder = rec; panelRoot = root; text = target;
        }

        void Awake() { if (panelRoot) panelRoot.SetActive(false); }

        /// <summary>Für den Button "Szenario beenden".</summary>
        public void EndAndShow()
        {
            if (sim.Ended) { Show(); return; }
            if (sim.Database == null)
            {
                // nach Neukompilieren im Play-Modus sind die Laufzeitdaten weg (Unity: Preferences › General › Script Changes While Playing)
                text.text = "Die Simulation wurde durch Neukompilieren im Play-Modus unterbrochen. Bitte Play beenden und neu starten.";
                Show();
                return;
            }
            sim.EndScenario();
            string dir = recorder.ExportCsv();
            var diagnoses = sim.Scenario == null ? null
                : DiagnosisEvaluation.Evaluate(sim.Scenario.injuries, recorder.Log.Events, sim.Database.Get, sim.ImagingModalities, sim.Differentials);
            text.text = recorder.Log.BuildDebrief(sim.Scenario?.title ?? "Szenario", sim.Scenario?.assessment, diagnoses)
                        + $"\n<size=70%>Verlaufsdaten (CSV): {dir}</size>";
            Show();
        }

        public void Show() { if (panelRoot) panelRoot.SetActive(true); }
        public void Hide() { if (panelRoot) panelRoot.SetActive(false); }
    }
}

using NotfallSIM.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Färbt einen Diagnose-Button je nachdem, ob die Verletzung gerade als Verdacht markiert ist.
    /// Der Klick selbst ruft <see cref="SimulationManager.ToggleSuspected"/> (im SceneBuilder verdrahtet).
    /// </summary>
    public class DiagnosisToggle : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] string injuryId;
        [SerializeField] Image background;
        [SerializeField] Color offColor = new Color(0.25f, 0.27f, 0.30f);
        [SerializeField] Color onColor = new Color(0.62f, 0.45f, 0.12f);

        public void Configure(SimulationManager simulation, string id, Image image, Color off, Color on)
        {
            sim = simulation; injuryId = id; background = image; offColor = off; onColor = on;
        }

        void Update()
        {
            if (sim == null || background == null) return;
            background.color = sim.IsSuspected(injuryId) ? onColor : offColor;
        }
    }
}

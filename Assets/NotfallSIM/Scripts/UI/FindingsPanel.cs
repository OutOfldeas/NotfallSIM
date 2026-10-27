using System.Collections.Generic;
using System.Text;
using NotfallSIM.Interaction;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>Zeigt die Befunde der zuletzt untersuchten Körperregion und Laborergebnisse an.</summary>
    public class FindingsPanel : MonoBehaviour
    {
        [SerializeField] TMP_Text text;
        [SerializeField] SimulationManager sim;

        public void Configure(TMP_Text target, SimulationManager simulation)
        {
            text = target;
            sim = simulation;
        }

        void OnEnable()
        {
            BodyRegion.OnExamined += Show;
            if (sim) sim.OnLogEntry += OnLog;
        }

        void OnDisable()
        {
            BodyRegion.OnExamined -= Show;
            if (sim) sim.OnLogEntry -= OnLog;
        }

        void OnLog(double time, string message, string tag)
        {
            if (text && tag != null && tag.StartsWith("result:"))
                text.text = (tag.StartsWith("result:Imaging") ? "<b>Bildgebung</b>\n" : "<b>Labor</b>\n") + message;
        }

        void Show(string region, IReadOnlyList<string> findings)
        {
            if (!text) return;
            var sb = new StringBuilder();
            sb.AppendLine($"<b>Untersuchung: {region}</b>");
            if (findings.Count == 0)
                sb.AppendLine("Unauffällig.");
            else
                foreach (var f in findings) sb.AppendLine("• " + f);
            text.text = sb.ToString();
        }
    }
}

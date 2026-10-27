using System.Collections.Generic;
using System.Text;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>Protokoll im Bild: die letzten Einträge (Maßnahmen, Befunde, Zustandsänderungen), neueste oben.</summary>
    public class EventLogPanel : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] TMP_Text text;
        [SerializeField] int maxLines = 9;
        readonly LinkedList<string> _lines = new LinkedList<string>();

        public void Configure(SimulationManager simulation, TMP_Text target) { sim = simulation; text = target; }

        void OnEnable() { if (sim) sim.OnLogEntry += OnLog; }
        void OnDisable() { if (sim) sim.OnLogEntry -= OnLog; }

        void OnLog(double time, string message, string tag)
        {
            // Wann eine Verletzung aktiv wird, verrät die Diagnose — nicht anzeigen.
            if (tag != null && tag.StartsWith("injury:")) return;
            string color = tag == null ? "#C8CDD3"
                : tag.StartsWith("state:") || tag == "arrest" || tag.StartsWith("warning:") ? "#FF7A70"
                : tag.StartsWith("result:") ? "#7FD3FF"
                : tag.StartsWith("intervention:") || tag.StartsWith("effect:") ? "#7FE08F"
                : tag.StartsWith("diagnosis") ? "#F2C14E" : "#C8CDD3";
            int t = (int)time;
            string msg = message.Length > 90 ? message.Substring(0, 87) + "…" : message;
            _lines.AddFirst($"<color=#8A9A96>{t / 60:00}:{t % 60:00}</color>  <color={color}>{msg}</color>");
            while (_lines.Count > maxLines) _lines.RemoveLast();
            var sb = new StringBuilder("<b>PROTOKOLL</b>\n");
            foreach (var l in _lines) sb.AppendLine(l);
            if (text) text.text = sb.ToString();
        }
    }
}

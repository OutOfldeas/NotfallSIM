using System;
using System.IO;
using NotfallSIM.Core;
using UnityEngine;

namespace NotfallSIM.Simulation
{
    /// <summary>
    /// Zeichnet Vitalwerte (alle paar Sekunden Simulationszeit) und alle Ereignisse auf.
    /// Export als CSV nach Application.persistentDataPath/Sessions.
    /// </summary>
    public class SessionRecorder : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [Tooltip("Abtastintervall in Sekunden Simulationszeit.")]
        [SerializeField] float sampleInterval_s = 5f;

        public SessionLog Log { get; } = new SessionLog();
        double _nextSample;

        public void Configure(SimulationManager simulation) => sim = simulation;

        // Awake läuft vor allen Start()-Aufrufen, so geht kein Ereignis verloren.
        void Awake()
        {
            sim.OnLogEntry += Log.AddEvent;
            Log.Mode = sim.Mode;
        }
        void OnDestroy() { if (sim) sim.OnLogEntry -= Log.AddEvent; }

        void Update()
        {
            if (sim.Engine == null || sim.Ended) return;
            var v = sim.Vitals;
            if (v.SimTime_s < _nextSample) return;
            Log.AddSample(v);
            _nextSample = v.SimTime_s + sampleInterval_s;
        }

        /// <summary>Schreibt zwei CSV-Dateien und gibt den Ordner zurück.</summary>
        public string ExportCsv()
        {
            Log.AddSample(sim.Vitals); // letzter Stand
            string dir = Path.Combine(Application.persistentDataPath, "Sessions");
            Directory.CreateDirectory(dir);
            string stem = $"{sim.Scenario?.id ?? "session"}_{DateTime.Now:yyyyMMdd_HHmmss}";
            // UTF-8 mit BOM, damit Excel Umlaute korrekt erkennt.
            var utf8Bom = new System.Text.UTF8Encoding(true);
            File.WriteAllText(Path.Combine(dir, stem + "_vitals.csv"), Log.ToVitalsCsv(), utf8Bom);
            File.WriteAllText(Path.Combine(dir, stem + "_events.csv"), Log.ToEventsCsv(), utf8Bom);
            Debug.Log($"[Recorder] CSV gespeichert: {dir}");
            return dir;
        }
    }
}

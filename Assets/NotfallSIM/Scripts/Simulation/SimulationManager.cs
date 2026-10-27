using System;
using System.Collections.Generic;
using NotfallSIM.Core;
using NotfallSIM.Data;
using NotfallSIM.Engines;
using UnityEngine;

namespace NotfallSIM.Simulation
{
    public enum EngineChoice { Mock, Pulse }

    /// <summary>
    /// Herzstück der Szene: lädt Szenario + Datenbank, wählt die Engine, treibt die Zeit voran
    /// und löst Verletzungen zum vorgesehenen Zeitpunkt aus. Eine Instanz pro Szene.
    /// </summary>
    public class SimulationManager : MonoBehaviour
    {
        [Header("Szenario")]
        [SerializeField] string scenarioId = "motorrad_beckenfraktur";
        [SerializeField] EngineChoice engine = EngineChoice.Mock;

        [Header("Zeit")]
        [Tooltip("1 = Echtzeit. Höher = schneller (zum Testen von Verläufen).")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Physiologischer Zeitschritt in Sekunden (Pulse arbeitet intern mit 0,02 s).")]
        [SerializeField] float stepSize_s = 0.1f;
        [SerializeField] bool paused;

        public IPhysiologyEngine Engine { get; private set; }
        public ScenarioDefinition Scenario { get; private set; }
        public InjuryDatabase Database { get; private set; }
        public VitalSigns Vitals => Engine?.Current ?? default(VitalSigns);

        /// <summary>Für Logging/Debriefing: jede Verletzung und Intervention mit Zeitstempel.</summary>
        public event Action<double, string> OnLogEntry;
        public event Action OnScenarioEnded;
        public bool Ended { get; private set; }

        readonly List<ScenarioInjury> _pending = new List<ScenarioInjury>();
        double _accumulator;

        void Start()
        {
            Database = InjuryDatabase.Load();
            Scenario = InjuryDatabase.LoadScenario(scenarioId);
            if (Scenario.defaultTimeScale > 0) timeScale = (float)Scenario.defaultTimeScale;

            Engine = CreateEngine();
            if (!Engine.Initialize(Scenario.patient))
            {
                Debug.LogError($"[Sim] {Engine.Name} konnte nicht initialisiert werden — falle auf Mock zurück.");
                Engine = new MockPhysiologyEngine();
                Engine.Initialize(Scenario.patient);
            }
            _pending.AddRange(Scenario.injuries);
            Log($"Szenario '{Scenario.title}' gestartet mit {Engine.Name} (DB v{Database.Version})");
        }

        IPhysiologyEngine CreateEngine()
        {
#if NOTFALLSIM_PULSE
            if (engine == EngineChoice.Pulse) return new PulsePhysiologyAdapter();
#else
            if (engine == EngineChoice.Pulse)
                Debug.LogWarning("[Sim] Pulse gewählt, aber NOTFALLSIM_PULSE nicht gesetzt — nutze Mock.");
#endif
            return new MockPhysiologyEngine();
        }

        void Update()
        {
            if (paused || Ended || Engine == null || Engine.Current.CardiacArrest) return;

            _accumulator += Time.deltaTime * timeScale;
            int guard = 0;
            while (_accumulator >= stepSize_s && guard++ < 500) // Schutz bei hohem timeScale
            {
                TriggerDueInjuries();
                Engine.Advance(stepSize_s);
                _accumulator -= stepSize_s;
            }
            if (Engine.Current.CardiacArrest) Log("Kreislaufstillstand");
        }

        void TriggerDueInjuries()
        {
            double t = Engine.Current.SimTime_s;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].onsetTime_s > t) continue;
                foreach (var ev in Database.ToEvents(_pending[i])) Engine.ApplyInjury(ev);
                Log($"Verletzung aktiv: {_pending[i].injuryId} (Schweregrad {_pending[i].severity:F1})");
                _pending.RemoveAt(i);
            }
        }

        public void ApplyIntervention(InterventionEvent iv, string label)
        {
            if (Engine == null || Ended || Engine.Current.CardiacArrest) return;
            Engine.ApplyIntervention(iv);
            Log($"Maßnahme: {label}");
        }

        /// <summary>Freier Protokolleintrag, z. B. für Untersuchungen.</summary>
        public void Note(string message) => Log(message);

        /// <summary>Stoppt die Simulation endgültig und löst das Debriefing aus.</summary>
        public void EndScenario()
        {
            if (Ended) return;
            Ended = true;
            Log("Szenario beendet");
            OnScenarioEnded?.Invoke();
        }

        public void SetPaused(bool value) => paused = value;
        public void SetTimeScale(float value)
        {
            timeScale = Mathf.Max(0, value);
            Log($"Zeitraffer {timeScale:0.#}x");
        }

        void Log(string msg)
        {
            double t = Engine?.Current.SimTime_s ?? 0;
            Debug.Log($"[Sim {t:F0}s] {msg}");
            OnLogEntry?.Invoke(t, msg);
        }
    }
}

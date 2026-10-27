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
        [Tooltip("Lehrbuch: ATLS-Stufen. Realität: an Registerdaten angelehnt, mit individueller Streuung (nur Mock).")]
        [SerializeField] PhysiologyMode mode = PhysiologyMode.Textbook;

        public PhysiologyMode Mode => mode;

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

        /// <summary>
        /// Für Logging/Debriefing: Zeit, lesbarer Text und maschinenlesbarer Tag
        /// (z. B. "intervention:HemorrhageControl:Pelvis", "exam:Becken"), auf den sich Bewertungskriterien beziehen.
        /// </summary>
        public event Action<double, string, string> OnLogEntry;
        public event Action OnScenarioEnded;
        public bool Ended { get; private set; }

        readonly List<ScenarioInjury> _pending = new List<ScenarioInjury>();
        // result != null: reines Ergebnis (z. B. BGA), das zum Fälligkeitszeitpunkt nur protokolliert wird.
        readonly List<(double due, InterventionEvent iv, string label, string result)> _delayed =
            new List<(double, InterventionEvent, string, string)>();
        readonly StateWatcher _watcher = new StateWatcher();
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
                Engine = new MockPhysiologyEngine(null, mode);
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
            return new MockPhysiologyEngine(null, mode);
        }

        void Update()
        {
            if (paused || Ended || Engine == null || Engine.Current.CardiacArrest) return;

            _accumulator += Time.deltaTime * timeScale;
            int guard = 0;
            while (_accumulator >= stepSize_s && guard++ < 500) // Schutz bei hohem timeScale
            {
                TriggerDueInjuries();
                TriggerDelayedInterventions();
                Engine.Advance(stepSize_s);
                foreach (var st in _watcher.Check(Engine.Current)) Log(st.message, st.tag);
                _accumulator -= stepSize_s;
            }
            if (Engine.Current.CardiacArrest) Log("Kreislaufstillstand", "arrest");
        }

        void TriggerDueInjuries()
        {
            double t = Engine.Current.SimTime_s;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].onsetTime_s > t) continue;
                foreach (var ev in Database.ToEvents(_pending[i])) Engine.ApplyInjury(ev);
                Log($"Verletzung aktiv: {_pending[i].injuryId} (Schweregrad {_pending[i].severity:F1})", "injury:" + _pending[i].injuryId);
                _pending.RemoveAt(i);
            }
        }

        /// <param name="delay_s">Verzögerung bis zur Wirkung (z. B. Kraniotomie: CT, Transport, OP-Beginn).
        /// Bewertet wird der Zeitpunkt der Anordnung.</param>
        public void ApplyIntervention(InterventionEvent iv, string label, double delay_s = 0)
        {
            if (Engine == null || Ended || Engine.Current.CardiacArrest) return;
            string tag = $"intervention:{iv.Type}:{iv.TargetCompartment ?? iv.Compound ?? ""}";
            if (iv.Type == InterventionType.BloodGas)
            {
                // Werte zum Abnahmezeitpunkt, Ergebnis nach der Laufzeit des Analysegeräts.
                var v = Engine.Current;
                string result = $"BGA-Ergebnis (abgenommen {Clock(v.SimTime_s)}): BE {v.BaseExcess:+0.0;-0.0} mmol/L, Laktat {v.Lactate:0.0} mmol/L";
                _delayed.Add((v.SimTime_s + delay_s, iv, label, result.Replace('.', ',')));
                Log($"Maßnahme: {label}", tag);
                return;
            }
            if (delay_s > 0)
            {
                _delayed.Add((Engine.Current.SimTime_s + delay_s, iv, label, null));
                Log($"Angeordnet: {label} (wirksam in ca. {delay_s / 60:0} min)", tag);
                return;
            }
            Engine.ApplyIntervention(iv);
            Log($"Maßnahme: {label}", tag);
        }

        void TriggerDelayedInterventions()
        {
            double t = Engine.Current.SimTime_s;
            for (int i = _delayed.Count - 1; i >= 0; i--)
            {
                if (_delayed[i].due > t) continue;
                if (_delayed[i].result != null)
                    Log(_delayed[i].result, "result:" + _delayed[i].iv.Type);
                else
                {
                    Engine.ApplyIntervention(_delayed[i].iv);
                    Log($"Wirksam: {_delayed[i].label}", "effect:" + _delayed[i].iv.Type);
                }
                _delayed.RemoveAt(i);
            }
        }

        /// <summary>Freier Protokolleintrag, z. B. für Untersuchungen (Tag "exam:&lt;Region&gt;").</summary>
        public void Note(string message, string tag = null) => Log(message, tag);

        /// <summary>Stoppt die Simulation endgültig und löst das Debriefing aus.</summary>
        public void EndScenario()
        {
            if (Ended) return;
            Ended = true;
            Log("Szenario beendet", "end");
            OnScenarioEnded?.Invoke();
        }

        public void SetPaused(bool value) => paused = value;
        public void SetTimeScale(float value)
        {
            timeScale = Mathf.Max(0, value);
            Log($"Zeitraffer {timeScale:0.#}x");
        }

        static string Clock(double s) { int t = (int)s; return $"{t / 60:00}:{t % 60:00}"; }

        void Log(string msg, string tag = null)
        {
            double t = Engine?.Current.SimTime_s ?? 0;
            Debug.Log($"[Sim {t:F0}s] {msg}");
            OnLogEntry?.Invoke(t, msg, tag);
        }
    }
}

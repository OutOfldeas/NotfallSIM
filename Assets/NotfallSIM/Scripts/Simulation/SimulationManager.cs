using System;
using System.Collections.Generic;
using System.Linq;
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
        [Tooltip("Statt eines festen Szenarios ein Zufallsszenario aus diesem Unfallmechanismus erzeugen (mechanisms.json), z. B. pkw_frontal. Leer = festes Szenario.")]
        [SerializeField] string randomMechanism = "";
        [Tooltip("Seed für das Zufallsszenario. 0 = jedes Mal neu. Gleicher Seed = gleiches Szenario wie im Browser-Prototyp.")]
        [SerializeField] int randomSeed = 0;
        [SerializeField] EngineChoice engine = EngineChoice.Mock;
        [Tooltip("Lehrbuch: ATLS-Stufen. Realität: an Registerdaten angelehnt, mit individueller Streuung (nur Mock).")]
        [SerializeField] PhysiologyMode mode = PhysiologyMode.Textbook;

        public PhysiologyMode Mode => mode;

        [Header("Bildgebung")]
        [Tooltip("CT direkt im Schockraum (z. B. Sliding Gantry): kein Transport, der Patient bleibt zugänglich.")]
        [SerializeField] bool ctInTraumaRoom = false;

        /// <summary>True, solange der Patient zur Bildgebung außerhalb des Schockraums ist.</summary>
        public bool IsAway => Engine != null && Engine.Current.SimTime_s < _awayUntil;
        public bool CtInTraumaRoom => ctInTraumaRoom;
        double _awayUntil = -1;
        readonly Dictionary<string, ImagingModality> _imaging = new Dictionary<string, ImagingModality>();

        [Header("Zeit")]
        [Tooltip("1 = Echtzeit. Höher = schneller (zum Testen von Verläufen).")]
        [SerializeField] float timeScale = 1f;
        [Tooltip("Physiologischer Zeitschritt in Sekunden (Pulse arbeitet intern mit 0,02 s).")]
        [SerializeField] float stepSize_s = 0.1f;
        [SerializeField] bool paused;

        public IPhysiologyEngine Engine { get; private set; }
        public ScenarioDefinition Scenario { get; private set; }
        public InjuryDatabase Database { get; private set; }
        public FindingsDatabaseFile NormalFindings { get; private set; }
        public DifferentialDatabaseFile Differentials { get; private set; }
        /// <summary>Bisheriges Protokoll (z. B. für Befunde, die von Maßnahmen abhängen).</summary>
        public IReadOnlyList<LogEntry> Events => _log;
        public ImagingModality[] ImagingModalities { get; private set; }

        // Verdachtsdiagnosen (Diagnose-Modus): Injury-IDs, die der Spieler aktuell vermutet.
        readonly HashSet<string> _suspected = new HashSet<string>();
        public bool IsSuspected(string injuryId) => _suspected.Contains(injuryId);
        /// <summary>Maßnahme oder Bildgebung angeordnet, aber noch nicht wirksam bzw. noch kein Ergebnis.</summary>
        public bool IsPending(string id) => _delayed.Any(d => d.iv.Id == id);
        public IEnumerable<string> Suspected => _suspected;
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
            // Auswahl aus dem Hauptmenü überschreibt die Inspector-Felder
            if (SessionSetup.Active)
            {
                scenarioId = SessionSetup.ScenarioId ?? scenarioId;
                randomMechanism = SessionSetup.RandomMechanism ?? "";
                randomSeed = 0;
                mode = SessionSetup.Mode;
                ctInTraumaRoom = SessionSetup.CtInTraumaRoom;
            }
            if (Crash.CrashHandoff.Pending != null)
            {
                // Fall aus der Unfall-Sandbox
                Scenario = Crash.CrashHandoff.Pending;
                Crash.CrashHandoff.Pending = null;
                Debug.Log($"[Sim] Crash-Szenario {Scenario.id}: {Scenario.expectedCourse}");
            }
            else if (string.IsNullOrEmpty(randomMechanism))
                Scenario = InjuryDatabase.LoadScenario(scenarioId);
            else
            {
                uint seed = randomSeed != 0 ? (uint)randomSeed : (uint)Environment.TickCount;
                Scenario = ScenarioGenerator.Generate(InjuryDatabase.LoadMechanisms(), randomMechanism, Database.All, seed);
                Debug.Log($"[Sim] Zufallsszenario {Scenario.id}: {Scenario.expectedCourse}");
            }
            if (Scenario.defaultTimeScale > 0) timeScale = (float)Scenario.defaultTimeScale;

            Engine = CreateEngine();
            if (!Engine.Initialize(Scenario.patient))
            {
                Debug.LogError($"[Sim] {Engine.Name} konnte nicht initialisiert werden — falle auf Mock zurück.");
                Engine = new MockPhysiologyEngine(null, mode);
                Engine.Initialize(Scenario.patient);
            }
            _pending.AddRange(Scenario.injuries);
            ImagingModalities = InjuryDatabase.LoadImaging().modalities;
            foreach (var m in ImagingModalities) _imaging[m.id] = m;
            NormalFindings = InjuryDatabase.LoadFindings();
            Differentials = InjuryDatabase.LoadDifferentials();
            // Verletzungen ab Sekunde 0 sofort anwenden, damit der Monitor schon beim ersten Bild den Ankunftszustand zeigt
            TriggerDueInjuries();
            Engine.Advance(stepSize_s);
            Log($"Szenario '{Scenario.title}' gestartet mit {Engine.Name} (DB v{Database.Version})");
            NotfallSIM.Rooms.TraumaRoom.Ensure(this); // Raum, Kamera, anatomischer Patient — auch in alten Szenendateien
            UI.PauseMenu.Ensure(this);
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
            if (IsAway) { Log($"Nicht möglich: Patient ist zur Bildgebung außerhalb des Schockraums ({label})", null); return; }
            string tag = $"intervention:{iv.Type}:{iv.TargetCompartment ?? iv.Compound ?? ""}:{iv.Id}";
            if (iv.Type == InterventionType.BloodGas)
            {
                // Werte zum Abnahmezeitpunkt, Ergebnis nach der Laufzeit des Analysegeräts.
                var v = Engine.Current;
                string result = iv.Compound == "Coag"
                    ? $"Gerinnung (abgenommen {Clock(v.SimTime_s)}): " + BloodGas.CoagReport(v)
                    : $"BGA-Ergebnis (abgenommen {Clock(v.SimTime_s)}): " + BloodGas.Report(v);
                _delayed.Add((v.SimTime_s + delay_s, iv, label, result));
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
                {
                    bool imaging = _delayed[i].result.StartsWith("Befund ");
                    Log(_delayed[i].result, imaging ? "result:Imaging:" + _delayed[i].iv.Id : "result:" + _delayed[i].iv.Type);
                    if (imaging && _awayUntil > 0 && t >= _awayUntil - 1e-6) { _awayUntil = -1; Log("Patient zurück im Schockraum", "transport:back"); }
                }
                else
                {
                    Engine.ApplyIntervention(_delayed[i].iv);
                    Log($"Wirksam: {_delayed[i].label}", "effect:" + _delayed[i].iv.Type);
                }
                _delayed.RemoveAt(i);
            }
        }

        /// <summary>
        /// Fordert ein Bildgebungsverfahren an (für Buttons: OnClick mit String-Parameter, z. B. "ct_ganzkoerper").
        /// Der Befund entsteht aus dem Zustand bei Anforderung und erscheint nach Untersuchungs- und Transportzeit.
        /// </summary>
        public void RequestImaging(string modalityId)
        {
            if (Engine == null || Ended || Engine.Current.CardiacArrest || IsAway) return;
            if (!_imaging.TryGetValue(modalityId, out var m)) { Debug.LogWarning($"[Bildgebung] Unbekannt: {modalityId}"); return; }

            var v = Engine.Current;
            bool transport = m.transport_s > 0 && !(m.isCT && ctInTraumaRoom);
            double total = m.duration_s + (transport ? m.transport_s : 0);
            var active = Scenario.injuries.Where(si => si.onsetTime_s <= v.SimTime_s).Select(si => Database.Get(si.injuryId));
            var lines = ImagingReport.Build(m, active, _log.Select(e => e.Tag));

            Log($"Bildgebung: {m.logLabel}" + (transport ? " (Transport in die Radiologie)" : m.isCT ? " (CT im Schockraum)" : ""), "imaging:" + m.id);
            if (transport)
            {
                _awayUntil = v.SimTime_s + total;
                if (v.SystolicBP_mmHg < 90)
                    Log("Warnung: hämodynamisch instabiler Patient verlässt den Schockraum", "warning:unstable-transport");
            }
            string result = $"Befund {m.label}: " + string.Join(" ", lines);
            _delayed.Add((v.SimTime_s + total, new InterventionEvent { Type = InterventionType.Supportive, Id = m.id }, m.label, result));
        }

        /// <summary>Freier Protokolleintrag, z. B. für Untersuchungen (Tag "exam:&lt;Region&gt;").</summary>
        public void Note(string message, string tag = null)
        {
            if (IsAway) return; // Patient ist nicht im Schockraum
            Log(message, tag);
        }

        /// <summary>
        /// Verdachtsdiagnose setzen oder zurücknehmen (für die Diagnose-Buttons). Geht auch, während der
        /// Patient in der Bildgebung ist — Nachdenken braucht keinen Patientenkontakt.
        /// </summary>
        public void ToggleSuspected(string injuryId)
        {
            if (Ended || Database == null) return;
            var diff = Differentials?.differentials?.FirstOrDefault(d => d.id == injuryId);
            string name = diff != null ? diff.name : Database.Get(injuryId).name;
            if (_suspected.Remove(injuryId))
                Log($"Verdacht verworfen: {name}", DiagnosisEvaluation.RetractPrefix + injuryId);
            else
            {
                _suspected.Add(injuryId);
                Log($"Verdachtsdiagnose: {name}", DiagnosisEvaluation.SuspectPrefix + injuryId);
            }
        }

        /// <summary>Stoppt die Simulation endgültig und löst das Debriefing aus.</summary>
        public void EndScenario()
        {
            if (Ended) return;
            Ended = true;
            Log("Szenario beendet", "end");
            OnScenarioEnded?.Invoke();
        }

        public void SetPaused(bool value) => paused = value;
        public bool Paused => paused;

        /// <summary>Für Buttons (z. B. „Hauptmenü“). Setzt die Zeit zurück, falls Zeitlupe/Zeitraffer aktiv war.</summary>
        public void LoadScene(string sceneName)
        {
            Time.timeScale = 1;
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }
        public void SetTimeScale(float value)
        {
            timeScale = Mathf.Max(0, value);
            Log($"Zeitraffer {timeScale:0.#}x");
        }

        static string Clock(double s) { int t = (int)s; return $"{t / 60:00}:{t % 60:00}"; }

        readonly List<LogEntry> _log = new List<LogEntry>();

        void Log(string msg, string tag = null)
        {
            double t = Engine?.Current.SimTime_s ?? 0;
            _log.Add(new LogEntry { Time_s = t, Message = msg, Tag = tag });
            Debug.Log($"[Sim {t:F0}s] {msg}");
            OnLogEntry?.Invoke(t, msg, tag);
        }
    }
}

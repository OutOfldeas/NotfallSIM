using System.Collections.Generic;
using System.IO;
using System.Linq;
using NotfallSIM.Core;
using NotfallSIM.Data;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Hauptmenü: festes Szenario oder Zufallsfall wählen, Physiologie-Modus und CT-Standort einstellen,
    /// Schockraum starten oder zur Unfall-Sandbox wechseln. Die Auswahl entsteht zur Laufzeit aus den Datendateien.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] RectTransform root;

        static readonly Color Bg = new Color(0.05f, 0.07f, 0.09f, 0.94f);
        static readonly Color Option = new Color(0.20f, 0.23f, 0.27f);
        static readonly Color Selected = new Color(0.12f, 0.45f, 0.38f);
        static readonly Color Primary = new Color(0.16f, 0.32f, 0.55f);
        static readonly Color Dim = new Color(0.75f, 0.78f, 0.82f);

        readonly List<(Button button, string key)> _choices = new List<(Button, string)>();
        List<ScenarioDefinition> _scenarios;
        MechanismDatabaseFile _mechanisms;
        string _choice;
        TextMeshProUGUI _brief, _warning;
        Button _modeTextbook, _modeRegistry, _ctRadiology, _ctRoom;

        public void Configure(RectTransform uiRoot) => root = uiRoot;

        void Start()
        {
            Time.timeScale = 1;
            _scenarios = Directory.GetFiles(Path.Combine(InjuryDatabase.Root, "scenarios"), "*.json")
                .Select(f => JsonUtility.FromJson<ScenarioDefinition>(File.ReadAllText(f))).OrderBy(s => s.title).ToList();
            _mechanisms = InjuryDatabase.LoadMechanisms();

            var left = RuntimeUi.Panel(root, "Auswahl", Bg);
            RuntimeUi.Place(left, new Vector2(0, 0), new Vector2(0.45f, 1), new Vector2(40, 40), new Vector2(-15, -40));
            RuntimeUi.Column(left, 8, 24);
            RuntimeUi.Label(left, "<b>NotfallSIM</b>", 40, Color.white, 54);
            RuntimeUi.Label(left, "SZENARIO", 16, Dim, 24);
            foreach (var s in _scenarios) AddChoice(left, s.title, "scenario:" + s.id);
            RuntimeUi.Label(left, "ZUFALLSFALL AUS UNFALLMECHANISMUS", 16, Dim, 30);
            foreach (var m in _mechanisms.mechanisms) AddChoice(left, m.label, "random:" + m.id);

            var right = RuntimeUi.Panel(root, "Start", Bg);
            RuntimeUi.Place(right, new Vector2(0.45f, 0), new Vector2(1, 1), new Vector2(15, 40), new Vector2(-40, -40));
            RuntimeUi.Column(right, 10, 24);
            _brief = RuntimeUi.Label(right, "", 22, Color.white, 300);
            RuntimeUi.Label(right, "PHYSIOLOGIE", 16, Dim, 24);
            _modeTextbook = RuntimeUi.Button(right, "Lehrbuch (ATLS-Schockklassen)", Option, () => { SessionSetup.Mode = PhysiologyMode.Textbook; Refresh(); });
            _modeRegistry = RuntimeUi.Button(right, "Realität (Registerdaten, individuelle Streuung)", Option, () => { SessionSetup.Mode = PhysiologyMode.Registry; Refresh(); });
            RuntimeUi.Label(right, "CT-STANDORT", 16, Dim, 24);
            _ctRadiology = RuntimeUi.Button(right, "Radiologie (Transport nötig)", Option, () => { SessionSetup.CtInTraumaRoom = false; Refresh(); });
            _ctRoom = RuntimeUi.Button(right, "Im Schockraum", Option, () => { SessionSetup.CtInTraumaRoom = true; Refresh(); });
            RuntimeUi.Label(right, "", 10, Dim, 12);
            RuntimeUi.Button(right, "Schockraum starten", Selected, StartTraumaRoom, 60, 26);
            RuntimeUi.Button(right, "Unfall-Sandbox (Ragdoll-Crash)", Primary, () => Load(SessionSetup.CrashScene), 56, 22);
            _warning = RuntimeUi.Label(right, "", 18, new Color(1f, 0.48f, 0.44f), 50);
            RuntimeUi.Label(right, "<size=80%>Experimentelles Lernwerkzeug, kein Medizinprodukt. Werte aus einem vereinfachten, literaturgestützten Modell im Entwurfsstatus.</size>", 16, Dim, 60);

            _choice = _choices.Count > 0 ? _choices[0].key : null;
            Refresh();
        }

        void AddChoice(Transform parent, string label, string key)
        {
            var b = RuntimeUi.Button(parent, label, Option, () => { _choice = key; Refresh(); }, 44, 19);
            _choices.Add((b, key));
        }

        void Refresh()
        {
            foreach (var c in _choices) c.button.GetComponent<Image>().color = c.key == _choice ? Selected : Option;
            _modeTextbook.GetComponent<Image>().color = SessionSetup.Mode == PhysiologyMode.Textbook ? Selected : Option;
            _modeRegistry.GetComponent<Image>().color = SessionSetup.Mode == PhysiologyMode.Registry ? Selected : Option;
            _ctRadiology.GetComponent<Image>().color = !SessionSetup.CtInTraumaRoom ? Selected : Option;
            _ctRoom.GetComponent<Image>().color = SessionSetup.CtInTraumaRoom ? Selected : Option;

            if (_choice == null) { _brief.text = ""; return; }
            if (_choice.StartsWith("scenario:"))
            {
                var s = _scenarios.First(x => "scenario:" + x.id == _choice);
                _brief.text = $"<b>{s.title}</b>\n{s.description}\n\n<color=#BFC6CE>{s.patient.age} J., {(s.patient.sex == "female" ? "weiblich" : "männlich")}, {s.patient.weight_kg:0} kg" +
                              (s.patient.betaBlocker ? " · Betablocker" : "") + "</color>\n\n<size=85%><b>Lernziele</b>\n• " + string.Join("\n• ", s.learningGoals) + "</size>";
            }
            else
            {
                var m = _mechanisms.mechanisms.First(x => "random:" + x.id == _choice);
                _brief.text = $"<b>Zufallsfall: {m.label}</b>\nPatient, Verletzungsmuster und präklinischer Blutverlust werden bei jedem Start neu gewürfelt. " +
                              "Welche Verletzungen vorliegen, zeigt erst das Debriefing — leite sie aus Mechanismus, Untersuchung und Bildgebung ab.";
            }
        }

        void StartTraumaRoom()
        {
            if (_choice == null) return;
            SessionSetup.Active = true;
            SessionSetup.ScenarioId = _choice.StartsWith("scenario:") ? _choice.Substring(9) : null;
            SessionSetup.RandomMechanism = _choice.StartsWith("random:") ? _choice.Substring(7) : null;
            Load(SessionSetup.TraumaRoomScene);
        }

        void Load(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                _warning.text = $"Szene „{scene}“ fehlt in der Build-Liste. Im Editor: NotfallSIM › Alle Szenen erstellen " +
                                "oder NotfallSIM › Szenen in Build-Liste eintragen.";
                return;
            }
            SceneManager.LoadScene(scene);
        }
    }
}

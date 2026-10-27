using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Rückmeldung an einem Maßnahmen- oder Bildgebungs-Button: kurzes Aufleuchten beim Klick,
    /// „läuft …“ solange angeordnet, grün „erledigt“ bei einmaligen Maßnahmen, grau wenn gerade nicht möglich.
    /// </summary>
    public class ActionButton : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] InterventionController controller; // leer = Bildgebung
        [SerializeField] string id;
        [SerializeField] string label;
        [SerializeField] Color normal = new Color(0.16f, 0.32f, 0.55f);

        static readonly Color Done = new Color(0.18f, 0.48f, 0.28f);
        static readonly Color Pending = new Color(0.62f, 0.45f, 0.12f);
        static readonly Color Blocked = new Color(0.22f, 0.23f, 0.25f);

        Image _bg;
        TMP_Text _text;
        Button _button;
        float _flash;

        public void Configure(SimulationManager simulation, InterventionController ivc, string actionId, string buttonLabel, Color baseColor)
        {
            sim = simulation; controller = ivc; id = actionId; label = buttonLabel; normal = baseColor;
        }

        void Awake()
        {
            _bg = GetComponent<Image>();
            _text = GetComponentInChildren<TMP_Text>();
            _button = GetComponent<Button>();
            if (_button) _button.onClick.AddListener(() => _flash = 0.25f);
        }

        void Update()
        {
            if (sim == null || _bg == null) return;
            bool pending = sim.IsPending(id);
            var def = controller ? controller.Get(id) : null;
            bool done = def != null && def.once && controller.IsUsed(id) && !pending;
            bool blocked = sim.Ended || sim.IsAway || sim.Vitals.CardiacArrest || done || (controller == null && pending);
            string state = pending ? (controller == null ? "läuft …" : "angeordnet …") : done ? "erledigt" : "";

            Color c = pending ? Pending : done ? Done : blocked ? Blocked : normal;
            if (_flash > 0) { _flash -= Time.unscaledDeltaTime; c = Color.Lerp(c, Color.white, Mathf.Clamp01(_flash / 0.25f) * 0.6f); }
            _bg.color = c;
            if (_button) _button.interactable = !blocked || pending;
            if (_text) _text.text = state.Length > 0 ? $"{label}\n<size=70%>{state}</size>" : label;
        }
    }
}

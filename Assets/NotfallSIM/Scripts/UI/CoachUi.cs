using System.Linq;
using System.Text;
using NotfallSIM.Data;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Knöpfe „Tipp (T)“ und „Strategie (G)“ unter „Ansicht &amp; Ton“. Tipp: nächster sinnvoller Schritt nach xABCDE
    /// (ohne Spoiler; der passende Maßnahmen-Knopf leuchtet auf). Strategie: optimales Vorgehen für diesen Fall mit
    /// Zeitzielen und Stand (zeigt die Lösung). Beides wird im Protokoll vermerkt (Tag "help:tip" bzw. "help:strategy").
    /// </summary>
    public class CoachUi : MonoBehaviour
    {
        SimulationManager _sim;
        InterventionController _ivc;
        GameObject _overlay;
        RectTransform _box;
        TMP_Text _title, _text;
        ScrollRect _scroll;
        bool _showingStrategy;
        float _refresh;
        InterventionDefinition[] _interventions;

        public static void Ensure(SimulationManager sim)
        {
            if (Object.FindAnyObjectByType<CoachUi>() != null) return;
            var go = new GameObject("Tipps und Strategie", typeof(RectTransform));
            var c = go.AddComponent<CoachUi>();
            c._sim = sim;
            c._ivc = sim.GetComponent<InterventionController>();
        }

        void Start()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 85;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)transform;

            Place(RuntimeUi.Button(root, "Tipp (T)", new Color(0.55f, 0.42f, 0.1f, 0.95f), ShowTip, 40, 16), new Vector2(24, -116), new Vector2(72, 40));
            Place(RuntimeUi.Button(root, "Strategie (G)", new Color(0.3f, 0.25f, 0.5f, 0.95f), ShowStrategy, 40, 14), new Vector2(102, -116), new Vector2(72, 40));

            _overlay = RuntimeUi.Panel(root, "Tipp", new Color(0.06f, 0.08f, 0.1f, 0.97f)).gameObject;
            _box = (RectTransform)_overlay.transform;
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 0.5f);
            _box.anchoredPosition = new Vector2(0, 40);
            _title = RuntimeUi.Label(_box, "", 22, Color.white);
            var tr = _title.rectTransform;
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
            tr.offsetMin = new Vector2(20, -46); tr.offsetMax = new Vector2(-60, -12);
            var close = RuntimeUi.Button(_box, "×", new Color(0.25f, 0.27f, 0.3f), Close, 30, 20);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1, 1);
            cr.anchoredPosition = new Vector2(-10, -10); cr.sizeDelta = new Vector2(32, 32);
            close.GetComponent<LayoutElement>().ignoreLayout = true;

            var view = new GameObject("Liste", typeof(RectTransform));
            view.transform.SetParent(_box, false);
            var vr = (RectTransform)view.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(20, 16); vr.offsetMax = new Vector2(-20, -52);
            view.AddComponent<RectMask2D>();
            view.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            _text = RuntimeUi.Label(view.transform, "", 17, Color.white);
            _text.alignment = TextAlignmentOptions.TopLeft;
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = view.AddComponent<ScrollRect>();
            _scroll.content = rt; _scroll.viewport = vr;
            _scroll.horizontal = false; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 30;
            _overlay.SetActive(false);
        }

        static void Place(Button b, Vector2 pos, Vector2 size)
        {
            var r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = pos; r.sizeDelta = size;
        }

        void Update()
        {
            bool t, g;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            t = kb.tKey.wasPressedThisFrame; g = kb.gKey.wasPressedThisFrame;
#else
            t = Input.GetKeyDown(KeyCode.T); g = Input.GetKeyDown(KeyCode.G);
#endif
            if (t) { if (_overlay.activeSelf && !_showingStrategy) Close(); else ShowTip(); }
            if (g) { if (_overlay.activeSelf && _showingStrategy) Close(); else ShowStrategy(); }
            // Strategie live aktualisieren (Häkchen, Zeitziele)
            if (_overlay.activeSelf && _showingStrategy && (_refresh -= Time.unscaledDeltaTime) <= 0) { _refresh = 1; RenderStrategy(false); }
        }

        CoachContext Context()
        {
            var db = _sim.Database;
            return new CoachContext
            {
                Scenario = _sim.Scenario,
                Injury = id => db.All.FirstOrDefault(d => d.id == id),
                Interventions = _interventions ?? (_interventions = InjuryDatabase.LoadInterventions().interventions),
                Imaging = _sim.ImagingModalities,
                Events = _sim.Events,
                Vitals = _sim.Vitals,
            };
        }

        void ShowTip()
        {
            if (_sim == null || _sim.Engine == null) return;
            var c = Context();
            var tip = Coach.Tip(c);
            _showingStrategy = false;
            _box.sizeDelta = new Vector2(640, 250);
            _title.text = $"<color=#F2C14E>Tipp</color>  <size=75%>{Coach.PhaseNames[tip.Phase]}</size>";
            var sb = new StringBuilder();
            sb.AppendLine($"<b><size=120%>{tip.Action}</size></b>");
            if (tip.Deadline_s.HasValue) sb.AppendLine($"<color=#8A9A96>{Coach.Status(tip, c.Vitals.SimTime_s)}</color>");
            sb.AppendLine();
            sb.AppendLine(tip.Why);
            foreach (var caution in Coach.Cautions(c)) sb.AppendLine($"\n<color=#FF8A5C>{caution}</color>");
            _text.text = sb.ToString();
            _overlay.SetActive(true);
            _scroll.verticalNormalizedPosition = 1;
            Highlight(tip.Key);
            _sim.Note($"Tipp angezeigt: {tip.Action}", "help:tip");
        }

        void ShowStrategy()
        {
            if (_sim == null || _sim.Engine == null) return;
            _showingStrategy = true;
            _box.sizeDelta = new Vector2(760, 640);
            RenderStrategy(true);
            _overlay.SetActive(true);
            _sim.Note("Strategie angezeigt", "help:strategy");
        }

        void RenderStrategy(bool scrollTop)
        {
            var c = Context();
            double now = c.Vitals.SimTime_s;
            _title.text = "<color=#B9A8F0>Strategie</color>  <size=70%>optimales Vorgehen für diesen Fall — zeigt die Lösung</size>";
            var sb = new StringBuilder();
            string phase = null;
            foreach (var i in Coach.Strategy(c))
            {
                if (i.Phase != phase) { phase = i.Phase; sb.AppendLine($"\n<b><color=#9FB3C8>{Coach.PhaseNames[i.Phase].ToUpperInvariant()}</color></b>"); }
                bool done = i.Done_s.HasValue || (i.Phase == "Vermeiden" && !i.Missed);
                string col = i.Missed ? "#FF7A70" : done ? "#7FE08F" : "#F2C14E";
                sb.AppendLine($"<color={col}>-</color> <b><color={col}>{i.Action}</color></b>  <size=80%><color=#8A9A96>{Coach.Status(i, now)}</color></size>");
                if (!string.IsNullOrEmpty(i.Why)) sb.AppendLine($"<indent=4%><size=82%><color=#B8C2C9>{i.Why}</color></size></indent>");
            }
            var names = (c.Scenario.injuries ?? new ScenarioInjury[0]).Select(si => c.Injury(si.injuryId)).Where(d => d != null).Select(d => d.name);
            sb.AppendLine($"\n<b><color=#9FB3C8>VERLETZUNGEN IN DIESEM FALL</color></b>\n{string.Join(", ", names)}");
            _text.text = sb.ToString();
            if (scrollTop) _scroll.verticalNormalizedPosition = 1;
        }

        void Close() => _overlay.SetActive(false);

        /// <summary>Den Knopf der vorgeschlagenen Maßnahme bzw. Bildgebung kurz aufleuchten lassen.</summary>
        void Highlight(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            foreach (var b in Object.FindObjectsByType<ActionButton>(FindObjectsSortMode.None))
                if (b.Id == key) b.Highlight(4f);
        }
    }
}

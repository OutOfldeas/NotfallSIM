using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// „Ansicht &amp; Ton“ im Schockraum: Lautstärken (gesamt, Monitor-Ton, Alarme) und Stumm, jedes Interface
    /// (Monitor, Maßnahmen, Verdachtsdiagnosen, Befunde, Protokoll, Röntgenblick, Hinweis) ein- und ausblenden und
    /// in der Größe ändern, dazu eine Gesamtgröße. Jedes Panel bekommt ein „×“ zum Ausblenden. Einstellungen bleiben
    /// gespeichert (PlayerPrefs). Tasten: V Einstellungen, H alle Interfaces aus/ein, M stumm.
    /// </summary>
    public class ViewSettings : MonoBehaviour
    {
        // Anzeigename → Name des Panels in der Szene
        static readonly (string label, string name)[] Panels =
        {
            ("Patientenmonitor", "Monitor"), ("Maßnahmen", "Massnahmen"), ("Verdachtsdiagnosen", "Verdachtsdiagnosen"),
            ("Befunde", "Befunde"), ("Protokoll", "Protokoll"), ("Röntgenblick", "Röntgenblick"), ("Steuerungshinweis", "Hinweis"),
        };
        const string K = "NotfallSIM.View.";
        static readonly float[] Scales = { 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.25f, 1.4f, 1.6f };

        class Entry { public string Label, Name; public RectTransform Panel; public TMP_Text Toggle, Size; public Image ToggleBg; }
        readonly List<Entry> _entries = new List<Entry>();
        GameObject _overlay;
        TMP_Text _master, _monitor, _alarm, _mute, _global, _hideAll;
        bool _allHidden;
        readonly HashSet<string> _hiddenByH = new HashSet<string>();

        public static void Ensure()
        {
            if (Object.FindAnyObjectByType<ViewSettings>() != null) return;
            var go = new GameObject("Ansicht und Ton", typeof(RectTransform));
            go.AddComponent<ViewSettings>();
        }

        /// <summary>Panel eines Interfaces finden (Kind eines Bildschirm-Canvas, auch wenn ausgeblendet).</summary>
        public static RectTransform FindPanel(string name)
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                var t = c.transform.Find(name);
                if (t != null) return (RectTransform)t;
            }
            return null;
        }

        void Start()
        {
            AudioPrefs.Init();
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;   // über den Interfaces, unter Pausenmenü (100)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)transform;

            // Knopf unter „Menü (Esc)“
            var open = RuntimeUi.Button(root, "Ansicht & Ton (V)", new Color(0.12f, 0.14f, 0.17f, 0.92f), Toggle, 40, 16);
            var ob = (RectTransform)open.transform;
            ob.anchorMin = ob.anchorMax = ob.pivot = new Vector2(0, 1);
            ob.anchoredPosition = new Vector2(24, -70);
            ob.sizeDelta = new Vector2(150, 40);

            foreach (var pe in Panels)
            {
                var p = FindPanel(pe.name);
                if (p == null) continue;
                _entries.Add(new Entry { Label = pe.label, Name = pe.name, Panel = p });
                AddCloseButton(p, pe.name);
            }

            _overlay = RuntimeUi.Panel(root, "Einstellungen", new Color(0.06f, 0.08f, 0.1f, 0.97f)).gameObject;
            var box = (RectTransform)_overlay.transform;
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0, 1);
            box.anchoredPosition = new Vector2(24, -120);
            box.sizeDelta = new Vector2(470, 0);
            RuntimeUi.Column(box, 6, 16);
            _overlay.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RuntimeUi.Label(box, "<b>TON</b>", 18, new Color(0.6f, 0.68f, 0.7f), 26);
            _master = Stepper(box, "Gesamt", () => StepMaster(-0.1f), () => StepMaster(0.1f));
            _monitor = Stepper(box, "Monitor-Ton (QRS)", () => StepMonitor(-0.1f), () => StepMonitor(0.1f));
            _alarm = Stepper(box, "Alarme", () => StepAlarm(-0.1f), () => StepAlarm(0.1f));
            var muteRow = RowOf(box, 40);
            var mute = RuntimeUi.Button(muteRow, "", new Color(0.2f, 0.22f, 0.26f), ToggleMute, 40, 17);
            FlexW(mute);
            _mute = mute.GetComponentInChildren<TMP_Text>();

            RuntimeUi.Label(box, "<b>INTERFACES</b>  <size=80%>ein-/ausblenden und Größe</size>", 18, new Color(0.6f, 0.68f, 0.7f), 30);
            foreach (var e in _entries)
            {
                var row = RowOf(box, 36);
                var entry = e;
                var tb = RuntimeUi.Button(row, e.Label, new Color(0.2f, 0.22f, 0.26f), () => SetVisible(entry, !entry.Panel.gameObject.activeSelf), 36, 15);
                FlexW(tb);
                e.Toggle = tb.GetComponentInChildren<TMP_Text>();
                e.ToggleBg = tb.GetComponent<Image>();
                FixedW(RuntimeUi.Button(row, "-", new Color(0.2f, 0.22f, 0.26f), () => StepScale(entry, -1), 36, 20), 40);
                e.Size = RuntimeUi.Label(row, "", 16, Color.white);
                e.Size.alignment = TextAlignmentOptions.Center;
                FixedW(e.Size, 64);
                FixedW(RuntimeUi.Button(row, "+", new Color(0.2f, 0.22f, 0.26f), () => StepScale(entry, 1), 36, 20), 40);
            }
            _global = Stepper(box, "Gesamtgröße", () => StepGlobal(-1), () => StepGlobal(1));

            var actions = RowOf(box, 40);
            var hide = RuntimeUi.Button(actions, "", new Color(0.2f, 0.22f, 0.26f), ToggleHideAll, 40, 15);
            _hideAll = hide.GetComponentInChildren<TMP_Text>();
            FlexW(hide);
            FlexW(RuntimeUi.Button(actions, "Zurücksetzen", new Color(0.25f, 0.27f, 0.30f), ResetAll, 40, 15));
            FlexW(RuntimeUi.Button(actions, "Schließen", new Color(0.12f, 0.45f, 0.38f), Toggle, 40, 15));
            RuntimeUi.Label(box, "<size=85%>Tasten: V Einstellungen · H alle Interfaces aus/ein · M stumm</size>", 15, new Color(0.6f, 0.68f, 0.7f), 22);

            // gespeicherte Einstellungen anwenden
            foreach (var e in _entries)
            {
                e.Panel.gameObject.SetActive(PlayerPrefs.GetInt(K + e.Name + ".Visible", 1) == 1);
                e.Panel.localScale = Vector3.one * PlayerPrefs.GetFloat(K + e.Name + ".Scale", 1f);
            }
            ApplyGlobal();
            _overlay.SetActive(false);
            Refresh();
        }

        void Update()
        {
            bool v, h, m;
#if ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            v = kb.vKey.wasPressedThisFrame; h = kb.hKey.wasPressedThisFrame; m = kb.mKey.wasPressedThisFrame;
#else
            v = Input.GetKeyDown(KeyCode.V); h = Input.GetKeyDown(KeyCode.H); m = Input.GetKeyDown(KeyCode.M);
#endif
            if (v) Toggle();
            if (h) ToggleHideAll();
            if (m) ToggleMute();
        }

        // ------------------------------------------------------------------ Aktionen

        void Toggle() { _overlay.SetActive(!_overlay.activeSelf); Refresh(); }

        void StepMaster(float d) { AudioPrefs.Master = Mathf.Round((AudioPrefs.Master + d) * 10) / 10; Refresh(); }
        void StepMonitor(float d) { AudioPrefs.Monitor = Mathf.Round((AudioPrefs.Monitor + d) * 10) / 10; Refresh(); }
        void StepAlarm(float d) { AudioPrefs.Alarm = Mathf.Round((AudioPrefs.Alarm + d) * 10) / 10; Refresh(); }
        void ToggleMute() { AudioPrefs.Muted = !AudioPrefs.Muted; Refresh(); }

        void SetVisible(Entry e, bool on)
        {
            e.Panel.gameObject.SetActive(on);
            PlayerPrefs.SetInt(K + e.Name + ".Visible", on ? 1 : 0);
            PlayerPrefs.Save();
            if (on) _hiddenByH.Remove(e.Name);
            Refresh();
        }

        void StepScale(Entry e, int dir)
        {
            float s = Next(e.Panel.localScale.x, dir);
            e.Panel.localScale = Vector3.one * s;
            PlayerPrefs.SetFloat(K + e.Name + ".Scale", s);
            PlayerPrefs.Save();
            Refresh();
        }

        void StepGlobal(int dir)
        {
            PlayerPrefs.SetFloat(K + "Global", Next(PlayerPrefs.GetFloat(K + "Global", 1f), dir));
            PlayerPrefs.Save();
            ApplyGlobal();
            Refresh();
        }

        static float Next(float current, int dir)
        {
            int i = 0;
            for (int k = 0; k < Scales.Length; k++) if (Mathf.Abs(Scales[k] - current) < Mathf.Abs(Scales[i] - current)) i = k;
            return Scales[Mathf.Clamp(i + dir, 0, Scales.Length - 1)];
        }

        /// <summary>Gesamtgröße: Bezugsauflösung aller Interface-Canvas (nicht Pause- und Dosisdialog) skalieren.</summary>
        void ApplyGlobal()
        {
            float g = PlayerPrefs.GetFloat(K + "Global", 1f);
            var canvases = _entries.Select(e => e.Panel.GetComponentInParent<Canvas>(true)).Where(c => c != null).Select(c => c.rootCanvas).Distinct();
            foreach (var c in canvases)
            {
                var sc = c.GetComponent<CanvasScaler>();
                if (sc != null && sc.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize) sc.referenceResolution = new Vector2(1920, 1080) / g;
            }
        }

        /// <summary>H: alle sichtbaren Interfaces aus — und genau diese wieder ein.</summary>
        void ToggleHideAll()
        {
            if (!_allHidden)
            {
                _hiddenByH.Clear();
                foreach (var e in _entries) if (e.Panel.gameObject.activeSelf) { _hiddenByH.Add(e.Name); e.Panel.gameObject.SetActive(false); }
                _allHidden = true;
            }
            else
            {
                foreach (var e in _entries) if (_hiddenByH.Contains(e.Name)) e.Panel.gameObject.SetActive(true);
                _hiddenByH.Clear();
                _allHidden = false;
            }
            Refresh();
        }

        void ResetAll()
        {
            foreach (var e in _entries) { e.Panel.gameObject.SetActive(true); e.Panel.localScale = Vector3.one; PlayerPrefs.DeleteKey(K + e.Name + ".Visible"); PlayerPrefs.DeleteKey(K + e.Name + ".Scale"); }
            PlayerPrefs.DeleteKey(K + "Global");
            AudioPrefs.Master = 0.8f; AudioPrefs.Monitor = 0.6f; AudioPrefs.Alarm = 0.8f; AudioPrefs.Muted = false;
            _allHidden = false; _hiddenByH.Clear();
            ApplyGlobal();
            Refresh();
        }

        void Refresh()
        {
            if (_master == null) return;
            _master.text = Pct(AudioPrefs.Master);
            _monitor.text = Pct(AudioPrefs.Monitor);
            _alarm.text = Pct(AudioPrefs.Alarm);
            _mute.text = AudioPrefs.Muted ? "Ton ist STUMM — einschalten (M)" : "Stumm schalten (M)";
            _global.text = Pct(PlayerPrefs.GetFloat(K + "Global", 1f));
            _hideAll.text = _allHidden ? "Interfaces ein (H)" : "Alle ausblenden (H)";
            foreach (var e in _entries)
            {
                bool on = e.Panel.gameObject.activeSelf;
                e.Toggle.text = e.Label + (on ? "  <color=#7CE08F><size=80%>sichtbar</size></color>" : "  <color=#8A8F96><size=80%>aus</size></color>");
                e.ToggleBg.color = on ? new Color(0.16f, 0.3f, 0.24f) : new Color(0.2f, 0.22f, 0.26f);
                e.Size.text = Pct(e.Panel.localScale.x);
            }
        }

        static string Pct(float x) => $"{Mathf.RoundToInt(x * 100)} %";

        // ------------------------------------------------------------------ Helfer

        /// <summary>„×“ oben rechts im Panel (nicht im Layout) zum schnellen Ausblenden.</summary>
        void AddCloseButton(RectTransform panel, string name)
        {
            var b = RuntimeUi.Button(panel, "×", new Color(0.25f, 0.27f, 0.3f, 0.85f), () =>
            {
                var e = _entries.FirstOrDefault(x => x.Name == name);
                if (e != null) SetVisible(e, false);
            }, 24, 18);
            var le = b.GetComponent<LayoutElement>();
            le.ignoreLayout = true;
            var r = (RectTransform)b.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(1, 1);
            r.anchoredPosition = new Vector2(-4, -4);
            r.sizeDelta = new Vector2(24, 24);
            var t = b.GetComponentInChildren<TMP_Text>();
            var tr = t.rectTransform; tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
        }

        static RectTransform RowOf(Transform parent, float h)
        {
            var go = new GameObject("Zeile", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = true;
            var le = go.AddComponent<LayoutElement>(); le.preferredHeight = h; le.minHeight = h;
            return (RectTransform)go.transform;
        }

        TMP_Text Stepper(Transform parent, string label, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            var row = RowOf(parent, 36);
            var l = RuntimeUi.Label(row, label, 16, Color.white);
            l.alignment = TextAlignmentOptions.Left;
            FlexW(l);
            FixedW(RuntimeUi.Button(row, "-", new Color(0.2f, 0.22f, 0.26f), minus, 36, 20), 40);
            var value = RuntimeUi.Label(row, "", 16, Color.white);
            value.alignment = TextAlignmentOptions.Center;
            FixedW(value, 64);
            FixedW(RuntimeUi.Button(row, "+", new Color(0.2f, 0.22f, 0.26f), plus, 36, 20), 40);
            return value;
        }

        static void FlexW(Component c) { var le = c.GetComponent<LayoutElement>(); if (le == null) le = c.gameObject.AddComponent<LayoutElement>(); le.flexibleWidth = 1; le.minWidth = 0; }
        static void FixedW(Component c, float w) { var le = c.GetComponent<LayoutElement>(); if (le == null) le = c.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.minWidth = w; le.flexibleWidth = 0; }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Text;
using NotfallSIM.Interaction;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Protokoll unten mit Reitern: <b>Verlauf</b> (alle Einträge, vollständig), <b>Untersuchung</b> (Befunde jeder
    /// untersuchten Region), <b>Bildgebung</b> (alle Radiologiebefunde im Wortlaut) und <b>Labor</b> (BGA, Gerinnung).
    /// Nichts geht verloren; die Liste scrollt (Mausrad über dem Panel), neueste Einträge oben. „Groß“ vergrößert
    /// das Panel zum Lesen. Ersetzt zur Laufzeit die alte Kurzliste (auch in älteren Szenen).
    /// </summary>
    public class ProtocolPanel : MonoBehaviour
    {
        enum Tab { Verlauf, Untersuchung, Bildgebung, Labor }
        static readonly string[] TabNames = { "Verlauf", "Untersuchung", "Bildgebung", "Labor" };

        class Entry { public double Time; public Tab Kind; public string Title, Body, Color; }

        SimulationManager _sim;
        readonly List<Entry> _all = new List<Entry>();
        Tab _tab = Tab.Verlauf;
        TMP_Text _text, _sizeLabel;
        readonly Image[] _tabBg = new Image[4];
        readonly TMP_Text[] _tabText = new TMP_Text[4];
        ScrollRect _scroll;
        RectTransform _panel;
        Vector2 _smallSize;
        bool _large;
        readonly int[] _unread = new int[4];

        public static ProtocolPanel Install(SimulationManager sim)
        {
            var panel = ViewSettings.FindPanel("Protokoll");
            if (panel == null || panel.GetComponent<ProtocolPanel>() != null) return null;
            var p = panel.gameObject.AddComponent<ProtocolPanel>();
            p._sim = sim;
            p.Build(panel);
            // bisherige Einträge übernehmen (das Szenario läuft schon)
            foreach (var e in sim.Events) p.OnLog(e.Time_s, e.Message, e.Tag);
            for (int i = 0; i < 4; i++) p._unread[i] = 0;
            p.Subscribe();
            p.Render();
            return p;
        }

        // Auch ausgeblendet weiter mitschreiben: deshalb nicht in OnEnable/OnDisable an- und abmelden.
        void Subscribe() { BodyRegion.OnExamined += OnExamined; _sim.OnLogEntry += OnLog; }
        void OnDestroy() { BodyRegion.OnExamined -= OnExamined; if (_sim) _sim.OnLogEntry -= OnLog; }

        // ------------------------------------------------------------------ Aufbau

        void Build(RectTransform panel)
        {
            _panel = panel;
            foreach (var old in panel.GetComponents<EventLogPanel>()) Destroy(old);
            for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);
            _smallSize = panel.sizeDelta;

            var tabs = new GameObject("Reiter", typeof(RectTransform));
            tabs.transform.SetParent(panel, false);
            var tr = (RectTransform)tabs.transform;
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 1);
            tr.offsetMin = new Vector2(12, -42); tr.offsetMax = new Vector2(-40, -8);   // rechts Platz für „ד
            var h = tabs.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var title = RuntimeUi.Label(tabs.transform, "<b>PROTOKOLL</b>", 17, new Color(0.6f, 0.68f, 0.7f));
            title.alignment = TextAlignmentOptions.Left;
            Width(title, 120);
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                var b = RuntimeUi.Button(tabs.transform, TabNames[i], new Color(0.2f, 0.22f, 0.26f), () => SelectTab((Tab)k), 32, 15);
                Width(b, 128);
                _tabBg[i] = b.GetComponent<Image>();
                _tabText[i] = b.GetComponentInChildren<TMP_Text>();
            }
            var spacer = new GameObject("Abstand", typeof(RectTransform));
            spacer.transform.SetParent(tabs.transform, false);
            spacer.AddComponent<LayoutElement>().flexibleWidth = 1;
            var size = RuntimeUi.Button(tabs.transform, "Groß", new Color(0.2f, 0.22f, 0.26f), ToggleLarge, 32, 14);
            Width(size, 70);
            _sizeLabel = size.GetComponentInChildren<TMP_Text>();

            // Scrollbereich
            var view = new GameObject("Liste", typeof(RectTransform));
            view.transform.SetParent(panel, false);
            var vr = (RectTransform)view.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(16, 10); vr.offsetMax = new Vector2(-16, -48);
            view.AddComponent<RectMask2D>();
            var bg = view.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.001f);   // fängt das Mausrad über der ganzen Fläche
            _text = RuntimeUi.Label(view.transform, "", 16, Color.white);
            _text.alignment = TextAlignmentOptions.TopLeft;
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            _text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll = view.AddComponent<ScrollRect>();
            _scroll.content = rt;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 30;
            _scroll.viewport = vr;
        }

        // ------------------------------------------------------------------ Einträge

        void OnLog(double time, string message, string tag)
        {
            if (tag != null && tag.StartsWith("injury:")) return;   // verrät die Diagnose
            string color = tag == null ? "#C8CDD3"
                : tag.StartsWith("state:") || tag == "arrest" || tag.StartsWith("warning:") ? "#FF7A70"
                : tag.StartsWith("result:") ? "#7FD3FF"
                : tag.StartsWith("intervention:") || tag.StartsWith("effect:") ? "#7FE08F"
                : tag.StartsWith("diagnosis") ? "#F2C14E" : "#C8CDD3";
            Add(new Entry { Time = time, Kind = Tab.Verlauf, Body = message, Color = color });
            if (tag == null) return;
            if (tag.StartsWith("result:Imaging"))
            {
                // „Befund CT Ganzkörper: …“ → Titel und Text
                int colon = message.IndexOf(": ");
                string head = colon > 0 ? message.Substring(0, colon) : "Bildgebung";
                string body = colon > 0 ? message.Substring(colon + 2) : message;
                Add(new Entry { Time = time, Kind = Tab.Bildgebung, Title = head.Replace("Befund ", ""), Body = body, Color = "#7FD3FF" });
            }
            else if (tag.StartsWith("result:"))
            {
                int colon = message.IndexOf(": ");
                string head = colon > 0 ? message.Substring(0, colon) : "Labor";
                string body = colon > 0 ? message.Substring(colon + 2).Replace(" · ", "\n") : message;
                Add(new Entry { Time = time, Kind = Tab.Labor, Title = head, Body = body, Color = "#7FD3FF" });
            }
        }

        void OnExamined(string region, IReadOnlyList<string> findings)
        {
            double t = _sim != null && _sim.Engine != null ? _sim.Vitals.SimTime_s : 0;
            string body = findings.Count == 0 ? "Unauffällig." : string.Join("\n", findings.Select(f => "• " + f));
            Add(new Entry { Time = t, Kind = Tab.Untersuchung, Title = region, Body = body, Color = "#E8ECEF" });
        }

        void Add(Entry e)
        {
            _all.Add(e);
            if (e.Kind != _tab) _unread[(int)e.Kind]++;
            Render();
        }

        // ------------------------------------------------------------------ Anzeige

        public void SelectTab(int tab) => SelectTab((Tab)Mathf.Clamp(tab, 0, 3));

        void SelectTab(Tab tab)
        {
            _tab = tab;
            _unread[(int)tab] = 0;
            Render();
            if (_scroll) _scroll.verticalNormalizedPosition = 1;
        }

        void ToggleLarge()
        {
            _large = !_large;
            _panel.sizeDelta = _large ? new Vector2(_smallSize.x, Mathf.Max(560, _smallSize.y * 2.2f)) : _smallSize;
            Render();
        }

        void Render()
        {
            if (_text == null) return;
            var sb = new StringBuilder();
            var list = _all.Where(e => e.Kind == _tab).Reverse().ToList();   // neueste oben
            if (list.Count == 0)
                sb.Append(_tab == Tab.Verlauf ? "<color=#8A9A96>Noch keine Einträge.</color>"
                        : _tab == Tab.Untersuchung ? "<color=#8A9A96>Noch keine Untersuchung. Körperregion anklicken.</color>"
                        : _tab == Tab.Bildgebung ? "<color=#8A9A96>Noch keine Bildgebung angefordert.</color>"
                        : "<color=#8A9A96>Noch keine Laborwerte (BGA, Gerinnung).</color>");
            foreach (var e in list)
            {
                int t = (int)e.Time;
                string clock = $"<color=#8A9A96>{t / 60:00}:{t % 60:00}</color>";
                if (_tab == Tab.Verlauf) sb.AppendLine($"{clock}  <color={e.Color}>{e.Body}</color>");
                else
                {
                    sb.AppendLine($"{clock}  <b><color={e.Color}>{e.Title}</color></b>");
                    sb.AppendLine($"<indent=6%>{e.Body}</indent>");
                    sb.AppendLine();
                }
            }
            _text.text = sb.ToString();
            for (int i = 0; i < 4; i++)
            {
                bool on = (int)_tab == i;
                _tabBg[i].color = on ? new Color(0.16f, 0.36f, 0.5f) : new Color(0.2f, 0.22f, 0.26f);
                _tabText[i].text = TabNames[i] + (_unread[i] > 0 ? $" <color=#F2C14E>({_unread[i]})</color>" : "");
            }
            _sizeLabel.text = _large ? "Klein" : "Groß";
        }

        static void Width(Component c, float w)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.minWidth = w; le.flexibleWidth = 0;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using NotfallSIM.Core;
using NotfallSIM.Simulation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Patientenmonitor wie im Schockraum: EKG mit HF, Pulsoxymetrie mit SpO2, Pulsfrequenz und Perfusionsindex,
    /// NIBP im Messzyklus (mit Messzeitpunkt, Intervall und „Messen“), Kapnografie mit etCO2 und AF, Temperatur,
    /// Alarmgrenzen an jedem Wert und Alarmzeile mit Prioritäten (rot/gelb/türkis) und Alarmton, der sich für 2 min
    /// stummschalten lässt. Baut den Inhalt des Panels „Monitor“ zur Laufzeit neu auf (auch in älteren Szenen) und
    /// versorgt den Monitor an der Deckenversorgung im Raum mit denselben Werten.
    /// </summary>
    public class PatientMonitor : MonoBehaviour
    {
        public static PatientMonitor Instance { get; private set; }

        public readonly NibpCycle Nibp = new NibpCycle();
        public readonly AlarmLimits Limits = new AlarmLimits();
        public List<MonitorAlarm> Alarms { get; private set; } = new List<MonitorAlarm>();
        public float AlarmSilencedUntil { get; private set; } = -1;   // Echtzeit (Time.unscaledTime)

        SimulationManager _sim;
        TMP_Text _clock, _alarmText, _silenceLabel, _hr, _hrLim, _spo2, _spo2Lim, _pr, _nibp, _nibpMap, _nibpInfo, _nibpLim, _co2, _co2Lim, _rr, _temp, _intervalLabel;
        Image _alarmBar;
        // Monitor im Raum
        TMP_Text _wHr, _wSpo2, _wCo2, _wRr, _wNibp, _wTemp, _wAlarm;
        float _refresh;
        AudioSource _audio;
        AudioClip _high, _medium, _low;
        float _nextTone;
        AlarmPriority? _lastPriority;

        static readonly Color HrC = new Color(0.3f, 1f, 0.4f), BpC = new Color(1f, 0.45f, 0.45f), SpC = new Color(0.4f, 0.85f, 1f),
                              Co2C = new Color(1f, 0.9f, 0.4f), TempC = new Color(0.92f, 0.94f, 0.95f), Dim = new Color(0.55f, 0.62f, 0.6f);
        static readonly Color HighC = new Color(0.9f, 0.12f, 0.1f), MedC = new Color(0.95f, 0.75f, 0.1f), LowC = new Color(0.2f, 0.75f, 0.8f);

        /// <summary>Panel „Monitor“ im UI-Canvas neu aufbauen bzw. anlegen.</summary>
        public static PatientMonitor Install(SimulationManager sim)
        {
            if (Instance != null) return Instance;
            var panel = ViewSettings.FindPanel("Monitor");
            if (panel == null)
            {
                var canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).FirstOrDefault(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay);
                if (canvas == null) return null;
                panel = RuntimeUi.Panel(canvas.transform, "Monitor", new Color(0.05f, 0.07f, 0.09f, 0.9f));
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 1);
                panel.anchoredPosition = new Vector2(-20, -20);
                panel.gameObject.AddComponent<VerticalLayoutGroup>();
                panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            // Logik auf eigenem Objekt: läuft weiter (NIBP, Alarme, Kurven, Ton), auch wenn das Panel ausgeblendet ist
            var pm = new GameObject("Patientenmonitor (Logik)").AddComponent<PatientMonitor>();
            pm._sim = sim;
            pm.Build(panel);
            pm.BuildWorld();
            Instance = pm;
            return pm;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------------ Aufbau

        void Build(RectTransform panel)
        {
            foreach (var old in panel.GetComponents<VitalsMonitor>()) Destroy(old);
            for (int i = panel.childCount - 1; i >= 0; i--) Destroy(panel.GetChild(i).gameObject);
            panel.sizeDelta = new Vector2(400, panel.sizeDelta.y);
            var v = panel.GetComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(12, 12, 10, 12);
            v.spacing = 4;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;

            var head = Row(panel, 24);
            var title = Text(head, "PATIENTENMONITOR · Schockraum 1", 15, Dim, TextAlignmentOptions.Left); Flex(title);
            _clock = Text(head, "00:00", 18, Dim, TextAlignmentOptions.Right); Fixed(_clock, 70);

            var alarmRow = Row(panel, 30);
            _alarmBar = alarmRow.gameObject.AddComponent<Image>();
            _alarmBar.color = new Color(0, 0, 0, 0);
            _alarmText = Text(alarmRow, "", 18, Color.white, TextAlignmentOptions.Left); Flex(_alarmText);
            var silence = RuntimeUi.Button(alarmRow, "Alarmton 2 min aus", new Color(0.2f, 0.22f, 0.26f), SilenceAlarms, 28, 13);
            Fixed(silence, 130);
            _silenceLabel = silence.GetComponentInChildren<TMP_Text>();

            var oldWaves = Object.FindAnyObjectByType<MonitorWaveforms>();   // aus der Szene (Panel) oder am Monitor im Raum
            var waves = gameObject.AddComponent<MonitorWaveforms>();
            waves.Configure(_sim, null, null, null);
            if (oldWaves != null)
            {
                var m = oldWaves.MirrorImages;   // Kurven am Monitor im Raum übernehmen
                if (m != null && m.Length == 3) waves.SetMirror(m[0], m[1], m[2]);
                Destroy(oldWaves);
            }

            var ecg = WaveRow(panel, "II", HrC, out var hrCol);
            ValueBlock(hrCol, "HF", "/min", HrC, out _hr, out _hrLim, 44);

            var pleth = WaveRow(panel, "Pleth", SpC, out var spCol);
            ValueBlock(spCol, "SpO2", "%", SpC, out _spo2, out _spo2Lim, 44);
            _pr = Text(spCol, "", 13, SpC, TextAlignmentOptions.Right); Height(_pr, 16);

            // NIBP: Wert mit Messzeit, Intervall und Taste
            var nibpRow = Row(panel, 70);
            var nibpLeft = Column(nibpRow); Flex(nibpLeft);
            var lab = Row(nibpLeft, 18);
            Flex(Text(lab, "NIBP  mmHg", 14, BpC, TextAlignmentOptions.Left));
            _nibpLim = Text(lab, "", 12, Dim, TextAlignmentOptions.Right); Fixed(_nibpLim, 90);
            var val = Row(nibpLeft, 38);
            _nibp = Text(val, "---/---", 34, BpC, TextAlignmentOptions.Left); Fixed(_nibp, 170);
            _nibpMap = Text(val, "(--)", 24, BpC, TextAlignmentOptions.Left); Flex(_nibpMap);
            _nibpInfo = Text(nibpLeft, "", 12, Dim, TextAlignmentOptions.Left); Height(_nibpInfo, 14);
            var nibpButtons = Column(nibpRow); Fixed(nibpButtons, 104);
            RuntimeUi.Button(nibpButtons, "NIBP messen", new Color(0.45f, 0.16f, 0.16f), MeasureNow, 32, 14);
            var interval = RuntimeUi.Button(nibpButtons, "Intervall", new Color(0.2f, 0.22f, 0.26f), CycleInterval, 32, 13);
            _intervalLabel = interval.GetComponentInChildren<TMP_Text>();

            var capno = WaveRow(panel, "CO2", Co2C, out var co2Col);
            ValueBlock(co2Col, "etCO2", "mmHg", Co2C, out _co2, out _co2Lim, 36);
            _rr = Text(co2Col, "AF --", 15, Co2C, TextAlignmentOptions.Right); Height(_rr, 18);

            var tempRow = Row(panel, 26);
            Flex(Text(tempRow, "Temp  °C", 14, TempC, TextAlignmentOptions.Left));
            _temp = Text(tempRow, "--,-", 22, TempC, TextAlignmentOptions.Right); Fixed(_temp, 90);

            waves.Retarget(ecg, pleth, capno);

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _high = Burst(new[] { 0f, 0.18f, 0.36f, 0.9f, 1.08f }, 960);   // 3 + 2 Pulse (hohe Priorität)
            _medium = Burst(new[] { 0f, 0.25f, 0.5f }, 700);                // 3 Pulse (mittlere Priorität)
            _low = Burst(new[] { 0f, 0.3f }, 520);                          // 2 Pulse (niedrige Priorität)
        }

        /// <summary>Den Monitor an der Deckenversorgung umbauen: Kurven bleiben, Werte kommen von hier.</summary>
        void BuildWorld()
        {
            var go = GameObject.Find("Monitor-Anzeige");
            if (go == null) return;
            foreach (var old in go.GetComponents<VitalsMonitor>()) Destroy(old);
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) Destroy(t.gameObject);
            var root = go.transform;
            _wAlarm = WorldText(root, new Vector2(10, -2), new Vector2(380, 20), 16, Color.white, TextAlignmentOptions.Left);
            _wHr = WorldText(root, new Vector2(400, -20), new Vector2(150, 70), 60, HrC, TextAlignmentOptions.Right);
            _wSpo2 = WorldText(root, new Vector2(400, -140), new Vector2(150, 70), 54, SpC, TextAlignmentOptions.Right);
            _wCo2 = WorldText(root, new Vector2(400, -255), new Vector2(150, 50), 40, Co2C, TextAlignmentOptions.Right);
            _wRr = WorldText(root, new Vector2(400, -305), new Vector2(150, 30), 22, Co2C, TextAlignmentOptions.Right);
            _wNibp = WorldText(root, new Vector2(10, -340), new Vector2(380, 36), 28, BpC, TextAlignmentOptions.Left);
            _wTemp = WorldText(root, new Vector2(400, -340), new Vector2(150, 36), 24, TempC, TextAlignmentOptions.Right);
        }

        // ------------------------------------------------------------------ Bedienung

        public void MeasureNow() { if (_sim != null && _sim.Engine != null) Nibp.StartNow(_sim.Vitals.SimTime_s); }
        public void CycleInterval() { if (_sim != null && _sim.Engine != null) Nibp.CycleInterval(_sim.Vitals.SimTime_s); }
        public void SilenceAlarms() { AlarmSilencedUntil = AlarmSilencedUntil > Time.unscaledTime ? -1 : Time.unscaledTime + 120; }

        // ------------------------------------------------------------------ Laufzeit

        void Update()
        {
            if (_sim == null || _sim.Engine == null) return;
            var v = _sim.Vitals;
            Nibp.Update(v);
            Alarms = PatientMonitorLogic.Evaluate(v, Nibp, Limits);
            AlarmSound();
            _refresh -= Time.unscaledDeltaTime;
            if (_refresh > 0) return;
            _refresh = 0.25f;   // Monitore aktualisieren Zahlen etwa 1–4-mal pro Sekunde
            Show(v);
        }

        bool InAlarm(string p) => Alarms.Any(a => a.Parameter == p);
        bool Blink => Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;

        void Show(VitalSigns v)
        {
            int t = (int)v.SimTime_s;
            _clock.text = $"{t / 60:00}:{t % 60:00}";
            var L = Limits;
            bool arrest = v.CardiacArrest, spOk = PatientMonitorLogic.SpO2Valid(v);

            Set(_hr, arrest ? "0" : $"{v.HeartRate_bpm:0}", HrC, InAlarm("HF"));
            _hrLim.text = $"{L.HrHigh:0}\n{L.HrLow:0}";
            Set(_spo2, spOk ? $"{v.SpO2_percent:0}" : "--", SpC, InAlarm("SpO2"));
            _spo2Lim.text = $"100\n{L.Spo2Low:0}";
            _pr.text = spOk ? $"PR {v.HeartRate_bpm:0}  PI {PatientMonitorLogic.PerfusionIndex(v):0.0}" : "Signal ?";
            Set(_co2, $"{v.EtCO2_mmHg:0}", Co2C, InAlarm("etCO2"));
            _co2Lim.text = $"{L.EtCo2High:0}\n{L.EtCo2Low:0}";
            Set(_rr, (v.Intubated ? "AF (Beatm.) " : "AF ") + (arrest ? "0" : $"{v.RespirationRate_bpm:0}"), Co2C, InAlarm("AF"));
            Set(_temp, $"{v.Temperature_C:0.0}", TempC, InAlarm("Temp"));

            var n = Nibp;
            string nibpText = !n.HasReading || n.Failed ? "---/---" : $"{n.Sys:0}/{n.Dia:0}";
            Set(_nibp, nibpText, BpC, InAlarm("NIBP"));
            _nibpMap.text = n.HasReading && !n.Failed ? $"({n.Map:0})" : "(--)";
            _nibpLim.text = $"sys {L.SysLow:0}–{L.SysHigh:0}";
            string age = n.HasReading ? $"gemessen vor {Ago(v.SimTime_s - n.MeasuredAt_s)}" : "noch keine Messung";
            _nibpInfo.text = (n.Measuring ? "<color=#FF8C8C>Messung läuft …</color>  " : "") + age + $" · Intervall {NibpCycle.IntervalLabel(n.Interval_s)}";
            _intervalLabel.text = $"Intervall {NibpCycle.IntervalLabel(n.Interval_s)}";

            // Alarmzeile: höchste Priorität zuerst, weitere Meldungen dahinter
            if (Alarms.Count == 0) { _alarmText.text = ""; _alarmBar.color = new Color(0, 0, 0, 0); }
            else
            {
                var top = Alarms[0];
                var c = top.Priority == AlarmPriority.High ? HighC : top.Priority == AlarmPriority.Medium ? MedC : LowC;
                _alarmBar.color = top.Priority == AlarmPriority.High && !Blink ? c * 0.55f : c;
                _alarmText.color = top.Priority == AlarmPriority.Medium ? Color.black : Color.white;
                string stars = top.Priority == AlarmPriority.High ? "*** " : top.Priority == AlarmPriority.Medium ? "** " : "* ";
                _alarmText.text = stars + top.Text + (Alarms.Count > 1 ? $"  <size=75%>(+{Alarms.Count - 1}: {string.Join(", ", Alarms.Skip(1).Take(2).Select(a => a.Text))})</size>" : "");
            }
            bool silenced = AlarmSilencedUntil > Time.unscaledTime;
            _silenceLabel.text = silenced ? $"Alarmton aus {Mathf.CeilToInt(AlarmSilencedUntil - Time.unscaledTime)} s" : "Alarmton 2 min aus";

            if (_wHr == null) return;
            _wHr.text = arrest ? "0" : $"{v.HeartRate_bpm:0}";
            _wSpo2.text = spOk ? $"{v.SpO2_percent:0}" : "--";
            _wCo2.text = $"{v.EtCO2_mmHg:0}";
            _wRr.text = $"AF {(arrest ? 0 : v.RespirationRate_bpm):0}";
            _wNibp.text = $"NIBP {nibpText} {_nibpMap.text}" + (n.HasReading ? $"  <size=60%>vor {Ago(v.SimTime_s - n.MeasuredAt_s)}</size>" : "");
            _wTemp.text = $"{v.Temperature_C:0.0} °C";
            _wAlarm.text = Alarms.Count > 0 ? (Alarms[0].Priority == AlarmPriority.High ? "<color=#FF4A3D>*** " : "<color=#F2C11A>** ") + Alarms[0].Text + "</color>" : "";
        }

        static string Ago(double s) { int x = Mathf.Max(0, (int)s); return x < 60 ? $"{x} s" : $"{x / 60}:{x % 60:00} min"; }

        void Set(TMP_Text t, string text, Color normal, bool alarm)
        {
            t.text = text;
            t.color = alarm && Blink ? new Color(1f, 0.3f, 0.25f) : normal;
        }

        void AlarmSound()
        {
            if (_audio == null) return;
            if (_sim.Paused || Alarms.Count == 0 || AlarmSilencedUntil > Time.unscaledTime) { _lastPriority = null; return; }
            var p = Alarms[0].Priority;
            // neuer oder höherer Alarm: sofort; sonst im Wiederholungsabstand (hoch 6 s, mittel 12 s, niedrig 30 s)
            if (_lastPriority == null || p > _lastPriority) _nextTone = 0;
            _lastPriority = p;
            if (Time.unscaledTime < _nextTone) return;
            _audio.volume = AudioPrefs.AlarmVolume * 0.5f;
            _audio.PlayOneShot(p == AlarmPriority.High ? _high : p == AlarmPriority.Medium ? _medium : _low);
            _nextTone = Time.unscaledTime + (p == AlarmPriority.High ? 6 : p == AlarmPriority.Medium ? 12 : 30);
        }

        static AudioClip Burst(float[] starts, float freq)
        {
            const int rate = 44100;
            const float pulse = 0.13f;
            int n = (int)(rate * (starts.Max() + pulse + 0.05f));
            var data = new float[n];
            foreach (float st in starts)
            {
                int i0 = (int)(st * rate), len = (int)(pulse * rate);
                for (int i = 0; i < len && i0 + i < n; i++)
                {
                    float env = Mathf.Min(1, i / 300f) * Mathf.Min(1, (len - i) / 600f);
                    float x = 2 * Mathf.PI * freq * i / rate;
                    data[i0 + i] += env * (0.6f * Mathf.Sin(x) + 0.25f * Mathf.Sin(2 * x) + 0.15f * Mathf.Sin(3 * x));   // Obertöne wie bei Alarmgebern
                }
            }
            var clip = AudioClip.Create("Alarm", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ------------------------------------------------------------------ Layout-Helfer

        static RectTransform Row(Transform parent, float height)
        {
            var go = new GameObject("Zeile", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var le = go.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minHeight = height;
            return (RectTransform)go.transform;
        }

        static RectTransform Column(Transform parent)
        {
            var go = new GameObject("Spalte", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            go.AddComponent<LayoutElement>();
            return (RectTransform)go.transform;
        }

        static TMP_Text Text(Transform parent, string text, float size, Color c, TextAlignmentOptions align)
        {
            var t = RuntimeUi.Label(parent, text, size, c);
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static LayoutElement Le(Component c)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();   // kein ??: Unity-„Fake-null“
            return le;
        }
        static void Flex(Component c) { var le = Le(c); le.flexibleWidth = 1; le.minWidth = 0; }
        static void Fixed(Component c, float w) { var le = Le(c); le.preferredWidth = w; le.minWidth = w; le.flexibleWidth = 0; }
        static void Height(Component c, float h) { var le = Le(c); le.preferredHeight = h; le.minHeight = h; }

        /// <summary>Kurve links (mit Kanalname), Wertespalte rechts.</summary>
        static RawImage WaveRow(Transform parent, string name, Color c, out RectTransform valueColumn)
        {
            var row = Row(parent, 66);
            var waveCol = Column(row); Flex(waveCol);
            Height(Text(waveCol, name, 12, c, TextAlignmentOptions.Left), 14);
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(waveCol, false);
            var img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>(); le.preferredHeight = 48; le.flexibleWidth = 1;
            valueColumn = Column(row); Fixed(valueColumn, 120);
            return img;
        }

        /// <summary>Beschriftung mit Einheit und Alarmgrenzen (oben/unten) neben großem Wert.</summary>
        static void ValueBlock(Transform col, string label, string unit, Color c, out TMP_Text value, out TMP_Text limits, float size)
        {
            var top = Row(col, 14);
            Flex(Text(top, $"{label} <size=80%>{unit}</size>", 12, c, TextAlignmentOptions.Left));
            var main = Row(col, size * 0.95f);
            limits = Text(main, "", 10, Dim, TextAlignmentOptions.Left); Fixed(limits, 26);
            value = Text(main, "--", size, c, TextAlignmentOptions.Right); Flex(value);
        }

        static TMP_Text WorldText(Transform parent, Vector2 pos, Vector2 size, float fontSize, Color c, TextAlignmentOptions align)
        {
            var t = RuntimeUi.Label(parent, "", fontSize, c);
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = pos; r.sizeDelta = size;
            return t;
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NotfallSIM.Data;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Steuerung der Unfall-Sandbox: Mechanismus und Geschwindigkeit wählen, Crash in Zeitlupe abspielen,
    /// Messwerte und Verletzungsrisiken anzeigen, Körperteile nach Risiko einfärben und den Fall
    /// (verdeckt) in den Schockraum übergeben.
    /// </summary>
    public class CrashSandbox : MonoBehaviour
    {
        [SerializeField] CrashRig rig;
        [SerializeField] TMP_Text settingsText;
        [SerializeField] TMP_Text resultText;
        [SerializeField] GameObject toTraumaRoomButton;
        [SerializeField] Camera cam;
        [SerializeField] string traumaRoomScene = "Schockraum";

        readonly CrashParams _p = new CrashParams();
        float _slowMo = 0.2f;
        CrashResult _result;

        static readonly Dictionary<string, (string label, float speed, float min, float max, float step, string unit)> Mechanisms =
            new Dictionary<string, (string, float, float, float, float, string)>
            {
                { "pkw_frontal", ("PKW frontal", 50, 10, 120, 10, "km/h") },
                { "pkw_seite",   ("PKW seitlich (Fahrerseite)", 50, 10, 100, 10, "km/h") },
                { "motorrad",    ("Motorrad gegen PKW", 50, 10, 120, 10, "km/h") },
                { "fussgaenger", ("Fußgänger", 40, 10, 90, 10, "km/h") },
                { "sturz_hoehe", ("Sturz aus der Höhe", 6, 1, 20, 1, "m") },
            };

        public void Configure(CrashRig crashRig, TMP_Text settings, TMP_Text results, GameObject toTraumaRoom, Camera camera)
        {
            rig = crashRig; settingsText = settings; resultText = results; toTraumaRoomButton = toTraumaRoom; cam = camera;
        }

        void Start()
        {
            SelectMechanism("pkw_frontal");
            if (toTraumaRoomButton) toTraumaRoomButton.SetActive(false);
            resultText.text = "Mechanismus wählen und „Crash starten“ drücken.";
        }

        // ---------------------------------------------------------------- UI-Aktionen (im SceneBuilder verdrahtet)

        public void SelectMechanism(string id)
        {
            if (rig.Running) return;
            _p.MechanismId = id;
            _p.Speed_kmh = Mechanisms[id].speed;
            Preview();
        }

        public void ChangeSpeed(float direction)
        {
            if (rig.Running) return;
            var m = Mechanisms[_p.MechanismId];
            _p.Speed_kmh = Mathf.Clamp(_p.Speed_kmh + Mathf.Sign(direction) * m.step, m.min, m.max);
            Preview();
        }

        public void ToggleBelt() { if (rig.Running) return; _p.Belted = !_p.Belted; Preview(); }

        public void SetSlowMotion(float factor) { _slowMo = factor; ShowSettings(); }

        public void StartCrash()
        {
            if (rig.Running) return;
            _p.Seed = Random.Range(1, 100000);
            _result = null;
            if (toTraumaRoomButton) toTraumaRoomButton.SetActive(false);
            resultText.text = "Crash läuft …";
            StartCoroutine(rig.Run(_p, _slowMo, OnMeasured));
        }

        public void GoToTraumaRoom()
        {
            if (_result == null) return;
            CrashHandoff.Pending = _result.Scenario;
            SceneManager.LoadScene(traumaRoomScene);
        }

        // ---------------------------------------------------------------- Ablauf

        void Preview()
        {
            rig.Build(_p, out _); // Ausgangslage zeigen, Physik ruht bis zum Start
            foreach (var rb in rig.Ragdoll.Segments.Values) rb.isKinematic = true;
            ShowSettings();
        }

        void ShowSettings()
        {
            var m = Mechanisms[_p.MechanismId];
            bool car = _p.MechanismId.StartsWith("pkw");
            settingsText.text = $"<b>{m.label}</b>\n{(_p.MechanismId == "sturz_hoehe" ? "Höhe" : "Geschwindigkeit")}: {_p.Speed_kmh:0} {m.unit}" +
                                (car ? $"\nGurt: {(_p.Belted ? "angelegt" : "nicht angelegt")}" : "") +
                                $"\nZeitlupe: {(_slowMo >= 1 ? "aus" : $"×{_slowMo:0.##}")}";
        }

        void OnMeasured(List<CrashMeasurement> measurements)
        {
            string root = InjuryDatabase.Root;
            var risk = JsonUtility.FromJson<InjuryRiskDatabaseFile>(File.ReadAllText(Path.Combine(root, "injury_risk.json")));
            var mech = InjuryDatabase.LoadMechanisms();
            var db = InjuryDatabase.Load();
            var mechDef = mech.mechanisms.FirstOrDefault(x => x.id == _p.MechanismId);
            var setup = new CrashSetup
            {
                MechanismId = _p.MechanismId, Male = _p.Male, Age = 35, Weight_kg = _p.Mass_kg, Height_cm = _p.Height_m * 100,
                Unrestrained = !_p.Belted, Speed_kmh = (int)_p.Speed_kmh, FallHeight_m = (int)_p.Speed_kmh,
                Rescue_min = mechDef != null ? (int)(Mathf.Round((float)(mechDef.rescueMin + mechDef.rescueMax) / 10f) * 5) : 30
            };
            _result = InjuryRisk.BuildScenario(risk, mech, db.All, measurements, setup, (uint)_p.Seed);
            Colorize();

            var sb = new StringBuilder();
            sb.AppendLine("<b>Messwerte und Verletzungsrisiko</b>");
            foreach (var r in _result.Risks)
            {
                string limit = r.Metric.limit > 0 ? $" <size=80%>(Grenzwert {r.Metric.limit:0.#}{Unit(r.Metric)}, {r.Metric.limitSource})</size>" : "";
                sb.AppendLine($"{r.Metric.label}: <b>{r.Value:0.#}{Unit(r.Metric)}</b> → {r.Metric.ais} {r.Probability:P0}{limit}");
            }
            var lat = measurements.FirstOrDefault(x => x.metric == "Abdomen.lateral");
            if (lat != null && Mathf.Abs((float)lat.value) > 0.2f)
                sb.AppendLine($"Rumpfanprall überwiegend von {(lat.value < 0 ? "links" : "rechts")}");
            sb.AppendLine();
            sb.AppendLine("<b>Wahrscheinlichkeit je Verletzung</b>");
            foreach (var kv in _result.InjuryProbability.OrderByDescending(x => x.Value).Where(x => x.Value >= 0.01))
                sb.AppendLine($"{db.Get(kv.Key).name}: {kv.Value:P0}");
            sb.AppendLine();
            sb.AppendLine("<size=80%>Welche Verletzungen gewürfelt wurden, zeigt erst der Schockraum. Werte aus einem starren Ragdoll — qualitativ, nicht mit Crashtest-Dummys vergleichbar.</size>");
            resultText.text = sb.ToString();
            if (toTraumaRoomButton) toTraumaRoomButton.SetActive(true);
        }

        static string Unit(RiskMetric m) => string.IsNullOrEmpty(m.unit) ? "" : " " + m.unit;

        /// <summary>Körperteile nach höchstem Risiko ihrer Region einfärben (grün → gelb → rot).</summary>
        void Colorize()
        {
            var byRegion = _result.Risks.GroupBy(r => r.Metric.region).ToDictionary(g => g.Key, g => g.Max(r => r.Probability));
            foreach (var s in rig.Ragdoll.Sensors)
            {
                if (!byRegion.TryGetValue(s.Region, out double p)) continue;
                var c = p < 0.5 ? Color.Lerp(new Color(0.3f, 0.75f, 0.4f), new Color(0.95f, 0.8f, 0.2f), (float)p * 2)
                                : Color.Lerp(new Color(0.95f, 0.8f, 0.2f), new Color(0.85f, 0.15f, 0.15f), (float)(p - 0.5) * 2);
                s.GetComponent<Renderer>().material.color = c;
            }
        }

        void LateUpdate()
        {
            if (cam == null || rig == null || rig.CameraTarget == null) return;
            Vector3 target = rig.CameraTarget.position;
            Vector3 offset = _p.MechanismId == "pkw_seite" ? new Vector3(0.4f, 1.6f, 3.2f) : new Vector3(3.4f, 1.3f, 0.6f);
            cam.transform.position = Vector3.Lerp(cam.transform.position, target + offset, 1 - Mathf.Exp(-6 * Time.unscaledDeltaTime));
            cam.transform.LookAt(target + Vector3.up * 0.2f);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NotfallSIM.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Sandbox-Teil für frei gestellte Unfälle: Vorlage wählen (traffic_scenarios.json), je Fahrzeug Geschwindigkeit, Winkel,
    /// Treffpunkt, Typ, Insassen, Bremsen und Ausweichen einstellen, Fahrbahn wählen; nach dem Crash Patient wählen.
    /// Die Knöpfe entstehen zur Laufzeit in einem eigenen Feld neben der Steuerung (auch in älteren Szenen).
    /// </summary>
    public partial class CrashSandbox
    {
        bool Scenario => _p.MechanismId == "szenario";
        TrafficScenario _sc;
        int _scIndex, _vehSel;
        GameObject _scPanel;
        readonly Dictionary<string, TMP_Text> _scLabels = new Dictionary<string, TMP_Text>();

        static readonly double[] HazardLeads = { 1.0, 1.5, 2.0, 2.5, 3.0 };
        static readonly (string id, string label)[] Reactions = { ("erwartet", "aufmerksam (0,75 s)"), ("unerwartet", "normal (1,25 s)"), ("ueberrascht", "überrascht (1,5 s)") };
        static readonly (int dir, string mode, string label)[] Steers =
        {
            (0, "", "nein"), (-1, "abwenden", "links"), (1, "abwenden", "rechts"), (-1, "spurwechsel", "Spurwechsel links"), (1, "spurwechsel", "Spurwechsel rechts")
        };
        static readonly (string label, double mu)[] Roads = { ("trocken", 0.8), ("nass", 0.5), ("Schnee", 0.2) };
        static readonly string[] Faces = { "front", "left", "right", "rear" };
        static readonly string[][] SeatSets =
        {
            new[] { "fahrer" }, new[] { "fahrer", "beifahrer" }, new[] { "fahrer", "hinten_links" },
            new[] { "fahrer", "beifahrer", "hinten_links", "hinten_rechts" }
        };

        /// <summary>Mechanismus-Knopf „Szenario“ unter „Stumpfe/spitze Gewalt“ (zur Laufzeit, auch in älteren Szenen).</summary>
        void MakeScenarioButton()
        {
            var texts = FindObjectsByType<TMP_Text>();
            if (texts.Any(t => t.text == "Szenario (mehrere Fahrzeuge)")) return;
            var after = texts.FirstOrDefault(t => t.text == "Stumpfe/spitze Gewalt") ?? texts.FirstOrDefault(t => t.text == "Sturz aus der Höhe");
            var src = after != null ? after.GetComponentInParent<Button>() : null;
            if (src == null) return;
            var go = CloneButton(src, src.transform.parent, "Szenario (mehrere Fahrzeuge)", () => SelectMechanism("szenario"));
            go.transform.SetSiblingIndex(src.transform.GetSiblingIndex() + 1);
        }

        static GameObject CloneButton(Button src, Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var go = Instantiate(src.gameObject, parent);
            go.name = text;
            var b = go.GetComponent<Button>();
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                b.onClick.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.Off);
            b.onClick.AddListener(action);
            var label = go.GetComponentInChildren<TMP_Text>();
            if (label != null) { label.text = text; label.fontSize = 15; }
            return go;
        }

        /// <summary>Eigenes Feld rechts neben der Steuerung mit den Szenario-Knöpfen.</summary>
        void BuildScenarioPanel()
        {
            var srcText = FindObjectsByType<TMP_Text>().FirstOrDefault(t => t.text == "Crash starten");
            var src = srcText != null ? srcText.GetComponentInParent<Button>() : null;
            var canvas = src != null ? src.GetComponentInParent<Canvas>() : null;
            if (src == null || canvas == null) return;
            _scPanel = new GameObject("Szenario", typeof(RectTransform));
            _scPanel.transform.SetParent(canvas.transform, false);
            var rt = _scPanel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(410, -20);
            rt.sizeDelta = new Vector2(380, 0);
            var img = _scPanel.AddComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.09f, 0.9f);
            var v = _scPanel.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(10, 10, 10, 10); v.spacing = 6;
            v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            _scPanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            Row(src, B("◀ Vorlage", () => CyclePreset(-1)), B("Vorlage ▶", () => CyclePreset(1)));
            Row(src, B("vehicle:Fahrzeug wählen", () => { _vehSel = (_vehSel + 1) % Math.Max(1, _sc.vehicles.Length); RefreshScenario(false); }),
                     B("type:Typ", () => CycleType()));
            Row(src, B("Winkel −15°", () => Turn(-15)), B("Winkel +15°", () => Turn(15)));
            Row(src, B("face:Treffpunkt", () => CycleFace()), B("Versatz −0,5 m", () => Offset(-0.5)), B("Versatz +0,5 m", () => Offset(0.5)));
            Row(src, B("hazard:Gefahr", () => CycleHazard()), B("reaction:Reaktion", () => CycleReaction()));
            Row(src, B("brake:Bremsen", () => CycleBrake()), B("steer:Ausweichen", () => CycleSteer()));
            Row(src, B("seats:Insassen", () => CycleSeats()), B("road:Fahrbahn", () => CycleRoad()));
            Row(src, B("patient:Patient", () => CyclePatient()), B("rescue:Rettung", () => CycleRescue()));
            _scPanel.SetActive(false);
        }

        class ScButton { public string key; public UnityEngine.Events.UnityAction action; }
        static ScButton B(string key, UnityEngine.Events.UnityAction action) => new ScButton { key = key, action = action };

        void Row(Button src, params ScButton[] items)
        {
            var row = new GameObject("Reihe", typeof(RectTransform));
            row.transform.SetParent(_scPanel.transform, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 6; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 42;
            foreach (var it in items)
            {
                string key = it.key.Contains(":") ? it.key.Substring(0, it.key.IndexOf(':')) : null;
                string text = key != null ? it.key.Substring(it.key.IndexOf(':') + 1) : it.key;
                var go = CloneButton(src, row.transform, text, it.action);
                var le = go.GetComponent<LayoutElement>();
                if (le != null) le.preferredHeight = 42;
                if (key != null) _scLabels[key] = go.GetComponentInChildren<TMP_Text>();
            }
        }

        void ShowScenarioPanel(bool on) { if (_scPanel != null) _scPanel.SetActive(on); }

        void EnsureScenario()
        {
            if (_sc != null) return;
            var file = CrashRig.LoadTrafficScenarios();
            if (file == null || file.scenarios == null || file.scenarios.Length == 0) { resultText.text = "traffic_scenarios.json fehlt."; return; }
            LoadPreset(0);
        }

        void LoadPreset(int i)
        {
            var file = CrashRig.LoadTrafficScenarios();
            if (file == null || file.scenarios.Length == 0) return;
            _scIndex = (i % file.scenarios.Length + file.scenarios.Length) % file.scenarios.Length;
            // Arbeitskopie: Änderungen betreffen nicht die Vorlage
            _sc = JsonUtility.FromJson<TrafficScenario>(JsonUtility.ToJson(file.scenarios[_scIndex], false));
            _vehSel = 0;
            _p.Patient = 0;
        }

        TrafficVehicle Sel => _sc != null && _sc.vehicles.Length > 0 ? _sc.vehicles[Mathf.Clamp(_vehSel, 0, _sc.vehicles.Length - 1)] : null;

        void CyclePreset(int dir) { if (Busy || !Scenario) return; LoadPreset(_scIndex + dir); RefreshScenario(true); }

        void RefreshScenario(bool rebuild)
        {
            _result = null;
            _rescueModes.Clear();
            if (toTraumaRoomButton) toTraumaRoomButton.SetActive(false);
            if (_replayButton) _replayButton.SetActive(false);
            UpdateScenarioLabels();
            if (rebuild) Preview(); else ShowSettings();
        }

        void Edit(Action<TrafficVehicle> change)
        {
            if (Busy || !Scenario || Sel == null) return;
            change(Sel);
            RefreshScenario(true);
        }

        void ScenarioSpeed(float dir) => Edit(v => v.speed_kmh = Mathf.Clamp((float)v.speed_kmh + Mathf.Sign(dir) * 10, 0, 150));
        void Turn(double deg) => Edit(v => v.heading_deg = ((v.heading_deg + deg) % 360 + 360) % 360);
        void Offset(double m) => Edit(v => v.targetOffset_m = Math.Round(v.targetOffset_m + m, 2));
        void CycleFace() => Edit(v =>
        {
            if (string.IsNullOrEmpty(v.target)) return;
            int i = Array.IndexOf(Faces, v.targetFace);
            v.targetFace = Faces[(i + 1) % Faces.Length];
        });
        void CycleType()
        {
            var types = CrashRig.LoadVehicleModel()?.vehicleTypes;
            if (types == null || types.Length == 0) return;
            Edit(v =>
            {
                int i = Array.FindIndex(types, t => t.id == v.type);
                bool wasBike = v.type == "motorrad";
                v.type = types[(i + 1) % types.Length].id;
                // Motorrad: nur Fahrer (Sozius über „Insassen“), ohne Gurt; zurück zum PKW wieder angegurtet
                if (v.type == "motorrad") v.occupants = new[] { new TrafficOccupant { seat = "fahrer", belted = false } };
                else if (wasBike) v.occupants = new[] { new TrafficOccupant { seat = "fahrer", belted = true } };
                _p.Patient = 0;
            });
        }
        /// <summary>Manöver auf „Gefahr erkennbar + Reaktionszeit“ umstellen (Vorgaben: 2 s vorher, normale Reaktion).</summary>
        static TrafficManeuver Man(TrafficVehicle v)
        {
            if (v.maneuver == null) v.maneuver = new TrafficManeuver();
            var m = v.maneuver;
            if (m.hazardLead_s <= 0) { m.hazardLead_s = 2.0; if (string.IsNullOrEmpty(m.reaction)) m.reaction = "unerwartet"; }
            return m;
        }
        void CycleBrake() => Edit(v => { var m = Man(v); m.brake = !m.brake; });
        void CycleSteer() => Edit(v =>
        {
            var m = Man(v);
            int i = Array.FindIndex(Steers, x => x.dir == m.steer && (x.dir == 0 || x.mode == (m.steerMode == "spurwechsel" ? "spurwechsel" : "abwenden")));
            var next = Steers[(i + 1) % Steers.Length];
            m.steer = next.dir; m.steerMode = next.mode;
        });
        void CycleHazard() => Edit(v =>
        {
            var m = Man(v);
            int i = Array.FindIndex(HazardLeads, x => Math.Abs(x - m.hazardLead_s) < 0.01);
            m.hazardLead_s = HazardLeads[(i + 1) % HazardLeads.Length];
        });
        void CycleReaction() => Edit(v =>
        {
            var m = Man(v);
            int i = Array.FindIndex(Reactions, x => x.id == (m.reaction ?? "unerwartet"));
            m.reaction = Reactions[(i + 1) % Reactions.Length].id;
            m.reaction_s = 0;
        });

        /// <summary>Kurzbeschreibung des Manövers, z. B. „erkennt die Gefahr 2,0 s vorher, reagiert nach 1,25 s: bremst, Spurwechsel links (0,75 s vor dem Aufprall)“.</summary>
        static string ManeuverText(TrafficManeuver m)
        {
            if (m == null || (!m.brake && m.steer == 0)) return "";
            string what = string.Join(" + ", new[] { m.brake ? "bremst" : null, m.steer != 0 ? (m.steerMode == "spurwechsel" ? "Spurwechsel " : "weicht ") + (m.steer < 0 ? "links" : "rechts") + (m.steerMode == "spurwechsel" ? "" : " aus") : null }.Where(x => x != null));
            if (m.hazardLead_s > 0)
            {
                double start = m.hazardLead_s - m.ReactionTime();
                return $", sieht die Gefahr {m.hazardLead_s:0.0} s vorher, reagiert nach {m.ReactionTime():0.00} s: {what}" +
                       (start > 0 ? $" ({start:0.00} s vor dem Aufprall)" : " (zu spät — erst nach dem geplanten Aufprall)");
            }
            return $", {what} ({Math.Max(m.brake ? m.brakeLead_s : 0, m.steer != 0 ? m.steerLead_s : 0):0.0} s vorher)";
        }

        void CycleSeats() => Edit(v =>
        {
            var cur = (v.occupants ?? new TrafficOccupant[0]).Select(o => o.seat).ToArray();
            if (v.type == "motorrad")
            {
                // Motorrad: Fahrer allein oder mit Sozius, nie angegurtet
                v.occupants = (cur.Length == 1 ? new[] { "fahrer", "sozius" } : new[] { "fahrer" }).Select(s => new TrafficOccupant { seat = s, belted = false }).ToArray();
                _p.Patient = 0;
                return;
            }
            int i = Array.FindIndex(SeatSets, s => s.SequenceEqual(cur));
            bool belted = v.occupants == null || v.occupants.Length == 0 || v.occupants[0].belted;
            v.occupants = SeatSets[(i + 1) % SeatSets.Length].Select(s => new TrafficOccupant { seat = s, belted = belted }).ToArray();
            _p.Patient = 0;
        });
        void ScenarioBelt() => Edit(v =>
        {
            if (v.type == "motorrad") return;   // kein Gurt auf dem Motorrad
            bool on = !(v.occupants != null && v.occupants.Length > 0 && v.occupants[0].belted);
            foreach (var o in v.occupants ?? new TrafficOccupant[0]) o.belted = on;
        });
        void CycleRoad()
        {
            if (Busy || !Scenario || _sc == null) return;
            int i = Array.FindIndex(Roads, r => Math.Abs(r.mu - _sc.friction) < 0.01);
            _sc.friction = Roads[(i + 1) % Roads.Length].mu;
            RefreshScenario(true);
        }

        /// <summary>Patient wählen: vor dem Crash für die Kamera, danach zeigt die Ergebnisanzeige diese Person.</summary>
        void CyclePatient()
        {
            if (Busy || !Scenario || rig.Humans.Count == 0) return;
            _p.Patient = (_p.Patient + 1) % rig.Humans.Count;
            rig.SelectHuman(_p.Patient);
            UpdateScenarioLabels();
            if (rig.Humans[_p.Patient].Measurements != null && rig.HasRecording) ShowScenarioResult();
            else ShowSettings();
        }

        void UpdateScenarioLabels()
        {
            var v = Sel;
            if (v == null) return;
            Action<string, string> set = (k, t) => { TMP_Text l; if (_scLabels.TryGetValue(k, out l) && l != null) l.text = t; };
            set("vehicle", $"Fahrzeug: {v.label ?? v.id}");
            var type = CrashRig.LoadVehicleModel()?.vehicleTypes?.FirstOrDefault(t => t.id == v.type);
            set("type", $"Typ: {type?.label ?? v.type}");
            set("face", string.IsNullOrEmpty(v.target) ? "Treffpunkt: –" : $"Trifft {v.target.ToUpper()}: {FaceLabel(v.targetFace)}");
            var m = v.maneuver;
            set("brake", m != null && m.brake ? "Bremsen: ja" : "Bremsen: nein");
            var st = m == null || m.steer == 0 ? "nein" : Steers.First(x => x.dir == m.steer && x.mode == (m.steerMode == "spurwechsel" ? "spurwechsel" : "abwenden")).label;
            set("steer", $"Ausweichen: {st}");
            set("hazard", m != null && m.hazardLead_s > 0 ? $"Gefahr {m.hazardLead_s:0.0} s vorher" : "Gefahr: feste Zeiten");
            var re = Reactions.FirstOrDefault(x => x.id == (m?.reaction ?? "unerwartet"));
            set("reaction", m != null && m.reaction_s > 0 ? $"Reaktion {m.reaction_s:0.00} s" : $"Reaktion: {re.label ?? "normal (1,25 s)"}");
            set("seats", $"Insassen: {(v.occupants ?? new TrafficOccupant[0]).Length}");
            var road = Roads.FirstOrDefault(r => Math.Abs(r.mu - _sc.friction) < 0.01);
            set("road", $"Fahrbahn: {road.label ?? $"µ {_sc.friction:0.0}"}");
            string rm; _rescueModes.TryGetValue(_p.Patient, out rm);
            set("rescue", $"Rettung: {RescueModes.First(x => x.id == (rm ?? "auto")).label}");
            set("patient", rig.Humans.Count > 0 ? $"Patient: {rig.Humans[Mathf.Clamp(_p.Patient, 0, rig.Humans.Count - 1)].Label}" : "Patient: –");
        }

        static string FaceLabel(string f) => f == "left" ? "links" : f == "right" ? "rechts" : f == "rear" ? "Heck" : "Front";

        string ScenarioSettingsText()
        {
            if (_sc == null) return "<b>Szenario</b>\ntraffic_scenarios.json fehlt.";
            UpdateScenarioLabels();
            var sb = new StringBuilder();
            sb.AppendLine($"<b>{_sc.label}</b>");
            sb.AppendLine($"<size=80%>{_sc.description}</size>");
            for (int i = 0; i < _sc.vehicles.Length; i++)
            {
                var v = _sc.vehicles[i];
                var m = v.maneuver;
                int belted = (v.occupants ?? new TrafficOccupant[0]).Count(o => o.belted), n = (v.occupants ?? new TrafficOccupant[0]).Length;
                string man = ManeuverText(m);
                string aim = string.IsNullOrEmpty(v.target) ? "" : $" → {v.target.ToUpper()} {FaceLabel(v.targetFace)}{(Math.Abs(v.targetOffset_m) > 0.01 ? $" {v.targetOffset_m:+0.0;-0.0} m" : "")}";
                sb.AppendLine($"{(i == _vehSel ? "▶ " : "")}<b>{v.id.ToUpper()}</b> {v.speed_kmh:0} km/h, {v.heading_deg:0}°{aim}, {n} Insass{(n == 1 ? "e" : "en")} ({belted} angegurtet){man}");
            }
            foreach (var w in _sc.pedestrians ?? new TrafficPedestrian[0]) sb.AppendLine($"{w.label}: {w.speed_kmh:0} km/h, {w.heading_deg:0}°");
            var pr = rig.TrafficPrediction;
            if (pr != null) sb.AppendLine(pr.FirstContact >= 0 ? $"<size=80%>Erster Kontakt nach {pr.FirstContact:0.00} s (Linien: vorausberechnete Bahnen)</size>" : "<color=#9f9>Kein Zusammenstoß vorausberechnet.</color>");
            return sb.ToString();
        }

        /// <summary>Ergebnis des Szenarios: Unfallhergang, alle Personen kurz, die gewählte Person ausführlich.</summary>
        void ShowScenarioResult()
        {
            if (rig.Humans.Count == 0) { resultText.text = rig.TrafficNote ?? "Keine Personen im Szenario."; return; }
            var cal = LoadCalibration();
            bool calibrated = cal != null && !string.IsNullOrEmpty(cal.calibratedAt) && cal.factors != null && cal.factors.Length > 0;
            string root = InjuryDatabase.Root;
            var risk = JsonUtility.FromJson<InjuryRiskDatabaseFile>(File.ReadAllText(Path.Combine(root, "injury_risk.json")));
            var mech = InjuryDatabase.LoadMechanisms();
            var db = InjuryDatabase.Load();

            var sb = new StringBuilder();
            sb.AppendLine("<b>Unfallhergang</b>");
            sb.Append("<size=85%>").Append(rig.TrafficReport()).AppendLine("</size>");
            sb.AppendLine("<b>Personen</b> <size=80%>(Vorsichtung nach mSTaRT beim Eintreffen; wahrscheinlichste Verletzungen)</size>");
            CrashResult chosen = null;
            string triPath = Path.Combine(root, "triage.json");
            var tri = File.Exists(triPath) ? JsonUtility.FromJson<TriageFile>(File.ReadAllText(triPath)) : null;
            string resPath = Path.Combine(root, "rescue.json");
            var rescue = File.Exists(resPath) ? JsonUtility.FromJson<RescueFile>(File.ReadAllText(resPath)) : null;
            var defs = db.All.ToDictionary(d => d.id);
            string patientRescue = "";
            for (int i = 0; i < rig.Humans.Count; i++)
            {
                var h = rig.Humans[i];
                var meas = InjuryRisk.Calibrate(h.Measurements ?? new List<CrashMeasurement>(), calibrated ? cal : null);
                TriageCard card; RescueSituation sit; RescuePlan plan;
                var res = BuildFor(h, i, meas, risk, mech, db, defs, tri, rescue, out card, out sit, out plan);
                var top = res.InjuryProbability.OrderByDescending(x => x.Value).Where(x => x.Value >= 0.1).Take(3).Select(x => $"{db.Get(x.Key).name} {x.Value:P0}");
                string line = string.Join(", ", top);
                string cat = "";
                if (card != null)
                {
                    var c = tri.categories.FirstOrDefault(x => x.id == card.Category);
                    cat = c != null ? $"<color={c.color}><b>{c.label}</b></color> " : "";
                }
                string resc = plan != null ? $" <size=80%>[{sit.Text}; {plan.Label}, {plan.Total_min:0} min bis Schockraum]</size>" : "";
                sb.AppendLine($"{(i == _p.Patient ? "▶ " : "")}{cat}{h.Label}: <size=85%>{(line.Length > 0 ? line : "kaum verletzt")}</size>{resc}");
                if (i == _p.Patient)
                {
                    chosen = res;
                    if (plan != null)
                        patientRescue = $"Rettung: {sit.Text}. {plan.Label}: Befreiung {plan.Extrication_min:0} min, bis zum Schockraum {plan.Total_min:0} min" +
                                        (plan.Replaced ? " <size=80%>(gewählte Art hier nicht möglich)</size>" : "") + $"\n<size=80%>{plan.Note}</size>";
                }
            }
            rig.SelectHuman(_p.Patient);
            _result = chosen;
            var patient = rig.Humans[_p.Patient];
            Colorize();
            _crashMarks = GenerateMarks(patient.Belted);

            sb.AppendLine();
            sb.AppendLine($"<b>{patient.Label}</b>" + (calibrated ? $" <size=80%>(kalibriert {cal.calibratedAt})</size>" : " <size=80%>(unkalibriert)</size>"));
            foreach (var r in _result.Risks.Where(x => x.Probability >= 0.05).OrderByDescending(x => x.Probability).Take(6))
                sb.AppendLine($"{r.Metric.label}: <b>{r.Value:0.#}{Unit(r.Metric)}</b> → {r.Metric.ais} {r.Probability:P0}");
            var organLoad = rig.LastAbdomenLoad;
            if (organLoad != null && organLoad.PeakCompression > 0.01)
                foreach (var o in organLoad.Organs.Where(x => x.Exposure > 0 && x.Probability >= 0.05))
                    sb.AppendLine($"{o.Organ.label}: Dehnung {o.Strain:P0} von {o.FailureStrain:P0} → {o.Probability:P0}");
            if (_crashMarks.Count > 0) sb.AppendLine("Zeichen: " + string.Join("; ", _crashMarks.Take(4).Select(c => c.Finding)));
            if (patientRescue.Length > 0) sb.AppendLine(patientRescue);
            string glass = rig.GlassReport();
            if (glass.Length > 0) sb.AppendLine("<b>Scheiben</b>\n" + glass.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("<size=80%>„Patient“ wechselt die Person. Fahrzeugbewegung aus einem Draufsicht-Modell (kein Überschlag); Werte qualitativ.</size>");
            resultText.text = sb.ToString();
            if (toTraumaRoomButton) toTraumaRoomButton.SetActive(true);
            if (_replayButton) _replayButton.SetActive(true);
            UpdateScenarioLabels();
        }

        /// <summary>Rettungsart je Person (Index in rig.Humans); fehlt = automatisch.</summary>
        readonly Dictionary<int, string> _rescueModes = new Dictionary<int, string>();
        static readonly (string id, string label)[] RescueModes =
        {
            ("auto", "automatisch"), ("selbst", "Selbstbefreiung"), ("sofort", "Sofortrettung"), ("schnell", "schnelle Rettung"), ("schonend", "schonende Rettung")
        };

        void CycleRescue()
        {
            if (Busy || !Scenario || rig.Humans.Count == 0) return;
            string cur; _rescueModes.TryGetValue(_p.Patient, out cur);
            int i = Array.FindIndex(RescueModes, x => x.id == (cur ?? "auto"));
            _rescueModes[_p.Patient] = RescueModes[(i + 1) % RescueModes.Length].id;
            UpdateScenarioLabels();
            if (rig.Humans[_p.Patient].Measurements != null && rig.HasRecording) ShowScenarioResult();
        }

        /// <summary>
        /// Fall einer Person in zwei Durchgängen mit gleichem Seed (gleiche Verletzungen): erst mit der Zeit bis zum Eintreffen
        /// (Sichtung vor Ort), dann mit der Rettungszeit aus Einklemmung und Rettungsart (Blutverlust bis zum Schockraum).
        /// </summary>
        CrashResult BuildFor(ScenarioHuman h, int index, List<CrashMeasurement> meas, InjuryRiskDatabaseFile risk, MechanismDatabaseFile mech, InjuryDatabase db,
                             Dictionary<string, InjuryDefinition> defs, TriageFile tri, RescueFile rescue, out TriageCard card, out RescueSituation sit, out RescuePlan plan)
        {
            card = null; sit = null; plan = null;
            var mechDef = mech.mechanisms.FirstOrDefault(x => x.id == h.MechanismId);
            var tr = rig.Traffic;
            double speed = 0;
            if (tr != null)
            {
                if (h.Pedestrian) speed = tr.Walkers[h.Walker].ContactSpeed * 3.6;
                else speed = tr.Bodies[h.Vehicle].MaxDeltaV * 3.6;
            }
            var setup = new CrashSetup
            {
                MechanismId = h.MechanismId, Male = _p.Male, Age = 35, Weight_kg = _p.Mass_kg, Height_cm = _p.Height_m * 100,
                Unrestrained = !h.Pedestrian && !h.Rider && !h.Belted, Speed_kmh = (int)Math.Round(speed), FallHeight_m = 0,
                Rescue_min = mechDef != null ? (int)(Mathf.Round((float)(mechDef.rescueMin + mechDef.rescueMax) / 10f) * 5) : 30
            };
            string title = $"Unfall: {_sc?.label} — {h.Label}";
            string desc = $"{_sc?.description} Patient/in: {h.Label}" + (h.Pedestrian ? "." : h.Rider ? $", Helm getragen, Δv ca. {speed:0} km/h." : $", {(h.Belted ? "angegurtet" : "nicht angegurtet")}, Δv ca. {speed:0} km/h.");
            uint seed = (uint)(_p.Seed + 7919 * index);
            if (rescue != null) setup.Rescue_min = (int)Math.Round(rescue.arrival_min);
            var atScene = InjuryRisk.BuildScenario(risk, mech, db.All, meas, setup, seed, null, title, desc);
            if (tri != null) card = Triage.Assess(atScene.Scenario, defs, tri.rules, h.Label);
            if (rescue == null) return atScene;

            bool walks = card != null && card.Walks;
            if (h.Pedestrian) sit = Rescue.PedestrianSituation();
            else if (h.Rider) sit = Rescue.RiderSituation();
            else
            {
                double side, foot, door;
                rig.SeatDeformation(h, out side, out foot, out door);
                sit = Rescue.Situation(rescue, side, foot, door, walks);
            }
            string mode;
            if (!_rescueModes.TryGetValue(index, out mode) || mode == "auto") mode = Rescue.AutoMethod(sit, walks, card != null && card.Category == "T1");
            plan = Rescue.Plan(rescue, sit, mode, walks);
            setup.Rescue_min = (int)Math.Round(plan.Total_min);
            string rescueText = h.Pedestrian || h.Rider ? "" : $" {char.ToUpper(sit.Text[0]) + sit.Text.Substring(1)}; {plan.Label}.";
            return InjuryRisk.BuildScenario(risk, mech, db.All, meas, setup, seed, null, title, desc + rescueText + $" Ankunft im Schockraum nach ca. {plan.Total_min:0} min.");
        }
    }
}

using System.Linq;
using NotfallSIM.Data;
using NotfallSIM.Interaction;
using NotfallSIM.Simulation;
using NotfallSIM.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NotfallSIM.EditorTools
{
    /// <summary>
    /// Menü "NotfallSIM › Schockraum-Szene erstellen": baut die komplette Testszene
    /// (Kamera, Trage, Platzhalter-Patient mit untersuchbaren Regionen, Monitor,
    /// Maßnahmen-Buttons, Befund- und Debriefing-Panel) und verdrahtet alles.
    /// Die Szene ist bewusst schlicht — sie ist Arbeitsgrundlage, kein Endprodukt.
    /// </summary>
    public static class SceneBuilder
    {
        const string SceneFolder = "Assets/NotfallSIM/Scenes";
        const string ScenePath = SceneFolder + "/Schockraum.unity";
        const string MaterialFolder = "Assets/NotfallSIM/Materials";

        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.9f);
        static readonly Color ButtonColor = new Color(0.16f, 0.32f, 0.55f);
        static readonly Color ImagingColor = new Color(0.22f, 0.40f, 0.42f);
        static readonly Color SpeedColor = new Color(0.25f, 0.27f, 0.30f);
        static readonly Color DiagnosisColor = new Color(0.62f, 0.45f, 0.12f);
        static readonly Color EndColor = new Color(0.60f, 0.18f, 0.18f);
        static readonly Color Dim = new Color(0.75f, 0.78f, 0.82f);

        [MenuItem("NotfallSIM/Schockraum-Szene erstellen")]
        public static void Build()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("TextMeshPro fehlt",
                    "Bitte zuerst Window › TextMeshPro › Import TMP Essential Resources ausführen " +
                    "und den Menüpunkt danach erneut starten.", "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Szene existiert bereits", ScenePath + " überschreiben?", "Überschreiben", "Abbrechen"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var simGo = new GameObject("Simulation");
            var sim = simGo.AddComponent<SimulationManager>();
            var interventions = simGo.AddComponent<InterventionController>();
            interventions.Configure(sim);
            var recorder = simGo.AddComponent<SessionRecorder>();
            recorder.Configure(sim);

            BuildRoom(sim);
            BuildUi(sim, interventions, recorder);

            EnsureFolder(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = simGo;
            Debug.Log($"[NotfallSIM] Szene erstellt: {ScenePath} — Play drücken zum Starten.");
        }

        // ---------------------------------------------------------------- 3D

        static void BuildRoom(SimulationManager sim)
        {
            var cam = Camera.main;
            cam.transform.position = new Vector3(0f, 2.4f, -1.75f);
            cam.transform.LookAt(new Vector3(0f, 0.9f, 0.05f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            cam.gameObject.AddComponent<PhysicsRaycaster>(); // nötig für Klicks auf 3D-Objekte

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Boden";
            floor.GetComponent<Renderer>().sharedMaterial = Mat("Boden", new Color(0.55f, 0.58f, 0.60f));

            var stretcher = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stretcher.name = "Trage";
            stretcher.transform.position = new Vector3(0f, 0.8f, -0.05f);
            stretcher.transform.localScale = new Vector3(0.75f, 0.08f, 2.35f);
            stretcher.GetComponent<Renderer>().sharedMaterial = Mat("Trage", new Color(0.35f, 0.37f, 0.40f));

            var patient = new GameObject("Patient").transform;
            Material skin = Mat("Haut", new Color(0.87f, 0.70f, 0.58f));
            Material clothes = Mat("Kleidung", new Color(0.22f, 0.27f, 0.40f));

            // Rückenlage, Kopf Richtung +z. Patientenseite rechts = -x.
            // Regionsnamen müssen zu "region" in injuries.json passen.
            Region(sim, patient, "Kopf", PrimitiveType.Sphere, new Vector3(0f, 0.97f, 0.82f), Vector3.zero, new Vector3(0.21f, 0.21f, 0.24f), skin);
            Region(sim, patient, "Thorax", PrimitiveType.Cube, new Vector3(0f, 0.95f, 0.46f), Vector3.zero, new Vector3(0.44f, 0.20f, 0.42f), clothes);
            Region(sim, patient, "Abdomen", PrimitiveType.Cube, new Vector3(0f, 0.94f, 0.13f), Vector3.zero, new Vector3(0.38f, 0.17f, 0.26f), clothes);
            Region(sim, patient, "Becken", PrimitiveType.Cube, new Vector3(0f, 0.95f, -0.14f), Vector3.zero, new Vector3(0.40f, 0.19f, 0.28f), clothes);
            var limb = new Vector3(90f, 0f, 0f);
            Region(sim, patient, "Oberschenkel rechts", PrimitiveType.Capsule, new Vector3(-0.11f, 0.94f, -0.52f), limb, new Vector3(0.15f, 0.22f, 0.15f), clothes);
            Region(sim, patient, "Oberschenkel links", PrimitiveType.Capsule, new Vector3(0.11f, 0.94f, -0.52f), limb, new Vector3(0.15f, 0.22f, 0.15f), clothes);
            Region(sim, patient, "Unterschenkel rechts", PrimitiveType.Capsule, new Vector3(-0.11f, 0.92f, -0.95f), limb, new Vector3(0.12f, 0.21f, 0.12f), clothes);
            Region(sim, patient, "Unterschenkel links", PrimitiveType.Capsule, new Vector3(0.11f, 0.92f, -0.95f), limb, new Vector3(0.12f, 0.21f, 0.12f), clothes);
            Region(sim, patient, "Arm rechts", PrimitiveType.Capsule, new Vector3(-0.29f, 0.93f, 0.32f), limb, new Vector3(0.10f, 0.30f, 0.10f), skin);
            Region(sim, patient, "Arm links", PrimitiveType.Capsule, new Vector3(0.29f, 0.93f, 0.32f), limb, new Vector3(0.10f, 0.30f, 0.10f), skin);
        }

        static void Region(SimulationManager sim, Transform parent, string name, PrimitiveType type,
                           Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type); // bringt passenden Collider mit
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.eulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.AddComponent<BodyRegion>().Configure(sim, name);
        }

        // ---------------------------------------------------------------- UI

        static void BuildUi(SimulationManager sim, InterventionController iv, SessionRecorder recorder)
        {
            var canvasGo = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform root = canvasGo.transform;

            // Hinweis oben links
            var hint = Label("Hinweis", root, "<b>NotfallSIM</b> · Schockraum\nKörperregion anklicken, um sie zu untersuchen.", 24, Dim);
            Place(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(700, 80));

            // Monitor oben rechts
            var monitor = Panel("Monitor", root, PanelColor);
            Place(monitor.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20), new Vector2(400, 0));
            Vertical(monitor.gameObject, 4, fit: true);
            Label("Titel", monitor.transform, "PATIENTENMONITOR", 18, Dim);
            var hr = VitalRow(monitor.transform, "HF", new Color(0.3f, 1f, 0.4f));
            var bp = VitalRow(monitor.transform, "RR", new Color(1f, 0.45f, 0.45f));
            var spo2 = VitalRow(monitor.transform, "SpO2", new Color(0.4f, 0.85f, 1f));
            var rr = VitalRow(monitor.transform, "AF", new Color(1f, 0.9f, 0.4f));
            var co2 = VitalRow(monitor.transform, "etCO2", new Color(1f, 0.9f, 0.4f));
            var time = VitalRow(monitor.transform, "Zeit", Dim);
            var status = Label("Status", monitor.transform, "", 20, Dim);
            monitor.gameObject.AddComponent<VitalsMonitor>().Configure(sim, hr, bp, rr, spo2, co2, time, status);

            // Debriefing-Panel (früh anlegen, der Beenden-Button braucht es)
            var debriefComp = canvasGo.AddComponent<DebriefPanel>();
            var debrief = Panel("Debriefing", root, new Color(0.04f, 0.05f, 0.07f, 0.97f));
            Place(debrief.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(960, 760));
            var debriefText = Label("Text", debrief.transform, "", 24, Color.white, TextAlignmentOptions.TopLeft);
            Stretch(debriefText.rectTransform, 32, 32, 32, 100);
            debriefText.enableAutoSizing = true;
            debriefText.fontSizeMin = 12;
            debriefText.fontSizeMax = 24;
            var close = MakeButton(debrief.transform, "Schließen", SpeedColor, debriefComp.Hide);
            Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32, 28), new Vector2(200, 52));
            debriefComp.Configure(sim, recorder, debrief.gameObject, debriefText);

            // Maßnahmen unten links
            var actions = Panel("Massnahmen", root, PanelColor);
            Place(actions.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 20), new Vector2(340, 0));
            Vertical(actions.gameObject, 8, fit: true);
            Label("Titel", actions.transform, "MASSNAHMEN", 18, Dim);
            // Buttons kommen aus interventions.json bzw. imaging.json — dort eintragen und Szene neu erzeugen.
            var ivGrid = ButtonGrid(actions.transform, "Massnahmen-Raster");
            foreach (var def in InjuryDatabase.LoadInterventions().interventions)
            {
                var b = MakeButton(ivGrid, def.label, ButtonColor, null, 17);
                UnityEventTools.AddStringPersistentListener(b.onClick, iv.Apply, def.id);
            }
            Label("Bildgebung", actions.transform, "BILDGEBUNG", 18, Dim);
            var imgGrid = ButtonGrid(actions.transform, "Bildgebung-Raster");
            foreach (var m in InjuryDatabase.LoadImaging().modalities)
            {
                var b = MakeButton(imgGrid, m.label, ImagingColor, null, 17);
                UnityEventTools.AddStringPersistentListener(b.onClick, sim.RequestImaging, m.id);
            }
            Label("Zeit", actions.transform, "ZEITRAFFER", 18, Dim);
            var speedRow = new GameObject("Zeitraffer", typeof(RectTransform));
            speedRow.transform.SetParent(actions.transform, false);
            var h = speedRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            speedRow.AddComponent<LayoutElement>().preferredHeight = 52;
            foreach (float s in new[] { 1f, 5f, 20f })
                SpeedButton(speedRow.transform, sim, s);
            MakeButton(actions.transform, "Szenario beenden", EndColor, debriefComp.EndAndShow);

            // Verdachtsdiagnosen oben in der Mitte: ein Button je Verletzung der Datenbank (Diagnose-Modus).
            var diag = Panel("Verdachtsdiagnosen", root, PanelColor);
            Place(diag.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-120, -20), new Vector2(680, 0));
            Vertical(diag.gameObject, 6, fit: true);
            Label("Titel", diag.transform, "VERDACHTSDIAGNOSEN (antippen = setzen / verwerfen)", 18, Dim);
            var diagGrid = ButtonGrid(diag.transform, "Diagnose-Raster");
            var dg = diagGrid.GetComponent<GridLayoutGroup>();
            dg.cellSize = new Vector2(214, 44); dg.constraintCount = 3;
            foreach (var inj in InjuryDatabase.LoadInjuryFile().injuries.OrderBy(x => x.region).ThenBy(x => x.name))
            {
                var b = MakeButton(diagGrid, inj.name, SpeedColor, null, 15);
                UnityEventTools.AddStringPersistentListener(b.onClick, sim.ToggleSuspected, inj.id);
                b.gameObject.AddComponent<DiagnosisToggle>().Configure(sim, inj.id, b.GetComponent<Image>(), SpeedColor, DiagnosisColor);
            }

            // Befunde unten rechts
            var findings = Panel("Befunde", root, PanelColor);
            Place(findings.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-20, 20), new Vector2(560, 300));
            var findingsText = Label("Text", findings.transform, "Noch keine Untersuchung.", 22, Color.white, TextAlignmentOptions.TopLeft);
            Stretch(findingsText.rectTransform, 20, 20, 20, 20);
            canvasGo.AddComponent<FindingsPanel>().Configure(findingsText, sim);

            debrief.transform.SetAsLastSibling(); // liegt über allem
            debrief.gameObject.SetActive(false);

            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        static TextMeshProUGUI VitalRow(Transform parent, string name, Color color)
        {
            var row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            row.AddComponent<LayoutElement>().preferredHeight = 54;

            var label = Label("Label", row.transform, name, 24, color, TextAlignmentOptions.Left);
            label.gameObject.AddComponent<LayoutElement>().preferredWidth = 110;
            var value = Label("Wert", row.transform, "--", 42, color, TextAlignmentOptions.Right);
            value.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return value;
        }

        static void SpeedButton(Transform parent, SimulationManager sim, float scale)
        {
            var b = MakeButton(parent, $"{scale:0}x", SpeedColor, null);
            UnityEventTools.AddFloatPersistentListener(b.onClick, sim.SetTimeScale, scale);
        }

        // ---------------------------------------------------------------- Helfer

        /// <summary>Zweispaltiges Raster für viele Buttons; seine Höhe ergibt sich aus der Anzahl.</summary>
        static Transform ButtonGrid(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(150, 44);
            g.spacing = new Vector2(6, 6);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = 2;
            return go.transform;
        }

        static Button MakeButton(Transform parent, string label, Color color, UnityAction action, float fontSize = 22)
        {
            var img = Panel(label, parent, color);
            var btn = img.gameObject.AddComponent<Button>();
            var le = img.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 52; le.minHeight = 52;
            var text = Label("Text", img.transform, label, fontSize, Color.white, TextAlignmentOptions.Center);
            Stretch(text.rectTransform, 8, 8, 4, 4);
            if (action != null) UnityEventTools.AddPersistentListener(btn.onClick, action);
            return btn;
        }

        static Image Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static TextMeshProUGUI Label(string name, Transform parent, string content, float size, Color color,
                                     TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false; // Texte sollen keine Klicks schlucken
            return t;
        }

        static void Vertical(GameObject go, float spacing, bool fit)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 16, 16);
            v.spacing = spacing;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (fit) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static Material Mat(string name, Color color)
        {
            EnsureFolder(MaterialFolder);
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using NotfallSIM.Anatomy;

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

            var patient = BuildRoom(sim);
            BuildUi(sim, interventions, recorder, patient);

            EnsureFolder(SceneFolder);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets(); // geänderte Materialien (Farben, Textur, Leuchten) auf die Platte schreiben
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = simGo;
            Debug.Log($"[NotfallSIM] Szene erstellt: {ScenePath} — Play drücken zum Starten.");
        }

        // ---------------------------------------------------------------- 3D

        static AnatomyPatient BuildRoom(SimulationManager sim)
        {
            NotfallSIM.Rooms.TraumaRoom.BuildGeometry(); // gleicher Aufbau wie zur Laufzeit (TraumaRoom.Ensure)

            var cam = Camera.main;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            cam.fieldOfView = 50;
            cam.nearClipPlane = 0.05f;
            var raycaster = cam.gameObject.AddComponent<PhysicsRaycaster>(); // nötig für Klicks auf 3D-Objekte
            raycaster.eventMask = ~(1 << 2); // Anatomie-Kollider (Ignore Raycast) stören die Untersuchung nicht
            cam.gameObject.AddComponent<OrbitCamera>().Configure(new Vector3(0, 0.95f, 0.05f), 2.5f, 25f, 35f);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // Grundlicht: schwaches, schräges Licht für Schatten; Hauptlicht ist die OP-Leuchte
            var sunGo = GameObject.Find("Directional Light");
            if (sunGo != null)
            {
                var sun = sunGo.GetComponent<Light>();
                sun.intensity = 0.35f;
                sun.color = new Color(1f, 0.98f, 0.95f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(60, -30, 0);
            }
            PostProcessing();

            // Patient: anatomisches Modell, wird zur Laufzeit aus anatomy/body.bin aufgebaut
            var patientGo = new GameObject("Patient");
            var patient = patientGo.AddComponent<AnatomyPatient>();
            return patient;
        }

        /// <summary>Globales Volume: Tonemapping, leichtes Bloom (OP-Leuchte, Monitore), Vignette, Farbkorrektur.</summary>
        internal static void PostProcessing()
        {
            const string path = "Assets/NotfallSIM/Materials/Schockraum_PostProcessing.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                EnsureFolder(MaterialFolder);
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
                var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
                var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
                var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(0.22f); vignette.smoothness.Override(0.45f);
                var color = profile.Add<ColorAdjustments>(true); color.contrast.Override(8f); color.saturation.Override(-4f); color.postExposure.Override(0.15f);
                foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            var vol = new GameObject("Post-Processing").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
        }

        // ---------------------------------------------------------------- UI

        static void BuildUi(SimulationManager sim, InterventionController iv, SessionRecorder recorder, AnatomyPatient patient)
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
            // (links daneben, bei x 24–174, setzt PauseMenu zur Laufzeit den Knopf „Menü (Esc)“)
            var hint = Label("Hinweis", root, "<b>Schockraum</b>\nRegion anklicken = untersuchen\nRechte Maus: Kamera drehen", 18, Dim);
            Place(hint.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(190, -16), new Vector2(290, 84));

            // Monitor oben rechts
            var monitor = Panel("Monitor", root, PanelColor);
            Place(monitor.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20), new Vector2(400, 0));
            Vertical(monitor.gameObject, 4, fit: true);
            Label("Titel", monitor.transform, "PATIENTENMONITOR", 18, Dim);
            var hr = VitalRow(monitor.transform, "HF", new Color(0.3f, 1f, 0.4f));
            var ecgImg = Wave(monitor.transform, "EKG");
            var bp = VitalRow(monitor.transform, "RR", new Color(1f, 0.45f, 0.45f));
            var spo2 = VitalRow(monitor.transform, "SpO2", new Color(0.4f, 0.85f, 1f));
            var plethImg = Wave(monitor.transform, "Pleth");
            var rr = VitalRow(monitor.transform, "AF", new Color(1f, 0.9f, 0.4f));
            var co2 = VitalRow(monitor.transform, "etCO2", new Color(1f, 0.9f, 0.4f));
            var capnoImg = Wave(monitor.transform, "Kapnografie");
            var waves = monitor.gameObject.AddComponent<MonitorWaveforms>();
            waves.Configure(sim, ecgImg, plethImg, capnoImg);
            WorldMonitor(sim, waves);
            var time = VitalRow(monitor.transform, "Zeit", Dim);
            var status = Label("Status", monitor.transform, "", 20, Dim);
            monitor.gameObject.AddComponent<VitalsMonitor>().Configure(sim, hr, bp, rr, spo2, co2, time, status);

            // Röntgenblick oben rechts zwischen Verdachtsdiagnosen (bis x 1180) und Monitor (ab x 1500), Bezug 1920×1080
            var xr = Panel("Röntgenblick", root, PanelColor);
            Place(xr.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-430, -20), new Vector2(300, 0));
            Vertical(xr.gameObject, 6, fit: true);
            var xrToggle = MakeButton(xr.transform, "Röntgenblick", new Color(0.18f, 0.36f, 0.55f), patient.ToggleXRay, 18);
            var layers = ButtonGrid(xr.transform, "Ebenen");
            var lg = layers.GetComponent<GridLayoutGroup>(); lg.cellSize = new Vector2(131, 38); lg.constraintCount = 2;
            string[] layerIds = { "bone", "organ", "vessel", "nerve" }, layerNames = { "Knochen", "Organe", "Gefäße", "Nerven" };
            var layerImages = new Image[layerIds.Length];
            for (int i = 0; i < layerIds.Length; i++)
            {
                var b = MakeButton(layers, layerNames[i], SpeedColor, null, 15);
                UnityEventTools.AddStringPersistentListener(b.onClick, patient.ToggleLayer, layerIds[i]);
                layerImages[i] = b.GetComponent<Image>();
            }
            MakeButton(xr.transform, "Verletzungen (nach Ende)", SpeedColor, patient.ToggleInjuries, 15);
            var hover = Label("Struktur", xr.transform, "", 17, new Color(1f, 0.9f, 0.5f));
            hover.gameObject.AddComponent<LayoutElement>().preferredHeight = 44; // zwei Zeilen für lange Namen
            patient.Configure(sim, hover, xrToggle.GetComponentInChildren<TMP_Text>(), layerIds, layerImages);

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
                var b = MakeButton(ivGrid, def.label, ButtonColor, null, 15);
                UnityEventTools.AddStringPersistentListener(b.onClick, iv.Apply, def.id);
                b.gameObject.AddComponent<ActionButton>().Configure(sim, iv, def.id, def.label, ButtonColor);
            }
            Label("Bildgebung", actions.transform, "BILDGEBUNG", 18, Dim);
            var imgGrid = ButtonGrid(actions.transform, "Bildgebung-Raster");
            foreach (var m in InjuryDatabase.LoadImaging().modalities)
            {
                var b = MakeButton(imgGrid, m.label, ImagingColor, null, 15);
                UnityEventTools.AddStringPersistentListener(b.onClick, sim.RequestImaging, m.id);
                b.gameObject.AddComponent<ActionButton>().Configure(sim, null, m.id, m.label, ImagingColor);
            }
            Label("Zeit", actions.transform, "ZEITRAFFER", 18, Dim);
            var speedRow = new GameObject("Zeitraffer", typeof(RectTransform));
            speedRow.transform.SetParent(actions.transform, false);
            var h = speedRow.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            speedRow.AddComponent<LayoutElement>().preferredHeight = 52;
            foreach (float s in new[] { 1f, 5f, 20f })
                SpeedButton(speedRow.transform, sim, s);
            var endRow = new GameObject("Ende", typeof(RectTransform));
            endRow.transform.SetParent(actions.transform, false);
            var eh = endRow.AddComponent<HorizontalLayoutGroup>();
            eh.spacing = 8; eh.childControlWidth = true; eh.childControlHeight = true; eh.childForceExpandWidth = true;
            endRow.AddComponent<LayoutElement>().preferredHeight = 52;
            MakeButton(endRow.transform, "Beenden", EndColor, debriefComp.EndAndShow, 18);
            var menuBtn = MakeButton(endRow.transform, "Hauptmenü", SpeedColor, null, 18);
            UnityEventTools.AddStringPersistentListener(menuBtn.onClick, sim.LoadScene, SessionSetup.MainMenuScene);

            // Protokoll unten in der Mitte
            var logPanel = Panel("Protokoll", root, PanelColor);
            Place(logPanel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-60, 20), new Vector2(880, 250));
            var logText = Label("Text", logPanel.transform, "<b>PROTOKOLL</b>", 17, Color.white, TextAlignmentOptions.TopLeft);
            Stretch(logText.rectTransform, 16, 16, 12, 12);
            logPanel.gameObject.AddComponent<EventLogPanel>().Configure(sim, logText);

            // Verdachtsdiagnosen oben in der Mitte: ein Button je Verletzung der Datenbank (Diagnose-Modus).
            var diag = Panel("Verdachtsdiagnosen", root, PanelColor);
            Place(diag.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-120, -20), new Vector2(680, 0));
            Vertical(diag.gameObject, 6, fit: true);
            Label("Titel", diag.transform, "VERDACHTSDIAGNOSEN (antippen = setzen / verwerfen)", 18, Dim);
            var diagGrid = ButtonGrid(diag.transform, "Diagnose-Raster");
            var dg = diagGrid.GetComponent<GridLayoutGroup>();
            dg.cellSize = new Vector2(214, 44); dg.constraintCount = 3;
            // Verletzungen und Differenzialdiagnosen gemischt und gleich aussehend — sonst wäre es zu einfach.
            var candidates = InjuryDatabase.LoadInjuryFile().injuries.Select(x => new { x.id, x.name, x.region })
                .Concat(InjuryDatabase.LoadDifferentials().differentials.Select(x => new { x.id, x.name, x.region }));
            foreach (var c in candidates.OrderBy(x => x.region).ThenBy(x => x.name))
            {
                var b = MakeButton(diagGrid, c.name, SpeedColor, null, 15);
                UnityEventTools.AddStringPersistentListener(b.onClick, sim.ToggleSuspected, c.id);
                b.gameObject.AddComponent<DiagnosisToggle>().Configure(sim, c.id, b.GetComponent<Image>(), SpeedColor, DiagnosisColor);
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

        static RawImage Wave(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<RawImage>();
            img.color = Color.white;
            img.raycastTarget = false;
            go.AddComponent<LayoutElement>().preferredHeight = 52;
            return img;
        }

        static TextMeshProUGUI Num(Transform parent, string name, Vector2 pos, Color c, float size)
        {
            var t = Label(name, parent, "--", size, c, TextAlignmentOptions.Right);
            Place(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(150, 70));
            return t;
        }

        static RawImage Img(Transform parent, string name, float y)
        {
            var img = new GameObject(name, typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(parent, false);
            Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, y), new Vector2(380, 80));
            return img;
        }

        /// <summary>Der Monitor an der Deckenversorgungseinheit zeigt dieselben Werte und Kurven (World-Space-Canvas).</summary>
        static void WorldMonitor(SimulationManager sim, MonitorWaveforms waves)
        {
            var body = NotfallSIM.Rooms.TraumaRoom.MonitorBody;
            if (body == null) return;
            var go = new GameObject("Monitor-Anzeige", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(560, 380);
            rt.localScale = Vector3.one * (0.5f / 560f);
            rt.rotation = body.rotation;
            rt.position = body.position - body.forward * 0.042f; // knapp vor der Bildschirmfläche (−z-Seite)
            var bg = Panel("Hintergrund", go.transform, new Color(0.01f, 0.02f, 0.02f, 1f));
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            var e = Img(go.transform, "EKG", -20); var p = Img(go.transform, "Pleth", -140); var c2 = Img(go.transform, "Kapno", -260);
            var hr = Num(go.transform, "HF", new Vector2(400, -25), new Color(0.3f, 1f, 0.4f), 64);
            var sp = Num(go.transform, "SpO2", new Vector2(400, -145), new Color(0.4f, 0.85f, 1f), 56);
            var co = Num(go.transform, "etCO2", new Vector2(400, -265), new Color(1f, 0.9f, 0.4f), 44);
            var bp = Label("RR", go.transform, "--", 30, new Color(1f, 0.45f, 0.45f), TextAlignmentOptions.Left);
            Place(bp.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 8), new Vector2(380, 40));
            waves.SetMirror(e, p, c2);
            go.AddComponent<VitalsMonitor>().Configure(sim, hr, bp, null, sp, co, null, null);
        }

        static void SpeedButton(Transform parent, SimulationManager sim, float scale)
        {
            var b = MakeButton(parent, $"{scale:0}x", SpeedColor, null);
            UnityEventTools.AddFloatPersistentListener(b.onClick, sim.SetTimeScale, scale);
        }

        // ---------------------------------------------------------------- Helfer

        /// <summary>Zweispaltiges Raster für viele Buttons; seine Höhe ergibt sich aus der Anzahl.</summary>
        internal static Transform ButtonGrid(Transform parent, string name)
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

        internal static Button MakeButton(Transform parent, string label, Color color, UnityAction action, float fontSize = 22)
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

        internal static Image Panel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        internal static TextMeshProUGUI Label(string name, Transform parent, string content, float size, Color color,
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

        internal static void Vertical(GameObject go, float spacing, bool fit)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(16, 16, 16, 16);
            v.spacing = spacing;
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            if (fit) go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        internal static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        internal static void Stretch(RectTransform rt, float left, float right, float top, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        internal static Material Mat(string name, Color color)
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

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        internal static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}

using NotfallSIM.Crash;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static NotfallSIM.EditorTools.SceneBuilder;

namespace NotfallSIM.EditorTools
{
    /// <summary>
    /// Menü „NotfallSIM › Unfall-Sandbox erstellen“: legt Assets/NotfallSIM/Scenes/UnfallSandbox.unity an.
    /// Fahrzeuge und Ragdoll entstehen zur Laufzeit (CrashRig), die Szene enthält nur Kamera, Licht und Bedienung.
    /// </summary>
    public static class CrashSceneBuilder
    {
        const string ScenePath = "Assets/NotfallSIM/Scenes/UnfallSandbox.unity";
        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.9f);
        static readonly Color MechColor = new Color(0.16f, 0.32f, 0.55f);
        static readonly Color NeutralColor = new Color(0.25f, 0.27f, 0.30f);
        static readonly Color StartColor = new Color(0.12f, 0.45f, 0.38f);
        static readonly Color GoColor = new Color(0.60f, 0.18f, 0.18f);
        static readonly Color Dim = new Color(0.75f, 0.78f, 0.82f);

        [MenuItem("NotfallSIM/Unfall-Sandbox erstellen")]
        public static void Build()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
            {
                EditorUtility.DisplayDialog("TextMeshPro fehlt", "Bitte zuerst Window › TextMeshPro › Import TMP Essential Resources ausführen.", "OK");
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Szene existiert bereits", ScenePath + " überschreiben?", "Überschreiben", "Abbrechen"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main;
            cam.transform.position = new Vector3(3.5f, 1.6f, 0.5f);
            cam.transform.LookAt(new Vector3(0, 0.8f, 0.3f));
            cam.nearClipPlane = 0.05f;

            var rigGo = new GameObject("Unfall");
            var rig = rigGo.AddComponent<CrashRig>();
            var sandbox = rigGo.AddComponent<CrashSandbox>();

            // ---- UI
            var canvasGo = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            Transform root = canvasGo.transform;

            var left = Panel("Steuerung", root, PanelColor);
            Place(left.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(380, 0));
            Vertical(left.gameObject, 8, fit: true);
            Label("Titel", left.transform, "<b>UNFALL-SANDBOX</b>", 22, Dim);
            Label("Mechanismus", left.transform, "MECHANISMUS", 16, Dim);
            string[] ids = { "pkw_frontal", "pkw_seite", "motorrad", "fussgaenger", "sturz_hoehe" };
            string[] labels = { "PKW frontal", "PKW seitlich", "Motorrad", "Fußgänger", "Sturz aus der Höhe" };
            for (int i = 0; i < ids.Length; i++)
            {
                var b = MakeButton(left.transform, labels[i], MechColor, null, 18);
                UnityEventTools.AddStringPersistentListener(b.onClick, sandbox.SelectMechanism, ids[i]);
            }
            var settings = Label("Einstellungen", left.transform, "", 20, Color.white);
            settings.gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
            var row = Row(left.transform);
            var minus = MakeButton(row, "– langsamer", NeutralColor, null, 17);
            UnityEventTools.AddFloatPersistentListener(minus.onClick, sandbox.ChangeSpeed, -1);
            var plus = MakeButton(row, "+ schneller", NeutralColor, null, 17);
            UnityEventTools.AddFloatPersistentListener(plus.onClick, sandbox.ChangeSpeed, 1);
            var safety = Row(left.transform);
            MakeButton(safety, "Gurt an/aus", NeutralColor, sandbox.ToggleBelt, 17);
            MakeButton(safety, "Airbag an/aus", NeutralColor, sandbox.ToggleAirbag, 17);
            var slow = Row(left.transform);
            foreach (float f in new[] { 0.05f, 0.2f, 1f })
            {
                var b = MakeButton(slow, f >= 1 ? "Echtzeit" : $"×{f:0.##}", NeutralColor, null, 17);
                UnityEventTools.AddFloatPersistentListener(b.onClick, sandbox.SetSlowMotion, f);
            }
            MakeButton(left.transform, "Crash starten", StartColor, sandbox.StartCrash, 22);
            MakeButton(left.transform, "Kalibrieren (Referenztests)", NeutralColor, sandbox.Calibrate, 16);
            MakeButton(left.transform, "Hauptmenü", NeutralColor, sandbox.GoToMainMenu, 16);

            var right = Panel("Ergebnis", root, PanelColor);
            Place(right.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20), new Vector2(620, 0));
            Vertical(right.gameObject, 8, fit: true);
            var result = Label("Text", right.transform, "", 19, Color.white);
            result.gameObject.AddComponent<LayoutElement>().preferredHeight = 560;
            var go = MakeButton(right.transform, "Fall in den Schockraum übernehmen", GoColor, sandbox.GoToTraumaRoom, 20);

            sandbox.Configure(rig, settings, result, go.gameObject, cam);

            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
            EnsureFolder("Assets/NotfallSIM/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = rigGo;
            Debug.Log("[NotfallSIM] Unfall-Sandbox erstellt. Für „Fall in den Schockraum übernehmen“ muss die Schockraum-Szene ebenfalls erzeugt sein.");
        }

        static Transform Row(Transform parent)
        {
            var go = new GameObject("Reihe", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            go.AddComponent<LayoutElement>().preferredHeight = 52;
            return go.transform;
        }
    }
}

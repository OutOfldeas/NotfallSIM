using System.Linq;
using NotfallSIM.Simulation;
using NotfallSIM.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NotfallSIM.EditorTools
{
    /// <summary>Menü „NotfallSIM › Hauptmenü erstellen“ und „Alle Szenen erstellen“.</summary>
    public static class MenuSceneBuilder
    {
        const string ScenePath = "Assets/NotfallSIM/Scenes/" + SessionSetup.MainMenuScene + ".unity";

        [MenuItem("NotfallSIM/Alle Szenen erstellen", priority = 0)]
        public static void BuildAll()
        {
            SceneBuilder.Build();
            CrashSceneBuilder.Build();
            Build();
        }

        [MenuItem("NotfallSIM/Hauptmenü erstellen", priority = 1)]
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
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.13f);

            var canvasGo = new GameObject("UI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<MainMenu>().Configure((RectTransform)canvasGo.transform);

            var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
            SceneBuilder.EnsureFolder("Assets/NotfallSIM/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            // Hauptmenü als erste Szene im Build
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[NotfallSIM] Hauptmenü erstellt. Play startet im Menü; Schockraum und Unfall-Sandbox müssen ebenfalls erzeugt sein.");
        }
    }
}

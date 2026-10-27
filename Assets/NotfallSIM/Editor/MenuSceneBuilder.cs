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
            SyncBuildScenes();
        }

        /// <summary>
        /// Trägt alle Szenen aus Assets/NotfallSIM/Scenes in die gemeinsame Szenenliste ein (Hauptmenü zuerst).
        /// Ohne Eintrag kann SceneManager.LoadScene eine Szene nicht laden.
        /// </summary>
        [MenuItem("NotfallSIM/Szenen in Build-Liste eintragen", priority = 2)]
        public static void SyncBuildScenes()
        {
            var ours = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/NotfallSIM/Scenes" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p == ScenePath ? 0 : 1).ThenBy(p => p)
                .ToList();
            var others = EditorBuildSettings.scenes.Where(s => !ours.Contains(s.path));
            EditorBuildSettings.scenes = ours.Select(p => new EditorBuildSettingsScene(p, true)).Concat(others).ToArray();
            Debug.Log("[NotfallSIM] Szenen in der Build-Liste: " + string.Join(", ", ours.Select(System.IO.Path.GetFileNameWithoutExtension)) +
                      ". Falls ein eigenes Build Profile aktiv ist: File › Build Profiles › Scene List prüfen.");
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

            SyncBuildScenes();
            Debug.Log("[NotfallSIM] Hauptmenü erstellt. Play startet im Menü; Schockraum und Unfall-Sandbox müssen ebenfalls erzeugt sein.");
        }
    }
}

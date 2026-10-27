using NotfallSIM.Simulation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// „Menü“-Knopf oben links und ESC: Pause mit Fortsetzen, Neustart und Rückkehr ins Hauptmenü.
    /// Baut sich zur Laufzeit selbst auf, damit es auch in älteren, nicht neu erzeugten Szenen vorhanden ist.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        SimulationManager _sim;
        GameObject _overlay;
        bool _open;

        public static void Ensure(SimulationManager sim)
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<PauseMenu>() != null) return;
#else
            if (FindObjectOfType<PauseMenu>() != null) return;
#endif
            var go = new GameObject("Pausenmenü", typeof(RectTransform));
            go.AddComponent<PauseMenu>()._sim = sim;
        }

        void Start()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // über Debriefing und allen anderen Panels
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)transform;

            var menuButton = RuntimeUi.Button(root, "Menü (Esc)", new Color(0.12f, 0.14f, 0.17f, 0.92f), Toggle, 44, 18);
            var mb = (RectTransform)menuButton.transform;
            mb.anchorMin = mb.anchorMax = new Vector2(0, 1);
            mb.pivot = new Vector2(0, 1);
            mb.anchoredPosition = new Vector2(470, -20);
            mb.sizeDelta = new Vector2(140, 44);

            _overlay = RuntimeUi.Panel(root, "Overlay", new Color(0, 0, 0, 0.6f)).gameObject;
            RuntimeUi.Place((RectTransform)_overlay.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var box = RuntimeUi.Panel(_overlay.transform, "Box", new Color(0.07f, 0.09f, 0.11f, 0.98f));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(460, 330);
            RuntimeUi.Column(box, 12, 28);
            RuntimeUi.Label(box, "<b>Pause</b>", 30, Color.white, 46);
            RuntimeUi.Button(box, "Fortsetzen", new Color(0.12f, 0.45f, 0.38f), Toggle, 52, 22);
            RuntimeUi.Button(box, "Szenario neu starten", new Color(0.16f, 0.32f, 0.55f), Restart, 52, 22);
            RuntimeUi.Button(box, "Zurück zum Hauptmenü", new Color(0.25f, 0.27f, 0.30f), ToMainMenu, 52, 22);
            _overlay.SetActive(false);
        }

        void Update()
        {
            bool esc;
#if ENABLE_INPUT_SYSTEM
            esc = UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            esc = Input.GetKeyDown(KeyCode.Escape);
#endif
            if (esc) Toggle();
        }

        void Toggle()
        {
            _open = !_open;
            _overlay.SetActive(_open);
            if (_sim != null) _sim.SetPaused(_open);
        }

        void Restart()
        {
            Time.timeScale = 1;
            // Gleicher Fall noch einmal — auch bei Zufalls- und Sandbox-Fällen (sonst entstünde ein neuer Fall)
            if (_sim != null && _sim.Scenario != null) Crash.CrashHandoff.Pending = _sim.Scenario;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void ToMainMenu()
        {
            Time.timeScale = 1;
            if (Application.CanStreamedLevelBeLoaded(SessionSetup.MainMenuScene)) SceneManager.LoadScene(SessionSetup.MainMenuScene);
            else Debug.LogWarning("[Menü] Hauptmenü-Szene fehlt in der Build-Liste: NotfallSIM › Alle Szenen erstellen.");
        }
    }
}

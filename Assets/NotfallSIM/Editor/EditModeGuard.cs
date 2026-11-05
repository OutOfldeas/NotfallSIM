using System;
using UnityEditor;
using UnityEngine;

namespace NotfallSIM.EditorTools
{
    /// <summary>
    /// Szenen lassen sich nur im Bearbeitungsmodus erstellen. Läuft gerade Play, wird es beendet und der Auftrag danach
    /// ausgeführt (statt „This cannot be used during play mode“).
    /// </summary>
    public static class EditModeGuard
    {
        static Action _pending;

        /// <summary>true: jetzt ausführen. false: Play wird beendet, <paramref name="action"/> läuft danach.</summary>
        public static bool Ready(Action action, string what)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            if (_pending == null) EditorApplication.playModeStateChanged += OnChange;
            _pending = action;
            Debug.Log($"[NotfallSIM] {what}: Play wird beendet, danach geht es automatisch weiter.");
            EditorApplication.isPlaying = false;
            return false;
        }

        static void OnChange(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnChange;
            var a = _pending; _pending = null;
            if (a != null) EditorApplication.delayCall += () => a();
        }
    }
}

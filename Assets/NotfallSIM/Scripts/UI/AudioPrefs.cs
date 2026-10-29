using UnityEngine;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Lautstärken (gespeichert in PlayerPrefs): Gesamt (AudioListener), Monitor-Ton (QRS-Piepen) und Alarme, dazu Stumm.
    /// </summary>
    public static class AudioPrefs
    {
        const string K = "NotfallSIM.Audio.";
        static bool _loaded;
        static float _master = 0.8f, _monitor = 0.6f, _alarm = 0.8f;
        static bool _muted;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _master = PlayerPrefs.GetFloat(K + "Master", _master);
            _monitor = PlayerPrefs.GetFloat(K + "Monitor", _monitor);
            _alarm = PlayerPrefs.GetFloat(K + "Alarm", _alarm);
            _muted = PlayerPrefs.GetInt(K + "Muted", 0) == 1;
            Apply();
        }

        public static float Master { get { Load(); return _master; } set { Load(); _master = Mathf.Clamp01(value); Save(); } }
        public static float Monitor { get { Load(); return _monitor; } set { Load(); _monitor = Mathf.Clamp01(value); Save(); } }
        public static float Alarm { get { Load(); return _alarm; } set { Load(); _alarm = Mathf.Clamp01(value); Save(); } }
        public static bool Muted { get { Load(); return _muted; } set { Load(); _muted = value; Save(); } }

        /// <summary>Lautstärke eines Kanals inklusive Stumm (für AudioSource.volume; Gesamt wirkt über den AudioListener).</summary>
        public static float MonitorVolume => Muted ? 0 : Monitor;
        public static float AlarmVolume => Muted ? 0 : Alarm;

        static void Save()
        {
            PlayerPrefs.SetFloat(K + "Master", _master);
            PlayerPrefs.SetFloat(K + "Monitor", _monitor);
            PlayerPrefs.SetFloat(K + "Alarm", _alarm);
            PlayerPrefs.SetInt(K + "Muted", _muted ? 1 : 0);
            PlayerPrefs.Save();
            Apply();
        }

        public static void Apply() { AudioListener.volume = _muted ? 0 : _master; }

        /// <summary>Beim Szenenstart anwenden.</summary>
        public static void Init() { Load(); Apply(); }
    }
}

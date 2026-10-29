using NotfallSIM.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Kurven wie am echten Monitor: EKG (Ableitung II), Pulsoxymetrie (Pleth) und Kapnografie, als Sweep von links
    /// nach rechts mit Lücke vor dem Cursor. Dazu der QRS-Ton, dessen Tonhöhe mit der Sättigung sinkt (wie bei
    /// klinischen Monitoren). Kurvenformen sind schematisch; Frequenz und Amplituden kommen aus der Physiologie.
    /// </summary>
    public class MonitorWaveforms : MonoBehaviour
    {
        [SerializeField] SimulationManager sim;
        [SerializeField] RawImage ecgImage, plethImage, capnoImage;
        [SerializeField] bool sound = true;
        [SerializeField] float sweep_px_per_s = 100;   // entspricht etwa 25 mm/s bei 4 px/mm

        const int W = 400, H = 70;
        static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        static readonly Color32 EcgColor = new Color32(77, 255, 102, 255), PlethColor = new Color32(102, 217, 255, 255), CapnoColor = new Color32(255, 230, 102, 255);

        Texture2D[] _tex;
        Color32[][] _px;
        float[] _lastY = new float[3];
        float _cursor, _beatPhase, _respPhase;
        AudioSource _audio;
        AudioClip _beep;

        [SerializeField] RawImage[] mirror = new RawImage[0]; // z. B. der Monitor im Raum

        public bool Sound { get => sound; set => sound = value; }
        public RawImage[] MirrorImages => mirror;

        /// <summary>Kurven in neue Bildflächen zeichnen (der Monitor baut sein Layout zur Laufzeit neu auf).</summary>
        public void Retarget(RawImage ecg, RawImage pleth, RawImage capno)
        {
            ecgImage = ecg; plethImage = pleth; capnoImage = capno;
            if (_tex == null) return;
            var images = new[] { ecg, pleth, capno };
            for (int i = 0; i < 3; i++) if (images[i]) images[i].texture = _tex[i];
        }

        public void SetMirror(RawImage ecg, RawImage pleth, RawImage capno)
        {
            mirror = new[] { ecg, pleth, capno };
            if (_tex == null) return; // sonst weist Start die Texturen zu
            for (int i = 0; i < 3; i++) if (mirror[i]) mirror[i].texture = _tex[i];
        }

        public void Configure(SimulationManager simulation, RawImage ecg, RawImage pleth, RawImage capno)
        {
            sim = simulation; ecgImage = ecg; plethImage = pleth; capnoImage = capno;
        }

        void Start()
        {
            var images = new[] { ecgImage, plethImage, capnoImage };
            _tex = new Texture2D[3];
            _px = new Color32[3][];
            for (int i = 0; i < 3; i++)
            {
                _tex[i] = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _px[i] = new Color32[W * H];
                for (int k = 0; k < _px[i].Length; k++) _px[i][k] = Clear;
                _tex[i].SetPixels32(_px[i]);
                _tex[i].Apply();
                if (images[i]) images[i].texture = _tex[i];
                if (mirror != null && i < mirror.Length && mirror[i]) mirror[i].texture = _tex[i];
                _lastY[i] = H * 0.3f;
            }
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.volume = 0.25f;
            _beep = Tone(880, 0.07f);
        }

        static AudioClip Tone(float freq, float seconds)
        {
            int rate = 44100, n = (int)(rate * seconds);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float env = Mathf.Min(1, i / 200f) * Mathf.Min(1, (n - i) / 400f);
                data[i] = Mathf.Sin(2 * Mathf.PI * freq * i / rate) * env;
            }
            var clip = AudioClip.Create("QRS", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Update()
        {
            if (sim == null || sim.Engine == null || _tex == null) return;
            var v = sim.Vitals;
            float dt = Time.deltaTime;
            if (dt <= 0 || sim.Paused) return; // pausiert: Kurven und Ton stehen
            bool arrest = v.CardiacArrest;
            float hr = arrest ? 0 : (float)v.HeartRate_bpm, rr = arrest ? 0 : (float)v.RespirationRate_bpm;
            float perfusion = arrest ? 0 : Mathf.Clamp01(((float)v.SystolicBP_mmHg - 40) / 80f);
            float co2 = (float)v.EtCO2_mmHg;

            // Physiologie läuft evtl. im Zeitraffer; die Kurven zeigen die aktuelle Frequenz in Echtzeit.
            int steps = Mathf.Max(1, Mathf.RoundToInt(dt * sweep_px_per_s));
            float stepDt = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                float prevBeat = _beatPhase;
                _beatPhase = (_beatPhase + stepDt * hr / 60f) % 1f;
                _respPhase = (_respPhase + stepDt * rr / 60f) % 1f;
                if (!arrest && _beatPhase < prevBeat && sound && _audio)
                {
                    _audio.pitch = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(((float)v.SpO2_percent - 75f) / 25f));
                    _audio.volume = 0.25f * AudioPrefs.MonitorVolume;
                    _audio.PlayOneShot(_beep);
                }

                float noise = (Mathf.PerlinNoise(Time.time * 7f, 0.3f) - 0.5f) * 0.03f;
                float ecg = arrest ? noise : Ecg(_beatPhase) + noise;
                float pleth = Pleth(_beatPhase) * perfusion;
                float capno = Capno(_respPhase) * Mathf.Clamp01(co2 / 50f);

                int x = (int)_cursor;
                Plot(0, x, H * (0.3f + 0.55f * ecg), EcgColor);
                Plot(1, x, H * (0.12f + 0.75f * pleth), PlethColor);
                Plot(2, x, H * (0.08f + 0.8f * capno), CapnoColor);
                _cursor = (_cursor + 1) % W;
            }
            for (int i = 0; i < 3; i++) { _tex[i].SetPixels32(_px[i]); _tex[i].Apply(false); }
        }

        void Plot(int ch, int x, float y, Color32 c)
        {
            var px = _px[ch];
            // Lücke vor dem Cursor löschen
            for (int g = 0; g < 10; g++)
            {
                int gx = (x + g) % W;
                for (int yy = 0; yy < H; yy++) px[yy * W + gx] = Clear;
            }
            float y0 = x == 0 ? y : _lastY[ch];
            int lo = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(y0, y)) - 1, 0, H - 1);
            int hi = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(y0, y)) + 1, 0, H - 1);
            for (int yy = lo; yy <= hi; yy++) px[yy * W + x] = c;
            _lastY[ch] = y;
        }

        static float G(float x, float mu, float sigma) => Mathf.Exp(-(x - mu) * (x - mu) / (2 * sigma * sigma));

        /// <summary>Schematischer Sinusrhythmus: P, QRS, T als Gauß-Wellen.</summary>
        static float Ecg(float p) =>
            0.12f * G(p, 0.10f, 0.025f) - 0.10f * G(p, 0.205f, 0.008f) + 1.0f * G(p, 0.225f, 0.010f)
            - 0.25f * G(p, 0.245f, 0.010f) + 0.28f * G(p, 0.45f, 0.040f);

        /// <summary>Pulskurve mit Inzisur, gegenüber dem QRS verzögert.</summary>
        static float Pleth(float p)
        {
            float x = (p - 0.28f + 1f) % 1f;
            return Mathf.Clamp01(0.95f * G(x, 0.14f, 0.07f) + 0.35f * G(x, 0.38f, 0.07f));
        }

        /// <summary>Kapnogramm: Anstieg, Plateau, Abfall bei Inspiration.</summary>
        static float Capno(float p)
        {
            if (p < 0.08f) return p / 0.08f;
            if (p < 0.50f) return 0.9f + 0.1f * (p - 0.08f) / 0.42f;
            if (p < 0.56f) return 1f - (p - 0.50f) / 0.06f;
            return 0;
        }
    }
}

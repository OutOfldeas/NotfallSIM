using NotfallSIM.Anatomy;
using NotfallSIM.Interaction;
using NotfallSIM.Simulation;
using NotfallSIM.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace NotfallSIM.Rooms
{
    /// <summary>
    /// Baut den Schockraum zur Laufzeit auf und ergänzt, was der geladenen Szene fehlt: Raum und Licht, Kamera,
    /// Bildnachbearbeitung, anatomischer Patient mit Röntgenblick, Monitor an der Deckenversorgung.
    ///
    /// Grund: Eine Szenendatei, die mit einer älteren Version erzeugt wurde (z. B. die im Repository liegende
    /// Schockraum.unity), enthielt noch Trage-Quader und Platzhalter-Patient. Wer „Alle Szenen erstellen“ nicht neu
    /// ausgeführt hat oder bei dem das fehlschlug, sah dann wieder den Dummy. Jetzt hängt das Aussehen nicht mehr
    /// von der Szenendatei ab.
    /// </summary>
    public static partial class TraumaRoom
    {
        public const string RootName = "Schockraum";

        public static void Ensure(SimulationManager sim)
        {
            RemoveLegacy();
            if (GameObject.Find(RootName) == null) BuildGeometry();
            SetupCamera();
            SetupLight();
            SetupPostProcessing();
            var patient = Object.FindAnyObjectByType<AnatomyPatient>();
            if (patient == null)
            {
                patient = new GameObject("Patient").AddComponent<AnatomyPatient>();
                patient.Configure(sim, null, null);
            }
            if (!patient.HasUi) XRayPanel(sim, patient);
            WorldMonitor(sim);
        }

        /// <summary>Innenraum zwischen den Wänden, Boden und Decke.</summary>
        public static Bounds InnerBounds => new Bounds(new Vector3(0, RoomH / 2, 0), new Vector3(RoomX * 2 - 0.1f, RoomH, RoomZ * 2 - 0.1f));

        public const string HintText = "<b>Schockraum</b>\nLinksklick: Region untersuchen\nRechte Maus ziehen: umsehen\n"
                                       + "Mausrad / +−: zoomen\nWASD / Pfeile: gehen, Q/E: tiefer/höher\nUmschalt: schneller · F: zurück zum Patienten";

        /// <summary>Steuerungshinweis oben links aktualisieren (auch in älteren Szenen).</summary>
        static void UpdateHint()
        {
            var go = GameObject.Find("Hinweis");
            var t = go != null ? go.GetComponent<TMP_Text>() : null;
            if (t == null) return;
            t.text = HintText;
            t.fontSize = 16;
            var r = t.rectTransform;
            r.sizeDelta = new Vector2(Mathf.Max(r.sizeDelta.x, 300), 132);
        }

        /// <summary>Platzhalter aus alten Szenen entfernen: Boden-Ebene, Trage-Quader, Patient aus Grundkörpern.</summary>
        static void RemoveLegacy()
        {
            foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == RootName) continue;
                bool legacyPatient = go.name == "Patient" && go.GetComponent<AnatomyPatient>() == null
                                     && go.GetComponentInChildren<BodyRegion>(true) != null;
                bool legacyProp = (go.name == "Boden" || go.name == "Trage") && go.GetComponent<MeshRenderer>() != null;
                if (legacyPatient || legacyProp)
                {
                    go.SetActive(false);
                    Object.Destroy(go);
                }
            }
        }

        static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.14f, 0.16f);
            cam.fieldOfView = 50;
            cam.nearClipPlane = 0.05f;
            var ray = cam.GetComponent<PhysicsRaycaster>() ?? cam.gameObject.AddComponent<PhysicsRaycaster>();
            ray.eventMask = ~(1 << 2); // Anatomie-Kollider (Ignore Raycast) stören die Untersuchung nicht
            var orbit = cam.GetComponent<OrbitCamera>();
            if (orbit == null)
            {
                orbit = cam.gameObject.AddComponent<OrbitCamera>();
                orbit.Configure(new Vector3(0, 0.95f, 0.05f), 2.5f, 25f, 35f);
            }
            orbit.SetRoom(InnerBounds);
            UpdateHint();
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
        }

        static void SetupLight()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type != LightType.Directional) continue;
                l.intensity = 0.35f;
                l.color = new Color(1f, 0.98f, 0.95f);
                l.shadows = LightShadows.Soft;
                l.transform.rotation = Quaternion.Euler(60, -30, 0);
            }
        }

        /// <summary>Tonemapping, leichtes Bloom (OP-Leuchte, Monitore), Vignette, Farbkorrektur.</summary>
        static void SetupPostProcessing()
        {
            if (Object.FindAnyObjectByType<Volume>() != null) return;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(0.22f); vignette.smoothness.Override(0.45f);
            var color = profile.Add<ColorAdjustments>(true); color.contrast.Override(8f); color.saturation.Override(-4f); color.postExposure.Override(0.15f);
            var vol = new GameObject("Post-Processing").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.profile = profile;
        }

        /// <summary>Röntgenblick-Feld für Szenen ohne eigenes (oben rechts, wie in der neu erzeugten Szene).</summary>
        static void XRayPanel(SimulationManager sim, AnatomyPatient patient)
        {
            var canvasGo = new GameObject("Röntgenblick-UI", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            var box = RuntimeUi.Panel(canvasGo.transform, "Röntgenblick", new Color(0.05f, 0.07f, 0.09f, 0.9f));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(1, 1);
            box.anchoredPosition = new Vector2(-430, -20);
            box.sizeDelta = new Vector2(300, 0);
            RuntimeUi.Column(box, 6, 12);
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var toggle = RuntimeUi.Button(box, "Röntgenblick", new Color(0.18f, 0.36f, 0.55f), patient.ToggleXRay, 46, 18);
            string[] ids = { "bone", "organ", "vessel", "nerve" }, names = { "Knochen", "Organe", "Gefäße", "Nerven" };
            var images = new Image[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var b = RuntimeUi.Button(box, names[i], new Color(0.25f, 0.27f, 0.30f), () => patient.ToggleLayer(id), 34, 15);
                images[i] = b.GetComponent<Image>();
            }
            RuntimeUi.Button(box, "Verletzungen (nach Ende)", new Color(0.25f, 0.27f, 0.30f), patient.ToggleInjuries, 40, 15);
            var hover = RuntimeUi.Label(box, "", 17, new Color(1f, 0.9f, 0.5f), 44);
            patient.Configure(sim, hover, toggle.GetComponentInChildren<TMP_Text>(), ids, images);
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
                es.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        /// <summary>Der Monitor an der Deckenversorgungseinheit zeigt Werte und Kurven (World-Space-Canvas).</summary>
        static void WorldMonitor(SimulationManager sim)
        {
            var body = MonitorBody;
            if (body == null || GameObject.Find("Monitor-Anzeige") != null) return;
            var go = new GameObject("Monitor-Anzeige", typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(560, 380);
            rt.localScale = Vector3.one * (0.5f / 560f);
            rt.rotation = body.rotation;
            rt.position = body.position - body.forward * 0.042f; // knapp vor der Bildschirmfläche (−z-Seite)
            var bg = RuntimeUi.Panel(go.transform, "Hintergrund", new Color(0.01f, 0.02f, 0.02f, 1f));
            RuntimeUi.Place(bg, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var e = Img(go.transform, -20); var p = Img(go.transform, -140); var c2 = Img(go.transform, -260);
            var hr = Num(go.transform, -25, new Color(0.3f, 1f, 0.4f), 64);
            var sp = Num(go.transform, -145, new Color(0.4f, 0.85f, 1f), 56);
            var co = Num(go.transform, -265, new Color(1f, 0.9f, 0.4f), 44);
            var bp = Num(go.transform, -330, new Color(1f, 0.45f, 0.45f), 30, TextAlignmentOptions.Left, 12);
            bp.rectTransform.sizeDelta = new Vector2(380, 40);
            var waves = Object.FindAnyObjectByType<MonitorWaveforms>();
            if (waves == null)
            {
                waves = go.AddComponent<MonitorWaveforms>();
                waves.Configure(sim, null, null, null);
            }
            waves.SetMirror(e, p, c2);
            go.AddComponent<VitalsMonitor>().Configure(sim, hr, bp, null, sp, co, null, null);
        }
        static RawImage Img(Transform parent, float y)
        {
            var img = new GameObject("Kurve", typeof(RectTransform)).AddComponent<RawImage>();
            img.transform.SetParent(parent, false);
            var r = img.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(10, y); r.sizeDelta = new Vector2(380, 80);
            return img;
        }

        static TMP_Text Num(Transform parent, float y, Color c, float size,
                            TextAlignmentOptions align = TextAlignmentOptions.Right, float x = 400)
        {
            var t = RuntimeUi.Label(parent, "--", size, c);
            t.alignment = align;
            var r = t.rectTransform; r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(150, 70);
            return t;
        }
    }
}

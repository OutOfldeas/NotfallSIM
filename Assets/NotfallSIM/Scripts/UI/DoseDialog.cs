using System;
using NotfallSIM.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>
    /// Dosisdialog für Maßnahmen mit einstellbarer Dosis (interventions.json, Feld "dose"): Dosis mit −/+ wählen,
    /// Gesamtdosis bei mg/kg, Hinweis außerhalb des üblichen Bereichs. Die Simulation läuft währenddessen weiter.
    /// Baut sich zur Laufzeit selbst auf.
    /// </summary>
    public class DoseDialog : MonoBehaviour
    {
        GameObject _overlay;
        TMP_Text _title, _value, _hint;
        InterventionDefinition _def;
        double _dose, _weight;
        Action<double> _give;

        static DoseDialog _instance;

        public static void Show(InterventionDefinition def, double weight_kg, Action<double> give)
        {
            if (_instance == null)
            {
                var go = new GameObject("Dosisdialog", typeof(RectTransform));
                _instance = go.AddComponent<DoseDialog>();
                _instance.Build();
            }
            _instance.Open(def, weight_kg, give);
        }

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var root = (RectTransform)transform;

            _overlay = RuntimeUi.Panel(root, "Overlay", new Color(0, 0, 0, 0.45f)).gameObject;
            RuntimeUi.Place((RectTransform)_overlay.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var box = RuntimeUi.Panel(_overlay.transform, "Box", new Color(0.07f, 0.09f, 0.11f, 0.98f));
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(520, 360);
            RuntimeUi.Column(box, 10, 24);
            _title = RuntimeUi.Label(box, "", 26, Color.white, 40);
            _value = RuntimeUi.Label(box, "", 40, new Color(0.45f, 0.88f, 0.55f), 56);
            _value.alignment = TextAlignmentOptions.Center;

            var row = new GameObject("Schritte", typeof(RectTransform));
            row.transform.SetParent(box, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = true;
            row.AddComponent<LayoutElement>().preferredHeight = 52;
            var step = new Color(0.20f, 0.23f, 0.27f);
            RuntimeUi.Button(row.transform, "− −", step, () => Step(-4), 52, 22);
            RuntimeUi.Button(row.transform, "−", step, () => Step(-1), 52, 22);
            RuntimeUi.Button(row.transform, "+", step, () => Step(1), 52, 22);
            RuntimeUi.Button(row.transform, "+ +", step, () => Step(4), 52, 22);

            _hint = RuntimeUi.Label(box, "", 17, new Color(0.7f, 0.74f, 0.78f), 48);

            var actions = new GameObject("Aktionen", typeof(RectTransform));
            actions.transform.SetParent(box, false);
            var ah = actions.AddComponent<HorizontalLayoutGroup>();
            ah.spacing = 8; ah.childControlWidth = true; ah.childControlHeight = true; ah.childForceExpandWidth = true;
            actions.AddComponent<LayoutElement>().preferredHeight = 52;
            RuntimeUi.Button(actions.transform, "Geben", new Color(0.12f, 0.45f, 0.38f), Give, 52, 22);
            RuntimeUi.Button(actions.transform, "Abbrechen", new Color(0.25f, 0.27f, 0.30f), Close, 52, 22);
            _overlay.SetActive(false);
        }

        void Open(InterventionDefinition def, double weight_kg, Action<double> give)
        {
            _def = def; _weight = weight_kg; _give = give;
            _dose = def.dose.reference;
            _title.text = $"<b>{def.label}</b>";
            Refresh();
            _overlay.SetActive(true);
        }

        void Step(int n) { _dose = _def.dose.Clamp(_dose + n * _def.dose.step); Refresh(); }

        void Refresh()
        {
            var d = _def.dose;
            bool usual = d.Usual(_dose);
            _value.text = d.Format(_dose, _weight);
            _value.color = usual ? new Color(0.45f, 0.88f, 0.55f) : new Color(1f, 0.55f, 0.35f);
            _hint.text = $"Üblich {d.Format(d.usualMin, 0).Replace(" " + d.unit, "")}–{d.Format(d.usualMax, 0)}" +
                         (usual ? "" : "  · <color=#FF8A5C>außerhalb des üblichen Bereichs</color>");
        }

        void Give() { _overlay.SetActive(false); _give?.Invoke(_dose); }
        void Close() => _overlay.SetActive(false);
    }
}

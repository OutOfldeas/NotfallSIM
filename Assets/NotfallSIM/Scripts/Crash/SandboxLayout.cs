using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NotfallSIM.Crash
{
    /// <summary>
    /// Hält die Panels der Unfall-Sandbox auf dem Bildschirm (zur Laufzeit, ohne die Szene neu zu erstellen):
    /// Einstellungstext wächst mit seinem Inhalt statt über die Knöpfe zu laufen; das linke Panel wird verkleinert, wenn es
    /// höher als das Fenster ist; der Ergebnistext steht in einer scrollbaren Ansicht (Mausrad), die das rechte Panel nie
    /// über den Bildschirm hinaus wachsen lässt.
    /// </summary>
    public class SandboxLayout : MonoBehaviour
    {
        const float Margin = 20;
        RectTransform _canvas, _left, _right, _view;
        LayoutElement _viewLayout;
        ScrollRect _scroll;
        TMP_Text _result;
        string _lastText;

        public static void Attach(TMP_Text settings, TMP_Text result)
        {
            if (settings == null || result == null || result.canvas == null) return;
            var root = result.canvas.rootCanvas;
            if (root.GetComponent<SandboxLayout>() != null) return;
            var l = root.gameObject.AddComponent<SandboxLayout>();
            l._canvas = root.GetComponent<RectTransform>();

            // Einstellungen: Höhe aus dem Text (vorher fest 135 px)
            var se = settings.GetComponent<LayoutElement>();
            if (se != null) se.preferredHeight = -1;
            l._left = settings.transform.parent as RectTransform;

            // Ergebnis: Text in eine scrollbare Ansicht an seiner Stelle verschieben
            l._right = result.transform.parent as RectTransform;
            var view = new GameObject("Ergebnis-Ansicht", typeof(RectTransform));
            view.transform.SetParent(l._right, false);
            view.transform.SetSiblingIndex(result.transform.GetSiblingIndex());
            l._view = view.GetComponent<RectTransform>();
            l._viewLayout = view.AddComponent<LayoutElement>();
            l._viewLayout.preferredHeight = 400;
            view.AddComponent<RectMask2D>();
            // unsichtbare Fläche: ohne Grafik bekommt die Ansicht keine Mausrad-Ereignisse
            var hit = view.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            hit.raycastTarget = true;
            var re = result.GetComponent<LayoutElement>();
            if (re != null) re.ignoreLayout = true;
            var rt = result.rectTransform;
            rt.SetParent(l._view, false);
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(0, 400);
            var fit = result.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            l._scroll = view.AddComponent<ScrollRect>();
            l._scroll.horizontal = false; l._scroll.vertical = true;
            l._scroll.movementType = ScrollRect.MovementType.Clamped;
            l._scroll.scrollSensitivity = 40;
            l._scroll.viewport = l._view;
            l._scroll.content = rt;
            l._result = result;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            float h = _canvas.rect.height - 2 * Margin;
            // Ergebnisansicht: so hoch wie der Text, höchstens so, dass das Panel mit den Knöpfen auf den Bildschirm passt
            if (_view != null && _right != null)
            {
                float others = _right.rect.height - _view.rect.height;
                float pref = _result.preferredHeight + 4;
                _viewLayout.preferredHeight = Mathf.Clamp(pref, 40, Mathf.Max(80, h - others));
                if (_result.text != _lastText) { _lastText = _result.text; _scroll.verticalNormalizedPosition = 1; }
            }
            // Linkes Panel: zu hoch → verkleinern (Ecke oben links bleibt)
            if (_left != null && _left.rect.height > 1)
            {
                float s = Mathf.Min(1, h / _left.rect.height);
                if (Mathf.Abs(_left.localScale.x - s) > 0.002f) _left.localScale = new Vector3(s, s, 1);
            }
        }
    }
}

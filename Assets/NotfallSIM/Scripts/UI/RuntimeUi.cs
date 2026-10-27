using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace NotfallSIM.UI
{
    /// <summary>Kleine Bausteine für Oberflächen, die erst zur Laufzeit entstehen (z. B. Hauptmenü aus den Szenariodateien).</summary>
    public static class RuntimeUi
    {
        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        public static VerticalLayoutGroup Column(RectTransform rt, float spacing, int padding)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            return v;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, float height = -1)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.color = color; t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.TopLeft;
            var le = go.AddComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;
            return t;
        }

        public static Button Button(Transform parent, string text, Color color, UnityAction onClick, float height = 48, float size = 20)
        {
            var go = new GameObject(text, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var b = go.AddComponent<Button>();
            b.onClick.AddListener(onClick);
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            var t = Label(go.transform, text, size, Color.white);
            t.alignment = TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(10, 4); rt.offsetMax = new Vector2(-10, -4);
            return b;
        }

        public static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }
    }
}

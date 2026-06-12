using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>程序化 uGUI 构建辅助:在指定父节点下快速建文本/按钮/全屏背景。仅 TapRush 示例用。</summary>
    public static class UiBuild
    {
        public static RectTransform FullScreen(Transform parent, string name, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            Stretch(rt);
            go.GetComponent<Image>().color = bg;
            return rt;
        }

        public static TMP_Text Label(Transform parent, string name, string text,
            Vector2 anchoredPos, float fontSize = 48f, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(900f, 120f);
            rt.anchoredPosition = anchoredPos;

            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = fontSize;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color ?? Color.white;
            return t;
        }

        public static Button Button(Transform parent, string name, string label,
            Vector2 anchoredPos, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = new Vector2(520f, 140f);
            rt.anchoredPosition = anchoredPos;
            go.GetComponent<Image>().color = new Color(0.2f, 0.5f, 0.9f, 1f);

            var btn = go.GetComponent<Button>();
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            Label(rt, "Label", label, Vector2.zero, 44f);
            return btn;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}

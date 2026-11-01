using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// The quiet, Arena-like menu look: a full-screen dark veil, a serif title with a hairline under it, and
    /// outlined capsule buttons in a centred column. Shared by Options and the in-game menu.
    /// </summary>
    public static class MenuKit
    {
        public static readonly Color Veil = new Color(0.02f, 0.025f, 0.035f, 0.88f);
        static readonly Color Fill = new Color(1f, 1f, 1f, 0.04f);
        static readonly Color Rim = new Color(1f, 1f, 1f, 0.42f);
        public static readonly Color DangerText = new Color(1f, 0.38f, 0.32f);

        /// <summary>A full-screen veil; returns it (clicking it does nothing, so a stray click never closes a menu).</summary>
        public static Image Shade(string name, RectTransform parent)
        {
            var shade = UIKit.Panel(name, parent, Veil, false);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(4000, 3000);
            // A soft darker band behind the content, like light falling off toward the edges.
            var glow = UIKit.Rect("Vignette", shade.transform);
            glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
            glow.sizeDelta = new Vector2(1920, 1080);
            var gi = glow.gameObject.AddComponent<RawImage>();
            gi.texture = UIKit.Gradient(new Color(0.06f, 0.07f, 0.09f, 0.0f), new Color(0f, 0f, 0f, 0.35f));
            gi.raycastTarget = false;
            return shade;
        }

        /// <summary>The page title, centred, with a thin line under it. Returns the title text.</summary>
        public static Text Title(Transform parent, string text, float top, float width)
        {
            var t = UIKit.Label("Title", parent, text, 46, new Color(0.93f, 0.9f, 0.84f), TextAnchor.MiddleCenter, FontStyle.Bold).Titled();
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 1); t.rectTransform.pivot = new Vector2(0.5f, 1);
            t.rectTransform.sizeDelta = new Vector2(width - 200, 70); t.rectTransform.anchoredPosition = new Vector2(0, -top);
            var line = UIKit.Panel("Rule", parent, new Color(1f, 1f, 1f, 0.12f), false);
            line.raycastTarget = false;
            var lr = line.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1); lr.pivot = new Vector2(0.5f, 1);
            lr.sizeDelta = new Vector2(width - 120, 1.5f); lr.anchoredPosition = new Vector2(0, -top - 78);
            return t;
        }

        /// <summary>An outlined capsule button (x, y = bottom-left in the parent's space).</summary>
        public static Button Capsule(Transform parent, string label, UnityAction onClick, float x, float y, float w = 300, float h = 46, bool danger = false)
        {
            var b = UIKit.Button("Item " + label, parent, label, Fill, onClick, 19);
            var rt = (RectTransform)b.transform;
            rt.Place(x, y, w, h);
            var img = b.GetComponent<Image>();
            img.pixelsPerUnitMultiplier = 32f / h * 0.5f;   // fully round ends
            var rim = b.gameObject.AddComponent<Outline>();
            rim.effectColor = Rim; rim.effectDistance = new Vector2(1.2f, -1.2f);
            // Hover: the faint fill brightens (Unity's tint can't raise a 4% fill, so do it by hand).
            b.transition = Selectable.Transition.None;
            var pe = b.gameObject.AddComponent<PointerEvents>();
            pe.OnEnter = () => img.color = new Color(1f, 1f, 1f, 0.14f);
            pe.OnExit = () => img.color = Fill;
            var t = UIKit.LabelOf(b);
            t.color = danger ? DangerText : new Color(0.92f, 0.92f, 0.94f);
            t.font = UIKit.Font != null ? UIKit.Font : t.font;   // regular weight, like Arena's menu items
            t.fontStyle = FontStyle.Normal;
            return b;
        }

        /// <summary>A small underlined text link (footer: GitHub, Report a bug...).</summary>
        public static Button Link(Transform parent, string label, UnityAction onClick, float x, float y, float w)
        {
            var b = UIKit.Button("Link " + label, parent, "", Color.clear, onClick, 15);
            ((RectTransform)b.transform).Place(x, y, w, 30);
            var t = UIKit.LabelOf(b);
            t.text = "<u>" + label + "</u>";
            t.supportRichText = true;
            t.color = new Color(0.75f, 0.78f, 0.84f);
            t.fontStyle = FontStyle.Normal;
            if (UIKit.Font != null) t.font = UIKit.Font;
            return b;
        }

        /// <summary>The ‹ back arrow in the top-left corner of a menu page.</summary>
        public static Button Back(Transform parent, UnityAction onClick, float top)
        {
            var b = UIKit.Button("Back", parent, "‹", Color.clear, onClick, 60);
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.sizeDelta = new Vector2(70, 70); rt.anchoredPosition = new Vector2(20, -top);
            var t = UIKit.LabelOf(b);
            t.color = new Color(0.9f, 0.9f, 0.92f);
            if (t is SymbolText st) st.Localize = false;
            return b;
        }

        public const string GitHub = "https://github.com/ryanthecomic/MTG-United";
        public const string Issues = GitHub + "/issues";
    }
}

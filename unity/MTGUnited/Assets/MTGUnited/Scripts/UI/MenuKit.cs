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
            shade.rectTransform.Stretch();   // the overlay layer covers the whole window
            // A soft darker band behind the content, like light falling off toward the edges.
            var glow = UIKit.Rect("Vignette", shade.transform);
            glow.anchorMin = glow.anchorMax = new Vector2(0.5f, 0.5f);
            glow.sizeDelta = new Vector2(1920, 1080);
            var gi = glow.gameObject.AddComponent<RawImage>();
            gi.texture = UIKit.Gradient(new Color(0.06f, 0.07f, 0.09f, 0.0f), new Color(0f, 0f, 0f, 0.35f));
            gi.raycastTarget = false;
            return shade;
        }

        /// <summary>Jodah, the Unifier (Resources/Visuals/Images): the menus' backdrop and the GitHub banner.</summary>
        public static Texture2D JodahArt
        {
            get
            {
                if (jodah == null) jodah = Resources.Load<Texture2D>("Visuals/Images/Jodah-the-Unifier-Dominaria-United-MtG-Art");
                return jodah;
            }
        }
        static Texture2D jodah;

        /// <summary>
        /// A picture filling <paramref name="holder"/> without stretching (cropped instead), with a point of the
        /// picture kept in view (focus: 0..1 from the bottom-left, e.g. a face), then dimmed by <paramref name="dim"/>.
        /// </summary>
        public static void Backdrop(RectTransform holder, Texture2D tex, float dim, Vector2 focus, float brightness = 1f)
        {
            if (tex == null) return;
            holder.gameObject.AddComponent<RectMask2D>();
            var pic = UIKit.Rect("Picture", holder);
            pic.SetAsFirstSibling();
            var img = pic.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            img.color = new Color(brightness, brightness, brightness, 1f);   // darker picture, same colours
            var cover = pic.gameObject.AddComponent<CoverFit>();
            cover.Aspect = tex.width / (float)tex.height;
            cover.Focus = focus;
            if (dim <= 0f) return;
            var veil = UIKit.Panel("Dim", holder, new Color(0.02f, 0.025f, 0.035f, dim), false);
            veil.rectTransform.Stretch();
            veil.raycastTarget = false;
            veil.transform.SetSiblingIndex(1);
        }

        /// <summary>The dim shadow over whatever is behind a menu (the game stays visible).</summary>
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.62f);
        public static readonly Color TitleColor = new Color(0.93f, 0.9f, 0.84f);

        /// <summary>
        /// Arena-style menu band: full width, centred vertically (offset by yOffset), with hairlines along its
        /// edges. With <paramref name="art"/> it shows that picture (Jodah, for the main menus); otherwise a quiet
        /// black-grey gradient. Clicks on it stay in the menu.
        /// </summary>
        public static RectTransform Band(Transform shade, float height, Texture2D art = null, float yOffset = 0f)
        {
            var band = UIKit.Rect("Band", shade);
            band.anchorMin = new Vector2(0, 0.5f); band.anchorMax = new Vector2(1, 0.5f); band.pivot = new Vector2(0.5f, 0.5f);
            band.sizeDelta = new Vector2(0, height); band.anchoredPosition = new Vector2(0, yOffset);
            var bg = band.gameObject.AddComponent<RawImage>();   // also catches clicks
            bg.texture = UIKit.Gradient(new Color(0.11f, 0.11f, 0.12f, 0.98f), new Color(0.025f, 0.025f, 0.03f, 0.98f));
            band.gameObject.AddComponent<PointerEvents>();
            if (art != null) Backdrop(band, art, 0.6f, new Vector2(0.47f, 0.8f), 0.5f);
            foreach (float edge in new[] { 0f, 1f })
            {
                var line = UIKit.Panel("Edge", band, new Color(1f, 0.85f, 0.6f, art != null ? 0.35f : 0.22f), false);
                line.raycastTarget = false;
                line.rectTransform.anchorMin = new Vector2(0, edge); line.rectTransform.anchorMax = new Vector2(1, edge);
                line.rectTransform.pivot = new Vector2(0.5f, edge); line.rectTransform.sizeDelta = new Vector2(0, 1.5f);
            }
            return band;
        }

        /// <summary>
        /// Turns an old-style centred dialog panel into the Arena look: the panel's box disappears and a
        /// full-width band sits behind it (following the panel's height and scale); the shade becomes the dim
        /// shadow; the title gets the menu title colour and size.
        /// </summary>
        public static RectTransform Bandify(Image panel, float pad = 70f)
        {
            var shade = panel.transform.parent;
            var shadeImg = shade.GetComponent<Image>();
            if (shadeImg != null) shadeImg.color = Dim;
            var band = Band(shade, panel.rectTransform.sizeDelta.y + pad);
            band.SetSiblingIndex(panel.transform.GetSiblingIndex());   // just behind the panel
            band.gameObject.AddComponent<BandFollow>().Init(panel.rectTransform, pad);
            panel.color = Color.clear;
            foreach (var fx in panel.GetComponents<Shadow>()) Object.Destroy(fx);   // Outline is a Shadow too
            var title = panel.transform.Find("Title");
            var t = title != null ? title.GetComponent<Text>() : null;
            if (t != null)
            {
                t.color = TitleColor;
                t.fontSize = Mathf.Max(t.fontSize, 32);
                t.Titled();
            }
            return band;
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
            var b = UIKit.Button("Link " + label, parent, label, Color.clear, onClick, 15);
            ((RectTransform)b.transform).Place(x, y, w, 30);
            var t = UIKit.LabelOf(b);
            t.color = new Color(0.78f, 0.8f, 0.86f);
            t.fontStyle = FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;   // one line
            if (UIKit.Font != null) t.font = UIKit.Font;
            // Unity's Text has no underline tag: a hairline under the words, as wide as they are.
            var line = UIKit.Panel("Underline", b.transform, new Color(0.78f, 0.8f, 0.86f, 0.7f), false);
            line.raycastTarget = false;
            var lr = line.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0); lr.pivot = new Vector2(0.5f, 0);
            lr.anchoredPosition = new Vector2(0, 4);
            b.gameObject.AddComponent<UnderlineFit>().Init(t, lr);
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

    /// <summary>Keeps a picture covering its parent (no stretching) with a chosen point of it in view.</summary>
    public class CoverFit : MonoBehaviour
    {
        public float Aspect = 1f;                       // width / height of the picture
        public Vector2 Focus = new Vector2(0.5f, 0.5f); // 0..1 from the picture's bottom-left
        Vector2 lastSize = new Vector2(-1, -1);

        void LateUpdate()
        {
            var parent = transform.parent as RectTransform;
            if (parent == null) return;
            var size = parent.rect.size;
            if (size == lastSize || size.x <= 0 || size.y <= 0) return;
            lastSize = size;
            var rt = (RectTransform)transform;
            float w = size.x, h = w / Aspect;
            if (h < size.y) { h = size.y; w = h * Aspect; }
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            float dx = Mathf.Clamp((0.5f - Focus.x) * w, -(w - size.x) / 2f, (w - size.x) / 2f);
            float dy = Mathf.Clamp((0.5f - Focus.y) * h, -(h - size.y) / 2f, (h - size.y) / 2f);
            rt.anchoredPosition = new Vector2(dx, dy);
        }
    }

    /// <summary>Sizes a link's underline to the width of its text.</summary>
    public class UnderlineFit : MonoBehaviour
    {
        Text text; RectTransform line;
        public void Init(Text t, RectTransform l) { text = t; line = l; }
        void LateUpdate()
        {
            if (text == null || line == null) return;
            float w = Mathf.Min(text.preferredWidth, ((RectTransform)transform).rect.width + 60f);
            if (!Mathf.Approximately(line.sizeDelta.x, w)) line.sizeDelta = new Vector2(w, 1.2f);
        }
    }

    /// <summary>Keeps a menu band as tall as its dialog panel (which may resize or scale) and level with it.</summary>
    public class BandFollow : MonoBehaviour
    {
        RectTransform target; float pad;
        public void Init(RectTransform t, float p) { target = t; pad = p; LateUpdate(); }
        void LateUpdate()
        {
            if (target == null) return;
            var rt = (RectTransform)transform;
            float h = target.rect.height * target.localScale.y + pad * target.localScale.y;
            if (!Mathf.Approximately(rt.sizeDelta.y, h)) rt.sizeDelta = new Vector2(0, h);
            float y = target.anchoredPosition.y;
            if (!Mathf.Approximately(rt.anchoredPosition.y, y)) rt.anchoredPosition = new Vector2(0, y);
        }
    }
}

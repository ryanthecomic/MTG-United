using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Colours and sizes for the whole client, so the look can be retuned in one place.</summary>
    public static class Theme
    {
        public static readonly Color BgTop = new Color(0.07f, 0.09f, 0.14f);
        public static readonly Color BgBottom = new Color(0.03f, 0.04f, 0.07f);
        public static readonly Color Panel = new Color(0.10f, 0.12f, 0.18f, 0.92f);
        public static readonly Color PanelLight = new Color(0.16f, 0.19f, 0.27f, 0.95f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Text = new Color(0.92f, 0.94f, 0.98f);
        public static readonly Color TextDim = new Color(0.62f, 0.67f, 0.76f);
        public static readonly Color Accent = new Color(0.98f, 0.62f, 0.18f);       // primary button
        public static readonly Color AccentDim = new Color(0.55f, 0.38f, 0.16f);
        public static readonly Color Secondary = new Color(0.25f, 0.30f, 0.42f);
        public static readonly Color Playable = new Color(0.25f, 0.75f, 1.00f);     // card you can play/activate
        public static readonly Color Selectable = new Color(1.00f, 0.86f, 0.25f);   // valid target / choice
        public static readonly Color Chosen = new Color(1.00f, 0.45f, 0.15f);       // already picked
        public static readonly Color Attack = new Color(0.95f, 0.25f, 0.22f);
        public static readonly Color Block = new Color(0.30f, 0.65f, 1.00f);
        public static readonly Color Life = new Color(0.95f, 0.97f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.30f, 0.30f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.45f);

        public static Color ForManaColor(string c)
        {
            switch (c)
            {
                case "W": return new Color(0.97f, 0.94f, 0.80f);
                case "U": return new Color(0.35f, 0.62f, 0.95f);
                case "B": return new Color(0.45f, 0.40f, 0.48f);
                case "R": return new Color(0.92f, 0.36f, 0.27f);
                case "G": return new Color(0.33f, 0.72f, 0.40f);
                default: return new Color(0.70f, 0.70f, 0.72f);
            }
        }

        /// <summary>Card frame colour for the text-only fallback, from Forge's colour string (e.g. "WU").</summary>
        public static Color FrameFor(CardData c)
        {
            if (c == null) return new Color(0.3f, 0.3f, 0.32f);
            if (c.IsLand) return new Color(0.42f, 0.36f, 0.28f);
            string cols = (c.Colors ?? "").ToUpperInvariant();
            int n = 0; Color sum = Color.black;
            foreach (char ch in "WUBRG")
            {
                if (cols.IndexOf(ch) >= 0) { sum += ForManaColor(ch.ToString()); n++; }
            }
            if (n == 0) return c.IsArtifact ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.50f, 0.50f, 0.52f);
            if (n > 1) return new Color(0.80f, 0.68f, 0.35f); // gold
            return sum * 0.85f;
        }
    }

    /// <summary>Helpers for building uGUI from code.</summary>
    public static class UIKit
    {
        static Font font;
        static Sprite white;
        static Sprite rounded;

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                font = OsFont("Segoe UI", "Helvetica Neue", "Arial");
                if (font != null) return font;
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (font == null) { try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
                if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Helvetica" }, 16);
                return font;
            }
        }

        static Font heading;
        static bool headingTried;

        /// <summary>Condensed display font for titles, buttons and numbers (Bahnschrift ships with Windows 10+).</summary>
        public static Font Heading
        {
            get
            {
                if (!headingTried) { headingTried = true; heading = OsFont("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold"); }
                return heading;
            }
        }

        static Font OsFont(params string[] names)
        {
            try
            {
                var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var n in names)
                    if (installed.Contains(n)) return Font.CreateDynamicFontFromOSFont(n, 32);
            }
            catch { }
            return null;
        }

        public static Sprite White
        {
            get
            {
                if (white != null) return white;
                var t = new Texture2D(4, 4);
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = Color.white;
                t.SetPixels(px); t.Apply();
                white = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                return white;
            }
        }

        /// <summary>A 9-sliced rounded rectangle generated at runtime.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (rounded != null) return rounded;
                const int size = 64, r = 16;
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                rounded = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return rounded;
            }
        }

        static Sprite softShadow;

        /// <summary>Blurred rounded rectangle used as a drop shadow under cards and panels.</summary>
        public static Sprite SoftShadow
        {
            get
            {
                if (softShadow != null) return softShadow;
                const int size = 96, r = 40;
                var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r - x - 0.5f, x + 0.5f - (size - r)));
                    float dy = Mathf.Max(0, Mathf.Max(r - y - 0.5f, y + 0.5f - (size - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / r;     // 0 inside, 1 at the edge
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(0, 0, 0, a * a));
                }
                t.Apply();
                t.wrapMode = TextureWrapMode.Clamp;
                softShadow = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(r, r, r, r));
                return softShadow;
            }
        }

        /// <summary>
        /// Adds a soft drop shadow behind a rect. If the target draws something itself, the shadow becomes a sibling
        /// placed just before it (a child would render on top of the target's own graphic); otherwise it becomes the
        /// target's first child. Call after the target has its final position.
        /// </summary>
        public static Image AddShadow(Transform target, float spread = 18, Vector2? offset = null, float alpha = 0.6f)
        {
            var o = offset ?? new Vector2(4, -8);
            var trt = (RectTransform)target;
            bool asSibling = target.GetComponent<Graphic>() != null && target.parent != null;
            var sh = Rect("Shadow", asSibling ? target.parent : target);
            var img = sh.gameObject.AddComponent<Image>();
            img.sprite = SoftShadow;
            img.type = Image.Type.Sliced;
            img.color = new Color(0, 0, 0, alpha);
            img.raycastTarget = false;
            if (asSibling)
            {
                sh.SetSiblingIndex(target.GetSiblingIndex());
                sh.anchorMin = trt.anchorMin; sh.anchorMax = trt.anchorMax; sh.pivot = trt.pivot;
                sh.sizeDelta = trt.sizeDelta + new Vector2(spread * 2, spread * 2);
                sh.anchoredPosition = trt.anchoredPosition + o + new Vector2((trt.pivot.x - 0.5f) * spread * 2, (trt.pivot.y - 0.5f) * spread * 2);
            }
            else
            {
                sh.SetAsFirstSibling();
                sh.anchorMin = Vector2.zero; sh.anchorMax = Vector2.one;
                sh.offsetMin = new Vector2(-spread + o.x, -spread + o.y);
                sh.offsetMax = new Vector2(spread + o.x, spread + o.y);
            }
            return img;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Places a rect in its parent's space using bottom-left based pixel coordinates.</summary>
        public static RectTransform Place(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + w / 2f, y + h / 2f);
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float bottom = 0, float right = 0, float top = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Panel(string name, Transform parent, Color color, bool roundedCorners = true)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = roundedCorners ? Rounded : White;
            img.type = roundedCorners ? Image.Type.Sliced : Image.Type.Simple;
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            // Bold text uses the display font, which already has the weight built in.
            bool useHeading = style == FontStyle.Bold && Heading != null;
            t.font = useHeading ? Heading : Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.fontStyle = useHeading ? FontStyle.Normal : style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.supportRichText = true;
            return t;
        }

        public static Text Outline(this Text t, float thickness = 1.5f)
        {
            var o = t.gameObject.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0, 0, 0, 0.85f);
            o.effectDistance = new Vector2(thickness, -thickness);
            return t;
        }

        public static Button Button(string name, Transform parent, string label, Color color, UnityAction onClick, int fontSize = 22)
        {
            var img = Panel(name, parent, color);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            colors.colorMultiplier = 1.2f;
            b.colors = colors;
            b.onClick.AddListener(() => Sound.Play(Sfx.UiClick, 0.6f));
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Label("Label", img.transform, label, fontSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Stretch(8, 4, 8, 4);
            return b;
        }

        public static Text LabelOf(Button b) => b.GetComponentInChildren<Text>();

        public static InputField Input(string name, Transform parent, string placeholder, bool multiline, int fontSize = 18)
        {
            var bg = Panel(name, parent, new Color(0.05f, 0.06f, 0.09f, 0.95f));
            var field = bg.gameObject.AddComponent<InputField>();
            var text = Label("Text", bg.transform, "", fontSize, Theme.Text, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft);
            text.rectTransform.Stretch(12, 8, 12, 8);
            text.supportRichText = false;
            text.verticalOverflow = multiline ? VerticalWrapMode.Overflow : VerticalWrapMode.Truncate;
            var ph = Label("Placeholder", bg.transform, placeholder, fontSize, Theme.TextDim, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, FontStyle.Italic);
            ph.rectTransform.Stretch(12, 8, 12, 8);
            field.textComponent = text;
            field.placeholder = ph;
            field.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            return field;
        }

        public static void SetVisible(this Component c, bool visible)
        {
            if (c != null && c.gameObject.activeSelf != visible) c.gameObject.SetActive(visible);
        }

        /// <summary>Vertical gradient background texture.</summary>
        public static Texture2D Gradient(Color top, Color bottom)
        {
            var t = new Texture2D(1, 64);
            for (int y = 0; y < 64; y++) t.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        public static string Plural(int n, string word) => n + " " + word + (n == 1 ? "" : "s");

        public static T GetOrAdd<T>(this GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }
    }

    /// <summary>Raises callbacks for hover and clicks on any UI graphic.</summary>
    public class PointerEvents : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerClickHandler
    {
        public Action OnEnter, OnExit, OnClick, OnRightClick;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => OnEnter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => OnExit?.Invoke();
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Right) OnRightClick?.Invoke();
            else OnClick?.Invoke();
        }
    }
}

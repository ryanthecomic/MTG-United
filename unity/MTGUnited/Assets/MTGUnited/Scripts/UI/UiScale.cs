using UnityEngine;

namespace MTGUnited
{
    /// <summary>Player-adjustable interface settings (Options menu).</summary>
    public static class UiScale
    {
        public const float Min = 0.8f, Max = 1.25f;

        /// <summary>Size of the HUD, player panels, previews and pop-ups (the battlefield itself is not scaled).</summary>
        static float cached = -1f;
        public static float Value
        {
            get { if (cached < 0f) cached = Mathf.Clamp(PlayerPrefs.GetFloat("uiScale", 1f), Min, Max); return cached; }
            set { cached = Mathf.Clamp(value, Min, Max); PlayerPrefs.SetFloat("uiScale", cached); }
        }

        /// <summary>Size of cards on the battlefield and in hand (Options > card size).</summary>
        static float cardCached = -1f;
        public static float CardScale
        {
            get { if (cardCached < 0f) cardCached = Mathf.Clamp(PlayerPrefs.GetFloat("cardScale", 1f), 0.8f, 1.3f); return cardCached; }
            set { cardCached = Mathf.Clamp(value, 0.8f, 1.3f); PlayerPrefs.SetFloat("cardScale", cardCached); }
        }

        /// <summary>Size of all interface text (Options > text size). Applied live to every label.</summary>
        static float textCached = -1f;
        public static float TextScale
        {
            get { if (textCached < 0f) textCached = Mathf.Clamp(PlayerPrefs.GetFloat("textScale", 1.1f), 0.85f, 1.5f); return textCached; }
            set { textCached = Mathf.Clamp(value, 0.85f, 1.5f); PlayerPrefs.SetFloat("textScale", textCached); SymbolText.RescaleAll(); }
        }

        /// <summary>Seconds of hovering before the large card preview appears.</summary>
        public static float PreviewDelay
        {
            get
            {
                // New default 0.5 s (was 1.5 s): applied once to existing installs too.
                if (PlayerPrefs.GetInt("previewDelayV2", 0) == 0) { PlayerPrefs.SetFloat("previewDelay", 0.5f); PlayerPrefs.SetInt("previewDelayV2", 1); }
                return PlayerPrefs.GetFloat("previewDelay", 0.5f);
            }
            set => PlayerPrefs.SetFloat("previewDelay", Mathf.Clamp(value, 0f, 3f));
        }

        /// <summary>
        /// How far the window extends past the 1920x1080 board on each side (board units; 0 when the window has
        /// exactly that shape). HUD corners are pinned out by this much so they sit on the real window edges
        /// instead of floating inside a letterboxed frame (16:10 screens, windows shorter than the taskbar...).
        /// Set every frame by GameController.
        /// </summary>
        public static Vector2 Extra;

        /// <summary>Scales a board point away from an origin, e.g. a pile position that grows with the HUD.</summary>
        public static Vector2 About(Vector2 point, Vector2 origin) => origin + (point - origin) * Value;
    }

    /// <summary>
    /// Scales an element by the UI scale around a fixed origin (in the element's anchored-position space), so a
    /// group of elements sharing one origin grows together like a single block anchored to a screen corner.
    /// Works with code that re-places the element every frame or only once.
    /// </summary>
    public class UiScaled : MonoBehaviour
    {
        public Vector2 Origin;
        /// <summary>Which window edges this element hugs (-1 left/bottom, +1 right/top, 0 none). From the origin by default.</summary>
        public Vector2 Pin;
        /// <summary>Cap so a big panel never grows past the screen (1080 board units tall).</summary>
        public float MaxScale = 99f;
        Vector2 basePos, applied;
        bool has;

        public static UiScaled Add(Component c, Vector2 origin)
        {
            var u = c.gameObject.GetComponent<UiScaled>();
            if (u == null) u = c.gameObject.AddComponent<UiScaled>();
            u.Origin = origin;
            // A corner origin means a corner element: pin it to that corner of the window.
            u.Pin = origin == Vector2.zero ? Vector2.zero
                : new Vector2(origin.x < 640 ? -1 : origin.x > 1280 ? 1 : 0, origin.y < 360 ? -1 : origin.y > 720 ? 1 : 0);
            return u;
        }

        void LateUpdate()
        {
            var rt = (RectTransform)transform;
            float s = Mathf.Min(UiScale.Value, MaxScale);
            if (!has || rt.anchoredPosition != applied) { basePos = rt.anchoredPosition; has = true; }
            applied = Origin + (basePos - Origin) * s + Vector2.Scale(Pin, UiScale.Extra);
            rt.anchoredPosition = applied;
            if (!Mathf.Approximately(rt.localScale.x, s)) rt.localScale = new Vector3(s, s, 1f);
        }
    }
}

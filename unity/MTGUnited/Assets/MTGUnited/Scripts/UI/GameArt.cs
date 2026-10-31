using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MTGUnited
{
    /// <summary>
    /// Art from Assets/MTGUnited/Resources/Visuals. Every getter returns null when the file is missing, and the UI
    /// falls back to its generated look, so the project still runs without these files.
    /// </summary>
    public static class GameArt
    {
        const string Root = "Visuals/";
        static bool loaded;
        static Texture2D tableBg, cardBack, cursor;
        static VideoClip menuVideo;
        static Font titleFont;
        const string PreferredTitleFont = "NightGeorgia (DEMO)";

        public static Texture2D TableBackground { get { Load(); return tableBg; } }
        public static Texture2D CardBack { get { Load(); return cardBack; } }
        public static Texture2D CursorTexture { get { Load(); return cursor; } }
        public static VideoClip MenuVideo { get { Load(); return menuVideo; } }

        /// <summary>First font file (.ttf/.otf) found in Resources/Visuals/Fonts, used for titles. Null if none.</summary>
        public static Font TitleFont { get { Load(); return titleFont; } }

        /// <summary>Switches a label to the title font when one is installed; otherwise leaves it unchanged.</summary>
        public static Text Titled(this Text t)
        {
            if (TitleFont != null) { t.font = TitleFont; t.fontStyle = FontStyle.Normal; }
            return t;
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            tableBg = Resources.Load<Texture2D>(Root + "Images/BG_Table");
            cardBack = Resources.Load<Texture2D>(Root + "Images/sleeve_weathered_cardback");
            // MenuLoop: the menu video re-encoded with proper colour tags and a keyframe every second, so Windows'
            // decoder neither warns about unknown colour primaries nor runs dry when the loop jumps back to the start.
            menuVideo = Resources.Load<VideoClip>(Root + "Videos/MenuLoop");
            if (menuVideo == null) menuVideo = Resources.Load<VideoClip>(Root + "Videos/TableBG");
            // Preferred title font by file name (without extension); otherwise the first font in the folder.
            titleFont = Resources.Load<Font>(Root + "Fonts/" + PreferredTitleFont);
            if (titleFont == null)
            {
                var fonts = Resources.LoadAll<Font>(Root + "Fonts");
                if (fonts != null && fonts.Length > 0) titleFont = fonts[0];
            }
            var cur = Resources.Load<Texture2D>(Root + "Images/Cursor32");
            if (cur != null) cursor = MakeReadable(cur);
            if (cardBack != null) { cardBack.wrapMode = TextureWrapMode.Clamp; }
        }

        /// <summary>Applies the custom mouse cursor (tip of the arrow at the top-left).</summary>
        public static void ApplyCursor()
        {
            var tex = CursorTexture;
            if (tex == null) return;
            Cursor.SetCursor(tex, new Vector2(2, 2), CursorMode.Auto);
        }

        /// <summary>
        /// Cursor textures must be readable RGBA32 without mipmaps. Instead of depending on the image's import
        /// settings, copy it through the GPU into a texture that meets those rules.
        /// </summary>
        static Texture2D MakeReadable(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}

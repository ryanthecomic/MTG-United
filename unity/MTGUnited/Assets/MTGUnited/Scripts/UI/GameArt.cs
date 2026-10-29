using UnityEngine;
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

        public static Texture2D TableBackground { get { Load(); return tableBg; } }
        public static Texture2D CardBack { get { Load(); return cardBack; } }
        public static Texture2D CursorTexture { get { Load(); return cursor; } }
        public static VideoClip MenuVideo { get { Load(); return menuVideo; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            tableBg = Resources.Load<Texture2D>(Root + "Images/BG_Table");
            cardBack = Resources.Load<Texture2D>(Root + "Images/sleeve_weathered_cardback");
            menuVideo = Resources.Load<VideoClip>(Root + "Videos/TableBG");
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

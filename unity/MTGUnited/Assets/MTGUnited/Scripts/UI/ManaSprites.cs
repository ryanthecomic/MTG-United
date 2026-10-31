using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// Mana symbols from Forge's own skin (sprite_manaicons.png and friends, GPL-3 like Forge), shipped in
    /// StreamingAssets/Mana and sliced with the rectangles from Forge's FSkinProp. Loaded byte-for-byte (no Unity
    /// import resizing). If the files are missing, Get returns null and the drawn symbols are used instead.
    /// Codes: W U B R G C, 0–20, X Y Z, S (snow), T (tap), Q (untap), P, W/P, W/U, 2/W, C/W, W/U/P.
    /// </summary>
    public static class ManaSprites
    {
        static bool loaded;
        static Texture2D mana, hybridColorless, phyrexian;
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        static readonly Dictionary<string, (int sheet, int x, int y, int w, int h)> rects = new Dictionary<string, (int, int, int, int, int)>();

        static void R(string code, int x, int y, int sheet = 0, int size = 80) => rects[code] = (sheet, x, y, size, size);

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            mana = LoadPng("sprite_manaicons.png");
            hybridColorless = LoadPng("sprite_hybrid_colorless.png");
            phyrexian = LoadPng("sprite_phyrexian.png");

            // sprite_manaicons.png (FSkinProp, PropType.MANAICONS)
            R("B", 166, 2); R("R", 330, 2); R("C", 248, 2); R("U", 330, 84); R("G", 166, 84); R("W", 412, 84);
            R("2/B", 166, 494); R("2/G", 248, 494); R("2/R", 330, 494); R("2/U", 412, 494); R("2/W", 166, 576);
            R("B/G", 166, 166); R("B/R", 248, 166); R("G/U", 166, 248); R("G/W", 248, 248); R("R/G", 166, 330);
            R("R/W", 248, 330); R("U/B", 330, 330); R("U/R", 412, 330); R("W/B", 330, 412); R("W/U", 412, 412);
            R("P", 166, 822); R("U/P", 330, 248); R("W/P", 412, 248); R("R/P", 412, 166); R("G/P", 330, 166); R("B/P", 248, 84);
            R("S", 412, 2); R("T", 166, 412); R("Q", 248, 412);
            R("X", 248, 576); R("Y", 330, 576); R("Z", 412, 576);
            int[,] numbers =
            {
                { 2, 2 }, { 84, 2 }, { 2, 84 }, { 84, 84 }, { 2, 166 }, { 84, 166 }, { 2, 248 }, { 84, 248 }, { 2, 330 }, { 84, 330 },
                { 2, 412 }, { 84, 412 }, { 2, 494 }, { 84, 494 }, { 2, 576 }, { 84, 576 }, { 2, 658 }, { 84, 658 }, { 166, 658 },
                { 248, 658 }, { 330, 658 },
            };
            for (int i = 0; i <= 20; i++) R(i.ToString(), numbers[i, 0], numbers[i, 1]);

            // sprite_hybrid_colorless.png (COLORLESS_HYBRID, 199 px)
            R("C/W", 2, 2, 1, 199); R("C/U", 219, 2, 1, 199); R("C/B", 439, 2, 1, 199); R("C/R", 657, 2, 1, 199); R("C/G", 878, 2, 1, 199);

            // sprite_phyrexian.png (PHYREXIAN hybrids, 199 px)
            R("W/U/P", 2, 2, 2, 199); R("U/B/P", 219, 2, 2, 199); R("B/R/P", 439, 2, 2, 199); R("R/W/P", 657, 2, 2, 199);
            R("G/W/P", 878, 2, 2, 199); R("W/B/P", 2, 221, 2, 199); R("U/R/P", 219, 222, 2, 199); R("B/G/P", 439, 222, 2, 199);
            R("R/G/P", 657, 221, 2, 199); R("G/U/P", 876, 219, 2, 199);
        }

        static Texture2D LoadPng(string file)
        {
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "Mana", file);
                if (!File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!t.LoadImage(File.ReadAllBytes(path), true)) return null;   // no CPU copy kept
                t.filterMode = FilterMode.Trilinear;
                t.wrapMode = TextureWrapMode.Clamp;
                t.anisoLevel = 2;
                return t;
            }
            catch { return null; }
        }

        /// <summary>Forge's picture of a mana symbol, or null when there isn't one (the caller draws its own).</summary>
        public static Sprite Get(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;
            Load();
            code = code.ToUpperInvariant();
            if (cache.TryGetValue(code, out var s)) return s;
            s = Make(code);
            if (s == null)
            {
                // Hybrids come in either order ("U/W" is drawn as "W/U").
                var parts = code.Split('/');
                if (parts.Length == 2 && parts[1] != "P") s = Make(parts[1] + "/" + parts[0]);
                else if (parts.Length == 3 && parts[2] == "P") s = Make(parts[1] + "/" + parts[0] + "/P");
            }
            cache[code] = s;
            return s;
        }

        static Sprite Make(string code)
        {
            if (!rects.TryGetValue(code, out var r)) return null;
            var tex = r.sheet == 0 ? mana : r.sheet == 1 ? hybridColorless : phyrexian;
            if (tex == null) return null;
            // Forge measures from the top-left; Unity from the bottom-left.
            var rect = new Rect(r.x, tex.height - r.y - r.h, r.w, r.h);
            if (rect.xMax > tex.width || rect.yMin < 0) return null;
            return Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }
    }
}

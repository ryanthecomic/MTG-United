using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// A Text that draws mana and tap symbols inline: "{2}{R}{G}", "{T}: Add {C}{C}" become small round symbols
    /// instead of brace codes. Each code is swapped for an invisible placeholder letter tagged with a marker
    /// colour; after the text is laid out the marked glyphs are found in the mesh and a symbol is placed on each.
    /// Text without '{' takes the normal Text path with no extra cost. Symbols are pooled per label.
    /// </summary>
    public class SymbolText : Text
    {
        /// <summary>Off for input fields, where the raw text must stay editable.</summary>
        public bool Symbols = true;

        // ---------------------------------------------------------------- text size (Options > text size)
        static readonly HashSet<SymbolText> all = new HashSet<SymbolText>();
        int baseSize = -1, baseMin, baseMax;

        /// <summary>Re-applies the text size option to every label (bases are the sizes the code asked for).</summary>
        public static void RescaleAll() { foreach (var t in all) if (t != null) t.ApplyScale(); }

        protected override void Awake() { base.Awake(); all.Add(this); }
        protected override void OnDestroy() { all.Remove(this); base.OnDestroy(); }

        // Captured on the first frame, after the creating code has set its sizes and best-fit limits.
        protected override void Start() { base.Start(); ApplyScale(); }

        /// <summary>Set by labels that should drop lines that don't fit (logs) instead of spilling over.</summary>
        public bool KeepTruncate;

        void ApplyScale()
        {
            // A line taller than its box is dropped entirely by Truncate: with bigger text that made names vanish.
            // Spill over instead (best-fit labels shrink to fit and keep their setting).
            if (!KeepTruncate && !resizeTextForBestFit && verticalOverflow == VerticalWrapMode.Truncate) verticalOverflow = VerticalWrapMode.Overflow;
            if (baseSize < 0)
            {
                baseSize = fontSize; baseMin = resizeTextMinSize; baseMax = resizeTextMaxSize;
                // Titles in the display font (Georgia) read small next to the body text: 2 points bigger.
                if (GameArt.TitleFont != null && font == GameArt.TitleFont) { baseSize += 2; baseMax += 2; }
            }
            float k = UiScale.TextScale;
            fontSize = Mathf.Max(6, Mathf.RoundToInt(baseSize * k));
            resizeTextMaxSize = Mathf.Max(6, Mathf.RoundToInt(baseMax * k));
            resizeTextMinSize = Mathf.Max(4, Mathf.RoundToInt(baseMin * k));
        }

        string raw = "";
        readonly List<string> codes = new List<string>();
        readonly List<(Vector2 center, float size, int code)> slots = new List<(Vector2, float, int)>();
        readonly List<SymbolView> pool = new List<SymbolView>();
        bool slotsDirty;

        /// <summary>
        /// Reads return the laid-out string (Text's own mesh code reads this property); writes take the plain
        /// string with {codes}. Writing back what was read is a no-op, so symbols survive "t.text = t.text".
        /// </summary>
        public override string text
        {
            get => base.text;
            set
            {
                value = value ?? "";
                if (value == raw || value == base.text) return;
                raw = value;
                base.text = Symbols ? Convert(Shown(value)) : value;
            }
        }

        /// <summary>Off for text that must never be translated (card names and rules text, typed input).</summary>
        public bool Localize = true;

        /// <summary>Re-applies the interface language to every label (Options > language).</summary>
        public static void RelocalizeAll() { foreach (var t in all) if (t != null) t.Relocalize(); }

        void Relocalize()
        {
            if (!Symbols || string.IsNullOrEmpty(raw)) return;
            base.text = Convert(Shown(raw));
        }

        /// <summary>What a label displays: set tags hidden (unless Options say otherwise), then translated.</summary>
        string Shown(string value)
        {
            value = SetTags.Clean(value);
            return Localize ? Loc.T(value) : value;
        }

        /// <summary>The text as it was set, with {codes} intact.</summary>
        public string RawText => raw;

        string Convert(string s)
        {
            codes.Clear();
            if (s.IndexOf('{') < 0) return s;
            var sb = new StringBuilder(s.Length + 32);
            int i = 0;
            while (i < s.Length)
            {
                if (s[i] == '{')
                {
                    int end = s.IndexOf('}', i + 1);
                    if (end > i && end - i <= 9)
                    {
                        string code = s.Substring(i + 1, end - i - 1).ToUpperInvariant();
                        if (ManaSymbols.Known(code) && codes.Count < 250)
                        {
                            // Invisible "M" whose colour carries the code index: #01 02 <index> 00.
                            sb.Append("<color=#0102").Append(codes.Count.ToString("X2")).Append("00>M</color>");
                            codes.Add(code);
                            i = end + 1;
                            continue;
                        }
                    }
                }
                sb.Append(s[i]);
                i++;
            }
            if (codes.Count > 0) supportRichText = true;
            return sb.ToString();
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            base.OnPopulateMesh(toFill);
            slots.Clear();
            if (codes.Count > 0)
            {
                var v = new UIVertex();
                int n = toFill.currentVertCount;
                for (int q = 0; q + 3 < n; q += 4)
                {
                    toFill.PopulateUIVertex(ref v, q);
                    var c = v.color;
                    if (c.r != 1 || c.g != 2 || c.a != 0) continue;
                    Vector2 min = v.position, max = v.position;
                    for (int k = 1; k < 4; k++)
                    {
                        toFill.PopulateUIVertex(ref v, q + k);
                        min = Vector2.Min(min, v.position); max = Vector2.Max(max, v.position);
                    }
                    float size = Mathf.Max(max.x - min.x, (max.y - min.y)) * 1.12f;
                    slots.Add(((min + max) * 0.5f, size, c.b));
                }
            }
            slotsDirty = true;
        }

        void LateUpdate()
        {
            if (!slotsDirty) return;
            slotsDirty = false;
            int used = 0;
            foreach (var (center, size, code) in slots)
            {
                if (code >= codes.Count) continue;
                if (used >= pool.Count) pool.Add(SymbolView.Create(transform));
                var sv = pool[used++];
                sv.Show(codes[code], center, size, rectTransform.pivot, color.a);
            }
            for (int i = used; i < pool.Count; i++) pool[i].Hide();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            slotsDirty = true;
        }

        /// <summary>One inline symbol: a coloured disc (two halves for hybrid) and a short label or icon.</summary>
        class SymbolView
        {
            RectTransform root;
            Image disc, half, icon;
            Text label;

            public static SymbolView Create(Transform parent)
            {
                var s = new SymbolView();
                var go = new GameObject("Symbol", typeof(RectTransform));
                go.transform.SetParent(parent, false);
                s.root = (RectTransform)go.transform;
                s.disc = go.AddComponent<Image>();
                s.disc.sprite = Icons.Circle; s.disc.raycastTarget = false;
                var h = new GameObject("Half", typeof(RectTransform));
                h.transform.SetParent(go.transform, false);
                ((RectTransform)h.transform).Stretch();
                s.half = h.AddComponent<Image>();
                s.half.sprite = Icons.Circle; s.half.raycastTarget = false;
                s.half.type = Image.Type.Filled; s.half.fillMethod = Image.FillMethod.Horizontal; s.half.fillOrigin = 1; s.half.fillAmount = 0.5f;
                var ic = new GameObject("Icon", typeof(RectTransform));
                ic.transform.SetParent(go.transform, false);
                ((RectTransform)ic.transform).Stretch();
                s.icon = ic.AddComponent<Image>();
                s.icon.raycastTarget = false; s.icon.preserveAspect = true;
                var lt = new GameObject("Label", typeof(RectTransform));
                lt.transform.SetParent(go.transform, false);
                ((RectTransform)lt.transform).Stretch();
                s.label = lt.AddComponent<Text>();   // plain Text: never recurses into symbols
                s.label.font = UIKit.Heading != null ? UIKit.Heading : UIKit.Font;
                s.label.alignment = TextAnchor.MiddleCenter;
                s.label.raycastTarget = false;
                s.label.horizontalOverflow = HorizontalWrapMode.Overflow;
                s.label.verticalOverflow = VerticalWrapMode.Overflow;
                return s;
            }

            public void Show(string code, Vector2 center, float size, Vector2 parentPivot, float alpha)
            {
                root.gameObject.SetActive(true);
                root.anchorMin = root.anchorMax = parentPivot;   // anchored position = mesh position (pivot space)
                root.sizeDelta = new Vector2(size, size);
                root.anchoredPosition = center;
                // Forge's own symbol art when there is one.
                var forge = ManaSprites.Get(code);
                if (forge != null)
                {
                    disc.sprite = forge;
                    disc.color = new Color(1, 1, 1, alpha);
                    half.enabled = false; icon.enabled = false; label.enabled = false;
                    return;
                }
                disc.sprite = Icons.Circle;
                var look = ManaSymbols.Look(code);
                disc.color = WithAlpha(look.a, alpha);
                half.enabled = look.hybrid;
                half.color = WithAlpha(look.b, alpha);
                icon.enabled = look.icon != null;
                icon.sprite = look.icon;
                icon.color = WithAlpha(look.ink, alpha);
                ((RectTransform)icon.transform).Stretch(size * 0.17f, size * 0.17f, size * 0.17f, size * 0.17f);
                label.enabled = look.icon == null;
                label.text = look.text;
                label.color = WithAlpha(look.ink, alpha);
                label.fontSize = Mathf.Max(6, Mathf.RoundToInt(size * (look.text.Length > 1 ? 0.5f : 0.66f)));
            }

            public void Hide() { if (root.gameObject.activeSelf) root.gameObject.SetActive(false); }

            static Color WithAlpha(Color c, float a) { c.a *= a; return c; }
        }
    }

    /// <summary>The symbol set: colours and labels for mana, hybrid, Phyrexian, generic, X, tap, untap, snow, energy.</summary>
    public static class ManaSymbols
    {
        public struct LookInfo { public Color a, b, ink; public bool hybrid; public string text; public Sprite icon; }

        static readonly Color W = new Color(0.98f, 0.96f, 0.84f), U = new Color(0.62f, 0.82f, 0.97f), B = new Color(0.72f, 0.68f, 0.68f),
                              R = new Color(0.96f, 0.6f, 0.5f), G = new Color(0.6f, 0.83f, 0.62f), Generic = new Color(0.8f, 0.78f, 0.76f),
                              Ink = new Color(0.1f, 0.08f, 0.08f);

        static Color Of(char c)
        {
            switch (c)
            {
                case 'W': return W; case 'U': return U; case 'B': return B; case 'R': return R; case 'G': return G;
                default: return Generic;
            }
        }

        public static bool Known(string code)
        {
            if (code.Length == 0) return false;
            if (int.TryParse(code, out _)) return true;
            switch (code)
            {
                case "W": case "U": case "B": case "R": case "G": case "C": case "X": case "Y": case "Z":
                case "T": case "Q": case "S": case "E": case "P": case "CHAOS": case "H": case "TK": case "A":
                    return true;
            }
            // Hybrid / Phyrexian / two-generic hybrid: W/U, 2/W, R/P, G/U/P, HR.
            foreach (char ch in code) if ("WUBRGCP2/H".IndexOf(ch) < 0) return false;
            return true;
        }

        public static LookInfo Look(string code)
        {
            var l = new LookInfo { a = Generic, b = Generic, ink = Ink, text = code };
            if (int.TryParse(code, out _)) return l;
            switch (code)
            {
                case "W": case "U": case "B": case "R": case "G":
                    l.a = Of(code[0]); l.text = code; return l;
                case "C": l.a = new Color(0.86f, 0.84f, 0.88f); l.text = "C"; return l;
                case "T": l.icon = Icons.Tap; l.text = ""; return l;
                case "Q": l.icon = Icons.Untap; l.text = ""; l.a = new Color(0.25f, 0.25f, 0.28f); l.ink = Color.white; return l;
                case "S": l.a = new Color(0.85f, 0.92f, 1f); l.text = "S"; return l;
                case "E": l.a = new Color(0.3f, 0.3f, 0.34f); l.ink = new Color(1f, 0.85f, 0.4f); l.text = "E"; return l;
            }
            string[] parts = code.Split('/');
            if (code.Length == 2 && code[0] == 'H') { l.a = Of(code[1]); l.text = "½"; return l; }
            if (parts.Length >= 2)
            {
                bool phyrexian = parts[parts.Length - 1] == "P";
                if (phyrexian && parts.Length == 2) { l.a = Of(parts[0][0]); l.text = "P"; return l; }
                l.hybrid = true;
                l.a = parts[0] == "2" ? Generic : Of(parts[0][0]);
                l.b = Of(parts[1][0]);
                l.text = parts[0] == "2" ? "2" : "";
                if (phyrexian) l.text = "P";
                return l;
            }
            return l;
        }
    }

    /// <summary>
    /// Forge names AI decks like "Tramplesaurus Rex [FDC] [2026]". Players don't need the set code and year, so
    /// those tags are hidden everywhere (Options → Gameplay → Show set codes brings them back).
    /// </summary>
    public static class SetTags
    {
        static readonly System.Text.RegularExpressions.Regex Tags =
            new System.Text.RegularExpressions.Regex(@"\s*\[(?:[A-Z0-9]{2,5}|\d{4})\]");

        static int show = -1;
        public static bool Show
        {
            get { if (show < 0) show = PlayerPrefs.GetInt("showSetCodes", 0); return show == 1; }
            set { show = value ? 1 : 0; PlayerPrefs.SetInt("showSetCodes", show); SymbolText.RelocalizeAll(); }
        }

        public static string Clean(string s)
        {
            if (Show || string.IsNullOrEmpty(s) || s.IndexOf('[') < 0) return s;
            return Tags.Replace(s, "");
        }
    }
}

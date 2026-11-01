using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Home carousel pictures and the GitHub slide.
    /// Pictures come from Resources/Visuals/Carousel:
    ///  • forge.png / kitchen.png / github.png — the background of that slide (any size; it's cropped to fill);
    ///  • any other picture — a slide of its own, just the picture. A name like "07 Title of the slide" sorts it
    ///    and gives it a caption (the number is dropped); "-" in the name is shown as a space.
    /// Without pictures the slides keep their dark gradients.
    /// </summary>
    public partial class MainMenu
    {
        const string CarouselFolder = "Visuals/Carousel";

        /// <summary>
        /// Puts a picture behind a slide's content, cropped to fill, with a dark fade so text stays readable:
        /// Resources/Visuals/Carousel/&lt;name&gt; if present, else the built-in art (focusY keeps that height in view).
        /// Hides the slide's faint icon when there's a picture. Returns true if one was used.
        /// </summary>
        bool SlideImage(RectTransform slide, string name, string builtIn = null, float focusY = 0.5f, GameObject icon = null)
        {
            var tex = Resources.Load<Texture2D>(CarouselFolder + "/" + name);
            if (tex == null && builtIn != null) tex = Resources.Load<Texture2D>(builtIn); else focusY = 0.5f;
            if (tex == null) return false;
            AddCover(slide, tex, 0.78f, focusY);
            if (icon != null) icon.SetActive(false);
            return true;
        }

        // Built-in slide art (credited in README.md).
        const string ForgeArt = "Visuals/Images/Urabrasks-Forge-2-Phyrexia-All-Will-Be-One-MtG-Art";   // Lie Setiawan
        const string KitchenArt = "Visuals/Images/Trivia-Contest-Unfinity-MtG-Art";                      // Caroline Gariba

        static void AddCover(RectTransform slide, Texture2D tex, float fade, float focusY = 0.5f)
        {
            const float W = 1150, H = 690;
            var pic = UIKit.Rect("Picture", slide);
            pic.anchorMin = pic.anchorMax = new Vector2(0.5f, 0.5f);
            float k = Mathf.Max(W / tex.width, H / tex.height);   // cover: fills the slide, keeps the picture's shape
            float w = tex.width * k, h = tex.height * k;
            pic.sizeDelta = new Vector2(w, h);
            // Keep the interesting part (focusY: 0 = bottom, 1 = top of the picture) in view.
            float dy = Mathf.Clamp((0.5f - focusY) * h, -(h - H) / 2f, (h - H) / 2f);
            pic.anchoredPosition = new Vector2(0, dy);
            var img = pic.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            pic.SetAsFirstSibling();
            if (fade <= 0f) return;
            // Darker toward the left and bottom, where the text sits.
            var shade = UIKit.Rect("Fade", slide).Stretch();
            var si = shade.gameObject.AddComponent<RawImage>();
            si.texture = UIKit.Gradient(new Color(0, 0, 0, fade * 0.35f), new Color(0, 0, 0, fade));
            si.raycastTarget = false;
            shade.SetSiblingIndex(1);
        }

        void BuildGitHubSlide(RectTransform hero)
        {
            var gh = UIKit.Rect("GitHubSlide", hero).Stretch();
            var bg = gh.gameObject.AddComponent<RawImage>();
            bg.texture = UIKit.Gradient(new Color(0.09f, 0.09f, 0.14f), new Color(0.03f, 0.03f, 0.05f));
            gh.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); Application.OpenURL(MenuKit.GitHub); };
            var mark = UIKit.Rect("Mark", gh);
            mark.Place(700, 150, 420, 420);
            var mi = mark.gameObject.AddComponent<Image>();
            mi.sprite = Icons.Star; mi.color = new Color(0.75f, 0.8f, 1f, 0.1f); mi.raycastTarget = false; mi.preserveAspect = true;
            var k = UIKit.Label("Kicker", gh, "OPEN SOURCE", 16, new Color(0.7f, 0.78f, 1f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            k.rectTransform.Place(50, 590, 600, 30);
            var t = UIKit.Label("Title", gh, "MTG United on GitHub", 56, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.5f).Titled();
            t.rectTransform.Place(46, 510, 900, 80);
            var b = UIKit.Label("Body", gh,
                "Follow the project, grab new versions, report bugs and suggest features. Stars help a lot!",
                21, new Color(0.86f, 0.88f, 0.94f), TextAnchor.UpperLeft);
            b.rectTransform.Place(52, 360, 640, 140);
            var url = UIKit.Label("Url", gh, "github.com/ryanthecomic/MTG-United", 17, new Color(0.6f, 0.68f, 0.9f), TextAnchor.UpperLeft);
            url.rectTransform.Place(52, 160, 640, 30);
            if (url is SymbolText us) us.Localize = false;
            UIKit.Pill("Open", gh, "Open GitHub", new Color(0.22f, 0.3f, 0.62f), () => Application.OpenURL(MenuKit.GitHub), 50, 70, 320, 70, 28);
            var bug = UIKit.Button("Bug", gh, "REPORT A BUG", Theme.Secondary, () => Application.OpenURL(MenuKit.Issues), 18);
            ((RectTransform)bug.transform).Place(400, 80, 240, 52);
            slides.Insert(0, gh);   // GitHub comes first
            // Banner: Resources/Visuals/Carousel/github if there is one, else Jodah, the Unifier (his face near the top).
            var custom = Resources.Load<Texture2D>(CarouselFolder + "/github");
            var art = custom != null ? custom : MenuKit.JodahArt;
            if (art != null) { AddCover(gh, art, 0.8f, custom != null ? 0.5f : 0.8f); mark.gameObject.SetActive(false); }
        }

        /// <summary>Every other picture in the carousel folder becomes a slide of its own.</summary>
        void BuildImageSlides(RectTransform hero)
        {
            var all = Resources.LoadAll<Texture2D>(CarouselFolder);
            if (all == null || all.Length == 0) return;
            System.Array.Sort(all, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var tex in all)
            {
                string n = tex.name.ToLowerInvariant();
                if (n == "forge" || n == "kitchen" || n == "github") continue;
                var s = UIKit.Rect("Picture " + tex.name, hero).Stretch();
                s.gameObject.AddComponent<Image>().color = Color.black;
                string caption = SlideCaption(tex.name);
                AddCover(s, tex, caption.Length > 0 ? 0.55f : 0f);
                if (caption.Length > 0)
                {
                    var t = UIKit.Label("Caption", s, caption, 40, Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold).Outline(1.5f).Titled();
                    t.rectTransform.Place(46, 60, 1000, 80);
                }
                slides.Add(s);
            }
        }

        /// <summary>"07 Some-title" → "Some title"; a name that is only a number has no caption.</summary>
        static string SlideCaption(string file)
        {
            string s = file.Trim();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_' || s[i] == ' ' || s[i] == '.')) i++;
            return s.Substring(i).Replace('-', ' ').Trim();
        }
    }
}

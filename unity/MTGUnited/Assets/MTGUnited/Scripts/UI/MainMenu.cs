using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MTGUnited
{
    /// <summary>
    /// Front end: animated table background, a top navigation bar (Home / Decks / Profile), and a play cluster in
    /// the bottom-right corner showing the selected deck, pod size and a large PLAY button.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        GameController gc;
        RectTransform root, content;
        VideoPlayer video;
        RenderTexture videoTarget;

        // Navigation
        readonly Dictionary<string, (Text label, Image bar, Image icon, Image bg)> tabs = new Dictionary<string, (Text, Image, Image, Image)>();
        readonly Dictionary<string, RectTransform> pages = new Dictionary<string, RectTransform>();
        string currentTab = "home";
        Text connText;
        Image connDot;

        // Play cluster
        Text deckTitle, deckSub, modeText, oppText, aiText, statusText, launcherText;
        RawImage deckArt;
        Button playButton, launchButton;
        RectTransform playCluster;   // deck summary + PLAY; only shown on the Home tab
        Image aiPreconBtn, aiRandomBtn;

        // Decks page
        RectTransform deckGrid;
        List<SavedDeck> decks = new List<SavedDeck>();
        DeckBuilder builder;
        RectTransform decksFooter;
        float lastTileClick; string lastTileId;

        // Profile page
        InputField nameInput;
        Text statsText;

        int opponents;
        string aiDecks;
        string aiStrength;
        readonly Dictionary<string, Image> strengthBtns = new Dictionary<string, Image>();
        string selectedDeckId;
        bool connected;
        RawImage heroArt;
        Text deckCountText;
        string playMode;
        Image modeForgeBtn, modeTableBtn;
        Text skillTitle, tableHint;
        GameObject lanButton;
        readonly List<GameObject> forgeOnly = new List<GameObject>();
        CustomSetsPanel customSets;

        public bool IsOpen => gameObject.activeSelf;

        public static MainMenu Create(RectTransform parent, GameController gc)
        {
            var bg = UIKit.Rect("MainMenu", parent).Stretch();
            var m = bg.gameObject.AddComponent<MainMenu>();
            m.gc = gc;
            m.root = bg;
            m.opponents = PlayerPrefs.GetInt("opponents", 3);
            m.aiDecks = PlayerPrefs.GetString("aiDecks", "precon");
            m.aiStrength = PlayerPrefs.GetString("aiStrength", "normal");
            m.selectedDeckId = PlayerPrefs.GetString("selectedDeck", "");
            m.playMode = PlayerPrefs.GetString("playMode", "forge");
            m.decks = DeckStore.LoadAll();
            m.Build();
            return m;
        }

        // =================================================================================================== build

        void Build()
        {
            BuildBackground();

            // Board-space container so the layout matches the 1920x1080 design on any screen.
            var frame = UIKit.Rect("Frame", root);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(1920, 1080);

            content = UIKit.Rect("Pages", frame).Stretch(0, 0, 0, 96);
            BuildNavBar(frame);
            pages["home"] = BuildHome(content);
            pages["decks"] = BuildDecks(content);
            pages["profile"] = BuildProfile(content);
            pages["forge"] = ForgePage.Create(content, gc);
            BuildPlayCluster(frame);
            builder = DeckBuilder.Create(content, gc);
            customSets = CustomSetsPanel.Create(root, gc);

            SelectTab("home");
            RefreshPlayCluster();
            SetConnection(false);
        }

        void BuildBackground()
        {
            var bgRt = UIKit.Rect("Background", root).Stretch();
            var raw = bgRt.gameObject.AddComponent<RawImage>();
            raw.color = Color.white;
            var fitter = bgRt.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;

            if (GameArt.MenuVideo != null)
            {
                videoTarget = new RenderTexture(1920, 1080, 0);
                video = bgRt.gameObject.AddComponent<VideoPlayer>();
                video.clip = GameArt.MenuVideo;
                video.isLooping = true;
                video.renderMode = VideoRenderMode.RenderTexture;
                video.targetTexture = videoTarget;
                video.audioOutputMode = VideoAudioOutputMode.None;
                video.playOnAwake = true;
                raw.texture = GameArt.TableBackground != null ? (Texture)GameArt.TableBackground : videoTarget;
                // Show the still image until the first video frame is ready, then swap to the video.
                video.prepareCompleted += vp => { raw.texture = videoTarget; vp.Play(); };
                video.Prepare();
            }
            else if (GameArt.TableBackground != null)
            {
                raw.texture = GameArt.TableBackground;
            }
            else
            {
                raw.texture = UIKit.Gradient(Theme.BgTop, Theme.BgBottom);
            }

            // Darken the edges: strong at the top (nav bar) and bottom (play cluster), lighter in the middle.
            var vignette = UIKit.Rect("Vignette", root).Stretch();
            var v = vignette.gameObject.AddComponent<RawImage>();
            v.texture = VerticalVignette();
            v.raycastTarget = false;
        }

        static Texture2D VerticalVignette()
        {
            var t = new Texture2D(1, 128);
            for (int y = 0; y < 128; y++)
            {
                float f = y / 127f;                    // 0 bottom .. 1 top
                float edge = Mathf.Max(Mathf.Pow(1f - f, 3f) * 0.85f, Mathf.Pow(f, 4f) * 0.75f);
                t.SetPixel(0, y, new Color(0.02f, 0.02f, 0.04f, Mathf.Clamp01(0.18f + edge)));
            }
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        /// <summary>
        /// Top bar: icon-over-label tabs on the left (the active one lit, with a glowing underline), the bridge
        /// status and a settings gear on the right.
        /// </summary>
        void BuildNavBar(RectTransform frame)
        {
            var bar = UIKit.Rect("NavBar", frame);
            bar.Place(0, 1080 - 92, 1920, 92);
            var barBg = bar.gameObject.AddComponent<RawImage>();
            barBg.texture = UIKit.Gradient(new Color(0.16f, 0.17f, 0.2f, 0.97f), new Color(0.07f, 0.075f, 0.09f, 0.97f));
            UIKit.AddShadow(bar.transform, 20, new Vector2(0, -10), 0.7f);
            var edge = UIKit.Panel("Edge", bar, new Color(1f, 1f, 1f, 0.08f), false);
            edge.rectTransform.anchorMin = new Vector2(0, 0); edge.rectTransform.anchorMax = new Vector2(1, 0);
            edge.rectTransform.sizeDelta = new Vector2(0, 2); edge.rectTransform.anchoredPosition = Vector2.zero;
            edge.raycastTarget = false;

            string[] ids = { "home", "profile", "decks", "forge" };
            string[] labels = { "Home", "Profile", "Decks", "Forge" };
            Sprite[] icons = { Icons.Home, Icons.Person, Icons.Decks, Icons.Anvil };
            // Wordmark in the top-left corner, tabs after it.
            var logo = UIKit.Label("Logo", bar, "MTG UNITED", 30, new Color(0.95f, 0.75f, 0.45f), TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            logo.rectTransform.Place(28, 22, 260, 50);
            float x = 290;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var tab = UIKit.Rect("Tab " + id, bar);
                tab.Place(x, 0, 132, 92);
                var bg = tab.gameObject.AddComponent<Image>();
                bg.sprite = UIKit.White;
                bg.color = Color.clear;
                tab.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); SelectTab(id); };
                var ic = UIKit.Rect("Icon", tab); ic.Place(41, 38, 50, 46);
                var icon = ic.gameObject.AddComponent<Image>();
                icon.sprite = icons[i]; icon.preserveAspect = true; icon.raycastTarget = false;
                var lbl = UIKit.Label("Label", tab, labels[i], 22, Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold).Titled();
                lbl.rectTransform.Place(0, 6, 132, 32);
                var underline = UIKit.Panel("Underline", tab, new Color(1f, 0.45f, 0.12f));
                underline.rectTransform.anchorMin = new Vector2(0.06f, 0); underline.rectTransform.anchorMax = new Vector2(0.94f, 0);
                underline.rectTransform.sizeDelta = new Vector2(0, 5); underline.rectTransform.anchoredPosition = new Vector2(0, 3);
                underline.pixelsPerUnitMultiplier = 6f;
                underline.raycastTarget = false;
                var glow = UIKit.Panel("Glow", underline.transform, new Color(1f, 0.5f, 0.15f, 0.35f));
                glow.rectTransform.Stretch(-6, -6, -6, -6);
                glow.raycastTarget = false;
                tabs[id] = (lbl, underline, icon, bg);
                // Thin divider between tabs.
                var div = UIKit.Panel("Divider", bar, new Color(1, 1, 1, 0.07f), false);
                div.rectTransform.Place(x + 132, 8, 1, 76);
                div.raycastTarget = false;
                x += 133;
            }


            // Bridge status.
            var pill = UIKit.Panel("Connection", bar, new Color(0, 0, 0, 0.3f));
            pill.rectTransform.Place(1420, 26, 360, 40);
            pill.pixelsPerUnitMultiplier = 32f / 40f;
            connDot = UIKit.Panel("Dot", pill.transform, Theme.Danger);
            connDot.rectTransform.anchorMin = connDot.rectTransform.anchorMax = new Vector2(0, 0.5f);
            connDot.rectTransform.sizeDelta = new Vector2(12, 12); connDot.rectTransform.anchoredPosition = new Vector2(20, 0);
            connText = UIKit.Label("Text", pill.transform, "", 15, Theme.Text, TextAnchor.MiddleLeft);
            connText.rectTransform.Stretch(36, 0, 10, 0);

            // Settings gear (top right).
            UIKit.IconButton("Settings", bar, Icons.Gear, 1806, 16, 60, () => gc.Options.Open());
        }

        // =================================================================================================== home

        /// <summary>
        /// Home: a carousel in the big panel. Slide 1 is the Forge bridge (status, and a way into the Forge tab);
        /// slide 2 is the selected deck's commander art. Arrows, dots, and it advances on its own unless hovered.
        /// </summary>
        RectTransform BuildHome(RectTransform parent)
        {
            var page = UIKit.Rect("Home", parent).Stretch();

            var hero = UIKit.Panel("Hero", page, new Color(0.05f, 0.05f, 0.07f, 0.95f));
            hero.rectTransform.Place(70, 190, 1150, 690);
            hero.pixelsPerUnitMultiplier = 3f;
            UIKit.AddShadow(hero.transform, 26, new Vector2(0, -12), 0.7f);
            hero.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var hpe = hero.gameObject.AddComponent<PointerEvents>();
            hpe.OnEnter = () => heroHovered = true;
            hpe.OnExit = () => heroHovered = false;

            // ---- slide 1: Forge bridge
            var forge = UIKit.Rect("ForgeSlide", hero.transform).Stretch();
            var fbg = forge.gameObject.AddComponent<RawImage>();
            fbg.texture = UIKit.Gradient(new Color(0.16f, 0.1f, 0.06f), new Color(0.04f, 0.03f, 0.03f));
            forge.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); SelectTab("forge"); };
            var mark = UIKit.Rect("Anvil", forge);
            mark.Place(700, 150, 420, 420);
            var markImg = mark.gameObject.AddComponent<Image>();
            markImg.sprite = Icons.Anvil; markImg.color = new Color(1f, 0.6f, 0.25f, 0.13f); markImg.raycastTarget = false; markImg.preserveAspect = true;
            var fk = UIKit.Label("Kicker", forge, "RULES ENGINE", 16, new Color(1f, 0.7f, 0.35f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            fk.rectTransform.Place(50, 590, 600, 30);
            var ft = UIKit.Label("Title", forge, "Forge Bridge", 56, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.5f).Titled();
            ft.rectTransform.Place(46, 510, 700, 80);
            forgeStatus = UIKit.Label("Status", forge, "", 26, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            forgeStatus.rectTransform.Place(52, 440, 700, 60);
            forgeStatus.supportRichText = true;
            launcherText = UIKit.Label("Detail", forge, "", 17, Theme.TextDim, TextAnchor.UpperLeft);
            launcherText.rectTransform.Place(52, 300, 640, 130);
            var manage = UIKit.Pill("Manage", forge, "Manage Forge", new Color(0.62f, 0.33f, 0.12f), () => SelectTab("forge"), 50, 70, 320, 70, 28);
            launchButton = UIKit.Button("Launch", forge, "START FORGE", Theme.Secondary, () => gc.Launcher.Launch(), 18);
            ((RectTransform)launchButton.transform).Place(400, 80, 240, 52);

            // ---- slide 2: gather your pod (commander art)
            var pod = UIKit.Rect("PodSlide", hero.transform).Stretch();
            var artRt = UIKit.Rect("Art", pod);
            artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 0.5f);
            artRt.sizeDelta = new Vector2(1150, 1610);        // card-shaped; the banner shows the art band
            artRt.anchoredPosition = new Vector2(0, -260);
            heroArt = artRt.gameObject.AddComponent<RawImage>();
            heroArt.raycastTarget = false;
            var fade = UIKit.Rect("Fade", pod).Stretch();
            var fadeImg = fade.gameObject.AddComponent<RawImage>();
            fadeImg.texture = UIKit.Gradient(new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.55f));
            fadeImg.raycastTarget = false;
            var strip = UIKit.Panel("Caption", pod, new Color(0.03f, 0.03f, 0.05f, 0.82f), false);
            strip.rectTransform.anchorMin = new Vector2(0, 0); strip.rectTransform.anchorMax = new Vector2(1, 0);
            strip.rectTransform.pivot = new Vector2(0.5f, 0); strip.rectTransform.sizeDelta = new Vector2(0, 150);
            var title = UIKit.Label("Title", strip.transform, "Gather your pod", 44, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.5f).Titled();
            title.rectTransform.Stretch(30, 0, 30, 16);
            var body = UIKit.Label("Body", strip.transform,
                "Commander with full rules from the Forge engine, against up to five AI opponents. Pick a deck, set the pod, press Play.",
                20, new Color(0.88f, 0.9f, 0.95f), TextAnchor.UpperLeft);
            body.rectTransform.Stretch(32, 20, 40, 76);

            slides.Add(forge); slides.Add(pod);

            // ---- arrows + dots
            var left = UIKit.Button("Prev", hero.transform, "‹", new Color(0, 0, 0, 0.45f), () => ShowSlide(slide - 1), 40);
            ((RectTransform)left.transform).Place(14, 315, 50, 60);
            var right = UIKit.Button("Next", hero.transform, "›", new Color(0, 0, 0, 0.45f), () => ShowSlide(slide + 1), 40);
            ((RectTransform)right.transform).Place(1150 - 64, 315, 50, 60);
            for (int i = 0; i < slides.Count; i++)
            {
                int k = i;
                var dot = UIKit.Panel("Dot", hero.transform, Color.white);
                dot.rectTransform.Place(1150 / 2f - slides.Count * 14 + i * 28, 14, 14, 14);
                dot.pixelsPerUnitMultiplier = 32f / 14f;
                dot.gameObject.AddComponent<PointerEvents>().OnClick = () => ShowSlide(k);
                slideDots.Add(dot);
            }
            ShowSlide(0, true);
            return page;
        }

        readonly List<RectTransform> slides = new List<RectTransform>();
        readonly List<Image> slideDots = new List<Image>();
        int slide;
        float slideShownAt;
        bool heroHovered;
        Text forgeStatus;

        void ShowSlide(int i, bool instant = false)
        {
            int n = slides.Count;
            i = ((i % n) + n) % n;
            for (int k = 0; k < n; k++)
            {
                bool on = k == i;
                slides[k].gameObject.SetActive(on);
                slideDots[k].color = on ? new Color(1f, 0.75f, 0.4f) : new Color(1f, 1f, 1f, 0.35f);
            }
            // Cross-fade with a short slide in from the side it came from (travel, within budget).
            if (!instant) FadeIn.Play(slides[i].gameObject, Motion.Travel, 1f, new Vector2(i > slide || (slide == n - 1 && i == 0) ? 50f : -50f, 0));
            slide = i;
            slideShownAt = Time.unscaledTime;
        }

        // =================================================================================================== decks

        RectTransform BuildDecks(RectTransform parent)
        {
            var page = UIKit.Rect("Decks", parent).Stretch();
            var shade = UIKit.Panel("Shade", page, new Color(0.02f, 0.02f, 0.04f, 0.45f), false);
            shade.rectTransform.Stretch();
            shade.raycastTarget = false;

            var title = UIKit.Label("Title", page, "My Decks", 54, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            title.rectTransform.Place(80, 860, 800, 80);
            deckCountText = UIKit.Label("Count", page, "", 38, new Color(0.88f, 0.86f, 0.8f), TextAnchor.MiddleRight, FontStyle.Bold).Titled();
            deckCountText.rectTransform.Place(1500, 860, 340, 80);

            var scroll = UIKit.Rect("Scroll", page);
            scroll.Place(60, 140, 1800, 710);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = Color.clear;
            deckGrid = UIKit.Rect("Grid", scroll);
            deckGrid.anchorMin = new Vector2(0, 1); deckGrid.anchorMax = new Vector2(1, 1); deckGrid.pivot = new Vector2(0.5f, 1);
            var grid = deckGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(316, 300);
            grid.spacing = new Vector2(26, 30);
            grid.padding = new RectOffset(10, 10, 14, 14);
            deckGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = deckGrid; sr.horizontal = false; sr.scrollSensitivity = 40;
            sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);

            // Footer: delete on the left, Edit Deck on the right.
            decksFooter = UIKit.Rect("Footer", page).Stretch();
            UIKit.IconButton("Delete", decksFooter, Icons.Trash, 80, 34, 64, DeleteSelected);
            var sets = UIKit.Button("CustomSets", decksFooter, "CUSTOM SETS", new Color(0.55f, 0.3f, 0.12f), () => customSets.Open(), 18);
            ((RectTransform)sets.transform).Place(170, 40, 240, 54);
            UIKit.Pill("EditDeck", decksFooter, "Edit Deck", new Color(0.16f, 0.3f, 0.72f), () => OpenBuilder(FindDeck(selectedDeckId)), 1530, 30, 330, 80, 36);

            RebuildDeckGrid();
            return page;
        }

        void RebuildDeckGrid()
        {
            foreach (Transform c in deckGrid) Destroy(c.gameObject);
            deckCountText.text = decks.Count + (decks.Count == 1 ? " deck" : " decks");

            // "New deck" tile
            var add = UIKit.Panel("New", deckGrid, new Color(0.03f, 0.03f, 0.05f, 0.9f));
            add.pixelsPerUnitMultiplier = 1.2f;
            add.gameObject.AddComponent<PointerEvents>().OnClick = () =>
            {
                Sound.Play(Sfx.UiClick, 0.6f);
                OpenBuilder(new SavedDeck { Name = "New Deck" });
            };
            var plusRt = UIKit.Rect("Plus", add.transform);
            plusRt.anchorMin = plusRt.anchorMax = new Vector2(0.5f, 0.5f); plusRt.sizeDelta = new Vector2(150, 150);
            plusRt.anchoredPosition = new Vector2(0, 14);
            var plusImg = plusRt.gameObject.AddComponent<Image>();
            plusImg.sprite = Icons.Plus; plusImg.color = new Color(0.9f, 0.88f, 1f); plusImg.raycastTarget = false;
            var plusGlow = UIKit.Rect("Glow", add.transform);
            plusGlow.anchorMin = plusGlow.anchorMax = new Vector2(0.5f, 0.5f); plusGlow.sizeDelta = new Vector2(190, 190);
            plusGlow.anchoredPosition = new Vector2(0, 14); plusGlow.SetSiblingIndex(0);
            var pg = plusGlow.gameObject.AddComponent<Image>(); pg.sprite = Icons.Plus; pg.color = new Color(0.55f, 0.45f, 1f, 0.35f); pg.raycastTarget = false;
            var addLbl = UIKit.Label("Label", add.transform, "New Deck", 22, Theme.TextDim, TextAnchor.LowerCenter, FontStyle.Bold).Titled();
            addLbl.rectTransform.Stretch(0, 22, 0, 0);

            foreach (var d in decks) DeckBox(d);
        }

        /// <summary>A deck box: commander art, colour pips, and a gold-rimmed name banner. Click selects, double-click edits.</summary>
        void DeckBox(SavedDeck deck)
        {
            bool selected = deck.Id == selectedDeckId;
            var tile = UIKit.Rect("Deck", deckGrid);
            var hit = tile.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            tile.gameObject.AddComponent<PointerEvents>().OnClick = () => OnDeckBoxClick(deck);

            if (selected)
            {
                var glow = UIKit.Panel("Selected", tile, new Color(1f, 0.6f, 0.2f, 0.55f));
                glow.rectTransform.Stretch(-8, -8, -8, -8);
                glow.raycastTarget = false;
            }
            var box = UIKit.Panel("Box", tile, new Color(0.06f, 0.06f, 0.08f));
            box.rectTransform.Stretch(0, 46, 0, 0);
            box.raycastTarget = false;
            box.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var artRt = UIKit.Rect("Art", box.transform);
            artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 0.5f);
            artRt.sizeDelta = new Vector2(316, 442); artRt.anchoredPosition = new Vector2(0, -60);
            var art = artRt.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.texture = GameArt.CardBack;
            // Box side shading on the right edge, like a closed deck box.
            var side = UIKit.Rect("Side", box.transform);
            side.anchorMin = new Vector2(1, 0); side.anchorMax = new Vector2(1, 1); side.pivot = new Vector2(1, 0.5f);
            side.sizeDelta = new Vector2(26, 0);
            var sideImg = side.gameObject.AddComponent<Image>(); sideImg.sprite = UIKit.White;
            sideImg.color = new Color(0.04f, 0.04f, 0.05f, 0.85f); sideImg.raycastTarget = false;

            var pips = UIKit.Rect("Pips", box.transform);
            pips.anchorMin = pips.anchorMax = new Vector2(0.5f, 0); pips.pivot = new Vector2(0.5f, 0);
            pips.sizeDelta = new Vector2(200, 30); pips.anchoredPosition = new Vector2(-13, 8);

            var banner = UIKit.Panel("Banner", tile, new Color(0.06f, 0.05f, 0.04f, 0.98f));
            banner.rectTransform.anchorMin = new Vector2(0, 0); banner.rectTransform.anchorMax = new Vector2(1, 0);
            banner.rectTransform.pivot = new Vector2(0.5f, 0); banner.rectTransform.sizeDelta = new Vector2(0, 54);
            banner.rectTransform.anchoredPosition = new Vector2(0, 4);
            banner.pixelsPerUnitMultiplier = 32f / 54f;
            banner.raycastTarget = false;
            var rim = banner.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = selected ? new Color(1f, 0.7f, 0.3f) : new Color(0.85f, 0.65f, 0.28f, 0.85f);
            rim.effectDistance = new Vector2(2, -2);
            var name = UIKit.Label("Name", banner.transform, deck.Name, 23, new Color(1f, 0.86f, 0.55f), TextAnchor.MiddleCenter, FontStyle.Bold).Titled();
            name.rectTransform.Stretch(18, 0, 18, 0);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 14; name.resizeTextMaxSize = 23;

            var count = UIKit.Label("Count", box.transform, deck.CardCount + "", 15, Color.white, TextAnchor.UpperRight, FontStyle.Bold).Outline(1.5f);
            count.rectTransform.Stretch(0, 0, 36, 10);

            var model = DeckModel.From(deck);
            string cmd = model.Commanders.Count > 0 ? model.Commanders[0] : null;
            if (cmd != null && CardImageCache.Instance != null)
                CardImageCache.Instance.Get(cmd, null, tex => { if (art != null) art.texture = tex; });
            if (model.Commanders.Count > 0)
                CardCatalog.Ensure(model.Commanders, () => { if (pips != null) FillPips(pips, model.Commanders); });
        }

        /// <summary>Colour identity pips under the art (from the commanders' colours).</summary>
        static void FillPips(RectTransform pips, List<string> commanders)
        {
            foreach (Transform c in pips) Destroy(c.gameObject);
            string colors = "";
            foreach (var n in commanders)
            {
                var info = CardCatalog.Get(n);
                if (info == null) continue;
                foreach (char ch in info.Colors ?? "") if (colors.IndexOf(ch) < 0) colors += ch;
            }
            string order = "WUBRG";
            var sorted = new List<char>();
            foreach (char ch in order) if (colors.IndexOf(ch) >= 0) sorted.Add(ch);
            float x = -(sorted.Count - 1) * 15f;
            foreach (char ch in sorted)
            {
                var pip = UIKit.Rect("Pip", pips);
                pip.anchorMin = pip.anchorMax = new Vector2(0.5f, 0.5f); pip.sizeDelta = new Vector2(28, 28);
                pip.anchoredPosition = new Vector2(x, 0);
                var ring = pip.gameObject.AddComponent<Image>(); ring.sprite = Icons.Circle; ring.color = new Color(0.05f, 0.05f, 0.05f); ring.raycastTarget = false;
                var fill = UIKit.Rect("Fill", pip).Stretch(3, 3, 3, 3).gameObject.AddComponent<Image>();
                fill.sprite = Icons.Circle; fill.color = Theme.ForManaColor(ch.ToString()); fill.raycastTarget = false;
                x += 30f;
            }
        }

        void OnDeckBoxClick(SavedDeck deck)
        {
            Sound.Play(Sfx.UiClick, 0.6f);
            bool dbl = lastTileId == deck.Id && Time.unscaledTime - lastTileClick < 0.35f;
            lastTileId = deck.Id; lastTileClick = Time.unscaledTime;
            if (dbl) { OpenBuilder(deck); return; }
            if (selectedDeckId != deck.Id) SelectDeck(deck.Id);
        }

        void OpenBuilder(SavedDeck deck)
        {
            if (deck == null) { gc.Toasts.Show("Select a deck first, or create one with +."); return; }
            pages["decks"].gameObject.SetActive(false);
            builder.Open(deck, d =>
            {
                if (string.IsNullOrEmpty(d.Id))
                {
                    decks.Remove(d);
                    if (selectedDeckId == lastBuilt) SelectDeck(decks.Count > 0 ? decks[0].Id : "");
                }
                else
                {
                    if (!decks.Contains(d)) decks.Insert(0, d);
                    if (string.IsNullOrEmpty(selectedDeckId)) selectedDeckId = d.Id;
                }
                decks.Sort((a, b) => b.Updated.CompareTo(a.Updated));
                PlayerPrefs.SetString("selectedDeck", selectedDeckId);
                RebuildDeckGrid();
                RefreshPlayCluster();
                if (currentTab == "decks") pages["decks"].gameObject.SetActive(true);
            });
            lastBuilt = deck.Id;
        }
        string lastBuilt;

        void DeleteSelected()
        {
            var deck = FindDeck(selectedDeckId);
            if (deck == null) { gc.Toasts.Show("Select a deck to delete."); return; }
            // Two clicks within 3 s confirm, so a deck is never lost to one stray click.
            if (pendingDelete != deck.Id || Time.unscaledTime - pendingDeleteAt > 3f)
            {
                pendingDelete = deck.Id; pendingDeleteAt = Time.unscaledTime;
                gc.Toasts.Show("Click the trash again to delete \"" + deck.Name + "\".");
                return;
            }
            pendingDelete = null;
            DeckStore.Delete(deck);
            decks.Remove(deck);
            SelectDeck(decks.Count > 0 ? decks[0].Id : "");
            gc.Toasts.Show("Deleted \"" + deck.Name + "\"");
        }
        string pendingDelete; float pendingDeleteAt;

        void SelectDeck(string id)
        {
            selectedDeckId = id ?? "";
            PlayerPrefs.SetString("selectedDeck", selectedDeckId);
            RebuildDeckGrid();
            RefreshPlayCluster();
        }

        SavedDeck FindDeck(string id) => string.IsNullOrEmpty(id) ? null : decks.Find(d => d.Id == id);

        // =================================================================================================== profile

        RectTransform BuildProfile(RectTransform parent)
        {
            var page = UIKit.Rect("Profile", parent).Stretch();
            var card = Card(page, 60, 300, 980, 600);
            var t = UIKit.Label("Title", card, "PROFILE", 18, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 40, 30);

            FieldLabel(card, "Player name", 500);
            nameInput = UIKit.Input("Name", card, "Player", false, 24);
            nameInput.GetComponent<RectTransform>().Place(30, 440, 500, 56);
            nameInput.text = PlayerPrefs.GetString("playerName", "Player");
            nameInput.onEndEdit.AddListener(v =>
            {
                PlayerPrefs.SetString("playerName", string.IsNullOrWhiteSpace(v) ? "Player" : v.Trim());
                RefreshProfileChip();
            });

            statsText = UIKit.Label("Stats", card, "", 26, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            statsText.rectTransform.Place(32, 250, 900, 160);

            var opts = UIKit.Button("Options", card, "OPTIONS (SIZE, SOUND, AUTO-PAY)", Theme.Secondary, () => gc.Options.Open(), 16);
            ((RectTransform)opts.transform).Place(30, 120, 400, 52);

            var clear = UIKit.Button("ClearCache", card, "CLEAR CARD IMAGE CACHE", Theme.Secondary, ClearImageCache, 16);
            ((RectTransform)clear.transform).Place(450, 120, 400, 52);
            var resetStats = UIKit.Button("ResetStats", card, "RESET STATS", new Color(0.45f, 0.14f, 0.14f), () =>
            {
                PlayerPrefs.SetInt("statGames", 0); PlayerPrefs.SetInt("statWins", 0); RefreshProfile();
            }, 16);
            ((RectTransform)resetStats.transform).Place(30, 50, 400, 52);

            RefreshProfile();
            return page;
        }

        void RefreshProfile()
        {
            int g = PlayerStats.Games, w = PlayerStats.Wins;
            string rate = g > 0 ? Mathf.RoundToInt(100f * w / g) + "%" : "—";
            statsText.text = "GAMES  " + g + "        WINS  <color=#f7a540>" + w + "</color>        LOSSES  " + PlayerStats.Losses + "        WIN RATE  " + rate;
        }

        void RefreshProfileChip() { }   // the player's name shows on the Profile page

        void FieldLabel(Transform parent, string text, float y)
        {
            var l = UIKit.Label("Label", parent, text, 15, Theme.TextDim, TextAnchor.LowerLeft);
            l.rectTransform.Place(32, y, 660, 26);
        }

        void ClearImageCache()
        {
            var dir = CardImageCache.CacheFolder;
            if (dir == null) return;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(dir)) { try { System.IO.File.Delete(f); n++; } catch { } }
            gc.Toasts.Show("Deleted " + n + " cached images. They download again when needed (restart to clear memory too).");
        }

        // =================================================================================================== play cluster

        /// <summary>
        /// Home's right column: three tiles (deck, pod, AI skill) beside the hero banner, and the big Play button
        /// in the bottom-right corner. Only shown on Home.
        /// </summary>
        void BuildPlayCluster(RectTransform frame)
        {
            playCluster = UIKit.Rect("PlayCluster", frame).Stretch();

            // Tile 1: selected deck (click to pick another).
            var deckTile = Tile(playCluster, 1260, 662, 600, 218);
            deckTile.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); SelectTab("decks"); };
            var artMask = UIKit.Panel("ArtMask", deckTile, Color.black);
            artMask.rectTransform.Stretch();
            artMask.pixelsPerUnitMultiplier = 3f;
            artMask.raycastTarget = false;
            artMask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var artImg = UIKit.Rect("Img", artMask.transform);
            artImg.anchorMin = artImg.anchorMax = new Vector2(0.5f, 0.5f);
            artImg.sizeDelta = new Vector2(600, 840);
            artImg.anchoredPosition = new Vector2(0, -150);
            deckArt = artImg.gameObject.AddComponent<RawImage>();
            deckArt.raycastTarget = false;
            var cap = UIKit.Panel("Caption", deckTile, new Color(0.02f, 0.02f, 0.04f, 0.8f), false);
            cap.rectTransform.anchorMin = new Vector2(0, 0); cap.rectTransform.anchorMax = new Vector2(1, 0);
            cap.rectTransform.pivot = new Vector2(0.5f, 0); cap.rectTransform.sizeDelta = new Vector2(0, 80);
            cap.raycastTarget = false;
            deckTitle = UIKit.Label("Title", cap.transform, "", 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.2f).Titled();
            deckTitle.rectTransform.Stretch(22, 0, 22, 6);
            deckSub = UIKit.Label("Sub", cap.transform, "", 14, new Color(0.82f, 0.84f, 0.9f), TextAnchor.LowerLeft);
            deckSub.rectTransform.Stretch(24, 8, 22, 0);

            // Tile 2: pod (opponents + AI decks).
            var pod = Tile(playCluster, 1260, 426, 600, 216);
            modeText = UIKit.Label("Mode", pod, "", 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.2f).Titled();
            modeText.rectTransform.Stretch(24, 0, 24, 14);
            oppText = UIKit.Label("Opp", pod, "", 19, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            oppText.rectTransform.Place(26, 92, 260, 44);
            var minus = UIKit.Button("Minus", pod, "−", Theme.Secondary, () => { opponents = Mathf.Max(1, opponents - 1); RefreshPlayCluster(); }, 26);
            ((RectTransform)minus.transform).Place(430, 94, 66, 42);
            var plus = UIKit.Button("Plus", pod, "+", Theme.Secondary, () => { opponents = Mathf.Min(5, opponents + 1); RefreshPlayCluster(); }, 26);
            ((RectTransform)plus.transform).Place(506, 94, 66, 42);
            aiText = UIKit.Label("AiLabel", pod, "AI DECKS", 15, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            aiText.rectTransform.Place(26, 28, 160, 42);
            var precon = UIKit.Button("Precon", pod, "PRECONS", Theme.Secondary, () => { aiDecks = "precon"; RefreshPlayCluster(); }, 16);
            ((RectTransform)precon.transform).Place(250, 28, 156, 42);
            aiPreconBtn = precon.GetComponent<Image>();
            var random = UIKit.Button("Random", pod, "RANDOM", Theme.Secondary, () => { aiDecks = "random"; RefreshPlayCluster(); }, 16);
            ((RectTransform)random.transform).Place(416, 28, 156, 42);
            aiRandomBtn = random.GetComponent<Image>();
            forgeOnly.Add(aiText.gameObject); forgeOnly.Add(precon.gameObject); forgeOnly.Add(random.gameObject);

            // Tile 3: AI skill. Strong is Forge's maximum (simulates mulligans and combat) and is slow in big pods.
            var skill = Tile(playCluster, 1260, 190, 600, 216);
            var sTitle = UIKit.Label("Title", skill, "AI Skill", 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.2f).Titled();
            sTitle.rectTransform.Stretch(24, 0, 24, 14);
            skillTitle = sTitle;
            // Mode: Forge rules (AI opponents) or Kitchen table (free table, custom cards, no Forge needed).
            var forgeMode = UIKit.Button("ModeForge", skill, "FORGE RULES", Theme.Secondary, () => SetPlayMode("forge"), 16);
            ((RectTransform)forgeMode.transform).Place(26, 104, 266, 46);
            modeForgeBtn = forgeMode.GetComponent<Image>();
            var tableMode = UIKit.Button("ModeTable", skill, "KITCHEN TABLE", Theme.Secondary, () => SetPlayMode("table"), 16);
            ((RectTransform)tableMode.transform).Place(306, 104, 266, 46);
            modeTableBtn = tableMode.GetComponent<Image>();
            tableHint = UIKit.Label("TableHint", skill, "A free table like Cockatrice: nothing is enforced, custom sets welcome, no Forge needed. PLAY seats everyone on this screen.",
                14, Theme.TextDim, TextAnchor.UpperLeft);
            tableHint.rectTransform.Place(26, 56, 548, 44);
            // LAN: host or join a table with other PCs on the same network.
            var lan = UIKit.Button("Lan", skill, "PLAY ON LAN (HOST / JOIN)", new Color(0.16f, 0.3f, 0.72f), () =>
            {
                var d = FindDeck(selectedDeckId);
                if (d == null) { gc.Toasts.Show("Pick a deck first (Decks tab).", true); return; }
                gc.Lan.Open(d);
            }, 16);
            ((RectTransform)lan.transform).Place(26, 12, 548, 40);
            lanButton = lan.gameObject;
            string[] ids = { "fast", "normal", "strong" };
            string[] labels = { "FAST", "NORMAL", "STRONG" };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var b = UIKit.Button("Skill " + id, skill, labels[i], Theme.Secondary, () => { aiStrength = id; RefreshPlayCluster(); }, 16);
                ((RectTransform)b.transform).Place(26 + i * 186, 28, 174, 46);
                strengthBtns[id] = b.GetComponent<Image>();
                forgeOnly.Add(b.gameObject);
            }

            statusText = UIKit.Label("Status", playCluster, "", 15, Theme.Danger, TextAnchor.LowerRight);
            statusText.rectTransform.Place(900, 120, 600, 60);

            // The big button.
            playButton = UIKit.Pill("Play", playCluster, "Play", new Color(0.95f, 0.43f, 0.1f), OnPlay, 1520, 34, 340, 92, 46);
        }

        /// <summary>A home tile: dark rounded panel with a soft shadow and a faint light rim.</summary>
        RectTransform Tile(Transform parent, float x, float y, float w, float h)
        {
            var img = UIKit.Panel("Tile", parent, new Color(0.05f, 0.06f, 0.09f, 0.9f));
            img.rectTransform.Place(x, y, w, h);
            img.pixelsPerUnitMultiplier = 3f;
            UIKit.AddShadow(img.transform, 22, new Vector2(0, -10), 0.6f);
            var edge = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            edge.effectColor = new Color(1, 1, 1, 0.08f);
            edge.effectDistance = new Vector2(1, -1);
            return img.rectTransform;
        }

        void RefreshPlayCluster()
        {
            PlayerPrefs.SetInt("opponents", opponents);
            PlayerPrefs.SetString("aiDecks", aiDecks);
            bool table = playMode == "table";
            modeText.text = (table ? "Kitchen table · " : "Commander · ") + (opponents + 1) + " players";
            oppText.text = (table ? "OTHER SEATS   " : "OPPONENTS   ") + opponents;
            foreach (var go in forgeOnly) go.SetActive(!table);
            tableHint.gameObject.SetActive(table);
            lanButton.SetActive(table);
            skillTitle.text = table ? "Mode" : "Mode & AI skill";
            modeForgeBtn.color = table ? Theme.Secondary : Theme.Accent;
            modeTableBtn.color = table ? Theme.Accent : Theme.Secondary;
            playButton.interactable = table || connected;
            aiPreconBtn.color = aiDecks == "precon" ? Theme.Accent : Theme.Secondary;
            aiRandomBtn.color = aiDecks == "random" ? Theme.Accent : Theme.Secondary;
            PlayerPrefs.SetString("aiStrength", aiStrength);
            foreach (var kv in strengthBtns) kv.Value.color = kv.Key == aiStrength ? Theme.Accent : Theme.Secondary;

            var deck = FindDeck(selectedDeckId);
            if (deck == null)
            {
                deckTitle.text = "Random precon";
                deckSub.text = "Click to choose one of your decks";
                deckArt.texture = GameArt.CardBack;
                heroArt.texture = GameArt.TableBackground;
            }
            else
            {
                deckTitle.text = deck.Name;
                string cmd = deck.CommanderName;
                deckSub.text = (cmd ?? "No commander set") + "  ·  " + deck.CardCount + " cards";
                if (!string.IsNullOrEmpty(cmd) && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(cmd, null, tex =>
                    {
                        if (deckArt != null) deckArt.texture = tex;
                        if (heroArt != null) heroArt.texture = tex;
                    });
                else { deckArt.texture = GameArt.CardBack; heroArt.texture = GameArt.TableBackground; }
            }
        }

        // =================================================================================================== shared bits

        RectTransform Card(Transform parent, float x, float y, float w, float h)
        {
            var img = UIKit.Panel("Card", parent, new Color(0.06f, 0.07f, 0.11f, 0.86f));
            img.rectTransform.Place(x, y, w, h);
            UIKit.AddShadow(img.transform, 24, new Vector2(0, -12), 0.6f);
            var edge = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
            edge.effectColor = new Color(1, 1, 1, 0.06f);
            edge.effectDistance = new Vector2(1, -1);
            return img.rectTransform;
        }

        void SelectTab(string id)
        {
            if (id == "options") { gc.Options.Open(); return; }   // a pop-up, not a page
            currentTab = id;
            if (builder != null && builder.IsOpen) builder.Done();   // leaving the builder saves the deck
            foreach (var kv in tabs)
            {
                bool on = kv.Key == id;
                kv.Value.label.color = on ? Color.white : Theme.TextDim;
                kv.Value.icon.color = on ? Color.white : new Color(0.6f, 0.64f, 0.72f);
                kv.Value.bg.color = on ? new Color(1f, 1f, 1f, 0.07f) : Color.clear;
                kv.Value.bar.SetVisible(on);
            }
            foreach (var kv in pages)
            {
                bool show = kv.Key == id;
                bool was = kv.Value.gameObject.activeSelf;
                kv.Value.gameObject.SetActive(show);
                if (show && !was) FadeIn.Play(kv.Value.gameObject, 0.2f);   // screen cross-fade (200 ms)
            }
            if (playCluster != null) playCluster.gameObject.SetActive(id == "home");
            if (id == "profile") RefreshProfile();
        }

        // =================================================================================================== API used by GameController

        public void SetConnection(bool isConnected)
        {
            connected = isConnected;
            if (connText == null) return;
            connDot.color = isConnected ? Theme.Good : Theme.Danger;
            connText.text = isConnected ? "Connected to Forge" : "Waiting for Forge (port " + gc.Bridge.Port + ")";
            playButton.interactable = isConnected || playMode == "table";

        }

        public void SetBusy(string text)
        {
            statusText.color = Theme.TextDim;
            statusText.text = text;
        }

        public void ShowErrors(List<string> errors)
        {
            SelectTab("home");   // errors are shown under the play panel
            statusText.color = Theme.Danger;
            statusText.text = string.Join("\n", errors.Count > 3 ? errors.GetRange(0, 3) : errors) + (errors.Count > 3 ? "\n(+" + (errors.Count - 3) + " more)" : "");
            if (errors.Count > 3) gc.Toasts.Show(string.Join("\n", errors.GetRange(0, Mathf.Min(10, errors.Count))), true);
        }

        void Update()
        {
            if (slides.Count > 1 && !heroHovered && currentTab == "home" && Time.unscaledTime - slideShownAt > 8f) ShowSlide(slide + 1);
            if (forgeStatus != null)
            {
                bool running = gc.Launcher.Running;
                forgeStatus.text = connected ? "<color=#5fd47a>●</color>  Connected and ready"
                    : ForgeSetup.Busy ? "<color=#f7a540>●</color>  " + ForgeSetup.TaskName + "..."
                    : !ForgeSetup.Installed ? "<color=#e55>●</color>  Not installed. Set it up in the Forge tab"
                    : running ? "<color=#f7a540>●</color>  Starting (loading cards)..."
                    : "<color=#9aa0aa>●</color>  Installed, not running";
                launchButton.SetVisible(!connected && ForgeSetup.Installed && !running && !ForgeSetup.Busy);
            }
            if (launcherText != null)
            {
                string line = gc.Launcher.StatusLine;
                string source = ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing ? "your own Forge (" + ForgeSetup.ForgeDir + ")" : "MTG United's copy of Forge";
                launcherText.text = connected ? "Using " + source + "."
                    : !ForgeSetup.Installed ? "MTG United uses Forge as its rules engine. Install it, or point MTG United at a Forge you already have; nothing is downloaded twice."
                    : (string.IsNullOrEmpty(line) ? "Looking for the bridge..." : line);
            }
        }

        void SetPlayMode(string mode)
        {
            playMode = mode;
            PlayerPrefs.SetString("playMode", mode);
            RefreshPlayCluster();
        }

        /// <summary>Kitchen table: seat 1 plays your selected deck; other seats take your other decks in turn.</summary>
        void StartTable()
        {
            var deck = FindDeck(selectedDeckId);
            if (deck == null) { gc.Toasts.Show("Pick a deck first (Decks tab).", true); return; }
            var others = decks.FindAll(d => d.Id != deck.Id);
            var seats = new List<(string, SavedDeck)> { (PlayerPrefs.GetString("playerName", "Player"), deck) };
            for (int i = 0; i < opponents; i++)
            {
                var d = others.Count > 0 ? others[i % others.Count] : deck;
                seats.Add(("Player " + (i + 2), d));
            }
            gc.Table.Begin(seats);
        }

        void OnPlay()
        {
            if (playMode == "table") { StartTable(); return; }
            var deck = FindDeck(selectedDeckId);
            var commanders = new List<object>();
            if (deck != null)
                foreach (var c in (deck.Commander ?? "").Split(';'))
                    if (c.Trim().Length > 0) commanders.Add(c.Trim());

            gc.BoardView.Clear();
            gc.StartGame(new Dictionary<string, object>
            {
                { "t", "start" },
                { "deck", deck?.List ?? "" },
                { "deckName", deck?.Name ?? "" },
                { "commanders", commanders },
                { "name", PlayerPrefs.GetString("playerName", "Player") },
                { "opponents", opponents },
                { "aiDecks", aiDecks },
                { "aiStrength", aiStrength },
            });
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            statusText.text = "";
            if (video != null && !video.isPlaying) video.Play();
            RefreshProfile();
        }

        public void Hide()
        {
            if (video != null) video.Pause();
            gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (videoTarget != null) videoTarget.Release();
        }
    }
}

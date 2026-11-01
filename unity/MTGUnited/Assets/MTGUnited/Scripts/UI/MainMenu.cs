using System;
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

        public DeckBuilder Builder => builder;

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
            m.forgeFormat = PlayerPrefs.GetString("forgeFormat", DeckFormat.Commander);
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
            pages["kitchen"] = BuildKitchen(content);
            BuildPlayCluster(frame);
            builder = DeckBuilder.Create(content, gc);
            builder.DeckAdded = d => { decks.Insert(0, d); RebuildDeckGrid(); };
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
                video.skipOnDrop = true;
                video.waitForFirstFrame = true;
                // If Windows can't decode it, keep the still table image instead of a black screen.
                video.errorReceived += (vp, msg) => { if (raw != null && GameArt.TableBackground != null) raw.texture = GameArt.TableBackground; };
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

            string[] ids = { "home", "profile", "decks", "forge", "kitchen" };
            string[] labels = { "Home", "Profile", "Decks", "Forge", "Table" };
            Sprite[] icons = { Icons.Home, Icons.Person, Icons.Decks, Icons.Anvil, Icons.Fork };
            // Wordmark in the top-left corner, tabs after it.
            var logo = UIKit.Label("Logo", bar, "MTG UNITED", 30, new Color(0.95f, 0.75f, 0.45f), TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            logo.rectTransform.Place(28, 22, 260, 50);
            var version = UIKit.Label("Version", bar, "v" + Application.version, 12, new Color(1f, 1f, 1f, 0.35f), TextAnchor.LowerLeft);
            version.rectTransform.Place(31, 4, 200, 20);
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

            // ---- slide 3: kitchen table & custom sets
            var kt = UIKit.Rect("KitchenSlide", hero.transform).Stretch();
            var kbg = kt.gameObject.AddComponent<RawImage>();
            kbg.texture = UIKit.Gradient(new Color(0.08f, 0.13f, 0.12f), new Color(0.02f, 0.04f, 0.04f));
            kt.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); SelectTab("kitchen"); };
            var kmark = UIKit.Rect("Cards", kt);
            kmark.Place(690, 130, 440, 440);
            var kmarkImg = kmark.gameObject.AddComponent<Image>();
            kmarkImg.sprite = Icons.Table; kmarkImg.color = new Color(0.45f, 1f, 0.8f, 0.12f); kmarkImg.raycastTarget = false; kmarkImg.preserveAspect = true;
            var kk = UIKit.Label("Kicker", kt, "NEW · KITCHEN TABLE", 16, new Color(0.5f, 1f, 0.82f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            kk.rectTransform.Place(50, 590, 600, 30);
            var ktTitle = UIKit.Label("Title", kt, "Play your own cards", 56, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.5f).Titled();
            ktTitle.rectTransform.Place(46, 510, 800, 80);
            var ktBody = UIKit.Label("Body", kt,
                "Import custom sets from Magic Set Editor and playtest them at a free table, like Cockatrice: alone with several seats, " +
                "or with friends over your local network. No rules engine and no Forge needed.",
                21, new Color(0.86f, 0.9f, 0.9f), TextAnchor.UpperLeft);
            ktBody.rectTransform.Place(52, 330, 640, 170);
            UIKit.Pill("Learn", kt, "How it works", new Color(0.12f, 0.45f, 0.38f), () => SelectTab("kitchen"), 50, 70, 320, 70, 28);
            var imp = UIKit.Button("Import", kt, "IMPORT A SET", Theme.Secondary, () => { SelectTab("kitchen"); customSets.Import(); }, 18);
            ((RectTransform)imp.transform).Place(400, 80, 240, 52);

            slides.Add(forge); slides.Add(pod); slides.Add(kt);

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

        // =================================================================================================== kitchen table

        Text kitchenSets;

        /// <summary>
        /// The Kitchen Table tab: what the mode is, how to bring in a Magic Set Editor set, and buttons to do it
        /// (import, see your sets, start a table alone or on the LAN).
        /// </summary>
        RectTransform BuildKitchen(RectTransform parent)
        {
            var page = UIKit.Rect("Kitchen", parent).Stretch();
            var shade = UIKit.Panel("Shade", page, new Color(0.02f, 0.02f, 0.04f, 0.5f), false);
            shade.rectTransform.Stretch();
            shade.raycastTarget = false;

            var icon = UIKit.Rect("Icon", page); icon.Place(76, 868, 64, 64);
            var ii = icon.gameObject.AddComponent<Image>(); ii.sprite = Icons.Fork; ii.color = new Color(0.5f, 1f, 0.82f); ii.preserveAspect = true; ii.raycastTarget = false;
            var title = UIKit.Label("Title", page, "Kitchen Table", 54, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            title.rectTransform.Place(156, 860, 800, 80);
            var sub = UIKit.Label("Sub", page, "A free table for playtesting and casual games, like Cockatrice: you move the cards, nothing is enforced.",
                18, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Place(80, 812, 1300, 44);

            // ---- how it works (left)
            var how = Card(page, 70, 150, 860, 640);
            var ht = UIKit.Label("Title", how, "HOW IT WORKS", 16, new Color(0.5f, 1f, 0.82f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            ht.rectTransform.Place(30, 590, 800, 30);
            var hb = UIKit.Label("Body", how,
                "<b>No rules engine.</b> Unlike Forge games, the table doesn't know the rules: you play the cards like on a real table. " +
                "That's what lets it use cards that don't exist anywhere else.\n\n" +
                "<b>Start a table:</b> on Home, set the mode to KITCHEN TABLE, pick your deck and press Play. Each other seat gets a deck " +
                "(CHOOSE THEIR DECKS picks them, great for goldfishing a matchup). Ctrl + click a seat's nameplate to play from their side.\n\n" +
                "<b>Moving cards:</b> drag cards anywhere on your battlefield, into your hand, onto a pile, or to another player. " +
                "Drag a pile to take its top card. Click to tap, double-click a hand card to play it, right-click anything for every action. " +
                "Hold Alt to zoom, Q / E to turn a card, F to transform double-faced cards. Ctrl + Z undoes.\n\n" +
                "<b>With friends:</b> PLAY ON LAN (HOST / JOIN) on Home. Everyone on the same network; custom sets are copied to " +
                "the others automatically, so everybody plays with the same cards.\n\n" +
                "<b>Decks:</b> build them in Decks like any other. Set the format (Commander, 60 or 40 cards) and the card pool " +
                "(your custom sets, real cards, or both).",
                17, new Color(0.86f, 0.88f, 0.92f), TextAnchor.UpperLeft);
            hb.rectTransform.Place(30, 24, 800, 556);
            hb.supportRichText = true;

            // ---- custom sets (right)
            var sets = Card(page, 960, 150, 890, 640);
            var st = UIKit.Label("Title", sets, "CUSTOM SETS FROM MAGIC SET EDITOR", 16, new Color(0.5f, 1f, 0.82f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            st.rectTransform.Place(30, 590, 830, 30);
            var steps = UIKit.Label("Steps", sets,
                "<b>1.</b>  In Magic Set Editor, open your set and choose <b>File → Export → Cockatrice</b>. Keep the card images it " +
                "exports in the same folder as the .xml (or a folder inside it).\n\n" +
                "<b>2.</b>  Press <b>Import a set</b> below and choose that .xml file. MTG United copies the cards and finds their images by name.\n\n" +
                "<b>3.</b>  In Decks, open a deck, set <b>POOL</b> to your set, and build. Search works with Scryfall words too " +
                "(t:creature c:r mv<=3).\n\n" +
                "<b>4.</b>  Play it here at the kitchen table. Tokens and double-faced cards from the set work: right-click a card " +
                "to create its tokens, press F to transform.\n\n" +
                "Changed the set in MSE? Export again and import over it: same set code, it's replaced.",
                17, new Color(0.86f, 0.88f, 0.92f), TextAnchor.UpperLeft);
            steps.rectTransform.Place(30, 170, 830, 410);
            steps.supportRichText = true;
            kitchenSets = UIKit.Label("Sets", sets, "", 16, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            kitchenSets.rectTransform.Place(30, 112, 830, 50);
            kitchenSets.supportRichText = true;
            UIKit.Pill("Import", sets, "Import a set", new Color(0.12f, 0.45f, 0.38f), () => customSets.Import(), 30, 26, 300, 70, 26);
            var manage = UIKit.Button("Manage", sets, "MY SETS", Theme.Secondary, () => customSets.Open(), 17);
            ((RectTransform)manage.transform).Place(350, 36, 200, 52);
            var play = UIKit.Button("Play", sets, "PLAY AT THE TABLE", new Color(0.62f, 0.33f, 0.12f), () =>
            {
                SetPlayMode("table");
                SelectTab("home");
            }, 17);
            ((RectTransform)play.transform).Place(570, 36, 290, 52);

            CustomSets.Changed += RefreshKitchen;
            RefreshKitchen();
            return page;
        }

        void RefreshKitchen()
        {
            if (kitchenSets == null) return;
            var all = CustomSets.All;
            if (all.Count == 0) { kitchenSets.text = "<color=#9aa0aa>No custom sets yet.</color>"; return; }
            var names = new List<string>();
            int cards = 0;
            foreach (var s in all) { names.Add(s.Name); cards += s.Cards.Count; }
            kitchenSets.text = "Your sets: " + string.Join(", ", names) + "  <color=#9aa0aa>(" + cards + " cards)</color>";
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

            // Footer: deck tools on the left (act on the selected deck), custom sets, Edit Deck on the right.
            decksFooter = UIKit.Rect("Footer", page).Stretch();
            float fx = 80;
            void Tool(string label, Sprite icon, UnityEngine.Events.UnityAction a)
            {
                UIKit.IconButton(label, decksFooter, icon, fx, 44, 58, a, label);
                var l = UIKit.Label(label + "Label", decksFooter, label, 13, Theme.TextDim, TextAnchor.UpperCenter);
                l.rectTransform.Place(fx - 20, 14, 98, 26);
                fx += 80;
            }
            Tool("Delete", Icons.Trash, DeleteSelected);
            Tool("Rename", Icons.Pencil, () =>
            {
                var d = FindDeck(selectedDeckId);
                if (d == null) { gc.Toasts.Show("Select a deck to rename."); return; }
                StartRename(d);
            });
            Tool("Duplicate", Icons.Copy, DuplicateSelected);
            Tool("Export", Icons.Export, () =>
            {
                var d = FindDeck(selectedDeckId);
                if (d == null) { gc.Toasts.Show("Select a deck to export."); return; }
                DeckFiles.Export(d.Name, DeckModel.From(d), gc.Toasts);
            });
            Tool("Import", Icons.Import, ImportDeckFile);
            var sets = UIKit.Button("CustomSets", decksFooter, "CUSTOM SETS", new Color(0.55f, 0.3f, 0.12f), () => customSets.Open(), 18);
            ((RectTransform)sets.transform).Place(fx + 20, 46, 240, 54);
            UIKit.Pill("EditDeck", decksFooter, "Edit Deck", new Color(0.16f, 0.3f, 0.72f), () => OpenBuilder(FindDeck(selectedDeckId)), 1530, 30, 330, 80, 36);

            RebuildDeckGrid();
            return page;
        }

        void RebuildDeckGrid()
        {
            foreach (Transform c in deckGrid) Destroy(c.gameObject);
            nameBanners.Clear();
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
            var tilePe = tile.gameObject.AddComponent<PointerEvents>();
            tilePe.OnClick = () => OnDeckBoxClick(deck);
            tilePe.OnRightClick = () => DeckBoxMenu(deck);

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
            UIKit.CropToArt(artRt, new Vector2(316, 254));   // just the illustration, no card frame
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
            nameBanners[deck.Id ?? ""] = banner.rectTransform;
            // Click the name of the selected deck to rename it right here.
            banner.raycastTarget = true;
            var bannerPe = banner.gameObject.AddComponent<PointerEvents>();
            bannerPe.OnClick = () =>
            {
                if (deck.Id == selectedDeckId && Time.unscaledTime - selectedAt > 0.5f) StartRename(deck);
                else OnDeckBoxClick(deck);
            };
            bannerPe.OnRightClick = () => DeckBoxMenu(deck);

            var count = UIKit.Label("Count", box.transform, deck.CardCount + "", 15, Color.white, TextAnchor.UpperRight, FontStyle.Bold).Outline(1.5f);
            count.rectTransform.Stretch(0, 0, 36, 10);

            // Favourite star (top-left): click to star / unstar; starred decks come first.
            var starRt = UIKit.Rect("Favorite", tile);
            starRt.anchorMin = starRt.anchorMax = new Vector2(0, 1); starRt.pivot = new Vector2(0, 1);
            starRt.sizeDelta = new Vector2(44, 44); starRt.anchoredPosition = new Vector2(8, -8);
            var starImg = starRt.gameObject.AddComponent<Image>();
            starImg.sprite = deck.Favorite ? Icons.Star : Icons.StarOutline;
            starImg.color = deck.Favorite ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 1f, 1f, 0.55f);
            var starShadow = UIKit.AddShadow(starRt, 10, new Vector2(0, -2), 0.5f);
            starShadow.raycastTarget = false;
            starRt.gameObject.AddComponent<HoverPop>().Scale = 1.2f;
            var starPe = starRt.gameObject.AddComponent<PointerEvents>();
            var starPop = starRt.GetComponent<HoverPop>();
            starPe.OnEnter = () => starPop.Hovered = true;
            starPe.OnExit = () => starPop.Hovered = false;
            starPe.OnClick = () => ToggleFavorite(deck);

            var model = DeckModel.From(deck);
            string cmd = !string.IsNullOrWhiteSpace(deck.Cover) ? deck.Cover.Trim() : model.Commanders.Count > 0 ? model.Commanders[0] : null;
            if (cmd != null && CardImageCache.Instance != null)
                CardImageCache.Instance.Get(cmd, CustomSets.Get(cmd)?.Set, tex => { if (art != null) art.texture = tex; });
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
                var forgePip = ManaSprites.Get(ch.ToString());   // Forge's mana symbol art
                var fill = UIKit.Rect("Fill", pip).Stretch(forgePip != null ? 1 : 3, forgePip != null ? 1 : 3, forgePip != null ? 1 : 3, forgePip != null ? 1 : 3).gameObject.AddComponent<Image>();
                fill.sprite = forgePip != null ? forgePip : Icons.Circle; fill.color = forgePip != null ? Color.white : Theme.ForManaColor(ch.ToString()); fill.raycastTarget = false;
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
                DeckStore.Sort(decks);
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
            ConfirmDialog.Instance.Show("Delete this deck?", "\"" + deck.Name + "\" will be deleted for good.",
                ("Delete", ConfirmDialog.Danger, () => DeleteDeck(deck)),
                ("Cancel", Theme.Secondary, null));
        }

        void DuplicateSelected()
        {
            var deck = FindDeck(selectedDeckId);
            if (deck == null) { gc.Toasts.Show("Select a deck to duplicate."); return; }
            var copy = new SavedDeck
            {
                Name = deck.Name + " (copy)", List = deck.List, Commander = deck.Commander, Format = deck.Format,
                Pool = deck.Pool, Cover = deck.Cover,
            };
            DeckStore.Save(copy);
            decks.Add(copy);
            DeckStore.Sort(decks);
            Sound.Play(Sfx.Token, 0.6f);
            SelectDeck(copy.Id);
        }

        /// <summary>A deck list file (.txt / .dck / .dec) becomes a new deck.</summary>
        void ImportDeckFile()
        {
            ImportDialog.Instance.Open((model, name) =>
            {
                var d = new SavedDeck { Name = string.IsNullOrWhiteSpace(name) ? "Imported Deck" : name };
                model.WriteTo(d);
                if (model.Commanders.Count == 0 && model.Total <= 60) d.Format = model.Total <= 40 ? DeckFormat.Limited : DeckFormat.Constructed;
                DeckStore.Save(d);
                decks.Add(d);
                DeckStore.Sort(decks);
                SelectDeck(d.Id);
                gc.Toasts.Show("Imported " + model.Total + " cards. Type a name for the deck.");
                StartRename(d);   // name it right away (the list itself has no name)
            });
        }

        readonly Dictionary<string, RectTransform> nameBanners = new Dictionary<string, RectTransform>();
        float selectedAt;

        /// <summary>Turns the deck's name banner into a text field; Enter or clicking away saves, Esc cancels.</summary>
        void StartRename(SavedDeck deck)
        {
            if (!nameBanners.TryGetValue(deck.Id ?? "", out var banner) || banner == null) return;
            if (banner.Find("Rename") != null) return;
            var field = UIKit.Input("Rename", banner, "Deck name", false, 20);
            var fr = (RectTransform)field.transform;
            fr.Stretch(10, 6, 10, 6);
            field.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            field.textComponent.alignment = TextAnchor.MiddleCenter;
            field.characterLimit = 60;
            field.text = deck.Name;
            var label = banner.Find("Name");
            if (label != null) label.gameObject.SetActive(false);
            bool done = false;
            field.onEndEdit.AddListener(text =>
            {
                if (done) return;
                done = true;
                string name = (text ?? "").Trim();
                bool cancelled = Input.GetKeyDown(KeyCode.Escape);
                if (!cancelled && name.Length > 0 && name != deck.Name)
                {
                    deck.Name = name;
                    DeckStore.Save(deck);
                    DeckStore.Sort(decks);
                    Sound.Play(Sfx.CardPlace, 0.5f);
                }
                RebuildDeckGrid();
                RefreshPlayCluster();
            });
            field.Select();
            field.ActivateInputField();
            Sound.Play(Sfx.UiClick, 0.5f);
        }

        /// <summary>Right-click a deck box: every deck action in one place.</summary>
        void DeckBoxMenu(SavedDeck deck)
        {
            if (selectedDeckId != deck.Id) SelectDeck(deck.Id);
            PopupMenu.Instance.Show(Input.mousePosition, new List<(string, Action)>
            {
                (deck.Name.ToUpperInvariant(), null),
                ("Edit", () => OpenBuilder(deck)),
                ("Rename", () => StartRename(deck)),
                (deck.Favorite ? "Remove from favourites" : "Add to favourites", () => ToggleFavorite(deck)),
                ("Duplicate", DuplicateSelected),
                ("Export...", () => DeckFiles.Export(deck.Name, DeckModel.From(deck), gc.Toasts)),
                ("Delete", DeleteSelected),
            });
        }

        void ToggleFavorite(SavedDeck deck)
        {
            deck.Favorite = !deck.Favorite;
            DeckStore.Save(deck);
            DeckStore.Sort(decks);
            Sound.Play(deck.Favorite ? Sfx.Counter : Sfx.UiClick, 0.6f);
            RebuildDeckGrid();
        }

        void DeleteDeck(SavedDeck deck)
        {
            DeckStore.Delete(deck);
            decks.Remove(deck);
            SelectDeck(decks.Count > 0 ? decks[0].Id : "");
            gc.Toasts.Show("Deleted \"" + deck.Name + "\"");
        }

        void SelectDeck(string id)
        {
            if (selectedDeckId != (id ?? "")) selectedAt = Time.unscaledTime;   // a click that selects doesn't also rename
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
            UIKit.CropToArt(artImg, new Vector2(600, 218));
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
            // Format (rules engine): a segmented switch — Commander, 60 cards or 40 cards. The AI's decks follow it.
            formatSwitch = FormatSwitch.Create(pod, new Rect(292, 164, 282, 36), new[]
            {
                (DeckFormat.Commander, "Commander"), (DeckFormat.Constructed, "60 cards"), (DeckFormat.Limited, "40 cards"),
            }, SetForgeFormat);
            forgeOnly.Add(formatSwitch.gameObject);
            // Kitchen table, playing alone: pick each other seat's deck (goldfishing / playtesting a matchup).
            var seatDecks = UIKit.Button("SeatDecks", pod, "CHOOSE THEIR DECKS", new Color(0.16f, 0.3f, 0.72f), () => SeatDeckMenu(Input.mousePosition), 16);
            ((RectTransform)seatDecks.transform).Place(26, 28, 546, 42);
            seatDecksButton = seatDecks.gameObject;

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
            // LAN: the kitchen table's lobby, or a rules-engine table (people + AIs) in Forge mode.
            var lanPlay = UIKit.Button("LanPlay", playCluster, "PLAY ON LAN", new Color(0.16f, 0.3f, 0.72f), () =>
            {
                var d = FindDeck(selectedDeckId);
                if (d == null) { gc.Toasts.Show("Pick a deck first (Decks tab).", true); return; }
                if (playMode == "table") gc.Lan.Open(d);
                else ForgeLan.Instance.Open();
            }, 18);
            ((RectTransform)lanPlay.transform).Place(1260, 50, 244, 60);
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
            modeText.text = (table ? "Table · " : "") + (opponents + 1) + " players";
            if (formatSwitch != null) formatSwitch.Select(forgeFormat);
            oppText.text = (table ? "OTHER SEATS   " : "OPPONENTS   ") + opponents;
            foreach (var go in forgeOnly) go.SetActive(!table);
            tableHint.gameObject.SetActive(table);
            lanButton.SetActive(table);
            seatDecksButton.SetActive(table);
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
                string artCard = deck.ArtCard;
                if (!string.IsNullOrEmpty(artCard) && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(artCard, CustomSets.Get(artCard)?.Set, tex =>
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
            // Leaving the builder with unsaved changes asks first, then switches.
            if (builder != null && builder.IsOpen && builder.Dirty) { builder.RequestClose(() => SelectTab(id)); return; }
            currentTab = id;
            if (builder != null && builder.IsOpen) builder.Done();
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
            var seats = new List<(string, SavedDeck)> { (PlayerPrefs.GetString("playerName", "Player"), deck) };
            for (int i = 0; i < opponents; i++) seats.Add(("Player " + (i + 2), SeatDeck(i + 2, deck)));
            gc.Table.Begin(seats);
        }

        GameObject seatDecksButton;

        /// <summary>The deck chosen for a seat (2, 3...), else your other decks in turn, else your own.</summary>
        SavedDeck SeatDeck(int seat, SavedDeck mine)
        {
            string id = PlayerPrefs.GetString("seatDeck" + seat, "");
            if (id == "same") return mine;
            var chosen = FindDeck(id);
            if (chosen != null) return chosen;
            var others = decks.FindAll(d => d.Id != mine.Id);
            return others.Count > 0 ? others[(seat - 2) % others.Count] : mine;
        }

        void SeatDeckMenu(Vector2 at)
        {
            var mine = FindDeck(selectedDeckId);
            if (mine == null) { gc.Toasts.Show("Pick your deck first (Decks tab).", true); return; }
            var items = new List<(string, Action)> { ("OTHER SEATS' DECKS", null) };
            for (int s = 2; s <= opponents + 1; s++)
            {
                int seat = s;
                string id = PlayerPrefs.GetString("seatDeck" + seat, "");
                string label = id == "same" ? "same as yours" : FindDeck(id) != null ? FindDeck(id).Name : "auto: " + SeatDeck(seat, mine).Name;
                items.Add(("Seat " + seat + ":  " + label, () => SeatDeckChoice(seat, at)));
            }
            PopupMenu.Instance.Show(at, items);
        }

        void SeatDeckChoice(int seat, Vector2 at)
        {
            var items = new List<(string, Action)> { ("SEAT " + seat, null) };
            void Pick(string id) { PlayerPrefs.SetString("seatDeck" + seat, id); Sound.Play(Sfx.UiClick, 0.6f); SeatDeckMenu(at); }
            items.Add(("Automatic (your other decks in turn)", () => Pick("")));
            items.Add(("Same deck as yours (mirror)", () => Pick("same")));
            foreach (var d in decks)
            {
                var deck = d;
                items.Add((deck.Name + "  ·  " + DeckFormat.Label(deck.Format), () => Pick(deck.Id)));
            }
            PopupMenu.Instance.Show(at, items);
        }

        void OnPlay()
        {
            if (playMode == "table") { StartTable(); return; }
            if (gc.RemoteBridge) { gc.Toasts.Show("You're seated at someone's LAN table: the host starts the game."); return; }
            var deck = FindDeck(selectedDeckId);
            if (deck != null && (deck.Format ?? DeckFormat.Commander) != forgeFormat)
                gc.Toasts.Show("Heads up: \"" + deck.Name + "\" is a " + DeckFormat.Label(deck.Format) + " deck, playing it as " + DeckFormat.Label(forgeFormat) + ".");
            gc.BoardView.Clear();
            gc.StartGame(ForgeStartMessage(false, opponents));
        }

        /// <summary>The bridge's "start" message for a rules-engine game (also used by the LAN host, with lan = true).</summary>
        public Dictionary<string, object> ForgeStartMessage(bool lan, int aiCount)
        {
            var deck = FindDeck(selectedDeckId);
            var commanders = new List<object>();
            if (deck != null && forgeFormat == DeckFormat.Commander)
                foreach (var c in (deck.Commander ?? "").Split(';'))
                    if (c.Trim().Length > 0) commanders.Add(c.Trim());
            return new Dictionary<string, object>
            {
                { "t", "start" },
                { "deck", deck?.List ?? "" },
                { "deckName", deck?.Name ?? "" },
                { "commanders", commanders },
                { "name", PlayerPrefs.GetString("playerName", "Player") },
                { "opponents", aiCount },
                { "aiDecks", aiDecks },
                { "aiStrength", aiStrength },
                { "format", forgeFormat },
                { "lan", lan },
            };
        }

        /// <summary>What a LAN player sends to take a seat at someone's Forge table.</summary>
        public Dictionary<string, object> ForgeJoinMessage()
        {
            var deck = FindDeck(selectedDeckId);
            var commanders = new List<object>();
            if (deck != null)
                foreach (var c in (deck.Commander ?? "").Split(';'))
                    if (c.Trim().Length > 0) commanders.Add(c.Trim());
            if (deck != null && commanders.Count == 0 && !string.IsNullOrEmpty(deck.CommanderName)) commanders.Add(deck.CommanderName);
            return new Dictionary<string, object>
            {
                { "t", "join" },
                { "name", PlayerPrefs.GetString("playerName", "Player") },
                { "deck", deck?.List ?? "" },
                { "deckName", deck?.Name ?? "" },
                { "commanders", commanders },
            };
        }

        public SavedDeck SelectedDeck => FindDeck(selectedDeckId);
        public string ForgeFormat => forgeFormat;

        // Rules-engine format: Commander, 60-card constructed or 40-card limited (AI decks follow it).
        string forgeFormat = DeckFormat.Commander;
        FormatSwitch formatSwitch;

        void SetForgeFormat(string f)
        {
            if (forgeFormat == f) return;
            forgeFormat = f;
            PlayerPrefs.SetString("forgeFormat", f);
            Sound.Play(Sfx.UiClick, 0.6f);
            RefreshPlayCluster();
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

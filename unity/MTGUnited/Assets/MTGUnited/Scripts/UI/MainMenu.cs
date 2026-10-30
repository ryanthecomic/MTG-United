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
        readonly Dictionary<string, (Text label, Image bar)> tabs = new Dictionary<string, (Text, Image)>();
        readonly Dictionary<string, RectTransform> pages = new Dictionary<string, RectTransform>();
        string currentTab = "home";
        Text connText, profileName, profileInitial;
        Image connDot;

        // Play cluster
        Text deckTitle, deckSub, modeText, oppText, aiText, statusText, launcherText;
        RawImage deckArt;
        Button playButton, launchButton;
        RectTransform playCluster;   // deck summary + PLAY; only shown on the Home tab
        Image aiPreconBtn, aiRandomBtn;

        // Decks page
        RectTransform deckGrid;
        InputField deckNameInput, deckCmdInput, deckListInput;
        Text deckEditorTitle;
        List<SavedDeck> decks = new List<SavedDeck>();
        SavedDeck editing;

        // Profile page
        InputField nameInput;
        Text statsText;

        int opponents;
        string aiDecks;
        string aiStrength;
        readonly Dictionary<string, Image> strengthBtns = new Dictionary<string, Image>();
        string selectedDeckId;
        bool connected;

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
            BuildPlayCluster(frame);

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

        void BuildNavBar(RectTransform frame)
        {
            var bar = UIKit.Panel("NavBar", frame, new Color(0.04f, 0.04f, 0.07f, 0.88f), false);
            bar.rectTransform.Place(0, 1080 - 92, 1920, 92);
            UIKit.AddShadow(bar.transform, 20, new Vector2(0, -10), 0.7f);
            var accent = UIKit.Panel("Accent", bar.transform, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.55f), false);
            accent.rectTransform.anchorMin = new Vector2(0, 0); accent.rectTransform.anchorMax = new Vector2(1, 0);
            accent.rectTransform.sizeDelta = new Vector2(0, 2); accent.rectTransform.anchoredPosition = Vector2.zero;
            accent.raycastTarget = false;

            var logo = UIKit.Label("Logo", bar.transform, "MTG UNITED", 40, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(2).Titled();
            logo.rectTransform.Place(40, 26, 360, 56);
            var sub = UIKit.Label("Sub", bar.transform, "COMMANDER  ·  FORGE ENGINE", 13, Theme.TextDim, TextAnchor.MiddleLeft);
            sub.rectTransform.Place(42, 8, 360, 22);

            string[] ids = { "home", "decks", "profile", "options" };
            string[] labels = { "HOME", "DECKS", "PROFILE", "OPTIONS" };
            float x = 500;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var tab = UIKit.Rect("Tab " + id, bar.transform);
                tab.Place(x, 0, 180, 92);
                var hit = tab.gameObject.AddComponent<Image>();
                hit.color = Color.clear;
                tab.gameObject.AddComponent<PointerEvents>().OnClick = () => SelectTab(id);
                var lbl = UIKit.Label("Label", tab, labels[i], 26, Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold).Titled();
                lbl.rectTransform.Stretch(0, 8, 0, 0);
                var underline = UIKit.Panel("Underline", tab, Theme.Accent);
                underline.rectTransform.anchorMin = new Vector2(0.2f, 0); underline.rectTransform.anchorMax = new Vector2(0.8f, 0);
                underline.rectTransform.sizeDelta = new Vector2(0, 5); underline.rectTransform.anchoredPosition = new Vector2(0, 6);
                underline.raycastTarget = false;
                tabs[id] = (lbl, underline);
                x += 190;
            }

            // Connection pill
            var pill = UIKit.Panel("Connection", bar.transform, new Color(1, 1, 1, 0.06f));
            pill.rectTransform.Place(1290, 26, 330, 40);
            connDot = UIKit.Panel("Dot", pill.transform, Theme.Danger);
            connDot.rectTransform.anchorMin = connDot.rectTransform.anchorMax = new Vector2(0, 0.5f);
            connDot.rectTransform.sizeDelta = new Vector2(12, 12); connDot.rectTransform.anchoredPosition = new Vector2(20, 0);
            connText = UIKit.Label("Text", pill.transform, "", 15, Theme.Text, TextAnchor.MiddleLeft);
            connText.rectTransform.Stretch(36, 0, 10, 0);

            // Profile chip
            var chip = UIKit.Rect("ProfileChip", bar.transform);
            chip.Place(1640, 14, 260, 64);
            var chipHit = chip.gameObject.AddComponent<Image>();
            chipHit.color = Color.clear;
            chip.gameObject.AddComponent<PointerEvents>().OnClick = () => SelectTab("profile");
            var avatar = UIKit.Panel("Avatar", chip, Theme.Accent);
            avatar.rectTransform.Place(0, 4, 56, 56);
            avatar.pixelsPerUnitMultiplier = 0.55f; // fully round
            profileInitial = UIKit.Label("Initial", avatar.transform, "P", 28, new Color(0.15f, 0.08f, 0.02f), TextAnchor.MiddleCenter, FontStyle.Bold);
            profileInitial.rectTransform.Stretch();
            profileName = UIKit.Label("Name", chip, "", 20, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            profileName.rectTransform.Place(68, 4, 190, 56);
            RefreshProfileChip();
        }

        // =================================================================================================== home

        RectTransform BuildHome(RectTransform parent)
        {
            var page = UIKit.Rect("Home", parent).Stretch();

            var hero = Card(page, 60, 430, 980, 470);
            var kicker = UIKit.Label("Kicker", hero, "WELCOME BACK", 16, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            kicker.rectTransform.Stretch(40, 0, 40, 36);
            var title = UIKit.Label("Title", hero, "Gather your pod.", 64, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(2).Titled();
            title.rectTransform.Stretch(38, 0, 40, 62);
            var body = UIKit.Label("Body", hero,
                "Full Commander rules from the Forge engine, against up to five AI opponents.\n\n" +
                "•  Build or paste a deck in <b>Decks</b> (Arena, Moxfield and MTGO exports work).\n" +
                "•  Pick the pod size on the right, then hit <b>PLAY</b>.\n" +
                "•  In game: blue glow = playable, yellow = valid choice, Space = pass.",
                21, new Color(0.85f, 0.88f, 0.94f), TextAnchor.UpperLeft);
            body.rectTransform.Stretch(40, 40, 40, 160);

            var bridge = Card(page, 60, 150, 980, 250);
            var bTitle = UIKit.Label("Title", bridge, "FORGE BRIDGE", 16, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            bTitle.rectTransform.Stretch(40, 0, 40, 30);
            launcherText = UIKit.Label("Status", bridge, "", 17, Theme.TextDim, TextAnchor.UpperLeft);
            launcherText.rectTransform.Stretch(40, 80, 40, 66);
            launchButton = UIKit.Button("Launch", bridge, "START FORGE BRIDGE", Theme.Secondary, () => gc.Launcher.Launch(), 18);
            ((RectTransform)launchButton.transform).Place(40, 26, 320, 52);
            return page;
        }

        // =================================================================================================== decks

        RectTransform BuildDecks(RectTransform parent)
        {
            var page = UIKit.Rect("Decks", parent).Stretch();

            // Deck grid (left)
            var gridCard = Card(page, 40, 30, 1100, 920);
            var gTitle = UIKit.Label("Title", gridCard, "YOUR DECKS", 18, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            gTitle.rectTransform.Stretch(30, 0, 30, 24);
            var scroll = UIKit.Rect("Scroll", gridCard).Stretch(24, 24, 24, 64);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            deckGrid = UIKit.Rect("Grid", scroll);
            deckGrid.anchorMin = new Vector2(0, 1); deckGrid.anchorMax = new Vector2(1, 1); deckGrid.pivot = new Vector2(0.5f, 1);
            var grid = deckGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(196, 290);
            grid.spacing = new Vector2(20, 22);
            grid.padding = new RectOffset(6, 6, 6, 6);
            deckGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = deckGrid; sr.horizontal = false; sr.scrollSensitivity = 40;
            UIKit.AddScrollbar(sr);

            // Editor (right)
            var ed = Card(page, 1160, 330, 720, 620);
            deckEditorTitle = UIKit.Label("Title", ed, "EDIT DECK", 18, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            deckEditorTitle.rectTransform.Stretch(30, 0, 30, 24);
            FieldLabel(ed, "Name", 540);
            deckNameInput = UIKit.Input("Name", ed, "Deck name", false, 20);
            deckNameInput.GetComponent<RectTransform>().Place(30, 476, 660, 48);
            FieldLabel(ed, "Commander (optional; partners separated by ';')", 440);
            deckCmdInput = UIKit.Input("Commander", ed, "Taken from the list's 'Commander' section if empty", false, 18);
            deckCmdInput.GetComponent<RectTransform>().Place(30, 376, 660, 48);
            FieldLabel(ed, "Decklist", 340);
            deckListInput = UIKit.Input("List", ed, "Commander\n1 Atraxa, Praetors' Voice\n\nDeck\n1 Sol Ring\n1 Command Tower\n...", true, 14);
            deckListInput.GetComponent<RectTransform>().Place(30, 90, 660, 246);

            var save = UIKit.Button("Save", ed, "SAVE", Theme.Accent, SaveEditing, 20);
            ((RectTransform)save.transform).Place(30, 24, 200, 52);
            var use = UIKit.Button("Use", ed, "SAVE & USE", Theme.Secondary, () => { SaveEditing(); if (editing != null) SelectDeck(editing.Id); }, 18);
            ((RectTransform)use.transform).Place(246, 24, 220, 52);
            var del = UIKit.Button("Delete", ed, "DELETE", new Color(0.45f, 0.14f, 0.14f), DeleteEditing, 18);
            ((RectTransform)del.transform).Place(490, 24, 200, 52);

            RebuildDeckGrid();
            EditDeck(FindDeck(selectedDeckId) ?? (decks.Count > 0 ? decks[0] : null));
            return page;
        }

        void FieldLabel(Transform parent, string text, float y)
        {
            var l = UIKit.Label("Label", parent, text, 15, Theme.TextDim, TextAnchor.LowerLeft);
            l.rectTransform.Place(32, y, 660, 26);
        }

        void RebuildDeckGrid()
        {
            foreach (Transform c in deckGrid) Destroy(c.gameObject);

            // "New deck" tile
            var add = UIKit.Panel("New", deckGrid, new Color(1, 1, 1, 0.05f));
            add.gameObject.AddComponent<PointerEvents>().OnClick = () => EditDeck(new SavedDeck());
            var plus = UIKit.Label("Plus", add.transform, "+", 90, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            plus.rectTransform.Stretch(0, 40, 0, 0);
            var addLbl = UIKit.Label("Label", add.transform, "NEW DECK", 18, Theme.TextDim, TextAnchor.LowerCenter, FontStyle.Bold);
            addLbl.rectTransform.Stretch(0, 30, 0, 0);

            foreach (var d in decks)
            {
                var deck = d;
                bool selected = deck.Id == selectedDeckId;
                var tile = UIKit.Panel("Deck", deckGrid, selected ? Theme.Accent : new Color(0.15f, 0.16f, 0.22f));
                tile.gameObject.AddComponent<PointerEvents>().OnClick = () => EditDeck(deck);
                var artRt = UIKit.Rect("Art", tile.transform).Stretch(5, 62, 5, 5);
                var mask = artRt.gameObject.AddComponent<Image>();
                mask.sprite = UIKit.Rounded; mask.type = Image.Type.Sliced; mask.color = new Color(0.08f, 0.08f, 0.1f);
                mask.raycastTarget = false;
                artRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var art = UIKit.Rect("Img", artRt).Stretch().gameObject.AddComponent<RawImage>();
                art.raycastTarget = false;
                art.enabled = false;
                string cmd = deck.CommanderName;
                if (!string.IsNullOrEmpty(cmd) && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(cmd, null, tex => { if (art != null) { art.texture = tex; art.enabled = true; } });
                else if (GameArt.CardBack != null) { art.texture = GameArt.CardBack; art.enabled = true; }

                var name = UIKit.Label("Name", tile.transform, deck.Name, 17, selected ? new Color(0.12f, 0.06f, 0.02f) : Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                name.rectTransform.anchorMin = new Vector2(0, 0); name.rectTransform.anchorMax = new Vector2(1, 0);
                name.rectTransform.pivot = new Vector2(0.5f, 0); name.rectTransform.sizeDelta = new Vector2(-16, 30);
                name.rectTransform.anchoredPosition = new Vector2(0, 28);
                var info = UIKit.Label("Info", tile.transform, (cmd ?? "No commander") + "  ·  " + deck.CardCount, 12,
                    selected ? new Color(0.2f, 0.1f, 0.03f) : Theme.TextDim, TextAnchor.UpperLeft);
                info.rectTransform.anchorMin = new Vector2(0, 0); info.rectTransform.anchorMax = new Vector2(1, 0);
                info.rectTransform.pivot = new Vector2(0.5f, 0); info.rectTransform.sizeDelta = new Vector2(-16, 22);
                info.rectTransform.anchoredPosition = new Vector2(0, 6);
                if (selected)
                {
                    var tag = UIKit.Panel("InUse", tile.transform, new Color(0.1f, 0.1f, 0.12f, 0.92f));
                    tag.rectTransform.anchorMin = tag.rectTransform.anchorMax = new Vector2(1, 1);
                    tag.rectTransform.pivot = new Vector2(1, 1);
                    tag.rectTransform.sizeDelta = new Vector2(84, 26); tag.rectTransform.anchoredPosition = new Vector2(-10, -10);
                    tag.raycastTarget = false;
                    var t = UIKit.Label("T", tag.transform, "IN USE", 13, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                    t.rectTransform.Stretch();
                }
            }
        }

        void EditDeck(SavedDeck d)
        {
            editing = d;
            deckEditorTitle.text = d == null ? "EDIT DECK" : string.IsNullOrEmpty(d.Id) ? "NEW DECK" : "EDIT DECK";
            deckNameInput.text = d?.Name ?? "";
            deckCmdInput.text = d?.Commander ?? "";
            deckListInput.text = d?.List ?? "";
        }

        void SaveEditing()
        {
            if (editing == null) editing = new SavedDeck();
            editing.Name = string.IsNullOrWhiteSpace(deckNameInput.text) ? "Untitled Deck" : deckNameInput.text.Trim();
            editing.Commander = deckCmdInput.text.Trim();
            editing.List = deckListInput.text;
            bool isNew = string.IsNullOrEmpty(editing.Id);
            DeckStore.Save(editing);
            if (isNew) decks.Insert(0, editing);
            if (string.IsNullOrEmpty(selectedDeckId)) SelectDeck(editing.Id);
            RebuildDeckGrid();
            RefreshPlayCluster();
            gc.Toasts.Show("Saved \"" + editing.Name + "\"");
        }

        void DeleteEditing()
        {
            if (editing == null || string.IsNullOrEmpty(editing.Id)) { EditDeck(null); return; }
            DeckStore.Delete(editing);
            decks.Remove(editing);
            if (selectedDeckId == editing.Id) SelectDeck(decks.Count > 0 ? decks[0].Id : "");
            EditDeck(decks.Count > 0 ? decks[0] : null);
            RebuildDeckGrid();
            RefreshPlayCluster();
        }

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

        void RefreshProfileChip()
        {
            string n = PlayerPrefs.GetString("playerName", "Player");
            profileName.text = n;
            profileInitial.text = n.Length > 0 ? n.Substring(0, 1).ToUpperInvariant() : "P";
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

        void BuildPlayCluster(RectTransform frame)
        {
            playCluster = UIKit.Rect("PlayCluster", frame).Stretch();
            var panel = Card(playCluster, 1450, 150, 430, 380);

            var mode = UIKit.Label("Mode", panel, "COMMANDER", 16, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            mode.rectTransform.Stretch(24, 0, 24, 18);
            modeText = mode;

            // Selected deck row
            var deckRow = UIKit.Panel("DeckRow", panel, new Color(1, 1, 1, 0.05f));
            deckRow.rectTransform.Place(18, 236, 394, 100);
            deckRow.gameObject.AddComponent<PointerEvents>().OnClick = () => SelectTab("decks");
            var artRt = UIKit.Rect("Art", deckRow.transform);
            artRt.Place(8, 8, 120, 84);
            var maskImg = artRt.gameObject.AddComponent<Image>();
            maskImg.sprite = UIKit.Rounded; maskImg.type = Image.Type.Sliced; maskImg.color = new Color(0.08f, 0.08f, 0.1f);
            artRt.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var artImg = UIKit.Rect("Img", artRt);
            artImg.anchorMin = artImg.anchorMax = new Vector2(0.5f, 0.5f);
            artImg.sizeDelta = new Vector2(120, 168);          // card-shaped; the mask crops it to the art area
            artImg.anchoredPosition = new Vector2(0, -24);
            deckArt = artImg.gameObject.AddComponent<RawImage>();
            deckArt.raycastTarget = false;
            deckTitle = UIKit.Label("Title", deckRow.transform, "", 21, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            deckTitle.rectTransform.Stretch(140, 0, 10, 14);
            deckSub = UIKit.Label("Sub", deckRow.transform, "", 13, Theme.TextDim, TextAnchor.LowerLeft);
            deckSub.rectTransform.Stretch(140, 14, 10, 0);

            // Opponents stepper
            oppText = UIKit.Label("Opp", panel, "", 18, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            oppText.rectTransform.Place(24, 178, 200, 44);
            var minus = UIKit.Button("Minus", panel, "−", Theme.Secondary, () => { opponents = Mathf.Max(1, opponents - 1); RefreshPlayCluster(); }, 24);
            ((RectTransform)minus.transform).Place(300, 180, 50, 40);
            var plus = UIKit.Button("Plus", panel, "+", Theme.Secondary, () => { opponents = Mathf.Min(5, opponents + 1); RefreshPlayCluster(); }, 24);
            ((RectTransform)plus.transform).Place(360, 180, 50, 40);

            // AI decks toggle
            aiText = UIKit.Label("AiLabel", panel, "AI DECKS", 14, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            aiText.rectTransform.Place(24, 134, 120, 36);
            var precon = UIKit.Button("Precon", panel, "PRECONS", Theme.Secondary, () => { aiDecks = "precon"; RefreshPlayCluster(); }, 15);
            ((RectTransform)precon.transform).Place(150, 134, 125, 36);
            aiPreconBtn = precon.GetComponent<Image>();
            var random = UIKit.Button("Random", panel, "RANDOM", Theme.Secondary, () => { aiDecks = "random"; RefreshPlayCluster(); }, 15);
            ((RectTransform)random.transform).Place(285, 134, 125, 36);
            aiRandomBtn = random.GetComponent<Image>();

            // AI strength: Strong is Forge's maximum (simulates mulligans and combat) and is slow in big pods.
            var strengthLbl = UIKit.Label("StrengthLabel", panel, "AI SKILL", 14, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            strengthLbl.rectTransform.Place(24, 90, 120, 36);
            string[] ids = { "fast", "normal", "strong" };
            string[] labels = { "FAST", "NORMAL", "STRONG" };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                var b = UIKit.Button("Skill " + id, panel, labels[i], Theme.Secondary, () => { aiStrength = id; RefreshPlayCluster(); }, 14);
                ((RectTransform)b.transform).Place(150 + i * 88, 90, 82, 36);
                strengthBtns[id] = b.GetComponent<Image>();
            }

            statusText = UIKit.Label("Status", panel, "", 14, Theme.Danger, TextAnchor.UpperLeft);
            statusText.rectTransform.Place(24, 8, 390, 76);

            // The big button.
            playButton = UIKit.Button("Play", playCluster, "PLAY", Theme.Accent, OnPlay, 52);
            ((RectTransform)playButton.transform).Place(1450, 30, 430, 108);
            UIKit.AddShadow(playButton.transform, 26, new Vector2(0, -10), 0.75f);
            var shine = UIKit.Rect("Shine", playButton.transform).Stretch(4, 54, 4, 4);
            var shineImg = shine.gameObject.AddComponent<RawImage>();
            shineImg.texture = UIKit.Gradient(new Color(1, 1, 1, 0.28f), new Color(1, 1, 1, 0f));
            shineImg.raycastTarget = false;
            UIKit.LabelOf(playButton).color = new Color(0.16f, 0.07f, 0.01f);
            UIKit.LabelOf(playButton).Titled();
        }

        void RefreshPlayCluster()
        {
            PlayerPrefs.SetInt("opponents", opponents);
            PlayerPrefs.SetString("aiDecks", aiDecks);
            modeText.text = "COMMANDER  ·  " + (opponents + 1) + "-PLAYER POD";
            oppText.text = "OPPONENTS   " + opponents;
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
                deckArt.enabled = GameArt.CardBack != null;
            }
            else
            {
                deckTitle.text = deck.Name;
                string cmd = deck.CommanderName;
                deckSub.text = (cmd ?? "No commander set") + "  ·  " + deck.CardCount + " cards";
                deckArt.enabled = false;
                if (!string.IsNullOrEmpty(cmd) && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(cmd, null, tex => { if (deckArt != null) { deckArt.texture = tex; deckArt.enabled = true; } });
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
            foreach (var kv in tabs)
            {
                bool on = kv.Key == id;
                kv.Value.label.color = on ? Theme.Text : Theme.TextDim;
                kv.Value.bar.SetVisible(on);
            }
            foreach (var kv in pages) kv.Value.gameObject.SetActive(kv.Key == id);
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
            playButton.interactable = isConnected;
            launchButton.SetVisible(!isConnected);
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
            if (launcherText != null)
            {
                string line = gc.Launcher.StatusLine;
                launcherText.text = connected ? "Forge is running and connected." : (string.IsNullOrEmpty(line) ? "Looking for the bridge..." : line);
            }
        }

        void OnPlay()
        {
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

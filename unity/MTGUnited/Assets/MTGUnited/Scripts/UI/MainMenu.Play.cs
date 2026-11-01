using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Home's right column and the play panel.
    ///  • Home: the selected deck, a summary of the game you'll get (mode · format · players) and "play with
    ///    friends". The big Play button opens the play panel.
    ///  • Play panel (like Arena's Find Match): slides in from the right with the mode tabs, the format list,
    ///    players and AI settings, and its own Play; the rest of the screen dims and shows your decks to pick from
    ///    (decks of the chosen format first).
    /// </summary>
    public partial class MainMenu
    {
        // Home tiles
        Text setupKicker, setupTitle, setupSub;
        Button homePlay;

        // Play panel
        RectTransform playPanel, side, deckChooser;
        Image playVeil;
        Text chooserTitle, chooserSub, panelHint, oppCount, oppLabel;
        readonly List<(string mode, Image bar, Text label, Image icon, Image bg)> modeTabs = new List<(string, Image, Text, Image, Image)>();
        readonly List<(string format, Image diamond, Text label, Image bg)> formatRows = new List<(string, Image, Text, Image)>();
        RectTransform forgeGroup, tableGroup, formatGroup;
        Text tableFormatNote;
        FormatSwitch skillSwitch, aiDeckSwitch;
        float panelT, panelTarget;   // 0 = hidden, 1 = shown
        const float SideW = 460;
        static readonly string[] OfferedFormats = { DeckFormat.Commander };
        CanvasGroup panelGroup;
        string chooserClickId; float chooserClickAt = -1f;

        public bool PlayPanelOpen => playPanel != null && playPanel.gameObject.activeSelf && panelTarget > 0f;

        // =================================================================================================== home

        void BuildPlayCluster(RectTransform frame)
        {
            playCluster = UIKit.Rect("PlayCluster", frame).Stretch();

            // Tile 1: the selected deck (click: choose another, in the play panel).
            var deckTile = Tile(playCluster, 1260, 662, 600, 218);
            deckTile.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); OpenPlayPanel(); };
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
            if (deckTitle is SymbolText dts) dts.Localize = false;   // deck names stay as typed
            deckSub = UIKit.Label("Sub", cap.transform, "", 14, new Color(0.82f, 0.84f, 0.9f), TextAnchor.LowerLeft);
            deckSub.rectTransform.Stretch(24, 8, 22, 0);

            // Tile 2: the game you'll get. Click (or CHANGE) opens the play panel.
            var setup = Tile(playCluster, 1260, 426, 600, 216);
            setup.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); OpenPlayPanel(); };
            setupKicker = UIKit.Label("Kicker", setup, "", 15, new Color(1f, 0.7f, 0.35f), TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            setupKicker.rectTransform.Place(26, 164, 550, 28);
            setupTitle = UIKit.Label("Title", setup, "", 32, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.2f).Titled();
            setupTitle.rectTransform.Place(24, 104, 552, 56);
            setupSub = UIKit.Label("Sub", setup, "", 16, Theme.TextDim, TextAnchor.UpperLeft);
            setupSub.rectTransform.Place(26, 70, 548, 30);
            var change = UIKit.Button("Change", setup, "CHANGE", Theme.Secondary, OpenPlayPanel, 16);
            ((RectTransform)change.transform).Place(26, 20, 180, 42);

            // Tile 3: play with friends (LAN).
            var friends = Tile(playCluster, 1260, 190, 600, 216);
            var fTitle = UIKit.Label("Title", friends, "Play with friends", 32, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.2f).Titled();
            fTitle.rectTransform.Place(24, 140, 552, 56);
            var fBody = UIKit.Label("Body", friends, "Host a table or join one on your network. Only the host needs Forge.", 16, Theme.TextDim, TextAnchor.UpperLeft);
            fBody.rectTransform.Place(26, 74, 548, 60);
            var lan = UIKit.Button("Lan", friends, "PLAY ON LAN", new Color(0.16f, 0.3f, 0.72f), OpenLan, 16);
            ((RectTransform)lan.transform).Place(26, 20, 260, 42);

            statusText = UIKit.Label("Status", playCluster, "", 15, Theme.Danger, TextAnchor.LowerRight);
            statusText.rectTransform.Place(900, 120, 600, 60);

            // The big button: opens the play panel (its own Play, in the same spot, starts the game).
            homePlay = UIKit.Pill("Play", playCluster, "Play", new Color(0.95f, 0.43f, 0.1f), OpenPlayPanel, 1520, 34, 340, 92, 46);

            BuildPlayPanel(frame);
        }

        void OpenLan()
        {
            var d = FindDeck(selectedDeckId);
            if (d == null) { gc.Toasts.Show("Pick a deck first (Decks tab).", true); return; }
            ClosePlayPanel(true);
            if (playMode == "table") gc.Lan.Open(d);
            else ForgeLan.Instance.Open();
        }

        void RefreshPlayCluster()
        {
            PlayerPrefs.SetInt("opponents", opponents);
            PlayerPrefs.SetString("aiDecks", aiDecks);
            PlayerPrefs.SetString("aiStrength", aiStrength);
            bool table = playMode == "table";

            // Home: the game you'll get.
            if (setupTitle != null)
            {
                setupKicker.text = table ? "KITCHEN TABLE" : "RULES ENGINE";
                setupTitle.text = (table ? "" : Loc.T(DeckFormat.Label(forgeFormat)) + "  ·  ") + Loc.T((opponents + 1) + " players");
                setupSub.text = table ? Loc.T("A free table: nothing is enforced, custom sets welcome.")
                    : Loc.T("AI") + ": " + Loc.T(Cap(aiStrength)) + "  ·  " + Loc.T(aiDecks == "random" ? "Random decks" : "Precon decks");
            }

            // Play panel.
            if (playPanel != null)
            {
                foreach (var t in modeTabs)
                {
                    bool on = t.mode == playMode;
                    t.bar.SetVisible(on);
                    t.label.color = on ? Color.white : Theme.TextDim;
                    t.icon.color = on ? new Color(1f, 0.72f, 0.4f) : new Color(0.6f, 0.62f, 0.68f);
                    t.bg.color = on ? new Color(1f, 1f, 1f, 0.06f) : Color.clear;
                }
                foreach (var r in formatRows)
                {
                    bool on = r.format == forgeFormat;
                    r.diamond.color = on ? new Color(1f, 0.66f, 0.25f) : new Color(1f, 1f, 1f, 0.28f);
                    r.label.color = on ? Color.white : new Color(0.78f, 0.8f, 0.85f);
                    r.bg.color = on ? new Color(1f, 1f, 1f, 0.07f) : Color.clear;
                }
                formatGroup.gameObject.SetActive(!table);
                tableFormatNote.gameObject.SetActive(table);
                forgeGroup.gameObject.SetActive(!table);
                tableGroup.gameObject.SetActive(table);
                oppLabel.text = table ? "Other seats" : "AI opponents";
                oppCount.text = opponents.ToString();
                skillSwitch.Select(aiStrength);
                aiDeckSwitch.Select(aiDecks);
                panelHint.text = !table && !connected
                    ? Loc.T("Forge isn't running yet: start it in the Forge tab.")
                    : "";
                RebuildChooser();
            }
            if (playButton != null) playButton.interactable = table || connected;

            // The selected deck tile.
            var deck = FindDeck(selectedDeckId);
            if (deck == null)
            {
                deckTitle.text = Loc.T("Random precon");
                deckSub.text = Loc.T("Click to choose one of your decks");
                deckArt.texture = GameArt.CardBack;
            }
            else
            {
                deckTitle.text = deck.Name;
                string cmd = deck.CommanderName;
                deckSub.text = (cmd ?? Loc.T("No commander set")) + "  ·  " + deck.CardCount + " " + Loc.T("cards") + "  ·  " + Loc.T(DeckFormat.Label(deck.Format));
                string artCard = deck.ArtCard;
                if (!string.IsNullOrEmpty(artCard) && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(artCard, CustomSets.Get(artCard)?.Set, tex => { if (deckArt != null) deckArt.texture = tex; });
                else deckArt.texture = GameArt.CardBack;
            }
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

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // =================================================================================================== play panel

        void BuildPlayPanel(RectTransform frame)
        {
            playPanel = UIKit.Rect("PlayPanel", frame).Stretch(0, 0, 0, 96);   // below the nav bar

            // The dimmed left side: click it to close; it holds the deck chooser.
            playVeil = UIKit.Panel("Veil", playPanel, new Color(0.01f, 0.01f, 0.02f, 0.8f), false);
            playVeil.rectTransform.Stretch();
            playVeil.gameObject.AddComponent<PointerEvents>().OnClick = () => ClosePlayPanel();

            var chooser = UIKit.Rect("Chooser", playPanel);
            chooser.Place(0, 0, 1920 - SideW, 984);
            chooserTitle = UIKit.Label("Title", chooser, "Choose a deck", 44, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            chooserTitle.rectTransform.Place(60, 878, 900, 70);
            chooserSub = UIKit.Label("Sub", chooser, "", 17, Theme.TextDim, TextAnchor.UpperLeft);
            chooserSub.rectTransform.Place(62, 844, 1200, 30);
            var view = UIKit.Rect("Decks", chooser);
            view.Place(40, 30, 1920 - SideW - 120, 800);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            view.gameObject.AddComponent<PointerEvents>().OnClick = () => ClosePlayPanel();   // empty space: close, like the veil
            var sr = view.gameObject.AddComponent<ScrollRect>();
            deckChooser = UIKit.Rect("Content", view);
            deckChooser.anchorMin = new Vector2(0, 1); deckChooser.anchorMax = new Vector2(1, 1); deckChooser.pivot = new Vector2(0.5f, 1);
            sr.content = deckChooser; sr.horizontal = false; sr.scrollSensitivity = 60; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr, 12f);

            // The right-hand panel.
            side = UIKit.Rect("Side", playPanel);
            side.anchorMin = new Vector2(1, 0); side.anchorMax = new Vector2(1, 1); side.pivot = new Vector2(1, 0.5f);
            side.sizeDelta = new Vector2(SideW, 0); side.anchoredPosition = Vector2.zero;
            var sbg = side.gameObject.AddComponent<RawImage>();
            sbg.texture = UIKit.Gradient(new Color(0.09f, 0.075f, 0.065f, 0.99f), new Color(0.03f, 0.03f, 0.04f, 0.99f));
            side.gameObject.AddComponent<PointerEvents>();   // clicks inside don't reach the veil
            // A glowing seam down the left edge.
            var seamGlow = UIKit.Panel("SeamGlow", side, new Color(1f, 0.45f, 0.12f, 0.14f), false);
            seamGlow.rectTransform.anchorMin = new Vector2(0, 0); seamGlow.rectTransform.anchorMax = new Vector2(0, 1);
            seamGlow.rectTransform.pivot = new Vector2(0.5f, 0.5f); seamGlow.rectTransform.sizeDelta = new Vector2(18, 0);
            seamGlow.raycastTarget = false;
            var seam = UIKit.Panel("Seam", side, new Color(1f, 0.55f, 0.18f, 0.95f), false);
            seam.rectTransform.anchorMin = new Vector2(0, 0); seam.rectTransform.anchorMax = new Vector2(0, 1);
            seam.rectTransform.pivot = new Vector2(0, 0.5f); seam.rectTransform.sizeDelta = new Vector2(3, 0);
            seam.raycastTarget = false;

            var close = UIKit.IconButton("Close", playPanel, Icons.Cross, 1920 - SideW - 70, 984 - 70, 52, () => ClosePlayPanel());
            close.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            // Mode tabs.
            ModeTab("forge", "Rules engine", Icons.Anvil, 0);
            ModeTab("table", "Kitchen table", Icons.Fork, 1);

            // Format list (rules engine).
            formatGroup = UIKit.Rect("Format", side).Stretch();
            Caption(formatGroup, "FORMAT", 832);
            float fy = 772;
            // Only Commander is offered for now; 60 / 40-card games still work in the bridge if they come back.
            foreach (var (f, label) in new[] { (DeckFormat.Commander, "Commander") })
            {
                string ff = f;
                var row = UIKit.Panel("Row " + label, formatGroup, Color.clear, false);
                row.rectTransform.Place(20, fy, SideW - 40, 50);
                var pe = row.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = () => SetForgeFormat(ff);
                var d = UIKit.Rect("Diamond", row.transform);
                d.Place(18, 13, 24, 24);
                var di = d.gameObject.AddComponent<Image>();
                di.sprite = Icons.Diamond; di.preserveAspect = true; di.raycastTarget = false;
                var t = UIKit.Label("Label", row.transform, label, 20, Theme.Text, TextAnchor.MiddleLeft);
                t.rectTransform.Stretch(58, 0, 10, 0);
                formatRows.Add((f, di, t, row));
                // A hairline between rows, like a list.
                var hl = UIKit.Panel("Line", formatGroup, new Color(1f, 1f, 1f, 0.06f), false);
                hl.rectTransform.Place(20, fy - 3, SideW - 40, 1);
                hl.raycastTarget = false;
                fy -= 56;
            }
            tableFormatNote = UIKit.Label("TableNote", side, "Any deck, any size. Nothing is enforced; custom sets welcome.", 17, Theme.TextDim, TextAnchor.UpperLeft);
            tableFormatNote.rectTransform.Place(30, 700, SideW - 60, 110);

            // Players.
            var players = UIKit.Rect("Players", side).Stretch();
            Caption(players, "PLAYERS", 590);
            oppLabel = UIKit.Label("Label", players, "", 20, Theme.Text, TextAnchor.MiddleLeft);
            oppLabel.rectTransform.Place(30, 530, 220, 48);
            var minus = UIKit.Button("Minus", players, "−", Theme.Secondary, () => { opponents = Mathf.Max(1, opponents - 1); RefreshPlayCluster(); }, 26);
            ((RectTransform)minus.transform).Place(SideW - 196, 532, 52, 44);
            oppCount = UIKit.Label("Count", players, "", 26, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            oppCount.rectTransform.Place(SideW - 140, 530, 56, 48);
            var plus = UIKit.Button("Plus", players, "+", Theme.Secondary, () => { opponents = Mathf.Min(5, opponents + 1); RefreshPlayCluster(); }, 26);
            ((RectTransform)plus.transform).Place(SideW - 80, 532, 52, 44);

            // Rules engine: AI skill and decks.
            forgeGroup = UIKit.Rect("Forge", side).Stretch();
            Caption(forgeGroup, "AI SKILL", 470);
            skillSwitch = FormatSwitch.Create(forgeGroup, new Rect(30, 420, SideW - 60, 40), new[]
            {
                ("fast", "Fast"), ("normal", "Normal"), ("strong", "Strong"),
            }, id => { aiStrength = id; Sound.Play(Sfx.UiClick, 0.6f); RefreshPlayCluster(); });
            Caption(forgeGroup, "AI DECKS", 370);
            aiDeckSwitch = FormatSwitch.Create(forgeGroup, new Rect(30, 320, SideW - 60, 40), new[]
            {
                ("precon", "Precons"), ("random", "Random"),
            }, id => { aiDecks = id; Sound.Play(Sfx.UiClick, 0.6f); RefreshPlayCluster(); });

            // Kitchen table: the other seats' decks.
            tableGroup = UIKit.Rect("Table", side).Stretch();
            Caption(tableGroup, "THEIR DECKS", 470);
            var seatDecks = UIKit.Button("SeatDecks", tableGroup, "CHOOSE THEIR DECKS", Theme.Secondary, () => SeatDeckMenu(Input.mousePosition), 16);
            ((RectTransform)seatDecks.transform).Place(30, 412, SideW - 60, 46);
            var tHint = UIKit.Label("Hint", tableGroup, "Playing alone? Pick each seat's deck to goldfish a matchup.", 15, Theme.TextDim, TextAnchor.UpperLeft);
            tHint.rectTransform.Place(30, 340, SideW - 60, 60);

            // LAN, hint, Play.
            var lan = UIKit.Button("Lan", side, "PLAY ON LAN (HOST / JOIN)", new Color(0.16f, 0.3f, 0.72f), OpenLan, 15);
            ((RectTransform)lan.transform).Place(30, 210, SideW - 60, 44);
            panelHint = UIKit.Label("Hint", side, "", 15, new Color(1f, 0.66f, 0.3f), TextAnchor.UpperLeft);
            panelHint.rectTransform.Place(30, 140, SideW - 60, 56);
            // Same spot as Home's Play, so a second click starts the game.
            playButton = UIKit.Pill("PanelPlay", playPanel, "Play", new Color(0.95f, 0.43f, 0.1f), () => { ClosePlayPanel(true); OnPlay(); }, 1520, 34 - 0, 340, 92, 46);

            panelT = panelTarget = 0f;
            ApplyPanel();
            playPanel.gameObject.SetActive(false);
        }

        void ModeTab(string mode, string label, Sprite icon, int index)
        {
            float w = SideW / 2f;
            var bg = UIKit.Panel("Tab " + mode, side, Color.clear, false);
            bg.rectTransform.Place(index * w + 3, 984 - 112, w - 3, 112);
            bg.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); SetPlayMode(mode); };
            var ic = UIKit.Rect("Icon", bg.transform);
            ic.Place(w / 2f - 24, 44, 48, 48);
            var ii = ic.gameObject.AddComponent<Image>();
            ii.sprite = icon; ii.preserveAspect = true; ii.raycastTarget = false;
            var t = UIKit.Label("Label", bg.transform, label, 15, Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Place(0, 12, w, 28);
            var bar = UIKit.Panel("Bar", bg.transform, new Color(1f, 0.6f, 0.2f), false);
            bar.rectTransform.Place(20, 0, w - 40, 4);
            bar.raycastTarget = false;
            modeTabs.Add((mode, bar, t, ii, bg));
        }

        static void Caption(Transform parent, string text, float y)
        {
            var c = UIKit.Label("Caption", parent, text, 13, new Color(1f, 0.7f, 0.38f, 0.85f), TextAnchor.LowerLeft, FontStyle.Bold);
            c.rectTransform.Place(32, y, SideW - 60, 24);
        }

        public void OpenPlayPanel()
        {
            if (playPanel == null) return;
            if (currentTab != "home") SelectTab("home");
            playPanel.gameObject.SetActive(true);
            playPanel.SetAsLastSibling();
            panelTarget = 1f;
            RefreshPlayCluster();
            Sound.Play(Sfx.CardSlide, 0.4f);
        }

        public void ClosePlayPanel(bool instant = false)
        {
            if (playPanel == null || !playPanel.gameObject.activeSelf) return;
            panelTarget = 0f;
            if (instant) { panelT = 0f; ApplyPanel(); playPanel.gameObject.SetActive(false); }
        }

        /// <summary>Called from Update: slides the panel in / out and fades the veil.</summary>
        void AnimatePlayPanel()
        {
            if (playPanel == null || !playPanel.gameObject.activeSelf) return;
            if (Mathf.Approximately(panelT, panelTarget)) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * (Motion.ReduceMotion ? 60f : 14f));
            panelT = Mathf.Lerp(panelT, panelTarget, k);
            if (Mathf.Abs(panelT - panelTarget) < 0.004f) panelT = panelTarget;
            ApplyPanel();
            if (panelT <= 0f && panelTarget <= 0f) playPanel.gameObject.SetActive(false);
        }

        void ApplyPanel()
        {
            float e = 1f - (1f - panelT) * (1f - panelT);   // ease out
            side.anchoredPosition = new Vector2((1f - e) * (SideW + 30f), 0);
            var c = playVeil.color; c.a = 0.8f * e; playVeil.color = c;
            if (panelGroup == null) panelGroup = playPanel.gameObject.AddComponent<CanvasGroup>();
            panelGroup.alpha = Mathf.Clamp01(panelT * 1.6f);
        }

        // ---------------------------------------------------------------- deck chooser

        void RebuildChooser()
        {
            if (deckChooser == null || !playPanel.gameObject.activeSelf) return;
            foreach (Transform c in deckChooser) Destroy(c.gameObject);
            bool table = playMode == "table";
            chooserSub.text = table ? Loc.T("Kitchen table: any deck works, nothing is enforced.")
                : forgeFormat == DeckFormat.Commander ? Loc.T("Commander: 100 cards, led by a legendary commander.")
                : forgeFormat == DeckFormat.Limited ? Loc.T("40 cards: limited-style decks, 40 cards or more.")
                : Loc.T("60 cards: constructed decks, 60 cards or more.");

            var fits = new List<SavedDeck>();
            var others = new List<SavedDeck>();
            foreach (var d in decks)
                ((table || (d.Format ?? DeckFormat.Commander) == forgeFormat) ? fits : others).Add(d);

            const float tw = 250, th = 232, gap = 22;
            float width = 1920 - SideW - 120 - 20;
            int cols = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (tw + gap)));
            float y = 0;
            int col = 0;

            void Header(string text)
            {
                if (col > 0) { y -= th + gap; col = 0; }
                var h = UIKit.Label("Header", deckChooser, text, 15, new Color(1f, 0.7f, 0.38f, 0.85f), TextAnchor.LowerLeft, FontStyle.Bold);
                var r = h.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
                r.sizeDelta = new Vector2(600, 30); r.anchoredPosition = new Vector2(6, y);
                y -= 40;
            }
            void Add(SavedDeck deck, bool dim)
            {
                var t = ChooserTile(deck, dim, tw, th);
                t.anchoredPosition = new Vector2(col * (tw + gap), y);
                if (++col >= cols) { col = 0; y -= th + gap; }
            }

            Header(Loc.T("MY DECKS"));
            if (!table) Add(null, false);   // a random precon
            foreach (var d in fits) Add(d, false);
            if (others.Count > 0)
            {
                Header(Loc.T("OTHER FORMATS (they still play, as the chosen format)"));
                foreach (var d in others) Add(d, true);
            }
            if (col > 0) y -= th + gap;
            deckChooser.sizeDelta = new Vector2(0, -y + 10);
        }

        RectTransform ChooserTile(SavedDeck deck, bool dim, float w, float h)
        {
            string id = deck?.Id ?? "";
            bool selected = id == (selectedDeckId ?? "") || (deck == null && FindDeck(selectedDeckId) == null);
            var frame = UIKit.Panel("Deck", deckChooser, selected ? new Color(1f, 0.62f, 0.2f) : new Color(1f, 1f, 1f, 0.08f));
            var fr = frame.rectTransform;
            fr.anchorMin = fr.anchorMax = new Vector2(0, 1); fr.pivot = new Vector2(0, 1);
            fr.sizeDelta = new Vector2(w, h);
            frame.pixelsPerUnitMultiplier = 2.4f;

            var mask = UIKit.Panel("Mask", frame.transform, Color.black);
            mask.rectTransform.Stretch(3, 58, 3, 3);
            mask.pixelsPerUnitMultiplier = 2.6f;
            mask.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var art = UIKit.Rect("Art", mask.transform);
            UIKit.CropToArt(art, new Vector2(w - 6, h - 61));
            var ai = art.gameObject.AddComponent<RawImage>();
            ai.raycastTarget = false;
            ai.texture = GameArt.CardBack;
            string artCard = deck?.ArtCard;
            if (!string.IsNullOrEmpty(artCard) && CardImageCache.Instance != null)
                CardImageCache.Instance.Get(artCard, CustomSets.Get(artCard)?.Set, tex => { if (ai != null) ai.texture = tex; });

            var strip = UIKit.Panel("Strip", frame.transform, new Color(0.05f, 0.05f, 0.07f, 0.98f));
            strip.rectTransform.Stretch(3, 3, 3, h - 58);
            strip.raycastTarget = false;
            var name = UIKit.Label("Name", strip.transform, deck == null ? "Random precon" : deck.Name, 18, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            name.rectTransform.Stretch(8, 20, 8, 4);
            name.resizeTextForBestFit = true; name.resizeTextMinSize = 11; name.resizeTextMaxSize = 18;
            if (deck != null && name is SymbolText ns) ns.Localize = false;
            var sub = UIKit.Label("Sub", strip.transform, deck == null ? Loc.T("A ready-made deck") : Loc.T(DeckFormat.Label(deck.Format)) + "  ·  " + deck.CardCount, 12, Theme.TextDim, TextAnchor.LowerCenter);
            sub.rectTransform.Stretch(8, 4, 8, 0);

            if (dim)
            {
                var cg = frame.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 0.5f;
            }
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            var pop = frame.gameObject.AddComponent<HoverPop>();
            pop.Scale = 1.04f;
            pe.OnEnter = () => pop.Hovered = true;
            pe.OnExit = () => pop.Hovered = false;
            pe.OnClick = () =>
            {
                // (The tiles are rebuilt when the selection changes, so the double-click is tracked here, by deck.)
                bool dbl = chooserClickId == id && Time.unscaledTime - chooserClickAt < 0.4f;
                chooserClickId = id; chooserClickAt = Time.unscaledTime;
                Sound.Play(Sfx.UiClick, 0.6f);
                if (dbl) { ClosePlayPanel(true); OnPlay(); return; }   // double-click: play with it
                if (id != (selectedDeckId ?? "")) SelectDeck(id);
            };
            return fr;
        }
    }
}

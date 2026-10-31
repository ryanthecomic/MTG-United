using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Full-screen deck builder with two layouts:
    ///  • Collection: a card grid on the left (your deck, or the 60 closest search matches from all cards) and the
    ///    deck list on the right.
    ///  • Columns: the whole deck laid out in mana-value columns, commander first.
    /// Nothing is loaded until it is needed: search asks the bridge for 60 matches at a time, and card images are
    /// fetched only for the tiles on screen. Tiles and rows are pooled and reused between refreshes.
    /// </summary>
    public class DeckBuilder : MonoBehaviour
    {
        const float Top = 988;              // below the nav bar (board space)
        const float GridLeft = 40, GridRight = 1430, ListLeft = 1452, ListRight = 1890;
        const float TileW = 196, TileH = 274;

        GameController gc;
        Action<SavedDeck> onDone;
        RectTransform root;
        SavedDeck deck;
        DeckModel model;

        enum Scope { Deck, All }
        Scope scope = Scope.Deck;
        bool columnsLayout;
        string query = "";
        float searchAt = -1f;
        List<CardInfo> results = new List<CardInfo>();
        bool searching;

        // header
        InputField nameInput, searchInput;
        Text countText, scopeText, hintText, layoutLabel;
        RawImage headerArt;
        Image scopeBg;
        Button scopeButton;
        Image layoutIcon;

        // collection layout
        RectTransform collectionRoot, gridContent;
        ScrollRect gridScroll;
        RectTransform listContent;
        readonly List<Tile> gridPool = new List<Tile>();
        readonly List<Row> rowPool = new List<Row>();

        // columns layout
        RectTransform columnsRoot, columnsContent;
        ScrollRect columnsScroll;
        readonly List<Tile> columnPool = new List<Tile>();

        Button formatButton, poolButton;
        static bool commanderFormat = true;   // read by the deck list rows (crown only in Commander)
        string Format => deck?.Format ?? DeckFormat.Commander;

        // text import / export
        RectTransform textDialog;
        InputField textInput;

        public bool IsOpen => gameObject.activeSelf;

        public static DeckBuilder Create(RectTransform frame, GameController gc)
        {
            var rt = UIKit.Rect("DeckBuilder", frame).Stretch();
            var b = rt.gameObject.AddComponent<DeckBuilder>();
            b.gc = gc;
            b.root = rt;
            b.columnsLayout = PlayerPrefs.GetInt("deckLayoutColumns", 0) == 1;
            b.Build();
            rt.gameObject.SetActive(false);
            return b;
        }

        // =================================================================================================== build

        void Build()
        {
            // Dim the background so the cards read clearly.
            var shade = UIKit.Panel("Shade", root, new Color(0.02f, 0.02f, 0.04f, 0.72f), false);
            shade.rectTransform.Place(0, 0, 1920, Top);

            BuildHeader();
            BuildCollection();
            BuildColumns();
            BuildFooter();
            BuildTextDialog();
        }

        void BuildHeader()
        {
            // Deck plate (left): commander art, editable name, card count.
            var plate = UIKit.Panel("DeckPlate", root, new Color(0.13f, 0.09f, 0.06f, 0.95f));
            plate.rectTransform.Place(30, Top - 102, 540, 86);
            var rim = plate.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(0.85f, 0.62f, 0.3f, 0.6f); rim.effectDistance = new Vector2(1.5f, -1.5f);
            var artMask = UIKit.Panel("ArtMask", plate.transform, Color.black);
            artMask.rectTransform.Place(8, 8, 110, 70);
            artMask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var artRt = UIKit.Rect("Art", artMask.transform);
            artRt.anchorMin = artRt.anchorMax = new Vector2(0.5f, 0.5f);
            artRt.sizeDelta = new Vector2(110, 154); artRt.anchoredPosition = new Vector2(0, -22);
            headerArt = artRt.gameObject.AddComponent<RawImage>();
            headerArt.raycastTarget = false;
            nameInput = UIKit.Input("Name", plate.transform, "Deck name", false, 26);
            nameInput.GetComponent<RectTransform>().Place(128, 34, 400, 46);
            nameInput.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            nameInput.textComponent.font = GameArt.TitleFont != null ? GameArt.TitleFont : nameInput.textComponent.font;
            countText = UIKit.Label("Count", plate.transform, "", 17, new Color(0.85f, 0.8f, 0.72f), TextAnchor.MiddleLeft);
            countText.rectTransform.Place(140, 6, 380, 28);

            // Search box.
            var sbg = UIKit.Panel("Search", root, new Color(0.02f, 0.02f, 0.03f, 0.92f));
            sbg.rectTransform.Place(600, Top - 82, 420, 52);
            sbg.pixelsPerUnitMultiplier = 32f / 52f;
            var sIcon = UIKit.Rect("Icon", sbg.transform); sIcon.Place(16, 13, 26, 26);
            var si = sIcon.gameObject.AddComponent<Image>(); si.sprite = Icons.Search; si.color = Theme.TextDim; si.raycastTarget = false;
            searchInput = UIKit.Input("Field", sbg.transform, "Search cards...", false, 20);
            searchInput.GetComponent<RectTransform>().Place(48, 4, 320, 44);
            searchInput.GetComponent<Image>().color = Color.clear;
            searchInput.onValueChanged.AddListener(OnQueryChanged);
            UIKit.IconButton("Clear", sbg.transform, Icons.Cross, 372, 8, 36, () => { searchInput.text = ""; });

            // In deck <-> All cards.
            scopeButton = UIKit.Button("Scope", root, "", Theme.Secondary, ToggleScope, 18);
            ((RectTransform)scopeButton.transform).Place(1036, Top - 82, 250, 52);
            scopeBg = scopeButton.GetComponent<Image>();
            scopeBg.pixelsPerUnitMultiplier = 32f / 52f;
            scopeText = UIKit.LabelOf(scopeButton);

            // Layout switch.
            var lay = UIKit.IconButton("Layout", root, Icons.Columns, 1300, Top - 84, 56, ToggleLayout);
            layoutIcon = lay.transform.Find("Icon").GetComponent<Image>();
            layoutLabel = UIKit.Label("LayoutLabel", root, "", 13, Theme.TextDim, TextAnchor.UpperCenter);
            layoutLabel.rectTransform.Place(1250, Top - 112, 156, 24);

            // Import / export as text.
            UIKit.IconButton("Text", root, Icons.List, 1366, Top - 84, 56, OpenTextDialog);
            var tl = UIKit.Label("TextLabel", root, "Text", 13, Theme.TextDim, TextAnchor.UpperCenter);
            tl.rectTransform.Place(1344, Top - 112, 100, 24);

            // Format (size / copy limits) and card pool (which sets "all cards" draws from).
            formatButton = UIKit.Button("Format", root, "", new Color(0.3f, 0.22f, 0.45f), CycleFormat, 16);
            ((RectTransform)formatButton.transform).Place(1440, Top - 82, 186, 52);
            formatButton.GetComponent<Image>().pixelsPerUnitMultiplier = 32f / 52f;
            poolButton = UIKit.Button("Pool", root, "", new Color(0.22f, 0.32f, 0.3f), () => PoolMenu(Input.mousePosition), 16);
            ((RectTransform)poolButton.transform).Place(1636, Top - 82, 190, 52);
            poolButton.GetComponent<Image>().pixelsPerUnitMultiplier = 32f / 52f;

            hintText = UIKit.Label("Hint", root, "", 15, Theme.TextDim, TextAnchor.MiddleLeft);
            hintText.rectTransform.Place(600, Top - 118, 700, 30);
        }

        void BuildCollection()
        {
            collectionRoot = UIKit.Rect("Collection", root).Stretch();

            // Card grid.
            var view = UIKit.Rect("Grid", collectionRoot);
            view.Place(GridLeft, 20, GridRight - GridLeft, Top - 150);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;   // wheel scrolls between tiles too
            gridScroll = view.gameObject.AddComponent<ScrollRect>();
            gridContent = UIKit.Rect("Content", view);
            gridContent.anchorMin = new Vector2(0, 1); gridContent.anchorMax = new Vector2(1, 1); gridContent.pivot = new Vector2(0.5f, 1);
            var grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(TileW, TileH + 24);
            grid.spacing = new Vector2(braceGap, 14);
            grid.padding = new RectOffset(12, 12, 8, 8);
            grid.childAlignment = TextAnchor.UpperCenter;
            gridContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            gridScroll.content = gridContent; gridScroll.horizontal = false; gridScroll.scrollSensitivity = 50;
            gridScroll.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(gridScroll, 12f);

            // Deck list.
            var listBg = UIKit.Panel("DeckList", collectionRoot, new Color(0.04f, 0.04f, 0.06f, 0.9f));
            listBg.rectTransform.Place(ListLeft, 130, ListRight - ListLeft, Top - 150 - 110 - 118);
            listRect = listBg.rectTransform;
            BuildCurve();
            var lview = UIKit.Rect("View", listBg.transform).Stretch(8, 8, 8, 8);
            lview.gameObject.AddComponent<RectMask2D>();
            lview.gameObject.AddComponent<Image>().color = Color.clear;
            var lscroll = lview.gameObject.AddComponent<ScrollRect>();
            listContent = UIKit.Rect("Content", lview);
            listContent.anchorMin = new Vector2(0, 1); listContent.anchorMax = new Vector2(1, 1); listContent.pivot = new Vector2(0.5f, 1);
            var vl = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 5; vl.childControlHeight = false; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            lscroll.content = listContent; lscroll.horizontal = false; lscroll.scrollSensitivity = 40;
            lscroll.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(lscroll, 10f);
        }

        const float braceGap = 24;

        void BuildColumns()
        {
            columnsRoot = UIKit.Rect("Columns", root).Stretch();
            var view = UIKit.Rect("View", columnsRoot);
            view.Place(30, 20, 1860, Top - 150);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            columnsScroll = view.gameObject.AddComponent<ScrollRect>();
            columnsContent = UIKit.Rect("Content", view);
            columnsContent.anchorMin = columnsContent.anchorMax = new Vector2(0, 1);
            columnsContent.pivot = new Vector2(0, 1);
            columnsScroll.content = columnsContent; columnsScroll.vertical = true; columnsScroll.horizontal = true;
            columnsScroll.scrollSensitivity = 50;
            columnsScroll.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(columnsScroll, 12f);
        }

        void BuildFooter()
        {
            UIKit.IconButton("Delete", root, Icons.Trash, 40, 30, 60, DeleteDeck);
            UIKit.Pill("Done", root, "Done", new Color(0.93f, 0.42f, 0.1f), Done, 1560, 26, 320, 84, 40);
            // X in the top-right corner: back to the deck list (saves, like Done).
            var x = UIKit.IconButton("Close", root, Icons.Cross, 1838, Top - 82, 52, Done);
            x.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
        }

        void BuildTextDialog()
        {
            var shade = UIKit.Panel("TextDialog", root, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.Place(0, 0, 1920, Top);
            textDialog = shade.rectTransform;
            var p = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            p.rectTransform.Place(460, 110, 1000, 760);
            var t = UIKit.Label("Title", p.transform, "DECK AS TEXT", 24, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(30, 0, 30, 24);
            var sub = UIKit.Label("Sub", p.transform, "Paste a list (Arena, Moxfield, MTGO) to replace this deck, or copy it out. Put commanders under a \"Commander\" line.",
                15, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Stretch(30, 0, 30, 64);
            textInput = UIKit.Input("List", p.transform, "Commander\n1 ...\n\nDeck\n1 ...", true, 16);
            textInput.GetComponent<RectTransform>().Place(30, 100, 940, 560);
            var apply = UIKit.Button("Apply", p.transform, "APPLY", Theme.Accent, ApplyText, 20);
            ((RectTransform)apply.transform).Place(750, 28, 220, 54);
            var copy = UIKit.Button("Copy", p.transform, "COPY", Theme.Secondary, () => { GUIUtility.systemCopyBuffer = textInput.text; gc.Toasts.Show("Deck list copied"); }, 18);
            ((RectTransform)copy.transform).Place(510, 28, 220, 54);
            var cancel = UIKit.Button("Cancel", p.transform, "CANCEL", Theme.Secondary, () => textDialog.gameObject.SetActive(false), 18);
            ((RectTransform)cancel.transform).Place(30, 28, 200, 54);
            UIKit.CloseX(p.transform, () => textDialog.gameObject.SetActive(false));
            shade.gameObject.SetActive(false);
        }

        // =================================================================================================== open / close

        public void Open(SavedDeck d, Action<SavedDeck> done)
        {
            deck = d;
            onDone = done;
            model = DeckModel.From(d);
            nameInput.text = d.Name;
            searchInput.SetTextWithoutNotify("");
            query = "";
            results.Clear();
            scope = Scope.Deck;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Travel, 0.94f);   // the deck box opens into the editor
            textDialog.gameObject.SetActive(false);
            Refresh();
            CardCatalog.Ensure(model.AllNames(), Refresh);
        }

        /// <summary>Saves and leaves (also used when the player switches tab).</summary>
        public void Done()
        {
            if (!IsOpen) return;
            Save();
            gameObject.SetActive(false);
            gc.Preview.Hide();
            onDone?.Invoke(deck);
        }

        void Save()
        {
            deck.Name = string.IsNullOrWhiteSpace(nameInput.text) ? "Untitled Deck" : nameInput.text.Trim();
            model.WriteTo(deck);
            DeckStore.Save(deck);
        }

        void DeleteDeck()
        {
            DeckStore.Delete(deck);
            gameObject.SetActive(false);
            gc.Preview.Hide();
            gc.Toasts.Show("Deleted \"" + deck.Name + "\"");
            deck.Id = null;   // tells the menu it's gone
            onDone?.Invoke(deck);
        }

        // =================================================================================================== interaction

        void OnQueryChanged(string text)
        {
            query = text.Trim();
            if (query.Length > 0 && scope == Scope.Deck) scope = Scope.All;   // typing means "find cards"
            if (query.Length > 0 && columnsLayout) SetLayout(false);
            searchAt = Time.unscaledTime + 0.25f;   // debounce: one request after typing pauses
            if (query.Length == 0) { results.Clear(); searching = false; Refresh(); }
        }

        void Update()
        {
            if (searchAt > 0 && Time.unscaledTime >= searchAt)
            {
                searchAt = -1f;
                if (query.Length > 0)
                {
                    searching = true;
                    UpdateHint();
                    CardCatalog.Search(query, (q, list) => { results = list; searching = false; if (scope == Scope.All) Refresh(); });
                }
            }
        }

        void ToggleScope()
        {
            scope = scope == Scope.Deck ? Scope.All : Scope.Deck;
            if (scope == Scope.All && columnsLayout) SetLayout(false);
            Refresh();
        }

        void ToggleLayout() => SetLayout(!columnsLayout);

        void SetLayout(bool columns)
        {
            columnsLayout = columns;
            PlayerPrefs.SetInt("deckLayoutColumns", columns ? 1 : 0);
            if (columns) scope = Scope.Deck;
            Refresh();
        }

        // ---------------------------------------------------------------- format & card pool

        void CycleFormat()
        {
            deck.Format = DeckFormat.Next(Format);
            if (!DeckFormat.HasCommander(Format))
            {
                // Commanders become ordinary cards in formats without a command zone.
                foreach (var c in model.Commanders) model.Add(c);
                model.Commanders.Clear();
            }
            Refresh();
        }

        HashSet<string> Pool()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in (deck?.Pool ?? "").Split(',')) if (p.Trim().Length > 0) set.Add(p.Trim());
            return set;
        }

        bool InPool(CardInfo c, HashSet<string> pool)
        {
            if (pool.Count == 0) return true;
            var custom = CustomSets.Get(c.Name);
            return custom != null ? pool.Contains(custom.Set) : pool.Contains("REAL");
        }

        /// <summary>Only custom sets in the pool: "all cards" lists them whole, no search needed.</summary>
        bool WholeSetView
        {
            get { var p = Pool(); return p.Count > 0 && !p.Contains("REAL"); }
        }

        List<CardInfo> PoolResults()
        {
            var pool = Pool();
            if (query.Length == 0 && WholeSetView)
            {
                var all = new List<CardInfo>();
                foreach (var set in CustomSets.All)
                    if (pool.Contains(set.Code))
                        foreach (var c in set.Cards) if (!c.Token) all.Add(c.ToInfo());
                all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return all.Count > 400 ? all.GetRange(0, 400) : all;
            }
            return results.Where(c => InPool(c, pool)).ToList();
        }

        string PoolLabel()
        {
            var p = Pool();
            if (p.Count == 0) return "ALL";
            if (p.Count == 1) return p.Contains("REAL") ? "REAL CARDS" : p.First();
            return p.Count + " SETS";
        }

        /// <summary>Tick sets in or out of the pool (stays open so several can be toggled).</summary>
        void PoolMenu(Vector2 at)
        {
            var pool = Pool();
            var items = new List<(string, Action)> { ("CARD POOL", null) };
            void Toggle(string code)
            {
                if (!pool.Remove(code)) pool.Add(code);
                deck.Pool = string.Join(",", pool);
                Refresh();
                PoolMenu(at);
            }
            items.Add(((pool.Count == 0 ? "(•) " : "( ) ") + "Everything", () => { deck.Pool = ""; Refresh(); PoolMenu(at); }));
            items.Add(((pool.Contains("REAL") ? "[x] " : "[  ] ") + "Real cards (Forge)", () => Toggle("REAL")));
            foreach (var set in CustomSets.All)
            {
                string code = set.Code;
                items.Add(((pool.Contains(code) ? "[x] " : "[  ] ") + code + " · " + set.Name + " (" + set.Cards.Count + ")", () => Toggle(code)));
            }
            if (CustomSets.All.Count == 0) items.Add(("Import custom sets in Decks → Custom Sets", null));
            PopupMenu.Instance.Show(at, items);
        }

        void AddCard(CardInfo c, RectTransform from = null)
        {
            if (c == null) return;
            bool unlimited = c.Basic || (c.Text ?? "").IndexOf("any number of cards named", StringComparison.OrdinalIgnoreCase) >= 0;
            if (DeckFormat.Singleton(Format) && model.Contains(c.Name) && !unlimited)
            {
                gc.Toasts.Show(c.Name + " is already in the deck (Commander is singleton).");
                return;
            }
            // Commander: the first legendary creature into an empty command zone becomes the commander.
            if (DeckFormat.HasCommander(Format) && model.Commanders.Count == 0 && c.CanBeCommander) model.Commanders.Add(c.Name);
            else model.Add(c.Name);
            Sound.Play(Sfx.Draw, 0.5f);
            if (from != null) FlyToList(c, from);
            Refresh();
        }

        // ---------------------------------------------------------------- deck-builder motion (doc 8.1)

        RectTransform listRect, curveRoot;
        readonly Image[] curveBars = new Image[8];
        readonly Text[] curveCounts = new Text[8];
        readonly TweenF[] curveH = new TweenF[8];
        const float CurveMaxH = 62f;

        /// <summary>Mana curve above the deck list: one bar per mana value (7+ grouped), lands excluded.</summary>
        void BuildCurve()
        {
            var bg = UIKit.Panel("Curve", collectionRoot, new Color(0.04f, 0.04f, 0.06f, 0.9f));
            bg.rectTransform.Place(ListLeft, 130 + (Top - 150 - 110 - 118) + 10, ListRight - ListLeft, 104);
            curveRoot = bg.rectTransform;
            float w = (ListRight - ListLeft - 40) / 8f;
            for (int i = 0; i < 8; i++)
            {
                var bar = UIKit.Panel("Bar", bg.transform, new Color(0.95f, 0.62f, 0.22f, 0.9f));
                var r = bar.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0, 0); r.pivot = new Vector2(0.5f, 0);
                r.sizeDelta = new Vector2(w - 12, 0); r.anchoredPosition = new Vector2(20 + w * i + w / 2, 24);
                bar.pixelsPerUnitMultiplier = 4f;
                bar.raycastTarget = false;
                curveBars[i] = bar;
                var lbl = UIKit.Label("Cmc", bg.transform, i == 7 ? "7+" : i.ToString(), 13, Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0, 0);
                lbl.rectTransform.sizeDelta = new Vector2(w, 20); lbl.rectTransform.anchoredPosition = new Vector2(20 + w * i + w / 2, 12);
                curveCounts[i] = UIKit.Label("N", bar.transform, "", 12, Color.white, TextAnchor.LowerCenter, FontStyle.Bold).Outline(1f);
                var cr = curveCounts[i].rectTransform;
                cr.anchorMin = new Vector2(0, 1); cr.anchorMax = new Vector2(1, 1); cr.pivot = new Vector2(0.5f, 0);
                cr.sizeDelta = new Vector2(0, 16); cr.anchoredPosition = new Vector2(0, 1);
                curveH[i] = new TweenF { From = 0, To = 0 };
            }
        }

        /// <summary>Bars grow to their new heights with a springy settle (spring.card stand-in: ease.land).</summary>
        void RefreshCurve()
        {
            var counts = new int[8];
            foreach (var (count, name) in model.Cards)
            {
                var info = CardCatalog.Get(name);
                if (info == null || info.Land) continue;
                counts[Mathf.Clamp(info.Cmc, 0, 7)] += count;
            }
            int max = Mathf.Max(8, counts.Max());
            for (int i = 0; i < 8; i++)
            {
                float h = CurveMaxH * counts[i] / max;
                if (Mathf.Abs(curveH[i].To - h) > 0.1f) curveH[i].Retarget(h, Motion.Now, Motion.Base, Ease.Land);
                curveCounts[i].text = counts[i] > 0 ? counts[i].ToString() : "";
            }
        }

        void LateUpdate()
        {
            if (curveRoot == null || !curveRoot.gameObject.activeInHierarchy) return;
            float now = Motion.Now;
            for (int i = 0; i < 8; i++)
            {
                var r = curveBars[i].rectTransform;
                r.sizeDelta = new Vector2(r.sizeDelta.x, Mathf.Max(0f, curveH[i].Eval(now)));
            }
        }

        /// <summary>Adding a card flies a mini-card from the grid into the deck list.</summary>
        void FlyToList(CardInfo c, RectTransform from)
        {
            if (CardImageCache.Instance == null || listRect == null) return;
            var fly = UIKit.Rect("Flyer", root);
            fly.anchorMin = fly.anchorMax = new Vector2(0.5f, 0.5f);
            var raw = fly.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            raw.color = new Color(1, 1, 1, 0);
            CardImageCache.Instance.Get(c.Name, c.Set, tex => { if (raw != null) { raw.texture = tex; raw.color = Color.white; } });
            Vector2 a = root.InverseTransformPoint(from.TransformPoint(from.rect.center));
            Vector2 b = root.InverseTransformPoint(listRect.TransformPoint(new Vector2(listRect.rect.center.x, listRect.rect.yMax - 40f)));
            fly.gameObject.AddComponent<Flyer>().Init(a, b, new Vector2(TileW, TileH), new Vector2(TileW * 0.3f, TileH * 0.3f));
        }

        /// <summary>One-shot flight on an arc (travel), shrinking and fading at the end.</summary>
        class Flyer : MonoBehaviour
        {
            Vector2 a, b, s0, s1; float start;
            public void Init(Vector2 from, Vector2 to, Vector2 size0, Vector2 size1) { a = from; b = to; s0 = size0; s1 = size1; start = Motion.Now; Update(); }
            void Update()
            {
                float t = Mathf.Clamp01((Motion.Now - start) / Motion.Travel);
                float e = Easing.Eval(Ease.InOut, t);
                var mid = (a + b) * 0.5f + Vector2.up * (Motion.ReduceMotion ? 0f : 120f);
                var p = (1 - e) * (1 - e) * a + 2 * (1 - e) * e * mid + e * e * b;
                var rt = (RectTransform)transform;
                rt.anchoredPosition = p;
                rt.sizeDelta = Vector2.Lerp(s0, s1, e);
                var raw = GetComponent<RawImage>();
                if (raw.texture != null) raw.color = new Color(1, 1, 1, t < 0.8f ? 1f : 1f - (t - 0.8f) / 0.2f);
                if (t >= 1f) Destroy(gameObject);
            }
        }

        void RemoveCard(string name)
        {
            model.RemoveOne(name);
            Sound.Play(Sfx.Discard, 0.4f);
            Refresh();
        }

        void ToggleCommander(string name)
        {
            model.ToggleCommander(name);
            Refresh();
        }

        void Hover(CardInfo c, bool right)
        {
            if (c == null) gc.Preview.Hide();
            else gc.Preview.Request(c.ToCardData(), right);
        }

        void Inspect(CardInfo c)
        {
            if (c != null) gc.Inspector.Show(c.ToCardData());
        }

        CardInfo InfoFor(string name) => CardCatalog.Get(name) ?? new CardInfo { Name = name, Type = "", Cost = "", Colors = "" };

        // =================================================================================================== text dialog

        void OpenTextDialog()
        {
            var tmp = new SavedDeck();
            model.WriteTo(tmp);
            textInput.text = tmp.List;
            textDialog.gameObject.SetActive(true);
            textDialog.SetAsLastSibling();
        }

        void ApplyText()
        {
            model = DeckModel.From(new SavedDeck { List = textInput.text });
            textDialog.gameObject.SetActive(false);
            Refresh();
            CardCatalog.Ensure(model.AllNames(), Refresh);
        }

        // =================================================================================================== refresh

        void Refresh()
        {
            if (!IsOpen) return;
            int total = model.Total;
            int size = DeckFormat.Size(Format);
            commanderFormat = DeckFormat.HasCommander(Format);
            countText.text = total + "/" + size + " Cards" + (DeckFormat.HasCommander(Format) && model.Commanders.Count == 0 ? "   ·   <color=#f7a540>no commander yet</color>" : "");
            UIKit.LabelOf(formatButton).text = DeckFormat.Label(Format).ToUpperInvariant();
            UIKit.LabelOf(poolButton).text = "POOL: " + PoolLabel();
            countText.supportRichText = true;
            string cmd = model.Commanders.Count > 0 ? model.Commanders[0] : null;
            if (cmd != null && CardImageCache.Instance != null)
            {
                var info = CardCatalog.Get(cmd);
                CardImageCache.Instance.Get(cmd, info?.Set, tex => { if (headerArt != null) headerArt.texture = tex; });
            }
            else headerArt.texture = GameArt.CardBack;

            scopeText.text = scope == Scope.Deck ? "SHOWING: YOUR DECK" : "SHOWING: ALL CARDS";
            scopeBg.color = scope == Scope.Deck ? Theme.Secondary : new Color(0.55f, 0.32f, 0.1f);
            scopeButton.SetVisible(!columnsLayout);
            layoutIcon.sprite = columnsLayout ? Icons.Grid : Icons.Columns;
            layoutLabel.text = columnsLayout ? "Collection view" : "Column view";

            collectionRoot.gameObject.SetActive(!columnsLayout);
            columnsRoot.gameObject.SetActive(columnsLayout);
            if (columnsLayout) RefreshColumns();
            else { RefreshGrid(); RefreshList(); RefreshCurve(); }
            UpdateHint();
        }

        void UpdateHint()
        {
            if (columnsLayout) hintText.text = "Click a card to remove one copy  ·  right-click to zoom";
            else if (scope == Scope.All && query.Length == 0 && WholeSetView)
                hintText.text = "All " + PoolResults().Count + " cards of " + PoolLabel() + "  ·  click to add  ·  type to filter";
            else if (scope == Scope.All)
                hintText.text = query.Length == 0 ? "Type a card name to search all cards (shows the 60 closest matches)"
                    : searching ? "Searching..."
                    : results.Count == 0 ? "No cards match \"" + query + "\""
                    : results.Count + " closest matches  ·  click to add  ·  ◆ = already in your deck" + (gc.Bridge.Connected ? "" : "  (Forge isn't running: only custom sets searched)");
            else hintText.text = "Click a card to remove one copy  ·  search to add cards";
        }

        // ---------------------------------------------------------------- collection grid

        void RefreshGrid()
        {
            var shown = new List<(CardInfo info, int count)>();
            if (scope == Scope.All)
            {
                foreach (var c in PoolResults()) shown.Add((c, model.CountOf(c.Name) + (model.Commanders.Contains(c.Name) ? 1 : 0)));
            }
            else
            {
                foreach (var name in model.Commanders) shown.Add((InfoFor(name), 1));
                foreach (var (count, name) in Sorted()) shown.Add((InfoFor(name), count));
                if (query.Length > 0) shown = shown.Where(s => s.info.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            }

            while (gridPool.Count < shown.Count) gridPool.Add(Tile.Create(gridContent, TileW, TileH, true));
            for (int i = 0; i < gridPool.Count; i++)
            {
                var tile = gridPool[i];
                if (i >= shown.Count) { tile.Root.gameObject.SetActive(false); continue; }
                var (info, count) = shown[i];
                bool commander = model.Commanders.Contains(info.Name);
                var ci = info;
                tile.Bind(info, count, scope == Scope.All && count > 0, commander,
                    () => { if (scope == Scope.All) AddCard(ci, tile.Root); else RemoveCard(ci.Name); },
                    () => Inspect(ci), h => Hover(h ? ci : null, false));
                tile.Root.gameObject.SetActive(true);
            }
        }

        // ---------------------------------------------------------------- deck list (right)

        void RefreshList()
        {
            var rows = new List<(string name, int count, bool commander)>();
            foreach (var c in model.Commanders) rows.Add((c, 1, true));
            foreach (var (count, name) in Sorted()) rows.Add((name, count, false));

            while (rowPool.Count < rows.Count) rowPool.Add(Row.Create(listContent));
            for (int i = 0; i < rowPool.Count; i++)
            {
                var row = rowPool[i];
                if (i >= rows.Count) { row.Root.gameObject.SetActive(false); continue; }
                var (name, count, commander) = rows[i];
                var info = InfoFor(name);
                string n = name;
                row.Bind(info, count, commander,
                    () => RemoveCard(n), () => Inspect(info), () => ToggleCommander(n), h => Hover(h ? info : null, false));
                row.Root.gameObject.SetActive(true);
            }
        }

        /// <summary>Main deck sorted like a decklist: nonlands by mana value then name, lands last.</summary>
        IEnumerable<(int count, string name)> Sorted()
        {
            return model.Cards
                .OrderBy(c => InfoFor(c.name).Land ? 1 : 0)
                .ThenBy(c => InfoFor(c.name).Cmc)
                .ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase);
        }

        // ---------------------------------------------------------------- columns

        void RefreshColumns()
        {
            const float colW = 186, cardW = 172, cardH = 240, step = 34, gap = 14, maxPerColumn = 14;
            var columns = new List<(string label, List<string> cards)>();
            if (model.Commanders.Count > 0) columns.Add(("COMMANDER", new List<string>(model.Commanders)));

            var groups = new SortedDictionary<int, List<string>>();
            var lands = new List<string>();
            foreach (var (count, name) in Sorted())
            {
                var info = InfoFor(name);
                for (int k = 0; k < count; k++)
                {
                    if (info.Land) lands.Add(name);
                    else
                    {
                        int key = Mathf.Min(info.Cmc, 7);
                        if (!groups.TryGetValue(key, out var g)) groups[key] = g = new List<string>();
                        g.Add(name);
                    }
                }
            }
            foreach (var kv in groups) Split(columns, kv.Key == 7 ? "7+" : kv.Key.ToString(), kv.Value, (int)maxPerColumn);
            Split(columns, "LANDS", lands, (int)maxPerColumn);

            int used = 0;
            float x = 0, maxH = 0;
            foreach (var (label, cards) in columns)
            {
                float y = -30;
                for (int i = 0; i < cards.Count; i++)
                {
                    if (used >= columnPool.Count) columnPool.Add(Tile.Create(columnsContent, cardW, cardH, false));
                    var tile = columnPool[used++];
                    var info = InfoFor(cards[i]);
                    bool commander = label == "COMMANDER";
                    string n = cards[i];
                    bool previewRight = x < 900;   // preview on the side away from the card
                    tile.Bind(info, 1, false, commander, () => RemoveCard(n), () => Inspect(info), h => Hover(h ? info : null, previewRight));
                    var rt = tile.Root;
                    rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                    rt.anchoredPosition = new Vector2(x + (colW - cardW) / 2, y);
                    rt.gameObject.SetActive(true);
                    rt.SetAsLastSibling();   // later cards overlap earlier ones, leaving their title bars visible
                    y -= (i == cards.Count - 1) ? cardH : step;
                }
                maxH = Mathf.Max(maxH, -y + 20);
                x += colW + gap;
            }
            for (int i = used; i < columnPool.Count; i++) columnPool[i].Root.gameObject.SetActive(false);

            // Column headings.
            foreach (Transform c in columnsContent) if (c.name == "Heading") Destroy(c.gameObject);
            float hx = 0;
            foreach (var (label, cards) in columns)
            {
                var h = UIKit.Label("Heading", columnsContent, label + "  <color=#8a90a0>" + cards.Count + "</color>", 16, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                h.rectTransform.anchorMin = h.rectTransform.anchorMax = new Vector2(0, 1); h.rectTransform.pivot = new Vector2(0, 1);
                h.rectTransform.sizeDelta = new Vector2(colW, 26); h.rectTransform.anchoredPosition = new Vector2(hx, 0);
                hx += colW + gap;
            }
            columnsContent.sizeDelta = new Vector2(Mathf.Max(x, 100), Mathf.Max(maxH, 100));
        }

        static void Split(List<(string, List<string>)> columns, string label, List<string> cards, int max)
        {
            if (cards.Count == 0) return;
            int parts = Mathf.CeilToInt(cards.Count / (float)max);
            int per = Mathf.CeilToInt(cards.Count / (float)parts);
            for (int p = 0; p < parts; p++)
                columns.Add((p == 0 ? label : "", cards.Skip(p * per).Take(per).ToList()));
        }

        // =================================================================================================== pooled widgets

        /// <summary>A card image tile: art (or a text fallback while it loads), a ◆ marker above when the card is in the deck.</summary>
        class Tile
        {
            public RectTransform Root;
            RawImage art;
            Image frame, marker, crown;
            Text fallback, badge;
            PointerEvents pe;
            string boundName;
            Action click, right;
            Action<bool> hover;

            public static Tile Create(Transform parent, float w, float h, bool withMarker)
            {
                var t = new Tile();
                var root = UIKit.Rect("Tile", parent);
                root.sizeDelta = new Vector2(w, h + (withMarker ? 24 : 0));
                t.Root = root;
                t.frame = UIKit.Panel("Frame", root, new Color(0.12f, 0.12f, 0.15f));
                var fr = t.frame.rectTransform;
                fr.anchorMin = new Vector2(0, 0); fr.anchorMax = new Vector2(1, 0); fr.pivot = new Vector2(0.5f, 0);
                fr.sizeDelta = new Vector2(0, h); fr.anchoredPosition = Vector2.zero;
                t.frame.pixelsPerUnitMultiplier = 2.2f;
                t.frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                t.fallback = UIKit.Label("Text", t.frame.transform, "", 15, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
                t.fallback.rectTransform.Stretch(8, 8, 8, 10);
                t.art = UIKit.Rect("Art", t.frame.transform).Stretch().gameObject.AddComponent<RawImage>();
                t.art.raycastTarget = false;
                if (withMarker)
                {
                    var m = UIKit.Rect("Marker", root);
                    m.anchorMin = m.anchorMax = new Vector2(0.5f, 1); m.pivot = new Vector2(0.5f, 1);
                    m.sizeDelta = new Vector2(18, 18); m.anchoredPosition = new Vector2(0, -2);
                    t.marker = m.gameObject.AddComponent<Image>();
                    t.marker.sprite = Icons.Diamond;
                    t.marker.color = new Color(1f, 0.82f, 0.35f);
                    t.marker.raycastTarget = false;
                }
                var b = UIKit.Panel("Badge", t.frame.transform, new Color(0, 0, 0, 0.8f));
                b.rectTransform.anchorMin = b.rectTransform.anchorMax = new Vector2(1, 0); b.rectTransform.pivot = new Vector2(1, 0);
                b.rectTransform.sizeDelta = new Vector2(46, 28); b.rectTransform.anchoredPosition = new Vector2(-6, 6);
                b.raycastTarget = false;
                t.badge = UIKit.Label("N", b.transform, "", 17, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.badge.rectTransform.Stretch();
                var c = UIKit.Rect("Crown", t.frame.transform);
                c.anchorMin = c.anchorMax = new Vector2(0, 0); c.pivot = new Vector2(0, 0);
                c.sizeDelta = new Vector2(30, 30); c.anchoredPosition = new Vector2(6, 6);
                t.crown = c.gameObject.AddComponent<Image>();
                t.crown.sprite = Icons.Crown; t.crown.color = new Color(1f, 0.8f, 0.3f); t.crown.raycastTarget = false;

                t.pe = t.frame.gameObject.AddComponent<PointerEvents>();
                t.pe.OnClick = () => t.click?.Invoke();
                t.pe.OnRightClick = () => t.right?.Invoke();
                t.pe.OnEnter = () => t.hover?.Invoke(true);
                t.pe.OnExit = () => t.hover?.Invoke(false);
                return t;
            }

            public void Bind(CardInfo info, int count, bool inDeck, bool commander, Action onClick, Action onRight, Action<bool> onHover)
            {
                click = onClick; right = onRight; hover = onHover;
                if (marker != null) marker.enabled = inDeck;
                badge.transform.parent.gameObject.SetActive(count > 1);
                badge.text = "x" + count;
                crown.enabled = commander;
                if (boundName == info.Name) return;   // same card: keep the loaded art
                boundName = info.Name;
                fallback.text = info.Name + "\n<size=12>" + info.Type + "</size>";
                frame.color = FrameColor(info);
                art.enabled = false;
                art.texture = null;
                if (CardImageCache.Instance != null && !info.Unknown)
                {
                    string want = info.Name;
                    CardImageCache.Instance.Get(info.Name, info.Set, tex =>
                    {
                        if (art == null || boundName != want) return;
                        art.texture = tex;
                        art.enabled = true;
                    });
                }
            }
        }

        /// <summary>One line of the deck list: "1x Name   cost", tinted by colour. Crown toggles commander.</summary>
        class Row
        {
            public RectTransform Root;
            Image band, crown;
            Text count, name, cost;
            Action click, right, crownClick;
            Action<bool> hover;

            public static Row Create(Transform parent)
            {
                var r = new Row();
                var root = UIKit.Rect("Row", parent);
                root.sizeDelta = new Vector2(0, 40);
                r.Root = root;
                var cnt = UIKit.Panel("CountBg", root, new Color(0.08f, 0.08f, 0.1f));
                cnt.rectTransform.anchorMin = new Vector2(0, 0); cnt.rectTransform.anchorMax = new Vector2(0, 1);
                cnt.rectTransform.pivot = new Vector2(0, 0.5f); cnt.rectTransform.sizeDelta = new Vector2(48, 0);
                cnt.raycastTarget = false;
                r.count = UIKit.Label("Count", cnt.transform, "", 18, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                r.count.rectTransform.Stretch();
                r.band = UIKit.Panel("Band", root, Color.gray);
                r.band.rectTransform.Stretch(52, 0, 0, 0);
                r.band.pixelsPerUnitMultiplier = 1.6f;
                r.name = UIKit.Label("Name", r.band.transform, "", 18, new Color(0.08f, 0.06f, 0.05f), TextAnchor.MiddleLeft, FontStyle.Bold);
                r.name.rectTransform.Stretch(40, 0, 70, 0);
                r.name.horizontalOverflow = HorizontalWrapMode.Overflow;
                r.cost = UIKit.Label("Cost", r.band.transform, "", 15, new Color(0.1f, 0.08f, 0.06f), TextAnchor.MiddleRight, FontStyle.Bold);
                r.cost.rectTransform.Stretch(0, 0, 10, 0);
                var pe = r.band.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = () => r.click?.Invoke();
                pe.OnRightClick = () => r.right?.Invoke();
                pe.OnEnter = () => r.hover?.Invoke(true);
                pe.OnExit = () => r.hover?.Invoke(false);

                var cr = UIKit.Rect("Crown", r.band.transform);
                cr.anchorMin = cr.anchorMax = new Vector2(0, 0.5f); cr.pivot = new Vector2(0, 0.5f);
                cr.sizeDelta = new Vector2(28, 28); cr.anchoredPosition = new Vector2(6, 0);
                r.crown = cr.gameObject.AddComponent<Image>();
                r.crown.sprite = Icons.Crown;
                r.crown.gameObject.AddComponent<PointerEvents>().OnClick = () => r.crownClick?.Invoke();
                return r;
            }

            public void Bind(CardInfo info, int n, bool commander, Action onClick, Action onRight, Action onCrown, Action<bool> onHover)
            {
                click = onClick; right = onRight; crownClick = onCrown; hover = onHover;
                count.text = commander ? "" : n + "x";
                name.text = info.Name;
                cost.text = ManaShort(info.Cost);
                band.color = commander ? new Color(1f, 0.83f, 0.45f) : Color.Lerp(FrameColor(info), Color.white, 0.45f);
                // Crown: solid on the commander, faint on other cards that could be one, hidden otherwise.
                bool eligible = commander || (commanderFormat && info.CanBeCommander);
                crown.enabled = eligible;
                crown.color = commander ? new Color(0.45f, 0.25f, 0.02f) : new Color(0.2f, 0.15f, 0.1f, 0.35f);
            }
        }

        static Color FrameColor(CardInfo c)
        {
            if (c == null) return new Color(0.3f, 0.3f, 0.32f);
            if (c.Land) return new Color(0.55f, 0.47f, 0.36f);
            string cols = (c.Colors ?? "").ToUpperInvariant();
            if (cols.Length == 0) return new Color(0.62f, 0.64f, 0.68f);
            if (cols.Length > 1) return new Color(0.85f, 0.7f, 0.35f);
            return Theme.ForManaColor(cols);
        }

        /// <summary>"{2}{R}{G}" or "2 R G" → "2RG" for the deck list.</summary>
        static string ManaShort(string cost) => CardView.ManaText(cost);   // drawn as symbols by the label
    }
}

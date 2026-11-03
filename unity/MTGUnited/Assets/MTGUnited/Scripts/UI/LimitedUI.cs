using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Draft and sealed. Runs the event on this machine (alone with bots, or as the LAN host) or follows the host's
    /// event (LAN client), shows the draft screen, and at the end turns the opened cards into a 40-card deck to build.
    /// </summary>
    public class LimitedSession : MonoBehaviour
    {
        public static LimitedSession Instance { get; private set; }
        GameController gc;
        LimitedEvent ev;          // the running event (solo / host); null when following a host
        bool lan;                 // a LAN event (as host or client)
        int mySeat;
        LimitedView view;
        string eventName = "Draft";

        public bool Running => DraftView.Instance != null && DraftView.Instance.IsOpen;

        public static LimitedSession Create(GameController gc)
        {
            var s = gc.gameObject.AddComponent<LimitedSession>();
            s.gc = gc;
            Instance = s;
            return s;
        }

        /// <summary>Starts an event set up in the dialog: alone (seat 0 + bots) or with the LAN lobby (+ bots).</summary>
        public void Begin(LimitedEvent e, bool withLan)
        {
            ev = e;
            lan = withLan;
            mySeat = 0;
            eventName = (e.Draft ? "Draft" : "Sealed") + " · " + e.Sources[0].Label;
            e.Changed += PushViews;
            try { e.Start(); }
            catch (Exception ex) { gc.Toasts.Show(ex.Message, true); ev = null; return; }
            if (lan) gc.Lan.Hide();
            PushViews();
        }

        void PushViews()
        {
            if (ev == null) return;
            if (lan) for (int i = 1; i < ev.Seats.Count; i++) if (!ev.Seats[i].Bot) gc.Net.SendLimitedView(i, ev.ViewFor(i));
            ShowView(LimitedView.From(ev.ViewFor(mySeat)), false);
        }

        /// <summary>The screen for my seat (from my own event, or received from the host).</summary>
        public void ShowView(LimitedView v, bool fromHost)
        {
            if (fromHost) { lan = true; ev = null; if (gc.Lan.IsOpen) gc.Lan.Hide(); eventName = (v.Draft ? "Draft" : "Sealed") + " · " + v.Source; }
            view = v;
            DraftView.Instance.Show(v);
        }

        /// <summary>I take a card.</summary>
        public void Pick(string card)
        {
            if (ev != null) ev.Pick(mySeat, card);
            else gc.Net.SendLimitedPick(card);
            Sound.Play(Sfx.Draw, 0.5f);
        }

        /// <summary>Host: a LAN player took a card.</summary>
        public void RemotePick(int seat, string card) { if (ev != null) ev.Pick(seat, card); }

        /// <summary>The event is over for me: my opened cards become a deck, and the builder opens on it.</summary>
        public void BuildDeck()
        {
            if (view == null) return;
            var deck = new SavedDeck
            {
                Name = eventName + " · " + DateTime.Now.ToString("dd/MM"),
                Format = DeckFormat.Limited,
                LimitedPool = LimitedView.PoolText(view.Picks),
                PoolCards = view.Picks.GroupBy(c => c.Name).Select(g => g.First()).ToList(),
            };
            var cover = view.Picks.OrderByDescending(c => c.Rarity == "mythic" ? 3 : c.Rarity == "rare" ? 2 : c.Rarity == "uncommon" ? 1 : 0).FirstOrDefault();
            if (cover != null) deck.Cover = cover.Name;
            DeckStore.Save(deck);
            DraftView.Instance.Close();
            bool wasLan = lan;
            ev = null;
            gc.Menu.OpenLimitedDeck(deck, built =>
            {
                // At a LAN table this is the deck you'll play: the host's lobby learns it.
                if (wasLan && gc.Net.Mode != TableNet.Role.None)
                {
                    gc.Net.SetMyDeck(built);
                    gc.Lan.Open(built);
                }
            });
        }

        public void Abandon()
        {
            if (ev != null) ev.Changed -= PushViews;
            ev = null;
            view = null;
            DraftView.Instance.Close();
            if (lan && gc.Net.Mode != TableNet.Role.None) gc.Lan.Open(gc.Menu.SelectedDeck);
        }

        // ---------------------------------------------------------------- loading a real set (Scryfall)

        public void LoadRealSet(string code, Action<LimitedSource> done)
        {
            StartCoroutine(ScryfallSearch.FetchSet(code.Trim().ToLowerInvariant(), (cards, error) =>
            {
                if (cards.Count == 0) { gc.Toasts.Show(error ?? "No cards found for " + code + ".", true); done(null); return; }
                var src = new LimitedSource { Type = LimitedSource.Kind.Set, Code = code.Trim().ToUpperInvariant(), Name = code.Trim().ToUpperInvariant() };
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (info, rarity) in cards) if (!info.Basic && seen.Add(info.Name)) src.Cards.Add(LimitedCard.From(info, rarity));
                done(src);
            }));
        }
    }

    // =================================================================================================================

    /// <summary>Sets up a draft or sealed event: packs (a real set, a custom set or a cube, per pack), which cards
    /// can be opened, and how many bots fill the seats.</summary>
    public class LimitedSetup : MonoBehaviour
    {
        public static LimitedSetup Instance { get; private set; }
        GameController gc;
        RectTransform panel;
        bool lan, draft = true;
        int bots = 7;
        readonly LimitedSource[] sources = new LimitedSource[3];   // draft: one per pack (null = same as pack 1)
        readonly List<(Text label, Button cards, GameObject row)> rows = new List<(Text, Button, GameObject)>();
        Text playersText, botsText, hint;
        FormatSwitch modeSwitch;
        InputField codeInput;
        GameObject codeRow;
        int codeFor = -1;
        CardPicker picker;

        public bool IsOpen => gameObject.activeSelf;

        public static LimitedSetup Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("LimitedSetup", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.Stretch();
            var d = shade.gameObject.AddComponent<LimitedSetup>();
            Instance = d;
            d.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = d.Close;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            d.panel = p.rectTransform;
            d.panel.anchorMin = d.panel.anchorMax = new Vector2(0.5f, 0.5f);
            d.panel.sizeDelta = new Vector2(1000, 760);
            p.gameObject.AddComponent<PointerEvents>();
            UiScaled.Add(p, Vector2.zero).MaxScale = 1.25f;

            var t = UIKit.Label("Title", p.transform, "DRAFT / SEALED", 28, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 40, 26);

            d.modeSwitch = FormatSwitch.Create(p.transform, new Rect(40, 620, 420, 44), new[] { ("draft", "Draft"), ("sealed", "Sealed") }, id =>
            {
                d.draft = id == "draft";
                if (!d.draft && d.bots > 7) d.bots = 7;
                d.Refresh();
            });

            // Packs.
            var cap = UIKit.Label("Packs", p.transform, "PACKS", 13, new Color(1f, 0.7f, 0.38f), TextAnchor.LowerLeft, FontStyle.Bold);
            cap.rectTransform.Place(40, 572, 400, 24);
            float y = 508;
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var row = UIKit.Rect("Row" + i, p.transform);
                row.Place(40, y, 920, 56);
                var name = UIKit.Label("Pack", row, "", 17, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
                name.rectTransform.Place(0, 4, 120, 48);
                var pick = UIKit.Button("Source", row, "", new Color(0.14f, 0.14f, 0.15f), () => d.SourceMenu(k, Input.mousePosition), 17);
                ((RectTransform)pick.transform).Place(124, 4, 560, 48);
                UIKit.LabelOf(pick).alignment = TextAnchor.MiddleLeft;
                if (UIKit.LabelOf(pick) is SymbolText st) st.Localize = false;
                var cards = UIKit.Button("Cards", row, "CARDS…", Theme.Secondary, () => d.OpenPicker(k), 15);
                ((RectTransform)cards.transform).Place(696, 4, 224, 48);
                d.rows.Add((UIKit.LabelOf(pick), cards, row.gameObject));
                name.text = i == 0 ? "PACK 1" : "PACK " + (i + 1);
                y -= 62;
            }

            // A real set's code, typed in.
            var cr = UIKit.Rect("CodeRow", p.transform);
            cr.Place(40, 316, 920, 56);
            d.codeRow = cr.gameObject;
            var cl = UIKit.Label("L", cr, "Set code (e.g. MKM, DSK, FDN):", 16, Theme.Text, TextAnchor.MiddleLeft);
            cl.rectTransform.Place(0, 4, 300, 48);
            d.codeInput = UIKit.Input("Code", cr, "MKM", false, 18);
            ((RectTransform)d.codeInput.transform).Place(304, 4, 200, 48);
            var load = UIKit.Button("Load", cr, "LOAD FROM SCRYFALL", Theme.Accent, d.LoadCode, 15);
            ((RectTransform)load.transform).Place(516, 4, 260, 48);
            d.codeRow.SetActive(false);

            // Players.
            var pc = UIKit.Label("Players", p.transform, "PLAYERS", 13, new Color(1f, 0.7f, 0.38f), TextAnchor.LowerLeft, FontStyle.Bold);
            pc.rectTransform.Place(40, 272, 400, 24);
            d.playersText = UIKit.Label("Who", p.transform, "", 16, Theme.Text, TextAnchor.UpperLeft);
            d.playersText.rectTransform.Place(40, 196, 560, 72);
            d.botsText = UIKit.Label("Bots", p.transform, "", 17, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            d.botsText.rectTransform.Place(620, 220, 180, 48);
            var minus = UIKit.Button("Minus", p.transform, "−", Theme.Secondary, () => { d.bots = Mathf.Max(0, d.bots - 1); d.Refresh(); }, 24);
            ((RectTransform)minus.transform).Place(810, 222, 56, 44);
            var plus = UIKit.Button("Plus", p.transform, "+", Theme.Secondary, () => { d.bots = Mathf.Min(11, d.bots + 1); d.Refresh(); }, 24);
            ((RectTransform)plus.transform).Place(876, 222, 56, 44);

            d.hint = UIKit.Label("Hint", p.transform, "", 14, Theme.TextDim, TextAnchor.UpperLeft);
            d.hint.rectTransform.Place(40, 112, 600, 70);
            UIKit.Pill("Start", p.transform, "Start", new Color(0.95f, 0.43f, 0.1f), d.StartEvent, 700, 30, 260, 76, 30);

            UIKit.CloseX(p.transform, d.Close);
            d.picker = CardPicker.Create(parent);
            MenuKit.Bandify(p);
            shade.gameObject.SetActive(false);
            return d;
        }

        public void Open(bool withLan)
        {
            lan = withLan;
            if (sources[0] == null && CustomSets.All.Count > 0) sources[0] = LimitedSource.FromCustom(CustomSets.All[0]);
            modeSwitch.Select(draft ? "draft" : "sealed");
            codeRow.SetActive(false);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(panel.gameObject, Motion.Short, 0.95f);
            Refresh();
        }

        public void Close() { codeRow.SetActive(false); gameObject.SetActive(false); }

        /// <summary>Opens the setup with one of the saved cubes already chosen for every pack.</summary>
        public void OpenWithCube(SavedCube cube, bool withLan)
        {
            sources[0] = LimitedSource.FromList(cube.Name, cube.List);
            sources[1] = sources[2] = null;
            Open(withLan);
        }

        int Humans => lan && gc.Net.Mode == TableNet.Role.Host ? gc.Net.Lobby.Count : 1;

        void Refresh()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var (label, cards, row) = rows[i];
                bool shown = draft ? true : i == 0;
                row.SetActive(shown);
                var src = sources[i];
                label.text = src != null ? "  " + src.Label + "   (" + src.Usable.Count() + (src.Excluded.Count > 0 ? " of " + src.Cards.Count : "") + " " + Loc.T("cards") + ")"
                           : i == 0 ? "  " + Loc.T("Choose a set or cube…") : "  " + Loc.T("Same as pack 1");
                cards.interactable = src != null;
            }
            var names = lan && gc.Net.Mode == TableNet.Role.Host ? gc.Net.Lobby.Select(p => p.Name).ToList() : new List<string> { TableNet.MyName };
            playersText.text = string.Join(", ", names) + (bots > 0 ? "  +  " + bots + " " + Loc.T("bots") : "")
                + "\n<size=13>" + (Humans + bots) + " " + Loc.T("seats") + (draft ? " · " + Loc.T("8 is a classic draft table") : " · " + Loc.T("6 packs each")) + "</size>";
            playersText.supportRichText = true;
            botsText.text = Loc.T("BOTS") + "   " + bots;
            hint.text = draft
                ? Loc.T("Three packs; take a card and pass the rest (left, right, left). Then build 40 cards from what you took.")
                : Loc.T("Everyone opens six packs and builds 40 cards from them.");
        }

        void SourceMenu(int pack, Vector2 at)
        {
            var items = new List<(string, Action)> { ("PACK " + (pack + 1), null) };
            if (pack > 0) items.Add(("Same as pack 1", () => { sources[pack] = null; Refresh(); }));
            foreach (var set in CustomSets.All)
            {
                var s = set;
                items.Add((s.Code + " · " + s.Name + " (" + s.Cards.Count + ")", () => { sources[pack] = LimitedSource.FromCustom(s); Refresh(); }));
            }
            items.Add(("---", null));
            items.Add(("A real set (type its code)…", () => { codeFor = pack; codeRow.SetActive(true); codeInput.ActivateInputField(); }));
            foreach (var cube in CubeStore.LoadAll())
            {
                var c = cube;
                items.Add(("Cube: " + c.Name + " (" + c.Count + ")", () => { sources[pack] = LimitedSource.FromList(c.Name, c.List); Refresh(); }));
            }
            items.Add(("Cube from the clipboard (one card per line)", () =>
            {
                var cube = LimitedSource.FromList("Clipboard", GUIUtility.systemCopyBuffer ?? "");
                if (cube.Cards.Count < 15) { gc.Toasts.Show(Loc.T("Copy a cube list (one card per line) first."), true); return; }
                sources[pack] = cube; Refresh();
            }));
            foreach (var deck in DeckStore.LoadAll().Where(dk => dk.CardCount >= 45).Take(12))
            {
                var dk = deck;
                items.Add(("Cube from deck: " + dk.Name + " (" + dk.CardCount + ")", () => { sources[pack] = LimitedSource.FromList(dk.Name, dk.List); Refresh(); }));
            }
            PopupMenu.Instance.Show(at, items);
        }

        void LoadCode()
        {
            string code = (codeInput.text ?? "").Trim();
            if (code.Length < 2) { gc.Toasts.Show(Loc.T("Type the set's code first."), true); return; }
            int pack = Mathf.Max(0, codeFor);
            gc.Toasts.Show(Loc.T("Loading the set from Scryfall…"));
            LimitedSession.Instance.LoadRealSet(code, src =>
            {
                if (src == null) return;
                sources[pack] = src;
                codeRow.SetActive(false);
                gc.Toasts.Show(src.Code + ": " + src.Cards.Count + " " + Loc.T("cards"));
                Refresh();
            });
        }

        void OpenPicker(int pack)
        {
            var src = sources[pack];
            if (src == null) return;
            picker.Open(src, Refresh);
        }

        void StartEvent()
        {
            if (sources[0] == null) { gc.Toasts.Show(Loc.T("Choose what the packs are made of."), true); return; }
            var e = new LimitedEvent { Draft = draft };
            for (int i = 0; i < (draft ? 3 : 1); i++) e.Sources.Add(sources[i] ?? sources[0]);
            if (e.Sources.Any(s => s.Usable.Count() < 15)) { gc.Toasts.Show(Loc.T("A pack needs at least 15 cards to choose from."), true); return; }
            int cubeNeeded = (Humans + bots) * (draft ? 3 : 6) * 15;
            if (e.Sources.Any(s => s.Type == LimitedSource.Kind.Cube) && e.Sources.First(s => s.Type == LimitedSource.Kind.Cube).Usable.Count() < cubeNeeded)
                gc.Toasts.Show(Loc.T("The cube is small for this many players: some packs will be short."));
            if (lan && gc.Net.Mode == TableNet.Role.Host)
                foreach (var p in gc.Net.Lobby) e.Seats.Add(new LimitedEvent.Seat { Name = p.Name });
            else e.Seats.Add(new LimitedEvent.Seat { Name = TableNet.MyName });
            for (int i = 0; i < bots; i++) e.Seats.Add(new LimitedEvent.Seat { Name = "Bot " + (i + 1), Bot = true });
            Close();
            LimitedSession.Instance.Begin(e, lan && gc.Net.Mode == TableNet.Role.Host);
        }
    }

    // =================================================================================================================

    /// <summary>Which cards of a set or cube can be opened: tick them in or out (search to narrow the list).</summary>
    public class CardPicker : MonoBehaviour
    {
        public static CardPicker Instance { get; private set; }
        public bool IsOpen => gameObject.activeSelf;
        LimitedSource src;
        Action done;
        RectTransform content, panel;
        InputField filter;
        Text title;

        public static CardPicker Create(RectTransform parent)
        {
            var shade = UIKit.Panel("CardPicker", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.Stretch();
            var d = shade.gameObject.AddComponent<CardPicker>();
            Instance = d;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = d.Close;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            d.panel = p.rectTransform;
            d.panel.anchorMin = d.panel.anchorMax = new Vector2(0.5f, 0.5f);
            d.panel.sizeDelta = new Vector2(900, 820);
            p.gameObject.AddComponent<PointerEvents>();
            d.title = UIKit.Label("Title", p.transform, "", 26, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            d.title.rectTransform.Stretch(40, 0, 40, 26);
            d.filter = UIKit.Input("Filter", p.transform, "Filter: name, type, rarity...", false, 17);
            ((RectTransform)d.filter.transform).Place(40, 690, 460, 48);
            d.filter.onValueChanged.AddListener(_ => d.Rebuild());
            var all = UIKit.Button("All", p.transform, "ALL SHOWN IN", Theme.Secondary, () => d.SetAll(true), 14);
            ((RectTransform)all.transform).Place(512, 690, 170, 48);
            var none = UIKit.Button("None", p.transform, "ALL SHOWN OUT", Theme.Secondary, () => d.SetAll(false), 14);
            ((RectTransform)none.transform).Place(692, 690, 170, 48);
            var view = UIKit.Rect("Scroll", p.transform);
            view.Place(40, 110, 822, 566);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            d.content = UIKit.Rect("List", view);
            d.content.anchorMin = new Vector2(0, 1); d.content.anchorMax = new Vector2(1, 1); d.content.pivot = new Vector2(0.5f, 1);
            sr.content = d.content; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);
            var ok = UIKit.Button("Done", p.transform, "DONE", Theme.Accent, d.Close, 18);
            ((RectTransform)ok.transform).Place(642, 30, 220, 56);
            UIKit.CloseX(p.transform, d.Close);
            MenuKit.Bandify(p);
            shade.gameObject.SetActive(false);
            return d;
        }

        public void Open(LimitedSource s, Action onDone)
        {
            src = s; done = onDone;
            filter.SetTextWithoutNotify("");
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Rebuild();
        }

        public void Close()
        {
            gameObject.SetActive(false);
            done?.Invoke();
        }

        IEnumerable<LimitedCard> Shown()
        {
            string f = (filter.text ?? "").Trim();
            return src.Cards.Where(c => f.Length == 0 || c.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                || c.Type.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || c.Rarity.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        }

        void SetAll(bool on)
        {
            foreach (var c in Shown()) { if (on) src.Excluded.Remove(c.Name); else src.Excluded.Add(c.Name); }
            Rebuild();
        }

        void Rebuild()
        {
            if (src == null) return;
            title.text = src.Label + "  ·  " + src.Usable.Count() + " / " + src.Cards.Count + " " + Loc.T("cards in packs");
            foreach (Transform ch in content) Destroy(ch.gameObject);
            var list = Shown().OrderBy(c => c.Name).ToList();
            float y = -4;
            foreach (var c in list.Take(600))
            {
                var card = c;
                bool on = !src.Excluded.Contains(c.Name);
                var row = UIKit.Button("Row", content, "", on ? new Color(0.16f, 0.22f, 0.17f) : new Color(0.12f, 0.12f, 0.13f), () =>
                {
                    if (!src.Excluded.Remove(card.Name)) src.Excluded.Add(card.Name);
                    Rebuild();
                }, 15);
                var rr = (RectTransform)row.transform;
                rr.anchorMin = new Vector2(0, 1); rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(0.5f, 1);
                rr.sizeDelta = new Vector2(-24, 34); rr.anchoredPosition = new Vector2(-6, y);
                var lbl = UIKit.LabelOf(row);
                lbl.alignment = TextAnchor.MiddleLeft;
                lbl.text = (on ? "✓  " : "     ") + c.Name + "   <color=#9aa0aa><size=12>" + c.Type + "</size></color>";
                lbl.supportRichText = true;
                if (lbl is SymbolText st) st.Localize = false;
                lbl.color = on ? Theme.Text : new Color(1, 1, 1, 0.4f);
                var rar = UIKit.Label("Rarity", row.transform, c.Rarity.Length > 0 ? c.Rarity.Substring(0, 1).ToUpperInvariant() : "", 14, RarityColor(c.Rarity), TextAnchor.MiddleRight, FontStyle.Bold);
                rar.rectTransform.Stretch(0, 0, 14, 0);
                var pe = row.gameObject.AddComponent<PointerEvents>();
                pe.OnEnter = () => AltZoom.Enter(Data(card));
                pe.OnExit = () => AltZoom.Exit(null);
                y -= 38;
            }
            content.sizeDelta = new Vector2(0, -y + 6);
        }

        public static Color RarityColor(string r) => r == "mythic" ? new Color(1f, 0.45f, 0.2f) : r == "rare" ? new Color(1f, 0.82f, 0.35f)
            : r == "uncommon" ? new Color(0.75f, 0.82f, 0.9f) : new Color(0.6f, 0.6f, 0.64f);

        public static CardData Data(LimitedCard c)
        {
            var info = CardCatalog.Get(c.Name);
            if (info == null) { info = c.ToInfo(); CardCatalog.Remember(info); }
            return info.ToCardData();
        }
    }

    // =================================================================================================================

    /// <summary>
    /// The draft screen: the pack in front of you (click a card to choose it, click again or press PICK to take it),
    /// what you've taken so far, and who's still picking. Sealed / end of draft: build your deck.
    /// </summary>
    public class DraftView : MonoBehaviour
    {
        public static DraftView Instance { get; private set; }
        GameController gc;
        RectTransform packArea, picksContent, seatsRow;
        Text title, sub, waiting, picksTitle;
        Button pickButton, buildButton;
        RectTransform pickHolder, buildHolder;
        string chosen;
        LimitedView view;
        readonly Dictionary<string, Image> tiles = new Dictionary<string, Image>();

        public bool IsOpen => gameObject.activeSelf;

        public static DraftView Create(RectTransform parent, GameController gc)
        {
            var shade = MenuKit.Shade("DraftView", parent);
            shade.color = new Color(0.02f, 0.02f, 0.025f, 0.96f);
            var v = shade.gameObject.AddComponent<DraftView>();
            Instance = v;
            v.gc = gc;
            var frame = UIKit.Rect("Frame", shade.transform);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(1920, 1080);

            v.title = UIKit.Label("Title", frame, "", 44, MenuKit.TitleColor, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            v.title.rectTransform.Place(60, 980, 1000, 70);
            v.sub = UIKit.Label("Sub", frame, "", 18, Theme.TextDim, TextAnchor.UpperLeft);
            v.sub.rectTransform.Place(62, 946, 1100, 32);
            v.seatsRow = UIKit.Rect("Seats", frame);
            v.seatsRow.Place(1080, 960, 800, 80);

            v.packArea = UIKit.Rect("Pack", frame);
            v.packArea.Place(60, 390, 1800, 540);
            v.waiting = UIKit.Label("Waiting", frame, "", 26, Theme.TextDim, TextAnchor.MiddleCenter);
            v.waiting.rectTransform.Place(60, 560, 1800, 120);

            // Each pill in its own holder: a pill's glow is a sibling, so hiding just the button left the glow behind.
            v.pickHolder = UIKit.Rect("PickHolder", frame).Stretch();
            v.buildHolder = UIKit.Rect("BuildHolder", frame).Stretch();
            v.pickButton = UIKit.Pill("Pick", v.pickHolder, "Pick", new Color(0.95f, 0.43f, 0.1f), v.ConfirmPick, 1580, 290, 280, 80, 34);
            v.buildButton = UIKit.Pill("Build", v.buildHolder, "Build your deck", new Color(0.95f, 0.43f, 0.1f), () => LimitedSession.Instance.BuildDeck(), 1460, 290, 400, 80, 30);

            v.picksTitle = UIKit.Label("PicksTitle", frame, "", 15, new Color(1f, 0.7f, 0.38f), TextAnchor.LowerLeft, FontStyle.Bold);
            v.picksTitle.rectTransform.Place(60, 250, 1000, 26);
            var picksView = UIKit.Rect("Picks", frame);
            picksView.Place(60, 30, 1800, 216);
            picksView.gameObject.AddComponent<RectMask2D>();
            picksView.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.03f);
            var sr = picksView.gameObject.AddComponent<ScrollRect>();
            v.picksContent = UIKit.Rect("Content", picksView);
            v.picksContent.anchorMin = new Vector2(0, 0); v.picksContent.anchorMax = new Vector2(0, 1); v.picksContent.pivot = new Vector2(0, 0.5f);
            sr.content = v.picksContent; sr.vertical = false; sr.horizontal = true; sr.scrollSensitivity = 60; sr.movementType = ScrollRect.MovementType.Clamped;

            var leave = UIKit.IconButton("Leave", frame, Icons.Cross, 1920 - 70, 1080 - 70, 52, () => v.AskLeave(), "Leave");
            leave.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            shade.gameObject.SetActive(false);
            return v;
        }

        public void Show(LimitedView v)
        {
            bool first = !gameObject.activeSelf;
            view = v;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (first) FadeIn.Play(gameObject, Motion.Base);
            bool over = v.Done || !v.Draft;
            title.text = !v.Draft ? Loc.T("Sealed") : over ? Loc.T("Draft complete") : Loc.T("Pack") + " " + (v.Round + 1) + "  ·  " + Loc.T("Pick") + " " + v.Pick;
            sub.text = v.Source + (v.Draft && !over ? "   ·   " + Loc.T("passing") + " " + (v.Pass == "left" ? "← " + Loc.T("left") : Loc.T("right") + " →") : "");
            // Seats: who's still picking.
            foreach (Transform ch in seatsRow) Destroy(ch.gameObject);
            float x = 0;
            foreach (var (name, bot, waitingPacks, picks) in v.Seats.Take(12))
            {
                var chip = UIKit.Panel("Seat", seatsRow, waitingPacks > 0 ? new Color(0.3f, 0.2f, 0.08f, 0.9f) : new Color(1, 1, 1, 0.06f));
                chip.rectTransform.Place(x, 40, 128, 34);
                var l = UIKit.Label("Name", chip.transform, (bot ? "🤖 " : "") + name, 13, waitingPacks > 0 ? Theme.Text : Theme.TextDim, TextAnchor.MiddleCenter);
                l.rectTransform.Stretch(4, 0, 4, 0);
                if (l is SymbolText st) st.Localize = false;
                x += 134;
                if (x > 780) break;
            }
            // The pack.
            foreach (Transform ch in packArea) Destroy(ch.gameObject);
            tiles.Clear();
            chosen = null;
            const float w = 196, h = 274, gap = 14;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((1800 + gap) / (w + gap)));
            for (int i = 0; i < v.Pack.Count && !over; i++)
            {
                var c = v.Pack[i];
                var data = CardPicker.Data(c);
                var tile = CardTile.Create(packArea, data, w, h, () => Choose(c.Name), cd => { if (cd != null) gc.Preview.Request(cd, true); else gc.Preview.Hide(); });
                var rt = tile.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
                rt.anchoredPosition = new Vector2((i % perRow) * (w + gap), -(i / perRow) * (h + gap));
                tiles[c.Name] = tile;
            }
            waiting.text = over ? "" : v.Pack.Count == 0 ? Loc.T("Waiting for the next pack…") : "";
            pickHolder.gameObject.SetActive(!over && v.Pack.Count > 0);
            pickButton.interactable = false;
            buildHolder.gameObject.SetActive(over);
            // Picks so far.
            foreach (Transform ch in picksContent) Destroy(ch.gameObject);
            picksTitle.text = (over ? Loc.T("YOUR CARDS") : Loc.T("YOUR PICKS")) + " (" + v.Picks.Count + ")";
            float px = 8;
            foreach (var c in v.Picks.OrderBy(c => ColorOrder(c)).ThenBy(c => c.Cmc))
            {
                var t = CardTile.Create(picksContent, CardPicker.Data(c), 140, 196, null, cd => { if (cd != null) gc.Preview.Request(cd, false); else gc.Preview.Hide(); });
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
                rt.anchoredPosition = new Vector2(px, 0);
                px += 92;   // overlapping, like a fanned pile
            }
            picksContent.sizeDelta = new Vector2(px + 60, 0);
            if (over && first == false) Sound.Play(Sfx.Victory, 0.4f);
        }

        static int ColorOrder(LimitedCard c)
        {
            if (c.Land) return 9;
            var cols = c.Colors.Where(ch => "WUBRG".IndexOf(ch) >= 0).ToList();
            if (cols.Count == 0) return 7;
            if (cols.Count > 1) return 6;
            return "WUBRG".IndexOf(cols[0]);
        }

        void Choose(string name)
        {
            if (chosen == name) { ConfirmPick(); return; }   // second click on the same card takes it
            chosen = name;
            foreach (var kv in tiles)
            {
                var o = kv.Value.GetComponent<UnityEngine.UI.Outline>() ?? kv.Value.gameObject.AddComponent<UnityEngine.UI.Outline>();
                o.effectColor = kv.Key == name ? Theme.Chosen : Color.clear;
                o.effectDistance = new Vector2(5, -5);
            }
            pickButton.interactable = true;
            Sound.Play(Sfx.UiClick, 0.5f);
        }

        void ConfirmPick()
        {
            if (string.IsNullOrEmpty(chosen)) return;
            string c = chosen;
            chosen = null;
            pickButton.interactable = false;
            gc.Preview.Hide();
            LimitedSession.Instance.Pick(c);
        }

        public void AskLeave()
        {
            ConfirmDialog.Instance.Show(Loc.T("Leave the draft?"), Loc.T("Your picks so far are lost."),
                (Loc.T("Leave"), ConfirmDialog.Danger, () => LimitedSession.Instance.Abandon()),
                (Loc.T("Keep drafting"), Theme.Secondary, null));
        }

        public void Close() { gc.Preview.Hide(); gameObject.SetActive(false); }
    }
}

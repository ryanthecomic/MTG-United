using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Kitchen Table mode: a free table like Cockatrice. No rules engine and no Forge needed; you move the cards.
    /// Works with real cards (images from Scryfall) and custom cards imported from Magic Set Editor.
    ///
    /// You (the seat you're viewing) sit at the bottom: hand, a free-form battlefield, piles on the right, life at the
    /// bottom-left, a log on the left. The other seats are across the top. Several local players can share the table;
    /// the view turns to whoever's turn it is.
    ///
    /// Cards: drag anywhere on your battlefield, onto a pile, back to hand, or to another player's side (gives
    /// control). Click a permanent to tap/untap; double-click a hand card to play it; right-click anything for actions.
    /// </summary>
    public class TableView : MonoBehaviour
    {
        // Board-space layout (1920x1080, bottom-left origin).
        const float FieldX = 280, FieldY = 215, FieldW = 1280, FieldH = 375;
        const float HandRight = 1560, HandY = 70;
        static float HandLeft => 2 + 408 * Mathf.Min(UiScale.Value, 1.2f);   // just right of my nameplate, which grows with the UI
        const float OppBandY = 605, OppFieldTop = 944;   // below the opponents' nameplates
        static readonly Rect LibBase = new Rect(1592, 392, 130, 182), GyBase = new Rect(1752, 392, 130, 182);
        static readonly Rect ExBase = new Rect(1592, 202, 130, 182), CmdBase = new Rect(1752, 202, 130, 182);
        // The right column (piles + action buttons) grows with Options > interface size, anchored bottom-right.
        static readonly Vector2 RightCorner = new Vector2(1912, 8);
        const float RightMax = 1.15f;
        static Rect Scaled(Rect r)
        {
            float k = Mathf.Min(UiScale.Value, RightMax);
            var min = RightCorner + (r.min - RightCorner) * k;
            return new Rect(min, r.size * k);
        }
        static Rect LibRect => Scaled(LibBase);
        static Rect GyRect => Scaled(GyBase);
        static Rect ExRect => Scaled(ExBase);
        static Rect CmdRect => Scaled(CmdBase);

        GameController gc;
        RectTransform root, cardLayer, uiLayer, oppLayer;
        public TableState State { get; private set; }
        int viewSeat;            // index of the player shown at the bottom
        bool followTurn = true;

        readonly Dictionary<int, CardView> views = new Dictionary<int, CardView>();
        readonly Dictionary<int, string> lastZone = new Dictionary<int, string>();
        TableZoneViewer viewer;
        TokenDialog tokenDialog;

        // my HUD
        Text logText, turnText;
        PlayerPanel myPanel;
        readonly Dictionary<string, (Image frame, RawImage top, Text count)> piles = new Dictionary<string, (Image, RawImage, Text)>();
        readonly List<OppPanel> oppPanels = new List<OppPanel>();

        public bool IsOpen => gameObject.activeSelf;
        public TablePlayer Me => State != null && State.Players.Count > 0 ? State.Players[viewSeat] : null;

        public static TableView Create(RectTransform board, GameController gc)
        {
            var rt = UIKit.Rect("KitchenTable", board).Stretch();
            var t = rt.gameObject.AddComponent<TableView>();
            t.gc = gc;
            t.root = rt;
            t.Build();
            rt.gameObject.SetActive(false);
            return t;
        }

        // =================================================================================================== build

        void Build()
        {
            var field = UIKit.Panel("MyField", root, new Color(1f, 1f, 1f, 0.035f));
            field.rectTransform.Place(FieldX - 10, FieldY - 6, FieldW + 20, FieldH + 12);
            field.raycastTarget = false;
            var oppBg = UIKit.Panel("OppBand", root, new Color(1f, 1f, 1f, 0.02f));
            oppBg.rectTransform.Place(6, OppBandY, 1908, 1080 - OppBandY - 6);
            oppBg.raycastTarget = false;

            // Empty table: drag a box to select cards; a plain click clears the selection.
            var area = UIKit.Rect("SelectArea", root).Stretch();
            area.gameObject.AddComponent<Image>().color = Color.clear;
            area.gameObject.AddComponent<PointerEvents>().OnClick = ClearSelection;
            area.gameObject.AddComponent<MarqueeHandler>().Table = this;
            area.SetAsFirstSibling();

            oppLayer = UIKit.Rect("Opponents", root).Stretch().SubCanvas();
            uiLayer = UIKit.Rect("UI", root).Stretch().SubCanvas();
            cardLayer = UIKit.Rect("Cards", root).Stretch().SubCanvas();
            noteLayer = UIKit.Rect("Notes", root).Stretch().SubCanvas();   // notes ride above the cards

            // ---- my nameplate (bottom-left): the same Arena-style panel as Forge games (seat colour, commander
            //      art, life badge with − / +, zone symbols). Right-click it for poison and seat options.
            myPanel = PlayerPanel.Create(uiLayer, gc, false);
            myPanel.MoveTo(16, 16, 400, 130, true);
            myPanel.AddLifeButtons(d => Life(Me, d));
            myPanel.EnableLifeEdit(v => { if (Me != null) Run(A("setlife", "p", Me.Id, "v", v)); });
            myPanel.ZoneClicked = z => ZoneClick(Me, z);
            myPanel.Clicked = () => { };   // (Ctrl+click another seat's nameplate to sit there)
            myPanel.RightClicked = () => PlayerMenu(Me, Input.mousePosition);
            UiScaled.Add(myPanel, new Vector2(8, 8)).MaxScale = 1.2f;
            turnText = UIKit.Label("Turn", uiLayer, "", 16, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            turnText.rectTransform.Place(24, 150, 380, 40);
            UiScaled.Add(turnText, new Vector2(8, 8)).MaxScale = 1.2f;
            turnText.supportRichText = true;

            // ---- log (left column)
            var logBg = UIKit.Panel("Log", uiLayer, new Color(0.04f, 0.04f, 0.07f, 0.75f));
            logBg.rectTransform.Place(16, 200, 250, 392);
            UiScaled.Add(logBg, new Vector2(8, 600)).MaxScale = 1.08f;
            var lt = UIKit.Label("Title", logBg.transform, "TABLE LOG", 13, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            lt.rectTransform.Stretch(12, 0, 8, 8);
            logText = UIKit.Label("Lines", logBg.transform, "", 13, new Color(0.82f, 0.85f, 0.9f), TextAnchor.LowerLeft);
            logText.rectTransform.Stretch(12, 10, 10, 30);
            logText.verticalOverflow = VerticalWrapMode.Truncate;
            if (logText is SymbolText lst) lst.KeepTruncate = true;

            // ---- piles (right column)
            Pile("Library", LibBase);
            Pile("Graveyard", GyBase);
            Pile("Exile", ExBase);
            Pile("Command", CmdBase);

            // ---- actions (bottom-right)
            float bx = 1592, by = 136;
            void Act(string label, Color c, Action a, string key)
            {
                var b = UIKit.Button(label, uiLayer, label, c, () => a(), 16);
                ((RectTransform)b.transform).Place(bx, by, 142, 40);
                UiScaled.Add(b, RightCorner).MaxScale = RightMax;
                // Its shortcut, faint in the corner.
                var k = UIKit.Label("Key", b.transform, key, 11, new Color(1f, 1f, 1f, 0.45f), TextAnchor.LowerRight, FontStyle.Bold);
                k.rectTransform.Stretch(0, 2, 6, 0);
                bx += 150;
                if (bx > 1800) { bx = 1592; by -= 46; }
            }
            Act("Next turn", Theme.Accent, NextTurn, "N");
            Act("Untap all", Theme.Secondary, UntapAll, "X");
            Act("Draw", Theme.Secondary, () => Draw(1), "C");
            Act("Shuffle", Theme.Secondary, Shuffle, "V");
            Act("Token", Theme.Secondary, OpenTokenDialog, "G");
            Act("Dice & coin", Theme.Secondary, () => DiceMenu(Input.mousePosition), "R");

            // Settings gear in the top-right corner, like everywhere else: opens the game menu.
            var gear = UIKit.IconButton("Menu", uiLayer, Icons.Gear, 1920 - 12 - 56, 1080 - 12 - 56, 56, () => gc.GameMenu.Open(), "Menu");
            gear.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            UiScaled.Add(gear, new Vector2(1912, 1072));

            viewer = TableZoneViewer.Create(root, gc);
            tokenDialog = TokenDialog.Create(root);
            noteDialog = NoteDialog.Create(root);
        }

        void Pile(string zone, Rect r)
        {
            var frame = UIKit.Panel(zone, uiLayer, new Color(0.1f, 0.12f, 0.18f, 0.9f));
            frame.rectTransform.Place(r.x, r.y, r.width, r.height);
            UiScaled.Add(frame, RightCorner).MaxScale = RightMax;
            UIKit.RoundCard(frame, r.width);
            var top = UIKit.Rect("Top", frame.transform).Stretch(4, 4, 4, 4).gameObject.AddComponent<RawImage>();
            top.raycastTarget = false;
            top.color = new Color(1, 1, 1, 0.75f);
            var count = UIKit.Label("Count", frame.transform, "", 15, Color.white, TextAnchor.LowerCenter, FontStyle.Bold).Outline(1.5f);
            count.rectTransform.Stretch(2, 6, 2, 2);
            // Zone symbol in a dark disc at the top, same symbols as the nameplates.
            var disc = UIKit.Panel("Disc", frame.transform, new Color(0.02f, 0.03f, 0.06f, 0.85f));
            var dr = disc.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 1); dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(40, 40); dr.anchoredPosition = new Vector2(0, -26);
            disc.pixelsPerUnitMultiplier = 32f / 40f;
            disc.raycastTarget = false;
            var icon = UIKit.Rect("Icon", disc.transform).Stretch(8, 8, 8, 8).gameObject.AddComponent<Image>();
            icon.sprite = zone == "Library" ? Icons.Library : zone == "Graveyard" ? Icons.Grave : zone == "Exile" ? Icons.Spiral : Icons.Crown;
            icon.color = new Color(0.88f, 0.9f, 0.95f); icon.preserveAspect = true; icon.raycastTarget = false;
            var rim = frame.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(1f, 1f, 1f, 0.1f); rim.effectDistance = new Vector2(1, -1);
            frame.gameObject.AddComponent<HoverPop>().Scale = 1.05f;
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            string z = zone;
            pe.OnClick = () => PileClick(z);
            pe.OnRightClick = () => PileMenu(z, Input.mousePosition);
            var pop = frame.GetComponent<HoverPop>();
            pe.OnEnter = () => { pop.Hovered = true; hoveredPile = z; };
            pe.OnExit = () => { pop.Hovered = false; if (hoveredPile == z) hoveredPile = null; };
            // Drag the pile: the top card comes with you (click still draws / opens it, as before).
            var drag = frame.gameObject.AddComponent<PileDragHandler>();
            drag.Table = this; drag.Zone = zone;
            piles[zone] = (frame, top, count);
        }

        // =================================================================================================== start / leave

        /// <summary>Starts a new table: one seat per (name, deck); everyone shuffles and draws seven.</summary>
        public void Begin(List<(string name, SavedDeck deck)> seats)
        {
            Clear();
            State = new TableState();
            foreach (var (name, deck) in seats) State.AddPlayer(name, deck);
            foreach (var p in State.Players) State.Draw(p, 7);
            State.Say("— Turn 1: " + State.Players[0].Name + " —");
            viewSeat = 0;
            StartView();
        }

        /// <summary>LAN host: the table dealt by <see cref="TableNet.StartGame"/>; you sit in seat 1.</summary>
        public void BeginNetwork(TableState state)
        {
            Clear();
            State = state;
            viewSeat = 0;
            StartView();
            gc.Net.BroadcastState(State);
        }

        void StartView()
        {
            OpenView();
            BuildOpponentPanels();
            // Images and details for every card in play (custom cards are already local).
            CardCatalog.Ensure(State.Cards.Values.Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct(), Render);
            Render();
            Sound.Play(Sfx.DrawMany, 0.6f);
        }

        void OpenView()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            gc.BoardView.gameObject.SetActive(false);
            gc.Hud.gameObject.SetActive(false);
            gc.Menu.Hide();
            gc.Lan?.Hide();
            gc.ScreenFx.MatchIntro();
        }

        /// <summary>Esc: closes the table's own pop-ups first. Returns true if one was open.</summary>
        public bool CloseTopDialog()
        {
            if (noteDialog.IsOpen) { noteDialog.Close(); return true; }
            if (tokenDialog.IsOpen) { tokenDialog.Close(); return true; }
            if (viewer.IsOpen) { viewer.Close(); return true; }
            return false;
        }

        public void Leave()
        {
            if (gc.Net != null && gc.Net.Mode != TableNet.Role.None) gc.Net.Stop();
            Clear();
            State = null;
            gameObject.SetActive(false);
            gc.BoardView.gameObject.SetActive(true);
            gc.Hud.gameObject.SetActive(true);
            gc.Menu.Show();
        }

        void Clear()
        {
            foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
            views.Clear();
            lastZone.Clear();
            foreach (var n in noteLabels.Values) if (n != null) Destroy(n.transform.parent.gameObject);
            noteLabels.Clear();
            foreach (var o in oppPanels) Destroy(o.Root.gameObject);
            oppPanels.Clear();
        }

        // =================================================================================================== actions

        /// <summary>True when this table is a LAN game (you're hosting or joined one).</summary>
        public bool Networked => gc.Net != null && gc.Net.Mode != TableNet.Role.None && gc.Net.Started;
        bool IsClient => Networked && gc.Net.Mode == TableNet.Role.Client;

        /// <summary>An action as data: ints/floats become doubles so it reads the same locally and after JSON.</summary>
        static Dictionary<string, object> A(string op, params object[] kv)
        {
            var d = new Dictionary<string, object> { { "op", op } };
            for (int i = 0; i + 1 < kv.Length; i += 2)
            {
                object v = kv[i + 1];
                if (v is int n) v = (double)n; else if (v is float f) v = (double)f;
                d[(string)kv[i]] = v;
            }
            return d;
        }

        /// <summary>
        /// Every change goes through here. Hot-seat / host: undo point, apply, redraw, send everyone the new table.
        /// Joined player: the action goes to the host, and the table redraws when the host's update arrives.
        /// </summary>
        void Run(Dictionary<string, object> a)
        {
            if (Me == null) return;
            ErrorReporter.Crumb("table: " + a.Str("op"));
            a["by"] = (double)Me.Id;
            if (IsClient) { gc.Net.SendAction(a); return; }
            ApplyHere(a);
        }

        void ApplyHere(Dictionary<string, object> a)
        {
            string op = a.Str("op");
            bool checkpoint = op != "undo" && op != "say";
            if (checkpoint) State.Checkpoint();
            try { State.Apply(a); }
            catch (Exception ex)
            {
                // A half-applied action would leave the table (and everyone's copy of it) inconsistent:
                // put it back the way it was, then carry on.
                ErrorReporter.Report(ex, "applying the table action '" + op + "'");
                if (checkpoint) State.Undo(false);
                State.Events.Clear();
            }
            AfterChange();
        }

        void AfterChange()
        {
            var events = State.Events.ToList();
            State.Events.Clear();
            foreach (var e in events) ShowEvent(e);
            Render();
            if (Networked && gc.Net.Mode == TableNet.Role.Host)
            {
                gc.Net.BroadcastState(State);
                foreach (var e in events) gc.Net.BroadcastEvent(e);
            }
        }

        /// <summary>Host: an action from a joined player.</summary>
        public void ApplyRemote(Dictionary<string, object> a)
        {
            if (State == null) return;
            ApplyHere(a);
            RemoteSound(a.Str("op"));
        }

        /// <summary>Joined player: the host's latest table.</summary>
        public void ApplySnapshot(TableState s, int mySeatId)
        {
            bool first = State == null || !IsOpen;
            if (first) { Clear(); State = s; OpenView(); }
            else
            {
                int before = State.Log.Count;
                string lastBefore = before > 0 ? State.Log[before - 1] : "";
                State = s;
                string lastNow = s.Log.Count > 0 ? s.Log[s.Log.Count - 1] : "";
                if (lastNow != lastBefore) Sound.Play(Sfx.CardPlace, 0.35f);
            }
            int idx = State.Players.FindIndex(p => p.Id == mySeatId);
            viewSeat = Mathf.Max(0, idx);
            if (first) BuildOpponentPanels();
            CardCatalog.Ensure(State.Cards.Values.Select(c => c.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct(), Render);
            Render();
        }

        /// <summary>Pop-ups everyone sees: dice results and reveals.</summary>
        public void ShowEvent(Dictionary<string, object> e)
        {
            string text = e.Str("text");
            if (e.Str("kind") == "reveal")
            {
                var cards = e.Arr("cards").Select(o => ToData(new TableCard { Id = -1, Name = o as string ?? "", Zone = "Hand" }, false)).ToList();
                gc.Reveals.Show(text, cards);
            }
            else gc.Toasts.Show(text);
            Sound.Play(Sfx.Popup, 0.7f);
        }

        static void RemoteSound(string op)
        {
            switch (op)
            {
                case "tap": Sound.Play(Sfx.Tap, 0.5f); break;
                case "draw": Sound.QueueDraw(1); break;
                case "next": Sound.Play(Sfx.TurnStart, 0.6f); break;
                case "life": Sound.Play(Sfx.LifeLoss, 0.4f); break;
                case "token": case "clone": Sound.Play(Sfx.Token, 0.6f); break;
                case "counter": Sound.Play(Sfx.Counter, 0.6f); break;
                default: Sound.Play(Sfx.CardPlace, 0.4f); break;
            }
        }

        void Draw(int n) { if (Me.Library.Count > 0) Sound.QueueDraw(n); Run(A("draw", "n", n)); }

        void Shuffle() { Sound.Play(Sfx.DrawMany, 0.7f); Run(A("shuffle")); }

        void UntapAll() { Sound.Play(Sfx.Untap, 0.7f); Run(A("untapall")); }

        void NextTurn()
        {
            Sound.Play(Sfx.TurnStart, 0.7f);
            Sound.QueueDraw(1);
            Run(A("next"));
            if (followTurn && !Networked) { viewSeat = State.Active; Render(); }
        }

        void Life(TablePlayer p, int delta)
        {
            if (p == null) return;
            Sound.Play(delta < 0 ? Sfx.LifeLoss : Sfx.LifeGain, 0.5f);
            Run(A("life", "p", p.Id, "d", delta));
        }

        void MoveCard(int id, string zone, int toPlayer, int index = 0, Vector2? pos = null)
        {
            PlayMoveSound(id, zone);
            var a = A("move", "id", id, "zone", zone, "to", toPlayer, "index", index);
            if (pos.HasValue) { a["x"] = (double)pos.Value.x; a["y"] = (double)pos.Value.y; }
            Run(a);
        }

        void PlayMoveSound(int id, string zone)
        {
            switch (zone)
            {
                case "Graveyard": Sound.Play(Sfx.Die, 0.8f); break;
                case "Exile": Sound.Play(Sfx.Exile, 0.8f); break;
                case "Library": Sound.Play(Sfx.ToLibrary, 0.7f); break;
                case "Hand": Sound.Play(Sfx.CardSlide, 0.5f); break;
                case "Battlefield":
                    var info = State.Cards.TryGetValue(id, out var c) ? CardCatalog.Get(c.Name) : null;
                    Sound.Play(info != null && info.Land ? Sfx.LandPlay : Sfx.Resolve, 0.8f);
                    break;
                default: Sound.Play(Sfx.CardSlide, 0.5f); break;
            }
        }

        void ToggleTap(int id)
        {
            if (!State.Cards.TryGetValue(id, out var c) || c.Zone != "Battlefield") return;
            Sound.Play(!c.Tapped ? Sfx.Tap : Sfx.Untap, 0.7f);
            Run(A("tap", "id", id, "v", !c.Tapped));
        }

        void OpenTokenDialog()
        {
            tokenDialog.Show((name, pt, colors) =>
            {
                Sound.Play(Sfx.Token, 0.8f);
                Run(A("token", "to", Me.Id, "name", name, "pt", pt ?? "", "colors", colors ?? ""));
            });
        }

        void DiceMenu(Vector2 at)
        {
            PopupMenu.Instance.Reopen(() => DiceMenu(at)).Show(at, new List<(string, Action)>
            {
                ("Roll d6", PopupMenu.Stay(() => Run(A("roll", "sides", 6)))),
                ("Roll d20", PopupMenu.Stay(() => Run(A("roll", "sides", 20)))),
                ("Flip a coin", PopupMenu.Stay(() => Run(A("coin")))),
                ("Choose a random player", () => Run(A("randomplayer"))),
            });
        }

        // ---------------------------------------------------------------- piles

        void PileClick(string zone)
        {
            switch (zone)
            {
                case "Library": Draw(1); break;
                default: ShowZone(Me, zone); break;
            }
        }

        /// <summary>A pile's right-click menu (shortcut keys work while the mouse is over the pile).</summary>
        void PileMenu(string zone, Vector2 at)
        {
            var items = new List<(string, Action)>();
            Action Stay(Action x) => PopupMenu.Stay(x);
            if (zone == "Library")
            {
                items.Add(("LIBRARY", null));
                items.Add(("View", () => ShowZone(Me, "Library")));
                items.Add(("Draw\tC", Stay(() => Draw(1))));
                items.Add(("Draw X…", () => NumberMenu("DRAW", at, n => Draw(n))));
                items.Add(("Scry X…", () => NumberMenu("SCRY", at, n => ShowLookAt("Scry", n))));
                items.Add(("Surveil X…", () => NumberMenu("SURVEIL", at, n => ShowLookAt("Surveil", n))));
                items.Add(("Mill X…", () => NumberMenu("MILL", at, n => { Sound.Play(Sfx.Die, 0.6f); Run(A("mill", "n", n)); })));
                items.Add(("Play the top card\tP", Stay(() => TopCard("Library", id => MoveCard(id, "Battlefield", Me.Id)))));
                items.Add(("Shuffle\tV", Stay(Shuffle)));
                items.Add(("Mulligan\tM", () => { Sound.Play(Sfx.DrawMany, 0.6f); Run(A("mulligan")); }));
                items.Add(("Reveal the top card", RevealTop));
                items.Add(("---", null));
                AddTopCardMoves(items, "Library");
            }
            else
            {
                items.Add((zone.ToUpperInvariant(), null));
                items.Add(("View", () => ShowZone(Me, zone)));
                items.Add(("---", null));
                AddTopCardMoves(items, zone);
                items.Add(("---", null));
                if (zone == "Graveyard") items.Add(("Exile the whole graveyard", () => { Sound.Play(Sfx.Exile, 0.7f); Run(A("exilegy")); }));
                if (zone == "Graveyard" || zone == "Exile")
                    items.Add(("Shuffle all into library", () => { Sound.Play(Sfx.DrawMany, 0.7f); Run(A("zonetolib", "zone", zone)); }));
            }
            PopupMenu.Instance.Reopen(() => PileMenu(zone, at)).Show(at, items);
        }

        /// <summary>Moves for a pile's top card (same keys as on a card): H D S T Y B.</summary>
        void AddTopCardMoves(List<(string, Action)> items, string zone)
        {
            Action Stay(Action x) => PopupMenu.Stay(x);
            if (zone != "Hand") items.Add(("Hand\tH", Stay(() => TopCard(zone, id => MoveCard(id, "Hand", Me.Id)))));
            if (zone != "Battlefield" && zone != "Library") items.Add(("Play\tP", Stay(() => TopCard(zone, id => MoveCard(id, "Battlefield", Me.Id)))));
            if (zone != "Graveyard") items.Add(("Discard\tD", Stay(() => TopCard(zone, id => MoveCard(id, "Graveyard", Me.Id)))));
            if (zone != "Exile") items.Add(("Exile\tS", Stay(() => TopCard(zone, id => MoveCard(id, "Exile", Me.Id)))));
            if (zone != "Library") items.Add(("To deck top\tT", Stay(() => TopCard(zone, id => MoveCard(id, "Library", Me.Id, 0)))));
            items.Add(("To deck bottom\tY", Stay(() => TopCard(zone, id => MoveCard(id, "Library", Me.Id, -1)))));
            if (zone != "Command") items.Add(("Command zone\tB", Stay(() => TopCard(zone, id => MoveCard(id, "Command", Me.Id)))));
        }

        /// <summary>Runs an action on the top card of one of my piles (library: the top; others: the last one in).</summary>
        void TopCard(string zone, Action<int> act)
        {
            var ids = Me?.ZoneList(zone);
            if (ids == null || ids.Count == 0) return;
            act(zone == "Library" ? ids[0] : ids[ids.Count - 1]);
        }

        /// <summary>Pick a number (Draw X, Scry X, Mill X...).</summary>
        void NumberMenu(string title, Vector2 at, Action<int> pick)
        {
            var items = new List<(string, Action)> { (title + " HOW MANY?", null) };
            foreach (int n in new[] { 1, 2, 3, 4, 5, 6, 7, 10 }) { int k = n; items.Add((k.ToString(), () => pick(k))); }
            PopupMenu.Instance.Show(at, items);
        }

        int lookMoved;

        /// <summary>Scry / surveil: the top N cards in the viewer; move each from its right-click menu (bottom, graveyard...).</summary>
        void ShowLookAt(string what, int n, bool fresh = true)
        {
            if (fresh)
            {
                lookMoved = 0;
                Run(A("say", "text", Me.Name + " " + (what == "Scry" ? "scries " : "surveils ") + n + "."));
            }
            int left = n - lookMoved;
            if (left <= 0 || Me.Library.Count == 0) { viewer.Close(); return; }
            var cards = Me.Library.Take(left).Select(id => ToData(State.Cards[id], false)).ToList();
            string how = what == "Scry" ? "right-click: To deck bottom (Y), or leave it on top" : "right-click: Discard (D) to the graveyard, or leave it on top";
            viewerReopen = () => { lookMoved++; ShowLookAt(what, n, false); };
            viewer.Show(what + " " + n + " · top first · " + how, cards, (id, at) => CardMenu(id, at, true));
        }

        Action viewerReopen;

        void RevealTop()
        {
            if (Me.Library.Count == 0) return;
            Run(A("reveal", "id", Me.Library[0]));
        }

        void ShowTop(int n)
        {
            viewerReopen = null;
            var cards = Me.Library.Take(n).Select(id => ToData(State.Cards[id], false)).ToList();
            Run(A("say", "text", Me.Name + " looked at the top " + cards.Count + " cards of their library."));
            viewer.Show("Top " + cards.Count + " of your library (top first)", cards, (id, at) => CardMenu(id, at, true));
        }

        void ShowZone(TablePlayer p, string zone)
        {
            viewerReopen = null;
            var ids = p.ZoneList(zone);
            var list = (zone == "Graveyard" || zone == "Exile" ? ids.AsEnumerable().Reverse() : ids.AsEnumerable())
                .Select(id => ToData(State.Cards[id], false)).ToList();
            if (zone == "Library") Run(A("say", "text", p.Name + " searched their library."));
            viewer.Show(p.Name + " — " + zone, list, (id, at) => CardMenu(id, at, true));
        }

        // ---------------------------------------------------------------- card actions

        void CardMenu(int id, Vector2 at, bool fromViewer = false, RectTransform beside = null)
        {
            if (!State.Cards.TryGetValue(id, out var c)) return;
            var me = Me;
            bool unknown = string.IsNullOrEmpty(c.Name);
            // A right-click on one of several selected cards acts on the whole selection.
            var group = !fromViewer && selected.Contains(id) && selected.Count > 1 ? Targets(id) : new List<TableCard> { c };
            bool many = group.Count > 1;
            string face = FaceName(c);
            string header = many ? group.Count + " SELECTED CARDS" : (c.FaceDown && c.Zone == "Battlefield") || unknown ? "FACE-DOWN CARD" : face.ToUpperInvariant();
            var items = new List<(string, Action)> { (header, null) };
            void After()
            {
                if (!fromViewer) return;
                if (viewerReopen != null) viewerReopen(); else viewer.Close();
            }
            Action Stay(Action x) => PopupMenu.Stay(x);
            bool field = group.All(g => g.Zone == "Battlefield");

            if (field)
            {
                bool anyUntapped = group.Any(g => !g.Tapped);
                items.Add(((many ? (anyUntapped ? "Tap them" : "Untap them") : c.Tapped ? "Untap" : "Tap") + "\tSpace", Stay(() => KeyTap(group))));
                items.Add(("Untap all\tX", Stay(UntapAll)));
                items.Add(((group.All(g => g.Sick) ? "Not summoning sick" : "Summoning sick") + "\tZ", Stay(() => KeySick(group))));
                items.Add(((!many && CustomSets.OtherFace(c.Name) != null && !c.FaceDown ? (c.Flipped ? "Transform to the front" : "Transform to the back") : "Flip (face down / up)") + "\tF", Stay(() => KeyFlip(group))));
                items.Add(("Turn left\tQ", Stay(() => Batch(group, g => A("rotate", "id", g.Id, "d", -1), Sfx.Tap))));
                items.Add(("Turn right\tE", Stay(() => Batch(group, g => A("rotate", "id", g.Id, "d", 1), Sfx.Tap))));
                items.Add(("---", null));
                items.Add(("+1/+1 counter\tU", Stay(() => Batch(group, g => A("counter", "id", g.Id, "kind", "+1/+1", "d", 1), Sfx.Counter))));
                items.Add(("−1/−1 counter\tI", Stay(() => Batch(group, g => A("counter", "id", g.Id, "kind", "-1/-1", "d", 1), Sfx.Counter))));
                items.Add(("Add a counter\tO", Stay(() => Batch(group, g => A("counter", "id", g.Id, "kind", "counter", "d", 1), Sfx.Counter))));
                if (group.Any(g => g.Counters.Count > 0))
                    items.Add(("Remove a counter\tShift+O", Stay(() => Batch(group.Where(g => g.Counters.Count > 0).ToList(), g => A("counter", "id", g.Id, "kind", "*first", "d", -1), Sfx.Counter))));
                items.Add(("Copy (token)\tK", Stay(() => Batch(group, g => A("clone", "id", g.Id), Sfx.Token))));
                if (!many) items.Add((c.Note.Length > 0 ? "Edit the note\tShift+N" : "Add a note\tShift+N", () => EditNote(id)));
                if (!many)
                {
                    var custom = CustomSets.Get(face);
                    if (custom != null)
                        foreach (var rel in custom.Related)
                        {
                            string r = rel;
                            items.Add(("Create " + r, Stay(() =>
                            {
                                var tk = CustomSets.Get(r);
                                Sound.Play(Sfx.Token);
                                Run(A("token", "to", c.Controller, "name", r, "pt", tk?.PT ?? "", "colors", tk?.Colors ?? ""));
                            })));
                        }
                }
            }
            items.Add(("!Delete\tBackspace", () => { After(); KeyDelete(group); }));
            items.Add(("---", null));
            if (!field)
            {
                items.Add(((c.Zone == "Hand" && !many ? "Play\tP / Double-click" : "Play\tP"), () => { After(); MoveGroup(group, "Battlefield"); }));
                if (c.Zone == "Hand" && !many) items.Add(("Play face down", () =>
                {
                    Sound.Play(Sfx.Resolve, 0.6f);
                    Run(A("move", "id", id, "zone", "Battlefield", "to", me.Id, "facedown", true));
                }));
            }
            if (group.Any(g => g.Zone != "Hand")) items.Add(("Hand\tH", () => { After(); MoveGroup(group, "Hand"); }));
            if (group.Any(g => g.Zone != "Graveyard")) items.Add(("Discard\tD", () => { After(); MoveGroup(group, "Graveyard"); }));
            if (group.Any(g => g.Zone != "Exile")) items.Add(("Exile\tS", () => { After(); MoveGroup(group, "Exile"); }));
            items.Add(("To deck top\tT", () => { After(); MoveGroup(group, "Library", 0); }));
            items.Add(("To deck bottom\tY", () => { After(); MoveGroup(group, "Library", -1); }));
            if (group.Any(g => g.Zone != "Command")) items.Add(("Command zone\tB", () => { After(); MoveGroup(group, "Command"); }));
            if (c.Zone == "Hand" && !many) items.Add(("Reveal\tW", () => Run(A("reveal", "id", id))));
            if (!unknown && !many) items.Add(("Zoom\tHold Alt", () => gc.Inspector.Show(ToData(c, false))));
            var menu = PopupMenu.Instance.Reopen(() =>
            {
                // Rebuild against the current table (the card may have changed or left).
                if (!State.Cards.TryGetValue(id, out _)) { PopupMenu.Instance.Close(); return; }
                var still = beside != null && beside.gameObject.activeInHierarchy ? beside : null;
                CardMenu(id, at, fromViewer, still);
            });
            if (beside != null) menu.ShowBeside(beside, items);
            else menu.Show(at, items);
        }

        // ---------------------------------------------------------------- acting on one card or a selection

        /// <summary>The cards an action applies to: the selection (if the card is part of it, or nothing is hovered), else just that card.</summary>
        List<TableCard> Targets(int focusId)
        {
            if (selected.Count > 0 && (focusId < 0 || selected.Contains(focusId)))
                return selected.Where(i => State.Cards.ContainsKey(i)).Select(i => State.Cards[i]).ToList();
            return focusId >= 0 && State.Cards.TryGetValue(focusId, out var c) ? new List<TableCard> { c } : new List<TableCard>();
        }

        /// <summary>Runs one action per card as a single step (one undo, one network message).</summary>
        void Batch(List<TableCard> cards, Func<TableCard, Dictionary<string, object>> make, Sfx? sound = null)
        {
            if (cards.Count == 0) return;
            if (sound.HasValue) Sound.Play(sound.Value, 0.6f);
            if (cards.Count == 1) { Run(make(cards[0])); return; }
            Run(A("batch", "ops", cards.Select(c => (object)make(c)).ToList()));
        }

        void MoveGroup(List<TableCard> cards, string zone, int index = 0)
        {
            if (cards.Count == 0) return;
            PlayMoveSound(cards[0].Id, zone);
            Batch(cards.Where(c => c.Zone != zone || zone == "Library").ToList(),
                c => A("move", "id", c.Id, "zone", zone, "to", zone == "Battlefield" ? Me.Id : c.Owner, "index", index));
            if (zone != "Battlefield") ClearSelection();
        }

        void KeyTap(List<TableCard> cards)
        {
            cards = cards.Where(c => c.Zone == "Battlefield").ToList();
            if (cards.Count == 0) return;
            bool tap = cards.Count == 1 ? !cards[0].Tapped : cards.Any(c => !c.Tapped);
            Batch(cards, c => A("tap", "id", c.Id, "v", tap), tap ? Sfx.Tap : Sfx.Untap);
        }

        void KeySick(List<TableCard> cards)
        {
            cards = cards.Where(c => c.Zone == "Battlefield").ToList();
            bool sick = !cards.All(c => c.Sick);
            Batch(cards, c => A("sick", "id", c.Id, "v", sick), Sfx.CardSlide);
        }

        /// <summary>F: double-faced cards transform; anything else turns face down / up.</summary>
        void KeyFlip(List<TableCard> cards)
        {
            cards = cards.Where(c => c.Zone == "Battlefield").ToList();
            if (cards.Count == 0) return;
            Sound.Play(Sfx.CardSlide, 0.7f, 1.1f);
            Batch(cards, c => !c.FaceDown && CustomSets.OtherFace(c.Name) != null ? A("flip", "id", c.Id) : A("face", "id", c.Id, "v", !c.FaceDown));
        }

        void KeyDelete(List<TableCard> cards)
        {
            if (cards.Count == 0) return;
            Batch(cards, c => A("delete", "id", c.Id), Sfx.Exile);
            ClearSelection();
        }

        // ---------------------------------------------------------------- selection (Shift + click, or drag a box)

        readonly HashSet<int> selected = new HashSet<int>();
        RectTransform marquee;
        Vector2 marqueeStart;

        void ClearSelection()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            Render();
        }

        void ToggleSelected(int id)
        {
            if (!selected.Remove(id)) selected.Add(id);
            Sound.Play(Sfx.UiClick, 0.5f);
            Render();
        }

        internal void BeginMarquee(Vector2 screen)
        {
            marqueeStart = ToBoard(screen);
            if (marquee == null)
            {
                var img = UIKit.Panel("Marquee", root, new Color(0.45f, 0.75f, 1f, 0.08f), false);
                img.raycastTarget = false;
                var o = img.gameObject.AddComponent<UnityEngine.UI.Outline>();
                o.effectColor = new Color(0.75f, 0.85f, 1f, 0.7f); o.effectDistance = new Vector2(1, -1);
                marquee = img.rectTransform;
                marquee.anchorMin = marquee.anchorMax = Vector2.zero; marquee.pivot = Vector2.zero;
            }
            marquee.gameObject.SetActive(true);
            marquee.SetAsLastSibling();
            UpdateMarquee(screen);
        }

        internal void UpdateMarquee(Vector2 screen)
        {
            if (marquee == null) return;
            var p = ToBoard(screen);
            var min = Vector2.Min(marqueeStart, p);
            marquee.anchoredPosition = min;
            marquee.sizeDelta = Vector2.Max(marqueeStart, p) - min;
        }

        /// <summary>Selects every card on the battlefield inside the box (Shift adds to the selection).</summary>
        internal void EndMarquee(Vector2 screen)
        {
            if (marquee == null) return;
            var p = ToBoard(screen);
            var box = Rect.MinMaxRect(Mathf.Min(p.x, marqueeStart.x), Mathf.Min(p.y, marqueeStart.y), Mathf.Max(p.x, marqueeStart.x), Mathf.Max(p.y, marqueeStart.y));
            marquee.gameObject.SetActive(false);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!shift) selected.Clear();
            foreach (var kv in views)
            {
                if (kv.Value == null || kv.Value.IsExiting || !State.Cards.TryGetValue(kv.Key, out var c) || c.Zone != "Battlefield") continue;
                if (box.Contains(kv.Value.CurrentPos)) selected.Add(kv.Key);
            }
            if (selected.Count > 0) Sound.Play(Sfx.UiClick, 0.5f);
            Render();
        }

        // ---------------------------------------------------------------- pointer events from cards

        int lastClickId = -1; float lastClickAt;

        void OnClick(CardView cv)
        {
            if (!State.Cards.TryGetValue(cv.CardId, out var c)) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && (c.Zone == "Battlefield" || c.Zone == "Hand")) { ToggleSelected(c.Id); return; }   // Shift + click: select
            if (selected.Count > 0 && !selected.Contains(c.Id)) ClearSelection();
            bool dbl = lastClickId == c.Id && Time.unscaledTime - lastClickAt < 0.35f;
            lastClickId = c.Id; lastClickAt = Time.unscaledTime;
            if (c.Zone == "Battlefield") ToggleTap(c.Id);
            else if (c.Zone == "Hand" && dbl && c.Owner == Me.Id) MoveCard(c.Id, "Battlefield", Me.Id);
        }

        /// <summary>A card view wired to the table's click / hover / drag handlers.</summary>
        CardView NewView(int id)
        {
            var cv = CardView.Create(cardLayer, id);
            cv.Clicked = OnClick;
            cv.RightClicked = OnRightClick;
            cv.HoverStart = v => { hovered = v; if (!altZoom && v.Data != null && !v.Data.Hidden) gc.Preview.Request(v.Data, v.CurrentPos.x < 960); };
            cv.HoverEnd = v => { if (hovered == v) hovered = null; gc.Preview.Hide(); };
            cv.DragBegan = OnDragBegan;
            cv.DragMoved = OnDragMoved;
            cv.DragEnded = OnDragEnded;
            views[id] = cv;
            return cv;
        }

        // ---------------------------------------------------------------- dragging the top card straight off a pile

        /// <summary>Picks up the top card of one of my piles (library: face down) without opening the pile.</summary>
        internal void BeginPileDrag(string zone, Vector2 screen)
        {
            if (State == null || Me == null || dragging != null) return;
            var ids = Me.ZoneList(zone);
            if (ids == null || ids.Count == 0) return;
            int id = zone == "Library" ? ids[0] : ids[ids.Count - 1];
            if (!State.Cards.TryGetValue(id, out var c)) return;
            if (views.TryGetValue(id, out var old) && old != null) Destroy(old.gameObject);
            var cv = NewView(id);
            bool hidden = zone == "Library" && !c.Revealed;
            cv.SetData(ToData(c, hidden));
            var r = zone == "Library" ? LibRect : zone == "Graveyard" ? GyRect : zone == "Exile" ? ExRect : CmdRect;
            float h = 140f * UiScale.CardScale;
            cv.SpawnFrom(r.center, new Vector2(h * 0.714f, h));
            OnDragBegan(cv, screen);
            if (dragging != cv) { views.Remove(id); Destroy(cv.gameObject); }
        }

        internal void MovePileDrag(Vector2 screen) { if (dragging != null) OnDragMoved(dragging, screen); }

        internal void EndPileDrag(Vector2 screen) { if (dragging != null) OnDragEnded(dragging, screen); }

        void OnRightClick(CardView cv) => CardMenu(cv.CardId, Input.mousePosition, false, (RectTransform)cv.transform);

        CardView hovered;

        /// <summary>The face a card shows: its back when transformed (double-faced custom cards).</summary>
        static string FaceName(TableCard c) => c.Flipped ? CustomSets.OtherFace(c.Name) ?? c.Name : c.Name;

        /// <summary>Transform a double-faced card (F over it, or its menu).</summary>
        void Flip(int id)
        {
            if (!State.Cards.TryGetValue(id, out var c)) return;
            if (CustomSets.OtherFace(c.Name) == null) { gc.Toasts.Show("That card has no other face."); return; }
            Sound.Play(Sfx.CardSlide, 0.7f, 1.15f);
            Run(A("flip", "id", id));
        }

        void FlipHovered()
        {
            if (hovered == null || hovered.IsExiting || !State.Cards.TryGetValue(hovered.CardId, out var c)) return;
            if (c.FaceDown || string.IsNullOrEmpty(c.Name)) return;
            Flip(c.Id);
        }

        // ---------------------------------------------------------------- notes on cards

        NoteDialog noteDialog;
        RectTransform noteLayer;
        readonly Dictionary<int, Text> noteLabels = new Dictionary<int, Text>();

        /// <summary>Opens the note editor for a permanent; a new note starts as the card's own rules text.</summary>
        void EditNote(int id)
        {
            if (!State.Cards.TryGetValue(id, out var c) || c.Zone != "Battlefield") return;
            string text = c.Note;
            if (string.IsNullOrEmpty(text) && !c.FaceDown)
            {
                var info = CardCatalog.Get(FaceName(c));
                text = info?.Text ?? "";
            }
            noteDialog.Show(c.FaceDown ? "face-down card" : FaceName(c), text, t =>
            {
                Sound.Play(Sfx.CardPlace, 0.5f);
                Run(A("note", "id", id, "text", t ?? ""));
            });
        }

        /// <summary>Keeps a little yellow sticky under every permanent that has a note.</summary>
        void LateUpdate()
        {
            if (State == null || noteLayer == null) return;
            foreach (var id in noteLabels.Keys.ToList())
            {
                bool keep = State.Cards.TryGetValue(id, out var nc) && nc.Zone == "Battlefield" && (!string.IsNullOrEmpty(nc.Note) || nc.Sick)
                            && views.TryGetValue(id, out var nv) && nv != null && !nv.IsExiting;
                if (!keep) { if (noteLabels[id] != null) Destroy(noteLabels[id].transform.parent.gameObject); noteLabels.Remove(id); }
            }
            foreach (var c in State.Cards.Values)
            {
                if (c.Zone != "Battlefield" || (string.IsNullOrEmpty(c.Note) && !c.Sick)) continue;
                if (!views.TryGetValue(c.Id, out var cv) || cv == null || cv.IsExiting) continue;
                if (!noteLabels.TryGetValue(c.Id, out var label) || label == null)
                {
                    var bg = UIKit.Panel("Note", noteLayer, new Color(1f, 0.9f, 0.45f, 0.95f));
                    var r = bg.rectTransform;
                    r.anchorMin = r.anchorMax = Vector2.zero; r.pivot = new Vector2(0.5f, 1f);
                    bg.raycastTarget = false;
                    bg.pixelsPerUnitMultiplier = 3f;
                    UIKit.AddShadow(bg.transform, 6, new Vector2(0, -3), 0.5f);
                    label = UIKit.Label("Text", bg.transform, "", 12, new Color(0.15f, 0.1f, 0.02f), TextAnchor.UpperLeft);
                    label.rectTransform.Stretch(6, 4, 6, 4);
                    label.resizeTextForBestFit = true; label.resizeTextMinSize = 8; label.resizeTextMaxSize = 12;
                    noteLabels[c.Id] = label;
                }
                string shown = c.Sick ? "zZz  Summoning sick" + (string.IsNullOrEmpty(c.Note) ? "" : "\n" + c.Note) : c.Note;
                if (label.text != shown) label.text = shown;
                var cardRt = (RectTransform)cv.transform;
                float rot = Mathf.Abs(Mathf.DeltaAngle(cardRt.localEulerAngles.z, 0f));
                bool sideways = rot > 45f && rot < 135f;
                float halfH = (sideways ? cardRt.sizeDelta.x : cardRt.sizeDelta.y) / 2f;
                float w = Mathf.Max(110f, (sideways ? cardRt.sizeDelta.y : cardRt.sizeDelta.x) * 1.15f);
                var noteRt = (RectTransform)label.transform.parent;
                noteRt.sizeDelta = new Vector2(w, 46);
                noteRt.anchoredPosition = cv.CurrentPos - new Vector2(0, halfH - 10f);   // tucked over the card's bottom edge
            }
        }

        /// <summary>
        /// Keys for the card under the mouse, or for every selected card (the same choices as the right-click menu):
        /// Space tap · Z summoning sick · F flip · Q / E turn · U / I +1/+1 / −1/−1 · O / Shift+O counter · K copy ·
        /// Backspace delete · P play · H hand · D discard · S exile · T / Y deck top / bottom · B command zone ·
        /// Shift+N note · W reveal.
        /// </summary>
        void CardKeys(bool shift)
        {
            int focus = hovered != null && !hovered.IsExiting && State.Cards.ContainsKey(hovered.CardId) ? hovered.CardId : -1;
            var group = Targets(focus);
            if (group.Count == 0) return;
            var field = group.Where(c => c.Zone == "Battlefield").ToList();
            if (Input.GetKeyDown(KeyCode.Space)) KeyTap(group);
            if (Input.GetKeyDown(KeyCode.Z)) KeySick(group);
            if (Input.GetKeyDown(KeyCode.F)) KeyFlip(group);
            if (Input.GetKeyDown(KeyCode.Q)) Batch(field, c => A("rotate", "id", c.Id, "d", -1), Sfx.Tap);
            if (Input.GetKeyDown(KeyCode.E)) Batch(field, c => A("rotate", "id", c.Id, "d", 1), Sfx.Tap);
            if (Input.GetKeyDown(KeyCode.U)) Batch(field, c => A("counter", "id", c.Id, "kind", "+1/+1", "d", 1), Sfx.Counter);
            if (Input.GetKeyDown(KeyCode.I)) Batch(field, c => A("counter", "id", c.Id, "kind", "-1/-1", "d", 1), Sfx.Counter);
            if (Input.GetKeyDown(KeyCode.O))
            {
                if (!shift) Batch(field, c => A("counter", "id", c.Id, "kind", "counter", "d", 1), Sfx.Counter);
                else Batch(field.Where(c => c.Counters.Count > 0).ToList(), c => A("counter", "id", c.Id, "kind", "*first", "d", -1), Sfx.Counter);
            }
            if (Input.GetKeyDown(KeyCode.K)) Batch(field, c => A("clone", "id", c.Id), Sfx.Token);
            if (Input.GetKeyDown(KeyCode.Backspace) || Input.GetKeyDown(KeyCode.Delete)) KeyDelete(group);
            if (Input.GetKeyDown(KeyCode.P)) MoveGroup(group.Where(c => c.Zone != "Battlefield").ToList(), "Battlefield");
            if (Input.GetKeyDown(KeyCode.H)) MoveGroup(group, "Hand");
            if (Input.GetKeyDown(KeyCode.D)) MoveGroup(group, "Graveyard");
            if (Input.GetKeyDown(KeyCode.S)) MoveGroup(group, "Exile");
            if (Input.GetKeyDown(KeyCode.T)) MoveGroup(group, "Library", 0);
            if (Input.GetKeyDown(KeyCode.Y)) MoveGroup(group, "Library", -1);
            if (Input.GetKeyDown(KeyCode.B)) MoveGroup(group, "Command");
            if (group.Count == 1)
            {
                var one = group[0];
                if (Input.GetKeyDown(KeyCode.W) && one.Zone == "Hand") Run(A("reveal", "id", one.Id));
                if (Input.GetKeyDown(KeyCode.N) && shift && one.Zone == "Battlefield") EditNote(one.Id);
            }
        }

        /// <summary>Keys over one of my piles act on its top card; over the library also C draw, V shuffle, M mulligan.</summary>
        void PileKeys(string zone)
        {
            if (zone == "Library")
            {
                if (Input.GetKeyDown(KeyCode.V)) Shuffle();
                if (Input.GetKeyDown(KeyCode.M)) { Sound.Play(Sfx.DrawMany, 0.6f); Run(A("mulligan")); }
                if (Input.GetKeyDown(KeyCode.H)) Draw(1);
            }
            else if (Input.GetKeyDown(KeyCode.H)) TopCard(zone, id => MoveCard(id, "Hand", Me.Id));
            if (Input.GetKeyDown(KeyCode.P)) TopCard(zone, id => MoveCard(id, "Battlefield", Me.Id));
            if (Input.GetKeyDown(KeyCode.D) && zone != "Graveyard") TopCard(zone, id => MoveCard(id, "Graveyard", Me.Id));
            if (Input.GetKeyDown(KeyCode.S) && zone != "Exile") TopCard(zone, id => MoveCard(id, "Exile", Me.Id));
            if (Input.GetKeyDown(KeyCode.T) && zone != "Library") TopCard(zone, id => MoveCard(id, "Library", Me.Id, 0));
            if (Input.GetKeyDown(KeyCode.Y)) TopCard(zone, id => MoveCard(id, "Library", Me.Id, -1));
            if (Input.GetKeyDown(KeyCode.B) && zone != "Command") TopCard(zone, id => MoveCard(id, "Command", Me.Id));
        }

        string hoveredPile;

        /// <summary>Q / E: turn the hovered permanent a quarter turn left / right.</summary>
        void RotateHovered(int d)
        {
            if (hovered == null || hovered.IsExiting || !State.Cards.TryGetValue(hovered.CardId, out var c) || c.Zone != "Battlefield") return;
            Sound.Play(Sfx.Tap, 0.6f, d > 0 ? 1f : 0.92f);
            Run(A("rotate", "id", c.Id, "d", d));
        }

        CardView dragging;

        void OnDragBegan(CardView cv, Vector2 screen)
        {
            if (!State.Cards.TryGetValue(cv.CardId, out var c)) return;
            bool mine = c.Zone == "Hand" || c.Zone == "Library" || c.Zone == "Graveyard" || c.Zone == "Exile" || c.Zone == "Command"
                ? c.Owner == Me.Id : c.Zone == "Battlefield";
            if (!mine) return;
            dragging = cv;
            cv.Dragging = true;
            cv.DragBoardPos = ToBoard(screen);
            cv.transform.SetAsLastSibling();
            gc.Preview.Hide();
            Sound.Play(Sfx.CardSlide, 0.5f, 0.85f);
        }

        void OnDragMoved(CardView cv, Vector2 screen) { if (dragging == cv) cv.DragBoardPos = ToBoard(screen); }

        void OnDragEnded(CardView cv, Vector2 screen)
        {
            if (dragging != cv) return;
            dragging = null;
            cv.Dragging = false;
            var p = ToBoard(screen);
            if (!State.Cards.TryGetValue(cv.CardId, out var c)) { Render(); return; }

            // Dragging one of several selected cards takes the whole selection along.
            var group = selected.Contains(c.Id) && selected.Count > 1 ? Targets(c.Id) : null;

            string pile = PileAt(p);
            if (pile != null)
            {
                if (group != null) MoveGroup(group, pile, 0);
                else MoveCard(c.Id, pile, c.Owner, 0);
                return;
            }

            if (group != null && p.y < FieldY - 20 && p.x > HandLeft - 40 && p.x < HandRight + 40) { MoveGroup(group, "Hand"); return; }
            if (group != null && p.y < OppBandY && c.Zone == "Battlefield" && c.Controller == Me.Id)
            {
                // Same shift for every selected permanent of mine.
                var to = new Vector2(Mathf.Clamp01((p.x - FieldX) / FieldW), Mathf.Clamp01((p.y - FieldY) / FieldH));
                var delta = to - c.Pos;
                Sound.Play(Sfx.CardSlide, 0.5f, 0.85f);
                Batch(group.Where(g => g.Zone == "Battlefield" && g.Controller == Me.Id).ToList(), g =>
                {
                    var np = new Vector2(Mathf.Clamp01(g.Pos.x + delta.x), Mathf.Clamp01(g.Pos.y + delta.y));
                    return A("pos", "id", g.Id, "x", np.x, "y", np.y);
                });
                return;
            }

            if (p.y < FieldY - 20 && p.x > HandLeft - 40 && p.x < HandRight + 40)
            {
                // Into my hand: from the battlefield it returns; inside the hand it's reordered.
                if (c.Zone == "Hand")
                {
                    Sound.Play(Sfx.CardSlide, 0.5f, 0.85f);
                    int to = HandIndexAt(p.x, Me.Hand.Count);
                    if (IsClient)
                    {
                        // Reorder here right away so the host's round-trip doesn't make the card snap back.
                        Me.Hand.Remove(c.Id);
                        Me.Hand.Insert(Mathf.Clamp(to, 0, Me.Hand.Count), c.Id);
                        Render();
                    }
                    Run(A("handorder", "id", c.Id, "index", to));
                }
                else
                {
                    // From the board: it lands where you let go, not at the end.
                    PlayMoveSound(c.Id, "Hand");
                    var mv = A("move", "id", c.Id, "zone", "Hand", "to", c.Owner);
                    if (c.Owner == Me.Id) mv["hindex"] = (double)HandIndexAt(p.x, Me.Hand.Count + 1);
                    Run(mv);
                }
                return;
            }

            if (p.y < OppBandY)
            {
                // My battlefield, anywhere I drop it.
                var pos = new Vector2(Mathf.Clamp01((p.x - FieldX) / FieldW), Mathf.Clamp01((p.y - FieldY) / FieldH));
                if (c.Zone == "Battlefield" && c.Controller == Me.Id) { if (IsClient) { c.Pos = pos; Render(); } Sound.Play(Sfx.CardSlide, 0.5f, 0.85f); Run(A("pos", "id", c.Id, "x", pos.x, "y", pos.y)); }   // just moved it
                else MoveCard(c.Id, "Battlefield", Me.Id, 0, pos);
                return;
            }

            // Across the table: hand the card to that player (they control it on their side).
            var opp = OppAt(p.x);
            if (opp != null)
            {
                var rect = OppField(oppPanels.FindIndex(o => o.PlayerId == opp.Id));
                var pos = new Vector2(Mathf.Clamp01((p.x - rect.x) / rect.width), Mathf.Clamp01((OppFieldTop - p.y) / rect.height));
                Sound.Play(Sfx.Resolve, 0.7f);
                Run(A("give", "id", c.Id, "to", opp.Id, "x", pos.x, "y", pos.y));
                return;
            }
            Render();
        }

        static Vector2 ToBoardStatic(RectTransform root, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local + new Vector2(960, 540);
        }

        Vector2 ToBoard(Vector2 screen) => ToBoardStatic(root, screen);

        string PileAt(Vector2 p)
        {
            if (Grow(LibRect).Contains(p)) return "Library";
            if (Grow(GyRect).Contains(p)) return "Graveyard";
            if (Grow(ExRect).Contains(p)) return "Exile";
            if (Grow(CmdRect).Contains(p)) return "Command";
            return null;
        }

        static Rect Grow(Rect r) => new Rect(r.x - 10, r.y - 10, r.width + 20, r.height + 20);

        // =================================================================================================== keys

        bool altZoom;
        int altCard = -1;

        void Update()
        {
            if (State == null) return;
            if (altZoom && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
            {
                altZoom = false;
                altCard = -1;
                if (gc.Inspector.IsOpen) gc.Inspector.ForceHide();
            }
#if ENABLE_LEGACY_INPUT_MANAGER
            if (viewer.IsOpen || tokenDialog.IsOpen || noteDialog.IsOpen || PopupMenu.Instance.IsOpen || gc.GameMenu.IsOpen || gc.Options.IsOpen) return;
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.Z)) { Sound.Play(Sfx.CardSlide, 0.5f); Run(A("undo")); return; }
            if (ctrl) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (Input.GetKeyDown(KeyCode.Escape) && selected.Count > 0) { ClearSelection(); return; }
            // Over a card (or with cards selected): card keys. Over a pile: its top card. Otherwise: table keys.
            bool cardKeys = (hovered != null && !hovered.IsExiting && State.Cards.ContainsKey(hovered.CardId)) || selected.Count > 0;
            if (cardKeys) CardKeys(shift);
            else if (hoveredPile != null) PileKeys(hoveredPile);
            else
            {
                if (Input.GetKeyDown(KeyCode.T)) OpenTokenDialog();
            }
            if (Input.GetKeyDown(KeyCode.C) && !cardKeys) Draw(1);
            if (Input.GetKeyDown(KeyCode.X)) UntapAll();
            if (Input.GetKeyDown(KeyCode.V) && !cardKeys && hoveredPile == null) Shuffle();
            if (Input.GetKeyDown(KeyCode.M) && !cardKeys && hoveredPile == null) { Sound.Play(Sfx.DrawMany, 0.6f); Run(A("mulligan")); }
            if (Input.GetKeyDown(KeyCode.N) && !shift) NextTurn();
            if (Input.GetKeyDown(KeyCode.R)) DiceMenu(Input.mousePosition);
            if (Input.GetKeyDown(KeyCode.G)) OpenTokenDialog();
            // Hold Alt over a card: the big zoomed view, until Alt is released.
            bool alt = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (alt && hovered != null && hovered.Data != null && !hovered.Data.Hidden && !string.IsNullOrEmpty(hovered.Data.Name)
                && (!altZoom || altCard != hovered.CardId))
            {
                // Follows the mouse while Alt stays down: hover another card and the big view switches to it.
                altZoom = true;
                altCard = hovered.CardId;
                gc.Preview.Hide();
                gc.Inspector.ShowPassive(hovered.Data);
            }
#endif
        }

        // =================================================================================================== render

        /// <summary>Lays out every visible card and refreshes the HUD from the table state.</summary>
        public void Render()
        {
            if (State == null) return;
            // Selected cards that left the battlefield / hand drop out of the selection.
            selected.RemoveWhere(id => !State.Cards.TryGetValue(id, out var sc) || (sc.Zone != "Battlefield" && sc.Zone != "Hand"));
            var me = Me;
            var placements = new Dictionary<int, (Vector2 pos, Vector2 size, float rot, int order, CardData data)>();
            float cardH = 140f * UiScale.CardScale, cardW = cardH * 0.714f;

            // My battlefield: free positions.
            int order = 0;
            foreach (var c in State.Battlefield(me.Id).OrderBy(c => c.Pos.y).ThenBy(c => c.Pos.x))
            {
                var pos = new Vector2(FieldX + c.Pos.x * FieldW, FieldY + c.Pos.y * FieldH);
                placements[c.Id] = (pos, new Vector2(cardW, cardH), (c.Tapped ? -90f : 0f) - c.Rot * 90f, 2000 + order++, ToData(c, false));
            }

            // My hand.
            int n = me.Hand.Count;
            float hh = 210f * UiScale.CardScale, hw = hh * 0.714f;
            float spacing = n <= 1 ? 0 : Mathf.Min(hw * 0.82f, (HandRight - HandLeft - hw) / (n - 1));
            float cx = (HandLeft + HandRight) / 2f;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0 : (i / (float)(n - 1)) * 2f - 1f;
                var c = State.Cards[me.Hand[i]];
                placements[c.Id] = (new Vector2(cx - spacing * (n - 1) / 2f + i * spacing, HandY - t * t * 18f), new Vector2(hw, hh), -t * 6f, 10000 + i, ToData(c, false));
            }

            // Everyone else across the top.
            var others = OtherPlayers();
            for (int k = 0; k < others.Count; k++)
            {
                var p = others[k];
                var rect = OppField(k);
                float s = Mathf.Clamp(rect.width / FieldW, 0.55f, 1f);
                float oh = cardH * s, ow = oh * 0.714f;
                int o = 0;
                foreach (var c in State.Battlefield(p.Id).OrderBy(c => c.Pos.y))
                {
                    // Seen from across the table: their near edge is at the top.
                    var pos = new Vector2(rect.x + c.Pos.x * rect.width, OppFieldTop - c.Pos.y * rect.height);
                    placements[c.Id] = (pos, new Vector2(ow, oh), (c.Tapped ? 90f : 0f) + c.Rot * 90f, 100 + o++, ToData(c, false));
                }
                oppPanels[k].Render(p, State);
            }

            // Leaving cards fly to where they went.
            foreach (var id in views.Keys.ToList())
            {
                if (placements.ContainsKey(id)) continue;
                var v = views[id];
                views.Remove(id);
                if (v == null || v.IsExiting) continue;
                if (dragging == v) dragging = null;
                State.Cards.TryGetValue(id, out var c);
                string zone = c?.Zone ?? "Gone";
                var target = PileTarget(c);
                var kind = zone == "Graveyard" ? ExitKind.Die : zone == "Exile" ? ExitKind.Exile : zone == "Library" ? ExitKind.ToLibrary : ExitKind.Fade;
                v.PlayExit(kind, target);
            }

            // Arrivals and moves.
            foreach (var kv in placements.OrderBy(k => k.Value.order))
            {
                var (pos, size, rot, ord, data) = kv.Value;
                if (!views.TryGetValue(kv.Key, out var cv))
                {
                    cv = NewView(kv.Key);
                    cv.SetData(data);
                    State.Cards.TryGetValue(kv.Key, out var tc);
                    lastZone.TryGetValue(kv.Key, out var fromZone);
                    var from = SpawnOrigin(tc, fromZone);
                    if (data.Token && fromZone == null) { cv.SpawnFrom(pos, size, rot); cv.Materialize(); }
                    else
                    {
                        cv.SpawnFrom(from, size * 0.7f);
                        if (data.Zone == "Hand" && fromZone == "Library") cv.RevealDuringTravel();
                    }
                    cv.MoveTo(pos, size, rot, data.Zone == "Battlefield" ? MoveKind.Land : MoveKind.Travel);
                }
                else
                {
                    cv.SetData(data);
                    if (cv != dragging) cv.MoveTo(pos, size, rot, MoveKind.Reflow);
                }
                cv.SetHighlight(selected.Contains(kv.Key) ? Highlight.Selected : Highlight.None, false);
                cv.transform.SetAsLastSibling();
            }
            if (dragging != null) dragging.transform.SetAsLastSibling();

            foreach (var c in State.Cards.Values) lastZone[c.Id] = c.Zone;
            RenderHud();
        }

        void RenderHud()
        {
            var me = Me;
            myPanel.SetColor(Theme.PlayerColor(viewSeat));
            myPanel.Render(ToPlayerData(me), FakeState(), true);
            var active = State.ActivePlayer;
            turnText.text = "Turn " + State.Turn + "  ·  " + (active == me ? "<color=#f7a540>your turn</color>" : active.Name + "'s turn");
            PileView("Library", me.Library, true);
            PileView("Graveyard", me.Graveyard, false);
            PileView("Exile", me.Exile, false);
            PileView("Command", me.Command, false);
            int from = Mathf.Max(0, State.Log.Count - 18);
            logText.text = string.Join("\n", State.Log.GetRange(from, State.Log.Count - from));
        }

        void PileView(string zone, List<int> ids, bool faceDown)
        {
            var (frame, top, count) = piles[zone];
            count.text = zone + "\n" + ids.Count;
            if (ids.Count == 0) { top.enabled = false; return; }
            top.enabled = true;
            var topCard = State.Cards[faceDown ? ids[0] : ids[ids.Count - 1]];
            if (faceDown && !topCard.Revealed) { top.texture = GameArt.CardBack; return; }
            var data = ToData(topCard, false);
            top.texture = null;
            CardImageCache.Instance?.GetFor(data, tex => { if (top != null) top.texture = tex; });
        }

        Vector2 PileTarget(TableCard c)
        {
            if (c == null) return new Vector2(1660, 480);
            if (c.Owner == Me.Id || c.Controller == Me.Id)
            {
                switch (c.Zone)
                {
                    case "Library": return LibRect.center;
                    case "Graveyard": return GyRect.center;
                    case "Exile": return ExRect.center;
                    case "Command": return CmdRect.center;
                    case "Hand": return new Vector2(960, 40);
                }
            }
            int k = oppPanels.FindIndex(o => o.PlayerId == c.Owner);
            return k >= 0 ? oppPanels[k].Center : new Vector2(960, 1000);
        }

        Vector2 SpawnOrigin(TableCard c, string fromZone)
        {
            if (c == null) return new Vector2(960, 540);
            if (fromZone == null) return LibRect.center;
            if (c.Owner == Me.Id)
            {
                switch (fromZone)
                {
                    case "Library": return LibRect.center;
                    case "Graveyard": return GyRect.center;
                    case "Exile": return ExRect.center;
                    case "Command": return CmdRect.center;
                    case "Hand": return new Vector2(960, 40);
                }
            }
            int k = oppPanels.FindIndex(o => o.PlayerId == c.Owner);
            return k >= 0 ? oppPanels[k].Center : new Vector2(960, 900);
        }

        int HandIndexAt(float x, int count)
        {
            if (count <= 1) return 0;
            float hw = 210f * UiScale.CardScale * 0.714f;
            float spacing = Mathf.Min(hw * 0.82f, (HandRight - HandLeft - hw) / (count - 1));
            float start = (HandLeft + HandRight) / 2f - spacing * (count - 1) / 2f;
            return Mathf.Clamp(Mathf.RoundToInt((x - start) / spacing), 0, count - 1);
        }

        // ---------------------------------------------------------------- card data for CardView

        /// <summary>A table card as CardView data: name, art, type, P/T (with +1/+1 and −1/−1 counters applied).</summary>
        CardData ToData(TableCard c, bool hidden)
        {
            string face = FaceName(c);
            var info = CardCatalog.Get(face);
            var custom = CustomSets.Get(face);
            var d = new CardData
            {
                Id = c.Id, Name = face, Set = custom?.Set ?? info?.Set ?? "", Zone = c.Zone, Tapped = c.Tapped,
                FaceDown = c.FaceDown, Token = c.Token && custom == null, Owner = c.Owner, Controller = c.Controller,
                Hidden = hidden || (c.FaceDown && c.Zone == "Battlefield"),
                ManaCost = info?.Cost ?? "", TypeLine = info?.Type ?? (c.Token ? "Token" : ""), Text = info?.Text ?? "",
                Colors = info?.Colors ?? c.TokenColors, IsLand = info?.Land ?? false, IsCreature = info?.Creature ?? c.TokenPT.Length > 0,
            };
            foreach (var kv in c.Counters) d.Counters[kv.Key] = kv.Value;
            string pt = custom?.PT ?? (c.TokenPT.Length > 0 ? c.TokenPT : null);
            if (pt != null && pt.Contains("/"))
            {
                var parts = pt.Split('/');
                if (int.TryParse(parts[0].Trim(), out int pw) && int.TryParse(parts[1].Trim(), out int tg))
                {
                    c.Counters.TryGetValue("+1/+1", out int plus); c.Counters.TryGetValue("-1/-1", out int minus);
                    d.HasPT = true; d.IsCreature = true;
                    d.Power = pw + plus - minus; d.Toughness = tg + plus - minus;
                }
            }
            return d;
        }

        // ---------------------------------------------------------------- other players

        List<TablePlayer> OtherPlayers()
        {
            var list = new List<TablePlayer>();
            for (int i = 1; i < State.Players.Count; i++) list.Add(State.Players[(viewSeat + i) % State.Players.Count]);
            return list;
        }

        Rect OppField(int k)
        {
            int n = Mathf.Max(1, State.Players.Count - 1);
            float colW = 1908f / n;
            float x0 = 6 + k * colW;
            return new Rect(x0 + 60, OppBandY + 10, colW - 120, OppFieldTop - OppBandY - 70);
        }

        TablePlayer OppAt(float x)
        {
            int n = Mathf.Max(1, State.Players.Count - 1);
            int k = Mathf.Clamp((int)((x - 6) / (1908f / n)), 0, n - 1);
            var others = OtherPlayers();
            return k < others.Count ? others[k] : null;
        }

        void BuildOpponentPanels()
        {
            foreach (var o in oppPanels) Destroy(o.Root.gameObject);
            oppPanels.Clear();
            int n = State.Players.Count - 1;
            for (int k = 0; k < n; k++)
            {
                float colW = 1908f / n;
                float pw = Mathf.Min(400f, colW - 24f);
                var panel = new OppPanel(this, oppLayer, new Rect(6 + k * colW + colW / 2f - pw / 2f, 1080 - 118 - 8, pw, 118));
                oppPanels.Add(panel);
            }
        }

        /// <summary>Switch which seat you're viewing (and acting as).</summary>
        void SitAt(int playerId)
        {
            int idx = State.Players.FindIndex(p => p.Id == playerId);
            if (idx < 0) return;
            viewSeat = idx;
            // Re-seat: rebuild the views so cards fly to their new places on the turned table.
            foreach (var v in views.Values) if (v != null) Destroy(v.gameObject);
            views.Clear();
            for (int i = 0; i < oppPanels.Count; i++) oppPanels[i].Reset();
            Render();
        }

        // ---------------------------------------------------------------- player panels (shared with Forge games)

        /// <summary>A table seat as PlayerPanel data: life, counts, poison, commander (for the faint art).</summary>
        PlayerData ToPlayerData(TablePlayer p)
        {
            var d = new PlayerData
            {
                Id = p.Id, Name = p.Name, Life = p.Life,
                HandSize = p.Hand.Count, LibrarySize = p.Library.Count, GraveyardSize = p.Graveyard.Count, ExileSize = p.Exile.Count,
            };
            if (p.Poison > 0) d.Counters["Poison"] = p.Poison;
            foreach (var id in p.Command) if (State.Cards.TryGetValue(id, out var c)) d.Commanders.Add(new CommanderInfo { Id = id, Name = c.Name });
            if (d.Commanders.Count == 0)   // commander on the battlefield: still show its art
                foreach (var c in State.Cards.Values) if (c.Owner == p.Id && p.CommanderNames.Contains(c.Name)) { d.Commanders.Add(new CommanderInfo { Id = c.Id, Name = c.Name }); break; }
            return d;
        }

        GameState FakeState() => new GameState { ActivePlayer = State.ActivePlayer?.Id ?? -1, Me = Me?.Id ?? -1 };

        /// <summary>Zone symbols on a panel: library opens its menu, graveyard/exile/hand-less zones open viewers.</summary>
        void ZoneClick(TablePlayer p, string zone)
        {
            if (p == Me && zone == "Library") { PileMenu("Library", Input.mousePosition); return; }
            if (zone == "Hand") { if (p != Me) gc.Toasts.Show(p.Name + " has " + p.Hand.Count + " cards in hand."); return; }
            if (zone == "Library" && p != Me) { gc.Toasts.Show(p.Name + " has " + p.Library.Count + " cards in their library."); return; }
            ShowZone(p, zone);
        }

        /// <summary>Right-click a nameplate: poison, sit at that seat, browse their piles.</summary>
        void PlayerMenu(TablePlayer p, Vector2 at)
        {
            var items = new List<(string, Action)> { (p.Name.ToUpperInvariant(), null) };
            items.Add(("+1 poison", PopupMenu.Stay(() => Run(A("poison", "p", p.Id, "d", 1)))));
            if (p.Poison > 0) items.Add(("−1 poison", PopupMenu.Stay(() => Run(A("poison", "p", p.Id, "d", -1)))));
            items.Add(("Set life to 20", () => Run(A("setlife", "p", p.Id, "v", 20))));
            items.Add(("Set life to 40", () => Run(A("setlife", "p", p.Id, "v", 40))));
            if (p != Me && !Networked) items.Add(("Sit here (play as " + p.Name + ")", () => SitAt(p.Id)));
            items.Add(("Graveyard (" + p.Graveyard.Count + ")", () => ShowZone(p, "Graveyard")));
            items.Add(("Exile (" + p.Exile.Count + ")", () => ShowZone(p, "Exile")));
            items.Add(("Command zone (" + p.Command.Count + ")", () => ShowZone(p, "Command")));
            PopupMenu.Instance.Reopen(() => { var again = State?.Player(p.Id); if (again != null) PlayerMenu(again, at); }).Show(at, items);
        }

        /// <summary>Nameplate for a seat across the table (the Arena-style PlayerPanel), wired to table actions.</summary>
        class OppPanel
        {
            public RectTransform Root;
            public int PlayerId = -1;
            public Vector2 Center;
            readonly PlayerPanel panel;
            readonly TableView table;

            public OppPanel(TableView t, RectTransform parent, Rect r)
            {
                table = t;
                panel = PlayerPanel.Create(parent, t.gc, false);
                panel.MoveTo(r.x, r.y, r.width, r.height, true);
                Root = panel.Rect;
                Center = r.center;
                panel.AddLifeButtons(d => table.Life(table.State.Player(PlayerId), d));
                panel.EnableLifeEdit(v => { if (PlayerId >= 0) table.Run(A("setlife", "p", PlayerId, "v", v)); });
                panel.ZoneClicked = z => table.ZoneClick(table.State.Player(PlayerId), z);
                // Ctrl+click a seat's nameplate (life total included): look at the table from their side.
                panel.Clicked = () =>
                {
                    bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    if (ctrl && !table.Networked && PlayerId >= 0) { table.SitAt(PlayerId); Sound.Play(Sfx.CardSlide, 0.6f); }
                };
                panel.RightClicked = () => table.PlayerMenu(table.State.Player(PlayerId), Input.mousePosition);
            }

            public void Render(TablePlayer p, TableState s)
            {
                PlayerId = p.Id;
                panel.SetColor(Theme.PlayerColor(s.Players.IndexOf(p)));
                panel.Render(table.ToPlayerData(p), table.FakeState(), false);
            }

            public void Reset() { PlayerId = -1; }
        }
    }

    /// <summary>Lets a kitchen-table pile hand its top card to a drag.</summary>
    public class PileDragHandler : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        public TableView Table;
        public string Zone;
        bool active;

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            active = e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && Table != null;
            if (active) Table.BeginPileDrag(Zone, e.position);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) { if (active) Table.MovePileDrag(e.position); }

        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!active) return;
            active = false;
            Table.EndPileDrag(e.position);
        }
    }

    /// <summary>Drag on the empty table: a selection box.</summary>
    public class MarqueeHandler : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        public TableView Table;
        bool active;

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            active = e.button == UnityEngine.EventSystems.PointerEventData.InputButton.Left && Table != null;
            if (active) Table.BeginMarquee(e.pressPosition);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) { if (active) Table.UpdateMarquee(e.position); }

        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!active) return;
            active = false;
            Table.EndMarquee(e.position);
        }
    }
}

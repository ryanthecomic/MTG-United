using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Lays out every visible card for a Commander pod on a fixed 1920x1080 board (you at the bottom, opponents
    /// across the top) and turns the difference between two game states into animations: draws, casts, lands,
    /// deaths, exile, bounce, tokens, taps, attacks, blocks, combat damage and life changes (design doc 3-5).
    /// Every animation fits the 400 ms budget and retargets if a newer state arrives mid-flight.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        // Board regions (bottom-left origin, 1920x1080 space).
        const float MyFieldLeft = 280;
        // The battlefield gives way when the HUD on the right is scaled up (Options > interface size).
        // (Library, graveyard and exile now sit in my nameplate, so the field runs up to the HUD column.)
        static float MyFieldRight => 1560f - Mathf.Max(0f, UiScale.Value - 1f) * 480f;
        // Card sizes follow Options > card size; the two battlefield rows re-space around them.
        // Two rows must fit between the hand band (215) and the opponents' area, whatever the card size; focusing
        // my own board (scroll up over it, or F) gives it the whole table below the opponents' nameplates.
        const float MyFieldBottom = 215f;
        float MyFieldTop => focusMe ? 1080f - OppHeaderH * UiScale.Value - 24f : OppBandBottom - 8f;
        float MyCardH => Mathf.Min((focusMe ? 200f : 150f) * UiScale.CardScale, (MyFieldTop - MyFieldBottom - 18f - 12f) / 2f);
        float MyOtherRowY => MyFieldBottom + 8f + MyCardH / 2f;
        float MyCreatureRowY => MyOtherRowY + MyCardH + 18f;
        static float HandCardH => 220f * UiScale.CardScale;
        static float HandY => 70f + (HandCardH - 220f) * 0.25f - UiScale.Extra.y;   // follows the window's bottom edge
        const float HandLeft = 410;   // clear of my nameplate
        const float OppBandBottom = 600, OppHeaderH = 118, OppPanelW = 400;
        const float MyCommanderH = 180;

        // Where things fly to / from (these move with the scaled HUD).
        static readonly Vector2 HudCorner = new Vector2(1910, 10), MyCorner = new Vector2(10, 10);
        Vector2 MyLibraryPos => ZonePos(last?.Me ?? -1, "Library");
        Vector2 MyGraveyardPos => ZonePos(last?.Me ?? -1, "Graveyard");
        Vector2 MyExilePos => ZonePos(last?.Me ?? -1, "Exile");
        static Vector2 MyPortraitPos => UiScale.About(new Vector2(200, 100), MyCorner);
        static readonly Vector2 MyHandOrigin = new Vector2(850, 40);
        static Vector2 StackPos => UiScale.About(new Vector2(1745, 482), HudCorner);

        GameController gc;
        RectTransform root, zoneLayer, cardLayer, panelLayer;
        FxLayer fx;
        Vfx vfx;
        readonly Dictionary<int, Vector2> posBefore = new Dictionary<int, Vector2>();
        readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        readonly Dictionary<int, PlayerPanel> panels = new Dictionary<int, PlayerPanel>();
        readonly Dictionary<int, Vector2> panelCenters = new Dictionary<int, Vector2>();
        readonly List<Image> zoneBackgrounds = new List<Image>();
        Image myFieldBg;
        Image dimmer;              // darkens the board behind the mulligan hand / selection tray
        RectTransform focusLayer;  // above the HUD buttons, below the menus: cards you are choosing from

        GameState prev, last;
        int hoveredHandId = -1;
        CardView hoverCandidate; float hoverSince;
        bool previewShown;

        public static BoardView Create(RectTransform board, GameController gc)
        {
            var rt = UIKit.Rect("BoardView", board).Stretch();
            var bv = rt.gameObject.AddComponent<BoardView>();
            bv.gc = gc;
            bv.root = rt;
            bv.zoneLayer = UIKit.Rect("Zones", rt).Stretch();
            bv.panelLayer = UIKit.Rect("Panels", rt).Stretch().SubCanvas();
            bv.cardLayer = UIKit.Rect("Cards", rt).Stretch().SubCanvas();   // card motion rebuilds only this layer
            bv.vfx = Vfx.Create(rt);
            ((RectTransform)bv.vfx.transform).SubCanvas();
            bv.fx = FxLayer.Create(rt);
            ((RectTransform)bv.fx.transform).SubCanvas();

            bv.myFieldBg = UIKit.Panel("MyField", bv.zoneLayer, new Color(1f, 1f, 1f, 0.035f));
            bv.myFieldBg.rectTransform.Place(MyFieldLeft - 10, 215, MyFieldRight - MyFieldLeft + 20, 340);
            bv.myFieldBg.raycastTarget = false;

            bv.dimmer = UIKit.Panel("Dim", rt, new Color(0, 0, 0, 0), false);
            bv.dimmer.rectTransform.anchorMin = bv.dimmer.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bv.dimmer.rectTransform.sizeDelta = new Vector2(4000, 3000);
            bv.dimmer.raycastTarget = false;
            return bv;
        }

        struct Placement
        {
            public CardData Data;
            public Vector2 Pos;
            public Vector2 Size;
            public float Rotation;
            public int Order;
            public bool InHand;
        }

        // =================================================================================== render

        public void Render(GameState s)
        {
            prev = last;
            last = s;
            // Where every card was before this update (effects aim at cards that may be leaving).
            posBefore.Clear();
            foreach (var kv in cards) posBefore[kv.Key] = kv.Value.CurrentPos;
            var me = s.MePlayer;
            var opps = s.Opponents.ToList();
            var placements = new Dictionary<int, Placement>();

            // ---------------------------------------------------------------- me
            if (me != null)
            {
                var myPanel = PanelFor(me, true);
                myPanel.SetColor(Theme.PlayerColor(s.Players.IndexOf(me)));
                myPanel.MoveTo(16, 16, 370, 168, true);
                UiScaled.Add(myPanel, MyCorner);
                myPanel.Render(me, s, true);
                panelCenters[me.Id] = MyPortraitPos;
                myFieldBg.rectTransform.Place(MyFieldLeft - 10, MyFieldBottom, MyFieldRight - MyFieldLeft + 20, MyFieldTop - MyFieldBottom);

                LayoutHand(me.Hand, placements);
                LayoutField(me, MyFieldLeft, MyFieldRight, MyCreatureRowY, MyOtherRowY, MyCardH, placements, false);
                // Commanders aren't drawn on the table: right-click a nameplate to see (and cast) them.
            }

            // ---------------------------------------------------------------- opponents
            int n = Mathf.Max(1, opps.Count);
            float baseW = 1920f / n;
            EnsureZoneBackgrounds(n);
            int fi = opps.FindIndex(o => o.Id == focusedOpp);
            if (fi < 0) focusedOpp = -1;
            for (int i = 0; i < opps.Count; i++)
            {
                var p = opps[i];
                // Focus: the chosen opponent takes the whole top band; the others slide off to their own side.
                float colW = fi < 0 ? baseW : (i == fi ? 1920f : baseW);
                float x0 = fi < 0 ? i * baseW : i == fi ? 0f : i < fi ? -(fi - i) * baseW - 40f : 1920f + 40f + (i - fi - 1) * baseW;
                columnSpans[p.Id] = (x0, colW);
                var bg = zoneBackgrounds[i];
                float lift = focusMe ? 760f : 0f;   // my board focused: their cards slide up off the table
                bg.rectTransform.Place(x0 + 6, OppBandBottom + lift, colW - 12, 1080 - OppBandBottom - 6);
                var seat = Theme.PlayerColor(s.Players.IndexOf(p));
                bg.color = new Color(seat.r, seat.g, seat.b, p.Id == s.ActivePlayer ? 0.11f : 0.05f);   // each player's area in their colour

                // Nameplate: centred at the top of the column; when this board is focused it moves to the top-left
                // corner and fades (until hovered) so the cards can use the full height.
                bool focused = i == fi;
                float headerH = OppHeaderH * UiScale.Value;
                float pw = Mathf.Min(OppPanelW, colW - 24);
                var panel = PanelFor(p, false);
                panel.SetColor(Theme.PlayerColor(s.Players.IndexOf(p)));
                Vector2 plateOrigin = focused ? new Vector2(12, 1072) : new Vector2(x0 + colW / 2f, 1072);
                if (focused) panel.MoveTo(12, 1080 - OppHeaderH - 8, pw, OppHeaderH);
                else panel.MoveTo(x0 + colW / 2f - pw / 2f, 1080 - OppHeaderH - 8, pw, OppHeaderH);
                panel.Faded = focused;
                UiScaled.Add(panel, focused ? new Vector2(12, 1072) : plateOrigin).Pin = new Vector2(focused ? -1 : 0, 1);   // to the top edge, over its column
                panel.Render(p, s, false);
                panelCenters[p.Id] = focused ? new Vector2(12 + pw * UiScale.Value / 2f, 1072 - headerH / 2f) : new Vector2(x0 + colW / 2f, 1072 - headerH / 2f);

                // Field: rows hang from the top of the band and must never reach my side (below OppBandBottom).
                float fieldTop = focused ? 1080 - 12 : 1080 - headerH - 16;
                float fieldLeft = focused ? 12 + pw * UiScale.Value + 16 : x0 + 16;   // focused: clear of the faded nameplate
                float room = fieldTop - OppBandBottom - 8;
                float cardH = Mathf.Min(Mathf.Clamp(colW / 5.6f, 70, focused ? 190 : 150) * UiScale.CardScale, (room - 22f) / 2.05f);
                float otherY = fieldTop - cardH * 0.55f - 4;
                float creatureY = otherY - cardH - 18;
                LayoutField(p, fieldLeft, x0 + colW - 14, creatureY + lift, otherY + lift, cardH, placements, true);
            }

            // ---------------------------------------------------------------- selection tray
            var trayCards = new List<CardData>();
            foreach (var c in s.Prompt.SelectableCardData)
                if (!placements.ContainsKey(c.Id)) trayCards.Add(c);
            gc.Tray.Render(s, trayCards);

            // ---------------------------------------------------------------- leaving cards
            foreach (var id in cards.Keys.ToList())
            {
                if (placements.ContainsKey(id)) continue;
                var cv = cards[id];
                cards.Remove(id);
                if (cv == hoverCandidate) { hoverCandidate = null; HidePreview(); }
                PlayExitFor(cv, s);
            }

            // ---------------------------------------------------------------- arrivals, moves, highlights
            bool selecting = s.Prompt.SelMax > 0 && s.Prompt.SelectableCards.Any(id => placements.ContainsKey(id));
            int drawIndex = 0, drawsThisUpdate = placements.Values.Count(pl => pl.InHand && !cards.ContainsKey(pl.Data.Id));
            var untaps = new List<CardView>();
            var newAttackers = new List<(CardView cv, Placement pl)>();

            foreach (var kv in placements)
            {
                var pl = kv.Value;
                var d = pl.Data;
                MoveKind kind;
                float delay = 0f;

                if (!cards.TryGetValue(kv.Key, out var cv))
                {
                    cv = CardView.Create(cardLayer, kv.Key);
                    cv.Clicked = c => gc.ClickCard(c.CardId);
                    cv.RightClicked = c => { HidePreview(); gc.Inspector.Show(c.Data); };
                    cv.HoverStart = OnHoverStart;
                    cv.HoverEnd = OnHoverEnd;
                    cv.DragBegan = OnDragBegan;
                    cv.DragMoved = OnDragMoved;
                    cv.DragEnded = OnDragEnded;
                    cards[kv.Key] = cv;
                    cv.SetData(d);
                    kind = Arrive(cv, pl, s, ref drawIndex, drawsThisUpdate, out delay);
                }
                else
                {
                    var before = cv.Data;
                    bool zoneChanged = before != null && before.Zone != d.Zone;
                    bool controllerChanged = before != null && before.Controller != d.Controller;
                    kind = zoneChanged ? (d.Zone == "Battlefield" ? MoveKind.Land : MoveKind.Travel)
                         : controllerChanged ? MoveKind.Travel : MoveKind.Reflow;
                    if (before != null && before.Tapped && !d.Tapped) untaps.Add(cv);
                    if (before != null && !before.Attacking && d.Attacking) newAttackers.Add((cv, pl));
                    cv.SetData(d);
                }

                cv.MoveTo(pl.Pos, pl.Size, pl.Rotation, kind, delay);
                cv.HoverLift = true;
                var h = HighlightFor(d, s);
                cv.SetHighlight(h, selecting && d.Zone != "Command");
                cv.SetTag(TagFor(d, s, out var tagColor), tagColor);
                cv.SetPose(PoseFor(d, pl, s), Motion.Short);
            }

            // Untap: batched with a small stagger (left to right), kept inside the budget.
            if (untaps.Count > 1)
            {
                float step = Mathf.Min(0.030f, (Motion.Budget - Motion.Short) / untaps.Count);
                int i = 0;
                foreach (var cv in untaps.OrderBy(c => c.TargetPos.x)) cv.MoveTo(cv.TargetPos, cv.TargetSize, 0f, MoveKind.Reflow, step * i++);
            }

            // Attack declared: attackers step forward together, 40 ms stagger left to right; tapping is sharper.
            if (newAttackers.Count > 0)
            {
                float step = Mathf.Min(Motion.Stagger, (Motion.Budget - Motion.Short) / newAttackers.Count);
                int i = 0;
                foreach (var (cv, pl) in newAttackers.OrderBy(a => a.pl.Pos.x))
                {
                    cv.SharpTap();
                    cv.SetPose(PoseFor(pl.Data, pl, s) + Vector2.one * 0.01f, Motion.Short, step * i++);
                    if (pl.Data.HasKeyword("VIGILANCE")) cv.Pulse();   // vigilance: doesn't tap, brief shimmer instead
                }
            }

            DetectStackAndCombat(s);
            DetectEffects(s);
            DetectDamageAndLife(s);

            // Draw order: placement order so hands overlap correctly and auras sit behind their hosts.
            foreach (var kv in placements.OrderBy(p => p.Value.Order))
                cards[kv.Key].transform.SetAsLastSibling();
            foreach (var cv in cards.Values) if (cv.IsTraveling) cv.transform.SetAsLastSibling();   // flight layer
            PlaceFocusCards(s, placements);

            // Hand states: wobble while discarding to hand size; grey cards you can't play while you hold priority.
            bool discarding = s.Prompt.SelMax > 0 && (s.Prompt.Message ?? "").IndexOf("discard", System.StringComparison.OrdinalIgnoreCase) >= 0;
            bool myPriority = s.PriorityPlayer == s.Me && !s.Mulligan && s.Prompt.ActionableCards.Count > 0;
            foreach (var cv in cards.Values)
            {
                bool inHand = cv.Data != null && cv.Data.Zone == "Hand" && cv.Data.Controller == s.Me;
                cv.Wobble = inHand && discarding && s.Prompt.SelectableCards.Contains(cv.CardId);
                cv.SetUnplayable(inHand && myPriority && !s.Prompt.ActionableCards.Contains(cv.CardId) && !s.Prompt.SelectableCards.Contains(cv.CardId));
            }

            foreach (var id in panels.Keys.ToList())
            {
                if (s.Player(id) != null) continue;
                Destroy(panels[id].gameObject);
                panels.Remove(id);
            }
        }

        /// <summary>Decides where a card that just appeared comes from (design doc section 3).</summary>
        MoveKind Arrive(CardView cv, Placement pl, GameState s, ref int drawIndex, int drawsThisUpdate, out float delay)
        {
            delay = 0f;
            var d = pl.Data;
            int owner = d.Owner >= 0 ? d.Owner : d.Controller;

            // Resolving from the stack: drops from the stack column onto the battlefield.
            if (prev != null && prev.Stack.Any(si => si.SourceCardId == d.Id))
            {
                cv.SpawnFrom(StackPos, pl.Size * Motion.StackShowScale);
                return d.Zone == "Battlefield" ? MoveKind.Land : MoveKind.Travel;
            }

            if (pl.InHand)
            {
                // Draw: from the library pile, face-down, flipping mid-arc. Opening hands deal with a short stagger.
                cv.SpawnFrom(MyLibraryPos, new Vector2(110, 155));
                cv.RevealDuringTravel();
                if (drawsThisUpdate > 2) delay = Mathf.Min(0.015f * drawIndex, Motion.Budget - Motion.Travel);
                drawIndex++;
                return MoveKind.Travel;
            }

            if (d.Zone == "Battlefield")
            {
                // Returning from a graveyard / exile pile (reanimation, flicker).
                var pile = PileFor(prev, d.Id, owner);
                if (pile.HasValue) { cv.SpawnFrom(pile.Value, pl.Size * 0.6f); return MoveKind.Land; }

                if (d.Token || prev == null)
                {
                    // Token created: fades and scales in on its slot.
                    cv.SpawnFrom(pl.Pos, pl.Size, pl.Rotation);
                    if (prev != null)
                    {
                        cv.Materialize(Motion.Base * 0.8f);   // particles converge first, then the token fades in
                        vfx.Summon(pl.Pos, Palette.For(d.Colors));
                    }
                    return MoveKind.Snap;
                }
                // Played from hand (lands, or anything we didn't see on the stack): arcs in and settles.
                Vector2 from = owner == s.Me ? MyHandOrigin : PanelCenter(owner);
                cv.SpawnFrom(from, pl.Size);
                return MoveKind.Land;
            }

            // Command zone and anything else: from the owner's side.
            cv.SpawnFrom(owner == s.Me ? MyPortraitPos : PanelCenter(owner), pl.Size * 0.6f);
            return prev == null ? MoveKind.Snap : MoveKind.Travel;
        }

        /// <summary>Plays the right exit for a card that is no longer drawn on the board.</summary>
        void PlayExitFor(CardView cv, GameState s)
        {
            var before = cv.Data;
            int id = cv.CardId;
            int owner = before != null && before.Owner >= 0 ? before.Owner : before?.Controller ?? -1;
            bool mine = owner == s.Me;
            bool fromHand = before != null && before.Zone == "Hand";
            Vector2 ownerSpot = mine ? MyPortraitPos : PanelCenter(owner);

            // Cast: the card is now the source of a stack item.
            if (s.Stack.Any(si => si.SourceCardId == id)) { cv.PlayExit(ExitKind.ToStack, StackPos); return; }

            foreach (var p in s.Players)
            {
                if (p.Command.Any(c => c.Id == id)) { cv.PlayExit(ExitKind.ToLibrary, PanelCenter(p.Id)); return; }
                if (p.Graveyard.Any(c => c.Id == id))
                {
                    var target = ZonePos(p.Id, "Graveyard");
                    cv.PlayExit(fromHand ? ExitKind.Discard : ExitKind.Die, target);
                    return;
                }
                if (p.Exile.Any(c => c.Id == id))
                {
                    cv.PlayExit(ExitKind.Exile, ZonePos(p.Id, "Exile"));
                    return;
                }
            }
            // Not visible anywhere: shuffled or put back into a library (or a hand we can't see).
            cv.PlayExit(ExitKind.ToLibrary, owner >= 0 ? ZonePos(owner, "Library") : ownerSpot);
        }

        Vector2? PileFor(GameState s, int cardId, int owner)
        {
            if (s == null) return null;
            foreach (var p in s.Players)
            {
                if (p.Graveyard.Any(c => c.Id == cardId)) return ZonePos(p.Id, "Graveyard");
                if (p.Exile.Any(c => c.Id == cardId)) return ZonePos(p.Id, "Exile");
                if (p.Command.Any(c => c.Id == cardId)) return PanelCenter(p.Id);
            }
            return null;
        }

        Vector2 PanelCenter(int playerId) => panelCenters.TryGetValue(playerId, out var v) ? v : new Vector2(960, 900);

        /// <summary>Board position of a player's zone symbol (hand, library, graveyard, exile) in their nameplate.</summary>
        Vector2 ZonePos(int playerId, string zone)
        {
            if (panels.TryGetValue(playerId, out var panel))
            {
                var rt = panel.ZoneRect(zone);
                if (rt != null)
                {
                    Vector3 world = rt.TransformPoint(rt.rect.center);
                    Vector2 local = root.InverseTransformPoint(world);
                    return local + new Vector2(960, 540);
                }
            }
            return PanelCenter(playerId);
        }

        // ---------------------------------------------------------------- focus (scroll over an opponent)

        int focusedOpp = -1;
        bool focusMe;

        /// <summary>Keyboard (F): toggle the focus on my own board.</summary>
        public void ToggleFocusMe()
        {
            if (last == null) return;
            focusMe = !focusMe;
            if (focusMe) focusedOpp = -1;
            Sound.Play(Sfx.Draw, 0.4f);
            Render(last);
        }
        readonly Dictionary<int, (float x0, float w)> columnSpans = new Dictionary<int, (float, float)>();

        /// <summary>Keyboard: focus the n-th opponent (0-based), or -1 to show every board.</summary>
        public void FocusOpponent(int index)
        {
            if (last == null) return;
            var opps = last.Opponents.ToList();
            int id = index >= 0 && index < opps.Count ? opps[index].Id : -1;
            if (id == focusedOpp && !focusMe) return;
            focusedOpp = id;
            focusMe = false;
            Render(last);
        }

        /// <summary>Keyboard: focus the next opponent (wraps to everyone after the last).</summary>
        public void FocusNext()
        {
            if (last == null) return;
            var opps = last.Opponents.ToList();
            int i = opps.FindIndex(o => o.Id == focusedOpp);
            FocusOpponent(i + 1 >= opps.Count ? -1 : i + 1);
        }

        /// <summary>Scroll up over an opponent's area to focus their board; scroll down to show everyone again.</summary>
        void HandleFocusScroll()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f || last == null) return;
            if (gc.Dialog.IsOpen || gc.Menu.IsOpen || gc.ZoneViewer.IsOpen || gc.Options.IsOpen || gc.Inspector.IsOpen) return;
            if (gc.Tray.gameObject.activeSelf) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out var local)) return;
            var p = local + new Vector2(960, 540);
            if (wheel < 0f)
            {
                if (focusedOpp >= 0 || focusMe) { focusedOpp = -1; focusMe = false; Render(last); }
                return;
            }
            if (p.y < (focusMe ? MyFieldTop : OppBandBottom))
            {
                // Over my own battlefield (not the hand): focus my board.
                if (!focusMe && p.y > MyFieldBottom && p.x > MyFieldLeft - 20 && p.x < MyFieldRight + 20) { focusMe = true; focusedOpp = -1; Sound.Play(Sfx.Draw, 0.4f); Render(last); }
                return;
            }
            foreach (var kv in columnSpans)
            {
                if (last.Player(kv.Key) == null || kv.Key == last.Me) continue;
                if (p.x >= kv.Value.x0 && p.x < kv.Value.x0 + kv.Value.w && (focusedOpp != kv.Key || focusMe))
                {
                    focusedOpp = kv.Key;
                    focusMe = false;
                    Sound.Play(Sfx.Draw, 0.4f);
                    Render(last);
                    return;
                }
            }
#endif
        }

        // =================================================================================== stack / combat / damage

        void DetectStackAndCombat(GameState s)
        {
            if (prev == null) return;

            // Ability activated / trigger: the source pulses as its ability goes on the stack.
            foreach (var si in s.Stack)
            {
                if (prev.Stack.Any(p => p.Id == si.Id)) continue;
                if (si.SourceCardId >= 0 && cards.TryGetValue(si.SourceCardId, out var src) && src.Data != null && src.Data.Zone == "Battlefield")
                {
                    src.Pulse();
                    if (si.IsTrigger || si.IsAbility) vfx.TriggerNotice(src.CurrentPos, Palette.For(src.Data.Colors));   // trigger notice
                }
            }

            // Damage step (section 5.2): entering a combat damage step plays wind-up, lunge, impact, recoil.
            bool firstStrikeStep = s.Phase == "COMBAT_FIRST_STRIKE_DAMAGE";
            bool damageStep = s.Phase == "COMBAT_DAMAGE" || firstStrikeStep;
            if (!damageStep || prev.Phase == s.Phase) return;
            var combat = s.Combat.Count > 0 ? s.Combat : prev.Combat;
            var attackers = combat
                .Where(c => cards.ContainsKey(c.Attacker))
                .Select(c => (c, cv: cards[c.Attacker]))
                .Where(a =>
                {
                    var d = a.cv.Data;
                    bool fs = d != null && (d.HasKeyword("FIRST_STRIKE") || d.HasKeyword("DOUBLE_STRIKE"));
                    bool onlyFirst = d != null && d.HasKeyword("FIRST_STRIKE") && !d.HasKeyword("DOUBLE_STRIKE");
                    return firstStrikeStep ? fs : !onlyFirst;
                })
                .OrderBy(a => a.cv.TargetPos.x)
                .ToList();
            if (attackers.Count == 0) return;

            float step = attackers.Count > 1 ? Mathf.Min(0.120f, 0.120f / (attackers.Count - 1)) : 0f;
            for (int i = 0; i < attackers.Count; i++)
            {
                var (cmb, cv) = attackers[i];
                CardView blocker = cmb.Blockers.Select(b => cards.TryGetValue(b, out var bc) ? bc : null).FirstOrDefault(b => b != null);
                Vector2 target = blocker != null ? blocker.CurrentPos
                    : cmb.DefenderKind == "player" ? PanelCenter(cmb.DefenderId)
                    : cards.TryGetValue(cmb.DefenderId, out var pw) ? pw.CurrentPos : PanelCenter(cmb.DefenderId);
                bool deathtouch = cv.Data != null && cv.Data.HasKeyword("DEATHTOUCH");
                if (firstStrikeStep || (cv.Data != null && cv.Data.HasKeyword("DOUBLE_STRIKE"))) cv.Pulse();   // blade glint
                var attackerView = cv;
                int defenderId = cmb.DefenderKind == "player" ? cmb.DefenderId : -1;
                cv.Lunge(target, step * i, () =>
                {
                    // Impact: both cards flash, the target shakes. Deathtouch hits in sickly green-black.
                    Color? tint = deathtouch ? new Color(0.35f, 0.75f, 0.3f) : (Color?)null;
                    Sound.Play(Sfx.Impact, blocker != null ? 0.9f : 1f, deathtouch ? 0.8f : 1f);
                    if (blocker != null && blocker != null && !blocker.IsExiting) { blocker.Hit(attackerView.Data?.Power ?? 1, tint); attackerView.Hit(blocker.Data?.Power ?? 0); }
                    else if (defenderId >= 0 && panels.TryGetValue(defenderId, out var dp)) dp.Impact();
                });
            }
        }

        void DetectDamageAndLife(GameState s)
        {
            if (prev == null) return;
            // Damage marked on creatures: red number above the card.
            foreach (var kv in cards)
            {
                var d = kv.Value.Data;
                if (d == null || d.Zone != "Battlefield") continue;
                if (!prev.CardsById.TryGetValue(d.Id, out var before)) continue;
                int delta = d.Damage - before.Damage;
                if (delta > 0) fx.Number(kv.Value.CurrentPos + new Vector2(0, kv.Value.TargetSize.y * 0.45f), "-" + delta, new Color(1f, 0.32f, 0.3f));
            }
            // Life changes: red for damage, gold for gain (lifelink, gain effects); the panel rolls and shakes.
            foreach (var p in s.Players)
            {
                var before = prev.Player(p.Id);
                if (before == null || before.Life == p.Life) continue;
                int delta = p.Life - before.Life;
                var at = PanelCenter(p.Id) + new Vector2(0, p.Id == s.Me ? 120 : -60);
                fx.Number(at, (delta > 0 ? "+" : "") + delta, delta > 0 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.32f, 0.3f));
            }
        }

        /// <summary>
        /// Effect families (design doc 6.1) from what changed: projectiles for damage, beams for destroy/exile,
        /// aura washes for buffs, an area pulse for wipes, a shield slam for counterspells, mana orbs, drains.
        /// </summary>
        void DetectEffects(GameState s)
        {
            if (prev == null || vfx == null) return;
            Vector2 PosOf(int id) => cards.TryGetValue(id, out var cv) ? cv.CurrentPos : posBefore.TryGetValue(id, out var p) ? p : StackPos;
            bool OnField(GameState g, int id) => g.CardsById.TryGetValue(id, out var c) && c.Zone == "Battlefield";

            var removed = prev.Stack.Where(si => !s.Stack.Any(x => x.Id == si.Id)).ToList();
            var countered = new HashSet<int>();
            if (removed.Count >= 2)
            {
                // A spell leaving at the same moment as the item above it, straight to a graveyard: countered.
                var top = removed[0];
                foreach (var si in removed.Skip(1))
                {
                    if (si.IsAbility || si.IsTrigger) continue;
                    bool toGrave = s.Players.Any(p => p.Graveyard.Any(c => c.Id == si.SourceCardId) || p.Exile.Any(c => c.Id == si.SourceCardId));
                    if (!toGrave) continue;
                    countered.Add(si.Id);
                    vfx.Counter(StackPos, Palette.For(top.Source?.Colors));
                    Sound.Play(Sfx.Impact, 0.8f, 1.3f);
                }
            }

            int creaturesLost = prev.CardsById.Values.Count(c => c.Zone == "Battlefield" && c.IsCreature && !OnField(s, c.Id));
            foreach (var si in removed)
            {
                if (countered.Contains(si.Id)) continue;
                var pal = Palette.For(si.Source?.Colors);
                int hits = 0;
                foreach (int tid in si.TargetCards)
                {
                    if (!prev.CardsById.TryGetValue(tid, out var before) || before.Zone != "Battlefield") continue;
                    var to = PosOf(tid);
                    if (!OnField(s, tid)) { vfx.Beam(StackPos, to, pal, hits * Motion.Stagger); hits++; continue; }
                    var now = s.CardsById[tid];
                    if (now.Damage > before.Damage) { vfx.Projectile(StackPos, to, pal, Mathf.Clamp01((now.Damage - before.Damage) / 6f), () => Sound.Play(Sfx.Impact, 0.55f)); hits++; }
                    else if (now.Power > before.Power || now.Toughness > before.Toughness)
                    {
                        var size = cards.TryGetValue(tid, out var cv) ? cv.TargetSize : new Vector2(107, 150);
                        vfx.AuraWash(to, size, pal); hits++;
                    }
                }
                foreach (int pid in si.TargetPlayers)
                {
                    var a = prev.Player(pid); var b = s.Player(pid);
                    if (a == null || b == null) continue;
                    if (b.Life < a.Life) { vfx.Projectile(StackPos, PanelCenter(pid), pal, Mathf.Clamp01((a.Life - b.Life) / 6f)); hits++; }
                }
                // No targets, several creatures gone at once: a board wipe.
                if (hits == 0 && si.TargetCards.Count == 0 && creaturesLost >= 3) vfx.AreaPulse(StackPos, pal);
            }

            // Life: a player gains while another loses at the same time (drain / lifelink) -> particles stream across.
            var gainers = s.Players.Where(p => prev.Player(p.Id) != null && p.Life > prev.Player(p.Id).Life).ToList();
            var losers = s.Players.Where(p => prev.Player(p.Id) != null && p.Life < prev.Player(p.Id).Life).ToList();
            foreach (var g in gainers)
            {
                if (losers.Count > 0 && losers.Count <= 3)
                    foreach (var l in losers) vfx.Drain(PanelCenter(l.Id), PanelCenter(g.Id), Palette.For("B"));
                vfx.Motes(PanelCenter(g.Id), new Color(1f, 0.85f, 0.4f));   // gain: gold glow + rising motes
            }

            // Mana: my land taps and my pool grows -> a small orb from the land to the pool.
            var me = s.MePlayer; var meBefore = prev.MePlayer;
            if (me != null && meBefore != null && me.TotalManaInPool > meBefore.TotalManaInPool)
            {
                string colour = "C";
                foreach (var kv in me.Mana)
                {
                    meBefore.Mana.TryGetValue(kv.Key, out int was);
                    if (kv.Value > was) { colour = kv.Key; break; }
                }
                var pool = ZonePos(s.Me, "Mana");
                foreach (var c in me.Battlefield)
                    if (c.Tapped && prev.CardsById.TryGetValue(c.Id, out var b) && !b.Tapped && cards.TryGetValue(c.Id, out var cv))
                        vfx.Mana(cv.CurrentPos, pool, Theme.ForManaColor(colour));
            }
        }

        /// <summary>Combat poses: attackers step toward the opponent (tilt.attack), blockers step forward to meet them.</summary>
        Vector2 PoseFor(CardData d, Placement pl, GameState s)
        {
            if (d == null || d.Zone != "Battlefield") return Vector2.zero;
            float dir = d.Controller == s.Me ? 1f : -1f;   // my side faces up, opponents face down
            if (d.Attacking) return new Vector2(0, dir * pl.Size.y * Motion.AttackForward);
            if (s.IsBlocking(d.Id)) return new Vector2(0, dir * pl.Size.y * 0.15f);
            return Vector2.zero;
        }

        // =================================================================================== per frame

        /// <summary>The mulligan hand is lifted into a layer above the HUD buttons so nothing covers it.</summary>
        void PlaceFocusCards(GameState s, Dictionary<int, Placement> placements)
        {
            bool any = false;
            foreach (var kv in cards)
            {
                bool focus = s.Mulligan && placements.TryGetValue(kv.Key, out var pl) && pl.InHand;
                if (focus && focusLayer == null)
                {
                    focusLayer = UIKit.Rect("FocusCards", (RectTransform)root.parent).Stretch().SubCanvas();
                }
                var want = focus ? focusLayer : cardLayer;
                if (want != null && kv.Value.transform.parent != want) kv.Value.transform.SetParent(want, false);
                any |= focus;
            }
            if (any)
            {
                focusLayer.SetAsLastSibling();
                foreach (var kv in placements.OrderBy(p => p.Value.Order))
                    if (cards.TryGetValue(kv.Key, out var cv) && cv.transform.parent == focusLayer) cv.transform.SetAsLastSibling();
            }
        }

        void Update()
        {
            if (dimmer != null)
            {
                bool dim = last != null && gameObject.activeInHierarchy &&
                           (last.Mulligan || (gc.Tray != null && gc.Tray.gameObject.activeSelf && !gc.Tray.Collapsed));
                var c = dimmer.color;
                float a = Mathf.MoveTowards(c.a, dim ? 0.62f : 0f, Time.unscaledDeltaTime * 2.5f);
                if (!Mathf.Approximately(a, c.a)) { c.a = a; dimmer.color = c; }
                dimmer.transform.SetAsLastSibling();
            }
            UpdateInner();
        }

        void OnEnable() { if (focusLayer != null) focusLayer.gameObject.SetActive(true); }
        void OnDisable() { if (focusLayer != null) focusLayer.gameObject.SetActive(false); }

        void UpdateInner()
        {
            HandleFocusScroll();
            DrawCombatLines();

            // Large preview after the hover delay; right-click opens the full-size inspector instead.
            if (hoverCandidate != null && !previewShown && hoverCandidate.Data != null)
            {
                float wait = UiScale.PreviewDelay;   // Options > card preview delay (1.5 s by default)
                if (Motion.Now - hoverSince >= wait)
                {
                    previewShown = true;
                    gc.Preview.Show(hoverCandidate.Data, hoverCandidate.CurrentPos.x < 960);
                }
            }
        }

        StackItemData stackHover;
        /// <summary>Hovering a stack item draws thin arrows from the stack to its targets.</summary>
        public void SetStackHover(StackItemData item) => stackHover = item;

        void DrawCombatLines()
        {
            fx.BeginLines();
            var s = last;
            if (stackHover != null)
            {
                var col = new Color(1f, 0.85f, 0.45f, 0.75f);
                foreach (int id in stackHover.TargetCards)
                    if (cards.TryGetValue(id, out var tc)) fx.AddLine(StackPos + new Vector2(-150, 0), tc.CurrentPos, col, 3f, true);
                foreach (int pid in stackHover.TargetPlayers) fx.AddLine(StackPos + new Vector2(-150, 0), PanelCenter(pid), col, 3f, true);
            }
            if (s != null)
            {
                foreach (var c in s.Combat)
                {
                    if (!cards.TryGetValue(c.Attacker, out var atk)) continue;
                    Vector2 from = atk.CurrentPos;
                    // Faint arrow from attacker to its defender.
                    Vector2 to = c.DefenderKind == "player" ? PanelCenter(c.DefenderId)
                        : cards.TryGetValue(c.DefenderId, out var pw) ? pw.CurrentPos : PanelCenter(c.DefenderId);
                    if (c.Blockers.Count == 0)
                    {
                        Vector2 dirv = (to - from).normalized;
                        fx.AddLine(from + dirv * atk.TargetSize.y * 0.5f, to - dirv * 40f, new Color(0.89f, 0.34f, 0.18f, 0.35f), 4f, true);
                    }
                    // Solid connector from each blocker to the attacker.
                    foreach (var b in c.Blockers)
                    {
                        if (!cards.TryGetValue(b, out var blk)) continue;
                        fx.AddLine(blk.CurrentPos, from, new Color(0.29f, 0.5f, 0.76f, 0.85f), 4f, false);
                    }
                }
            }
            fx.EndLines();
        }

        // =================================================================================== hover

        void OnHoverStart(CardView cv)
        {
            hoverCandidate = cv;
            hoverSince = Motion.Now;
            previewShown = false;
            if (cv.Data != null && cv.Data.Zone == "Hand" && hoveredHandId != cv.CardId)
            {
                hoveredHandId = cv.CardId;
                RelayoutHand();
            }
        }

        void OnHoverEnd(CardView cv)
        {
            if (hoverCandidate == cv) { hoverCandidate = null; HidePreview(); }
            if (hoveredHandId == cv.CardId)
            {
                hoveredHandId = -1;
                RelayoutHand();
            }
        }

        void HidePreview()
        {
            previewShown = false;
            gc.Preview.Hide();
        }

        /// <summary>Neighbours spread away from the hovered hand card (20% of a card width).</summary>
        void RelayoutHand()
        {
            var me = last?.MePlayer;
            if (me == null) return;
            var placements = new Dictionary<int, Placement>();
            LayoutHand(me.Hand, placements);
            // Left-to-right draw order so the hand overlaps correctly after a reorder; the dragged card stays on top.
            foreach (var kv in placements.OrderBy(k => k.Value.Order))
                if (cards.TryGetValue(kv.Key, out var cv) && !cv.IsExiting)
                {
                    if (cv != dragging) cv.MoveTo(kv.Value.Pos, kv.Value.Size, kv.Value.Rotation, MoveKind.Reflow);
                    cv.transform.SetAsLastSibling();
                }
            if (dragging != null) dragging.transform.SetAsLastSibling();
        }

        // =================================================================================== layout

        void LayoutHand(List<CardData> hand, Dictionary<int, Placement> outp)
        {
            int n = hand.Count;
            if (n == 0) return;
            if (last != null && last.Mulligan) { LayoutMulliganHand(hand, outp); return; }
            hand = OrderedHand(hand);
            float h = HandCardH, w = h * 0.714f;
            float maxWidth = MyFieldRight - HandLeft;
            float spacing = n == 1 ? 0 : Mathf.Min(w * 0.82f, (maxWidth - w) / (n - 1));
            float total = spacing * (n - 1);
            float cx = (HandLeft + MyFieldRight) / 2f;
            int hoverIndex = hand.FindIndex(c => c.Id == hoveredHandId);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0 : (i / (float)(n - 1)) * 2f - 1f; // -1..1
                float x = cx - total / 2 + i * spacing;
                if (hoverIndex >= 0 && i != hoverIndex) x += (i < hoverIndex ? -1f : 1f) * w * 0.2f;
                float y = HandY - Mathf.Abs(t) * Mathf.Abs(t) * 18f;
                outp[hand[i].Id] = new Placement
                {
                    Data = hand[i], Pos = new Vector2(x, y), Size = new Vector2(w, h),
                    Rotation = i == hoverIndex ? 0f : -t * 6f, Order = 10000 + i, InHand = true,
                };
            }
        }

        // =================================================================================== drag (hand cards)

        readonly List<int> handOrder = new List<int>();   // my own arrangement of the hand (client-side only)
        CardView dragging;
        int dragInsert = -1;
        // Drop zones (board y): below HandBandY reorders the hand; above PlayLineY (well up the battlefield)
        // plays the card; in between is a neutral strip where letting go just returns the card.
        const float HandBandY = 300f, PlayLineY = 440f;

        /// <summary>The hand in my arrangement: cards I've ordered keep their place; new cards go on the right.</summary>
        List<CardData> OrderedHand(List<CardData> hand)
        {
            handOrder.RemoveAll(id => !hand.Exists(c => c.Id == id));
            foreach (var c in hand) if (!handOrder.Contains(c.Id)) handOrder.Add(c.Id);
            var list = new List<CardData>(hand);
            list.Sort((a, b) => handOrder.IndexOf(a.Id).CompareTo(handOrder.IndexOf(b.Id)));
            // While dragging, the dragged card is lifted out and the rest close up around the insertion gap.
            if (dragging != null && dragInsert >= 0)
            {
                var d = list.Find(c => c.Id == dragging.CardId);
                if (d != null) { list.Remove(d); list.Insert(Mathf.Clamp(dragInsert, 0, list.Count), d); }
            }
            return list;
        }

        Vector2 ToBoard(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var local);
            return local + new Vector2(960, 540);
        }

        void OnDragBegan(CardView cv, Vector2 screen)
        {
            if (cv.Data == null || cv.Data.Zone != "Hand" || last == null || cv.Data.Controller != last.Me || last.Mulligan) return;
            dragging = cv;
            cv.Dragging = true;
            cv.DragBoardPos = ToBoard(screen);
            cv.transform.SetAsLastSibling();
            hoverCandidate = null; HidePreview();
            Sound.Play(Sfx.CardSlide, 0.5f, 0.85f);   // picked up
        }

        void OnDragMoved(CardView cv, Vector2 screen)
        {
            if (dragging != cv) return;
            var p = ToBoard(screen);
            cv.DragBoardPos = p;
            // In the hand band: show where it would land; above it: no gap (it's about to be played).
            int insert = p.y < HandBandY ? HandIndexAt(p.x) : -1;
            if (insert != dragInsert)
            {
                dragInsert = insert;
                RelayoutHand();
            }
        }

        void OnDragEnded(CardView cv, Vector2 screen)
        {
            if (dragging != cv) return;
            var p = ToBoard(screen);
            dragging = null;
            cv.Dragging = false;
            if (p.y >= PlayLineY && last != null)
            {
                // Dropped on the battlefield: play it if Forge allows; otherwise it slides back.
                if (last.Prompt.ActionableCards.Contains(cv.CardId) || last.Prompt.SelectableCards.Contains(cv.CardId)) gc.ClickCard(cv.CardId);
                else gc.Toasts.Show("You can't play " + (cv.Data?.Name ?? "that card") + " right now.");
            }
            else if (dragInsert >= 0)
            {
                // Dropped in the hand: keep the new order.
                handOrder.Remove(cv.CardId);
                var others = handOrder.Where(id => id != cv.CardId).ToList();
                others.Insert(Mathf.Clamp(dragInsert, 0, others.Count), cv.CardId);
                handOrder.Clear(); handOrder.AddRange(others);
                Sound.Play(Sfx.CardSlide, 0.5f, 0.85f);   // same swish as picking it up
            }
            dragInsert = -1;
            RelayoutHand();
        }

        /// <summary>Insertion index in the hand for a pointer x (between the cards it falls among).</summary>
        int HandIndexAt(float x)
        {
            var me = last?.MePlayer;
            if (me == null) return 0;
            int n = me.Hand.Count;
            if (n <= 1) return 0;
            float w = HandCardH * 0.714f;
            float maxWidth = MyFieldRight - HandLeft;
            float spacing = Mathf.Min(w * 0.82f, (maxWidth - w) / (n - 1));
            float cx = (HandLeft + MyFieldRight) / 2f;
            float start = cx - spacing * (n - 1) / 2f;
            return Mathf.Clamp(Mathf.RoundToInt((x - start) / spacing), 0, n - 1);
        }

        /// <summary>Mulligan (design doc 8): the opening hand is presented large at the centre of the board.</summary>
        void LayoutMulliganHand(List<CardData> hand, Dictionary<int, Placement> outp)
        {
            int n = hand.Count;
            float h = Mathf.Min(330f, HandCardH * 1.5f), w = h * 0.714f;
            float spacing = Mathf.Min(w + 14f, 1280f / Mathf.Max(1, n));
            float cx = 820f, y = 470f;
            for (int i = 0; i < n; i++)
            {
                float x = cx + (i - (n - 1) / 2f) * spacing;
                outp[hand[i].Id] = new Placement { Data = hand[i], Pos = new Vector2(x, y), Size = new Vector2(w, h), Rotation = 0f, Order = 10000 + i, InHand = true };
            }
        }

        void LayoutField(PlayerData p, float left, float right, float creatureY, float otherY, float cardH,
                         Dictionary<int, Placement> outp, bool isOpponent)
        {
            var onField = p.Battlefield;
            var hosts = new HashSet<int>(onField.Select(c => c.Id));
            var attachments = onField.Where(c => c.AttachedTo >= 0 && hosts.Contains(c.AttachedTo)).ToList();
            var free = onField.Where(c => !(c.AttachedTo >= 0 && hosts.Contains(c.AttachedTo))).ToList();

            var creatures = free.Where(c => c.IsCreature).ToList();
            var lands = free.Where(c => !c.IsCreature && c.IsLand).OrderBy(c => c.Name).ThenBy(c => c.Id).ToList();
            var others = free.Where(c => !c.IsCreature && !c.IsLand).ToList();

            int baseOrder = isOpponent ? 100 : 2000;
            LayoutRow(creatures, left, creatureY - cardH / 2, right - left, cardH, outp, baseOrder + 200, true);

            int total = Mathf.Max(1, lands.Count + others.Count);
            float split = others.Count == 0 ? left : lands.Count == 0 ? right : left + (right - left) * Mathf.Clamp(others.Count / (float)total, 0.25f, 0.6f);
            if (others.Count > 0) LayoutRow(others, left, otherY - cardH / 2, split - left - 10, cardH, outp, baseOrder, true);
            if (lands.Count > 0) LayoutRow(lands, split, otherY - cardH / 2, right - split, cardH, outp, baseOrder + 100, true);

            // Auras and equipment tuck behind their host with a 15% offset.
            var perHost = new Dictionary<int, int>();
            foreach (var a in attachments)
            {
                if (!outp.TryGetValue(a.AttachedTo, out var host)) continue;
                perHost.TryGetValue(a.AttachedTo, out int k);
                perHost[a.AttachedTo] = k + 1;
                outp[a.Id] = new Placement
                {
                    Data = a,
                    Pos = host.Pos + new Vector2(host.Size.x * 0.15f * (k + 1), host.Size.y * 0.15f * (k + 1)),
                    Size = host.Size * 0.92f,
                    Rotation = a.Tapped ? -90 : 0,
                    Order = host.Order - 1 - k,
                };
            }
        }

        void LayoutRow(List<CardData> row, float x, float y, float width, float cardH, Dictionary<int, Placement> outp, int baseOrder, bool rotateTapped)
        {
            int n = row.Count;
            if (n == 0) return;
            float cardW = cardH * 0.714f;
            float slot = cardH * 0.82f;
            float spacing = n == 1 ? 0 : Mathf.Min(slot + 6, (width - slot) / (n - 1));
            float total = spacing * (n - 1) + slot;
            float startX = rotateTapped ? x + (width - total) / 2 + slot / 2 : x + cardW / 2;
            for (int i = 0; i < n; i++)
            {
                var c = row[i];
                outp[c.Id] = new Placement
                {
                    Data = c,
                    Pos = new Vector2(startX + i * spacing, y + cardH / 2),
                    Size = new Vector2(cardW, cardH),
                    Rotation = rotateTapped && c.Tapped ? -90 : 0,
                    Order = baseOrder + i,
                };
            }
        }

        /// <summary>One highlight per card: Selected > Eligible > Attacking/Blocking > Playable > Idle.</summary>
        Highlight HighlightFor(CardData c, GameState s)
        {
            if (c == null) return Highlight.None;
            var p = s.Prompt;
            if (p.ChosenCards.Contains(c.Id)) return Highlight.Selected;
            if (p.SelectableCards.Contains(c.Id) && p.SelMax > 0) return Highlight.Eligible;
            if (c.Attacking) return Highlight.Attacking;
            if (s.IsBlocking(c.Id)) return Highlight.Blocking;
            if (p.ActionableCards.Contains(c.Id) || p.SelectableCards.Contains(c.Id)) return Highlight.Playable;
            return Highlight.None;
        }

        string TagFor(CardData c, GameState s, out Color color)
        {
            color = new Color(0.25f, 0.25f, 0.3f, 0.9f);
            if (c == null || c.Zone != "Battlefield") return null;
            if (c.Sick && c.IsCreature && c.Controller == s.Me && s.ActivePlayer == s.Me && !c.Attacking) return "Summoning sick";
            return null;
        }

        PlayerPanel PanelFor(PlayerData p, bool isMe)
        {
            if (!panels.TryGetValue(p.Id, out var panel))
            {
                panel = PlayerPanel.Create(panelLayer, gc, isMe);
                panels[p.Id] = panel;
            }
            return panel;
        }

        void EnsureZoneBackgrounds(int n)
        {
            while (zoneBackgrounds.Count < n)
            {
                var img = UIKit.Panel("OppZone" + zoneBackgrounds.Count, zoneLayer, new Color(1, 1, 1, 0.03f));
                img.raycastTarget = false;
                zoneBackgrounds.Add(img);
            }
            for (int i = 0; i < zoneBackgrounds.Count; i++) zoneBackgrounds[i].SetVisible(i < n);
        }

        public void Clear()
        {
            foreach (var cv in cards.Values) Destroy(cv.gameObject);
            cards.Clear();
            foreach (var p in panels.Values) Destroy(p.gameObject);
            panels.Clear();
            prev = last = null;
            hoveredHandId = -1;
            hoverCandidate = null;
            focusedOpp = -1;
            focusMe = false;
            columnSpans.Clear();
        }
    }
}

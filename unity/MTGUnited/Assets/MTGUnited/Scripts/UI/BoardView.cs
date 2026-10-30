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
        const float MyFieldLeft = 280, MyFieldRight = 1420;
        const float MyCreatureRowY = 470, MyOtherRowY = 300;
        const float MyCardH = 150;
        const float HandY = 70, HandCardH = 220;
        const float OppBandBottom = 600, OppHeaderH = 76;

        // Where things fly to / from.
        static readonly Vector2 MyLibraryPos = new Vector2(1495, 457);
        static readonly Vector2 MyGraveyardPos = new Vector2(1495, 287);
        static readonly Vector2 MyExilePos = new Vector2(1495, 117);
        static readonly Vector2 MyPortraitPos = new Vector2(140, 120);
        static readonly Vector2 MyHandOrigin = new Vector2(850, 40);
        static readonly Vector2 StackPos = new Vector2(1745, 482);

        GameController gc;
        RectTransform root, zoneLayer, cardLayer, panelLayer;
        FxLayer fx;
        readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        readonly Dictionary<int, PlayerPanel> panels = new Dictionary<int, PlayerPanel>();
        readonly Dictionary<int, Vector2> panelCenters = new Dictionary<int, Vector2>();
        readonly List<Image> zoneBackgrounds = new List<Image>();
        PilesView myPiles;
        Image myFieldBg;

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
            bv.panelLayer = UIKit.Rect("Panels", rt).Stretch();
            bv.cardLayer = UIKit.Rect("Cards", rt).Stretch();
            bv.fx = FxLayer.Create(rt);

            bv.myFieldBg = UIKit.Panel("MyField", bv.zoneLayer, new Color(1f, 1f, 1f, 0.035f));
            bv.myFieldBg.rectTransform.Place(MyFieldLeft - 10, 215, MyFieldRight - MyFieldLeft + 20, 340);
            bv.myFieldBg.raycastTarget = false;

            bv.myPiles = PilesView.Create(bv.panelLayer, gc);
            bv.myPiles.Rect.Place(1440, 40, 120, 500);
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
            var me = s.MePlayer;
            var opps = s.Opponents.ToList();
            var placements = new Dictionary<int, Placement>();

            // ---------------------------------------------------------------- me
            if (me != null)
            {
                var myPanel = PanelFor(me, true);
                myPanel.Rect.Place(20, 20, 240, 200);
                myPanel.Render(me, s, true);
                panelCenters[me.Id] = MyPortraitPos;
                myPiles.Render(me, s);

                LayoutHand(me.Hand, placements);
                LayoutField(me, MyFieldLeft, MyFieldRight, MyCreatureRowY, MyOtherRowY, MyCardH, placements, false);
                LayoutRow(me.Command, 30, 230, 90, 126, placements, 0, false);
            }

            // ---------------------------------------------------------------- opponents
            int n = Mathf.Max(1, opps.Count);
            float colW = 1920f / n;
            EnsureZoneBackgrounds(n);
            for (int i = 0; i < opps.Count; i++)
            {
                var p = opps[i];
                float x0 = i * colW;
                var bg = zoneBackgrounds[i];
                bg.rectTransform.Place(x0 + 6, OppBandBottom, colW - 12, 1080 - OppBandBottom - 6);
                bg.color = p.Id == s.ActivePlayer ? new Color(1f, 0.8f, 0.4f, 0.06f) : new Color(1f, 1f, 1f, 0.03f);

                var panel = PanelFor(p, false);
                panel.Rect.Place(x0 + 12, 1080 - OppHeaderH - 8, colW - 24, OppHeaderH);
                panel.Render(p, s, false);
                panelCenters[p.Id] = new Vector2(x0 + colW / 2f, 1080 - OppHeaderH / 2f - 8);

                float cardH = Mathf.Clamp(colW / 5.6f, 70, 150);
                float fieldTop = 1080 - OppHeaderH - 16;
                float otherY = fieldTop - cardH * 0.55f - 4;
                float creatureY = otherY - cardH - 18;
                float cmdW = cardH * 0.62f;
                LayoutRow(p.Command, x0 + 12, otherY - cardH * 0.45f, cmdW, cardH * 0.9f, placements, 0, false);
                LayoutField(p, x0 + 18 + cmdW + 8, x0 + colW - 14, creatureY, otherY, cardH, placements, true);
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
                    cv.HoverStart = OnHoverStart;
                    cv.HoverEnd = OnHoverEnd;
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
            DetectDamageAndLife(s);

            // Draw order: placement order so hands overlap correctly and auras sit behind their hosts.
            foreach (var kv in placements.OrderBy(p => p.Value.Order))
                cards[kv.Key].transform.SetAsLastSibling();

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
                    if (prev != null) cv.Materialize();
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
                if (p.Graveyard.Any(c => c.Id == id))
                {
                    var target = p.Id == s.Me ? MyGraveyardPos : PanelCenter(p.Id);
                    cv.PlayExit(fromHand ? ExitKind.Discard : ExitKind.Die, target);
                    return;
                }
                if (p.Exile.Any(c => c.Id == id))
                {
                    cv.PlayExit(ExitKind.Exile, p.Id == s.Me ? MyExilePos : PanelCenter(p.Id));
                    return;
                }
            }
            // Not visible anywhere: shuffled or put back into a library (or a hand we can't see).
            cv.PlayExit(ExitKind.ToLibrary, mine ? MyLibraryPos : ownerSpot);
        }

        Vector2? PileFor(GameState s, int cardId, int owner)
        {
            if (s == null) return null;
            foreach (var p in s.Players)
            {
                if (p.Graveyard.Any(c => c.Id == cardId)) return p.Id == s.Me ? MyGraveyardPos : PanelCenter(p.Id);
                if (p.Exile.Any(c => c.Id == cardId)) return p.Id == s.Me ? MyExilePos : PanelCenter(p.Id);
            }
            return null;
        }

        Vector2 PanelCenter(int playerId) => panelCenters.TryGetValue(playerId, out var v) ? v : new Vector2(960, 900);

        // =================================================================================== stack / combat / damage

        void DetectStackAndCombat(GameState s)
        {
            if (prev == null) return;

            // Ability activated / trigger: the source pulses as its ability goes on the stack.
            foreach (var si in s.Stack)
            {
                if (prev.Stack.Any(p => p.Id == si.Id)) continue;
                if (si.SourceCardId >= 0 && cards.TryGetValue(si.SourceCardId, out var src) && src.Data != null && src.Data.Zone == "Battlefield")
                    src.Pulse();
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

        void Update()
        {
            DrawCombatLines();

            // Large preview: after 500 ms for hand cards (design doc), quickly for others.
            if (hoverCandidate != null && !previewShown && hoverCandidate.Data != null)
            {
                float wait = hoverCandidate.Data.Zone == "Hand" ? 0.5f : 0.15f;
                if (Motion.Now - hoverSince >= wait)
                {
                    previewShown = true;
                    gc.Preview.Show(hoverCandidate.Data, hoverCandidate.CurrentPos.x < 960);
                }
            }
        }

        void DrawCombatLines()
        {
            fx.BeginLines();
            var s = last;
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
            foreach (var kv in placements)
                if (cards.TryGetValue(kv.Key, out var cv) && !cv.IsExiting)
                    cv.MoveTo(kv.Value.Pos, kv.Value.Size, kv.Value.Rotation, MoveKind.Reflow);
        }

        // =================================================================================== layout

        void LayoutHand(List<CardData> hand, Dictionary<int, Placement> outp)
        {
            int n = hand.Count;
            if (n == 0) return;
            float h = HandCardH, w = h * 0.714f;
            float maxWidth = MyFieldRight - MyFieldLeft;
            float spacing = n == 1 ? 0 : Mathf.Min(w * 0.82f, (maxWidth - w) / (n - 1));
            float total = spacing * (n - 1);
            float cx = (MyFieldLeft + MyFieldRight) / 2f;
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
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Lays out every visible card for a Commander pod on a fixed 1920x1080 board:
    /// you at the bottom (hand, two battlefield rows, command zone, piles), opponents side by side across the top.
    /// Each render computes a target for every card; CardView animates towards it.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        // Board regions (bottom-left origin, 1920x1080 space).
        const float MyFieldLeft = 280, MyFieldRight = 1420;
        const float MyCreatureRowY = 470, MyOtherRowY = 300;
        const float MyCardH = 150;
        const float HandY = 70, HandCardH = 220;
        const float OppBandBottom = 600, OppHeaderH = 76;

        GameController gc;
        RectTransform root, zoneLayer, cardLayer, panelLayer;
        readonly Dictionary<int, CardView> cards = new Dictionary<int, CardView>();
        readonly Dictionary<int, PlayerPanel> panels = new Dictionary<int, PlayerPanel>();
        readonly List<Image> zoneBackgrounds = new List<Image>();
        PilesView myPiles;
        Text trayTitle;
        Image trayBg;
        Image myFieldBg;

        public static BoardView Create(RectTransform board, GameController gc)
        {
            var rt = UIKit.Rect("BoardView", board).Stretch();
            var bv = rt.gameObject.AddComponent<BoardView>();
            bv.gc = gc;
            bv.root = rt;
            bv.zoneLayer = UIKit.Rect("Zones", rt).Stretch();
            bv.panelLayer = UIKit.Rect("Panels", rt).Stretch();
            bv.cardLayer = UIKit.Rect("Cards", rt).Stretch();

            bv.myFieldBg = UIKit.Panel("MyField", bv.zoneLayer, new Color(1f, 1f, 1f, 0.035f));
            bv.myFieldBg.rectTransform.Place(MyFieldLeft - 10, 215, MyFieldRight - MyFieldLeft + 20, 340);
            bv.myFieldBg.raycastTarget = false;

            bv.myPiles = PilesView.Create(bv.panelLayer, gc);
            bv.myPiles.Rect.Place(1440, 40, 120, 500);

            bv.trayBg = UIKit.Panel("SelectionTray", bv.zoneLayer, new Color(0.05f, 0.06f, 0.1f, 0.92f));
            bv.trayBg.rectTransform.Place(300, 590, 1100, 300);
            bv.trayBg.raycastTarget = false;
            bv.trayTitle = UIKit.Label("Title", bv.trayBg.transform, "Choose", 20, Theme.Selectable, TextAnchor.UpperCenter, FontStyle.Bold);
            bv.trayTitle.rectTransform.Stretch(10, 0, 10, 8);
            bv.trayBg.gameObject.SetActive(false);
            return bv;
        }

        public void Render(GameState s)
        {
            var me = s.MePlayer;
            var opps = s.Opponents.ToList();
            var seen = new HashSet<int>();
            var placements = new Dictionary<int, Placement>();

            // ---------------------------------------------------------------- me
            if (me != null)
            {
                PanelFor(me, true).Rect.Place(20, 20, 240, 200);
                PanelFor(me, true).Render(me, s, true);
                myPiles.Render(me, s);

                LayoutHand(me.Hand, placements);
                LayoutField(me, s, MyFieldLeft, MyFieldRight, MyCreatureRowY, MyOtherRowY, MyCardH, placements, false);
                // Command zone sits above my portrait.
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

                float cardH = Mathf.Clamp(colW / 5.6f, 70, 150);
                float fieldTop = 1080 - OppHeaderH - 16;
                float otherY = fieldTop - cardH * 0.55f - 4;
                float creatureY = otherY - cardH - 18;
                float cmdW = cardH * 0.62f;
                LayoutRow(p.Command, x0 + 12, otherY - cardH * 0.45f, cmdW, cardH * 0.9f, placements, 0, false);
                LayoutField(p, s, x0 + 18 + cmdW + 8, x0 + colW - 14, creatureY, otherY, cardH, placements, true);
            }

            // ---------------------------------------------------------------- stack sources & selection tray
            // Cards Forge asks us to pick that are not drawn anywhere else (library, graveyard...) go into a tray.
            var trayCards = new List<CardData>();
            foreach (var c in s.Prompt.SelectableCardData)
                if (!placements.ContainsKey(c.Id)) trayCards.Add(c);
            trayBg.SetVisible(trayCards.Count > 0);
            if (trayCards.Count > 0)
            {
                trayTitle.text = s.Prompt.Message;
                float trayH = 200;
                float w = trayH * 0.714f;
                float spacing = Mathf.Min(w + 10, (1060 - w) / Mathf.Max(1, trayCards.Count - 1));
                float total = spacing * (trayCards.Count - 1) + w;
                float x = 850 - total / 2 + w / 2;
                for (int i = 0; i < trayCards.Count; i++)
                {
                    placements[trayCards[i].Id] = new Placement { Data = trayCards[i], Pos = new Vector2(x + i * spacing, 590 + 120), Size = new Vector2(w, trayH), Order = 5000 + i };
                }
            }

            // ---------------------------------------------------------------- apply
            foreach (var kv in placements)
            {
                seen.Add(kv.Key);
                var pl = kv.Value;
                if (!cards.TryGetValue(kv.Key, out var cv))
                {
                    cv = CardView.Create(cardLayer, kv.Key);
                    cv.Clicked = OnCardClicked;
                    cv.HoverStart = c => gc.Preview.Show(c.Data, c.Rect.anchoredPosition.x < 960);
                    cv.HoverEnd = c => gc.Preview.Hide();
                    // New cards fly in from their owner's library pile (or the board centre for opponents).
                    cv.TargetPos = SpawnPoint(pl.Data, s);
                    cv.TargetSize = pl.Size;
                    cv.SnapToTarget();
                    cards[kv.Key] = cv;
                }
                cv.SetData(pl.Data);
                cv.TargetPos = pl.Pos;
                cv.TargetSize = pl.Size;
                cv.TargetRotation = pl.Rotation;
                cv.HoverLift = pl.InHand;
                cv.SetHighlight(HighlightFor(pl.Data, s));
                cv.SetTag(TagFor(pl.Data, s, out var tagColor), tagColor);
            }

            // Draw order: sort by placement order so hands overlap correctly and auras sit behind their hosts.
            foreach (var kv in placements.OrderBy(p => p.Value.Order))
                cards[kv.Key].transform.SetAsLastSibling();

            foreach (var id in cards.Keys.ToList())
            {
                if (seen.Contains(id)) continue;
                Destroy(cards[id].gameObject);
                cards.Remove(id);
            }

            foreach (var id in panels.Keys.ToList())
            {
                if (s.Player(id) != null) continue;
                Destroy(panels[id].gameObject);
                panels.Remove(id);
            }
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

        Vector2 SpawnPoint(CardData c, GameState s)
        {
            if (c != null && c.Owner == s.Me) return new Vector2(1500, 470);
            return new Vector2(960, 900);
        }

        void LayoutHand(List<CardData> hand, Dictionary<int, Placement> outp)
        {
            int n = hand.Count;
            if (n == 0) return;
            float h = HandCardH, w = h * 0.714f;
            float maxWidth = MyFieldRight - MyFieldLeft;
            float spacing = n == 1 ? 0 : Mathf.Min(w * 0.82f, (maxWidth - w) / (n - 1));
            float total = spacing * (n - 1);
            float cx = (MyFieldLeft + MyFieldRight) / 2f;
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0 : (i / (float)(n - 1)) * 2f - 1f; // -1..1
                float x = cx - total / 2 + i * spacing;
                float y = HandY - Mathf.Abs(t) * Mathf.Abs(t) * 18f;   // gentle arc
                outp[hand[i].Id] = new Placement
                {
                    Data = hand[i], Pos = new Vector2(x, y), Size = new Vector2(w, h),
                    Rotation = -t * 6f, Order = 10000 + i, InHand = true,
                };
            }
        }

        /// <summary>Creatures in the front row, everything else behind; lands grouped on the right of that row.</summary>
        void LayoutField(PlayerData p, GameState s, float left, float right, float creatureY, float otherY, float cardH,
                         Dictionary<int, Placement> outp, bool isOpponent)
        {
            var onField = p.Battlefield;
            var hosts = new HashSet<int>(onField.Select(c => c.Id));
            var attachments = onField.Where(c => c.AttachedTo >= 0 && hosts.Contains(c.AttachedTo)).ToList();
            var free = onField.Where(c => !(c.AttachedTo >= 0 && hosts.Contains(c.AttachedTo))).ToList();

            var creatures = free.Where(c => c.IsCreature).ToList();
            var lands = free.Where(c => !c.IsCreature && c.IsLand).OrderBy(c => c.Name).ThenBy(c => c.Id).ToList();
            var others = free.Where(c => !c.IsCreature && !c.IsLand).ToList();

            float cardW = cardH * 0.714f;
            int baseOrder = isOpponent ? 100 : 2000;
            LayoutRow(creatures, left, creatureY - cardH / 2, right - left, cardH, outp, baseOrder + 200, true);

            // Back row: non-land permanents on the left, lands on the right, sharing the width by count.
            int total = Mathf.Max(1, lands.Count + others.Count);
            float split = others.Count == 0 ? left : lands.Count == 0 ? right : left + (right - left) * Mathf.Clamp(others.Count / (float)total, 0.25f, 0.6f);
            if (others.Count > 0) LayoutRow(others, left, otherY - cardH / 2, split - left - 10, cardH, outp, baseOrder, true);
            if (lands.Count > 0) LayoutRow(lands, split, otherY - cardH / 2, right - split, cardH, outp, baseOrder + 100, true);

            // Auras and equipment peek out from behind what they're attached to.
            var perHost = new Dictionary<int, int>();
            foreach (var a in attachments)
            {
                if (!outp.TryGetValue(a.AttachedTo, out var host)) continue;
                perHost.TryGetValue(a.AttachedTo, out int k);
                perHost[a.AttachedTo] = k + 1;
                outp[a.Id] = new Placement
                {
                    Data = a,
                    Pos = host.Pos + new Vector2(12 * (k + 1), 20 * (k + 1)),
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
            float slot = cardH * 0.82f; // tapped cards are wider
            float spacing = n == 1 ? 0 : Mathf.Min(slot + 6, (width - slot) / (n - 1));
            float total = spacing * (n - 1) + slot;
            float startX = rotateTapped ? x + (width - total) / 2 + slot / 2 : x + cardW / 2;
            for (int i = 0; i < n; i++)
            {
                var c = row[i];
                float lift = c.Attacking ? cardH * 0.12f : 0;
                outp[c.Id] = new Placement
                {
                    Data = c,
                    Pos = new Vector2(startX + i * spacing, y + cardH / 2 + lift),
                    Size = new Vector2(cardW, cardH),
                    Rotation = rotateTapped && c.Tapped ? -90 : 0,
                    Order = baseOrder + i,
                };
            }
        }

        Highlight HighlightFor(CardData c, GameState s)
        {
            if (c == null) return Highlight.None;
            var p = s.Prompt;
            if (p.ChosenCards.Contains(c.Id)) return Highlight.Chosen;
            if (p.SelectableCards.Contains(c.Id)) return p.SelMax == 0 ? Highlight.Playable : Highlight.Selectable;
            if (p.ActionableCards.Contains(c.Id)) return Highlight.Playable;
            if (c.Attacking) return Highlight.Attacking;
            if (s.IsBlocking(c.Id)) return Highlight.Blocking;
            return Highlight.None;
        }

        string TagFor(CardData c, GameState s, out Color color)
        {
            color = Theme.Attack;
            if (c == null || c.Zone != "Battlefield") return null;
            var cb = s.CombatFor(c.Id);
            if (cb != null)
            {
                string target = cb.DefenderKind == "player" ? s.Player(cb.DefenderId)?.ShortName : "planeswalker";
                if (cb.DefenderKind == "player" && cb.DefenderId == s.Me) target = "you";
                return "→ " + target + (cb.Blockers.Count > 0 ? " (blocked)" : "");
            }
            if (s.IsBlocking(c.Id)) { color = Theme.Block; return "Blocking"; }
            if (c.Sick && c.IsCreature && c.Controller == s.Me && s.ActivePlayer == s.Me) { color = new Color(0.25f, 0.25f, 0.3f, 0.9f); return "Summoning sick"; }
            return null;
        }

        void OnCardClicked(CardView cv)
        {
            gc.ClickCard(cv.CardId);
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
        }
    }
}

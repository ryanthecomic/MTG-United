using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// A player's nameplate: life total, poison, hand/library counts, commander damage and mana pool.
    /// Clicking it selects the player (targeting, choosing whom to attack).
    /// </summary>
    public class PlayerPanel : MonoBehaviour
    {
        GameController gc;
        bool isMe;
        int playerId;
        Image bg, ring;
        Text nameText, lifeText, infoText, cmdText;
        RawImage art;
        Text dots;
        float thinkingSince = -1f;
        Image badge;
        UnityEngine.UI.Outline badgeRim;
        Color playerColor = new Color(0.35f, 0.55f, 0.95f);
        string artFor;
        RectTransform manaRow;
        readonly List<Button> manaButtons = new List<Button>();
        int lastLife = int.MinValue;
        float lifePulse;
        readonly Dictionary<string, (RectTransform rt, Text count)> zones = new Dictionary<string, (RectTransform, Text)>();

        public RectTransform Rect => (RectTransform)transform;

        static readonly string[] ManaKeys = { "W", "U", "B", "R", "G", "C" };
        static readonly byte[] ManaBytes = { 1, 2, 4, 8, 16, 0 };

        public static PlayerPanel Create(Transform parent, GameController gc, bool isMe)
        {
            var ring = UIKit.Panel("PlayerPanel", parent, Color.clear);
            var p = ring.gameObject.AddComponent<PlayerPanel>();
            p.gc = gc;
            p.isMe = isMe;
            p.ring = ring;
            ring.raycastTarget = false;

            p.bg = UIKit.Panel("Bg", ring.transform, Theme.Panel);
            p.bg.rectTransform.Stretch(4, 4, 4, 4);
            p.bg.gameObject.AddComponent<Mask>().showMaskGraphic = true;   // clips the commander art to the rounded panel
            var pe = p.bg.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => { if (p.Clicked != null) p.Clicked(); else gc.ClickPlayer(p.playerId); };
            pe.OnRightClick = () => { if (p.RightClicked != null) p.RightClicked(); else gc.ZoneViewer.Open(p.playerId, "Command"); };   // commander(s) live behind a right-click
            pe.OnEnter = () => p.hovered = true;
            pe.OnExit = () => p.hovered = false;
            p.fadeGroup = ring.gameObject.AddComponent<CanvasGroup>();

            // Commander art, faint, behind everything else in the panel.
            var artRt = UIKit.Rect("Art", p.bg.transform).Stretch();
            p.art = artRt.gameObject.AddComponent<RawImage>();
            p.art.raycastTarget = false;
            p.art.enabled = false;
            var shade = UIKit.Rect("Shade", p.bg.transform).Stretch();
            var shadeImg = shade.gameObject.AddComponent<RawImage>();
            shadeImg.texture = HorizontalShade();
            shadeImg.raycastTarget = false;

            // Same layout for everyone: life badge on the left; name, then a 2x2 grid of zone symbols.
            float manaH = isMe ? 36f : 0f;
            p.badge = UIKit.Panel("LifeBadge", p.bg.transform, new Color(0f, 0f, 0f, 0.38f));
            p.badge.raycastTarget = false;
            p.badge.pixelsPerUnitMultiplier = 1.4f;
            var brt = p.badge.rectTransform;
            brt.anchorMin = new Vector2(0, 0); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 0.5f);
            brt.offsetMin = new Vector2(6, 6 + manaH); brt.offsetMax = new Vector2(6 + 128, -6);
            p.badgeRim = p.badge.gameObject.AddComponent<UnityEngine.UI.Outline>();
            p.badgeRim.effectDistance = new Vector2(2, -2);
            p.lifeText = UIKit.Label("Life", p.badge.transform, "40", 58, Theme.Life, TextAnchor.MiddleCenter, FontStyle.Bold).Outline(2);
            p.lifeText.rectTransform.Stretch();
            p.lifeText.resizeTextForBestFit = true; p.lifeText.resizeTextMinSize = 24; p.lifeText.resizeTextMaxSize = 60;

            const float left = 146;
            p.nameText = UIKit.Label("Name", p.bg.transform, "", 21, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.2f);
            var nr = p.nameText.rectTransform;
            nr.anchorMin = new Vector2(0, 1); nr.anchorMax = new Vector2(1, 1); nr.pivot = new Vector2(0, 1);
            nr.offsetMin = new Vector2(left, -34); nr.offsetMax = new Vector2(-10, -4);
            p.nameText.resizeTextForBestFit = true; p.nameText.resizeTextMinSize = 12; p.nameText.resizeTextMaxSize = 21;

            var zr = UIKit.Rect("Zones", p.bg.transform);
            zr.anchorMin = zr.anchorMax = new Vector2(0, 1); zr.pivot = new Vector2(0, 1);
            zr.sizeDelta = new Vector2(170, 70); zr.anchoredPosition = new Vector2(left - 4, -38);
            p.BuildZones(zr);

            // "Thinking" dots for opponents holding priority (after 1.5 s of decision time).
            p.dots = UIKit.Label("Thinking", p.bg.transform, "", 30, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            var dr = p.dots.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(1, 1); dr.pivot = new Vector2(1, 1);
            dr.sizeDelta = new Vector2(70, 30); dr.anchoredPosition = new Vector2(-8, -6);
            p.dots.supportRichText = true;

            // Poison, commander damage: a small line beside the grid.
            p.infoText = UIKit.Label("Info", p.bg.transform, "", 13, Theme.TextDim, TextAnchor.UpperLeft);
            var ir = p.infoText.rectTransform;
            ir.anchorMin = new Vector2(0, 1); ir.anchorMax = new Vector2(1, 1); ir.pivot = new Vector2(0, 1);
            ir.offsetMin = new Vector2(left + 172, -110); ir.offsetMax = new Vector2(-8, -40);
            p.infoText.supportRichText = true;
            p.infoText.verticalOverflow = VerticalWrapMode.Overflow;

            if (isMe)
            {
                p.manaRow = UIKit.Rect("Mana", p.bg.transform);
                p.manaRow.anchorMin = new Vector2(0, 0); p.manaRow.anchorMax = new Vector2(1, 0); p.manaRow.pivot = new Vector2(0.5f, 0);
                p.manaRow.sizeDelta = new Vector2(-12, manaH - 6); p.manaRow.anchoredPosition = new Vector2(0, 5);
            }
            return p;
        }

        public void Render(PlayerData pd, GameState s, bool me)
        {
            playerId = pd.Id;
            nameText.text = (pd.Id == s.ActivePlayer ? "▶ " : "") + pd.ShortName + (pd.HasLost ? "  (out)" : "");
            if (pd.Life != lastLife)
            {
                if (lastLife != int.MinValue)
                {
                    // Life change (section 5.4): the number rolls to the new value; damage flashes red and
                    // shakes, gain glows gold. All within the 400 ms budget.
                    lifeRollFrom = displayedLife;
                    lifeRollStart = Motion.Now;
                    bool loss = pd.Life < lastLife;
                    lifeFlashColor = loss ? Theme.Danger : new Color(1f, 0.82f, 0.35f);
                    lifeFlashStart = Motion.Now;
                    if (loss) Impact();
                }
                else displayedLife = pd.Life;
                lastLife = pd.Life;
            }
            lowLife = pd.Life <= 5 && !pd.HasLost;

            var sb = new StringBuilder();
            SetZone("Hand", pd.HandSize);
            SetZone("Library", pd.LibrarySize);
            SetZone("Graveyard", pd.GraveyardSize);
            SetZone("Exile", pd.ExileSize);
            if (pd.Poison > 0) sb.Append("<color=#9be37a>Poison ").Append(pd.Poison).Append("</color>");
            if (!me)
            {
                // Commander damage this opponent has taken from my commander(s).
                foreach (var cd in pd.CommanderDamage)
                    if (cd.FromPlayer == s.Me) sb.Append(sb.Length > 0 ? "\n" : "").Append("<color=#ffb380>Cmdr ").Append(cd.Damage).Append("</color>");
            }
            infoText.text = sb.ToString();

            if (me)
            {
                // Commander damage I've taken, the number that actually kills you at 21.
                foreach (var cd in pd.CommanderDamage)
                    if (cd.Damage > 0) sb.Append(sb.Length > 0 ? "\n" : "").Append("<color=#ffb380>").Append(cd.CommanderName).Append(" ").Append(cd.Damage).Append("</color>");
                infoText.text = sb.ToString();
                if (manaRow != null) RenderMana(pd);
            }
            ShowArt(pd);

            bool thinking = !me && pd.HasPriority && !s.GameOver;
            if (thinking && thinkingSince < 0f) thinkingSince = Motion.Now;
            if (!thinking) thinkingSince = -1f;

            bool targetable = s.Prompt.TargetPlayers.Contains(pd.Id);
            bool chosen = s.Prompt.ChosenPlayers.Contains(pd.Id);
            ringState = chosen ? 2 : targetable ? 1 : pd.HasPriority ? 3 : 0;
            var tint = Color.Lerp(new Color(0.07f, 0.08f, 0.12f), playerColor, 0.22f);
            tint.a = 0.94f;
            currentBg = pd.HasLost ? new Color(0.2f, 0.08f, 0.08f, 0.9f) : tint;
        }

        /// <summary>
        /// Hand and library on the first row, graveyard and exile (clickable, in pills) on the second. Each symbol
        /// pops up a little on hover so it's an easy target.
        /// </summary>
        void BuildZones(RectTransform grid)
        {
            string[] ids = { "Hand", "Library", "Graveyard", "Exile" };
            Sprite[] icons = { Icons.Hand, Icons.Library, Icons.Grave, Icons.Spiral };
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                bool viewer = id == "Graveyard" || id == "Exile";
                var cell = UIKit.Rect(id, grid);
                cell.anchorMin = cell.anchorMax = new Vector2(0, 1); cell.pivot = new Vector2(0.5f, 0.5f);
                cell.sizeDelta = new Vector2(76, 30);
                cell.anchoredPosition = new Vector2(38 + (i % 2) * 86, -15 - (i / 2) * 36);
                var hit = cell.gameObject.AddComponent<Image>();
                hit.sprite = UIKit.Rounded; hit.type = Image.Type.Sliced; hit.pixelsPerUnitMultiplier = 32f / 30f;
                hit.color = new Color(1, 1, 1, viewer ? 0.09f : 0f);
                var pe = cell.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = () =>
                {
                    if (ZoneClicked != null) { Sound.Play(Sfx.UiClick, 0.6f); ZoneClicked(id); return; }
                    if (viewer) { Sound.Play(Sfx.UiClick, 0.6f); gc.ZoneViewer.Open(playerId, id); }
                    else gc.ClickPlayer(playerId);
                };
                pe.OnRightClick = () => { if (RightClicked != null) RightClicked(); else gc.ZoneViewer.Open(playerId, "Command"); };
                var pop = cell.gameObject.AddComponent<HoverPop>();
                pe.OnEnter = () => pop.Hovered = true;
                pe.OnExit = () => pop.Hovered = false;

                var ic = UIKit.Rect("Icon", cell);
                ic.anchorMin = ic.anchorMax = new Vector2(0, 0.5f); ic.pivot = new Vector2(0, 0.5f);
                ic.sizeDelta = new Vector2(22, 22); ic.anchoredPosition = new Vector2(6, 0);
                var img = ic.gameObject.AddComponent<Image>();
                img.sprite = icons[i]; img.color = new Color(0.88f, 0.9f, 0.95f); img.raycastTarget = false; img.preserveAspect = true;
                var t = UIKit.Label("Count", cell, "0", 17, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1f);
                t.rectTransform.Stretch(34, 0, 2, 0);
                zones[id] = (cell, t);
            }
        }

        // Hooks for the kitchen table (no Forge): when set, they replace the Forge actions.
        public System.Action Clicked, RightClicked;
        public System.Action<string> ZoneClicked;

        /// <summary>Kitchen table: small − / + buttons under the life total.</summary>
        public void AddLifeButtons(System.Action<int> delta)
        {
            lifeText.rectTransform.Stretch(0, 30, 0, 0);
            var minus = UIKit.Button("Minus", badge.transform, "−", new Color(0.45f, 0.15f, 0.15f, 0.9f), () => delta(-1), 20);
            var mr = (RectTransform)minus.transform;
            mr.anchorMin = new Vector2(0, 0); mr.anchorMax = new Vector2(0.5f, 0); mr.pivot = new Vector2(0.5f, 0);
            mr.offsetMin = new Vector2(4, 4); mr.offsetMax = new Vector2(-2, 30);
            var plus = UIKit.Button("Plus", badge.transform, "+", new Color(0.15f, 0.4f, 0.2f, 0.9f), () => delta(1), 20);
            var pr = (RectTransform)plus.transform;
            pr.anchorMin = new Vector2(0.5f, 0); pr.anchorMax = new Vector2(1, 0); pr.pivot = new Vector2(0.5f, 0);
            pr.offsetMin = new Vector2(2, 4); pr.offsetMax = new Vector2(-4, 30);
        }

        /// <summary>The player's seat colour: tints the panel, the life badge rim and the name.</summary>
        public void SetColor(Color c)
        {
            playerColor = c;
            badgeRim.effectColor = new Color(c.r, c.g, c.b, 0.85f);
            nameText.color = Color.Lerp(c, Color.white, 0.55f);
        }

        /// <summary>The commander's art, faint, behind the panel (cropped to the art box of the card image).</summary>
        void ShowArt(PlayerData pd)
        {
            string cmd = pd.Commanders.Count > 0 ? pd.Commanders[0].Name : (pd.Command.Count > 0 ? pd.Command[0].Name : null);
            if (cmd == artFor || string.IsNullOrEmpty(cmd) || CardImageCache.Instance == null) return;
            artFor = cmd;
            CardImageCache.Instance.Get(cmd, null, tex =>
            {
                if (art == null || artFor != cmd) return;
                art.texture = tex;
                // Art box of a card image is roughly x 8-92%, y 45-89%; crop it to the panel's shape.
                var r = ((RectTransform)art.transform).rect;
                float aspect = r.height > 0 ? r.width / r.height : 3f;
                float uvW = 0.84f, uvH = Mathf.Min(0.44f, uvW * tex.width / tex.height / aspect);
                art.uvRect = new Rect(0.08f, 0.67f - uvH / 2f, uvW, uvH);
                art.color = new Color(1, 1, 1, 0.28f);
                art.enabled = true;
            });
        }

        static Texture2D shadeTex;
        /// <summary>Darker on the left (behind the life total), lighter on the right, so the art shows where text is sparse.</summary>
        static Texture2D HorizontalShade()
        {
            if (shadeTex != null) return shadeTex;
            shadeTex = new Texture2D(64, 1);
            for (int x = 0; x < 64; x++) shadeTex.SetPixel(x, 0, new Color(0.02f, 0.03f, 0.06f, Mathf.Lerp(0.55f, 0.15f, x / 63f)));
            shadeTex.Apply();
            shadeTex.wrapMode = TextureWrapMode.Clamp;
            return shadeTex;
        }

        void SetZone(string id, int n)
        {
            if (zones.TryGetValue(id, out var z)) z.count.text = n.ToString();
        }

        /// <summary>Where a zone symbol sits, for cards flying to or from it.</summary>
        public RectTransform ZoneRect(string id) => id == "Mana" ? (manaRow != null ? manaRow : null) : zones.TryGetValue(id, out var z) ? z.rt : null;

        // Smooth panel moves (board focus slides the nameplates along with the cards).
        Vector4 target, current;
        bool placed;

        /// <summary>Faded nameplate (focused board): mostly transparent until the pointer is over it.</summary>
        public bool Faded;
        bool hovered;
        CanvasGroup fadeGroup;

        public void MoveTo(float x, float y, float w, float h, bool instant = false)
        {
            target = new Vector4(x, y, w, h);
            if (!placed || instant) { current = target; placed = true; Rect.Place(x, y, w, h); }
        }

        void LateUpdateMove()
        {
            if (!placed || current == target) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / (Motion.Base / 4f));
            current = Vector4.Lerp(current, target, k);
            if ((current - target).sqrMagnitude < 0.25f) current = target;
            Rect.Place(current.x, current.y, current.z, current.w);
        }

        void RenderMana(PlayerData pd)
        {
            if (manaButtons.Count == 0)
            {
                for (int i = 0; i < ManaKeys.Length; i++)
                {
                    int idx = i;
                    var b = UIKit.Button("Mana" + ManaKeys[i], manaRow, "", Theme.ForManaColor(ManaKeys[i]),
                        () => gc.Send("mana", "color", (int)ManaBytes[idx]), 14);
                    var r = (RectTransform)b.transform;
                    r.anchorMin = new Vector2(i / 6f, 0); r.anchorMax = new Vector2((i + 1) / 6f, 1);
                    r.offsetMin = new Vector2(2, 0); r.offsetMax = new Vector2(-2, 0);
                    UIKit.LabelOf(b).color = Color.black;
                    manaButtons.Add(b);
                }
            }
            for (int i = 0; i < ManaKeys.Length; i++)
            {
                pd.Mana.TryGetValue(ManaKeys[i], out int n);
                manaButtons[i].SetVisible(n > 0);
                UIKit.LabelOf(manaButtons[i]).text = ManaKeys[i] + n;
            }
        }

        // Life roll / flash / shake state.
        float displayedLife, lifeRollFrom, lifeRollStart = -10f, lifeFlashStart = -10f, shakeStart = -10f;
        Color lifeFlashColor;
        bool lowLife;
        Vector2 restPos; bool restKnown;

        /// <summary>Player hit (combat impact or life loss): the nameplate shakes briefly.</summary>
        public void Impact()
        {
            shakeStart = Motion.Now;
        }

        void Update()
        {
            LateUpdateMove();
            float a = Faded && !hovered ? 0.3f : 1f;
            if (fadeGroup != null && Mathf.Abs(fadeGroup.alpha - a) > 0.001f)
                fadeGroup.alpha = Mathf.MoveTowards(fadeGroup.alpha, a, Time.unscaledDeltaTime / Motion.Short);
            if (dots != null)
            {
                float since = thinkingSince < 0f ? 0f : Motion.Now - thinkingSince;
                if (since < 1.5f) { if (dots.text.Length > 0) dots.text = ""; }
                else
                {
                    // Three dots that light up in turn.
                    int lit = (int)((Motion.Now * 3f) % 3);
                    var sb = new StringBuilder();
                    for (int i = 0; i < 3; i++) sb.Append(i == lit ? "<color=#ffffff>•</color>" : "<color=#ffffff55>•</color>");
                    dots.text = sb.ToString();
                }
            }
            float now = Motion.Now;

            // Number rolls to its new value.
            float rollT = Mathf.Clamp01((now - lifeRollStart) / Motion.Travel);
            displayedLife = Mathf.Lerp(lifeRollFrom, lastLife, Easing.Eval(Ease.Out, rollT));
            if (rollT >= 1f) displayedLife = lastLife;
            if (lastLife != int.MinValue) lifeText.text = Mathf.RoundToInt(displayedLife).ToString();

            // Flash: pop and tint, fading over the budget.
            float ft = Mathf.Clamp01((now - lifeFlashStart) / Motion.Budget);
            float flashAmt = 1f - Easing.Eval(Ease.Out, ft);
            lifeText.transform.localScale = Vector3.one * (1f + 0.2f * flashAmt);
            Color baseColor = Theme.Life;
            if (lowLife)
            {
                // At 5 life or less the total gains a slow red heartbeat glow.
                float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(now * Mathf.PI * 1.2f)), 6f);
                baseColor = Color.Lerp(Theme.Life, Theme.Danger, 0.35f + 0.5f * beat);
            }
            lifeText.color = Color.Lerp(baseColor, lifeFlashColor, flashAmt);
            bg.color = Color.Lerp(currentBg, new Color(lifeFlashColor.r * 0.5f, lifeFlashColor.g * 0.2f, lifeFlashColor.b * 0.2f, 0.95f),
                flashAmt * (lifeFlashColor.g < 0.5f ? 0.6f : 0f));

            // Shake (impact duration), amplitude decays.
            var r = (RectTransform)bg.transform;
            float st = (now - shakeStart) / Motion.Impact;
            if (st >= 0f && st < 1f)
            {
                float k = 1f - st;
                r.anchoredPosition = new Vector2(Mathf.Sin(now * 95f), Mathf.Cos(now * 81f)) * 6f * k;
            }
            else r.anchoredPosition = Vector2.zero;

            // Same highlight language as cards: eligible = pulsing amber (1.2 s), selected = gold.
            switch (ringState)
            {
                case 2: ring.color = new Color(1f, 0.835f, 0.29f); break;
                case 1:
                    var c = new Color(0.96f, 0.65f, 0.14f);
                    c.a = Mathf.Lerp(0.4f, 1f, 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / 1.2f));
                    ring.color = c; break;
                case 3: ring.color = new Color(1f, 1f, 1f, 0.25f); break;
                default: ring.color = Color.clear; break;
            }
        }

        int ringState;

        Color currentBg = Theme.Panel;
    }

    /// <summary>My library, graveyard and exile piles on the right of my battlefield.</summary>
    public class PilesView : MonoBehaviour
    {
        GameController gc;
        Text libText, gyText, exText;
        RawImage gyTop, exTop;
        PlayerData player;

        public RectTransform Rect => (RectTransform)transform;

        public static PilesView Create(Transform parent, GameController gc)
        {
            var rt = UIKit.Rect("Piles", parent);
            var v = rt.gameObject.AddComponent<PilesView>();
            v.gc = gc;
            v.libText = v.Pile(rt, "Library", 340, out var libTop, null);
            if (GameArt.CardBack != null)
            {
                libTop.texture = GameArt.CardBack;
                libTop.color = Color.white;
                libTop.enabled = true;
            }
            v.gyText = v.Pile(rt, "Graveyard", 170, out v.gyTop, () => { if (v.player != null) gc.ZoneViewer.Open(v.player.Id, "Graveyard"); });
            v.exText = v.Pile(rt, "Exile", 0, out v.exTop, () => { if (v.player != null) gc.ZoneViewer.Open(v.player.Id, "Exile"); });
            return v;
        }

        Text Pile(RectTransform parent, string label, float y, out RawImage top, System.Action onClick)
        {
            var bg = UIKit.Panel(label, parent, Theme.PanelLight);
            bg.rectTransform.Place(0, y, 110, 155);
            var topRt = UIKit.Rect("Top", bg.transform).Stretch(4, 24, 4, 4);
            top = topRt.gameObject.AddComponent<RawImage>();
            top.color = new Color(1, 1, 1, 0.55f);
            top.enabled = false;
            top.raycastTarget = false;
            var t = UIKit.Label("Label", bg.transform, label, 15, Theme.Text, TextAnchor.LowerCenter, FontStyle.Bold).Outline();
            t.rectTransform.Stretch(2, 4, 2, 2);
            if (onClick != null)
            {
                var pe = bg.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = onClick;
            }
            return t;
        }

        public void Render(PlayerData p, GameState s)
        {
            player = p;
            libText.text = "Library\n" + p.LibrarySize;
            gyText.text = "Graveyard\n" + p.GraveyardSize;
            exText.text = "Exile\n" + p.ExileSize;
            ShowTop(gyTop, p.Graveyard);
            ShowTop(exTop, p.Exile);
        }

        void ShowTop(RawImage img, List<CardData> zone)
        {
            if (zone.Count == 0 || CardImageCache.Instance == null) { img.enabled = false; return; }
            var top = zone[zone.Count - 1];
            if (top.Hidden) { img.enabled = false; return; }
            CardImageCache.Instance.Get(top.Name, top.Set, tex => { if (img != null) { img.texture = tex; img.enabled = true; } });
        }
    }
}

namespace MTGUnited
{
    /// <summary>Grows an element a little while hovered (eased, well under the 400 ms budget).</summary>
    public class HoverPop : MonoBehaviour
    {
        public bool Hovered;
        public float Scale = 1.22f;
        float s = 1f;

        void Update()
        {
            float target = Hovered ? Scale : 1f;
            if (Mathf.Abs(s - target) < 0.001f) return;
            s = Mathf.Lerp(s, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.04f));
            transform.localScale = new Vector3(s, s, 1f);
            if (Hovered) transform.SetAsLastSibling();
        }
    }
}

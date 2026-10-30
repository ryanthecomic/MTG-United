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
        RectTransform manaRow;
        readonly List<Button> manaButtons = new List<Button>();
        int lastLife = int.MinValue;
        Button gyButton, exButton;
        float lifePulse;

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
            var pe = p.bg.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => gc.ClickPlayer(p.playerId);

            if (isMe)
            {
                p.lifeText = UIKit.Label("Life", p.bg.transform, "40", 64, Theme.Life, TextAnchor.MiddleCenter, FontStyle.Bold).Outline(2);
                p.lifeText.rectTransform.anchorMin = new Vector2(0, 0.38f); p.lifeText.rectTransform.anchorMax = new Vector2(1, 0.88f);
                p.lifeText.rectTransform.offsetMin = p.lifeText.rectTransform.offsetMax = Vector2.zero;
                p.nameText = UIKit.Label("Name", p.bg.transform, "", 18, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
                p.nameText.rectTransform.Stretch(8, 0, 8, 6);
                p.infoText = UIKit.Label("Info", p.bg.transform, "", 14, Theme.TextDim, TextAnchor.MiddleCenter);
                p.infoText.rectTransform.anchorMin = new Vector2(0, 0.24f); p.infoText.rectTransform.anchorMax = new Vector2(1, 0.4f);
                p.infoText.rectTransform.offsetMin = p.infoText.rectTransform.offsetMax = Vector2.zero;
                p.cmdText = UIKit.Label("Cmd", p.bg.transform, "", 13, new Color(1f, 0.7f, 0.5f), TextAnchor.MiddleCenter);
                p.cmdText.rectTransform.anchorMin = new Vector2(0, 0.12f); p.cmdText.rectTransform.anchorMax = new Vector2(1, 0.25f);
                p.cmdText.rectTransform.offsetMin = p.cmdText.rectTransform.offsetMax = Vector2.zero;
                p.manaRow = UIKit.Rect("Mana", p.bg.transform);
                p.manaRow.anchorMin = new Vector2(0, 0); p.manaRow.anchorMax = new Vector2(1, 0.13f);
                p.manaRow.offsetMin = new Vector2(8, 4); p.manaRow.offsetMax = new Vector2(-8, 0);
            }
            else
            {
                p.lifeText = UIKit.Label("Life", p.bg.transform, "40", 44, Theme.Life, TextAnchor.MiddleRight, FontStyle.Bold).Outline(2);
                p.lifeText.rectTransform.anchorMin = new Vector2(0.72f, 0); p.lifeText.rectTransform.anchorMax = new Vector2(1, 1);
                p.lifeText.rectTransform.offsetMin = new Vector2(0, 0); p.lifeText.rectTransform.offsetMax = new Vector2(-14, 0);
                p.nameText = UIKit.Label("Name", p.bg.transform, "", 20, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                p.nameText.rectTransform.anchorMin = new Vector2(0, 0.5f); p.nameText.rectTransform.anchorMax = new Vector2(0.72f, 1);
                p.nameText.rectTransform.offsetMin = new Vector2(14, 0); p.nameText.rectTransform.offsetMax = new Vector2(0, -6);
                p.nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
                p.infoText = UIKit.Label("Info", p.bg.transform, "", 14, Theme.TextDim, TextAnchor.LowerLeft);
                p.infoText.rectTransform.anchorMin = new Vector2(0, 0); p.infoText.rectTransform.anchorMax = new Vector2(0.72f, 0.5f);
                p.infoText.rectTransform.offsetMin = new Vector2(14, 8); p.infoText.rectTransform.offsetMax = new Vector2(-150, 0);
                p.infoText.supportRichText = true;
                p.infoText.resizeTextForBestFit = true; p.infoText.resizeTextMinSize = 9; p.infoText.resizeTextMaxSize = 14;

                // Graveyard / exile viewers for this opponent (to the left of the life total).
                p.gyButton = UIKit.Button("Graveyard", p.bg.transform, "GY", Theme.Secondary, () => gc.ZoneViewer.Open(p.playerId, "Graveyard"), 13);
                var gr = (RectTransform)p.gyButton.transform;
                gr.anchorMin = gr.anchorMax = new Vector2(0.72f, 0); gr.pivot = new Vector2(1, 0);
                gr.sizeDelta = new Vector2(70, 28); gr.anchoredPosition = new Vector2(-78, 8);
                p.exButton = UIKit.Button("Exile", p.bg.transform, "EX", Theme.Secondary, () => gc.ZoneViewer.Open(p.playerId, "Exile"), 13);
                var er = (RectTransform)p.exButton.transform;
                er.anchorMin = er.anchorMax = new Vector2(0.72f, 0); er.pivot = new Vector2(1, 0);
                er.sizeDelta = new Vector2(70, 28); er.anchoredPosition = new Vector2(-4, 8);
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
            sb.Append("Hand ").Append(pd.HandSize).Append("  ·  Library ").Append(pd.LibrarySize);
            if (pd.Poison > 0) sb.Append("  ·  <color=#9be37a>Poison ").Append(pd.Poison).Append("</color>");
            if (!me)
            {
                UIKit.LabelOf(gyButton).text = "GY " + pd.GraveyardSize;
                UIKit.LabelOf(exButton).text = "EX " + pd.ExileSize;
                // Commander damage this opponent has taken from my commander(s).
                foreach (var cd in pd.CommanderDamage)
                    if (cd.FromPlayer == s.Me) sb.Append("  ·  <color=#ffb380>Cmdr ").Append(cd.Damage).Append("</color>");
            }
            infoText.text = sb.ToString();

            if (me && cmdText != null)
            {
                // Commander damage I've taken, the number that actually kills you at 21.
                var parts = new List<string>();
                foreach (var cd in pd.CommanderDamage) parts.Add(cd.CommanderName + " " + cd.Damage);
                cmdText.text = parts.Count > 0 ? "Cmdr dmg: " + string.Join(", ", parts) : "";
                RenderMana(pd);
            }

            bool targetable = s.Prompt.TargetPlayers.Contains(pd.Id);
            bool chosen = s.Prompt.ChosenPlayers.Contains(pd.Id);
            ringState = chosen ? 2 : targetable ? 1 : pd.HasPriority ? 3 : 0;
            currentBg = pd.HasLost ? new Color(0.2f, 0.08f, 0.08f, 0.9f) : Theme.Panel;
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

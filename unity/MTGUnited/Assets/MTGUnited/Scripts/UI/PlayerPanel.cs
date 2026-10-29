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
                p.infoText.rectTransform.offsetMin = new Vector2(14, 8); p.infoText.rectTransform.offsetMax = new Vector2(0, 0);
                p.infoText.supportRichText = true;
            }
            return p;
        }

        public void Render(PlayerData pd, GameState s, bool me)
        {
            playerId = pd.Id;
            nameText.text = (pd.Id == s.ActivePlayer ? "▶ " : "") + pd.ShortName + (pd.HasLost ? "  (out)" : "");
            if (pd.Life != lastLife)
            {
                if (lastLife != int.MinValue) lifePulse = 1f;
                lastLife = pd.Life;
            }
            lifeText.text = pd.Life.ToString();

            var sb = new StringBuilder();
            sb.Append("Hand ").Append(pd.HandSize).Append("  ·  Library ").Append(pd.LibrarySize);
            if (pd.Poison > 0) sb.Append("  ·  <color=#9be37a>Poison ").Append(pd.Poison).Append("</color>");
            if (!me)
            {
                sb.Append("  ·  GY ").Append(pd.GraveyardSize);
                if (pd.ExileSize > 0) sb.Append("  ·  Exile ").Append(pd.ExileSize);
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
            ring.color = chosen ? Theme.Chosen : targetable ? Theme.Selectable : pd.HasPriority ? new Color(1f, 1f, 1f, 0.25f) : Color.clear;
            bg.color = pd.HasLost ? new Color(0.2f, 0.08f, 0.08f, 0.9f) : Theme.Panel;
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

        void Update()
        {
            if (lifePulse > 0)
            {
                lifePulse = Mathf.Max(0, lifePulse - Time.unscaledDeltaTime * 1.5f);
                lifeText.transform.localScale = Vector3.one * (1f + 0.25f * lifePulse);
                lifeText.color = Color.Lerp(Theme.Life, Theme.Danger, lifePulse);
            }
        }
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
            v.libText = v.Pile(rt, "Library", 340, out _, null);
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

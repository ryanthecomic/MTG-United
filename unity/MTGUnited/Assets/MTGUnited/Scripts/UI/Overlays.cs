using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Large card shown while hovering any card, on the side of the screen away from the pointer.</summary>
    public class CardPreview : MonoBehaviour
    {
        RectTransform rt;
        RawImage art;
        Image frame;
        Text text;
        CardData showing;

        public static CardPreview Create(RectTransform board)
        {
            var frame = UIKit.Panel("CardPreview", board, new Color(0.06f, 0.06f, 0.08f, 0.97f));
            var p = frame.gameObject.AddComponent<CardPreview>();
            p.rt = frame.rectTransform;
            p.frame = frame;
            frame.raycastTarget = false;
            p.text = UIKit.Label("Text", frame.transform, "", 17, Theme.Text, TextAnchor.UpperLeft);
            p.text.rectTransform.Stretch(16, 16, 16, 16);
            p.text.supportRichText = true;
            var artRt = UIKit.Rect("Art", frame.transform).Stretch(4, 4, 4, 4);
            p.art = artRt.gameObject.AddComponent<RawImage>();
            p.art.raycastTarget = false;
            frame.gameObject.SetActive(false);
            return p;
        }

        public void Show(CardData c, bool onRight)
        {
            if (c == null || c.Hidden) { Hide(); return; }
            showing = c;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            rt.Place(onRight ? 1590 - 360 : 290, 250, 340, 476);
            art.enabled = false;
            text.text = "<b>" + c.Name + "</b>   " + CardView.ManaText(c.ManaCost) + "\n<i>" + c.TypeLine + "</i>\n\n" + c.Text
                        + (c.HasPT ? "\n\n<b>" + c.Power + "/" + c.Toughness + "</b>" : "");
            if (!c.Token && CardImageCache.Instance != null)
            {
                var target = c;
                CardImageCache.Instance.Get(c.Name, c.Set, tex =>
                {
                    if (showing != target) return;
                    art.texture = tex;
                    art.enabled = true;
                });
            }
        }

        public void Hide()
        {
            showing = null;
            gameObject.SetActive(false);
        }
    }

    /// <summary>Short-lived notifications at the top of the screen.</summary>
    public class Toasts : MonoBehaviour
    {
        class Toast { public Image bg; public float life; }
        readonly List<Toast> toasts = new List<Toast>();
        RectTransform rt;

        public static Toasts Create(RectTransform parent)
        {
            var rt = UIKit.Rect("Toasts", parent).Stretch();
            var t = rt.gameObject.AddComponent<Toasts>();
            t.rt = rt;
            return t;
        }

        public void Show(string message, bool error = false)
        {
            if (string.IsNullOrEmpty(message)) return;
            Debug.Log("[MTGUnited] " + message);
            var bg = UIKit.Panel("Toast", rt, error ? new Color(0.45f, 0.1f, 0.1f, 0.95f) : new Color(0.1f, 0.13f, 0.2f, 0.95f));
            bg.raycastTarget = false;
            var r = bg.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(760, 64);
            var t = UIKit.Label("Text", bg.transform, message, 18, Theme.Text, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch(14, 4, 14, 4);
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 18;
            toasts.Add(new Toast { bg = bg, life = error ? 8f : 4.5f });
            transform.SetAsLastSibling();
        }

        void Update()
        {
            float y = -20;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                t.life -= Time.unscaledDeltaTime;
                if (t.life <= 0) { Destroy(t.bg.gameObject); toasts.RemoveAt(i); continue; }
                var c = t.bg.color; c.a = Mathf.Clamp01(t.life * 2f) * 0.95f; t.bg.color = c;
            }
            foreach (var t in toasts)
            {
                var r = t.bg.rectTransform;
                r.anchoredPosition = Vector2.Lerp(r.anchoredPosition, new Vector2(0, y), 0.25f);
                y -= 72;
            }
        }
    }

    /// <summary>Simple card tile (image with a text fallback) for dialogs and zone viewers.</summary>
    public static class CardTile
    {
        public static Image Create(Transform parent, CardData c, float w, float h, System.Action onClick, System.Action<CardData> onHover = null)
        {
            var frame = UIKit.Panel("Tile", parent, Theme.FrameFor(c));
            frame.rectTransform.sizeDelta = new Vector2(w, h);
            var label = UIKit.Label("Name", frame.transform, c == null ? "?" : c.Name + "\n\n<size=11>" + c.TypeLine + "</size>", 14, Color.black, TextAnchor.UpperCenter, FontStyle.Bold);
            label.rectTransform.Stretch(6, 6, 6, 6);
            var artRt = UIKit.Rect("Art", frame.transform).Stretch(2, 2, 2, 2);
            var art = artRt.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.enabled = false;
            if (c != null && !c.Hidden && !c.Token && CardImageCache.Instance != null)
                CardImageCache.Instance.Get(c.Name, c.Set, tex => { if (art != null) { art.texture = tex; art.enabled = true; } });
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => onClick?.Invoke();
            if (onHover != null)
            {
                pe.OnEnter = () => onHover(c);
                pe.OnExit = () => onHover(null);
            }
            return frame;
        }
    }

    /// <summary>Browse a graveyard or exile. Cards can be clicked (e.g. to cast with flashback).</summary>
    public class ZoneViewer : MonoBehaviour
    {
        GameController gc;
        Text title;
        RectTransform content;
        int playerId = -1;
        string zone;

        public static ZoneViewer Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("ZoneViewer", parent, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.Stretch();
            var v = shade.gameObject.AddComponent<ZoneViewer>();
            v.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = v.Close;
            var panel = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            var pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(1400, 640);
            panel.gameObject.AddComponent<PointerEvents>(); // swallow clicks so they don't close the viewer
            v.title = UIKit.Label("Title", panel.transform, "", 26, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            v.title.rectTransform.Stretch(0, 0, 0, 14);
            var scroll = UIKit.Rect("Scroll", panel.transform).Stretch(20, 70, 20, 60);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            v.content = UIKit.Rect("Content", scroll);
            v.content.anchorMin = new Vector2(0, 1); v.content.anchorMax = new Vector2(1, 1); v.content.pivot = new Vector2(0.5f, 1);
            var grid = v.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160, 224);
            grid.spacing = new Vector2(12, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            v.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = v.content;
            sr.horizontal = false;
            sr.scrollSensitivity = 40;
            var close = UIKit.Button("Close", panel.transform, "Close", Theme.Secondary, v.Close, 20);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0); cr.sizeDelta = new Vector2(200, 46); cr.anchoredPosition = new Vector2(0, 36);
            shade.gameObject.SetActive(false);
            return v;
        }

        public void Open(int player, string zoneName)
        {
            playerId = player;
            zone = zoneName;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh(gc.State);
        }

        public void Close()
        {
            playerId = -1;
            gc.Preview.Hide();
            gameObject.SetActive(false);
        }

        public void Refresh(GameState s)
        {
            if (!gameObject.activeSelf || s == null || playerId < 0) return;
            var p = s.Player(playerId);
            if (p == null) { Close(); return; }
            var list = zone == "Exile" ? p.Exile : p.Graveyard;
            title.text = p.ShortName + " — " + zone + " (" + list.Count + ")";
            foreach (Transform child in content) Destroy(child.gameObject);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var c = list[i];
                var tile = CardTile.Create(content, c, 160, 224, () => gc.ClickCard(c.Id), cd => { if (cd != null) gc.Preview.Show(cd, false); else gc.Preview.Hide(); });
                bool playable = s.Prompt.ActionableCards.Contains(c.Id) || s.Prompt.SelectableCards.Contains(c.Id);
                if (playable)
                {
                    var o = tile.gameObject.AddComponent<UnityEngine.UI.Outline>();
                    o.effectColor = Theme.Playable;
                    o.effectDistance = new Vector2(4, -4);
                }
            }
        }
    }

    public class GameOverPanel : MonoBehaviour
    {
        GameController gc;
        Text title, sub;

        public static GameOverPanel Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("GameOver", parent, new Color(0, 0, 0, 0.7f), false);
            shade.rectTransform.Stretch();
            var g = shade.gameObject.AddComponent<GameOverPanel>();
            g.gc = gc;
            var panel = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            var pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(700, 340);
            g.title = UIKit.Label("Title", panel.transform, "", 64, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Outline(2);
            g.title.rectTransform.Stretch(0, 0, 0, 30);
            g.sub = UIKit.Label("Sub", panel.transform, "", 22, Theme.Text, TextAnchor.MiddleCenter);
            g.sub.rectTransform.Stretch(20, 120, 20, 120);
            var again = UIKit.Button("Again", panel.transform, "Play again", Theme.Accent, () => { g.Hide(); gc.PlayAgain(); }, 24);
            var ar = (RectTransform)again.transform;
            ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0); ar.sizeDelta = new Vector2(260, 64); ar.anchoredPosition = new Vector2(-140, 60);
            var menu = UIKit.Button("Menu", panel.transform, "Main menu", Theme.Secondary, () => { g.Hide(); gc.Menu.Show(); }, 24);
            var mr = (RectTransform)menu.transform;
            mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0); mr.sizeDelta = new Vector2(260, 64); mr.anchoredPosition = new Vector2(140, 60);
            shade.gameObject.SetActive(false);
            return g;
        }

        public void Show(string winner, GameState s)
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            string myName = s?.MePlayer?.Name;
            bool won = winner != null && myName != null && winner == myName;
            if (winner != null) PlayerStats.Record(won);
            title.text = won ? "VICTORY" : "DEFEAT";
            title.color = won ? Theme.Accent : Theme.Danger;
            sub.text = winner == null ? "The game is over." : (won ? "You won the pod!" : "Winner: " + winner);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}

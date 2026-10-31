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
            var artRt = UIKit.Rect("Art", frame.transform).Stretch();
            p.art = artRt.gameObject.AddComponent<RawImage>();
            p.art.raycastTarget = false;
            frame.gameObject.SetActive(false);
            return p;
        }

        // Delayed hover preview (Options > card preview delay, 1.5 s by default).
        CardData pending; bool pendingRight; float pendingSince;
        PreviewTimer timer;

        /// <summary>Shows the preview after the hover delay, unless the pointer leaves first (Hide).</summary>
        public void Request(CardData c, bool onRight)
        {
            if (c == null || c.Hidden) { Hide(); return; }
            if (UiScale.PreviewDelay <= 0f) { Show(c, onRight); return; }
            pending = c; pendingRight = onRight; pendingSince = Time.unscaledTime;
            if (timer == null) { timer = transform.parent.gameObject.AddComponent<PreviewTimer>(); timer.Preview = this; }
        }

        internal void Tick()
        {
            if (pending == null || Time.unscaledTime - pendingSince < UiScale.PreviewDelay) return;
            var c = pending; pending = null;
            Show(c, pendingRight);
        }

        public void Show(CardData c, bool onRight)
        {
            if (c == null || c.Hidden) { Hide(); return; }
            pending = null;
            showing = c;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            float s = UiScale.Value, w = 340 * s, h = 476 * s;
            rt.Place(onRight ? 1570 - w : 290, Mathf.Max(20, 488 - h / 2f), w, h);
            UIKit.RoundCard(frame, w);
            art.enabled = false;
            text.text = "<b>" + c.Name + "</b>   " + CardView.ManaText(c.ManaCost) + "\n<i>" + c.TypeLine + "</i>\n\n" + c.Text
                        + (c.HasPT ? "\n\n<b>" + c.Power + "/" + c.Toughness + "</b>" : "");
            if (CardImageCache.Instance != null)
            {
                var target = c;
                CardImageCache.Instance.GetFor(c, tex =>
                {
                    if (showing != target) return;
                    art.texture = tex;
                    art.enabled = true;
                });
            }
        }

        public void Hide()
        {
            pending = null;
            showing = null;
            gameObject.SetActive(false);
        }
    }

    /// <summary>Drives the preview delay while the (inactive) preview itself can't update.</summary>
    public class PreviewTimer : MonoBehaviour
    {
        public CardPreview Preview;
        void Update() { if (Preview != null) Preview.Tick(); }
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
            UIKit.RoundCard(frame, w);
            var artRt = UIKit.Rect("Art", frame.transform).Stretch();
            var art = artRt.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.enabled = false;
            if (c != null && !c.Hidden && CardImageCache.Instance != null)
                CardImageCache.Instance.GetFor(c, tex => { if (art != null) { art.texture = tex; art.enabled = true; } });
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => onClick?.Invoke();
            pe.OnRightClick = () => { if (c != null) CardInspector.Instance?.Show(c); };
            if (onHover != null)
            {
                pe.OnEnter = () => onHover(c);
                pe.OnExit = () => onHover(null);
            }
            return frame;
        }
    }

    /// <summary>
    /// Browse a graveyard, exile, command zone (right-click a player) or this game's revealed cards. Cards can be
    /// clicked when Forge allows it (cast the commander, flashback...).
    /// </summary>
    public class ZoneViewer : MonoBehaviour
    {
        /// <summary>Cards revealed this game, newest last (filled by the reveal pop-up).</summary>
        public static readonly List<CardData> Revealed = new List<CardData>();
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
            v.title = UIKit.Label("Title", panel.transform, "", 26, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
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
            UIKit.AddScrollbar(sr);
            var close = UIKit.Button("Close", panel.transform, "Close", Theme.Secondary, v.Close, 20);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0); cr.sizeDelta = new Vector2(200, 46); cr.anchoredPosition = new Vector2(0, 36);
            UIKit.CloseX(panel.transform, v.Close);
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

        public bool IsOpen => gameObject.activeSelf;
        string lastSig;

        public void Close()
        {
            playerId = -1;
            lastSig = null;
            gc.Preview.Hide();
            gameObject.SetActive(false);
        }

        public void Refresh(GameState s)
        {
            if (!gameObject.activeSelf || s == null) return;
            List<CardData> list;
            if (zone == "Revealed")
            {
                list = Revealed;
                title.text = "Revealed this game (" + list.Count + ")";
            }
            else
            {
                if (playerId < 0) return;
                var p = s.Player(playerId);
                if (p == null) { Close(); return; }
                list = zone == "Exile" ? p.Exile : zone == "Command" ? p.Command : p.Graveyard;
                string label = zone == "Command" ? (list.Count == 1 ? "Commander" : "Commanders") : zone;
                title.text = p.ShortName + " — " + label + " (" + list.Count + ")";
                if (zone == "Command" && list.Count == 0) title.text = p.ShortName + " — commander is not in the command zone";
            }
            // Rebuild only when the cards or what's playable among them changed.
            var sig = new System.Text.StringBuilder(zone).Append(playerId).Append(':');
            foreach (var c0 in list) sig.Append(c0.Id).Append(s.Prompt.ActionableCards.Contains(c0.Id) || s.Prompt.SelectableCards.Contains(c0.Id) ? '*' : ',');
            if (sig.ToString() == lastSig) return;
            lastSig = sig.ToString();
            foreach (Transform child in content) Destroy(child.gameObject);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var c = list[i];
                System.Action click = zone == "Revealed" ? (System.Action)null : () => gc.ClickCard(c.Id);
                var tile = CardTile.Create(content, c, 160, 224, click, cd => { if (cd != null) gc.Preview.Request(cd, false); else gc.Preview.Hide(); });
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
        RectTransform plate;
        CanvasGroup group;
        float shownAt;

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
            g.plate = pr;
            g.group = shade.gameObject.AddComponent<CanvasGroup>();
            g.title = UIKit.Label("Title", panel.transform, "", 64, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Outline(2).Titled();
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
            Sound.Play(won ? Sfx.Victory : Sfx.Defeat);
            title.text = won ? "VICTORY" : "DEFEAT";
            title.color = won ? Theme.Accent : Theme.Danger;
            sub.text = winner == null ? "The game is over." : (won ? "You won the pod!" : "Winner: " + winner);
            shownAt = Motion.Now;
        }

        // The plate rises into place while the board dims (travel); victory rises a little higher and brighter.
        void Update()
        {
            float t = Mathf.Clamp01((Motion.Now - shownAt) / Motion.Travel);
            float e = Easing.Eval(Ease.Out, t);
            group.alpha = e;
            plate.anchoredPosition = new Vector2(0, Motion.ReduceMotion ? 0f : -70f * (1f - e));
            plate.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, e);
        }

        public void Hide() => gameObject.SetActive(false);
    }
}

namespace MTGUnited
{
    /// <summary>
    /// Cards revealed from a library (or hand) pop up briefly at the top of the board, then fade on their own.
    /// Hovering keeps them up; left-click dismisses; right-click opens every card revealed this game.
    /// </summary>
    public class RevealPopup : MonoBehaviour
    {
        const float Life = 4f, Fade = 0.3f;
        GameController gc;
        CanvasGroup group;
        RectTransform panel, row;
        Text title;
        float shownAt = -100f;
        bool hovered;

        public static RevealPopup Create(RectTransform parent, GameController gc)
        {
            var bg = UIKit.Panel("RevealPopup", parent, new Color(0.04f, 0.04f, 0.07f, 0.94f));
            var r = bg.gameObject.AddComponent<RevealPopup>();
            r.gc = gc;
            r.panel = bg.rectTransform;
            r.panel.anchorMin = r.panel.anchorMax = new Vector2(0.5f, 0.5f);
            r.group = bg.gameObject.AddComponent<CanvasGroup>();
            var rim = bg.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(1f, 0.8f, 0.4f, 0.6f); rim.effectDistance = new Vector2(2, -2);
            var pe = bg.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = r.Hide;
            pe.OnRightClick = () => { r.Hide(); gc.ZoneViewer.Open(-1, "Revealed"); };
            pe.OnEnter = () => r.hovered = true;
            pe.OnExit = () => { r.hovered = false; r.shownAt = Time.unscaledTime - 1.5f; };
            r.title = UIKit.Label("Title", bg.transform, "", 18, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            r.title.rectTransform.Stretch(16, 0, 16, 10);
            var hint = UIKit.Label("Hint", bg.transform, "right-click: all revealed cards", 12, Theme.TextDim, TextAnchor.LowerCenter);
            hint.rectTransform.Stretch(10, 6, 10, 0);
            r.row = UIKit.Rect("Row", bg.transform).Stretch(14, 26, 14, 40);
            bg.gameObject.SetActive(false);
            return r;
        }

        public void Show(string message, List<CardData> cards)
        {
            if (cards.Count == 0) return;
            ZoneViewer.Revealed.AddRange(cards);
            foreach (Transform c in row) Destroy(c.gameObject);
            int n = Mathf.Min(cards.Count, 7);
            const float w = 150, h = 210, gap = 10;
            for (int i = 0; i < n; i++)
            {
                var cd = cards[i];
                var tile = CardTile.Create(row, cd, w, h, null, null);
                var tr = tile.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0, 0.5f); tr.pivot = new Vector2(0, 0.5f);
                tr.anchoredPosition = new Vector2(i * (w + gap), 0);
                tile.raycastTarget = false;
            }
            title.text = string.IsNullOrEmpty(message) ? "Revealed" : message.Replace('\n', ' ');
            panel.sizeDelta = new Vector2(Mathf.Max(420, n * (w + gap) - gap + 28), h + 80);
            panel.anchoredPosition = new Vector2(0, 150);
            shownAt = Time.unscaledTime;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Sound.Play(Sfx.Popup, 0.6f);
        }

        public void Hide() => gameObject.SetActive(false);

        void Update()
        {
            if (hovered) { group.alpha = 1f; shownAt = Time.unscaledTime; return; }
            float t = Time.unscaledTime - shownAt;
            // Quick fade in (within the motion budget), hold, then fade out.
            group.alpha = t < 0.15f ? t / 0.15f : t > Life ? 1f - (t - Life) / Fade : 1f;
            if (t > Life + Fade) Hide();
        }
    }
}

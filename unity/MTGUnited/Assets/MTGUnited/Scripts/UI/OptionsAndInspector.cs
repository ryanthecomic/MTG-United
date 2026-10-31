using System;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Right-click any card: a very large view of it on the left edge of the screen.
    /// Closes on any click, another right-click, or Esc.
    /// </summary>
    public class CardInspector : MonoBehaviour
    {
        public static CardInspector Instance { get; private set; }
        RectTransform card;
        RawImage art;
        Text text;
        CardData showing;
        float openedAt;

        public bool IsOpen => gameObject.activeSelf;

        public static CardInspector Create(RectTransform top)
        {
            var shade = UIKit.Panel("Inspector", top, new Color(0, 0, 0, 0.45f), false);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(4000, 3000);   // covers the whole screen at any aspect
            var ins = shade.gameObject.AddComponent<CardInspector>();
            Instance = ins;
            var pe = shade.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = ins.Hide;
            pe.OnRightClick = ins.Hide;

            var frame = UIKit.Panel("Card", shade.transform, new Color(0.06f, 0.06f, 0.08f, 0.98f));
            frame.raycastTarget = false;
            ins.card = frame.rectTransform;
            ins.card.anchorMin = ins.card.anchorMax = new Vector2(0.5f, 0.5f);
            ins.text = UIKit.Label("Text", frame.transform, "", 26, Theme.Text, TextAnchor.UpperLeft);
            ins.text.rectTransform.Stretch(28, 28, 28, 28);
            ins.text.supportRichText = true;
            var artRt = UIKit.Rect("Art", frame.transform).Stretch(6, 6, 6, 6);
            ins.art = artRt.gameObject.AddComponent<RawImage>();
            ins.art.raycastTarget = false;
            var x = UIKit.CloseX(frame.transform, () => { ins.openedAt = -1f; ins.Hide(); }, 52, -26);
            x.transform.SetAsLastSibling();
            shade.gameObject.SetActive(false);
            return ins;
        }

        public void Show(CardData c)
        {
            if (c == null || c.Hidden) return;
            showing = c;
            openedAt = Time.unscaledTime;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            // Board space is 1920x1080 centred on the screen; the card hugs the left edge.
            float h = Mathf.Min(1040f, 940f * UiScale.Value), w = h * 0.716f;
            card.sizeDelta = new Vector2(w, h);
            card.anchoredPosition = new Vector2(-960 + 20 + w / 2f, 0);
            art.enabled = false;
            text.text = "<b>" + c.Name + "</b>   " + CardView.ManaText(c.ManaCost) + "\n<i>" + c.TypeLine + "</i>\n\n" + c.Text
                        + (c.HasPT ? "\n\n<b>" + c.Power + "/" + c.Toughness + "</b>" : "");
            Sound.Play(Sfx.Popup, 0.5f);
            if (CardImageCache.Instance != null)
            {
                var target = c;
                CardImageCache.Instance.GetFor(c, tex =>
                {
                    if (showing != target || tex == null) return;
                    art.texture = tex;
                    art.enabled = true;
                });
            }
        }

        public void Hide()
        {
            // The right-click that opened it must not also close it.
            if (Time.unscaledTime - openedAt < 0.1f) return;
            showing = null;
            gameObject.SetActive(false);
        }
    }

    /// <summary>Options: interface size, preview delay, sound and auto-pay. Opened from the main menu or the in-game menu.</summary>
    public class OptionsPanel : MonoBehaviour
    {
        GameController gc;
        RectTransform panel;
        Text scaleText, delayText, soundText, volumeText, autoPayText;

        public bool IsOpen => gameObject.activeSelf;

        public static OptionsPanel Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("Options", parent, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.Stretch();
            var o = shade.gameObject.AddComponent<OptionsPanel>();
            o.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = o.Close;

            var p = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            o.panel = p.rectTransform;
            o.panel.anchorMin = o.panel.anchorMax = new Vector2(0.5f, 0.5f);
            o.panel.sizeDelta = new Vector2(680, 880);
            p.gameObject.AddComponent<PointerEvents>();   // clicks inside the panel don't close it
            UiScaled.Add(p, Vector2.zero);

            var title = UIKit.Label("Title", p.transform, "OPTIONS", 30, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            title.rectTransform.Stretch(20, 0, 20, 24);

            // Tabs: Settings | Shortcuts.
            o.settingsPage = UIKit.Rect("Settings", p.transform).Stretch();
            o.shortcutsPage = UIKit.Rect("Shortcuts", p.transform).Stretch();
            o.tabSettings = UIKit.Button("TabSettings", p.transform, "SETTINGS", Theme.Accent, () => o.ShowTab(false), 18);
            ((RectTransform)o.tabSettings.transform).Place(150, 880 - 124, 180, 44);
            o.tabShortcuts = UIKit.Button("TabShortcuts", p.transform, "SHORTCUTS", Theme.Secondary, () => o.ShowTab(true), 18);
            ((RectTransform)o.tabShortcuts.transform).Place(350, 880 - 124, 180, 44);
            o.BuildShortcuts(o.shortcutsPage);

            float y = 680;
            o.scaleText = o.Row(o.settingsPage, "Interface size", ref y, () => o.Step(-1, Setting.Scale), () => o.Step(1, Setting.Scale));
            o.cardText = o.Row(o.settingsPage, "Card size", ref y, () => o.Step(-1, Setting.Cards), () => o.Step(1, Setting.Cards));
            o.textSizeText = o.Row(o.settingsPage, "Text size", ref y, () => o.Step(-1, Setting.Text), () => o.Step(1, Setting.Text));
            o.delayText = o.Row(o.settingsPage, "Card preview delay", ref y, () => o.Step(-1, Setting.Delay), () => o.Step(1, Setting.Delay));
            o.volumeText = o.Row(o.settingsPage, "Volume", ref y, () => o.Step(-1, Setting.Volume), () => o.Step(1, Setting.Volume));
            o.speedText = o.ToggleRow(o.settingsPage, "Animation speed", ref y, () =>
            {
                Motion.SpeedMode = (Motion.SpeedSetting)(((int)Motion.SpeedMode + 1) % 3);
                o.Refresh();
            });
            o.reduceText = o.ToggleRow(o.settingsPage, "Reduce motion", ref y, () => { Motion.ReduceMotion = !Motion.ReduceMotion; o.Refresh(); });
            o.soundText = o.ToggleRow(o.settingsPage, "Sound", ref y, () => { Sound.Enabled = !Sound.Enabled; o.Refresh(); });
            o.autoPayText = o.ToggleRow(o.settingsPage, "Auto-pay mana", ref y, () =>
            {
                gc.AutoPayMana = !gc.AutoPayMana;
                PlayerPrefs.SetInt("autoPay", gc.AutoPayMana ? 1 : 0);
                o.Refresh();
            });

            var close = UIKit.Button("Close", p.transform, "DONE", Theme.Accent, o.Close, 22);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0); cr.pivot = new Vector2(0.5f, 0);
            cr.sizeDelta = new Vector2(240, 56); cr.anchoredPosition = new Vector2(0, 28);

            UIKit.CloseX(p.transform, o.Close);
            shade.gameObject.SetActive(false);
            return o;
        }

        enum Setting { Scale, Cards, Text, Delay, Volume }
        RectTransform settingsPage, shortcutsPage;
        Button tabSettings, tabShortcuts;

        void ShowTab(bool shortcuts)
        {
            settingsPage.gameObject.SetActive(!shortcuts);
            shortcutsPage.gameObject.SetActive(shortcuts);
            tabSettings.GetComponent<Image>().color = shortcuts ? Theme.Secondary : Theme.Accent;
            tabShortcuts.GetComponent<Image>().color = shortcuts ? Theme.Accent : Theme.Secondary;
            FadeIn.Play((shortcuts ? shortcutsPage : settingsPage).gameObject, 0.2f);
        }

        /// <summary>The shortcuts tab: every key binding, two columns (key | what it does).</summary>
        void BuildShortcuts(RectTransform page)
        {
            float y = 700;
            foreach (var (key, what) in Shortcuts.List)
            {
                var k = UIKit.Panel("Key", page, new Color(1f, 1f, 1f, 0.08f));
                k.rectTransform.Place(40, y, 190, 38);
                var kt = UIKit.Label("K", k.transform, key, 17, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                kt.rectTransform.Stretch(4, 0, 4, 0);
                var w = UIKit.Label("What", page, what, 17, Theme.Text, TextAnchor.MiddleLeft);
                w.rectTransform.Place(248, y, 400, 38);
                y -= 44;
            }
        }
        Text cardText, speedText, reduceText, textSizeText;

        Text Row(Transform parent, string label, ref float y, Action minus, Action plus)
        {
            var l = UIKit.Label("Label", parent, label, 22, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.rectTransform.Place(40, y, 300, 52);
            var m = UIKit.Button("Minus", parent, "−", Theme.Secondary, () => minus(), 26);
            ((RectTransform)m.transform).Place(350, y + 2, 52, 48);
            var v = UIKit.Label("Value", parent, "", 22, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            v.rectTransform.Place(406, y, 128, 52);
            var pl = UIKit.Button("Plus", parent, "+", Theme.Secondary, () => plus(), 26);
            ((RectTransform)pl.transform).Place(538, y + 2, 52, 48);
            y -= 66;
            return v;
        }

        Text ToggleRow(Transform parent, string label, ref float y, Action toggle)
        {
            var l = UIKit.Label("Label", parent, label, 22, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.rectTransform.Place(40, y, 300, 52);
            var b = UIKit.Button("Toggle", parent, "", Theme.Secondary, () => toggle(), 20);
            ((RectTransform)b.transform).Place(350, y + 2, 240, 48);
            y -= 66;
            return UIKit.LabelOf(b);
        }

        void Step(int dir, Setting s)
        {
            switch (s)
            {
                case Setting.Scale: UiScale.Value = Mathf.Round((UiScale.Value + dir * 0.05f) * 20f) / 20f; break;
                case Setting.Text: UiScale.TextScale = Mathf.Round((UiScale.TextScale + dir * 0.05f) * 20f) / 20f; break;
                case Setting.Cards: UiScale.CardScale = Mathf.Round((UiScale.CardScale + dir * 0.05f) * 20f) / 20f; break;
                case Setting.Delay: UiScale.PreviewDelay = Mathf.Round((UiScale.PreviewDelay + dir * 0.25f) * 4f) / 4f; break;
                case Setting.Volume:
                    Sound.Volume = Mathf.Round((Sound.Volume + dir * 0.1f) * 10f) / 10f;
                    Sound.Play(Sfx.Cast);
                    break;
            }
            Refresh();
            if ((s == Setting.Scale || s == Setting.Cards) && gc.InGame && gc.State != null) gc.BoardView.Render(gc.State);   // re-flow around the bigger HUD
        }

        void Refresh()
        {
            scaleText.text = Mathf.RoundToInt(UiScale.Value * 100) + "%";
            cardText.text = Mathf.RoundToInt(UiScale.CardScale * 100) + "%";
            textSizeText.text = Mathf.RoundToInt(UiScale.TextScale * 100) + "%";
            delayText.text = UiScale.PreviewDelay <= 0f ? "instant" : UiScale.PreviewDelay.ToString("0.##") + " s";
            volumeText.text = Mathf.RoundToInt(Sound.Volume * 100) + "%";
            soundText.text = Sound.Enabled ? "ON" : "OFF";
            speedText.text = Motion.SpeedMode == Motion.SpeedSetting.Normal ? "NORMAL" : Motion.SpeedMode == Motion.SpeedSetting.Fast ? "FAST" : "INSTANT";
            reduceText.text = Motion.ReduceMotion ? "ON" : "OFF";
            autoPayText.text = gc.AutoPayMana ? "ON" : "OFF";
        }

        public void Open() => Open(false);

        public void Open(bool shortcutsTab)
        {
            ShowTab(shortcutsTab);
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close() => gameObject.SetActive(false);
    }
}

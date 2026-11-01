using System;
using System.Collections.Generic;
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
        RectTransform card, card2;
        RawImage art, art2;
        Text text, text2;
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

            MakeFace(shade.transform, "Card", out ins.card, out ins.art, out ins.text);
            // Double-faced cards: the other face, right beside the first.
            MakeFace(shade.transform, "OtherFace", out ins.card2, out ins.art2, out ins.text2);
            // X in the top-right corner of the screen (the board space is 1920x1080 centred in the shade).
            var x = UIKit.IconButton("Close", shade.transform, Icons.Cross, 0, 0, 56, () => { ins.openedAt = -1f; ins.Hide(); });
            var xr = (RectTransform)x.transform;
            xr.anchorMin = xr.anchorMax = new Vector2(0.5f, 0.5f); xr.pivot = new Vector2(1, 1);
            xr.anchoredPosition = new Vector2(960 - 10, 540 - 10);
            x.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            shade.gameObject.SetActive(false);
            return ins;
        }

        /// <summary>
        /// Kitchen table, Alt held: the same big view, but it doesn't block the mouse, so moving over other cards
        /// switches the view to them.
        /// </summary>
        public void ShowPassive(CardData c)
        {
            if (c == null || c.Hidden || c == showing) return;
            Show(c);
            SetBlocking(false);
        }

        void SetBlocking(bool on)
        {
            foreach (var g in GetComponentsInChildren<Graphic>(true)) if (g.GetComponent<Button>() == null) g.raycastTarget = on && g.gameObject == gameObject;
            var x = transform.Find("Close");
            if (x != null) x.gameObject.SetActive(on);
        }

        static void MakeFace(Transform parent, string name, out RectTransform rt, out RawImage img, out Text label)
        {
            var frame = UIKit.Panel(name, parent, new Color(0.06f, 0.06f, 0.08f, 0.98f));
            frame.raycastTarget = false;
            rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            label = UIKit.Label("Text", frame.transform, "", 26, Theme.Text, TextAnchor.UpperLeft);
            label.rectTransform.Stretch(28, 28, 28, 28);
            label.supportRichText = true;
            if (label is SymbolText ls) ls.Localize = false;   // card text stays as printed
            var artRt = UIKit.Rect("Art", frame.transform).Stretch();
            img = artRt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
        }

        public void Show(CardData c)
        {
            if (c == null || c.Hidden) return;
            SetBlocking(true);
            showing = c;
            openedAt = Time.unscaledTime;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            // Board space is 1920x1080 centred on the screen; the card hugs the left edge.
            float h = Mathf.Min(1040f, 940f * UiScale.Value), w = h * 0.716f;
            Fill(card, art, text, c, w, h, new Vector2(-960 + 20 + w / 2f, 0));

            // A double-faced card (custom set): its other face next to it.
            string other = c.Token ? null : CustomSets.OtherFace(c.Name);
            card2.gameObject.SetActive(other != null);
            if (other != null)
            {
                var info = CardCatalog.Get(other);
                var back = info != null ? info.ToCardData() : new CardData { Name = other };
                Fill(card2, art2, text2, back, w, h, new Vector2(-960 + 20 + w * 1.5f + 24, 0));
            }
            Sound.Play(Sfx.Popup, 0.5f);
        }

        void Fill(RectTransform frame, RawImage img, Text label, CardData c, float w, float h, Vector2 pos)
        {
            frame.sizeDelta = new Vector2(w, h);
            UIKit.RoundCard(frame.GetComponent<Image>(), w);
            frame.anchoredPosition = pos;
            img.enabled = false;
            label.text = "<b>" + c.Name + "</b>   " + CardView.ManaText(c.ManaCost ?? "") + "\n<i>" + c.TypeLine + "</i>\n\n" + c.Text
                         + (c.HasPT ? "\n\n<b>" + c.Power + "/" + c.Toughness + "</b>" : "");
            if (CardImageCache.Instance == null) return;
            var target = showing;
            CardImageCache.Instance.GetFor(c, tex =>
            {
                if (showing != target || tex == null || img == null) return;
                img.texture = tex;
                img.enabled = true;
            });
        }

        /// <summary>Closes right away (releasing Alt at the kitchen table).</summary>
        public void ForceHide()
        {
            showing = null;
            gameObject.SetActive(false);
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
            o.panel.sizeDelta = new Vector2(680, PanelH);
            p.gameObject.AddComponent<PointerEvents>();   // clicks inside the panel don't close it
            UiScaled.Add(p, Vector2.zero).MaxScale = 1040f / PanelH;   // always fits on screen

            var title = UIKit.Label("Title", p.transform, "OPTIONS", 30, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            title.rectTransform.Stretch(20, 0, 20, 24);

            // Tabs: Settings | Shortcuts.
            o.settingsPage = UIKit.Rect("Settings", p.transform).Stretch();
            o.shortcutsPage = UIKit.Rect("Shortcuts", p.transform).Stretch();
            o.tabSettings = UIKit.Button("TabSettings", p.transform, "SETTINGS", Theme.Accent, () => o.ShowTab(false), 18);
            ((RectTransform)o.tabSettings.transform).Place(150, PanelH - 124, 180, 44);
            o.tabShortcuts = UIKit.Button("TabShortcuts", p.transform, "SHORTCUTS", Theme.Secondary, () => o.ShowTab(true), 18);
            ((RectTransform)o.tabShortcuts.transform).Place(350, PanelH - 124, 180, 44);
            o.BuildShortcuts(o.shortcutsPage);

            float y = PanelH - 200;
            // Screen: resolution and window mode (the board always fits inside, letterboxed if needed).
            o.resText = o.Row(o.settingsPage, "Resolution", ref y, () => o.StepResolution(-1), () => o.StepResolution(1));
            o.modeText = o.ToggleRow(o.settingsPage, "Display mode", ref y, o.CycleDisplayMode);
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
            o.languageText = o.ToggleRow(o.settingsPage, "Language", ref y, () =>
            {
                Loc.Language = (Loc.Language + 1) % Loc.Codes.Length;
                o.Refresh();
            });
            if (o.languageText is SymbolText lt) lt.Localize = false;   // each language's own name

            // The error log (only real errors, one line each): for bug reports.
            var log = UIKit.Button("ErrorLog", o.settingsPage, "Error log", Theme.Secondary, () => ErrorReporter.OpenLog(gc), 15);
            ((RectTransform)log.transform).Place(28, 34, 170, 44);

            var close = UIKit.Button("Close", p.transform, "DONE", Theme.Accent, o.Close, 22);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0); cr.pivot = new Vector2(0.5f, 0);
            cr.sizeDelta = new Vector2(240, 56); cr.anchoredPosition = new Vector2(0, 28);

            UIKit.CloseX(p.transform, o.Close);
            shade.gameObject.SetActive(false);
            return o;
        }

        enum Setting { Scale, Cards, Text, Delay, Volume }
        const float PanelH = 960;
        Text resText, modeText;

        static readonly FullScreenMode[] Modes = { FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed };

        static string ModeName(FullScreenMode m) => m == FullScreenMode.Windowed ? "WINDOWED" : m == FullScreenMode.ExclusiveFullScreen ? "FULLSCREEN" : m == FullScreenMode.MaximizedWindow ? "MAXIMIZED" : "BORDERLESS";

        /// <summary>Every size the monitor offers (plus the current one), smallest first.</summary>
        static List<Vector2Int> Sizes()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 1024 && v.y >= 576 && !list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Screen.width, Screen.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => (a.x * a.y).CompareTo(b.x * b.y));
            return list;
        }

        void StepResolution(int dir)
        {
            var sizes = Sizes();
            int i = sizes.IndexOf(new Vector2Int(Screen.width, Screen.height));
            i = Mathf.Clamp(i + dir, 0, sizes.Count - 1);
            Screen.SetResolution(sizes[i].x, sizes[i].y, Screen.fullScreenMode);
            pendingRes = sizes[i];
            Refresh();
        }

        void CycleDisplayMode()
        {
            int i = System.Array.IndexOf(Modes, Screen.fullScreenMode);
            var next = Modes[(i + 1 + Modes.Length) % Modes.Length];
            int w = Screen.width, h = Screen.height;
            if (next == FullScreenMode.Windowed && w >= Display.main.systemWidth)
            {
                // A window as big as the monitor would hide its own title bar: start a bit smaller.
                w = Mathf.RoundToInt(Display.main.systemWidth * 0.8f); h = Mathf.RoundToInt(w * 9f / 16f);
            }
            else if (next != FullScreenMode.Windowed) { w = Display.main.systemWidth; h = Display.main.systemHeight; }
            Screen.SetResolution(w, h, next);
            pendingMode = next;
            pendingRes = new Vector2Int(w, h);
            Refresh();
        }

        Vector2Int? pendingRes;
        FullScreenMode? pendingMode;

        void Update()
        {
            // The screen changes a frame or two after asking; keep the labels honest.
            if (pendingRes.HasValue && Screen.width == pendingRes.Value.x && Screen.height == pendingRes.Value.y) pendingRes = null;
            if (pendingMode.HasValue && Screen.fullScreenMode == pendingMode.Value) pendingMode = null;
            var r = pendingRes ?? new Vector2Int(Screen.width, Screen.height);
            string res = r.x + " × " + r.y;
            if (resText.text != res) resText.text = res;
            string mode = ModeName(pendingMode ?? Screen.fullScreenMode);
            if (modeText.text != mode) modeText.text = mode;
        }
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
        readonly List<(Image tab, RectTransform list)> shortcutTabs = new List<(Image, RectTransform)>();

        /// <summary>The shortcuts tab: one sub-tab per mode (Forge game, kitchen table, deck builder, everywhere).</summary>
        void BuildShortcuts(RectTransform page)
        {
            float x = 40, w = (680 - 80 - 3 * 8) / (float)Shortcuts.Tabs.Length;
            for (int t = 0; t < Shortcuts.Tabs.Length; t++)
            {
                int index = t;
                var b = UIKit.Button("Mode" + t, page, Shortcuts.Tabs[t].tab, Theme.Secondary, () => ShowShortcuts(index), 14);
                ((RectTransform)b.transform).Place(x, PanelH - 186, w, 38);
                x += w + 8;
                var list = UIKit.Rect("List" + t, page).Stretch();
                BuildShortcutList(list, Shortcuts.Tabs[t].list);
                shortcutTabs.Add((b.GetComponent<Image>(), list));
            }
        }

        void ShowShortcuts(int index)
        {
            for (int i = 0; i < shortcutTabs.Count; i++)
            {
                shortcutTabs[i].tab.color = i == index ? new Color(0.55f, 0.32f, 0.1f) : Theme.Secondary;
                shortcutTabs[i].list.gameObject.SetActive(i == index);
            }
        }

        /// <summary>The shortcut list that fits what you're doing right now.</summary>
        int ContextShortcutTab()
        {
            if (gc.Table != null && gc.Table.IsOpen) return 1;
            if (gc.InGame) return 0;
            if (gc.Menu != null && gc.Menu.Builder != null && gc.Menu.Builder.IsOpen) return 2;
            return 0;
        }

        void BuildShortcutList(RectTransform page, (string key, string what)[] items)
        {
            // A scrolling list (the kitchen table has more shortcuts than fit).
            float top = PanelH - 200, bottom = 100;
            var view = UIKit.Rect("View", page);
            view.Place(30, bottom, 620, top - bottom);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            var sr = view.gameObject.AddComponent<ScrollRect>();
            var content = UIKit.Rect("Content", view);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            float rowH = 40, h = items.Length * rowH + 8;
            content.sizeDelta = new Vector2(0, h);
            sr.content = content; sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            if (h > top - bottom) UIKit.AddScrollbar(sr);
            float y = -4;
            foreach (var (key, what) in items)
            {
                bool heading = key.EndsWith(":");
                var k = UIKit.Panel("Key", content, new Color(1f, 1f, 1f, heading ? 0f : 0.08f));
                var kr = k.rectTransform;
                kr.anchorMin = kr.anchorMax = new Vector2(0, 1); kr.pivot = new Vector2(0, 1);
                kr.sizeDelta = new Vector2(190, 34); kr.anchoredPosition = new Vector2(10, y);
                var kt = UIKit.Label("K", k.transform, key, heading ? 15 : 17, heading ? new Color(1f, 0.72f, 0.4f) : Theme.Accent,
                    heading ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, FontStyle.Bold);
                kt.rectTransform.Stretch(4, 0, 4, 0);
                var w = UIKit.Label("What", content, what, 17, heading ? Theme.TextDim : Theme.Text, TextAnchor.MiddleLeft);
                var wr = w.rectTransform;
                wr.anchorMin = wr.anchorMax = new Vector2(0, 1); wr.pivot = new Vector2(0, 1);
                wr.sizeDelta = new Vector2(380, 34); wr.anchoredPosition = new Vector2(218, y);
                w.resizeTextForBestFit = true; w.resizeTextMinSize = 12; w.resizeTextMaxSize = 17;
                y -= rowH;
            }
        }
        Text cardText, speedText, reduceText, textSizeText, languageText;

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
            y -= 60;
            return v;
        }

        Text ToggleRow(Transform parent, string label, ref float y, Action toggle)
        {
            var l = UIKit.Label("Label", parent, label, 22, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.rectTransform.Place(40, y, 300, 52);
            var b = UIKit.Button("Toggle", parent, "", Theme.Secondary, () => toggle(), 20);
            ((RectTransform)b.transform).Place(350, y + 2, 240, 48);
            y -= 60;
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
            if ((s == Setting.Scale || s == Setting.Cards) && gc.Table != null && gc.Table.gameObject.activeSelf) gc.Table.Render();   // the Kitchen Table too
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
            languageText.text = Loc.Names[Loc.Language];
        }

        public void Open() => Open(false);

        public void Open(bool shortcutsTab)
        {
            ShowTab(shortcutsTab);
            ShowShortcuts(ContextShortcutTab());
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
        }

        public void Close() => gameObject.SetActive(false);
    }
}

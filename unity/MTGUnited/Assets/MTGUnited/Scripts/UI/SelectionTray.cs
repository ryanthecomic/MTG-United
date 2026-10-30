using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Scrollable panel for picking cards Forge offers from zones that aren't on the board (library searches,
    /// graveyard choices...). Nonlands come first, then lands, each sorted A-Z. The OK / Cancel buttons stay in
    /// the HUD on the right, which this panel never covers.
    /// </summary>
    public class SelectionTray : MonoBehaviour
    {
        // Centred on the board, but kept clear of the prompt and buttons on the right (they start at x = 1590).
        const float HeaderH = 72;
        const float CellW = 150, CellH = 210, Gap = 12, Width = 1220, Height = 840, Left = 960 - Width / 2, Bottom = 540 - Height / 2;

        GameController gc;
        RectTransform panel, content;
        ScrollRect scroll;
        Text title, toggleLabel;
        Button toggle;
        bool collapsed;
        string shownKey = "";
        readonly Dictionary<int, Image> tiles = new Dictionary<int, Image>();

        public static SelectionTray Create(RectTransform board, GameController gc)
        {
            var bg = UIKit.Panel("SelectionTray", board, new Color(0.04f, 0.05f, 0.08f, 0.95f));
            bg.rectTransform.Place(Left, Bottom, Width, Height);
            var t = bg.gameObject.AddComponent<SelectionTray>();
            t.gc = gc;
            t.panel = bg.rectTransform;

            t.title = UIKit.Label("Title", bg.transform, "", 22, Theme.Selectable, TextAnchor.UpperLeft, FontStyle.Bold);
            // Fixed header band at the top; long prompts shrink to fit instead of spilling into the card grid.
            var tr0 = t.title.rectTransform;
            tr0.anchorMin = new Vector2(0, 1); tr0.anchorMax = new Vector2(1, 1); tr0.pivot = new Vector2(0.5f, 1);
            tr0.offsetMin = new Vector2(24, -HeaderH + 8); tr0.offsetMax = new Vector2(-200, -12);
            t.title.alignment = TextAnchor.MiddleLeft;
            t.title.verticalOverflow = VerticalWrapMode.Truncate;
            t.title.resizeTextForBestFit = true; t.title.resizeTextMinSize = 12; t.title.resizeTextMaxSize = 22;
            t.title.Titled();

            t.toggle = UIKit.Button("Toggle", bg.transform, "View board", Theme.Secondary, t.ToggleCollapsed, 16);
            var tr = (RectTransform)t.toggle.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(1, 1);
            tr.sizeDelta = new Vector2(160, 40); tr.anchoredPosition = new Vector2(-16, -12);
            t.toggleLabel = UIKit.LabelOf(t.toggle);

            var view = UIKit.Rect("Scroll", bg.transform).Stretch(16, 16, 16, HeaderH + 4);
            view.gameObject.AddComponent<RectMask2D>();
            // A transparent graphic so the mouse wheel scrolls anywhere in the panel, not just over cards.
            view.gameObject.AddComponent<Image>().color = Color.clear;
            t.scroll = view.gameObject.AddComponent<ScrollRect>();
            t.content = UIKit.Rect("Content", view);
            t.content.anchorMin = new Vector2(0, 1); t.content.anchorMax = new Vector2(1, 1); t.content.pivot = new Vector2(0.5f, 1);
            t.scroll.content = t.content;
            t.scroll.horizontal = false;
            t.scroll.scrollSensitivity = 60;
            t.scroll.movementType = ScrollRect.MovementType.Clamped;

            bg.gameObject.SetActive(false);
            return t;
        }

        /// <summary>Called on every state update with the selectable cards that aren't drawn on the board.</summary>
        public void Render(GameState s, List<CardData> cards)
        {
            if (cards.Count == 0)
            {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                shownKey = "";
                collapsed = false;
                return;
            }
            if (!gameObject.activeSelf) { gameObject.SetActive(true); collapsed = false; }
            transform.SetAsLastSibling();
            // Forge's prompts are often "Card (id)\n-\nInstruction"; show them on one line.
            string msg = string.IsNullOrEmpty(s.Prompt.Message) ? "Choose a card" : s.Prompt.Message.Trim();
            msg = System.Text.RegularExpressions.Regex.Replace(msg, @"\s*\n\s*-?\s*\n?\s*", "  \u2014  ");
            title.text = msg;

            // Only rebuild when the offered cards change, so the scroll position survives picks.
            string key = string.Join(",", cards.Select(c => c.Id).OrderBy(i => i));
            if (key != shownKey)
            {
                shownKey = key;
                Rebuild(cards);
            }
            foreach (var kv in tiles)
            {
                bool chosen = s.Prompt.ChosenCards.Contains(kv.Key);
                var o = kv.Value.gameObject.GetOrAdd<UnityEngine.UI.Outline>();
                o.effectColor = chosen ? Theme.Chosen : new Color(0, 0, 0, 0);
                o.effectDistance = new Vector2(5, -5);
                kv.Value.transform.localScale = chosen ? Vector3.one * 0.94f : Vector3.one;
            }
            ApplyCollapsed();
        }

        void Rebuild(List<CardData> cards)
        {
            foreach (Transform c in content) Destroy(c.gameObject);
            tiles.Clear();

            var nonlands = cards.Where(c => !c.IsLand).OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase).ToList();
            var lands = cards.Where(c => c.IsLand).OrderBy(c => c.Name, System.StringComparer.OrdinalIgnoreCase).ToList();

            float y = 0;
            y = Section("Nonlands", nonlands, y);
            y = Section("Lands", lands, y);
            content.sizeDelta = new Vector2(0, y + 10);
            scroll.verticalNormalizedPosition = 1f;
        }

        float Section(string label, List<CardData> list, float y)
        {
            if (list.Count == 0) return y;
            var header = UIKit.Label("Header", content, label.ToUpperInvariant() + "  (" + list.Count + ")", 18, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            var hr = header.rectTransform;
            hr.anchorMin = new Vector2(0, 1); hr.anchorMax = new Vector2(1, 1); hr.pivot = new Vector2(0, 1);
            hr.sizeDelta = new Vector2(0, 32); hr.anchoredPosition = new Vector2(8, -y);
            header.Titled();
            y += 38;

            float usable = Width - 32 - 16;
            int cols = Mathf.Max(1, Mathf.FloorToInt((usable + Gap) / (CellW + Gap)));
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                int id = c.Id;
                var tile = CardTile.Create(content, c, CellW, CellH, () => gc.ClickCard(id),
                    cd => { if (cd != null) gc.Preview.Request(cd, false); else gc.Preview.Hide(); });
                var tr = tile.rectTransform;
                tr.anchorMin = tr.anchorMax = new Vector2(0, 1); tr.pivot = new Vector2(0, 1);
                tr.anchoredPosition = new Vector2(8 + (i % cols) * (CellW + Gap), -(y + (i / cols) * (CellH + Gap)));
                tiles[id] = tile;
            }
            int rows = (list.Count + cols - 1) / cols;
            return y + rows * (CellH + Gap) + 14;
        }

        void ToggleCollapsed()
        {
            collapsed = !collapsed;
            ApplyCollapsed();
        }

        // Collapsed = just the title bar, so you can look at the board before choosing.
        void ApplyCollapsed()
        {
            scroll.gameObject.SetActive(!collapsed);
            panel.sizeDelta = new Vector2(Width, collapsed ? HeaderH : Height);
            panel.anchoredPosition = new Vector2(Left + Width / 2f, collapsed ? Bottom + Height - HeaderH / 2f : Bottom + Height / 2f);
            toggleLabel.text = collapsed ? "Show cards" : "View board";
        }
    }
}

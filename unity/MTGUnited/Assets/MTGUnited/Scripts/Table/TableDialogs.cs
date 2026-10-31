using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Browse a pile on the table (library search, top N, graveyard, exile, command). Click a card for its actions.</summary>
    public class TableZoneViewer : MonoBehaviour
    {
        Text title;
        RectTransform content;
        Action<int, Vector2> onPick;
        GameController gc;

        public bool IsOpen => gameObject.activeSelf;

        public static TableZoneViewer Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("TableViewer", parent, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.Stretch();
            var v = shade.gameObject.AddComponent<TableZoneViewer>();
            v.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = v.Close;
            var panel = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            var pr = panel.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f);
            pr.sizeDelta = new Vector2(1500, 700);
            panel.gameObject.AddComponent<PointerEvents>();
            v.title = UIKit.Label("Title", panel.transform, "", 26, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            v.title.rectTransform.Stretch(0, 0, 0, 16);
            var hint = UIKit.Label("Hint", panel.transform, "Click a card for actions  ·  right-click to zoom", 14, Theme.TextDim, TextAnchor.UpperCenter);
            hint.rectTransform.Stretch(0, 0, 0, 52);
            var scroll = UIKit.Rect("Scroll", panel.transform).Stretch(20, 24, 20, 80);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = Color.clear;
            v.content = UIKit.Rect("Content", scroll);
            v.content.anchorMin = new Vector2(0, 1); v.content.anchorMax = new Vector2(1, 1); v.content.pivot = new Vector2(0.5f, 1);
            var grid = v.content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160, 224); grid.spacing = new Vector2(12, 12); grid.childAlignment = TextAnchor.UpperCenter;
            v.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = v.content; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);
            UIKit.CloseX(panel.transform, v.Close);
            shade.gameObject.SetActive(false);
            return v;
        }

        public void Show(string heading, List<CardData> cards, Action<int, Vector2> pick)
        {
            onPick = pick;
            title.text = heading + " (" + cards.Count + ")";
            foreach (Transform c in content) Destroy(c.gameObject);
            foreach (var cd in cards)
            {
                var c = cd;
                var tile = CardTile.Create(content, c, 160, 224, () =>
                {
                    onPick?.Invoke(c.Id, Input.mousePosition);
                }, h => { if (h != null) gc.Preview.Request(h, false); else gc.Preview.Hide(); });
                tile.raycastTarget = true;
            }
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Short);
        }

        public void Close() { gc.Preview.Hide(); gameObject.SetActive(false); }
    }

    /// <summary>
    /// Create a token: pick one from your imported custom sets, or type any name with power/toughness and colour.
    /// </summary>
    public class TokenDialog : MonoBehaviour
    {
        InputField nameInput, ptInput;
        RectTransform list;
        Action<string, string, string> onCreate;   // name, P/T, colours
        string colors = "";
        readonly Dictionary<char, Image> colorBtns = new Dictionary<char, Image>();

        public bool IsOpen => gameObject.activeSelf;

        public static TokenDialog Create(RectTransform parent)
        {
            var shade = UIKit.Panel("TokenDialog", parent, new Color(0, 0, 0, 0.6f), false);
            shade.rectTransform.Stretch();
            var d = shade.gameObject.AddComponent<TokenDialog>();
            shade.gameObject.AddComponent<PointerEvents>().OnClick = d.Close;
            var p = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(760, 640);
            p.gameObject.AddComponent<PointerEvents>();
            var t = UIKit.Label("Title", p.transform, "CREATE A TOKEN", 26, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(0, 0, 0, 18);

            UIKit.Label("L1", p.transform, "Name", 16, Theme.TextDim, TextAnchor.LowerLeft).rectTransform.Place(40, 540, 300, 24);
            d.nameInput = UIKit.Input("Name", p.transform, "Soldier, Treasure, Spirit...", false, 20);
            d.nameInput.GetComponent<RectTransform>().Place(40, 490, 420, 48);
            d.nameInput.onValueChanged.AddListener(_ => d.RefreshList());
            UIKit.Label("L2", p.transform, "Power/Toughness", 16, Theme.TextDim, TextAnchor.LowerLeft).rectTransform.Place(480, 540, 240, 24);
            d.ptInput = UIKit.Input("PT", p.transform, "1/1", false, 20);
            d.ptInput.GetComponent<RectTransform>().Place(480, 490, 120, 48);

            float x = 40;
            foreach (char c in "WUBRG")
            {
                char cc = c;
                var b = UIKit.Button("C" + c, p.transform, c.ToString(), Theme.ForManaColor(c.ToString()), () => d.ToggleColor(cc), 18);
                ((RectTransform)b.transform).Place(x, 428, 52, 44);
                UIKit.LabelOf(b).color = Color.black;
                d.colorBtns[c] = b.GetComponent<Image>();
                x += 60;
            }

            UIKit.Label("L3", p.transform, "From your custom sets", 16, Theme.TextDim, TextAnchor.LowerLeft).rectTransform.Place(40, 384, 400, 24);
            var scroll = UIKit.Rect("Scroll", p.transform);
            scroll.Place(40, 104, 680, 278);
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            d.list = UIKit.Rect("List", scroll);
            d.list.anchorMin = new Vector2(0, 1); d.list.anchorMax = new Vector2(1, 1); d.list.pivot = new Vector2(0.5f, 1);
            var vl = d.list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4; vl.padding = new RectOffset(6, 6, 6, 6); vl.childControlHeight = false; vl.childControlWidth = true;
            d.list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = d.list; sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);

            var create = UIKit.Button("Create", p.transform, "CREATE", Theme.Accent, () =>
            {
                if (string.IsNullOrWhiteSpace(d.nameInput.text)) return;
                d.onCreate?.Invoke(d.nameInput.text.Trim(), d.ptInput.text.Trim(), d.colors);
                d.Close();
            }, 20);
            ((RectTransform)create.transform).Place(500, 28, 220, 56);
            UIKit.CloseX(p.transform, d.Close);
            shade.gameObject.SetActive(false);
            return d;
        }

        public void Show(Action<string, string, string> create)
        {
            onCreate = create;
            nameInput.text = ""; ptInput.text = ""; colors = "";
            foreach (var kv in colorBtns) kv.Value.color = Theme.ForManaColor(kv.Key.ToString()) * new Color(1, 1, 1, 0.45f);
            RefreshList();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            nameInput.ActivateInputField();
        }

        void ToggleColor(char c)
        {
            colors = colors.Contains(c.ToString()) ? colors.Replace(c.ToString(), "") : colors + c;
            foreach (var kv in colorBtns) kv.Value.color = Theme.ForManaColor(kv.Key.ToString()) * new Color(1, 1, 1, colors.Contains(kv.Key.ToString()) ? 1f : 0.45f);
        }

        void RefreshList()
        {
            foreach (Transform c in list) Destroy(c.gameObject);
            var tokens = CustomSets.Search(nameInput.text, 40, true);
            if (tokens.Count == 0)
            {
                var none = UIKit.Label("None", list, CustomSets.All.Count == 0 ? "No custom sets imported yet. Type a name above." : "No custom tokens match.", 15, Theme.TextDim, TextAnchor.MiddleLeft);
                none.rectTransform.sizeDelta = new Vector2(0, 36);
                return;
            }
            foreach (var tk in tokens)
            {
                var info = tk;
                var custom = CustomSets.Get(info.Name);
                var b = UIKit.Button("Token", list, info.Name + (custom != null && custom.PT.Length > 0 ? "   " + custom.PT : "") + "   <color=#9aa0aa>" + info.Set + "</color>",
                    Theme.Secondary, () => { onCreate?.Invoke(info.Name, custom?.PT ?? "", info.Colors ?? ""); Close(); }, 16);
                UIKit.LabelOf(b).alignment = TextAnchor.MiddleLeft;
                UIKit.LabelOf(b).rectTransform.Stretch(12, 0, 8, 0);
                ((RectTransform)b.transform).sizeDelta = new Vector2(0, 40);
            }
        }

        public void Close() => gameObject.SetActive(false);
    }
}

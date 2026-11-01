using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Answers Forge's blocking questions: choose cards/options, order cards, yes/no, a number or text,
    /// and distributing damage or counters. "Peek" hides the dialog so you can look at the board.
    /// </summary>
    public class RequestDialog : MonoBehaviour
    {
        GameController gc;
        RectTransform panel, body;
        Text title, status;
        Button confirm, peek, skip;
        CanvasGroup group;
        Dictionary<string, object> req;
        readonly List<int> picked = new List<int>();
        readonly List<(Image tile, Text badge)> tiles = new List<(Image, Text)>();
        int[] amounts;
        readonly List<Text> amountLabels = new List<Text>();
        InputField textInput;
        List<string> allNames, suggestedNames;
        RectTransform nameList;
        bool peeking;

        public bool IsOpen => gameObject.activeSelf;

        public static RequestDialog Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("Dialog", parent, new Color(0, 0, 0, 0.55f), false);
            shade.rectTransform.Stretch();
            var d = shade.gameObject.AddComponent<RequestDialog>();
            d.gc = gc;
            d.group = shade.gameObject.AddComponent<CanvasGroup>();
            var p = UIKit.Panel("Panel", shade.transform, Theme.Panel);
            d.panel = p.rectTransform;
            d.panel.anchorMin = d.panel.anchorMax = new Vector2(0.5f, 0.5f);
            d.panel.sizeDelta = new Vector2(1400, 620);
            d.title = UIKit.Label("Title", p.transform, "", 24, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            d.title.rectTransform.Stretch(30, 0, 30, 18);
            d.status = UIKit.Label("Status", p.transform, "", 16, Theme.TextDim, TextAnchor.LowerLeft);
            d.status.rectTransform.Stretch(30, 30, 30, 0);
            d.body = UIKit.Rect("Body", p.transform).Stretch(30, 100, 30, 80);

            d.confirm = UIKit.Button("Confirm", p.transform, "Confirm", Theme.Accent, d.Submit, 22);
            Anchor((RectTransform)d.confirm.transform, 1, 0, -130, 46, 220, 58);
            d.skip = UIKit.Button("Skip", p.transform, "None", Theme.Secondary, d.SubmitNone, 20);
            Anchor((RectTransform)d.skip.transform, 1, 0, -370, 46, 220, 58);
            d.peek = UIKit.Button("Peek", parent, "Show dialog", Theme.Secondary, d.TogglePeek, 18);
            Anchor((RectTransform)d.peek.transform, 0.5f, 0, 0, 40, 220, 50);
            d.peek.gameObject.SetActive(false);
            var peekInPanel = UIKit.Button("PeekIn", p.transform, "View board", Theme.Secondary, d.TogglePeek, 18);
            Anchor((RectTransform)peekInPanel.transform, 0, 0, 120, 46, 200, 50);
            MenuKit.Bandify(p);   // Arena look: a full-width band, dim shadow behind
            shade.gameObject.SetActive(false);
            return d;
        }

        static void Anchor(RectTransform r, float ax, float ay, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(ax, ay);
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(x, y);
        }

        public void Open(Dictionary<string, object> request)
        {
            req = request;
            picked.Clear();
            tiles.Clear();
            amountLabels.Clear();
            textInput = null;
            nameList = null;
            foreach (Transform c in body) Destroy(c.gameObject);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            SetPeek(false);
            title.text = req.Str("title");
            confirm.SetVisible(true);
            skip.SetVisible(false);

            switch (req.Str("kind"))
            {
                case "choose": BuildChoose(false); break;
                case "order": BuildChoose(true); break;
                case "confirm": BuildConfirm(); break;
                case "text": BuildText(); break;
                case "distribute": BuildDistribute(); break;
                case "name": BuildName(); break;
                default:
                    title.text = "Unsupported question from Forge: " + req.Str("kind");
                    break;
            }
            UpdateStatus();
        }

        // ------------------------------------------------------------------ choose / order

        void BuildChoose(bool ordered)
        {
            var options = req.Arr("options");
            bool reveal = req.Bool("reveal");
            int min = req.Int("min"), max = req.Int("max");
            bool anyCards = false;
            foreach (var o in options) if ((o as Dictionary<string, object>).Obj("card") != null) { anyCards = true; break; }

            var scroll = UIKit.Rect("Scroll", body).Stretch();
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            var content = UIKit.Rect("Content", scroll);
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(0.5f, 1);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = content; sr.horizontal = false; sr.scrollSensitivity = 40;
            UIKit.AddScrollbar(sr);

            if (anyCards)
            {
                var grid = content.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(170, 238);
                grid.spacing = new Vector2(14, 14);
                grid.childAlignment = TextAnchor.UpperCenter;
            }
            else
            {
                var v = content.gameObject.AddComponent<VerticalLayoutGroup>();
                v.spacing = 8; v.childForceExpandHeight = false; v.childControlHeight = false; v.childAlignment = TextAnchor.UpperCenter;
                v.childControlWidth = false; v.childForceExpandWidth = false;
            }

            // Big card lists (library searches): nonlands first, then lands, each A-Z. Answers still use Forge's indices.
            var order = new List<int>();
            for (int i = 0; i < options.Count; i++) order.Add(i);
            if (anyCards && options.Count > 8)
            {
                System.Func<int, CardData> cardAt = i => CardData.From((options[i] as Dictionary<string, object>).Obj("card"));
                order.Sort((a, b) =>
                {
                    var ca = cardAt(a); var cb = cardAt(b);
                    bool la = ca != null && ca.IsLand, lb = cb != null && cb.IsLand;
                    if (la != lb) return la ? 1 : -1;
                    return string.Compare(ca?.Name ?? "", cb?.Name ?? "", System.StringComparison.OrdinalIgnoreCase);
                });
            }
            tiles.AddRange(new (Image, Text)[options.Count]);

            foreach (int i in order)
            {
                var o = options[i] as Dictionary<string, object>;
                int idx = i;
                var cardJson = o.Obj("card");
                Image tile;
                if (cardJson != null)
                {
                    var cd = CardData.From(cardJson);
                    tile = CardTile.Create(content, cd, 170, 238, () => Toggle(idx), c => { if (c != null) gc.Preview.Request(c, false); else gc.Preview.Hide(); });
                    // Extra text (e.g. which ability) under the card name when it differs from the card.
                    string label = o.Str("label");
                    if (!string.IsNullOrEmpty(label) && label != cd.Name)
                    {
                        var cap = UIKit.Panel("Caption", tile.transform, new Color(0, 0, 0, 0.8f));
                        cap.raycastTarget = false;
                        var cr = cap.rectTransform; cr.anchorMin = new Vector2(0, 0); cr.anchorMax = new Vector2(1, 0.32f);
                        cr.offsetMin = cr.offsetMax = Vector2.zero;
                        var ct = UIKit.Label("Text", cap.transform, label, 12, Theme.Text, TextAnchor.MiddleCenter);
                        ct.rectTransform.Stretch(4, 2, 4, 2);
                        ct.resizeTextForBestFit = true; ct.resizeTextMinSize = 7; ct.resizeTextMaxSize = 13;
                    }
                }
                else
                {
                    var b = UIKit.Button("Option", content, o.Str("label"), Theme.PanelLight, () => Toggle(idx), 19);
                    ((RectTransform)b.transform).sizeDelta = new Vector2(900, 52);
                    tile = b.GetComponent<Image>();
                }
                var badge = UIKit.Label("Badge", tile.transform, "", 34, Theme.Accent, TextAnchor.UpperRight, FontStyle.Bold).Outline(2);
                badge.rectTransform.Stretch(6, 6, 10, 4);
                tiles[idx] = (tile, badge);
            }

            if (reveal)
            {
                confirm.SetVisible(true);
                UIKit.LabelOf(confirm).text = "OK";
                return;
            }
            UIKit.LabelOf(confirm).text = "Confirm";
            // Optional single choice: allow picking nothing.
            if (min == 0 && max == 1)
            {
                skip.SetVisible(true);
                UIKit.LabelOf(skip).text = "None";
            }
            if (ordered && min >= options.Count && max >= options.Count)
            {
                skip.SetVisible(true);
                UIKit.LabelOf(skip).text = "Keep this order";
            }
        }

        void Toggle(int idx)
        {
            if (req.Bool("reveal")) return;
            int max = req.Int("max");
            int min = req.Int("min");
            if (picked.Contains(idx)) picked.Remove(idx);
            else
            {
                if (max > 0 && picked.Count >= max)
                {
                    if (max == 1) picked.Clear(); else return;
                }
                picked.Add(idx);
            }
            // Single, mandatory choice: one click answers it.
            if (req.Str("kind") == "choose" && max == 1 && min == 1 && picked.Count == 1) { Submit(); return; }
            UpdateStatus();
        }

        void UpdateStatus()
        {
            string kind = req.Str("kind");
            int min = req.Int("min"), max = req.Int("max");
            for (int i = 0; i < tiles.Count; i++)
            {
                int pos = picked.IndexOf(i);
                tiles[i].badge.text = pos < 0 ? "" : (kind == "order" ? (pos + 1).ToString() : "✓");
                var outline = tiles[i].tile.gameObject.GetOrAdd<UnityEngine.UI.Outline>();
                outline.effectColor = pos >= 0 ? Theme.Chosen : Color.clear;
                outline.effectDistance = new Vector2(4, -4);
            }
            if (kind == "choose" || kind == "order")
            {
                if (req.Bool("reveal")) { status.text = ""; confirm.interactable = true; return; }
                string range = max <= 0 ? "at least " + min : min == max ? min.ToString() : min + "–" + max;
                status.text = (kind == "order" ? "Click in order (first = top). " : "") + "Pick " + range + ".  Selected: " + picked.Count;
                confirm.interactable = picked.Count >= Mathf.Max(0, min) && (max <= 0 || picked.Count <= max);
            }
            else if (kind == "distribute")
            {
                int total = 0;
                foreach (var a in amounts) total += a;
                int amount = req.Int("amount");
                status.text = "Assigned " + total + " of " + amount;
                confirm.interactable = total == amount;
            }
            else if (kind != "name")
            {
                status.text = "";
                confirm.interactable = true;
            }
        }

        // ------------------------------------------------------------------ confirm / text / distribute

        void BuildConfirm()
        {
            confirm.SetVisible(false);
            var cardJson = req.Obj("card");
            float x = 0;
            if (cardJson != null)
            {
                var cd = CardData.From(cardJson);
                var tile = CardTile.Create(body, cd, 220, 308, null, c => { if (c != null) gc.Preview.Request(c, false); else gc.Preview.Hide(); });
                tile.rectTransform.anchorMin = tile.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                tile.rectTransform.anchoredPosition = new Vector2(-300, 0);
                x = 150;
            }
            var buttons = req.Arr("buttons");
            int def = req.Int("default");
            for (int i = 0; i < buttons.Count; i++)
            {
                int idx = i;
                var b = UIKit.Button("Choice", body, buttons[i].ToString(), i == def ? Theme.Accent : Theme.Secondary, () => Answer("index", idx), 22);
                var r = (RectTransform)b.transform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(420, 64);
                float y = (buttons.Count - 1) * 38 - i * 76;
                r.anchoredPosition = new Vector2(x, y);
            }
        }

        void BuildText()
        {
            textInput = UIKit.Input("Input", body, req.Bool("numeric") ? "Enter a number" : "Type here", false, 26);
            var r = textInput.GetComponent<RectTransform>();
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(500, 70);
            textInput.text = req.Str("initial");
            if (req.Bool("numeric")) textInput.contentType = InputField.ContentType.IntegerNumber;
            textInput.ActivateInputField();
            skip.SetVisible(true);
            UIKit.LabelOf(skip).text = "Cancel";
        }

        void BuildDistribute()
        {
            var options = req.Arr("options");
            amounts = new int[options.Count];
            int amount = req.Int("amount");
            // Start with everything on the first target, like Forge's default.
            if (amounts.Length > 0) amounts[0] = amount;
            var list = UIKit.Rect("List", body).Stretch();
            for (int i = 0; i < options.Count; i++)
            {
                var o = options[i] as Dictionary<string, object>;
                int idx = i;
                var row = UIKit.Panel("Row", list, Theme.PanelLight);
                var rr = row.rectTransform;
                rr.anchorMin = new Vector2(0.15f, 1); rr.anchorMax = new Vector2(0.85f, 1); rr.pivot = new Vector2(0.5f, 1);
                rr.sizeDelta = new Vector2(0, 56); rr.anchoredPosition = new Vector2(0, -i * 64);
                var name = UIKit.Label("Name", row.transform, o.Str("label"), 20, Theme.Text, TextAnchor.MiddleLeft);
                name.rectTransform.Stretch(16, 0, 300, 0);
                var minus = UIKit.Button("-", row.transform, "−", Theme.Secondary, () => Adjust(idx, -1), 26);
                Anchor((RectTransform)minus.transform, 1, 0.5f, -250, 0, 56, 44);
                var val = UIKit.Label("Value", row.transform, "", 26, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                var vr = val.rectTransform; vr.anchorMin = vr.anchorMax = new Vector2(1, 0.5f); vr.sizeDelta = new Vector2(80, 44); vr.anchoredPosition = new Vector2(-170, 0);
                var plus = UIKit.Button("+", row.transform, "+", Theme.Secondary, () => Adjust(idx, +1), 26);
                Anchor((RectTransform)plus.transform, 1, 0.5f, -90, 0, 56, 44);
                amountLabels.Add(val);
            }
            RefreshAmounts();
        }

        void Adjust(int idx, int delta)
        {
            var o = req.Arr("options")[idx] as Dictionary<string, object>;
            int cap = o.Int("max", req.Int("amount"));
            amounts[idx] = Mathf.Clamp(amounts[idx] + delta, 0, cap);
            RefreshAmounts();
        }

        void RefreshAmounts()
        {
            for (int i = 0; i < amounts.Length; i++) amountLabels[i].text = amounts[i].ToString();
            UpdateStatus();
        }

        // ------------------------------------------------------------------ name a card

        /// <summary>Search box over every card name; cards known in this game are listed before you type.</summary>
        void BuildName()
        {
            confirm.SetVisible(false);
            allNames = new List<string>();
            foreach (var n in req.Arr("names")) allNames.Add(n.ToString());
            suggestedNames = new List<string>();
            foreach (var n in req.Arr("suggested")) suggestedNames.Add(n.ToString());
            if (req.Int("min") == 0) { skip.SetVisible(true); UIKit.LabelOf(skip).text = "None"; }

            textInput = UIKit.Input("Search", body, "Type a card name...", false, 24);
            var ir = textInput.GetComponent<RectTransform>();
            ir.anchorMin = new Vector2(0.2f, 1); ir.anchorMax = new Vector2(0.8f, 1); ir.pivot = new Vector2(0.5f, 1);
            ir.sizeDelta = new Vector2(0, 58); ir.anchoredPosition = Vector2.zero;
            textInput.onValueChanged.AddListener(_ => RefreshNames());
            textInput.ActivateInputField();

            var scroll = UIKit.Rect("Scroll", body).Stretch(0, 0, 0, 70);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            scroll.gameObject.AddComponent<RectMask2D>();
            nameList = UIKit.Rect("List", scroll);
            nameList.anchorMin = new Vector2(0, 1); nameList.anchorMax = new Vector2(1, 1); nameList.pivot = new Vector2(0.5f, 1);
            var grid = nameList.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(400, 46);
            grid.spacing = new Vector2(10, 8);
            grid.childAlignment = TextAnchor.UpperCenter;
            nameList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = nameList; sr.horizontal = false; sr.scrollSensitivity = 40;
            UIKit.AddScrollbar(sr);
            RefreshNames();
        }

        void RefreshNames()
        {
            if (nameList == null) return;
            foreach (Transform c in nameList) Destroy(c.gameObject);
            string q = textInput != null ? textInput.text.Trim() : "";
            List<string> shown;
            if (q.Length == 0)
            {
                shown = suggestedNames;
                status.text = suggestedNames.Count > 0
                    ? "Known in this game (your deck and cards you've seen). Type to search all " + allNames.Count + " names."
                    : "Type to search all " + allNames.Count + " card names.";
            }
            else
            {
                // Known cards first, then names starting with the query, then names containing it.
                string ql = q.ToLowerInvariant();
                var known = new List<string>(); var starts = new List<string>(); var contains = new List<string>();
                var knownSet = new HashSet<string>(suggestedNames);
                foreach (var n in allNames)
                {
                    string nl = n.ToLowerInvariant();
                    int at = nl.IndexOf(ql, System.StringComparison.Ordinal);
                    if (at < 0) continue;
                    if (knownSet.Contains(n)) known.Add(n);
                    else if (at == 0) starts.Add(n);
                    else contains.Add(n);
                    if (starts.Count + contains.Count > 300) break;
                }
                shown = new List<string>(known);
                shown.AddRange(starts);
                shown.AddRange(contains);
                if (shown.Count > 60) shown = shown.GetRange(0, 60);
                status.text = shown.Count == 0 ? "No card named like \"" + q + "\"." : "Click a name to choose it.";
            }

            var knownNames = new HashSet<string>(suggestedNames);
            foreach (var n in shown)
            {
                string name = n;
                var b = UIKit.Button("Name", nameList, name, knownNames.Contains(name) ? new Color(0.30f, 0.24f, 0.12f) : Theme.PanelLight,
                    () => { gc.Preview.Hide(); Respond(new Dictionary<string, object> { { "name", name } }); }, 17);
                var pe = b.gameObject.AddComponent<PointerEvents>();
                pe.OnEnter = () => gc.Preview.Request(new CardData { Name = name }, false);
                pe.OnExit = () => gc.Preview.Hide();
            }
        }

        // ------------------------------------------------------------------ answers

        void Submit()
        {
            string kind = req.Str("kind");
            switch (kind)
            {
                case "choose":
                case "order":
                    var list = new List<object>();
                    foreach (var i in picked) list.Add(i);
                    Respond(new Dictionary<string, object> { { "indices", list } });
                    break;
                case "text":
                    Respond(new Dictionary<string, object> { { "text", textInput != null ? textInput.text : "" } });
                    break;
                case "distribute":
                    var a = new List<object>();
                    foreach (var v in amounts) a.Add(v);
                    Respond(new Dictionary<string, object> { { "amounts", a } });
                    break;
                default:
                    Respond(new Dictionary<string, object>());
                    break;
            }
        }

        void SubmitNone()
        {
            string kind = req.Str("kind");
            if (kind == "text") { Respond(new Dictionary<string, object> { { "text", null } }); return; }
            if (kind == "name") { Respond(new Dictionary<string, object> { { "name", null } }); return; }
            if (kind == "order")
            {
                // Keep the given order.
                var list = new List<object>();
                for (int i = 0; i < req.Arr("options").Count; i++) list.Add(i);
                Respond(new Dictionary<string, object> { { "indices", list } });
                return;
            }
            Respond(new Dictionary<string, object> { { "indices", new List<object>() } });
        }

        void Answer(string key, object value) => Respond(new Dictionary<string, object> { { key, value } });

        void Respond(Dictionary<string, object> fields)
        {
            if (req == null) return;
            var msg = new Dictionary<string, object> { { "t", "response" }, { "id", req.Int("id") } };
            foreach (var kv in fields) msg[kv.Key] = kv.Value;
            gc.Bridge.Send(msg);
            Close();
        }

        public void Close()
        {
            req = null;
            gc.Preview.Hide();
            SetPeek(false);
            gameObject.SetActive(false);
        }

        void TogglePeek() => SetPeek(!peeking);

        void SetPeek(bool on)
        {
            peeking = on;
            group.alpha = on ? 0f : 1f;
            group.blocksRaycasts = !on;
            peek.gameObject.SetActive(on && req != null);
            if (on) peek.transform.SetAsLastSibling();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Right-click menu: a list of actions at the pointer. Closes on a choice, a click elsewhere, or Esc.
    /// Entries with a null action are drawn as section headers.
    /// </summary>
    public class PopupMenu : MonoBehaviour
    {
        public static PopupMenu Instance { get; private set; }
        RectTransform panel, list;
        readonly List<GameObject> rows = new List<GameObject>();

        public bool IsOpen => gameObject.activeSelf;

        public static PopupMenu Create(RectTransform parent)
        {
            var shade = UIKit.Panel("PopupMenu", parent, Color.clear, false);
            shade.rectTransform.Stretch();
            var m = shade.gameObject.AddComponent<PopupMenu>();
            Instance = m;
            var pe = shade.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = m.Close;
            pe.OnRightClick = m.Close;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.08f, 0.09f, 0.13f, 0.98f));
            m.panel = p.rectTransform;
            m.panel.anchorMin = m.panel.anchorMax = new Vector2(0, 0);
            m.panel.pivot = new Vector2(0, 1);
            p.gameObject.AddComponent<PointerEvents>();
            var rim = p.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(1f, 0.75f, 0.4f, 0.35f); rim.effectDistance = new Vector2(1, -1);
            m.list = UIKit.Rect("List", p.transform).Stretch(6, 6, 6, 6);
            shade.gameObject.SetActive(false);
            return m;
        }

        /// <summary>Opens at a screen position with (label, action) entries.</summary>
        public void Show(Vector2 screenPos, List<(string label, Action action)> items)
        {
            foreach (var r in rows) Destroy(r);
            rows.Clear();
            const float rowH = 36, width = 320;
            float y = 0;
            foreach (var (label, action) in items)
            {
                if (action == null)
                {
                    var h = UIKit.Label("Header", list, label, 13, new Color(1f, 0.72f, 0.4f), TextAnchor.MiddleLeft, FontStyle.Bold);
                    Place(h.rectTransform, y, 26);
                    rows.Add(h.gameObject);
                    y += 26;
                    continue;
                }
                var a = action;
                // "Label\tKey": the key (its shortcut) is shown faintly on the right.
                string key = null, text = label;
                int tab = text.IndexOf('\t');
                if (tab >= 0) { key = text.Substring(tab + 1).Trim(); text = text.Substring(0, tab).TrimEnd(); }
                var b = UIKit.Button(text, list, text, new Color(1, 1, 1, 0.04f), () =>
                {
                    if (stays.Contains(a) && reopen != null)
                    {
                        // A "keep open" choice (add a counter, tap, roll again...): do it, then redraw the menu in place.
                        var again = reopen;
                        a();
                        if (!gameObject.activeSelf) return;   // the action closed it after all
                        quiet = true;
                        again();
                        quiet = false;
                    }
                    else { Close(); a(); }
                }, 16);
                var t = UIKit.LabelOf(b);
                t.alignment = TextAnchor.MiddleLeft;
                t.rectTransform.Stretch(12, 0, string.IsNullOrEmpty(key) ? 8 : 90, 0);
                if (!string.IsNullOrEmpty(key))
                {
                    var k = UIKit.Label("Key", b.transform, key, 14, new Color(1f, 1f, 1f, 0.42f), TextAnchor.MiddleRight, FontStyle.Bold);
                    k.rectTransform.Stretch(0, 0, 12, 0);
                }
                Place((RectTransform)b.transform, y, rowH - 2);
                rows.Add(b.gameObject);
                y += rowH;
            }
            panel.sizeDelta = new Vector2(width, y + 12);

            // Keep it on screen.
            var parentRt = (RectTransform)transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screenPos, null, out var local);
            local += parentRt.rect.size * 0.5f;   // to bottom-left origin
            float sx = Mathf.Min(local.x, parentRt.rect.width - width - 8);
            float sy = Mathf.Max(local.y, y + 20);
            panel.pivot = new Vector2(0, 1);
            panel.localScale = Vector3.one;
            panel.anchoredPosition = new Vector2(sx, sy);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (!quiet) FadeIn.Play(panel.gameObject, Motion.Micro, 0.96f);
        }

        /// <summary>Opens next to a card (its right edge, or its left when there's no room), growing out of it.</summary>
        public void ShowBeside(RectTransform target, List<(string label, Action action)> items)
        {
            Show(Vector2.zero, items);
            var parentRt = (RectTransform)transform;
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);   // bl, tl, tr, br (rotation-aware)
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(null, c);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRt, screen, null, out var l);
                l += parentRt.rect.size * 0.5f;
                minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x); maxY = Mathf.Max(maxY, l.y);
            }
            float w = panel.sizeDelta.x, h = panel.sizeDelta.y;
            bool right = maxX + 10 + w <= parentRt.rect.width - 8;
            float x = right ? maxX + 10 : Mathf.Max(8, minX - 10 - w);
            float y = Mathf.Clamp(maxY, h + 8, parentRt.rect.height - 8);
            // Grow out of the card's side.
            panel.pivot = new Vector2(right ? 0 : 1, 1);
            panel.anchoredPosition = new Vector2(right ? x : x + w, y);
            if (!quiet) FadeIn.Play(panel.gameObject, Motion.Short, 0.82f);
        }

        static void Place(RectTransform r, float y, float h)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(0, h); r.anchoredPosition = new Vector2(0, -y);
        }

        public void Close()
        {
            gameObject.SetActive(false);
            stays.Clear();
            reopen = null;
        }

        // ---------------------------------------------------------------- choices that keep the menu open

        static readonly HashSet<Action> stays = new HashSet<Action>();
        Action reopen;
        bool quiet;

        /// <summary>Marks a menu action as "keep the menu open" (it runs, then the menu is rebuilt in place).</summary>
        public static Action Stay(Action a) { if (a != null) stays.Add(a); return a; }

        /// <summary>How to rebuild this menu after a keep-open choice (usually: call the same menu function again).</summary>
        public PopupMenu Reopen(Action rebuild) { reopen = rebuild; return this; }
    }
}

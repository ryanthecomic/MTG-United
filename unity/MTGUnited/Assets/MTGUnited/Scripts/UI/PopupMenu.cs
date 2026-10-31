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
            const float rowH = 36, width = 280;
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
                var b = UIKit.Button(label, list, label, new Color(1, 1, 1, 0.04f), () => { Close(); a(); }, 16);
                var t = UIKit.LabelOf(b);
                t.alignment = TextAnchor.MiddleLeft;
                t.rectTransform.Stretch(12, 0, 8, 0);
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
            panel.anchoredPosition = new Vector2(sx, sy);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(panel.gameObject, Motion.Micro, 0.96f);
        }

        static void Place(RectTransform r, float y, float h)
        {
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.sizeDelta = new Vector2(0, h); r.anchoredPosition = new Vector2(0, -y);
        }

        public void Close() => gameObject.SetActive(false);
    }
}

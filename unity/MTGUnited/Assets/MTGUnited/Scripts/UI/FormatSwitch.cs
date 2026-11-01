using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// A segmented switch (Commander | 60 cards | 40 cards): one dark track, a warm thumb that slides under the
    /// chosen segment, dim labels that brighten on hover. Segment widths follow their label lengths.
    /// </summary>
    public class FormatSwitch : MonoBehaviour
    {
        RectTransform thumb;
        readonly List<(string id, RectTransform rt, Text label)> segs = new List<(string, RectTransform, Text)>();
        string selected, hovered;
        float thumbX, thumbW, targetX, targetW;
        bool placed;

        static readonly Color LabelOn = new Color(0.12f, 0.07f, 0.02f);
        static readonly Color LabelOff = new Color(0.72f, 0.75f, 0.82f);
        static readonly Color LabelHover = new Color(0.95f, 0.96f, 1f);

        public static FormatSwitch Create(Transform parent, Rect r, (string id, string label)[] items, Action<string> picked)
        {
            var track = UIKit.Panel("FormatSwitch", parent, new Color(0f, 0f, 0f, 0.42f));
            track.rectTransform.Place(r.x, r.y, r.width, r.height);
            track.pixelsPerUnitMultiplier = 2.2f;
            var rim = track.gameObject.AddComponent<Outline>();
            rim.effectColor = new Color(1f, 1f, 1f, 0.07f); rim.effectDistance = new Vector2(1, -1);
            var sw = track.gameObject.AddComponent<FormatSwitch>();

            var th = UIKit.Panel("Thumb", track.transform, Theme.Accent);
            th.raycastTarget = false;
            th.pixelsPerUnitMultiplier = 2.6f;
            sw.thumb = th.rectTransform;
            sw.thumb.anchorMin = new Vector2(0, 0); sw.thumb.anchorMax = new Vector2(0, 1); sw.thumb.pivot = new Vector2(0, 0.5f);
            var glow = th.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0f, 0f, 0f, 0.35f); glow.effectDistance = new Vector2(0, -2);

            // Widths by label length, with a floor so short labels still read as buttons.
            float inner = r.width - 6f, total = 0f;
            var weights = new float[items.Length];
            for (int i = 0; i < items.Length; i++) { weights[i] = Mathf.Max(5f, items[i].label.Length) + 3f; total += weights[i]; }
            float x = 3f;
            for (int i = 0; i < items.Length; i++)
            {
                string id = items[i].id;
                float w = inner * weights[i] / total;
                var seg = UIKit.Rect("Seg " + id, track.transform);
                seg.anchorMin = new Vector2(0, 0); seg.anchorMax = new Vector2(0, 1); seg.pivot = new Vector2(0, 0.5f);
                seg.anchoredPosition = new Vector2(x, 0); seg.sizeDelta = new Vector2(w, -6);
                seg.gameObject.AddComponent<Image>().color = Color.clear;
                var pe = seg.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = () => { sw.Select(id); picked?.Invoke(id); };
                pe.OnEnter = () => { sw.hovered = id; sw.Paint(); };
                pe.OnExit = () => { if (sw.hovered == id) sw.hovered = null; sw.Paint(); };
                var t = UIKit.Label("Label", seg, items[i].label, 15, LabelOff, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Stretch();
                t.raycastTarget = false;
                sw.segs.Add((id, seg, t));
                x += w;
            }
            return sw;
        }

        public void Select(string id)
        {
            selected = id;
            foreach (var s in segs)
                if (s.id == id) { targetX = s.rt.anchoredPosition.x; targetW = s.rt.sizeDelta.x; }
            if (!placed || !gameObject.activeInHierarchy) { thumbX = targetX; thumbW = targetW; placed = true; Apply(); }
            Paint();
        }

        void Paint()
        {
            foreach (var s in segs)
                s.label.color = s.id == selected ? LabelOn : s.id == hovered ? LabelHover : LabelOff;
        }

        void Apply()
        {
            thumb.anchoredPosition = new Vector2(thumbX, 0);
            thumb.sizeDelta = new Vector2(thumbW, -6);
        }

        void Update()
        {
            if (!placed || (Mathf.Approximately(thumbX, targetX) && Mathf.Approximately(thumbW, targetW))) return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 16f);
            thumbX = Mathf.Lerp(thumbX, targetX, k); thumbW = Mathf.Lerp(thumbW, targetW, k);
            if (Mathf.Abs(thumbX - targetX) < 0.3f && Mathf.Abs(thumbW - targetW) < 0.3f) { thumbX = targetX; thumbW = targetW; }
            Apply();
        }
    }
}

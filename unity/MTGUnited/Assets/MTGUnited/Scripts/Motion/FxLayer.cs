using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Board-level effects drawn above the cards: floating damage / life numbers and combat lines
    /// (faint attack arrows to defenders, solid connectors from blockers to attackers).
    /// Objects are pooled so combat doesn't allocate new UI every frame.
    /// </summary>
    public class FxLayer : MonoBehaviour
    {
        RectTransform root;

        class FloatNumber { public Text text; public float start; public Vector2 pos; public Color color; }
        readonly List<FloatNumber> numbers = new List<FloatNumber>();
        readonly Stack<Text> numberPool = new Stack<Text>();

        class Line { public RectTransform body; public Image bodyImg; public RectTransform head; public Image headImg; }
        readonly List<Line> lines = new List<Line>();
        int linesUsed;

        public static FxLayer Create(Transform parent)
        {
            var rt = UIKit.Rect("Fx", parent).Stretch();
            var fx = rt.gameObject.AddComponent<FxLayer>();
            fx.root = rt;
            return fx;
        }

        // ------------------------------------------------------------------ floating numbers

        /// <summary>
        /// Damage / life number: pops 1.4 -> 1 (micro), drifts up and fades, all within the 400 ms budget.
        /// ≥ 28 px with a dark outline for readability.
        /// </summary>
        public void Number(Vector2 boardPos, string text, Color color)
        {
            Text t = numberPool.Count > 0 ? numberPool.Pop() : MakeNumber();
            t.gameObject.SetActive(true);
            t.transform.SetAsLastSibling();
            t.text = text;
            t.color = color;
            numbers.Add(new FloatNumber { text = t, start = Motion.Now, pos = boardPos, color = color });
        }

        Text MakeNumber()
        {
            var t = UIKit.Label("Number", root, "", 34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).Outline(2.5f);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
            t.rectTransform.sizeDelta = new Vector2(160, 50);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        // ------------------------------------------------------------------ lines

        /// <summary>Call once per frame-update batch: BeginLines, any number of AddLine, EndLines.</summary>
        public void BeginLines() { linesUsed = 0; }

        public void AddLine(Vector2 from, Vector2 to, Color color, float width, bool arrowHead)
        {
            if (linesUsed >= lines.Count) lines.Add(MakeLine());
            var l = lines[linesUsed++];
            Vector2 d = to - from;
            float len = d.magnitude;
            if (len < 1f) { l.body.gameObject.SetActive(false); l.head.gameObject.SetActive(false); return; }
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            l.body.gameObject.SetActive(true);
            l.body.anchoredPosition = (from + to) * 0.5f;
            l.body.sizeDelta = new Vector2(len, width);
            l.body.localEulerAngles = new Vector3(0, 0, ang);
            l.bodyImg.color = color;
            l.head.gameObject.SetActive(arrowHead);
            if (arrowHead)
            {
                l.head.anchoredPosition = to;
                l.head.sizeDelta = new Vector2(width * 4f, width * 4f);
                l.head.localEulerAngles = new Vector3(0, 0, ang + 45f);
                l.headImg.color = color;
            }
        }

        public void EndLines()
        {
            for (int i = linesUsed; i < lines.Count; i++)
            {
                lines[i].body.gameObject.SetActive(false);
                lines[i].head.gameObject.SetActive(false);
            }
        }

        Line MakeLine()
        {
            var body = UIKit.Rect("Line", root);
            body.anchorMin = body.anchorMax = Vector2.zero;
            body.pivot = new Vector2(0.5f, 0.5f);
            var bimg = body.gameObject.AddComponent<Image>();
            bimg.sprite = UIKit.White;
            bimg.raycastTarget = false;
            var head = UIKit.Rect("Head", root);
            head.anchorMin = head.anchorMax = Vector2.zero;
            head.pivot = new Vector2(0.5f, 0.5f);
            var himg = head.gameObject.AddComponent<Image>();
            himg.sprite = UIKit.White;
            himg.raycastTarget = false;
            body.SetAsFirstSibling(); head.SetAsFirstSibling();   // lines under numbers
            return new Line { body = body, bodyImg = bimg, head = head, headImg = himg };
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            float now = Motion.Now;
            const float life = 0.7f;   // readable: pop (micro) + hold, fading over the last 40% (doc 10.3: hold >= 600 ms)
            for (int i = numbers.Count - 1; i >= 0; i--)
            {
                var n = numbers[i];
                float t = (now - n.start) / life;
                if (t >= 1f)
                {
                    n.text.gameObject.SetActive(false);
                    numberPool.Push(n.text);
                    numbers.RemoveAt(i);
                    continue;
                }
                float pop = Mathf.Lerp(1.4f, 1f, Easing.Eval(Ease.Out, Mathf.Clamp01((now - n.start) / Motion.Micro)));
                n.text.rectTransform.localScale = Vector3.one * pop;
                n.text.rectTransform.anchoredPosition = n.pos + new Vector2(0, 24f * Easing.Eval(Ease.Out, t));
                var c = n.color; c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                n.text.color = c;
            }
        }
    }
}

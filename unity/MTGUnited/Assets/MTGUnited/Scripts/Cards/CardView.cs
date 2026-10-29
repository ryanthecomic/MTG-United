using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    public enum Highlight { None, Playable, Selectable, Chosen, Attacking, Blocking }

    /// <summary>
    /// One card on screen. Cards glide to the position the board layout gives them, so zone changes, tapping and
    /// re-sorting animate smoothly instead of popping.
    /// </summary>
    public class CardView : MonoBehaviour
    {
        public int CardId;
        public CardData Data { get; private set; }

        public Vector2 TargetPos;
        public Vector2 TargetSize = new Vector2(100, 140);
        public float TargetRotation;
        public float TargetScale = 1f;
        public bool Hovered;
        public bool HoverLift = true;     // hand cards pop up when hovered
        public Action<CardView> Clicked, RightClicked, HoverStart, HoverEnd;

        RectTransform rt;
        Image glow;
        Image frame;
        RawImage art;
        Image back;
        Text nameText, typeText, bodyText, costText;
        Image ptBadge; Text ptText;
        Image counterBadge; Text counterText;
        Image tagBadge; Text tagText;
        Image dim;
        Image shadow;
        float lastWidth = -1;
        string imageKeyShown;
        bool initialized;
        float glowPulse;
        Highlight highlight;

        public RectTransform Rect => rt;

        public static CardView Create(Transform parent, int id)
        {
            var root = UIKit.Rect("Card " + id, parent);
            var cv = root.gameObject.AddComponent<CardView>();
            cv.CardId = id;
            cv.Build();
            return cv;
        }

        void Build()
        {
            rt = (RectTransform)transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            shadow = UIKit.AddShadow(rt, 14, new Vector2(3, -7), 0.65f);

            glow = UIKit.Panel("Glow", rt, Color.clear);
            glow.rectTransform.Stretch(-7, -7, -7, -7);
            glow.raycastTarget = false;

            // The frame's rounded shape masks everything inside it, so card art gets real rounded corners.
            frame = UIKit.Panel("Frame", rt, new Color(0.2f, 0.2f, 0.22f));
            frame.rectTransform.Stretch();
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            // Raycast target for clicks/hover.
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => Clicked?.Invoke(this);
            pe.OnRightClick = () => RightClicked?.Invoke(this);
            pe.OnEnter = () => { Hovered = true; HoverStart?.Invoke(this); };
            pe.OnExit = () => { Hovered = false; HoverEnd?.Invoke(this); };

            // Text fallback, visible until (or unless) the real image arrives.
            nameText = UIKit.Label("Name", frame.transform, "", 12, Color.black, TextAnchor.UpperLeft, FontStyle.Bold);
            nameText.rectTransform.anchorMin = new Vector2(0, 1); nameText.rectTransform.anchorMax = new Vector2(1, 1);
            nameText.rectTransform.pivot = new Vector2(0.5f, 1);
            nameText.rectTransform.offsetMin = new Vector2(6, -26); nameText.rectTransform.offsetMax = new Vector2(-6, -4);
            nameText.resizeTextForBestFit = true; nameText.resizeTextMinSize = 6; nameText.resizeTextMaxSize = 14;

            costText = UIKit.Label("Cost", frame.transform, "", 11, Color.black, TextAnchor.UpperRight, FontStyle.Bold);
            costText.rectTransform.anchorMin = new Vector2(0, 1); costText.rectTransform.anchorMax = new Vector2(1, 1);
            costText.rectTransform.pivot = new Vector2(0.5f, 1);
            costText.rectTransform.offsetMin = new Vector2(6, -40); costText.rectTransform.offsetMax = new Vector2(-6, -24);

            typeText = UIKit.Label("Type", frame.transform, "", 10, new Color(0.1f, 0.1f, 0.1f), TextAnchor.MiddleLeft, FontStyle.Italic);
            typeText.rectTransform.anchorMin = new Vector2(0, 0.45f); typeText.rectTransform.anchorMax = new Vector2(1, 0.55f);
            typeText.rectTransform.offsetMin = new Vector2(6, 0); typeText.rectTransform.offsetMax = new Vector2(-6, 0);
            typeText.resizeTextForBestFit = true; typeText.resizeTextMinSize = 5; typeText.resizeTextMaxSize = 11;

            bodyText = UIKit.Label("Body", frame.transform, "", 9, new Color(0.08f, 0.08f, 0.08f), TextAnchor.UpperLeft);
            bodyText.rectTransform.anchorMin = new Vector2(0, 0.08f); bodyText.rectTransform.anchorMax = new Vector2(1, 0.44f);
            bodyText.rectTransform.offsetMin = new Vector2(6, 0); bodyText.rectTransform.offsetMax = new Vector2(-6, 0);
            bodyText.resizeTextForBestFit = true; bodyText.resizeTextMinSize = 4; bodyText.resizeTextMaxSize = 11;

            var artRt = UIKit.Rect("Art", frame.transform);
            artRt.Stretch(2, 2, 2, 2);
            art = artRt.gameObject.AddComponent<RawImage>();
            art.raycastTarget = false;
            art.enabled = false;

            back = UIKit.Panel("Back", frame.transform, new Color(0.13f, 0.11f, 0.20f));
            back.rectTransform.Stretch();
            back.raycastTarget = false;
            var inner = UIKit.Panel("Inner", back.transform, new Color(0.30f, 0.20f, 0.42f));
            inner.rectTransform.Stretch(6, 6, 6, 6);
            inner.raycastTarget = false;
            var emblem = UIKit.Label("Emblem", inner.transform, "MTG\nUNITED", 14, new Color(1f, 0.85f, 0.55f, 0.9f), TextAnchor.MiddleCenter, FontStyle.Bold);
            emblem.rectTransform.Stretch();
            emblem.resizeTextForBestFit = true; emblem.resizeTextMinSize = 6; emblem.resizeTextMaxSize = 30;
            if (GameArt.CardBack != null)
            {
                // Real card back from Resources/Visuals replaces the generated one.
                inner.gameObject.SetActive(false);
                var backArt = UIKit.Rect("BackArt", back.transform).Stretch();
                var raw = backArt.gameObject.AddComponent<RawImage>();
                raw.texture = GameArt.CardBack;
                raw.raycastTarget = false;
            }
            back.gameObject.SetActive(false);

            dim = UIKit.Panel("Dim", frame.transform, new Color(0, 0, 0, 0.35f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = false;
            dim.gameObject.SetActive(false);

            ptBadge = UIKit.Panel("PT", rt, new Color(0.08f, 0.08f, 0.1f, 0.92f));
            ptBadge.raycastTarget = false;
            var ptr = ptBadge.rectTransform;
            ptr.anchorMin = ptr.anchorMax = new Vector2(1, 0); ptr.pivot = new Vector2(1, 0);
            ptr.sizeDelta = new Vector2(46, 22); ptr.anchoredPosition = new Vector2(2, -2);
            ptText = UIKit.Label("Text", ptBadge.transform, "", 15, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            ptText.rectTransform.Stretch();

            counterBadge = UIKit.Panel("Counters", rt, new Color(0.15f, 0.45f, 0.25f, 0.95f));
            counterBadge.raycastTarget = false;
            var cr = counterBadge.rectTransform;
            cr.anchorMin = cr.anchorMax = new Vector2(0, 1); cr.pivot = new Vector2(0, 1);
            cr.sizeDelta = new Vector2(60, 20); cr.anchoredPosition = new Vector2(-3, 3);
            counterText = UIKit.Label("Text", counterBadge.transform, "", 12, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            counterText.rectTransform.Stretch(2, 0, 2, 0);
            counterText.resizeTextForBestFit = true; counterText.resizeTextMinSize = 6; counterText.resizeTextMaxSize = 13;

            tagBadge = UIKit.Panel("Tag", rt, new Color(0.6f, 0.12f, 0.12f, 0.95f));
            tagBadge.raycastTarget = false;
            var tr = tagBadge.rectTransform;
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 0);
            tr.sizeDelta = new Vector2(0, 18); tr.anchoredPosition = new Vector2(0, 2);
            tagText = UIKit.Label("Text", tagBadge.transform, "", 12, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            tagText.rectTransform.Stretch(2, 0, 2, 0);
            tagText.resizeTextForBestFit = true; tagText.resizeTextMinSize = 6; tagText.resizeTextMaxSize = 12;
        }

        public void SetData(CardData d)
        {
            Data = d;
            bool hidden = d == null || d.Hidden || (d.FaceDown && string.IsNullOrEmpty(d.ImageCacheKey));
            back.gameObject.SetActive(hidden);

            if (!hidden)
            {
                frame.color = Theme.FrameFor(d);
                nameText.text = d.Name;
                costText.text = ManaText(d.ManaCost);
                typeText.text = d.TypeLine;
                bodyText.text = d.Text;
                string key = d.ImageCacheKey;
                if (key != imageKeyShown)
                {
                    imageKeyShown = key;
                    art.enabled = false;
                    if (!d.Token && CardImageCache.Instance != null)
                    {
                        CardImageCache.Instance.Get(d.Name, d.Set, tex =>
                        {
                            if (this == null || imageKeyShown != key) return;
                            art.texture = tex;
                            art.enabled = true;
                        });
                    }
                }
            }
            else
            {
                art.enabled = false;
                imageKeyShown = null;
            }

            bool onField = d != null && d.Zone == "Battlefield";
            // Live power/toughness (images show printed values) for creatures on the battlefield.
            bool showPT = onField && !hidden && d.HasPT && d.IsCreature;
            ptBadge.SetVisible(showPT || (onField && !hidden && (d.IsPlaneswalker || d.IsBattle)));
            if (showPT)
            {
                int toughLeft = d.Toughness - d.Damage;
                string t = d.Damage > 0 ? "<color=#ff7a7a>" + toughLeft + "</color>" : d.Toughness.ToString();
                ptText.text = d.Power + "/" + t;
                ptBadge.color = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            }
            else if (onField && !hidden && d.IsPlaneswalker)
            {
                ptText.text = d.Loyalty ?? "";
                ptBadge.color = new Color(0.2f, 0.2f, 0.35f, 0.95f);
            }
            else if (onField && !hidden && d.IsBattle)
            {
                ptText.text = d.Defense ?? "";
                ptBadge.color = new Color(0.4f, 0.2f, 0.1f, 0.95f);
            }

            var sb = new StringBuilder();
            if (d != null)
            {
                foreach (var kv in d.Counters)
                {
                    if (d.IsPlaneswalker && kv.Key.ToUpperInvariant().Contains("LOYALTY")) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(ShortCounter(kv.Key, kv.Value));
                }
            }
            counterBadge.SetVisible(sb.Length > 0);
            counterText.text = sb.ToString();

            dim.SetVisible(d != null && onField && d.PhasedOut);
        }

        public void SetTag(string text, Color color)
        {
            tagBadge.SetVisible(!string.IsNullOrEmpty(text));
            tagText.text = text ?? "";
            tagBadge.color = color;
        }

        public void SetHighlight(Highlight h)
        {
            highlight = h;
        }

        public void SnapToTarget()
        {
            rt.anchoredPosition = TargetPos;
            rt.sizeDelta = TargetSize;
            rt.localEulerAngles = new Vector3(0, 0, TargetRotation);
            rt.localScale = Vector3.one * TargetScale;
            initialized = true;
        }

        void Update()
        {
            if (!initialized) SnapToTarget();
            float k = 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime);
            Vector2 pos = TargetPos;
            float scale = TargetScale;
            // Lift stays under 0.125 of the height so the enlarged card still covers the pointer (no hover flicker).
            if (Hovered && HoverLift) { pos.y += TargetSize.y * 0.12f; scale *= 1.25f; }
            rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, pos, k);
            rt.sizeDelta = Vector2.Lerp(rt.sizeDelta, TargetSize, k);
            float z = Mathf.LerpAngle(rt.localEulerAngles.z, TargetRotation, k);
            rt.localEulerAngles = new Vector3(0, 0, z);
            rt.localScale = Vector3.Lerp(rt.localScale, Vector3.one * scale, k);
            UpdateCorners();

            glowPulse += Time.unscaledDeltaTime * 3f;
            Color gc;
            switch (highlight)
            {
                case Highlight.Playable: gc = Theme.Playable; break;
                case Highlight.Selectable: gc = Theme.Selectable; break;
                case Highlight.Chosen: gc = Theme.Chosen; break;
                case Highlight.Attacking: gc = Theme.Attack; break;
                case Highlight.Blocking: gc = Theme.Block; break;
                default: gc = Color.clear; break;
            }
            if (highlight == Highlight.Selectable || highlight == Highlight.Playable)
                gc.a = 0.55f + 0.35f * Mathf.Sin(glowPulse);
            glow.color = gc;
        }

        void UpdateCorners()
        {
            float w = rt.sizeDelta.x;
            if (Mathf.Abs(w - lastWidth) < 0.5f) return;
            lastWidth = w;
            // Rounded sprite corners are 16 px; scale them to about 5% of the card width.
            float mult = Mathf.Max(0.3f, 16f / Mathf.Max(1f, w * 0.055f));
            frame.pixelsPerUnitMultiplier = mult;
            back.pixelsPerUnitMultiplier = mult;
            glow.pixelsPerUnitMultiplier = mult * 0.6f;
            frame.SetVerticesDirty();
        }

        static string ShortCounter(string name, int n)
        {
            string u = name.ToUpperInvariant();
            if (u == "P1P1" || name == "+1/+1") return "+" + n + "/+" + n;
            if (u == "M1M1" || name == "-1/-1") return "-" + n + "/-" + n;
            string label = name.Length > 6 ? name.Substring(0, 6) : name;
            return n + " " + label;
        }

        /// <summary>Forge short mana strings look like "{2}{W}{U}" or "2 W U"; keep them compact.</summary>
        public static string ManaText(string cost)
        {
            if (string.IsNullOrEmpty(cost) || cost == "no cost") return "";
            return cost.Replace("{", "").Replace("}", "").Replace(" ", "");
        }
    }
}

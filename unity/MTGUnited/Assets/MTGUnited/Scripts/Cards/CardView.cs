using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>One highlight per card, chosen by priority: Selected > Eligible > Attacking/Blocking > Playable > Idle.</summary>
    public enum Highlight { None, Playable, Eligible, Selected, Attacking, Blocking }

    /// <summary>How a card gets to its new spot (design doc section 3).</summary>
    public enum MoveKind
    {
        Reflow,   // within a zone: base, ease.out
        Travel,   // between zones: travel, ease.inOut, arcs
        Land,     // onto the battlefield: travel, back-out overshoot, arcs
        Snap,     // no animation
    }

    /// <summary>How a card leaves the screen for a zone we don't draw (design doc section 3).</summary>
    public enum ExitKind { Die, Exile, ToStack, ToLibrary, Discard, Fade }

    /// <summary>
    /// One card on screen. Position, size, rotation and scale are separate tweens, so a card can arc to a new zone
    /// while it untaps, and any new board state simply retargets from wherever the card is (interruptible).
    /// </summary>
    public class CardView : MonoBehaviour
    {
        public int CardId;
        public CardData Data { get; private set; }

        public bool Hovered;
        public bool HoverLift = true;
        public bool IsExiting { get; private set; }
        public Action<CardView> Clicked, RightClicked, HoverStart, HoverEnd;

        // ----- dragging (hand cards): BoardView decides what a drag means
        public Action<CardView, Vector2> DragBegan, DragMoved, DragEnded;   // screen positions
        /// <summary>While set, the card follows the pointer at this board position.</summary>
        public bool Dragging;
        public Vector2 DragBoardPos;

        // ----- layout targets (what BoardView last asked for)
        public Vector2 TargetPos { get; private set; }
        public Vector2 TargetSize { get; private set; } = new Vector2(100, 140);
        public float TargetRotation { get; private set; }

        // ----- motion channels
        TweenV2 pos, size, pose;          // pose = combat offset (attack forward, lunge)
        TweenF rot, hoverT, scalePulse, flipX, alpha, flash, badgePop, ptFlash;
        Vector2 arcControl; bool arcActive; float arcTilt;
        float shakeUntil, shakeAmp;
        bool initialized;
        Color flashColor = Color.white;
        Color ptFlashColor;
        Color ptBase = new Color(0.08f, 0.08f, 0.1f, 0.92f);
        bool flipPending; bool flipShowFront;
        Action exitDone;
        float exitEnd;

        // ----- visuals
        RectTransform rt;
        CanvasGroup group;
        Image glow, frame, back, dim, shadow, flashImg, badge;
        RawImage art;
        Text nameText, typeText, bodyText, costText;
        Image ptBadge; Text ptText;
        Image counterBadge; Text counterText;
        Image tagBadge; Text tagText;
        Image backArtHolder;
        float lastWidth = -1;
        string imageKeyShown;
        Highlight highlight;
        bool ineligible;
        bool flying;
        string lastCounters = "";
        int lastPower = int.MinValue, lastToughness = int.MinValue;

        public RectTransform Rect => rt;
        public Vector2 CurrentPos => rt.anchoredPosition;

        static readonly Color PlayableColor = Hex("3FD0E8");
        static readonly Color EligibleColor = Hex("F5A623");
        static readonly Color SelectedColor = Hex("FFD54A");
        static readonly Color AttackColor = Hex("E4572E");
        static readonly Color BlockColor = Hex("4A7FC1");

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

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
            group = gameObject.AddComponent<CanvasGroup>();

            shadow = UIKit.AddShadow(rt, 14, new Vector2(3, -7), 0.65f);

            glow = UIKit.Panel("Glow", rt, Color.clear);
            glow.rectTransform.Stretch(-5, -5, -5, -5);
            glow.raycastTarget = false;

            frame = UIKit.Panel("Frame", rt, new Color(0.2f, 0.2f, 0.22f));
            frame.rectTransform.Stretch();
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var pe = frame.gameObject.AddComponent<PointerEvents>();
            pe.OnClick = () => { if (!IsExiting) Clicked?.Invoke(this); };
            pe.OnRightClick = () => { if (!IsExiting) RightClicked?.Invoke(this); };
            pe.OnEnter = () => { if (IsExiting) return; Hovered = true; HoverStart?.Invoke(this); };
            pe.OnExit = () => { Hovered = false; HoverEnd?.Invoke(this); };
            frame.gameObject.AddComponent<CardDragHandler>().Owner = this;

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
                inner.gameObject.SetActive(false);
                var backArt = UIKit.Rect("BackArt", back.transform).Stretch();
                var raw = backArt.gameObject.AddComponent<RawImage>();
                raw.texture = GameArt.CardBack;
                raw.raycastTarget = false;
            }
            back.gameObject.SetActive(false);

            dim = UIKit.Panel("Dim", frame.transform, new Color(0, 0, 0, 0.4f));
            dim.rectTransform.Stretch();
            dim.raycastTarget = false;
            dim.gameObject.SetActive(false);

            // Impact flash / white wash, confined to the card's own bounds (flash safety).
            flashImg = UIKit.Panel("Flash", frame.transform, new Color(1, 1, 1, 0));
            flashImg.rectTransform.Stretch();
            flashImg.raycastTarget = false;

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

            tagBadge = UIKit.Panel("Tag", rt, new Color(0.25f, 0.25f, 0.3f, 0.9f));
            tagBadge.raycastTarget = false;
            var tr = tagBadge.rectTransform;
            tr.anchorMin = new Vector2(0, 1); tr.anchorMax = new Vector2(1, 1); tr.pivot = new Vector2(0.5f, 0);
            tr.sizeDelta = new Vector2(0, 18); tr.anchoredPosition = new Vector2(0, 2);
            tagText = UIKit.Label("Text", tagBadge.transform, "", 12, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            tagText.rectTransform.Stretch(2, 0, 2, 0);
            tagText.resizeTextForBestFit = true; tagText.resizeTextMinSize = 6; tagText.resizeTextMaxSize = 12;
            tagBadge.gameObject.SetActive(false);

            // State badge (check / sword / shield) in the top-right corner.
            var badgeBg = UIKit.Panel("StateBadge", rt, new Color(0.06f, 0.06f, 0.08f, 0.92f));
            badgeBg.raycastTarget = false;
            badgeBg.pixelsPerUnitMultiplier = 0.5f;
            var br = badgeBg.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(1, 1); br.pivot = new Vector2(0.5f, 0.5f);
            br.sizeDelta = new Vector2(28, 28); br.anchoredPosition = new Vector2(-4, -4);
            badge = UIKit.Rect("Icon", badgeBg.transform).Stretch(4, 4, 4, 4).gameObject.AddComponent<Image>();
            badge.raycastTarget = false;
            backArtHolder = badgeBg;
            badgeBg.gameObject.SetActive(false);

            rot.Snap(0); hoverT.Snap(0); scalePulse.Snap(0); flipX.Snap(1); alpha.Snap(1); flash.Snap(0);
            badgePop.Snap(1); ptFlash.Snap(0); pose.Snap(Vector2.zero);
        }

        // =================================================================================== data

        public void SetData(CardData d)
        {
            var prev = Data;
            Data = d;
            bool hidden = IsHidden(d);
            // During a reveal flip the face swaps at the half-way point, not now.
            if (!flipPending) back.gameObject.SetActive(hidden);
            else flipShowFront = !hidden;

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
                    if (CardImageCache.Instance != null)
                    {
                        CardImageCache.Instance.GetFor(d, tex =>
                        {
                            if (this == null || imageKeyShown != key) return;
                            art.texture = tex;
                            art.enabled = true;
                        });
                    }
                }
                // Transform / flip: same card, other face on the battlefield.
                if (prev != null && !IsHidden(prev) && prev.Name != d.Name && d.DoubleFaced && d.Zone == "Battlefield" && prev.Zone == "Battlefield")
                    Flip(false);
            }
            else
            {
                art.enabled = false;
                imageKeyShown = null;
            }

            bool onField = d != null && d.Zone == "Battlefield";
            bool showPT = onField && !hidden && d.HasPT && d.IsCreature;
            ptBadge.SetVisible(showPT || (onField && !hidden && (d.IsPlaneswalker || d.IsBattle)));
            if (showPT)
            {
                int toughLeft = d.Toughness - d.Damage;
                string t = d.Damage > 0 ? "<color=#ff7a7a>" + toughLeft + "</color>" : d.Toughness.ToString();
                ptText.text = d.Power + "/" + t;
                // P/T change: flash green (up) or red (down) — design doc section 3.
                if (lastPower != int.MinValue && (d.Power != lastPower || d.Toughness != lastToughness))
                {
                    int delta = (d.Power - lastPower) + (d.Toughness - lastToughness);
                    ptFlashColor = delta >= 0 ? new Color(0.25f, 0.85f, 0.35f) : new Color(0.9f, 0.25f, 0.25f);
                    ptFlash.From = 1; ptFlash.To = 0; ptFlash.Start = Motion.Now; ptFlash.Duration = Motion.Short + Motion.Micro; ptFlash.Curve = Ease.Out;
                    badgePop.From = 1.3f; badgePop.To = 1f; badgePop.Start = Motion.Now; badgePop.Duration = Motion.Short; badgePop.Curve = Ease.Out;
                }
                lastPower = d.Power; lastToughness = d.Toughness;
            }
            else if (onField && !hidden && d.IsPlaneswalker)
            {
                ptText.text = d.Loyalty ?? "";
            }
            else if (onField && !hidden && d.IsBattle)
            {
                ptText.text = d.Defense ?? "";
            }
            ptBase = showPT ? new Color(0.08f, 0.08f, 0.1f, 0.92f)
                : d != null && d.IsPlaneswalker ? new Color(0.2f, 0.2f, 0.35f, 0.95f) : new Color(0.4f, 0.2f, 0.1f, 0.95f);

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
            string counters = sb.ToString();
            if (counters != lastCounters && counters.Length > 0 && lastCounters != null)
            {
                // Counter added / removed: badge pops 1.4 -> 1 (micro).
                counterPop.From = 1.4f; counterPop.To = 1f; counterPop.Start = Motion.Now; counterPop.Duration = Motion.Micro + Motion.Micro; counterPop.Curve = Ease.Out;
            }
            lastCounters = counters;
            counterBadge.SetVisible(counters.Length > 0);
            counterText.text = counters;

            flying = d != null && d.HasKeyword("FLYING");
            UpdateDim(d);
            UpdateArtTint();
        }

        TweenF counterPop = new TweenF { From = 1, To = 1 };

        static bool IsHidden(CardData d) => d == null || d.Hidden || (d.FaceDown && string.IsNullOrEmpty(d.ImageCacheKey));

        void UpdateDim(CardData d)
        {
            bool phased = d != null && d.Zone == "Battlefield" && d.PhasedOut;
            dim.SetVisible(phased || ineligible);
        }

        public void SetTag(string text, Color color)
        {
            tagBadge.SetVisible(!string.IsNullOrEmpty(text));
            tagText.text = text ?? "";
            tagBadge.color = color;
        }

        /// <summary>Sets the single highlight state and whether the card is dimmed as ineligible for the current choice.</summary>
        public void SetHighlight(Highlight h, bool dimIneligible)
        {
            highlight = h;
            ineligible = dimIneligible && h == Highlight.None;
            UpdateDim(Data);

            Sprite icon = null; Color iconColor = Color.white;
            switch (h)
            {
                case Highlight.Selected: icon = Icons.Check; iconColor = SelectedColor; break;
                case Highlight.Attacking: icon = Icons.Sword; iconColor = AttackColor; break;
                case Highlight.Blocking: icon = Icons.Shield; iconColor = BlockColor; break;
            }
            backArtHolder.SetVisible(icon != null);
            if (icon != null) { badge.sprite = icon; badge.color = iconColor; }

            // Selected = thick ring, others = thin rim.
            float ring = h == Highlight.Selected ? -8 : -5;
            glow.rectTransform.offsetMin = new Vector2(ring, ring);
            glow.rectTransform.offsetMax = new Vector2(-ring, -ring);
        }

        // =================================================================================== motion API

        /// <summary>Moves the card to a layout slot. Unchanged targets are ignored, so calling this every update is cheap.</summary>
        public void MoveTo(Vector2 p, Vector2 s, float rotation, MoveKind kind, float delay = 0f)
        {
            float now = Motion.Now;
            if (!initialized)
            {
                TargetPos = p; TargetSize = s; TargetRotation = rotation;
                pos.Snap(p); size.Snap(s); rot.Snap(rotation);
                initialized = true;
                Apply(now);
                return;
            }

            if ((p - TargetPos).sqrMagnitude > 0.25f || kind == MoveKind.Snap)
            {
                Vector2 from = pos.Eval(now);
                TargetPos = p;
                switch (kind)
                {
                    case MoveKind.Snap: pos.Snap(p); arcActive = false; break;
                    case MoveKind.Reflow: pos.Retarget(p, now, Motion.Base, Ease.Out, delay); arcActive = false; break;
                    case MoveKind.Travel:
                    case MoveKind.Land:
                        pos.Retarget(p, now, Motion.Travel, kind == MoveKind.Land ? Ease.Out : Ease.InOut, delay);
                        // Cards arc, never slide flat, between zones (lift.travel).
                        float dist = (p - from).magnitude;
                        float apex = Mathf.Min(dist * Motion.TravelApexFraction, Motion.TravelApexMax);
                        arcControl = (from + p) * 0.5f + Vector2.up * apex * 2f;   // quadratic control point (apex at t=0.5)
                        arcActive = dist > 20f && !Motion.ReduceMotion;   // reduce motion: short straight slide
                        if (arcActive) transform.SetAsLastSibling();      // cards in flight render above resting ones
                        arcTilt = Mathf.Clamp((p.x - from.x) / Mathf.Max(1f, dist) * Motion.MaxTravelTilt, -Motion.MaxTravelTilt, Motion.MaxTravelTilt);
                        if (kind == MoveKind.Land) landPending = true;
                        break;
                }
            }
            if ((s - TargetSize).sqrMagnitude > 0.25f)
            {
                TargetSize = s;
                size.Retarget(s, now, kind == MoveKind.Snap ? 0f : (kind == MoveKind.Reflow ? Motion.Base : Motion.Travel), Ease.Out, delay);
            }
            if (Mathf.Abs(Mathf.DeltaAngle(rotation, TargetRotation)) > 0.5f)
            {
                TargetRotation = rotation;
                // Tap / untap: 90 degrees with a small overshoot (short).
                rot.Retarget(rotation, now, kind == MoveKind.Snap ? 0f : Motion.Short, Ease.TapBack, delay);
            }
        }

        bool landPending;

        /// <summary>Tap rotation for "attack confirmed": a sharper turn (micro).</summary>
        public void SharpTap()
        {
            rot.Duration = Mathf.Min(rot.Duration, Motion.Micro);
        }

        /// <summary>Places the card somewhere before its first MoveTo, so it can fly in from there.</summary>
        public void SpawnFrom(Vector2 from, Vector2 fromSize, float rotation = 0f)
        {
            pos.Snap(from); size.Snap(fromSize); rot.Snap(rotation);
            TargetPos = from; TargetSize = fromSize; TargetRotation = rotation;
            initialized = true;
            Apply(Motion.Now);
        }

        /// <summary>Token created / unknown origin: fades and scales in on its slot (emphasis, capped to budget).</summary>
        public void Materialize(float delay = 0f)
        {
            float now = Motion.Now;
            alpha.From = 0; alpha.To = 1; alpha.Start = now + delay; alpha.Duration = Motion.Emphasis; alpha.Curve = Ease.Out;
            scalePulse.From = -0.35f; scalePulse.To = 0; scalePulse.Start = now + delay; scalePulse.Duration = Motion.Emphasis; scalePulse.Curve = Ease.Land;
        }

        /// <summary>Draw: starts face-down and flips to its face half-way along the arc.</summary>
        public void RevealDuringTravel()
        {
            if (IsHidden(Data)) return;
            back.gameObject.SetActive(true);
            flipPending = true; flipShowFront = true;
            flipX.From = 1; flipX.To = -1; flipX.Start = Motion.Now + Motion.Travel * 0.25f; flipX.Duration = Motion.Travel * 0.5f; flipX.Curve = Ease.InOut;
        }

        /// <summary>Transform / flip in place (capped to budget).</summary>
        public void Flip(bool _)
        {
            flipPending = true; flipShowFront = !IsHidden(Data);
            back.gameObject.SetActive(false);
            flipX.From = 1; flipX.To = -1; flipX.Start = Motion.Now; flipX.Duration = Motion.Dramatic; flipX.Curve = Ease.InOut;
        }

        /// <summary>Anticipation pulse on a source whose ability goes on the stack.</summary>
        public void Pulse()
        {
            scalePulse.From = 0.12f; scalePulse.To = 0f; scalePulse.Start = Motion.Now; scalePulse.Duration = Motion.Short + Motion.Micro; scalePulse.Curve = Ease.Out;
        }

        /// <summary>Impact: brief flash (white, or tinted e.g. deathtouch) plus a shake scaled by damage.</summary>
        public void Hit(int damage, Color? tint = null)
        {
            flashColor = tint ?? Color.white;
            flash.From = 0.85f; flash.To = 0; flash.Start = Motion.Now; flash.Duration = Motion.Impact; flash.Curve = Ease.Out;
            shakeAmp = Motion.ReduceMotion ? 0f : Mathf.Clamp(2f + damage * 1.5f, 3f, 10f);
            shakeUntil = Motion.Now + Motion.Impact;
        }

        /// <summary>Combat pose offset (attack forward / block forward) in board pixels.</summary>
        public void SetPose(Vector2 offset, float duration, float delay = 0f)
        {
            if ((offset - pose.To).sqrMagnitude < 0.25f) return;
            pose.Retarget(offset, Motion.Now, duration, Ease.Out, delay);
        }

        Vector2 lungeTarget; float lungeStart = -1;

        /// <summary>
        /// Damage step (section 5.2), compressed into the 400 ms budget: wind-up 60, lunge 120 (ease.in),
        /// impact at the end of the lunge, recoil 220 (ease.out). Calls onImpact when it lands.
        /// </summary>
        public void Lunge(Vector2 towardBoardPos, float delay, Action onImpact)
        {
            if (Motion.ReduceMotion)
            {
                // No lunge: a pulse in place, impact right after.
                Pulse();
                lungeTarget = towardBoardPos; lungeStart = -1; lungeFired = true;
                StartCoroutine(After(delay + Motion.Short, onImpact));
                return;
            }
            lungeTarget = towardBoardPos;
            lungeStart = Motion.Now + delay;
            lungeImpact = onImpact;
            lungeFired = false;
        }

        Action lungeImpact; bool lungeFired;

        System.Collections.IEnumerator After(float seconds, Action a)
        {
            yield return new WaitForSecondsRealtime(seconds);
            a?.Invoke();
        }

        // ----- drag weight: the card leans into its motion like a held 3D object, then springs level.
        Vector3 dragTilt, dragTiltVel;
        Vector2 lastDragPos, dragVel;
        bool hadDrag;
        float dragLift;

        void UpdateDragTilt()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (dt <= 0f) return;
            Vector3 target = Vector3.zero;
            if (Dragging && !Motion.ReduceMotion)
            {
                if (!hadDrag) { lastDragPos = DragBoardPos; dragVel = Vector2.zero; hadDrag = true; }
                Vector2 v = (DragBoardPos - lastDragPos) / dt;
                lastDragPos = DragBoardPos;
                dragVel = Vector2.Lerp(dragVel, v, 1f - Mathf.Exp(-dt / 0.06f));   // smoothed pointer velocity
                // Pitch with vertical motion, yaw and a little roll with horizontal motion (trailing edge lags).
                target = new Vector3(Mathf.Clamp(dragVel.y * 0.018f, -28f, 28f),
                                     Mathf.Clamp(-dragVel.x * 0.018f, -28f, 28f),
                                     Mathf.Clamp(-dragVel.x * 0.007f, -10f, 10f));
            }
            else hadDrag = false;
            // Underdamped spring: overshoots slightly and settles, so it "balances" when you stop.
            const float stiffness = 220f, damping = 16f;
            dragTiltVel += ((target - dragTilt) * stiffness - dragTiltVel * damping) * dt;
            dragTilt += dragTiltVel * dt;
            if (!Dragging && dragTilt.sqrMagnitude < 0.0004f && dragTiltVel.sqrMagnitude < 0.01f) { dragTilt = Vector3.zero; dragTiltVel = Vector3.zero; }
            dragLift = Mathf.MoveTowards(dragLift, Dragging ? 1f : 0f, dt / Motion.Short);
        }

        /// <summary>True while the card is arcing between zones (BoardView keeps it on top).</summary>
        public bool IsTraveling => arcActive;

        /// <summary>Hand cards wobble when you must discard down to hand size (design doc 8).</summary>
        public bool Wobble;

        bool unplayable;
        /// <summary>Greyed slightly: a hand card you can't play right now while you hold priority.</summary>
        public void SetUnplayable(bool on) { if (unplayable == on) return; unplayable = on; UpdateArtTint(); }

        /// <summary>Art tint: red wash while damage is marked, grey for unplayable hand cards (shader-state stand-ins).</summary>
        void UpdateArtTint()
        {
            if (art == null) return;
            bool damaged = Data != null && Data.Zone == "Battlefield" && Data.Damage > 0;
            Color c = Color.white;
            if (damaged) c = new Color(1f, 0.78f, 0.76f);
            if (unplayable) c = Color.Lerp(c, new Color(0.55f, 0.55f, 0.58f), 0.6f);
            art.color = c;
        }

        Vector2 LungeOffset(float now, Vector2 basePos)
        {
            if (lungeStart < 0) return Vector2.zero;
            float t = now - lungeStart;
            if (t < 0) return Vector2.zero;
            float windUp = 0.060f * Motion.Speed, strike = 0.120f * Motion.Speed, recoil = 0.220f * Motion.Speed;
            Vector2 dir = lungeTarget - basePos;
            float reach = Mathf.Max(0f, dir.magnitude - TargetSize.y * 0.55f);   // stop just short of the target
            Vector2 n = dir.sqrMagnitude > 1f ? dir.normalized : Vector2.up;
            if (t < windUp) return -n * TargetSize.y * 0.1f * Easing.Eval(Ease.Out, t / windUp);
            t -= windUp;
            if (t < strike) return Vector2.Lerp(-n * TargetSize.y * 0.1f, n * reach, Easing.Eval(Ease.In, t / strike));
            if (!lungeFired) { lungeFired = true; lungeImpact?.Invoke(); }
            t -= strike;
            if (t < recoil) return Vector2.Lerp(n * reach, Vector2.zero, Easing.Eval(Ease.Out, t / recoil));
            lungeStart = -1;
            return Vector2.zero;
        }

        /// <summary>
        /// Leaves the board for a zone we don't draw (or a pile). The card stops being interactive, plays its exit,
        /// then destroys itself. Every exit fits the 400 ms budget.
        /// </summary>
        public void PlayExit(ExitKind kind, Vector2 target, Action done = null)
        {
            if (IsExiting) return;
            IsExiting = true;
            Hovered = false;
            group.blocksRaycasts = false;
            exitDone = done;
            float now = Motion.Now;
            float dur = kind == ExitKind.Discard || kind == ExitKind.ToLibrary ? Motion.Travel : Motion.Dramatic;
            exitEnd = now + dur;
            highlight = Highlight.None;
            backArtHolder.SetVisible(false);
            transform.SetAsLastSibling();   // moving cards render above resting ones

            pos.Retarget(target, now, dur, kind == ExitKind.ToStack ? Ease.Out : Ease.In);
            arcActive = false;
            alpha.From = 1; alpha.To = 0; alpha.Start = now + dur * (kind == ExitKind.ToStack ? 0.55f : 0.35f); alpha.Duration = dur * (kind == ExitKind.ToStack ? 0.45f : 0.65f); alpha.Curve = Ease.In;
            switch (kind)
            {
                case ExitKind.Die:
                    // Desaturate (grey wash) and crumble toward the graveyard.
                    dim.color = new Color(0.25f, 0.25f, 0.25f, 0.55f); dim.gameObject.SetActive(true);
                    scalePulse.Retarget(-0.15f, now, dur, Ease.In);
                    break;
                case ExitKind.Exile:
                    // Washes to white-hot, then folds away toward the exile pile.
                    flashColor = new Color(1f, 0.97f, 0.85f);
                    flash.From = 0; flash.To = 1; flash.Start = now; flash.Duration = dur * 0.5f; flash.Curve = Ease.Out;
                    size.Retarget(new Vector2(TargetSize.x * 0.15f, TargetSize.y), now, dur, Ease.In);
                    break;
                case ExitKind.ToStack:
                    scalePulse.Retarget(Motion.StackShowScale - 1f, now, dur, Ease.Out);
                    break;
                case ExitKind.ToLibrary:
                    scalePulse.Retarget(-0.3f, now, dur, Ease.In);
                    break;
                case ExitKind.Discard:
                    rot.Retarget(TargetRotation + 180f, now, dur, Ease.In);
                    scalePulse.Retarget(-0.2f, now, dur, Ease.In);
                    break;
                default:
                    scalePulse.Retarget(-0.1f, now, dur, Ease.In);
                    break;
            }
        }

        // =================================================================================== per frame

        void Update()
        {
            float now = Motion.Now;
            if (IsExiting && now >= exitEnd)
            {
                exitDone?.Invoke();
                Destroy(gameObject);
                return;
            }
            Apply(now);
        }

        void Apply(float now)
        {
            // Hover lift (micro): +0.12 card heights, scale 1.08.
            float hoverGoal = Hovered && HoverLift && !IsExiting ? 1f : 0f;
            if (Mathf.Abs(hoverT.To - hoverGoal) > 0.01f) hoverT.Retarget(hoverGoal, now, Motion.Micro, Ease.Out);
            float hover = hoverT.Eval(now);

            Vector2 basePos = pos.Eval(now);
            float arcLift = 0f;
            float tilt = 0f;
            if (arcActive && !pos.Done(now))
            {
                float t = Easing.Eval(pos.Curve == Ease.Linear ? Ease.InOut : pos.Curve, pos.Progress(now));
                // Quadratic Bezier between start, apex control and end.
                Vector2 a = pos.From, c = arcControl, b = pos.To;
                Vector2 bez = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * c + t * t * b;
                arcLift = (bez - Vector2.Lerp(a, b, t)).magnitude;
                basePos = bez;
                tilt = arcTilt * Mathf.Sin(t * Mathf.PI);
            }
            else if (arcActive)
            {
                arcActive = false;
                if (landPending)
                {
                    // ease.land: settle onto the battlefield with a small overshoot.
                    landPending = false;
                    scalePulse.From = 0.06f; scalePulse.To = 0; scalePulse.Start = now; scalePulse.Duration = Motion.Short; scalePulse.Curve = Ease.Land;
                }
            }

            Vector2 s = size.Eval(now);
            Vector2 p = basePos + pose.Eval(now) + LungeOffset(now, basePos);
            p.y += s.y * 0.12f * hover;
            if (flying && Data != null && (Data.Attacking || Data.Blocking))
                p.y += 6f + Mathf.Sin(now * Mathf.PI) * 3f;   // slow bob only while in combat
            if (now < shakeUntil)
            {
                float k = (shakeUntil - now) / Mathf.Max(0.001f, Motion.Impact);
                p += new Vector2(Mathf.Sin(now * 90f), Mathf.Cos(now * 77f)) * shakeAmp * k;
            }

            if (Dragging)
            {
                // Follows the pointer, lifted and upright; eases toward it so fast flicks still read smoothly.
                p = Vector2.Lerp(rt.anchoredPosition, DragBoardPos, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.03f));
            }
            rt.anchoredPosition = p;
            rt.sizeDelta = s;
            float wobble = Wobble && !Motion.ReduceMotion ? Mathf.Sin(now * 14f + CardId) * 2.5f : 0f;
            UpdateDragTilt();
            rt.localEulerAngles = new Vector3(dragTilt.x, dragTilt.y, rot.Eval(now) + tilt + wobble + dragTilt.z);
            float scale = 1f + (Motion.HoverScale - 1f) * hover + scalePulse.Eval(now) + dragLift * 0.1f;
            float fx = flipX.Eval(now);
            if (flipPending && fx <= 0f)
            {
                back.gameObject.SetActive(!flipShowFront);
            }
            if (flipPending && flipX.Done(now)) { flipPending = false; flipX.Snap(1); fx = 1; }
            rt.localScale = new Vector3(scale * Mathf.Abs(fx), scale, 1f);
            group.alpha = alpha.Eval(now);

            // Shadow grows with height: arcs, hover and lunges all lift the card.
            float height = Mathf.Clamp01(arcLift / 120f + hover * 0.5f + scalePulse.Eval(now) * 2f + dragLift);
            var sh = shadow.rectTransform;
            float spread = 14f + 18f * height;
            Vector2 off = new Vector2(3f, -7f) + new Vector2(6f, -14f) * height;
            sh.offsetMin = new Vector2(-spread + off.x, -spread + off.y);
            sh.offsetMax = new Vector2(spread + off.x, spread + off.y);
            Color shadowColor = highlight == Highlight.Attacking ? new Color(AttackColor.r, AttackColor.g * 0.6f, AttackColor.b * 0.4f, 0.85f)
                : highlight == Highlight.Blocking ? new Color(BlockColor.r * 0.6f, BlockColor.g * 0.8f, BlockColor.b, 0.85f)
                : new Color(0, 0, 0, 0.65f - 0.2f * height);
            shadow.color = shadowColor;   // attacking / blocking: coloured underglow

            float f = flash.Eval(now);
            flashImg.color = new Color(flashColor.r, flashColor.g, flashColor.b, f);

            float pf = ptFlash.Eval(now);
            ptBadge.color = Color.Lerp(ptBase, ptFlashColor, pf * 0.85f);
            ptBadge.rectTransform.localScale = Vector3.one * badgePop.Eval(now);
            counterBadge.rectTransform.localScale = Vector3.one * counterPop.Eval(now);

            UpdateGlow(now);
            UpdateCorners();
        }

        void UpdateGlow(float now)
        {
            Color gc;
            switch (highlight)
            {
                case Highlight.Playable: gc = PlayableColor; gc.a = 0.9f; break;                              // steady rim
                case Highlight.Eligible:                                                                       // pulsing ring, 1.2 s period
                    gc = EligibleColor;
                    gc.a = Mathf.Lerp(0.4f, 1f, 0.5f + 0.5f * Mathf.Sin(now * Mathf.PI * 2f / 1.2f));
                    break;
                case Highlight.Selected: gc = SelectedColor; break;                                            // thick ring + check
                default: gc = Color.clear; break;                                                              // attack/block use underglow
            }
            glow.color = gc;
        }

        void UpdateCorners()
        {
            float w = rt.sizeDelta.x;
            if (Mathf.Abs(w - lastWidth) < 0.5f) return;
            lastWidth = w;
            float mult = Mathf.Max(0.3f, 16f / Mathf.Max(1f, w * 0.055f));
            frame.pixelsPerUnitMultiplier = mult;
            back.pixelsPerUnitMultiplier = mult;
            glow.pixelsPerUnitMultiplier = mult * 0.6f;
            flashImg.pixelsPerUnitMultiplier = mult;
            dim.pixelsPerUnitMultiplier = mult;
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

        public static string ManaText(string cost)
        {
            if (string.IsNullOrEmpty(cost) || cost == "no cost") return "";
            // Kept as {codes}: labels draw them as mana symbols.
            return cost.Replace(" ", "");
        }
    }

    /// <summary>Forwards Unity drag events from a card's frame to its CardView (a drag also cancels the click).</summary>
    public class CardDragHandler : MonoBehaviour, UnityEngine.EventSystems.IBeginDragHandler, UnityEngine.EventSystems.IDragHandler, UnityEngine.EventSystems.IEndDragHandler
    {
        public CardView Owner;
        bool active;

        public void OnBeginDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (e.button != UnityEngine.EventSystems.PointerEventData.InputButton.Left || Owner == null || Owner.IsExiting || Owner.DragBegan == null) return;
            active = true;
            Owner.DragBegan(Owner, e.position);
        }

        public void OnDrag(UnityEngine.EventSystems.PointerEventData e) { if (active) Owner.DragMoved?.Invoke(Owner, e.position); }

        public void OnEndDrag(UnityEngine.EventSystems.PointerEventData e)
        {
            if (!active) return;
            active = false;
            Owner.DragEnded?.Invoke(Owner, e.position);
        }
    }
}

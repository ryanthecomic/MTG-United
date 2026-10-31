using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// Motion tokens (design doc section 2). Every animation reads its timing from here so one change retunes the
    /// whole client. Durations follow the doc, but nothing may exceed <see cref="Budget"/> (400 ms): the longer doc
    /// tokens (emphasis, dramatic) are capped.
    /// </summary>
    public static class Motion
    {
        public const float Budget = 0.40f;

        /// <summary>Animation speed setting (design doc 10.2): Normal 1.0, Fast 0.65, Instant (short fades only).</summary>
        public enum SpeedSetting { Normal, Fast, Instant }

        static int speedSetting = -1;
        public static SpeedSetting SpeedMode
        {
            get { if (speedSetting < 0) speedSetting = Mathf.Clamp(PlayerPrefs.GetInt("animSpeed", 0), 0, 2); return (SpeedSetting)speedSetting; }
            set { speedSetting = (int)value; PlayerPrefs.SetInt("animSpeed", speedSetting); }
        }

        /// <summary>Global duration multiplier. Instant keeps a ~120 ms fade/highlight so changes are still visible.</summary>
        public static float Speed
        {
            get
            {
                switch (SpeedMode) { case SpeedSetting.Fast: return 0.65f; case SpeedSetting.Instant: return 0.3f; default: return 1f; }
            }
        }

        static int reduce = -1;
        /// <summary>Accessibility (10.3): no shakes or lunges, flatter travel, fewer particles.</summary>
        public static bool ReduceMotion
        {
            get { if (reduce < 0) reduce = PlayerPrefs.GetInt("reduceMotion", 0); return reduce == 1; }
            set { reduce = value ? 1 : 0; PlayerPrefs.SetInt("reduceMotion", reduce); }
        }

        public static float Micro => 0.090f * Speed;
        public static float Short => 0.160f * Speed;
        public static float Base => 0.260f * Speed;
        public static float Travel => 0.380f * Speed;
        public static float Emphasis => Mathf.Min(0.520f, Budget) * Speed;   // capped from 520 ms
        public static float Impact => 0.220f * Speed;
        public static float Dramatic => Mathf.Min(0.900f, Budget) * Speed;   // capped from 900 ms
        public static float Stagger => 0.040f * Speed;

        // Spatial tokens, in board pixels (the board is 1920x1080; one "unit" in the doc ~ one card height).
        public const float HoverScale = 1.08f;
        public const float TravelApexFraction = 0.25f;   // arc apex as a fraction of travel distance
        public const float TravelApexMax = 160f;
        public const float AttackForward = 0.25f;        // fraction of card height toward the opponent
        public const float StackShowScale = 1.6f;
        public const float MaxTravelTilt = 15f;

        /// <summary>Time source for all motion (unaffected by Time.timeScale).</summary>
        public static float Now => Time.unscaledTime;
    }

    public enum Ease { Linear, Out, In, InOut, Land, Snap, TapBack }

    /// <summary>Easing curves (design doc section 2.2).</summary>
    public static class Easing
    {
        public static float Eval(Ease e, float t)
        {
            t = Mathf.Clamp01(t);
            switch (e)
            {
                case Ease.Out: return Bezier(0.2f, 0.8f, 0.2f, 1f, t);
                case Ease.In: return Bezier(0.5f, 0f, 0.9f, 0.4f, t);
                case Ease.InOut: return Bezier(0.45f, 0f, 0.25f, 1f, t);
                case Ease.Land: return BackOut(t, 1.15f);
                case Ease.Snap: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case Ease.TapBack: return BackOut(t, 0.6f);           // small (~4 degree) overshoot on a 90 degree tap
                default: return t;
            }
        }

        /// <summary>Back-out: overshoots the target slightly and settles.</summary>
        static float BackOut(float t, float s)
        {
            t -= 1f;
            return t * t * ((s + 1f) * t + s) + 1f;
        }

        /// <summary>CSS-style cubic-bezier(x1, y1, x2, y2) evaluated at progress t.</summary>
        static float Bezier(float x1, float y1, float x2, float y2, float t)
        {
            // Solve x(u) = t for u with a few Newton steps, then return y(u).
            float u = t;
            for (int i = 0; i < 6; i++)
            {
                float x = Cubic(x1, x2, u) - t;
                float dx = CubicDeriv(x1, x2, u);
                if (Mathf.Abs(x) < 1e-4f || Mathf.Abs(dx) < 1e-6f) break;
                u = Mathf.Clamp01(u - x / dx);
            }
            return Cubic(y1, y2, u);
        }

        static float Cubic(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u;
        }

        static float CubicDeriv(float p1, float p2, float u)
        {
            float v = 1f - u;
            return 3f * v * v * p1 + 6f * v * u * (p2 - p1) + 3f * u * u * (1f - p2);
        }
    }

    /// <summary>A single animated value: from, to, start time, duration, curve. Retargets from wherever it is now.</summary>
    public struct TweenV2
    {
        public Vector2 From, To;
        public float Start, Duration;
        public Ease Curve;

        public Vector2 Eval(float now)
        {
            if (Duration <= 0f) return To;
            float t = (now - Start) / Duration;
            if (t >= 1f) return To;
            if (t <= 0f) return From;
            return Vector2.LerpUnclamped(From, To, Easing.Eval(Curve, t));
        }

        public float Progress(float now) => Duration <= 0f ? 1f : Mathf.Clamp01((now - Start) / Duration);
        public bool Done(float now) => now - Start >= Duration;

        public void Retarget(Vector2 to, float now, float duration, Ease curve, float delay = 0f)
        {
            From = Eval(now);
            To = to;
            Start = now + delay;
            Duration = duration;
            Curve = curve;
        }

        public void Snap(Vector2 v) { From = To = v; Duration = 0f; }
    }

    public struct TweenF
    {
        public float From, To, Start, Duration;
        public Ease Curve;

        public float Eval(float now)
        {
            if (Duration <= 0f) return To;
            float t = (now - Start) / Duration;
            if (t >= 1f) return To;
            if (t <= 0f) return From;
            return Mathf.LerpUnclamped(From, To, Easing.Eval(Curve, t));
        }

        public bool Done(float now) => now - Start >= Duration;

        public void Retarget(float to, float now, float duration, Ease curve, float delay = 0f)
        {
            From = Eval(now);
            To = to;
            Start = now + delay;
            Duration = duration;
            Curve = curve;
        }

        public void Snap(float v) { From = To = v; Duration = 0f; }
    }
}

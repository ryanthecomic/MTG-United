using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Colour-identity palette for effects (design doc 6.2). Multicolour blends the first two colours.</summary>
    public struct Palette
    {
        public Color Primary, Secondary;

        static readonly Palette W = Make("#F4ECD6", "#E6C46A"), U = Make("#2C6FD1", "#9BE3F2"), B = Make("#3B2347", "#9C948A"),
                                R = Make("#E2532A", "#FFC24A"), G = Make("#3E8E41", "#B4DD62"), C = Make("#8A96A3", "#D4DAE0");

        static Palette Make(string a, string b)
        {
            ColorUtility.TryParseHtmlString(a, out var p);
            ColorUtility.TryParseHtmlString(b, out var s);
            return new Palette { Primary = p, Secondary = s };
        }

        static Palette Of(char c)
        {
            switch (c) { case 'W': return W; case 'U': return U; case 'B': return B; case 'R': return R; case 'G': return G; default: return C; }
        }

        /// <summary>From a Forge colour string ("R", "WU", ""): one colour, a blend of the leading two, or steel for colourless.</summary>
        public static Palette For(string colors)
        {
            if (string.IsNullOrEmpty(colors)) return C;
            string c = colors.ToUpperInvariant();
            var first = Of(c[0]);
            if (c.Length == 1) return first;
            var second = Of(c[1]);
            return new Palette { Primary = first.Primary, Secondary = second.Secondary };
        }

        /// <summary>A colour along the effect's length (multicolour sources shift from one palette to the next).</summary>
        public Color At(float t) => Color.Lerp(Primary, Secondary, t);
    }

    /// <summary>
    /// Reusable effect families (design doc 6.1), drawn as pooled UI sprites over the board: projectile, beam,
    /// aura wash, drain, area pulse, summon, counter, mana and trigger notice. Each takes a source, target(s) and a
    /// palette; lengths are compressed into the 400 ms motion budget. No allocations per frame once warm.
    /// </summary>
    public class Vfx : MonoBehaviour
    {
        public static Vfx Instance { get; private set; }

        struct Particle
        {
            public RectTransform rt;
            public Image img;
            public Vector2 from, to, ctrl;
            public float start, dur, size0, size1, rot;
            public Color c0, c1;
            public Ease ease;
            public bool line;     // stretched between from and to (beam)
            public bool alive;
        }

        RectTransform root;
        Particle[] parts = new Particle[160];
        int count;
        static Sprite soft, ring;

        public static Vfx Create(Transform parent)
        {
            var rt = UIKit.Rect("Vfx", parent).Stretch();
            var v = rt.gameObject.AddComponent<Vfx>();
            v.root = rt;
            Instance = v;
            return v;
        }

        /// <summary>Particle count multiplier: reduce-motion uses fewer.</summary>
        static float Density => Motion.ReduceMotion ? 0.4f : 1f;

        // ================================================================================== families

        /// <summary>Damage to a target: an orb on a shallow arc with a trail, bursting on impact.</summary>
        public void Projectile(Vector2 from, Vector2 to, Palette pal, float intensity = 0.5f, Action onImpact = null)
        {
            float dur = Motion.Short + Motion.Micro;   // 250 ms flight
            var mid = (from + to) * 0.5f + Vector2.Perpendicular((to - from).normalized) * Mathf.Min(80f, (to - from).magnitude * 0.15f) + Vector2.up * 30f;
            float size = 26f + 16f * intensity;
            Spawn(from, to, mid, 0f, dur, size, size * 0.8f, pal.Secondary, pal.Primary, Ease.In);
            int trail = Mathf.RoundToInt(5 * Density);
            for (int i = 1; i <= trail; i++)
                Spawn(from, to, mid, i * 0.018f, dur, size * (1f - i * 0.13f), 4f, WithA(pal.Primary, 0.6f), WithA(pal.Primary, 0f), Ease.In);
            Burst(to, pal, dur, intensity);
            if (onImpact != null) Later(dur, onImpact);
        }

        /// <summary>Destroy / exile a target: a line from the source widens onto the target, which flares.</summary>
        public void Beam(Vector2 from, Vector2 to, Palette pal, float delay = 0f)
        {
            float dur = Motion.Travel;
            var i = Spawn(from, to, from, delay, dur, 2f, 16f, WithA(pal.Secondary, 0.95f), WithA(pal.Primary, 0f), Ease.Out);
            if (i >= 0) parts[i].line = true;
            Spawn(to, to, to, delay + dur * 0.35f, dur * 0.65f, 40f, 150f, WithA(pal.Secondary, 0.9f), WithA(pal.Primary, 0f), Ease.Out);
        }

        /// <summary>Buff / protection: an upward glow with rising motes around the target.</summary>
        public void AuraWash(Vector2 at, Vector2 cardSize, Palette pal)
        {
            float dur = Motion.Travel - Motion.Micro * 0.4f;
            Spawn(at, at + Vector2.up * 10f, at, 0f, dur, cardSize.x * 0.9f, cardSize.x * 1.3f, WithA(pal.Secondary, 0.45f), WithA(pal.Primary, 0f), Ease.Out);
            int n = Mathf.RoundToInt(8 * Density);
            for (int k = 0; k < n; k++)
            {
                float x = (k / (float)Mathf.Max(1, n - 1) - 0.5f) * cardSize.x * 0.9f;
                var a = at + new Vector2(x, -cardSize.y * 0.35f);
                Spawn(a, a + Vector2.up * cardSize.y * (0.5f + 0.3f * Hash(k)), a, k * 0.02f, dur * 0.9f, 9f, 3f, pal.At(Hash(k + 3)), WithA(pal.Secondary, 0f), Ease.Out);
            }
        }

        /// <summary>Life drain / steal: particles stream from the target into the receiver.</summary>
        public void Drain(Vector2 from, Vector2 to, Palette pal)
        {
            int n = Mathf.RoundToInt(9 * Density);
            float each = Motion.Base;
            for (int k = 0; k < n; k++)
            {
                var jitter = new Vector2(Hash(k) - 0.5f, Hash(k + 7) - 0.5f) * 60f;
                var ctrl = (from + to) * 0.5f + jitter * 2f;
                Spawn(from + jitter * 0.4f, to, ctrl, k * 0.016f, each, 12f, 5f, pal.At(k / (float)n), WithA(pal.Secondary, 0.2f), Ease.InOut);
            }
        }

        /// <summary>Board wipe / "each creature": a ring expands from the stack across the battlefield.</summary>
        public void AreaPulse(Vector2 from, Palette pal)
        {
            float dur = Motion.Emphasis;   // a board-wide ring: emphasis (520 ms; 400 in Fast)
            int i = Spawn(from, from, from, 0f, dur, 60f, 2600f, WithA(pal.Secondary, 0.8f), WithA(pal.Primary, 0f), Ease.Out);
            if (i >= 0) parts[i].img.sprite = Ring;
            Burst(from, pal, 0f, 0.7f);
        }

        /// <summary>Token / reanimation: particles converge into the card's spot before it fades in.</summary>
        public void Summon(Vector2 at, Palette pal)
        {
            int n = Mathf.RoundToInt(10 * Density);
            float dur = Motion.Base;
            for (int k = 0; k < n; k++)
            {
                float ang = k / (float)n * Mathf.PI * 2f;
                var a = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (90f + 40f * Hash(k));
                Spawn(a, at, a, k * 0.01f, dur, 6f, 12f, WithA(pal.Secondary, 0.4f), WithA(pal.Primary, 0.9f), Ease.In);
            }
            Spawn(at, at, at, dur * 0.8f, Motion.Short, 30f, 110f, WithA(pal.Secondary, 0.7f), WithA(pal.Primary, 0f), Ease.Out);
        }

        /// <summary>Counterspell: a shield sigil slams onto the spell and shards fly out.</summary>
        public void Counter(Vector2 at, Palette pal)
        {
            float slam = Motion.Short;
            int i = Spawn(at, at, at, 0f, slam, 150f, 84f, WithA(pal.Secondary, 0f), WithA(pal.Secondary, 1f), Ease.In);
            if (i >= 0) parts[i].img.sprite = Icons.Shield;
            int j = Spawn(at, at, at, slam, Motion.Base, 84f, 96f, WithA(pal.Secondary, 1f), WithA(pal.Primary, 0f), Ease.Out);
            if (j >= 0) parts[j].img.sprite = Icons.Shield;
            int n = Mathf.RoundToInt(10 * Density);
            for (int k = 0; k < n; k++)
            {
                float ang = (k / (float)n + Hash(k) * 0.05f) * Mathf.PI * 2f;
                Spawn(at, at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (90f + 50f * Hash(k + 2)), at, slam, Motion.Base, 10f, 3f,
                      pal.At(Hash(k)), WithA(pal.Primary, 0f), Ease.Out);
            }
        }

        /// <summary>Tapping a land for mana: a small coloured orb flies from the land to the mana pool.</summary>
        public void Mana(Vector2 from, Vector2 to, Color color)
        {
            Spawn(from, to, (from + to) * 0.5f + Vector2.up * 40f, 0f, Motion.Base * 0.77f, 16f, 10f, color, WithA(color, 0.7f), Ease.InOut);
        }

        /// <summary>Triggered ability: a quick ring around the source as its chip pops onto the stack.</summary>
        public void TriggerNotice(Vector2 at, Palette pal)
        {
            int i = Spawn(at, at, at, 0f, Motion.Base, 40f, 170f, WithA(pal.Secondary, 0.85f), WithA(pal.Primary, 0f), Ease.Out);
            if (i >= 0) parts[i].img.sprite = Ring;
        }

        /// <summary>Life gain at a portrait: gold glow and rising motes.</summary>
        public void Motes(Vector2 at, Color color)
        {
            int n = Mathf.RoundToInt(8 * Density);
            for (int k = 0; k < n; k++)
            {
                var a = at + new Vector2((Hash(k) - 0.5f) * 120f, -20f);
                Spawn(a, a + Vector2.up * (60f + 40f * Hash(k + 1)), a, k * 0.025f, Motion.Travel, 9f, 3f, color, WithA(color, 0f), Ease.Out);
            }
        }

        /// <summary>Impact burst used at the end of projectiles and pulses.</summary>
        void Burst(Vector2 at, Palette pal, float delay, float intensity)
        {
            int n = Mathf.RoundToInt((6 + 6 * intensity) * Density);
            for (int k = 0; k < n; k++)
            {
                float ang = (k / (float)n) * Mathf.PI * 2f + Hash(k) * 0.5f;
                float dist = 40f + 50f * Hash(k + 5) * (0.6f + intensity);
                Spawn(at, at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist, at, delay, Motion.Short + Motion.Micro * 0.5f, 12f, 2f,
                      pal.At(Hash(k + 9)), WithA(pal.Secondary, 0f), Ease.Out);
            }
            Spawn(at, at, at, delay, Motion.Short, 20f, 80f + 40f * intensity, WithA(pal.Secondary, 0.9f), WithA(pal.Primary, 0f), Ease.Out);
        }

        // ================================================================================== core

        int Spawn(Vector2 from, Vector2 to, Vector2 ctrl, float delay, float dur, float size0, float size1, Color c0, Color c1, Ease ease)
        {
            int slot = -1;
            for (int i = 0; i < count; i++) if (!parts[i].alive) { slot = i; break; }
            if (slot < 0)
            {
                if (count >= parts.Length) return -1;   // budget reached: drop the extra particle
                slot = count++;
                var rt = UIKit.Rect("P", root);
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                parts[slot].rt = rt; parts[slot].img = img;
            }
            ref var p = ref parts[slot];
            p.from = from; p.to = to; p.ctrl = ctrl;
            p.start = Motion.Now + delay; p.dur = Mathf.Max(0.02f, Mathf.Min(dur, Motion.Budget));
            p.size0 = size0; p.size1 = size1; p.c0 = c0; p.c1 = c1; p.ease = ease;
            p.line = false; p.alive = true; p.rot = 0f;
            p.img.sprite = Soft;
            p.img.color = new Color(0, 0, 0, 0);
            p.rt.gameObject.SetActive(true);
            return slot;
        }

        readonly List<(float at, Action a)> later = new List<(float, Action)>();
        void Later(float seconds, Action a) => later.Add((Motion.Now + seconds, a));

        void Update()
        {
            float now = Motion.Now;
            for (int i = later.Count - 1; i >= 0; i--)
                if (now >= later[i].at) { var a = later[i].a; later.RemoveAt(i); a(); }

            for (int i = 0; i < count; i++)
            {
                ref var p = ref parts[i];
                if (!p.alive) continue;
                float t = (now - p.start) / p.dur;
                if (t < 0f) { p.img.color = new Color(0, 0, 0, 0); continue; }
                if (t >= 1f) { p.alive = false; p.rt.gameObject.SetActive(false); continue; }
                float e = Easing.Eval(p.ease, t);
                Color c = Color.Lerp(p.c0, p.c1, t);
                if (p.line)
                {
                    // Beam: grows from the source to the target in the first 40%, then widens and fades.
                    float reach = Mathf.Clamp01(t / 0.4f);
                    Vector2 end = Vector2.Lerp(p.from, p.to, Easing.Eval(Ease.Out, reach));
                    Vector2 d = end - p.from;
                    p.rt.anchoredPosition = (p.from + end) * 0.5f;
                    p.rt.sizeDelta = new Vector2(Mathf.Max(1f, d.magnitude), Mathf.Lerp(p.size0, p.size1, e));
                    p.rt.localEulerAngles = new Vector3(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    p.img.sprite = UIKit.White;
                }
                else
                {
                    Vector2 pos = (1 - e) * (1 - e) * p.from + 2 * (1 - e) * e * p.ctrl + e * e * p.to;
                    float s = Mathf.Lerp(p.size0, p.size1, e);
                    p.rt.anchoredPosition = pos;
                    p.rt.sizeDelta = new Vector2(s, s);
                }
                p.img.color = c;
            }
        }

        static Color WithA(Color c, float a) { c.a = a; return c; }
        static float Hash(int k) { float x = Mathf.Sin(k * 12.9898f + 78.233f) * 43758.5453f; return x - Mathf.Floor(x); }

        /// <summary>Soft round particle (radial falloff), generated once.</summary>
        static Sprite Soft
        {
            get
            {
                if (soft != null) return soft;
                const int n = 32;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d)));
                }
                t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
                soft = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return soft;
            }
        }

        /// <summary>Thin soft ring for pulses.</summary>
        static Sprite Ring
        {
            get
            {
                if (ring != null) return ring;
                const int n = 128;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.08f);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                t.Apply(); t.wrapMode = TextureWrapMode.Clamp;
                ring = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
                return ring;
            }
        }
    }
}

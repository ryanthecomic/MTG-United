using System;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// Small white icons drawn at runtime (tinted by the Image colour): check mark for Selected, sword for
    /// Attacking, shield for Blocking. Shape, not just colour, tells the states apart (design doc section 4).
    /// </summary>
    public static class Icons
    {
        static Sprite check, sword, shield, circle;

        public static Sprite Check => check ?? (check = Make(CheckSdf));
        public static Sprite Sword => sword ?? (sword = Make(SwordSdf));
        public static Sprite Shield => shield ?? (shield = Make(ShieldSdf));
        public static Sprite Circle => circle ?? (circle = Make(p => p.magnitude - 0.9f));

        const int Size = 64;

        /// <summary>Rasterises a signed distance function (negative = inside) over [-1,1]^2 with soft edges.</summary>
        static Sprite Make(Func<Vector2, float> sdf)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            float px = 2f / Size;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                float d = sdf(p);
                float a = Mathf.Clamp01(0.5f - d / px);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            t.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(t, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100);
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        static float Box(Vector2 p, Vector2 half)
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y);
            return new Vector2(Mathf.Max(d.x, 0), Mathf.Max(d.y, 0)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0);
        }

        static Vector2 Rotate(Vector2 p, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x + s * p.y, -s * p.x + c * p.y);
        }

        static float CheckSdf(Vector2 p)
        {
            float a = Segment(p, new Vector2(-0.62f, 0.02f), new Vector2(-0.18f, -0.48f), 0.13f);
            float b = Segment(p, new Vector2(-0.18f, -0.48f), new Vector2(0.65f, 0.55f), 0.13f);
            return Mathf.Min(a, b);
        }

        static float SwordSdf(Vector2 p)
        {
            p = Rotate(p, 45f);
            float blade = Box(p - new Vector2(0, 0.18f), new Vector2(0.11f, 0.62f));
            float tip = Segment(p, new Vector2(0, 0.8f), new Vector2(0, 0.9f), 0.06f);
            float guard = Box(p - new Vector2(0, -0.48f), new Vector2(0.38f, 0.07f));
            float grip = Box(p - new Vector2(0, -0.7f), new Vector2(0.07f, 0.17f));
            return Mathf.Min(Mathf.Min(blade, tip), Mathf.Min(guard, grip));
        }

        static float ShieldSdf(Vector2 p)
        {
            // Rounded rectangle on top, tapering to a point at the bottom.
            float top = Box(p - new Vector2(0, 0.42f), new Vector2(0.64f, 0.38f)) - 0.08f;
            float taper = (Mathf.Abs(p.x) - 0.72f * (p.y + 0.9f) / 0.95f) * 0.8f;
            float lower = Mathf.Max(taper, Mathf.Max(p.y - 0.1f, -p.y - 0.9f));
            return Mathf.Min(top, lower);
        }
    }
}

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

        // Menu / deck builder icons (all original shapes).
        static Sprite gear, home, person, decks, plus, trash, columns, grid, diamond, crown, search, cross, list;
        public static Sprite Gear => gear ?? (gear = Make(GearSdf));
        public static Sprite Home => home ?? (home = Make(HomeSdf));
        public static Sprite Person => person ?? (person = Make(PersonSdf));
        public static Sprite Decks => decks ?? (decks = Make(DecksSdf));
        public static Sprite Plus => plus ?? (plus = Make(p => Mathf.Min(Box(p, new Vector2(0.62f, 0.13f)), Box(p, new Vector2(0.13f, 0.62f)))));
        public static Sprite Trash => trash ?? (trash = Make(TrashSdf));
        public static Sprite Columns => columns ?? (columns = Make(ColumnsSdf));
        public static Sprite Grid => grid ?? (grid = Make(GridSdf));
        public static Sprite Diamond => diamond ?? (diamond = Make(p => (Mathf.Abs(p.x) + Mathf.Abs(p.y)) * 0.7071f - 0.55f));
        public static Sprite Crown => crown ?? (crown = Make(CrownSdf));
        public static Sprite Search => search ?? (search = Make(SearchSdf));
        public static Sprite Cross => cross ?? (cross = Make(p => Mathf.Min(Segment(p, new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f), 0.12f),
                                                                       Segment(p, new Vector2(-0.5f, 0.5f), new Vector2(0.5f, -0.5f), 0.12f))));
        public static Sprite List => list ?? (list = Make(ListSdf));
        static Sprite tap, untap, grave, spiral, cardIcon, library, anvil;
        /// <summary>Anvil (the Forge tab).</summary>
        public static Sprite Anvil => anvil ?? (anvil = Make(AnvilSdf));
        /// <summary>Zone markers for player panels: hand (a card), library (a stack), graveyard (a headstone), exile (a spiral).</summary>
        public static Sprite Hand => cardIcon ?? (cardIcon = Make(p => Mathf.Abs(Box(Rotate(p, -8f), new Vector2(0.42f, 0.6f)) - 0.05f) - 0.09f));
        public static Sprite Library => library ?? (library = Make(LibrarySdf));
        public static Sprite Grave => grave ?? (grave = Make(GraveSdf));
        public static Sprite Spiral => spiral ?? (spiral = Make(SpiralSdf));
        /// <summary>Tap: a clockwise turning arrow. Untap: the same arrow turning back.</summary>
        public static Sprite Tap => tap ?? (tap = Make(p => TurnArrowSdf(p, false)));
        public static Sprite Untap => untap ?? (untap = Make(p => TurnArrowSdf(p, true)));

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
        static float GearSdf(Vector2 p)
        {
            float r = p.magnitude;
            float ang = Mathf.Atan2(p.y, p.x);
            // 8 teeth: radius bumps between 0.62 and 0.84.
            float teeth = Mathf.Clamp01((Mathf.Cos(ang * 8f) - 0.15f) * 3f);
            float outer = r - (0.64f + 0.2f * teeth);
            float hole = 0.26f - r;
            return Mathf.Max(outer * 0.8f, hole);
        }

        static float HomeSdf(Vector2 p)
        {
            // A wide house: broad body, roof with eaves overhanging the walls, a door cut out.
            float body = Box(p - new Vector2(0, -0.38f), new Vector2(0.62f, 0.42f));
            float roof = Mathf.Max(Mathf.Abs(p.x) * 0.62f + (p.y - 0.82f) * 0.78f, -(p.y + 0.02f));
            float door = Box(p - new Vector2(0, -0.56f), new Vector2(0.17f, 0.25f));
            return Mathf.Max(Mathf.Min(body, roof), -door);
        }

        static float PersonSdf(Vector2 p)
        {
            float head = (p - new Vector2(0, 0.4f)).magnitude - 0.3f;
            float shoulders = Box(p - new Vector2(0, -0.55f), new Vector2(0.55f, 0.3f)) - 0.14f;
            return Mathf.Min(head, shoulders);
        }

        static float DecksSdf(Vector2 p)
        {
            // Three stacked card outlines, fanned.
            float d = 10f;
            for (int i = 0; i < 3; i++)
            {
                var q = Rotate(p - new Vector2((i - 1) * 0.22f, -0.05f + Mathf.Abs(i - 1) * 0.06f), (i - 1) * -14f);
                float card = Mathf.Abs(Box(q, new Vector2(0.32f, 0.5f)) - 0.04f) - 0.06f;
                d = Mathf.Min(d, card);
            }
            return d;
        }

        static float TrashSdf(Vector2 p)
        {
            float lid = Box(p - new Vector2(0, 0.55f), new Vector2(0.6f, 0.07f));
            float handle = Box(p - new Vector2(0, 0.68f), new Vector2(0.18f, 0.08f));
            float can = Mathf.Abs(Box(p - new Vector2(0, -0.12f), new Vector2(0.44f, 0.55f)) - 0.02f) - 0.07f;
            float slats = Mathf.Min(Box(p - new Vector2(-0.18f, -0.12f), new Vector2(0.05f, 0.38f)), Box(p - new Vector2(0.18f, -0.12f), new Vector2(0.05f, 0.38f)));
            return Mathf.Min(Mathf.Min(lid, handle), Mathf.Min(can, slats));
        }

        static float ColumnsSdf(Vector2 p)
        {
            float d = 10f;
            for (int i = 0; i < 3; i++) d = Mathf.Min(d, Box(p - new Vector2((i - 1) * 0.58f, 0), new Vector2(0.2f, 0.72f)) - 0.04f);
            return d;
        }

        static float GridSdf(Vector2 p)
        {
            float d = 10f;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
                d = Mathf.Min(d, Box(p - new Vector2(x * 0.82f - 0.41f, y * 0.82f - 0.41f), new Vector2(0.3f, 0.3f)) - 0.04f);
            return d;
        }

        static float CrownSdf(Vector2 p)
        {
            float band = Box(p - new Vector2(0, -0.48f), new Vector2(0.66f, 0.14f));
            float body = Mathf.Max(Box(p - new Vector2(0, -0.12f), new Vector2(0.66f, 0.3f)),
                                   // three points: cut V notches out of the top
                                   p.y - 0.62f + Mathf.Abs(Mathf.Sin((p.x + 0.66f) / 1.32f * Mathf.PI * 2f)) * 0.55f);
            return Mathf.Min(band, body);
        }

        static float SearchSdf(Vector2 p)
        {
            float ring = Mathf.Abs((p - new Vector2(-0.15f, 0.15f)).magnitude - 0.42f) - 0.1f;
            float handle = Segment(p, new Vector2(0.18f, -0.18f), new Vector2(0.65f, -0.65f), 0.12f);
            return Mathf.Min(ring, handle);
        }

        static float ListSdf(Vector2 p)
        {
            float d = 10f;
            for (int i = 0; i < 3; i++) d = Mathf.Min(d, Box(p - new Vector2(0.12f, (1 - i) * 0.5f), new Vector2(0.5f, 0.09f)));
            for (int i = 0; i < 3; i++) d = Mathf.Min(d, (p - new Vector2(-0.6f, (1 - i) * 0.5f)).magnitude - 0.11f);
            return d;
        }

        static float TurnArrowSdf(Vector2 p, bool mirror)
        {
            if (mirror) p.x = -p.x;
            // Three-quarter ring from the top, clockwise, ending at the left with an arrow head.
            float r = p.magnitude;
            float ang = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;   // -180..180
            float ring = Mathf.Abs(r - 0.52f) - 0.12f;
            // Cut the gap in the upper-left quadrant (between 100 and 170 degrees).
            float gap = (ang > 100f && ang < 175f) ? 1f : -1f;
            float arc = Mathf.Max(ring, gap * 0.2f);
            // Arrow head at the end of the arc, near 100 degrees, pointing left (counter to the gap).
            var tip = new Vector2(Mathf.Cos(100f * Mathf.Deg2Rad), Mathf.Sin(100f * Mathf.Deg2Rad)) * 0.52f;
            float head = Mathf.Max(Mathf.Max(
                Vector2.Dot(p - tip - new Vector2(-0.3f, 0f), new Vector2(-0.6f, 0.8f)),
                Vector2.Dot(p - tip - new Vector2(-0.3f, 0f), new Vector2(-0.6f, -0.8f))),
                Vector2.Dot(p - tip, new Vector2(1f, 0f)) - 0.02f);
            return Mathf.Min(arc, head);
        }

        static float LibrarySdf(Vector2 p)
        {
            // Three offset card backs, the top one solid.
            float top = Box(p - new Vector2(-0.1f, 0.1f), new Vector2(0.4f, 0.55f)) - 0.05f;
            float mid = Mathf.Abs(Box(p - new Vector2(0.03f, -0.03f), new Vector2(0.4f, 0.55f)) - 0.05f) - 0.05f;
            float low = Mathf.Abs(Box(p - new Vector2(0.16f, -0.16f), new Vector2(0.4f, 0.55f)) - 0.05f) - 0.05f;
            return Mathf.Min(top, Mathf.Max(Mathf.Min(mid, low), -(top - 0.08f)));
        }

        static float GraveSdf(Vector2 p)
        {
            // Rounded-top headstone on a ground line, with a small cross cut out.
            float body = Mathf.Min(Box(p - new Vector2(0, -0.22f), new Vector2(0.42f, 0.42f)), (p - new Vector2(0, 0.2f)).magnitude - 0.42f);
            body = Mathf.Max(body, -(p.y + 0.64f));
            float ground = Box(p - new Vector2(0, -0.72f), new Vector2(0.72f, 0.07f));
            float cross = Mathf.Min(Box(p - new Vector2(0, 0.05f), new Vector2(0.06f, 0.26f)), Box(p - new Vector2(0, 0.13f), new Vector2(0.19f, 0.06f)));
            return Mathf.Min(Mathf.Max(body, -cross), ground);
        }

        static float SpiralSdf(Vector2 p)
        {
            // Archimedean spiral r = a + b*theta, drawn as a thick line.
            float r = p.magnitude;
            float th = Mathf.Atan2(p.y, p.x);
            if (th < 0) th += Mathf.PI * 2f;
            const float b = 0.24f / (Mathf.PI * 2f);   // 0.24 between turns
            float best = 10f;
            for (int k = 0; k < 4; k++)
            {
                float rr = 0.08f + b * (th + k * Mathf.PI * 2f);
                if (rr > 0.86f) break;
                best = Mathf.Min(best, Mathf.Abs(r - rr));
            }
            return best - 0.055f;
        }

        static float AnvilSdf(Vector2 p)
        {
            // Face with a horn on the left, a waist, and a wide foot.
            float face = Box(p - new Vector2(0.12f, 0.36f), new Vector2(0.5f, 0.15f)) - 0.03f;
            float horn = Mathf.Max(Mathf.Max(-(p.x + 0.86f), p.x + 0.36f), Mathf.Abs(p.y - 0.42f) - (p.x + 0.86f) * 0.22f);
            float waist = Box(p - new Vector2(0.12f, 0.02f), new Vector2(0.2f, 0.22f));
            float foot = Box(p - new Vector2(0.12f, -0.38f), new Vector2(0.48f, 0.13f)) - 0.03f;
            float flare = Mathf.Max(Box(p - new Vector2(0.12f, -0.18f), new Vector2(0.36f, 0.08f)), -(p.y + 0.26f));
            return Mathf.Min(Mathf.Min(face, horn), Mathf.Min(waist, Mathf.Min(foot, flare)));
        }
    }
}

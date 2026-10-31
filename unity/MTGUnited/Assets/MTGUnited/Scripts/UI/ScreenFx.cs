using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Screen-level motion (design doc 8), kept at the edges so it never competes with card animations:
    ///  • match intro: the board fades up from black and a plate names the pod and who goes first;
    ///  • turn start: a light band sweeps the active player's area, and "Your turn" slides in and out on yours;
    /// Every animated segment fits the 400 ms budget; holds are separate.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        GameController gc;
        RectTransform root;
        Image black, sweep;
        RectTransform banner, plate;
        Text bannerText, plateTitle, plateNames;
        CanvasGroup bannerGroup, plateGroup;

        float introStart = -100f, turnStart = -100f, sweepStart = -100f;
        Rect sweepArea;
        int lastActive = -1;
        bool introPending;

        const float IntroFade = 0.4f, PlateIn = 0.25f, PlateHold = 1.2f, PlateOut = 0.25f;
        const float BannerIn = 0.22f, BannerHold = 0.46f, BannerOut = 0.22f;

        public static ScreenFx Create(RectTransform board, GameController gc)
        {
            var rt = UIKit.Rect("ScreenFx", board).Stretch();
            var f = rt.gameObject.AddComponent<ScreenFx>();
            f.gc = gc;
            f.root = rt;

            f.sweep = UIKit.Panel("Sweep", rt, Color.clear, false);
            f.sweep.raycastTarget = false;
            f.sweep.sprite = BandSprite();


            f.banner = UIKit.Rect("TurnBanner", rt);
            f.banner.Place(560, 640, 800, 110);
            f.bannerGroup = f.banner.gameObject.AddComponent<CanvasGroup>();
            f.bannerGroup.blocksRaycasts = false;
            var bbg = f.banner.gameObject.AddComponent<RawImage>();
            bbg.texture = HorizontalFade();
            bbg.raycastTarget = false;
            f.bannerText = UIKit.Label("Text", f.banner, "Your turn", 54, new Color(1f, 0.9f, 0.7f), TextAnchor.MiddleCenter, FontStyle.Bold).Outline(2f).Titled();
            f.bannerText.rectTransform.Stretch();
            f.bannerGroup.alpha = 0;

            f.black = UIKit.Panel("Black", rt, Color.clear, false);
            f.black.rectTransform.anchorMin = f.black.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            f.black.rectTransform.sizeDelta = new Vector2(4000, 3000);
            f.black.raycastTarget = false;

            f.plate = UIKit.Rect("VersusPlate", rt);
            f.plate.Place(360, 430, 1200, 220);
            f.plateGroup = f.plate.gameObject.AddComponent<CanvasGroup>();
            f.plateGroup.blocksRaycasts = false;
            var pbg = f.plate.gameObject.AddComponent<RawImage>();
            pbg.texture = HorizontalFade();
            pbg.raycastTarget = false;
            f.plateTitle = UIKit.Label("Title", f.plate, "", 24, new Color(0.95f, 0.75f, 0.45f), TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            f.plateTitle.rectTransform.Stretch(0, 0, 0, 24);
            f.plateNames = UIKit.Label("Names", f.plate, "", 40, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold).Outline(1.5f).Titled();
            f.plateNames.rectTransform.Stretch(20, 20, 20, 60);
            f.plateNames.supportRichText = true;
            f.plateNames.resizeTextForBestFit = true; f.plateNames.resizeTextMinSize = 20; f.plateNames.resizeTextMaxSize = 40;
            f.plateGroup.alpha = 0;
            return f;
        }

        /// <summary>A new match: fade up from black; the versus plate appears once the first board state arrives.</summary>
        public void MatchIntro()
        {
            introStart = Motion.Now;
            introPending = true;
            lastActive = -1;
        }

        /// <summary>Called with every state.</summary>
        public void OnState(GameState s)
        {
            if (introPending && s.Players.Count > 0)
            {
                introPending = false;
                var names = s.Players.Select((p, i) =>
                    "<color=#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(Theme.PlayerColor(i), Color.white, 0.45f)) + ">" + p.ShortName + "</color>");
                plateNames.text = string.Join("  <color=#9aa0aa>vs</color>  ", names);
                var first = s.Player(s.ActivePlayer);
                plateTitle.text = "COMMANDER" + (first != null ? "   ·   " + first.ShortName.ToUpperInvariant() + " GOES FIRST" : "");
                introStart = Motion.Now - IntroFade * 0.5f;
            }

            if (s.ActivePlayer != lastActive && s.ActivePlayer >= 0)
            {
                bool first = lastActive < 0;
                lastActive = s.ActivePlayer;
                if (!first && !s.Mulligan) TurnStarted(s);
            }
        }

        void TurnStarted(GameState s)
        {
            sweepStart = Motion.Now;
            bool mine = s.ActivePlayer == s.Me;
            var col = Theme.PlayerColor(s.Players.FindIndex(p => p.Id == s.ActivePlayer));
            sweep.color = new Color(Mathf.Lerp(col.r, 1f, 0.5f), Mathf.Lerp(col.g, 1f, 0.5f), Mathf.Lerp(col.b, 1f, 0.5f), 0f);
            // The active player's half: mine is the bottom band; an opponent's is the top band (whole width, or their column).
            sweepArea = mine ? new Rect(0, 0, 1920, 600) : new Rect(0, 600, 1920, 480);
            if (mine)
            {
                turnStart = Motion.Now;
                bannerText.text = "Your turn";
            }
        }

        void Update()
        {
            float now = Motion.Now;

            // Intro: black fades out (400 ms); the plate fades in, holds, fades out.
            float ti = now - introStart;
            black.color = new Color(0, 0, 0, ti < IntroFade ? 1f - Easing.Eval(Ease.Out, ti / IntroFade) : 0f);
            float plateT = ti - IntroFade * 0.5f;
            float pa = plateT < 0 ? 0 : plateT < PlateIn ? plateT / PlateIn : plateT < PlateIn + PlateHold ? 1f : plateT < PlateIn + PlateHold + PlateOut ? 1f - (plateT - PlateIn - PlateHold) / PlateOut : 0f;
            plateGroup.alpha = introPending ? 0f : pa;
            plate.localScale = Vector3.one * (Motion.ReduceMotion ? 1f : Mathf.Lerp(0.94f, 1f, Easing.Eval(Ease.Out, Mathf.Clamp01(plateT / PlateIn))));

            // Turn sweep: a light band crosses the active player's area in 300 ms.
            float ts = (now - sweepStart) / 0.3f;
            if (ts >= 0f && ts <= 1f && !Motion.ReduceMotion)
            {
                float x = Mathf.Lerp(sweepArea.xMin - 400f, sweepArea.xMax, Easing.Eval(Ease.InOut, ts));
                sweep.rectTransform.Place(x, sweepArea.yMin, 400f, sweepArea.height);
                var c = sweep.color; c.a = 0.22f * Mathf.Sin(ts * Mathf.PI); sweep.color = c;
            }
            else if (sweep.color.a != 0f) { var c = sweep.color; c.a = 0f; sweep.color = c; }

            // "Your turn" banner: slides in (220 ms), holds, slides out (220 ms).
            float tb = now - turnStart;
            float slide = Motion.ReduceMotion ? 0f : 60f;
            if (tb < BannerIn) { bannerGroup.alpha = tb / BannerIn; banner.anchoredPosition = new Vector2(960 - slide * (1 - Easing.Eval(Ease.Out, tb / BannerIn)), 695); }
            else if (tb < BannerIn + BannerHold) { bannerGroup.alpha = 1f; banner.anchoredPosition = new Vector2(960, 695); }
            else if (tb < BannerIn + BannerHold + BannerOut)
            {
                float k = (tb - BannerIn - BannerHold) / BannerOut;
                bannerGroup.alpha = 1f - k; banner.anchoredPosition = new Vector2(960 + slide * Easing.Eval(Ease.In, k), 695);
            }
            else bannerGroup.alpha = 0f;

        }

        static Texture2D bandTex, fadeTex;

        static Sprite BandSprite()
        {
            if (bandTex == null)
            {
                bandTex = new Texture2D(64, 1);
                for (int x = 0; x < 64; x++) bandTex.SetPixel(x, 0, new Color(1, 1, 1, Mathf.Sin(x / 63f * Mathf.PI)));
                bandTex.Apply(); bandTex.wrapMode = TextureWrapMode.Clamp;
            }
            return Sprite.Create(bandTex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Dark plate that fades out at both ends (banner / versus plate backing).</summary>
        static Texture2D HorizontalFade()
        {
            if (fadeTex != null) return fadeTex;
            fadeTex = new Texture2D(64, 1);
            for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Abs(x / 63f - 0.5f) * 2f;
                fadeTex.SetPixel(x, 0, new Color(0.02f, 0.02f, 0.04f, 0.82f * Mathf.Clamp01((1f - d) * 2.2f)));
            }
            fadeTex.Apply(); fadeTex.wrapMode = TextureWrapMode.Clamp;
            return fadeTex;
        }
    }
}

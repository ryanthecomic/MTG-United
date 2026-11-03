using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// The Limited tab (draft and sealed: start an event, how it works, your saved cubes) and its carousel slide.
    /// </summary>
    public partial class MainMenu
    {
        // Built-in slide art (credited in README.md).
        const string LimitedArt = "Visuals/Images/Gelatinous-Cube-Adventures-in-the-Forgotten-Realms-MtG-Art-Olivier Bernad";   // Olivier Bernard
        static readonly Color LimitedTint = new Color(0.62f, 0.95f, 0.45f);

        RectTransform cubeList;
        Text cubeEmpty;
        GameObject limitedSummary, limitedHow;
        Text howToggleLabel;

        RectTransform BuildLimited(RectTransform parent)
        {
            var page = UIKit.Rect("Limited", parent).Stretch();
            var shade = UIKit.Panel("Shade", page, new Color(0.02f, 0.02f, 0.04f, 0.5f), false);
            shade.rectTransform.Stretch();
            shade.raycastTarget = false;

            var icon = UIKit.Rect("Icon", page); icon.Place(76, 868, 64, 64);
            var ii = icon.gameObject.AddComponent<Image>(); ii.sprite = Icons.Grid; ii.color = LimitedTint; ii.preserveAspect = true; ii.raycastTarget = false;
            var title = UIKit.Label("Title", page, "Limited", 54, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold).Outline(1.5f).Titled();
            title.rectTransform.Place(156, 860, 800, 80);
            var sub = UIKit.Label("Sub", page, "Draft and sealed: open packs from a real set, a custom set or a cube, then build 40 cards from what you got.",
                18, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Place(80, 812, 1300, 44);

            // ---- left: start an event / how it works
            var left = Card(page, 70, 150, 860, 640);
            var art = Resources.Load<Texture2D>(LimitedArt);
            if (art != null)
            {
                // The cube, faint, behind the card's text.
                var holder = UIKit.Rect("ArtMask", left).Stretch();
                holder.gameObject.AddComponent<RectMask2D>();
                var pic = UIKit.Rect("Art", holder);
                pic.anchorMin = pic.anchorMax = new Vector2(0.5f, 0.5f);
                float k = Mathf.Max(860f / art.width, 640f / art.height);
                pic.sizeDelta = new Vector2(art.width * k, art.height * k);
                var ri = pic.gameObject.AddComponent<RawImage>();
                ri.texture = art; ri.color = new Color(0.32f, 0.32f, 0.32f, 1f); ri.raycastTarget = false;
                var fade = UIKit.Rect("Fade", holder).Stretch();
                var fi = fade.gameObject.AddComponent<RawImage>();
                fi.texture = UIKit.Gradient(new Color(0, 0, 0, 0.2f), new Color(0, 0, 0, 0.75f));
                fi.raycastTarget = false;
            }

            // Summary (default view).
            var summary = UIKit.Rect("Summary", left).Stretch();
            limitedSummary = summary.gameObject;
            var st = UIKit.Label("Title", summary, "DRAFT AND SEALED", 16, LimitedTint, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            st.rectTransform.Place(30, 590, 800, 30);
            var sb = UIKit.Label("Body", summary,
                "<b>Draft:</b> three packs of 15. Take one card, pass the rest; bots fill the empty seats.\n\n" +
                "<b>Sealed:</b> six packs each, all yours.\n\n" +
                "Packs can come from an official set (by its code), a custom set from Magic Set Editor, or one of your cubes. " +
                "Then build 40 cards from what you opened and play them.\n\n" +
                "<b>With friends:</b> host a LAN table and press DRAFT / SEALED in the lobby.",
                19, new Color(0.88f, 0.9f, 0.9f), TextAnchor.UpperLeft);
            sb.rectTransform.Place(30, 150, 800, 430);
            sb.supportRichText = true;
            UIKit.Pill("Start", summary, "Draft / Sealed", new Color(0.95f, 0.43f, 0.1f), () => LimitedSetup.Instance.Open(false), 30, 30, 340, 80, 30);
            var lan = UIKit.Button("Lan", summary, "WITH FRIENDS (LAN)", Theme.Secondary, () => gc.Lan.Open(SelectedDeck), 17);
            ((RectTransform)lan.transform).Place(400, 44, 260, 52);

            // How it works (the small button swaps the card's text).
            var how = UIKit.Rect("How", left).Stretch();
            limitedHow = how.gameObject;
            var ht = UIKit.Label("Title", how, "HOW IT WORKS", 16, LimitedTint, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            ht.rectTransform.Place(30, 590, 800, 30);
            var hb = UIKit.Label("Body", how,
                "<b>1. Set up.</b> Press Draft / Sealed. For each pack choose what it's made of: an official set (type its code, like MKM; " +
                "cards and rarities come from Scryfall), one of your custom sets, or a cube. CARDS… picks which cards can be opened.\n\n" +
                "<b>2. Packs.</b> Sets with rarities open like real boosters: 1 rare (sometimes a mythic), 3 uncommons, 10 commons. " +
                "Cubes give 15 different cards, and each cube card is opened once per event.\n\n" +
                "<b>3. Draft.</b> Click a card, click it again (or PICK) to take it. Packs go left, right, left. " +
                "Bots pick good cards and stay in their colours.\n\n" +
                "<b>4. Build.</b> BUILD YOUR DECK opens the builder with only the cards you opened (the diamonds show how many) " +
                "plus all the basic lands you want. Make 40 cards, then play it against Forge or at the table.\n\n" +
                "<b>Cubes</b> are lists, one card per line. Import a .txt, paste one, or keep any deck of 45+ cards as a cube.",
                17, new Color(0.88f, 0.9f, 0.9f), TextAnchor.UpperLeft);
            hb.rectTransform.Place(30, 90, 800, 490);
            hb.supportRichText = true;
            how.gameObject.SetActive(false);

            var toggle = UIKit.Button("HowToggle", left, "HOW IT WORKS", new Color(0.24f, 0.55f, 0.16f), ToggleLimitedHow, 16);
            ((RectTransform)toggle.transform).Place(860 - 24 - 210, 580, 210, 44);
            var rim = toggle.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(0.75f, 1f, 0.6f, 0.6f);
            rim.effectDistance = new Vector2(1.5f, -1.5f);
            howToggleLabel = UIKit.LabelOf(toggle);

            // ---- right: my cubes
            var cubes = Card(page, 960, 150, 890, 640);
            var ct = UIKit.Label("Title", cubes, "MY CUBES", 16, LimitedTint, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            ct.rectTransform.Place(30, 590, 830, 30);
            var view = UIKit.Rect("Scroll", cubes);
            view.Place(30, 120, 830, 456);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.2f);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            cubeList = UIKit.Rect("List", view);
            cubeList.anchorMin = new Vector2(0, 1); cubeList.anchorMax = new Vector2(1, 1); cubeList.pivot = new Vector2(0.5f, 1);
            sr.content = cubeList; sr.horizontal = false; sr.scrollSensitivity = 40; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);
            cubeEmpty = UIKit.Label("Empty", view, "No cubes yet. Import a .txt list or paste one (one card per line).", 17, Theme.TextDim, TextAnchor.MiddleCenter);
            cubeEmpty.rectTransform.Stretch(30, 0, 30, 0);
            UIKit.Pill("Import", cubes, "Import a cube", new Color(0.3f, 0.5f, 0.16f), ImportCubeFile, 30, 26, 300, 70, 26);
            var paste = UIKit.Button("Paste", cubes, "PASTE A LIST", Theme.Secondary, PasteCube, 17);
            ((RectTransform)paste.transform).Place(350, 36, 230, 52);
            var fromDeck = UIKit.Button("FromDeck", cubes, "FROM A DECK", Theme.Secondary, CubeFromDeckMenu, 17);
            ((RectTransform)fromDeck.transform).Place(600, 36, 260, 52);

            CubeStore.Changed += RefreshCubes;
            RefreshCubes();
            return page;
        }

        void ToggleLimitedHow()
        {
            bool showHow = !limitedHow.activeSelf;
            limitedHow.SetActive(showHow);
            limitedSummary.SetActive(!showHow);
            howToggleLabel.text = showHow ? "BACK" : "HOW IT WORKS";
            Sound.Play(Sfx.UiClick, 0.6f);
        }

        /// <summary>Opens the Limited tab, optionally on its "how it works" text.</summary>
        void ShowLimitedTab(bool how)
        {
            SelectTab("limited");
            if (limitedHow != null && limitedHow.activeSelf != how) ToggleLimitedHow();
        }

        void RefreshCubes()
        {
            if (cubeList == null) return;
            foreach (Transform c in cubeList) Destroy(c.gameObject);
            var all = CubeStore.LoadAll();
            cubeEmpty.gameObject.SetActive(all.Count == 0);
            float y = -6;
            foreach (var cube in all)
            {
                var c = cube;
                var row = UIKit.Panel("Row", cubeList, new Color(1f, 1f, 1f, 0.05f));
                var rr = row.rectTransform;
                rr.anchorMin = new Vector2(0, 1); rr.anchorMax = new Vector2(1, 1); rr.pivot = new Vector2(0.5f, 1);
                rr.sizeDelta = new Vector2(-28, 56); rr.anchoredPosition = new Vector2(-8, y);
                var n = UIKit.Label("Name", row.transform, c.Name + "   <color=#9aa0aa><size=14>" + c.Count + " " + Loc.T("cards") + "</size></color>",
                    18, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                n.rectTransform.Stretch(16, 0, 330, 0);
                n.supportRichText = true;
                if (n is SymbolText ns) ns.Localize = false;
                var draft = UIKit.Button("Draft", row.transform, "DRAFT", new Color(0.62f, 0.33f, 0.12f), () => LimitedSetup.Instance.OpenWithCube(c, false), 14);
                ((RectTransform)draft.transform).Place(0, 8, 100, 40);
                AnchorRight((RectTransform)draft.transform, -224);
                var export = UIKit.Button("Export", row.transform, "EXPORT", Theme.Secondary, () => ExportCube(c), 14);
                ((RectTransform)export.transform).Place(0, 8, 100, 40);
                AnchorRight((RectTransform)export.transform, -116);
                var del = UIKit.Button("Delete", row.transform, "DELETE", ConfirmDialog.Danger, () =>
                    ConfirmDialog.Instance.Show(Loc.T("Delete this cube?"), c.Name,
                        (Loc.T("Delete"), ConfirmDialog.Danger, () => CubeStore.Delete(c)),
                        (Loc.T("Cancel"), Theme.Secondary, null)), 14);
                ((RectTransform)del.transform).Place(0, 8, 100, 40);
                AnchorRight((RectTransform)del.transform, -8);
                y -= 62;
            }
            cubeList.sizeDelta = new Vector2(0, -y + 6);
        }

        static void AnchorRight(RectTransform rt, float x)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
        }

        void ImportCubeFile()
        {
            DeckFiles.Import(gc.Toasts, (model, name) =>
            {
                var tmp = new SavedDeck();
                model.WriteTo(tmp);
                var saved = CubeStore.Save(name, tmp.List);
                gc.Toasts.Show(Loc.T("Cube saved") + ": " + saved.Name + " (" + saved.Count + ")");
            });
        }

        void PasteCube()
        {
            string text = GUIUtility.systemCopyBuffer ?? "";
            var model = DeckTextImport.Parse(text, DeckTextFormat.Plain);
            int n = model.Cards.Sum(c => c.count);
            if (n < 15) { gc.Toasts.Show(Loc.T("Copy a cube list (one card per line) first."), true); return; }
            var saved = CubeStore.Save("Cube " + DateTime.Now.ToString("dd-MM HH.mm"), text);
            gc.Toasts.Show(Loc.T("Cube saved") + ": " + saved.Name + " (" + saved.Count + ")");
        }

        void CubeFromDeckMenu()
        {
            var items = new List<(string, Action)> { ("KEEP A DECK AS A CUBE", null) };
            foreach (var d in decks.Where(dk => dk.CardCount >= 45).Take(16))
            {
                var dk = d;
                items.Add((dk.Name + " (" + dk.CardCount + ")", () =>
                {
                    var saved = CubeStore.Save(dk.Name, dk.List);
                    gc.Toasts.Show(Loc.T("Cube saved") + ": " + saved.Name + " (" + saved.Count + ")");
                }));
            }
            if (items.Count == 1) { gc.Toasts.Show(Loc.T("No deck with 45 or more cards.")); return; }
            PopupMenu.Instance.Show(Input.mousePosition, items);
        }

        void ExportCube(SavedCube c)
        {
            ForgeSetup.BrowseSaveFile("Export cube", "Card list (*.txt)|*.txt|All files (*.*)|*.*", c.Name + ".txt", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    System.IO.File.WriteAllText(path, c.List.Replace("\n", Environment.NewLine), new System.Text.UTF8Encoding(false));
                    gc.Toasts.Show("Exported to " + System.IO.Path.GetFileName(path));
                }
                catch (Exception ex) { gc.Toasts.Show("Couldn't export: " + ex.Message, true); }
            });
        }

        // ---------------------------------------------------------------- carousel slide

        RectTransform BuildLimitedSlide(RectTransform hero)
        {
            var s = UIKit.Rect("LimitedSlide", hero).Stretch();
            var bg = s.gameObject.AddComponent<RawImage>();
            bg.texture = UIKit.Gradient(new Color(0.08f, 0.13f, 0.06f), new Color(0.02f, 0.04f, 0.02f));
            s.gameObject.AddComponent<PointerEvents>().OnClick = () => { Sound.Play(Sfx.UiClick, 0.6f); ShowLimitedTab(false); };
            var mark = UIKit.Rect("Mark", s);
            mark.Place(700, 150, 420, 420);
            var mi = mark.gameObject.AddComponent<Image>();
            mi.sprite = Icons.Grid; mi.color = new Color(0.6f, 1f, 0.45f, 0.12f); mi.raycastTarget = false; mi.preserveAspect = true;
            var k = UIKit.Label("Kicker", s, "NEW · LIMITED", 16, LimitedTint, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            k.rectTransform.Place(50, 590, 600, 30);
            var t = UIKit.Label("Title", s, "Draft and Sealed", 56, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold).Outline(1.5f).Titled();
            t.rectTransform.Place(46, 510, 900, 80);
            var b = UIKit.Label("Body", s,
                "Open packs from an official set, your custom sets or a cube. Draft against bots or with friends on your network, " +
                "then build 40 cards from what you opened.",
                21, new Color(0.88f, 0.92f, 0.86f), TextAnchor.UpperLeft);
            b.rectTransform.Place(52, 330, 640, 170);
            UIKit.Pill("Learn", s, "How it works", new Color(0.3f, 0.5f, 0.16f), () => ShowLimitedTab(true), 50, 70, 320, 70, 28);
            var go = UIKit.Button("Start", s, "DRAFT / SEALED", Theme.Secondary, () => LimitedSetup.Instance.Open(false), 18);
            ((RectTransform)go.transform).Place(400, 80, 240, 52);
            var tex = Resources.Load<Texture2D>(CarouselFolder + "/limited") ?? Resources.Load<Texture2D>(LimitedArt);
            if (tex != null) { AddCover(s, tex, 0.78f, 0.5f); mark.gameObject.SetActive(false); }
            return s;
        }
    }
}

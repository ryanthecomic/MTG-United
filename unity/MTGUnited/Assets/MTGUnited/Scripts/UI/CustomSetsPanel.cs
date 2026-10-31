using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Your imported custom sets (Magic Set Editor → Cockatrice export). Import adds a set and its images to MTG
    /// United's library; the cards then show up in the deck builder search and play at the kitchen table.
    /// </summary>
    public class CustomSetsPanel : MonoBehaviour
    {
        GameController gc;
        RectTransform list;
        Text summary;

        public bool IsOpen => gameObject.activeSelf;

        public static CustomSetsPanel Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("CustomSets", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.Stretch();
            var c = shade.gameObject.AddComponent<CustomSetsPanel>();
            c.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = c.Close;
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.08f, 0.12f, 0.98f));
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(1000, 720);
            p.gameObject.AddComponent<PointerEvents>();
            var t = UIKit.Label("Title", p.transform, "CUSTOM SETS", 28, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 40, 26);
            var sub = UIKit.Label("Sub", p.transform,
                "In Magic Set Editor use File → Export → Cockatrice, then import the .xml here. Card images from the export are found and copied automatically. " +
                "Custom cards appear in deck builder searches and play at the kitchen table (Forge rules games use real cards only).",
                16, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Stretch(40, 0, 40, 72);
            c.summary = UIKit.Label("Summary", p.transform, "", 16, Theme.Text, TextAnchor.UpperLeft);
            c.summary.rectTransform.Place(40, 552, 900, 26);

            var scroll = UIKit.Rect("Scroll", p.transform);
            scroll.Place(40, 110, 920, 434);
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            c.list = UIKit.Rect("List", scroll);
            c.list.anchorMin = new Vector2(0, 1); c.list.anchorMax = new Vector2(1, 1); c.list.pivot = new Vector2(0.5f, 1);
            var vl = c.list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 6; vl.padding = new RectOffset(8, 8, 8, 8); vl.childControlHeight = false; vl.childControlWidth = true;
            c.list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = c.list; sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);

            UIKit.Pill("Import", p.transform, "Import set (.xml)", new Color(0.62f, 0.33f, 0.12f), c.Import, 40, 28, 340, 64, 24);
            var folder = UIKit.Button("Folder", p.transform, "OPEN SETS FOLDER", Theme.Secondary, () =>
            {
                System.IO.Directory.CreateDirectory(CustomSets.Root);
                ForgeSetup.OpenFolder(CustomSets.Root);
            }, 16);
            ((RectTransform)folder.transform).Place(700, 34, 260, 52);
            UIKit.CloseX(p.transform, c.Close);
            shade.gameObject.SetActive(false);
            return c;
        }

        public void Open()
        {
            Refresh();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Short);
        }

        public void Close() => gameObject.SetActive(false);

        public void Import()
        {
            ForgeSetup.BrowseFile("Choose a Cockatrice set export (.xml)", "Cockatrice card database (*.xml)|*.xml|All files (*.*)|*.*", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                string report = CustomSets.Import(path);
                gc.Toasts.Show(report, report.StartsWith("Couldn't") || report.StartsWith("No cards") || report.StartsWith("File"));
                Refresh();
            });
        }

        void Refresh()
        {
            foreach (Transform t in list) Destroy(t.gameObject);
            var sets = CustomSets.All;
            int cards = 0;
            foreach (var s in sets) cards += s.Cards.Count;
            summary.text = sets.Count == 0 ? "No custom sets yet." : sets.Count + (sets.Count == 1 ? " set" : " sets") + ", " + cards + " cards.";
            foreach (var s in sets)
            {
                var set = s;
                var row = UIKit.Panel("Set", list, new Color(1, 1, 1, 0.05f));
                row.rectTransform.sizeDelta = new Vector2(0, 64);
                var name = UIKit.Label("Name", row.transform, "<b>" + set.Name + "</b>  <color=#9aa0aa>" + set.Code + "</color>", 20, Theme.Text, TextAnchor.UpperLeft);
                name.rectTransform.Stretch(16, 0, 200, 8);
                var info = UIKit.Label("Info", row.transform, set.Cards.Count + " cards  ·  " + set.ImageCount + " with images  ·  " +
                    set.Cards.FindAll(x => x.Token).Count + " tokens", 14, Theme.TextDim, TextAnchor.LowerLeft);
                info.rectTransform.Stretch(16, 8, 200, 0);
                var remove = UIKit.Button("Remove", row.transform, "REMOVE", new Color(0.45f, 0.14f, 0.14f), () =>
                {
                    CustomSets.Remove(set);
                    gc.Toasts.Show("Removed " + set.Name + ".");
                    Refresh();
                }, 15);
                var r = (RectTransform)remove.transform;
                r.anchorMin = r.anchorMax = new Vector2(1, 0.5f); r.pivot = new Vector2(1, 0.5f);
                r.sizeDelta = new Vector2(150, 42); r.anchoredPosition = new Vector2(-12, 0);
            }
        }
    }
}

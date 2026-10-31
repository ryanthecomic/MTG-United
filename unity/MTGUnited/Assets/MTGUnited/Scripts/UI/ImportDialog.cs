using System;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// "Import a deck": pick where the list comes from (MTGO, Moxfield, Archidekt, plain text), and it's read from
    /// the clipboard. "From a file..." is still there for .txt / .dck / .dec files.
    /// </summary>
    public class ImportDialog : MonoBehaviour
    {
        public static ImportDialog Instance { get; private set; }
        GameController gc;
        RectTransform panel;
        Text clipText;
        Action<DeckModel, string> onImport;

        public bool IsOpen => gameObject.activeSelf;

        static readonly (DeckTextFormat format, string title, string how)[] Formats =
        {
            (DeckTextFormat.Mtgo, "MTGO Format", "\"4 Lightning Bolt\" lines; the sideboard after a blank line (commander there for Commander decks)."),
            (DeckTextFormat.Moxfield, "Moxfield Format", "Export → Copy for Moxfield / MTGO / Arena. Set codes and foil marks are ignored."),
            (DeckTextFormat.Archidekt, "Archidekt Format", "Export → Copy. The [Commander] category becomes the commander; maybeboard is left out."),
            (DeckTextFormat.Plain, "Plain Text", "One card per line, with or without a number in front."),
        };

        public static ImportDialog Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("ImportDialog", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(4000, 3000);
            var d = shade.gameObject.AddComponent<ImportDialog>();
            Instance = d;
            d.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = d.Close;

            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.08f, 0.12f, 0.98f));
            d.panel = p.rectTransform;
            d.panel.anchorMin = d.panel.anchorMax = new Vector2(0.5f, 0.5f);
            d.panel.sizeDelta = new Vector2(760, 690);
            p.gameObject.AddComponent<PointerEvents>();
            UiScaled.Add(p, Vector2.zero).MaxScale = 1040f / 690f;

            var t = UIKit.Label("Title", p.transform, "IMPORT A DECK", 28, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 80, 28);
            var sub = UIKit.Label("Sub", p.transform, "Copy the deck list on the site (or in MTGO), then pick where it came from. It's read from your clipboard.",
                16, Theme.TextDim, TextAnchor.UpperLeft);
            sub.rectTransform.Stretch(40, 0, 40, 74);
            d.clipText = UIKit.Label("Clip", p.transform, "", 14, new Color(0.75f, 0.8f, 0.9f), TextAnchor.UpperLeft);
            d.clipText.rectTransform.Place(40, 532, 680, 40);

            float y = 430;
            foreach (var (format, title, how) in Formats)
            {
                var f = format;
                var b = UIKit.Button(title, p.transform, "", new Color(0.13f, 0.15f, 0.21f), () => d.FromClipboard(f), 18);
                ((RectTransform)b.transform).Place(40, y, 680, 88);
                var bt = UIKit.Label("T", b.transform, title, 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                bt.rectTransform.Stretch(22, 0, 22, 12);
                var bh = UIKit.Label("H", b.transform, how, 14, Theme.TextDim, TextAnchor.LowerLeft);
                bh.rectTransform.Stretch(22, 12, 22, 0);
                y -= 100;
            }
            var file = UIKit.Button("File", p.transform, "FROM A FILE (.txt / .dck / .dec)...", Theme.Secondary, () =>
            {
                var cb = d.onImport;
                d.Close();
                DeckFiles.Import(gc.Toasts, (m, n) => cb?.Invoke(m, n));
            }, 16);
            ((RectTransform)file.transform).Place(40, 24, 420, 46);
            UIKit.CloseX(p.transform, d.Close);
            shade.gameObject.SetActive(false);
            return d;
        }

        /// <summary>Opens the dialog; <paramref name="done"/> gets the cards read and a suggested deck name.</summary>
        public void Open(Action<DeckModel, string> done)
        {
            onImport = done;
            string clip = GUIUtility.systemCopyBuffer ?? "";
            int lines = 0;
            foreach (var l in clip.Split('\n')) if (l.Trim().Length > 0) lines++;
            clipText.text = lines == 0 ? "<color=#f7a540>Your clipboard is empty: copy a deck list first.</color>"
                : "Clipboard: " + lines + " lines, starting with \"" + Short(clip.Trim().Split('\n')[0].Trim()) + "\"";
            clipText.supportRichText = true;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(panel.gameObject, Motion.Short, 0.95f);
            Sound.Play(Sfx.Popup, 0.5f);
        }

        static string Short(string s) => s.Length > 48 ? s.Substring(0, 47) + "…" : s;

        void FromClipboard(DeckTextFormat format)
        {
            string clip = GUIUtility.systemCopyBuffer ?? "";
            if (clip.Trim().Length == 0) { gc.Toasts.Show("Your clipboard is empty: copy the deck list first.", true); return; }
            var model = DeckTextImport.Parse(clip, format);
            if (model.Total == 0 && model.Commanders.Count == 0)
            {
                gc.Toasts.Show("No cards found in the clipboard for that format.", true);
                return;
            }
            var cb = onImport;
            Close();
            cb?.Invoke(model, "Imported Deck");
        }

        public void Close()
        {
            gameObject.SetActive(false);
            onImport = null;
        }
    }
}

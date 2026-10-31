using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MTGUnited
{
    public enum DeckTextFormat { Mtgo, Moxfield, Archidekt, Plain }

    /// <summary>
    /// Reads deck lists copied from the usual places:
    ///  • MTGO: "4 Lightning Bolt" lines; a blank line starts the sideboard (a 1–2 card sideboard on a 98+ card
    ///    deck is the commander, which is how MTGO exports Commander decks).
    ///  • Moxfield: "1 Sol Ring (C21) 263 *F*", with optional Commander / Deck / Sideboard headers.
    ///  • Archidekt: "1x Sol Ring (c21) 263 [Ramp]" — the [Commander] category marks commanders,
    ///    [Sideboard] / [Maybeboard] are left out.
    ///  • Plain text: one card per line, with or without a count.
    /// </summary>
    public static class DeckTextImport
    {
        static readonly Regex CountRe = new Regex(@"^\s*(\d+)\s*[xX]?\s+(.+)$");

        public static DeckModel Parse(string text, DeckTextFormat format)
        {
            var model = new DeckModel();
            if (string.IsNullOrWhiteSpace(text)) return model;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            switch (format)
            {
                case DeckTextFormat.Mtgo: ParseMtgo(lines, model); break;
                case DeckTextFormat.Archidekt: ParseArchidekt(lines, model); break;
                case DeckTextFormat.Moxfield: ParseSections(lines, model, true); break;
                default: ParseSections(lines, model, false); break;
            }
            return model;
        }

        // ---------------------------------------------------------------- formats

        static void ParseMtgo(string[] lines, DeckModel model)
        {
            var main = new List<(int, string)>();
            var side = new List<(int, string)>();
            bool inSide = false, sawCard = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) { if (sawCard) inSide = true; continue; }
                string header = Header(line);
                if (header == "sideboard") { inSide = true; continue; }
                if (header != null) continue;
                if (!TryCard(line, out int n, out string name)) continue;
                (inSide ? side : main).Add((n, name));
                sawCard = true;
            }
            int mainTotal = 0;
            foreach (var (n, _) in main) mainTotal += n;
            int sideTotal = 0;
            foreach (var (n, _) in side) sideTotal += n;
            bool sideIsCommander = side.Count > 0 && sideTotal <= 2 && mainTotal >= 97;
            foreach (var (n, name) in main) model.Add(name, n);
            if (sideIsCommander) foreach (var (_, name) in side) model.Commanders.Add(name);
        }

        /// <summary>Header-driven lists (Moxfield, Arena-style, plain): Commander / Deck / Sideboard sections.</summary>
        static void ParseSections(string[] lines, DeckModel model, bool blankStartsSideboard)
        {
            string section = "deck";
            bool sawHeader = false, sawCard = false;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                {
                    // Moxfield's MTGO-style copy has no headers: a blank line then the sideboard.
                    if (blankStartsSideboard && !sawHeader && sawCard) section = "sideboard";
                    continue;
                }
                string header = Header(line);
                if (header != null) { section = header; sawHeader = true; continue; }
                if (!TryCard(line, out int n, out string name)) continue;
                sawCard = true;
                if (section == "commander") model.Commanders.Add(name);
                else if (section == "deck") model.Add(name, n);
            }
        }

        static readonly Regex CategoryRe = new Regex(@"\[([^\]]*)\]");

        static void ParseArchidekt(string[] lines, DeckModel model)
        {
            string section = "deck";
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string header = Header(line);
                if (header != null) { section = header; continue; }
                // Categories in [brackets]: [Commander{top}], [Ramp,Draw], [Sideboard], [Maybeboard]...
                string cats = "";
                foreach (Match m in CategoryRe.Matches(line)) cats += m.Groups[1].Value + ",";
                if (!TryCard(line, out int n, out string name)) continue;
                string lc = cats.ToLowerInvariant();
                if (lc.Contains("maybeboard") || lc.Contains("sideboard") || lc.Contains("considering")) continue;
                if (lc.Contains("commander") || section == "commander") model.Commanders.Add(name);
                else if (section == "deck") model.Add(name, n);
            }
        }

        // ---------------------------------------------------------------- lines

        /// <summary>Section headers in any of the usual spellings ("Commander", "COMMANDER:", "// Sideboard", "Deck").</summary>
        static string Header(string line)
        {
            string l = line.Trim().TrimStart('/', '#', ' ').TrimEnd(':', ' ').ToLowerInvariant();
            if (CountRe.IsMatch(line)) return null;
            switch (l)
            {
                case "commander": case "commanders": return "commander";
                case "deck": case "main": case "mainboard": case "main deck": case "maindeck": return "deck";
                case "sideboard": case "side": return "sideboard";
                case "maybeboard": case "considering": case "tokens": case "companion": return "skip";
            }
            if (l.StartsWith("commander (")) return "commander";      // "Commander (1)"
            if (l.StartsWith("sideboard (") || l.StartsWith("maybeboard (")) return "sideboard";
            if (l.StartsWith("deck (") || l.StartsWith("mainboard (")) return "deck";
            return line.StartsWith("//") || line.StartsWith("#") ? "deck" : null;   // other comments: ignore, stay in the deck
        }

        /// <summary>"4 Name", "4x Name", "Name"; drops "(SET) 123", "*F*", "[categories]", "^tags^" and "#notes".</summary>
        static bool TryCard(string line, out int count, out string name)
        {
            count = 1;
            var m = CountRe.Match(line);
            if (m.Success) { count = Math.Max(1, int.Parse(m.Groups[1].Value)); line = m.Groups[2].Value; }
            line = CategoryRe.Replace(line, "");
            int caret = line.IndexOf('^');
            if (caret > 0) line = line.Substring(0, caret);
            int paren = line.IndexOf(" (");
            if (paren > 0) line = line.Substring(0, paren);
            int star = line.IndexOf(" *");
            if (star > 0) line = line.Substring(0, star);
            int note = line.IndexOf(" #");
            if (note > 0) line = line.Substring(0, note);
            name = line.Trim();
            // A double-faced card written as "Front / Back" (one slash): keep the front, the way Forge names it.
            if (name.Contains(" / ") && !name.Contains(" // ")) name = name.Substring(0, name.IndexOf(" / ")).Trim();
            return name.Length > 0;
        }
    }
}

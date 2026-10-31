using System;
using System.IO;
using System.Text;

namespace MTGUnited
{
    /// <summary>
    /// Deck lists as files: export (.txt Arena/Moxfield style, or Forge's .dck) and import (.txt, .dck, .dec).
    /// Used by the deck builder and the My Decks page.
    /// </summary>
    public static class DeckFiles
    {
        public const string SaveFilter = "Deck list (*.txt)|*.txt|Forge deck (*.dck)|*.dck|All files (*.*)|*.*";
        public const string OpenFilter = "Deck lists (*.txt;*.dck;*.dec)|*.txt;*.dck;*.dec|All files (*.*)|*.*";

        /// <summary>Asks where to save, then writes the deck there. Reports through the toast.</summary>
        public static void Export(string name, DeckModel model, Toasts toasts)
        {
            ForgeSetup.BrowseSaveFile("Export deck", SaveFilter, name + ".txt", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    string text;
                    if (path.EndsWith(".dck", StringComparison.OrdinalIgnoreCase)) text = ToForgeDck(name, model);
                    else { var tmp = new SavedDeck(); model.WriteTo(tmp); text = tmp.List; }
                    File.WriteAllText(path, text.Replace("\n", Environment.NewLine), new UTF8Encoding(false));
                    toasts?.Show("Exported to " + Path.GetFileName(path));
                }
                catch (Exception ex) { toasts?.Show("Couldn't export: " + ex.Message, true); }
            });
        }

        /// <summary>Asks for a file and hands back its cards (null if cancelled or empty) and a name from the file.</summary>
        public static void Import(Toasts toasts, Action<DeckModel, string> done)
        {
            ForgeSetup.BrowseFile("Import a deck list", OpenFilter, path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                string text;
                try { text = File.ReadAllText(path); }
                catch (Exception ex) { toasts?.Show("Couldn't read the file: " + ex.Message, true); return; }
                string name = Path.GetFileNameWithoutExtension(path);
                text = FromForgeDck(text, ref name);
                var model = DeckModel.From(new SavedDeck { List = text });
                if (model.Total == 0 && model.Commanders.Count == 0) { toasts?.Show("No cards found in " + Path.GetFileName(path) + ".", true); return; }
                done(model, name);
            });
        }

        public static string ToForgeDck(string name, DeckModel model)
        {
            var sb = new StringBuilder();
            sb.Append("[metadata]\nName=").Append(name).Append('\n');
            if (model.Commanders.Count > 0)
            {
                sb.Append("[Commander]\n");
                foreach (var c in model.Commanders) sb.Append("1 ").Append(c).Append('\n');
            }
            sb.Append("[Main]\n");
            foreach (var (count, n) in model.Cards) sb.Append(count).Append(' ').Append(n).Append('\n');
            return sb.ToString();
        }

        /// <summary>Forge's .dck sections ([Commander], [Main], [Sideboard]) as the plain sections the parser reads.</summary>
        public static string FromForgeDck(string text, ref string name)
        {
            if (text.IndexOf("[Main]", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("[metadata]", StringComparison.OrdinalIgnoreCase) < 0) return text;
            var sb = new StringBuilder();
            string section = "";
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    section = line.Trim('[', ']').ToLowerInvariant();
                    if (section == "commander") sb.Append("Commander\n");
                    else if (section == "main") sb.Append("\nDeck\n");
                    else if (section == "sideboard") sb.Append("\nSideboard\n");
                    continue;
                }
                if (line.Length == 0) continue;
                if (section == "metadata")
                {
                    if (line.StartsWith("Name=", StringComparison.OrdinalIgnoreCase) && line.Length > 5) name = line.Substring(5).Trim();
                    continue;
                }
                int bar = line.IndexOf('|');
                if (bar > 0) line = line.Substring(0, bar);   // "1 Sol Ring|C21" -> "1 Sol Ring"
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
    }
}

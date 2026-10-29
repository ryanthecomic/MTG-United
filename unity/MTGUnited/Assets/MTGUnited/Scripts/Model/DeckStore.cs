using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MTGUnited
{
    public class SavedDeck
    {
        public string Id;
        public string Name = "New Deck";
        public string Commander = "";   // optional override, ';' separates partners
        public string List = "";        // pasted decklist text
        public long Updated;

        /// <summary>Commander name for art and labels: the override, else the first card under a "Commander" heading.</summary>
        public string CommanderName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Commander)) return Commander.Split(';')[0].Trim();
                bool inSection = false;
                foreach (var raw in (List ?? "").Split('\n'))
                {
                    string line = raw.Trim();
                    string lower = line.ToLowerInvariant().TrimEnd(':');
                    if (lower == "commander" || lower == "commanders") { inSection = true; continue; }
                    if (!inSection) continue;
                    if (line.Length == 0) { if (inSection) break; continue; }
                    return StripCount(line);
                }
                return null;
            }
        }

        public int CardCount
        {
            get
            {
                int n = 0;
                foreach (var raw in (List ?? "").Split('\n'))
                {
                    string line = raw.Trim();
                    int sp = line.IndexOf(' ');
                    if (sp <= 0) continue;
                    if (int.TryParse(line.Substring(0, sp).TrimEnd('x', 'X'), out int c)) n += c;
                }
                return n;
            }
        }

        static string StripCount(string line)
        {
            int sp = line.IndexOf(' ');
            if (sp > 0 && int.TryParse(line.Substring(0, sp).TrimEnd('x', 'X'), out _)) line = line.Substring(sp + 1);
            int paren = line.IndexOf(" (");
            if (paren > 0) line = line.Substring(0, paren);
            return line.Trim();
        }
    }

    /// <summary>Decks saved as small JSON files under the app's persistent data folder.</summary>
    public static class DeckStore
    {
        static string Dir => Path.Combine(Application.persistentDataPath, "decks");

        public static List<SavedDeck> LoadAll()
        {
            Directory.CreateDirectory(Dir);
            var list = new List<SavedDeck>();
            foreach (var file in Directory.GetFiles(Dir, "*.json"))
            {
                try
                {
                    var d = Json.ParseObject(File.ReadAllText(file));
                    list.Add(new SavedDeck
                    {
                        Id = Path.GetFileNameWithoutExtension(file),
                        Name = d.Str("name", "Deck"),
                        Commander = d.Str("commander"),
                        List = d.Str("list"),
                        Updated = (long)(d.TryGetValue("updated", out var u) && u is double ud ? ud : 0),
                    });
                }
                catch (Exception ex) { Debug.LogWarning("[MTGUnited] Skipping unreadable deck " + file + ": " + ex.Message); }
            }

            // First run: bring over the single deck the old menu kept in PlayerPrefs.
            if (list.Count == 0 && !string.IsNullOrWhiteSpace(PlayerPrefs.GetString("deck", "")))
            {
                var migrated = new SavedDeck { Name = "My Deck", List = PlayerPrefs.GetString("deck", ""), Commander = PlayerPrefs.GetString("commander", "") };
                Save(migrated);
                list.Add(migrated);
            }
            list.Sort((a, b) => b.Updated.CompareTo(a.Updated));
            return list;
        }

        public static void Save(SavedDeck deck)
        {
            Directory.CreateDirectory(Dir);
            if (string.IsNullOrEmpty(deck.Id)) deck.Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            deck.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var d = new Dictionary<string, object>
            {
                { "name", deck.Name }, { "commander", deck.Commander }, { "list", deck.List }, { "updated", deck.Updated },
            };
            File.WriteAllText(Path.Combine(Dir, deck.Id + ".json"), Json.Write(d));
        }

        public static void Delete(SavedDeck deck)
        {
            if (string.IsNullOrEmpty(deck.Id)) return;
            var path = Path.Combine(Dir, deck.Id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>Simple local win/loss record shown on the Profile tab.</summary>
    public static class PlayerStats
    {
        public static int Games => PlayerPrefs.GetInt("statGames", 0);
        public static int Wins => PlayerPrefs.GetInt("statWins", 0);
        public static int Losses => Games - Wins;

        public static void Record(bool won)
        {
            PlayerPrefs.SetInt("statGames", Games + 1);
            if (won) PlayerPrefs.SetInt("statWins", Wins + 1);
            PlayerPrefs.Save();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>One card from an imported custom set.</summary>
    public class CustomCard
    {
        public string Name, Set, ManaCost = "", Type = "", Colors = "", PT = "", Loyalty = "", Text = "", PicUrl, ImagePath;
        public int Cmc;
        public bool Token;
        public readonly List<string> Related = new List<string>();

        public string MainType = "";

        // Type words in English, Portuguese and Spanish (MSE sets are often written in the designer's language).
        static readonly string[] LandWords = { "Land", "Terreno" };
        static readonly string[] CreatureWords = { "Creature", "Criatura" };
        static readonly string[] LegendaryWords = { "Legendary", "Lendári", "Legendari" };
        static readonly string[] BasicWords = { "Basic", "Básic", "Basic" };
        static bool Has(string text, string[] words)
        {
            foreach (var w in words) if (text.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
        string TypeText => Type + " " + MainType;

        // Front face decides for split / adventure / MDFC cards ("Criatura - Diabo // Feitiço").
        string FrontType { get { int i = TypeText.IndexOf("//"); return i > 0 ? TypeText.Substring(0, i) + " " + MainType : TypeText; } }
        public bool IsLand => Has(FrontType, LandWords);
        public bool IsCreature => Has(FrontType, CreatureWords);
        public bool IsLegendary => Has(FrontType, LegendaryWords);
        public bool IsBasic => Has(FrontType, BasicWords);

        public CardInfo ToInfo() => new CardInfo
        {
            Name = Name, Set = Set, Cost = ManaCost, Type = Type, Colors = Colors, Text = Text, Cmc = Cmc,
            Land = IsLand, Creature = IsCreature, Legendary = IsLegendary, Basic = IsBasic,
        };
    }

    public class CustomSet
    {
        public string Code, Name, Folder;
        public readonly List<CustomCard> Cards = new List<CustomCard>();
        public int ImageCount;
    }

    /// <summary>
    /// Custom sets imported from Magic Set Editor's Cockatrice export (or any Cockatrice card database .xml, v3/v4).
    /// Each import is copied into the app's data folder (customsets/&lt;SET&gt;/cards.xml + images/), so the set stays
    /// available after the original files move. Images are matched to cards by name, whatever folder layout the
    /// export used (SET/Name.png, pics/CUSTOM/SET/Name.full.jpg, ...). Custom cards win over real cards of the same name.
    /// </summary>
    public static class CustomSets
    {
        static readonly List<CustomSet> sets = new List<CustomSet>();
        static readonly Dictionary<string, CustomCard> byName = new Dictionary<string, CustomCard>(StringComparer.OrdinalIgnoreCase);
        static bool loaded;

        public static string Root => Path.Combine(Application.persistentDataPath, "customsets");
        public static IReadOnlyList<CustomSet> All { get { EnsureLoaded(); return sets; } }
        public static event Action Changed;

        public static CustomCard Get(string name)
        {
            EnsureLoaded();
            return name != null && byName.TryGetValue(name, out var c) ? c : null;
        }

        /// <summary>Local image file for a custom card, or null.</summary>
        public static string ImageFor(string name)
        {
            var c = Get(name);
            return c?.ImagePath != null && File.Exists(c.ImagePath) ? c.ImagePath : null;
        }

        // ---------------------------------------------------------------- search (deck builder, tokens)

        public static List<CardInfo> Search(string query, int limit, bool tokensOnly = false)
        {
            EnsureLoaded();
            var results = new List<(int score, CustomCard c)>();
            string q = (query ?? "").Trim().ToLowerInvariant();
            foreach (var set in sets)
                foreach (var c in set.Cards)
                {
                    if (tokensOnly != c.Token) continue;
                    string n = c.Name.ToLowerInvariant();
                    int score = q.Length == 0 ? 5 : n == q ? 0 : n.StartsWith(q) ? 1 : n.Contains(" " + q) ? 2 : n.Contains(q) ? 3
                        : c.Type.ToLowerInvariant().Contains(q) || c.Set.ToLowerInvariant() == q ? 4 : -1;
                    if (score >= 0) results.Add((score, c));
                }
            results.Sort((a, b) => a.score != b.score ? a.score.CompareTo(b.score) : string.Compare(a.c.Name, b.c.Name, StringComparison.OrdinalIgnoreCase));
            var list = new List<CardInfo>();
            for (int i = 0; i < results.Count && list.Count < limit; i++) list.Add(results[i].c.ToInfo());
            return list;
        }

        // ---------------------------------------------------------------- import

        /// <summary>Imports a Cockatrice card database .xml and its images. Returns a short report.</summary>
        public static string Import(string xmlPath)
        {
            if (!File.Exists(xmlPath)) return "File not found: " + xmlPath;
            var parsed = Parse(xmlPath, out string error);
            if (parsed == null) return "Couldn't read " + Path.GetFileName(xmlPath) + ": " + error;
            if (parsed.Cards.Count == 0) return "No cards found in " + Path.GetFileName(xmlPath) + ".";

            // Index every image near the export (same folder and below, a few levels deep).
            string srcDir = Path.GetDirectoryName(xmlPath);
            var images = IndexImages(srcDir, 4);

            string dest = Path.Combine(Root, Safe(parsed.Code));
            string imgDest = Path.Combine(dest, "images");
            Directory.CreateDirectory(imgDest);
            File.Copy(xmlPath, Path.Combine(dest, "cards.xml"), true);
            int copied = 0;
            foreach (var c in parsed.Cards)
            {
                string src = FindImage(images, c);
                if (src == null) continue;
                string target = Path.Combine(imgDest, Key(c.Name) + Path.GetExtension(src).ToLowerInvariant());
                try { File.Copy(src, target, true); copied++; } catch (Exception ex) { Debug.LogWarning("[MTGUnited] Image copy failed: " + ex.Message); }
            }
            Reload();
            int missing = parsed.Cards.Count - copied;
            return "Imported " + parsed.Name + " (" + parsed.Code + "): " + parsed.Cards.Count + " cards, " + copied + " images" +
                   (missing > 0 ? " (" + missing + " without an image: they'll show as text cards)" : "") + ".";
        }

        public static void Remove(CustomSet set)
        {
            try { if (Directory.Exists(set.Folder)) Directory.Delete(set.Folder, true); } catch (Exception ex) { Debug.LogWarning(ex.Message); }
            Reload();
        }

        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            sets.Clear();
            byName.Clear();
            if (!Directory.Exists(Root)) return;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                string xml = Path.Combine(dir, "cards.xml");
                if (!File.Exists(xml)) continue;
                var set = Parse(xml, out _);
                if (set == null) continue;
                set.Folder = dir;
                var images = IndexImages(Path.Combine(dir, "images"), 1);
                foreach (var c in set.Cards)
                {
                    c.ImagePath = FindImage(images, c);
                    if (c.ImagePath != null) set.ImageCount++;
                    byName[c.Name] = c;   // later sets override earlier ones with the same card name
                }
                sets.Add(set);
            }
        }

        // ---------------------------------------------------------------- Cockatrice XML (v3 and v4)

        static CustomSet Parse(string path, out string error)
        {
            error = null;
            var doc = new XmlDocument();
            try { doc.Load(path); }
            catch (Exception ex) { error = ex.Message; return null; }

            var set = new CustomSet();
            var setNode = doc.SelectSingleNode("//sets/set");
            set.Code = Text(setNode, "name") ?? Path.GetFileNameWithoutExtension(path).ToUpperInvariant();
            set.Name = Text(setNode, "longname") ?? set.Code;

            var cards = doc.SelectNodes("//cards/card");
            if (cards == null) return set;
            foreach (XmlNode n in cards)
            {
                var c = new CustomCard { Name = Text(n, "name") };
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                c.Name = c.Name.Trim();
                c.Text = Text(n, "text") ?? "";
                var prop = n.SelectSingleNode("prop");   // v4 keeps the details in <prop>; v3 has them on <card>
                var src = prop ?? n;
                c.Type = (Text(src, "type") ?? "").Trim();
                c.MainType = (Text(src, "maintype") ?? "").Trim();
                if (c.Type.Length == 0) c.Type = c.MainType;
                c.Token = c.Token || c.Type.IndexOf("Ficha", StringComparison.OrdinalIgnoreCase) >= 0 || c.Type.StartsWith("Token", StringComparison.OrdinalIgnoreCase);
                c.ManaCost = CostToBraces(Text(src, "manacost") ?? "");
                int.TryParse(Text(src, "cmc") ?? "", out c.Cmc);
                c.Colors = (Text(src, "colors") ?? JoinAll(n, "color") ?? "").Replace(" ", "").ToUpperInvariant();
                c.PT = Text(src, "pt") ?? "";
                c.Loyalty = Text(src, "loyalty") ?? "";
                c.Token = c.Token || (Text(n, "token") ?? "0").Trim() == "1";
                var setEl = n.SelectSingleNode("set");
                c.Set = setEl != null && !string.IsNullOrWhiteSpace(setEl.InnerText) ? setEl.InnerText.Trim() : set.Code;
                c.PicUrl = setEl?.Attributes?["picurl"]?.Value ?? setEl?.Attributes?["picURL"]?.Value;
                foreach (XmlNode r in n.SelectNodes("related|reverse-related")) if (!string.IsNullOrWhiteSpace(r.InnerText)) c.Related.Add(r.InnerText.Trim());
                if (c.Cmc == 0 && c.ManaCost.Length > 0) c.Cmc = CmcOf(c.ManaCost);
                set.Cards.Add(c);
            }
            return set;
        }

        static string Text(XmlNode parent, string child)
        {
            var n = parent?.SelectSingleNode(child);
            return n == null ? null : n.InnerText;
        }

        static string JoinAll(XmlNode parent, string child)
        {
            var list = parent.SelectNodes(child);
            if (list == null || list.Count == 0) return null;
            var sb = new StringBuilder();
            foreach (XmlNode x in list) sb.Append(x.InnerText.Trim());
            return sb.ToString();
        }

        /// <summary>"2RR" / "{2}{R}{R}" / "2 R R" → "{2}{R}{R}" (hybrid like "W/U" kept as one symbol).</summary>
        public static string CostToBraces(string cost)
        {
            cost = (cost ?? "").Trim();
            // Two faces ("1R // R"): convert each side.
            int split = cost.IndexOf("//");
            if (split >= 0) return CostToBraces(cost.Substring(0, split)) + " // " + CostToBraces(cost.Substring(split + 2));
            if (cost.Length == 0 || cost.Contains("{")) return cost.Replace(" ", "");
            var sb = new StringBuilder();
            int i = 0;
            while (i < cost.Length)
            {
                char ch = cost[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (char.IsDigit(ch))
                {
                    int j = i;
                    while (j < cost.Length && char.IsDigit(cost[j])) j++;
                    sb.Append('{').Append(cost, i, j - i).Append('}');
                    i = j;
                    continue;
                }
                if (i + 2 < cost.Length && cost[i + 1] == '/') { sb.Append('{').Append(cost, i, 3).Append('}'); i += 3; continue; }
                sb.Append('{').Append(char.ToUpperInvariant(ch)).Append('}');
                i++;
            }
            return sb.ToString();
        }

        static int CmcOf(string braces)
        {
            int total = 0, i = 0;
            while ((i = braces.IndexOf('{', i)) >= 0)
            {
                int end = braces.IndexOf('}', i);
                if (end < 0) break;
                string sym = braces.Substring(i + 1, end - i - 1);
                if (int.TryParse(sym, out int n)) total += n;
                else if (sym != "X" && sym != "Y") total += 1;
                i = end + 1;
            }
            return total;
        }

        // ---------------------------------------------------------------- images

        /// <summary>Normalised card name used to match image files ("Sol Ring", "sol_ring.full.jpg" → "solring").</summary>
        public static string Key(string name)
        {
            var sb = new StringBuilder();
            // NFC so "ã" typed one way matches "ã" stored another way in file names.
            foreach (char ch in name.Normalize(NormalizationForm.FormC).ToLowerInvariant()) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            return sb.ToString();
        }

        static Dictionary<string, string> IndexImages(string dir, int depth)
        {
            var map = new Dictionary<string, string>();
            if (!Directory.Exists(dir)) return map;
            void Walk(string d, int left)
            {
                string[] files;
                try { files = Directory.GetFiles(d); } catch { return; }
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                    string stem = Path.GetFileNameWithoutExtension(f);
                    if (stem.EndsWith(".full", StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - 5);
                    string key = Key(stem);
                    if (!map.ContainsKey(key)) map[key] = f;
                    // Names containing "/" become folders on disk ("CB09 Remoção -x/-x.png"): also key by folder + file.
                    string parent = Path.GetFileName(Path.GetDirectoryName(f));
                    string k1 = Key(parent + stem);
                    if (!map.ContainsKey(k1)) map[k1] = f;
                    // "SET - Name" / "001 Name" style names.
                    int dash = stem.IndexOf(" - ");
                    if (dash > 0) { string k2 = Key(stem.Substring(dash + 3)); if (!map.ContainsKey(k2)) map[k2] = f; }
                }
                if (left <= 0) return;
                try { foreach (var sub in Directory.GetDirectories(d)) Walk(sub, left - 1); } catch { }
            }
            Walk(dir, depth);
            return map;
        }

        static string FindImage(Dictionary<string, string> images, CustomCard c)
        {
            if (!string.IsNullOrEmpty(c.PicUrl) && !c.PicUrl.StartsWith("http") && File.Exists(c.PicUrl)) return c.PicUrl;
            return images.TryGetValue(Key(c.Name), out var p) ? p : null;
        }

        static string Safe(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return string.IsNullOrWhiteSpace(s) ? "CUSTOM" : s.Trim();
        }
    }
}

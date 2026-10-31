using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace MTGUnited
{
    /// <summary>
    /// Scryfall-style searching for the deck builder ("t:creature c:rg mv<=3 o:draw").
    ///  • Real cards: the query goes to Scryfall's own search (full syntax, exactly as on scryfall.com).
    ///  • Custom cards: a local reading of the common keywords, so imported sets answer the same queries.
    /// Plain words (no syntax) keep using the fast name search.
    /// </summary>
    public static class ScryfallSearch
    {
        /// <summary>True when the text uses search syntax rather than just a card name.</summary>
        public static bool LooksLikeSyntax(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            q = q.Trim();
            if (q.StartsWith("!")) return true;
            foreach (var token in Tokens(q))
            {
                string t = token.TrimStart('-');
                int op = t.IndexOfAny(new[] { ':', '<', '>', '=' });
                if (op > 0 && Keys.Contains(t.Substring(0, op).ToLowerInvariant())) return true;
            }
            return false;
        }

        static readonly HashSet<string> Keys = new HashSet<string>
        {
            "c", "color", "colors", "id", "identity", "ci", "t", "type", "o", "oracle", "fo", "mv", "cmc", "manavalue", "m", "mana",
            "pow", "power", "tou", "toughness", "loy", "loyalty", "r", "rarity", "s", "set", "e", "edition", "is", "not", "name",
            "f", "format", "legal", "banned", "a", "artist", "ft", "flavor", "kw", "keyword", "usd", "year", "game", "in", "st", "cn",
            "order", "unique", "prefer", "include", "produces", "devotion", "pt", "powtou", "frame", "border", "lang", "new", "has",
        };

        // =================================================================================================== real cards

        /// <summary>Asks Scryfall. Calls back with up to <paramref name="limit"/> cards (empty on no match or no internet).</summary>
        public static IEnumerator SearchReal(string query, int limit, Action<List<CardInfo>, string> done)
        {
            string url = "https://api.scryfall.com/cards/search?order=name&unique=cards&q=" + UnityWebRequest.EscapeURL(query);
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("Accept", "application/json");
                req.timeout = 15;
                yield return req.SendWebRequest();
                var list = new List<CardInfo>();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    // 404 = nothing matched; anything else = offline or a query Scryfall didn't understand.
                    string why = req.responseCode == 404 ? null : ReadError(req.downloadHandler?.text) ?? req.error;
                    done(list, why);
                    yield break;
                }
                Dictionary<string, object> root;
                try { root = Json.ParseObject(req.downloadHandler.text); }
                catch { done(list, "Scryfall sent something unreadable."); yield break; }
                foreach (var o in root.Arr("data"))
                {
                    if (!(o is Dictionary<string, object> c)) continue;
                    var info = FromScryfall(c);
                    if (info != null) { list.Add(info); CardCatalog.Remember(info); }
                    if (list.Count >= limit) break;
                }
                done(list, null);
            }
        }

        static string ReadError(string body)
        {
            try { var d = Json.ParseObject(body); var s = d.Str("details"); return string.IsNullOrEmpty(s) ? null : s; }
            catch { return null; }
        }

        static readonly HashSet<string> FrontFaceLayouts = new HashSet<string> { "transform", "modal_dfc", "flip", "adventure", "meld", "reversible_card", "prepare" };

        /// <summary>A Scryfall card as the builder's CardInfo (the name the way Forge's deck lists expect it).</summary>
        static CardInfo FromScryfall(Dictionary<string, object> c)
        {
            var faces = c.Arr("card_faces").OfType<Dictionary<string, object>>().ToList();
            var front = faces.Count > 0 ? faces[0] : c;
            string name = c.Str("name");
            if (faces.Count > 0 && FrontFaceLayouts.Contains(c.Str("layout"))) name = front.Str("name");
            if (string.IsNullOrEmpty(name)) return null;
            string type = c.Str("type_line", front.Str("type_line"));
            string text = c.ContainsKey("oracle_text") ? c.Str("oracle_text") : string.Join("\n//\n", faces.Select(f => f.Str("oracle_text")));
            var colorsArr = c.ContainsKey("colors") ? c.Arr("colors") : front.Arr("colors");
            string frontType = front.Str("type_line", type);
            return new CardInfo
            {
                Name = name,
                Set = c.Str("set").ToUpperInvariant(),
                Cost = c.ContainsKey("mana_cost") ? c.Str("mana_cost") : front.Str("mana_cost"),
                Type = type,
                Colors = string.Concat(colorsArr.Select(x => x as string)),
                Text = text,
                Cmc = c.TryGetValue("cmc", out var cmc) && cmc is double d ? (int)d : 0,
                Land = frontType.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0,
                Legendary = frontType.IndexOf("Legendary", StringComparison.OrdinalIgnoreCase) >= 0,
                Creature = frontType.IndexOf("Creature", StringComparison.OrdinalIgnoreCase) >= 0,
                Basic = frontType.IndexOf("Basic", StringComparison.OrdinalIgnoreCase) >= 0,
            };
        }

        // =================================================================================================== custom cards

        /// <summary>The same query run over the imported custom sets (the common keywords).</summary>
        public static List<CardInfo> SearchCustom(string query, int limit)
        {
            var terms = Tokens(query).ToList();
            var list = new List<CardInfo>();
            foreach (var set in CustomSets.All)
                foreach (var card in set.Cards)
                {
                    if (card.Token || card.IsBack) continue;
                    if (terms.All(t => Matches(card, t))) list.Add(card.ToInfo());
                    if (list.Count >= limit) return list;
                }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Splits on spaces, keeping "quoted phrases" and o:"quoted text" together.</summary>
        static IEnumerable<string> Tokens(string q)
        {
            var sb = new System.Text.StringBuilder();
            bool quoted = false;
            foreach (char ch in q)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if (char.IsWhiteSpace(ch) && !quoted) { if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); } continue; }
                sb.Append(ch);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        static bool Matches(CustomCard c, string token)
        {
            bool negate = token.StartsWith("-") && token.Length > 1;
            if (negate) token = token.Substring(1);
            if (token.Equals("or", StringComparison.OrdinalIgnoreCase)) return true;   // not supported locally: ignored
            bool r = MatchesPositive(c, token);
            return negate ? !r : r;
        }

        static bool MatchesPositive(CustomCard c, string token)
        {
            if (token.StartsWith("!")) return string.Equals(c.Name, token.Substring(1), StringComparison.OrdinalIgnoreCase);
            int i = token.IndexOfAny(new[] { ':', '<', '>', '=', '!' });
            if (i <= 0) return Contains(c.Name, token);
            string key = token.Substring(0, i).ToLowerInvariant();
            string op = token.Substring(i, token.Length > i + 1 && token[i + 1] == '=' ? 2 : 1);
            string val = token.Substring(i + op.Length);
            if (op == "!") return true;
            switch (key)
            {
                case "t": case "type": return Contains(c.Type + " " + c.MainType, val);
                case "o": case "oracle": case "fo": return Contains(c.Text.Replace(c.Name, "~"), val) || Contains(c.Text, val);
                case "name": return Contains(c.Name, val);
                case "mv": case "cmc": case "manavalue": return Compare(c.Cmc, op, val);
                case "pow": case "power": return int.TryParse(Part(c.PT, 0), out int pw) && Compare(pw, op, val);
                case "tou": case "toughness": return int.TryParse(Part(c.PT, 1), out int tg) && Compare(tg, op, val);
                case "loy": case "loyalty": return int.TryParse(c.Loyalty, out int ly) && Compare(ly, op, val);
                case "c": case "color": case "colors": case "id": case "identity": case "ci":
                    return ColorMatch(c.Colors, op, val, key.StartsWith("i") || key == "ci");
                case "s": case "set": case "e": case "edition":
                    return string.Equals(c.SetCode, val, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Set, val, StringComparison.OrdinalIgnoreCase);
                case "m": case "mana": return Contains(c.ManaCost.Replace("{", "").Replace("}", ""), val.Replace("{", "").Replace("}", ""));
                case "is":
                    switch (val.ToLowerInvariant())
                    {
                        case "commander": return c.ToInfo().CanBeCommander;
                        case "permanent": return !Contains(c.Type, "Instant") && !Contains(c.Type, "Sorcery") && !Contains(c.Type, "Instantânea") && !Contains(c.Type, "Feitiço");
                        case "spell": return !c.ToInfo().Land;
                        case "multicolor": case "multicolored": return c.Colors.Length > 1;
                        case "colorless": return c.Colors.Length == 0;
                        default: return true;
                    }
                default: return true;   // keywords custom sets don't have (rarity, format, price...) don't filter them out
            }
        }

        static bool Contains(string hay, string needle) => (hay ?? "").IndexOf(needle ?? "", StringComparison.OrdinalIgnoreCase) >= 0;

        static string Part(string pt, int i)
        {
            var parts = (pt ?? "").Split('/');
            return parts.Length == 2 ? parts[i].Trim() : "";
        }

        static bool Compare(int v, string op, string val)
        {
            if (!int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                if (val.Equals("even", StringComparison.OrdinalIgnoreCase)) return v % 2 == 0;
                if (val.Equals("odd", StringComparison.OrdinalIgnoreCase)) return v % 2 != 0;
                return true;
            }
            switch (op)
            {
                case ":": case "=": return v == n;
                case "<": return v < n;
                case ">": return v > n;
                case "<=": return v <= n;
                case ">=": return v >= n;
                case "!=": return v != n;
            }
            return true;
        }

        static readonly Dictionary<string, string> ColorWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "white", "W" }, { "blue", "U" }, { "black", "B" }, { "red", "R" }, { "green", "G" }, { "colorless", "" }, { "c", "" },
            { "azorius", "WU" }, { "dimir", "UB" }, { "rakdos", "BR" }, { "gruul", "RG" }, { "selesnya", "GW" },
            { "orzhov", "WB" }, { "izzet", "UR" }, { "golgari", "BG" }, { "boros", "RW" }, { "simic", "GU" },
            { "bant", "GWU" }, { "esper", "WUB" }, { "grixis", "UBR" }, { "jund", "BRG" }, { "naya", "RGW" },
            { "abzan", "WBG" }, { "jeskai", "URW" }, { "sultai", "BGU" }, { "mardu", "RWB" }, { "temur", "GUR" },
        };

        /// <summary>c: means "at least these colours", c= exactly, c&lt;= at most; id: (identity) means "within these".</summary>
        static bool ColorMatch(string cardColors, string op, string val, bool identity)
        {
            if (val.Equals("m", StringComparison.OrdinalIgnoreCase) || val.Equals("multicolor", StringComparison.OrdinalIgnoreCase)) return cardColors.Length > 1;
            string want = ColorWords.TryGetValue(val, out var w) ? w : val.ToUpperInvariant();
            var card = new HashSet<char>(cardColors.ToUpperInvariant().Where(ch => "WUBRG".IndexOf(ch) >= 0));
            var set = new HashSet<char>(want.Where(ch => "WUBRG".IndexOf(ch) >= 0));
            if (identity && op == ":") op = "<=";
            switch (op)
            {
                case ":": case ">=": return set.Count == 0 ? card.Count == 0 : set.IsSubsetOf(card);
                case "=": return card.SetEquals(set);
                case "<=": return card.IsSubsetOf(set);
                case "<": return card.IsProperSubsetOf(set);
                case ">": return card.IsProperSupersetOf(set);
                case "!=": return !card.SetEquals(set);
            }
            return true;
        }
    }
}

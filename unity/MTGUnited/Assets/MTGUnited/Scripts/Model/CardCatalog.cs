using System;
using System.Collections.Generic;
using System.Text;

namespace MTGUnited
{
    /// <summary>What the deck builder knows about a card (from the bridge's card catalog).</summary>
    public class CardInfo
    {
        public string Name, Set, Cost, Type, Colors, Text;
        public int Cmc;
        public bool Land, Legendary, Creature, Basic, Unknown;

        public bool CanBeCommander => Legendary && (Creature || (Text ?? "").IndexOf("can be your commander", StringComparison.OrdinalIgnoreCase) >= 0);

        public static CardInfo From(Dictionary<string, object> d) => new CardInfo
        {
            Name = d.Str("name"), Set = d.Str("set"), Cost = d.Str("cost"), Type = d.Str("type"), Colors = d.Str("colors"),
            Text = d.Str("text"), Cmc = d.Int("cmc"), Land = d.Bool("land"), Legendary = d.Bool("legendary"),
            Creature = d.Bool("creature"), Basic = d.Bool("basic"), Unknown = d.Bool("unknown"),
        };

        /// <summary>For the card preview / inspector, which take game card data.</summary>
        public CardData ToCardData() => new CardData
        {
            Id = -1, Name = Name, Set = Set ?? "", ManaCost = Cost ?? "", TypeLine = Type ?? "", Text = Text ?? "",
            Colors = Colors ?? "", IsLand = Land, IsCreature = Creature,
        };
    }

    /// <summary>
    /// Client side of the card catalog: asks the bridge for search results (top 60 only) and for details of the
    /// cards in a deck, and remembers every answer so nothing is asked twice.
    /// </summary>
    public static class CardCatalog
    {
        static readonly Dictionary<string, CardInfo> known = new Dictionary<string, CardInfo>(StringComparer.OrdinalIgnoreCase);
        static Action<string, List<CardInfo>> searchCallback;
        static readonly List<Action> infoWaiters = new List<Action>();
        static string lastQuery;
        static List<CardInfo> pendingCustom = new List<CardInfo>();
        public static GameController Gc;

        /// <summary>Custom (imported) cards first, then what the bridge told us.</summary>
        public static CardInfo Get(string name)
        {
            if (name == null) return null;
            var custom = CustomSets.Get(name);
            if (custom != null) return custom.ToInfo();
            return known.TryGetValue(name, out var c) ? c : null;
        }

        public static void Search(string query, Action<string, List<CardInfo>> onResults)
        {
            searchCallback = onResults;
            lastQuery = query;
            // Custom cards are searched locally and shown first; real cards come from Forge's catalog when it runs.
            pendingCustom = CustomSets.Search(query, 60);
            if (Gc != null && Gc.Bridge.Connected) Gc.Send("cardSearch", "q", query, "limit", 60);
            else onResults?.Invoke(query, new List<CardInfo>(pendingCustom));
        }

        /// <summary>Fetches details for any names not known yet, then calls back (immediately if all are known).</summary>
        public static void Ensure(IEnumerable<string> names, Action done)
        {
            var missing = new List<object>();
            foreach (var n in names) if (!string.IsNullOrEmpty(n) && !known.ContainsKey(n) && CustomSets.Get(n) == null && !missing.Contains(n)) missing.Add(n);
            if (missing.Count == 0 || Gc == null || !Gc.Bridge.Connected) { done?.Invoke(); return; }
            if (done != null) infoWaiters.Add(done);
            Gc.Send("cardInfo", "names", missing);
        }

        public static void OnMessage(Dictionary<string, object> msg)
        {
            string t = msg.Str("t");
            var list = new List<CardInfo>();
            foreach (var o in msg.Arr("cards"))
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var c = CardInfo.From(d);
                if (!c.Unknown) known[c.Name] = c;
                string asked = d.Str("asked");
                if (!string.IsNullOrEmpty(asked)) known[asked] = c;   // "Front face" typed for "Front // Back"
                else if (c.Unknown) known[c.Name] = c;
                list.Add(c);
            }
            if (t == "cardResults")
            {
                // Ignore answers to queries the user has already typed past.
                if (msg.Str("q") == lastQuery)
                {
                    var merged = new List<CardInfo>(pendingCustom);
                    foreach (var c in list) if (merged.Count < 60 && !merged.Exists(m => string.Equals(m.Name, c.Name, StringComparison.OrdinalIgnoreCase))) merged.Add(c);
                    searchCallback?.Invoke(lastQuery, merged);
                }
            }
            else if (t == "cardInfo")
            {
                var waiters = new List<Action>(infoWaiters);
                infoWaiters.Clear();
                foreach (var w in waiters) w();
            }
        }
    }

    /// <summary>A deck as the builder edits it: commander(s) plus a main deck of (count, name) entries.</summary>
    public class DeckModel
    {
        public readonly List<string> Commanders = new List<string>();
        public readonly List<(int count, string name)> Cards = new List<(int, string)>();

        public int Total
        {
            get { int n = Commanders.Count; foreach (var c in Cards) n += c.count; return n; }
        }

        public int CountOf(string name)
        {
            foreach (var c in Cards) if (string.Equals(c.name, name, StringComparison.OrdinalIgnoreCase)) return c.count;
            return 0;
        }

        public bool Contains(string name) => CountOf(name) > 0 || Commanders.Exists(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        public void Add(string name, int n = 1)
        {
            for (int i = 0; i < Cards.Count; i++)
                if (string.Equals(Cards[i].name, name, StringComparison.OrdinalIgnoreCase)) { Cards[i] = (Cards[i].count + n, Cards[i].name); return; }
            Cards.Add((n, name));
        }

        public void RemoveOne(string name)
        {
            for (int i = 0; i < Cards.Count; i++)
                if (string.Equals(Cards[i].name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (Cards[i].count > 1) Cards[i] = (Cards[i].count - 1, Cards[i].name);
                    else Cards.RemoveAt(i);
                    return;
                }
            Commanders.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Moves a card between the main deck and the command zone.</summary>
        public void ToggleCommander(string name)
        {
            if (Commanders.Exists(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase)))
            {
                Commanders.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
                Add(name);
                return;
            }
            if (Commanders.Count >= 2) { Add(Commanders[0]); Commanders.RemoveAt(0); }   // keep at most two (partners)
            for (int i = 0; i < Cards.Count; i++)
                if (string.Equals(Cards[i].name, name, StringComparison.OrdinalIgnoreCase)) { RemoveAt(i); break; }
            Commanders.Add(name);
        }

        void RemoveAt(int i)
        {
            if (Cards[i].count > 1) Cards[i] = (Cards[i].count - 1, Cards[i].name);
            else Cards.RemoveAt(i);
        }

        public IEnumerable<string> AllNames()
        {
            foreach (var c in Commanders) yield return c;
            foreach (var c in Cards) yield return c.name;
        }

        /// <summary>Reads a saved deck (Arena / Moxfield / MTGO style text plus the optional commander field).</summary>
        public static DeckModel From(SavedDeck d)
        {
            var m = new DeckModel();
            if (d == null) return m;
            string section = "deck";
            foreach (var raw in (d.List ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                string lower = line.ToLowerInvariant().TrimEnd(':');
                if (lower == "commander" || lower == "commanders") { section = "commander"; continue; }
                if (lower == "deck" || lower == "main" || lower == "mainboard") { section = "deck"; continue; }
                if (lower == "sideboard" || lower == "maybeboard" || lower == "companion") { section = "skip"; continue; }
                if (line.StartsWith("//") || line.StartsWith("#")) continue;
                if (section == "skip") continue;
                ParseLine(line, out int count, out string name);
                if (string.IsNullOrEmpty(name)) continue;
                if (section == "commander") m.Commanders.Add(name);
                else m.Add(name, count);
            }
            foreach (var c in (d.Commander ?? "").Split(';'))
            {
                string name = c.Trim();
                if (name.Length == 0 || m.Commanders.Exists(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
                for (int i = 0; i < m.Cards.Count; i++)
                    if (string.Equals(m.Cards[i].name, name, StringComparison.OrdinalIgnoreCase)) { m.RemoveAt(i); break; }
                m.Commanders.Add(name);
            }
            return m;
        }

        static void ParseLine(string line, out int count, out string name)
        {
            count = 1;
            int sp = line.IndexOf(' ');
            if (sp > 0 && int.TryParse(line.Substring(0, sp).TrimEnd('x', 'X'), out int c)) { count = c; line = line.Substring(sp + 1).Trim(); }
            // Drop set / collector number / foil markers: "Sol Ring (C21) 263 *F*".
            int paren = line.IndexOf(" (");
            if (paren > 0) line = line.Substring(0, paren);
            int star = line.IndexOf(" *");
            if (star > 0) line = line.Substring(0, star);
            name = line.Trim();
        }

        /// <summary>Writes the deck back as text in sections (the format the bridge's deck loader reads).</summary>
        public void WriteTo(SavedDeck d)
        {
            var sb = new StringBuilder();
            if (Commanders.Count > 0)
            {
                sb.Append("Commander\n");
                foreach (var c in Commanders) sb.Append("1 ").Append(c).Append('\n');
                sb.Append('\n');
            }
            sb.Append("Deck\n");
            foreach (var c in Cards) sb.Append(c.count).Append(' ').Append(c.name).Append('\n');
            d.List = sb.ToString();
            d.Commander = "";   // the Commander section is now the source of truth
        }
    }
}

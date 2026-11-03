using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MTGUnited
{
    /// <summary>A card that can be opened in a pack: what it is and how rare.</summary>
    public class LimitedCard
    {
        public string Name, Set = "", Cost = "", Type = "", Colors = "", Rarity = "";
        public int Cmc;
        public bool Land, Creature;

        public static LimitedCard From(CardInfo c, string rarity) => new LimitedCard
        {
            Name = c.Name, Set = c.Set ?? "", Cost = c.Cost ?? "", Type = c.Type ?? "", Colors = c.Colors ?? "", Cmc = c.Cmc,
            Land = c.Land, Creature = c.Creature, Rarity = NormRarity(rarity),
        };

        public CardInfo ToInfo() => new CardInfo { Name = Name, Set = Set, Cost = Cost, Type = Type, Colors = Colors, Cmc = Cmc, Land = Land, Creature = Creature };

        public static string NormRarity(string r)
        {
            r = (r ?? "").Trim().ToLowerInvariant();
            if (r.StartsWith("m")) return "mythic";
            if (r.StartsWith("r")) return "rare";
            if (r.StartsWith("u")) return "uncommon";
            if (r.StartsWith("c") && r != "cube") return "common";
            return r.StartsWith("s") ? "rare" : "";   // special / bonus sheets count as rare
        }

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            { "n", Name }, { "s", Set }, { "c", Cost }, { "t", Type }, { "col", Colors }, { "r", Rarity }, { "cmc", Cmc }, { "l", Land }, { "cr", Creature },
        };

        public static LimitedCard FromJson(Dictionary<string, object> d) => new LimitedCard
        {
            Name = d.Str("n"), Set = d.Str("s"), Cost = d.Str("c"), Type = d.Str("t"), Colors = d.Str("col"), Rarity = d.Str("r"),
            Cmc = d.Int("cmc"), Land = d.Bool("l"), Creature = d.Bool("cr"),
        };
    }

    /// <summary>
    /// Where packs come from: a real set (cards from Scryfall), an imported custom set, or a cube (a list of cards).
    /// Excluded cards never show up in packs.
    /// </summary>
    public class LimitedSource
    {
        public enum Kind { Set, Custom, Cube }
        public Kind Type;
        public string Code = "", Name = "";
        public readonly List<LimitedCard> Cards = new List<LimitedCard>();
        public readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<LimitedCard> Usable => Cards.Where(c => !Excluded.Contains(c.Name));
        public string Label => Type == Kind.Cube ? "Cube · " + Name : Type == Kind.Custom ? Code + " · " + Name : Code.ToUpperInvariant() + (Name.Length > 0 && Name != Code ? " · " + Name : "");

        /// <summary>An imported custom set (rarities from its export when it has them).</summary>
        public static LimitedSource FromCustom(CustomSet set)
        {
            var s = new LimitedSource { Type = Kind.Custom, Code = set.Code, Name = set.Name };
            foreach (var c in set.Cards)
            {
                if (c.Token || c.IsBack || c.IsBasic) continue;
                var lc = LimitedCard.From(c.ToInfo(), c.Rarity);
                lc.Set = set.Code;
                s.Cards.Add(lc);
            }
            return s;
        }

        /// <summary>A cube: one card per line ("1 Name", "Name", MTGO / Moxfield lists...). Each card is in the cube once.</summary>
        public static LimitedSource FromList(string name, string text)
        {
            var s = new LimitedSource { Type = Kind.Cube, Code = "CUBE", Name = name };
            var model = DeckTextImport.Parse(text ?? "", DeckTextFormat.Plain);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (count, card) in model.Cards)
                for (int i = 0; i < count; i++)
                {
                    if (!seen.Add(card + "#" + i)) continue;
                    var info = CardCatalog.Get(card) ?? new CardInfo { Name = card };
                    if (info.Basic) continue;
                    s.Cards.Add(LimitedCard.From(info, ""));
                }
            foreach (var c in model.Commanders) if (seen.Add(c + "#0")) s.Cards.Add(LimitedCard.From(CardCatalog.Get(c) ?? new CardInfo { Name = c }, ""));
            return s;
        }

        public bool HasRarities => Cards.Count(c => c.Rarity.Length > 0) >= Cards.Count * 0.8f && Cards.Any(c => c.Rarity == "common");

        public Dictionary<string, object> ToJson() => new Dictionary<string, object>
        {
            { "kind", Type.ToString() }, { "code", Code }, { "name", Name },
            { "cards", Cards.Select(c => (object)c.ToJson()).ToList() }, { "excluded", Excluded.Select(x => (object)x).ToList() },
        };
    }

    /// <summary>Makes packs: a real booster shape when the source has rarities, otherwise 15 random cards.</summary>
    public static class PackMaker
    {
        /// <summary>
        /// One pack. With rarities: 1 rare (a mythic 1 time in 8, if there are any), 3 uncommons, 10 commons. A cube deals
        /// each card once per event (from <paramref name="cubeDeck"/>); a set can repeat cards between packs.
        /// </summary>
        public static List<LimitedCard> Make(LimitedSource src, Random rng, List<LimitedCard> cubeDeck, int size = 15)
        {
            var usable = src.Usable.ToList();
            var pack = new List<LimitedCard>();
            if (usable.Count == 0) return pack;
            if (src.Type == LimitedSource.Kind.Cube || !src.HasRarities)
            {
                if (src.Type == LimitedSource.Kind.Cube && cubeDeck != null)
                {
                    for (int i = 0; i < size && cubeDeck.Count > 0; i++) { pack.Add(cubeDeck[cubeDeck.Count - 1]); cubeDeck.RemoveAt(cubeDeck.Count - 1); }
                    return pack;
                }
                return Distinct(usable, size, rng, pack);
            }
            var mythic = usable.Where(c => c.Rarity == "mythic").ToList();
            var rare = usable.Where(c => c.Rarity == "rare").ToList();
            var unc = usable.Where(c => c.Rarity == "uncommon").ToList();
            var com = usable.Where(c => c.Rarity == "common").ToList();
            var rareSlot = mythic.Count > 0 && (rare.Count == 0 || rng.Next(8) == 0) ? mythic : rare.Count > 0 ? rare : unc;
            Distinct(rareSlot, 1, rng, pack);
            Distinct(unc.Count > 0 ? unc : usable, 3, rng, pack);
            Distinct(com.Count > 0 ? com : usable, 10, rng, pack);
            if (pack.Count < 14) Distinct(usable, 14 - pack.Count, rng, pack);
            return pack;
        }

        static List<LimitedCard> Distinct(List<LimitedCard> from, int n, Random rng, List<LimitedCard> into)
        {
            var bag = from.Where(c => !into.Any(x => x.Name == c.Name)).ToList();
            for (int i = 0; i < n && bag.Count > 0; i++)
            {
                int k = rng.Next(bag.Count);
                into.Add(bag[k]);
                bag.RemoveAt(k);
            }
            return into;
        }

        public static List<LimitedCard> Shuffled(LimitedSource src, Random rng)
        {
            var list = src.Usable.ToList();
            for (int i = list.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
            return list;
        }
    }

    /// <summary>
    /// A draft or sealed event, run by one machine (solo, or the LAN host). Seats are people or bots.
    /// Draft: three rounds; each seat opens a pack, takes one card and passes the rest (left, right, left); a seat with
    /// several packs waiting works through them in order. Sealed: everyone opens six packs at once.
    /// </summary>
    public class LimitedEvent
    {
        public class Seat
        {
            public string Name;
            public bool Bot;
            public readonly List<List<LimitedCard>> Waiting = new List<List<LimitedCard>>();
            public readonly List<LimitedCard> Picks = new List<LimitedCard>();
        }

        public bool Draft = true;
        public int Rounds = 3, SealedPacks = 6;
        public readonly List<LimitedSource> Sources = new List<LimitedSource>();   // one per round (the last repeats)
        public readonly List<Seat> Seats = new List<Seat>();
        public int Round;           // 0-based draft round
        public bool Done;
        public event Action Changed;
        readonly Random rng = new Random();
        List<LimitedCard> cubeDeck;

        public LimitedSource SourceFor(int round) => Sources[Math.Min(round, Sources.Count - 1)];

        public void Start()
        {
            if (Sources.Count == 0 || Seats.Count == 0) throw new InvalidOperationException("No packs or no players.");
            if (Sources.Any(s => s.Type == LimitedSource.Kind.Cube)) cubeDeck = PackMaker.Shuffled(Sources.First(s => s.Type == LimitedSource.Kind.Cube), rng);
            if (!Draft)
            {
                foreach (var seat in Seats)
                    for (int i = 0; i < SealedPacks; i++) seat.Picks.AddRange(PackMaker.Make(SourceFor(i), rng, cubeDeck));
                Done = true;
                Changed?.Invoke();
                return;
            }
            Round = 0;
            OpenRound();
        }

        void OpenRound()
        {
            var src = SourceFor(Round);
            foreach (var seat in Seats)
            {
                seat.Waiting.Add(PackMaker.Make(src, rng, cubeDeck));
            }
            BotsPick();
            Changed?.Invoke();
        }

        /// <summary>A person picks a card from the pack in front of them.</summary>
        public bool Pick(int seatIndex, string cardName)
        {
            if (Done || seatIndex < 0 || seatIndex >= Seats.Count) return false;
            var seat = Seats[seatIndex];
            if (seat.Waiting.Count == 0) return false;
            var pack = seat.Waiting[0];
            int k = pack.FindIndex(c => c.Name == cardName);
            if (k < 0) return false;
            Take(seatIndex, k);
            BotsPick();
            Advance();
            Changed?.Invoke();
            return true;
        }

        void Take(int seatIndex, int cardIndex)
        {
            var seat = Seats[seatIndex];
            var pack = seat.Waiting[0];
            seat.Picks.Add(pack[cardIndex]);
            pack.RemoveAt(cardIndex);
            seat.Waiting.RemoveAt(0);
            if (pack.Count == 0) return;
            int dir = Round % 2 == 0 ? 1 : -1;   // left, right, left
            int next = ((seatIndex + dir) % Seats.Count + Seats.Count) % Seats.Count;
            Seats[next].Waiting.Add(pack);
        }

        /// <summary>Bots take a card from every pack they have (repeats until no bot has a pack waiting).</summary>
        void BotsPick()
        {
            bool any = true;
            while (any)
            {
                any = false;
                for (int i = 0; i < Seats.Count; i++)
                {
                    var s = Seats[i];
                    if (!s.Bot || s.Waiting.Count == 0) continue;
                    Take(i, BotChoice(s, s.Waiting[0]));
                    any = true;
                }
            }
        }

        void Advance()
        {
            if (Seats.Any(s => s.Waiting.Count > 0)) return;
            Round++;
            if (Round >= Rounds) { Done = true; return; }
            OpenRound();
        }

        /// <summary>A simple drafter: rarity first, then its two main colours once it has some picks, a bit of randomness.</summary>
        int BotChoice(Seat s, List<LimitedCard> pack)
        {
            var colorCount = new Dictionary<char, int>();
            foreach (var p in s.Picks) foreach (char ch in p.Colors) if ("WUBRG".IndexOf(ch) >= 0) colorCount[ch] = colorCount.TryGetValue(ch, out int n) ? n + 1 : 1;
            var top = colorCount.OrderByDescending(kv => kv.Value).Take(2).Select(kv => kv.Key).ToList();
            int best = 0; double bestScore = double.MinValue;
            for (int i = 0; i < pack.Count; i++)
            {
                var c = pack[i];
                double score = c.Rarity == "mythic" ? 4.2 : c.Rarity == "rare" ? 3.6 : c.Rarity == "uncommon" ? 2.2 : 1.0;
                if (c.Creature) score += 0.4;
                if (c.Land) score -= 0.8;
                var cols = c.Colors.Where(ch => "WUBRG".IndexOf(ch) >= 0).ToList();
                if (s.Picks.Count >= 5 && top.Count > 0)
                {
                    if (cols.Count == 0) score += 0.6;                       // colourless fits anything
                    else if (cols.All(top.Contains)) score += 1.6;
                    else score -= 0.9 * cols.Count(ch => !top.Contains(ch));
                }
                score += rng.NextDouble() * 0.9;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        // ---------------------------------------------------------------- what one seat sees (sent over LAN)

        public Dictionary<string, object> ViewFor(int seatIndex)
        {
            var me = Seats[seatIndex];
            var pack = me.Waiting.Count > 0 ? me.Waiting[0] : null;
            return new Dictionary<string, object>
            {
                { "draft", Draft }, { "done", Done }, { "round", Round }, { "rounds", Rounds },
                { "source", SourceFor(Math.Min(Round, Rounds - 1)).Label },
                { "pick", me.Picks.Count + 1 },
                { "pass", Round % 2 == 0 ? "left" : "right" },
                { "pack", pack == null ? new List<object>() : pack.Select(c => (object)c.ToJson()).ToList() },
                { "picks", me.Picks.Select(c => (object)c.ToJson()).ToList() },
                { "seats", Seats.Select(s => (object)new Dictionary<string, object> { { "name", s.Name }, { "bot", s.Bot }, { "waiting", s.Waiting.Count }, { "picks", s.Picks.Count } }).ToList() },
            };
        }
    }

    /// <summary>What a seat's screen shows (from <see cref="LimitedEvent.ViewFor"/>, local or received over LAN).</summary>
    public class LimitedView
    {
        public bool Draft, Done;
        public int Round, Rounds, Pick;
        public string Source = "", Pass = "left";
        public List<LimitedCard> Pack = new List<LimitedCard>(), Picks = new List<LimitedCard>();
        public List<(string name, bool bot, int waiting, int picks)> Seats = new List<(string, bool, int, int)>();

        public static LimitedView From(Dictionary<string, object> d)
        {
            var v = new LimitedView
            {
                Draft = d.Bool("draft"), Done = d.Bool("done"), Round = d.Int("round"), Rounds = d.Int("rounds", 3), Pick = d.Int("pick", 1),
                Source = d.Str("source"), Pass = d.Str("pass", "left"),
            };
            foreach (var o in d.Arr("pack")) if (o is Dictionary<string, object> c) v.Pack.Add(LimitedCard.FromJson(c));
            foreach (var o in d.Arr("picks")) if (o is Dictionary<string, object> c) v.Picks.Add(LimitedCard.FromJson(c));
            foreach (var o in d.Arr("seats")) if (o is Dictionary<string, object> s) v.Seats.Add((s.Str("name"), s.Bool("bot"), s.Int("waiting"), s.Int("picks")));
            return v;
        }

        /// <summary>The pool as a deck list ("3 Name" lines), for the deck builder.</summary>
        public static string PoolText(List<LimitedCard> picks)
        {
            var sb = new StringBuilder();
            foreach (var g in picks.GroupBy(c => c.Name).OrderBy(g => g.Key)) sb.Append(g.Count()).Append(' ').Append(g.Key).Append('\n');
            return sb.ToString();
        }
    }
}

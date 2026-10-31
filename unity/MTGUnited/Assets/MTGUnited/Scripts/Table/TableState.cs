using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>A card on the kitchen table. No rules: just where it is and how it's turned.</summary>
    public class TableCard
    {
        public int Id;
        public string Name;
        public int Owner, Controller;
        public string Zone;            // Library, Hand, Battlefield, Graveyard, Exile, Command
        public Vector2 Pos;            // battlefield: 0..1 across the owner's field (x left→right, y near→far)
        public bool Tapped, FaceDown, Token, Revealed;
        public int Rot;
        public bool Flipped;           // double-faced card showing its back
        public string Note = "";       // a sticky note everyone at the table sees (battlefield)
        public bool Sick;              // marked summoning sick (Z); cleared at its controller's next turn                // extra quarter turns clockwise (Q / E at the table), on top of tapping
        public string TokenPT = "", TokenColors = "";   // for tokens made up at the table
        public Dictionary<string, int> Counters = new Dictionary<string, int>();

        public TableCard Clone()
        {
            var c = (TableCard)MemberwiseClone();
            c.Counters = new Dictionary<string, int>(Counters);
            return c;
        }
    }

    public class TablePlayer
    {
        public int Id;
        public string Name;
        public string DeckName;
        public readonly List<string> CommanderNames = new List<string>();
        public int Life = 40;
        public int Poison;
        public readonly Dictionary<int, int> CommanderDamage = new Dictionary<int, int>();
        public List<int> Library = new List<int>(), Hand = new List<int>(), Graveyard = new List<int>(), Exile = new List<int>(), Command = new List<int>();

        public List<int> ZoneList(string zone)
        {
            switch (zone)
            {
                case "Library": return Library;
                case "Hand": return Hand;
                case "Graveyard": return Graveyard;
                case "Exile": return Exile;
                case "Command": return Command;
                default: return null;
            }
        }

        public TablePlayer Clone()
        {
            var p = (TablePlayer)MemberwiseClone();
            p.Library = new List<int>(Library); p.Hand = new List<int>(Hand); p.Graveyard = new List<int>(Graveyard);
            p.Exile = new List<int>(Exile); p.Command = new List<int>(Command);
            return p;
        }
    }

    /// <summary>
    /// The kitchen-table game: players, zones and cards, changed only through the action methods below (so every
    /// action can be logged and undone, and later sent over a network as-is). The table enforces nothing.
    /// </summary>
    public partial class TableState
    {
        public List<TablePlayer> Players = new List<TablePlayer>();
        public Dictionary<int, TableCard> Cards = new Dictionary<int, TableCard>();
        public int Turn = 1;
        public int Active;               // index into Players
        public readonly List<string> Log = new List<string>();
        int nextId = 1;
        readonly System.Random rng = new System.Random();
        readonly Stack<(List<TablePlayer>, Dictionary<int, TableCard>, int, int, int)> undo = new Stack<(List<TablePlayer>, Dictionary<int, TableCard>, int, int, int)>();

        public TablePlayer Player(int id) => Players.Find(p => p.Id == id);
        public TablePlayer ActivePlayer => Players.Count > 0 ? Players[Active] : null;
        public IEnumerable<TableCard> Battlefield(int playerId) => Cards.Values.Where(c => c.Zone == "Battlefield" && c.Controller == playerId);

        public void Say(string line)
        {
            Log.Add(line);
            if (Log.Count > 200) Log.RemoveAt(0);
        }

        // ---------------------------------------------------------------- undo

        /// <summary>Call before any change; Undo() restores the table to this point.</summary>
        public void Checkpoint()
        {
            var players = Players.Select(p => p.Clone()).ToList();
            var cards = Cards.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
            undo.Push((players, cards, Turn, Active, nextId));
            if (undo.Count > 60)
            {
                var keep = undo.ToArray().Take(60).Reverse().ToList();
                undo.Clear();
                foreach (var k in keep) undo.Push(k);
            }
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            var (players, cards, turn, active, next) = undo.Pop();
            Players = players; Cards = cards; Turn = turn; Active = active; nextId = next;
            Say("Undo.");
            return true;
        }

        // ---------------------------------------------------------------- setup

        public TablePlayer AddPlayer(string name, SavedDeck deck)
        {
            var p = new TablePlayer { Id = Players.Count + 1, Name = name, DeckName = deck?.Name, Life = DeckFormat.StartingLife(deck?.Format) };
            Players.Add(p);
            var model = DeckModel.From(deck);
            foreach (var c in model.Commanders) { p.Command.Add(NewCard(c, p.Id, "Command").Id); p.CommanderNames.Add(c); }
            foreach (var (count, cname) in model.Cards)
                for (int i = 0; i < count; i++) p.Library.Add(NewCard(cname, p.Id, "Library").Id);
            Shuffle(p, false);
            return p;
        }

        TableCard NewCard(string name, int owner, string zone)
        {
            var c = new TableCard { Id = nextId++, Name = name, Owner = owner, Controller = owner, Zone = zone };
            Cards[c.Id] = c;
            return c;
        }

        // ---------------------------------------------------------------- actions

        public void Shuffle(TablePlayer p, bool log = true)
        {
            for (int i = p.Library.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (p.Library[i], p.Library[j]) = (p.Library[j], p.Library[i]);
            }
            foreach (var id in p.Library) { Cards[id].Revealed = false; Cards[id].FaceDown = false; }
            if (log) Say(p.Name + " shuffled their library.");
        }

        public int Draw(TablePlayer p, int n)
        {
            int drawn = 0;
            for (int i = 0; i < n && p.Library.Count > 0; i++)
            {
                Move(p.Library[0], "Hand", p.Id, -1, null, false);
                drawn++;
            }
            if (drawn > 0) Say(p.Name + " drew " + (drawn == 1 ? "a card" : drawn + " cards") + ".");
            else Say(p.Name + " tried to draw from an empty library.");
            return drawn;
        }

        public void Mill(TablePlayer p, int n)
        {
            int k = 0;
            for (int i = 0; i < n && p.Library.Count > 0; i++) { Move(p.Library[0], "Graveyard", p.Id, -1, null, false); k++; }
            Say(p.Name + " milled " + k + ".");
        }

        /// <summary>Moves a card to a zone of a player. index: library position (0 = top, -1 = bottom); pos: battlefield spot.</summary>
        public void Move(int cardId, string zone, int toPlayer, int index = 0, Vector2? pos = null, bool log = true)
        {
            if (!Cards.TryGetValue(cardId, out var c)) return;
            string from = c.Zone;
            if (from != "Battlefield") foreach (var pl in Players) pl.ZoneList(from)?.Remove(cardId);

            // Tokens cease to exist anywhere but the battlefield.
            if (c.Token && zone != "Battlefield")
            {
                Cards.Remove(cardId);
                if (log) Say(c.Name + " token left the battlefield.");
                return;
            }

            c.Zone = zone;
            if (zone == "Battlefield")
            {
                c.Controller = toPlayer;
                if (pos.HasValue) c.Pos = pos.Value;
                else c.Pos = FreeSpot(toPlayer, Is(c, "Land") ? 0.15f : 0.55f);
                if (from != "Battlefield") { c.Tapped = false; }
            }
            else
            {
                // Leaving the battlefield resets the card (owner's zones, untapped, no counters).
                c.Controller = c.Owner;
                c.Tapped = false;
                c.Rot = 0;
                c.Flipped = false;   // double-faced cards return to their front face
                c.Note = "";         // notes belong to the permanent, not the card
                c.Sick = false;
                c.Counters.Clear();
                if (zone != "Library") c.FaceDown = false;
                var list = Player(c.Owner).ZoneList(zone);
                if (zone == "Library")
                {
                    if (index < 0 || index > list.Count) list.Add(cardId); else list.Insert(index, cardId);
                    c.Revealed = false;
                }
                else list.Add(cardId);
            }
            if (log && from == "Library" && zone == "Hand") Say(Player(c.Owner)?.Name + " drew a card.");   // the name stays secret
            else if (log && from != zone) Say(Player(toPlayer)?.Name + ": " + Describe(c) + " " + Arrow(from, zone, index));
        }

        static string Arrow(string from, string to, int index)
        {
            if (to == "Library") return index < 0 ? "→ bottom of library" : "→ top of library";
            if (to == "Battlefield") return from == "Hand" ? "played" : "→ battlefield";
            return "→ " + to.ToLowerInvariant();
        }

        string Describe(TableCard c) => c.FaceDown && c.Zone == "Battlefield" ? "a face-down card" : c.Name;

        static bool Is(TableCard c, string type)
        {
            var info = CardCatalog.Get(c.Name);
            if (info == null) return false;
            return type == "Land" ? info.Land : (info.Type ?? "").IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A free battlefield spot on a row (y in 0..1), left to right.</summary>
        public Vector2 FreeSpot(int playerId, float row)
        {
            var taken = Battlefield(playerId).Where(c => Mathf.Abs(c.Pos.y - row) < 0.2f).Select(c => c.Pos.x).ToList();
            for (float x = 0.06f; x < 0.96f; x += 0.075f)
                if (!taken.Any(t => Mathf.Abs(t - x) < 0.05f)) return new Vector2(x, row);
            return new Vector2(0.5f + (float)rng.NextDouble() * 0.3f, row + 0.08f);
        }

        public void SetTapped(int cardId, bool tapped)
        {
            if (Cards.TryGetValue(cardId, out var c) && c.Zone == "Battlefield") c.Tapped = tapped;
        }

        public void UntapAll(TablePlayer p)
        {
            foreach (var c in Battlefield(p.Id)) c.Tapped = false;
            Say(p.Name + " untapped everything.");
        }

        public void AddCounter(int cardId, string kind, int delta)
        {
            if (!Cards.TryGetValue(cardId, out var c)) return;
            // +1/+1 and -1/-1 counters cancel each other out (rule 704.5q).
            string opposite = kind == "+1/+1" ? "-1/-1" : kind == "-1/-1" ? "+1/+1" : null;
            if (opposite != null && delta > 0 && c.Counters.TryGetValue(opposite, out int other) && other > 0)
            {
                int cancel = Mathf.Min(other, delta);
                other -= cancel; delta -= cancel;
                if (other <= 0) c.Counters.Remove(opposite); else c.Counters[opposite] = other;
                if (delta == 0) return;
            }
            c.Counters.TryGetValue(kind, out int n);
            n += delta;
            if (n <= 0) c.Counters.Remove(kind); else c.Counters[kind] = n;
        }

        public TableCard CreateToken(int controller, string name, Vector2? pos = null, string pt = "", string colors = "")
        {
            var c = NewCard(name, controller, "Battlefield");
            c.Token = true;
            c.TokenPT = pt ?? ""; c.TokenColors = colors ?? "";
            c.Pos = pos ?? FreeSpot(controller, 0.55f);
            Say(Player(controller).Name + " created a " + name + " token.");
            return c;
        }

        public TableCard CloneCard(int cardId)
        {
            if (!Cards.TryGetValue(cardId, out var src)) return null;
            var c = CreateToken(src.Controller, src.Name, src.Pos + new Vector2(0.05f, -0.05f), src.TokenPT, src.TokenColors);
            return c;
        }

        public void NextTurn()
        {
            Active = (Active + 1) % Players.Count;
            if (Active == 0) Turn++;
            var p = ActivePlayer;
            Say("— Turn " + Turn + ": " + p.Name + " —");
            foreach (var c in Battlefield(p.Id)) { c.Tapped = false; c.Sick = false; }
            Draw(p, 1);
        }

        /// <summary>Takes a card out of the game entirely (tokens, misclicks): it's in no zone anymore.</summary>
        public void Delete(int cardId)
        {
            if (!Cards.TryGetValue(cardId, out var c)) return;
            foreach (var pl in Players) foreach (var z in new[] { "Library", "Hand", "Graveyard", "Exile", "Command" }) pl.ZoneList(z)?.Remove(cardId);
            Cards.Remove(cardId);
        }

        public int Roll(int sides) => rng.Next(1, sides + 1);

        /// <summary>London-style mulligan helper: hand back, shuffle, draw seven (put cards on the bottom yourself).</summary>
        public void Mulligan(TablePlayer p)
        {
            foreach (var id in p.Hand.ToList()) Move(id, "Library", p.Id, -1, null, false);
            Shuffle(p, false);
            Say(p.Name + " mulliganed.");
            Draw(p, 7);
        }
    }
}

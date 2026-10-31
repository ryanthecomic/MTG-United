using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// Table actions as plain data ({"op": "move", "id": 12, "zone": "Graveyard", ...}), so the same action runs
    /// locally, on the LAN host for a remote player, and in the undo history. Also: the whole table as JSON
    /// (sent to LAN players after every change, with other players' hidden cards blanked out).
    /// </summary>
    public partial class TableState
    {
        /// <summary>Things everyone should see pop up (dice results, reveals), drained by the view / host.</summary>
        public readonly List<Dictionary<string, object>> Events = new List<Dictionary<string, object>>();

        void Event(string kind, string text, params string[] cards)
        {
            Events.Add(new Dictionary<string, object> { { "kind", kind }, { "text", text }, { "cards", cards.Cast<object>().ToList() } });
        }

        static float F(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is double x ? (float)x : 0f;

        /// <summary>Applies one action. "by" is the acting player's id (for the log).</summary>
        public void Apply(Dictionary<string, object> a)
        {
            var by = Player(a.Int("by", Players.Count > 0 ? Players[0].Id : 1));
            var p = Player(a.Int("p", by?.Id ?? 0)) ?? by;
            int id = a.Int("id", -1);
            Cards.TryGetValue(id, out var c);
            switch (a.Str("op"))
            {
                case "draw": Draw(p, a.Int("n", 1)); break;
                case "shuffle": Shuffle(p); break;
                case "next": NextTurn(); break;
                case "life":
                    p.Life += a.Int("d");
                    Say(p.Name + ": " + (a.Int("d") > 0 ? "+" : "") + a.Int("d") + " life (" + p.Life + ")");
                    break;
                case "setlife": p.Life = a.Int("v"); Say(p.Name + ": life set to " + p.Life); break;
                case "poison": p.Poison = Mathf.Max(0, p.Poison + a.Int("d")); Say(p.Name + ": poison " + p.Poison); break;
                case "move":
                    Vector2? pos = a.ContainsKey("x") ? new Vector2(F(a, "x"), F(a, "y")) : (Vector2?)null;
                    Move(id, a.Str("zone"), a.Int("to", p.Id), a.Int("index", 0), pos, a.Bool("log", true));
                    if (c != null && a.Bool("facedown")) c.FaceDown = true;
                    if (c != null && a.ContainsKey("hindex") && c.Zone == "Hand")
                    {
                        // Dropped into a hand at a spot: put it there instead of at the end.
                        var hand = Player(c.Owner).Hand;
                        hand.Remove(id);
                        hand.Insert(Mathf.Clamp(a.Int("hindex"), 0, hand.Count), id);
                    }
                    break;
                case "flip":
                    if (c == null) break;
                    c.Flipped = !c.Flipped;
                    string other = CustomSets.OtherFace(c.Name);
                    Say((by?.Name ?? "") + ": " + (c.Flipped ? c.Name + " transformed into " + (other ?? "its back") : (other ?? "it") + " transformed back into " + c.Name) + ".");
                    break;
                case "note":
                    if (c == null) break;
                    c.Note = (a.Str("text") ?? "").Trim();
                    Say((by?.Name ?? "") + (c.Note.Length > 0 ? " put a note on " : " removed the note from ") + (c.FaceDown ? "a face-down card" : c.Name) + ".");
                    break;
                case "rotate": if (c != null) c.Rot = ((c.Rot + a.Int("d", 1)) % 4 + 4) % 4; break;
                case "pos": if (c != null) c.Pos = new Vector2(F(a, "x"), F(a, "y")); break;
                case "give":
                    if (c == null) break;
                    Move(id, "Battlefield", a.Int("to"), 0, new Vector2(F(a, "x"), F(a, "y")), false);
                    Say(by?.Name + " gave control of " + c.Name + " to " + Player(a.Int("to"))?.Name + ".");
                    break;
                case "tap": SetTapped(id, a.Bool("v")); break;
                case "face": if (c != null) c.FaceDown = a.Bool("v"); break;
                case "counter":
                    if (c == null) break;
                    string kind = a.Str("kind");
                    if (kind == "*first") kind = c.Counters.Keys.FirstOrDefault();
                    if (kind != null) AddCounter(id, kind, a.Int("d", 1));
                    break;
                case "token":
                    Vector2? tpos = a.ContainsKey("x") ? new Vector2(F(a, "x"), F(a, "y")) : (Vector2?)null;
                    CreateToken(a.Int("to", p.Id), a.Str("name"), tpos, a.Str("pt"), a.Str("colors"));
                    break;
                case "clone": CloneCard(id); break;
                case "untapall": UntapAll(p); break;
                case "mill": Mill(p, a.Int("n", 1)); break;
                case "mulligan": Mulligan(p); break;
                case "handorder":
                    if (p.Hand.Remove(id)) p.Hand.Insert(Mathf.Clamp(a.Int("index"), 0, p.Hand.Count), id);
                    break;
                case "exilegy":
                    foreach (var g in p.Graveyard.ToList()) Move(g, "Exile", p.Id, 0, null, false);
                    Say(p.Name + " exiled their graveyard.");
                    break;
                case "zonetolib":
                    foreach (var z in p.ZoneList(a.Str("zone")).ToList()) Move(z, "Library", p.Id, -1, null, false);
                    Shuffle(p);
                    break;
                case "roll":
                {
                    int sides = a.Int("sides", 6), r = Roll(sides);
                    Say(p.Name + " rolled a d" + sides + ": " + r);
                    Event("toast", p.Name + " rolled a d" + sides + ": " + r);
                    break;
                }
                case "coin":
                {
                    string side = Roll(2) == 1 ? "heads" : "tails";
                    Say(p.Name + " flipped a coin: " + side);
                    Event("toast", p.Name + " flipped " + side);
                    break;
                }
                case "randomplayer":
                {
                    var rp = Players[Roll(Players.Count) - 1];
                    Say("Random player: " + rp.Name);
                    Event("toast", "Random player: " + rp.Name);
                    break;
                }
                case "reveal":
                    if (c == null) break;
                    c.Revealed = true;
                    Say(p.Name + " revealed " + c.Name + (c.Zone == "Library" ? " from the top of their library." : "."));
                    Event("reveal", p.Name + " reveals", c.Name);
                    break;
                case "say": Say(a.Str("text")); break;
                case "undo": if (Undo()) Say((by?.Name ?? "Someone") + " undid the last action."); break;
            }
        }

        // ---------------------------------------------------------------- JSON snapshot

        /// <summary>The table for one viewer: other players' hands, libraries and face-down cards are blanked.</summary>
        public Dictionary<string, object> ToJson(int viewerId)
        {
            var players = new List<object>();
            foreach (var p in Players)
                players.Add(new Dictionary<string, object>
                {
                    { "id", p.Id }, { "name", p.Name }, { "deck", p.DeckName ?? "" }, { "life", p.Life }, { "poison", p.Poison },
                    { "library", p.Library.Cast<object>().ToList() }, { "hand", p.Hand.Cast<object>().ToList() },
                    { "graveyard", p.Graveyard.Cast<object>().ToList() }, { "exile", p.Exile.Cast<object>().ToList() },
                    { "command", p.Command.Cast<object>().ToList() }, { "commanders", p.CommanderNames.Cast<object>().ToList() },
                });
            var cards = new List<object>();
            foreach (var c in Cards.Values)
            {
                bool hidden = (c.Zone == "Hand" && c.Owner != viewerId)
                              || (c.Zone == "Library" && !c.Revealed && c.Owner != viewerId)   // you can search your own library
                              || (c.Zone == "Battlefield" && c.FaceDown && c.Controller != viewerId);
                var counters = new Dictionary<string, object>();
                foreach (var kv in c.Counters) counters[kv.Key] = kv.Value;
                cards.Add(new Dictionary<string, object>
                {
                    { "id", c.Id }, { "name", hidden ? "" : c.Name }, { "owner", c.Owner }, { "ctrl", c.Controller }, { "zone", c.Zone },
                    { "x", (double)c.Pos.x }, { "y", (double)c.Pos.y }, { "tap", c.Tapped }, { "rot", c.Rot }, { "flip", c.Flipped }, { "note", c.Note ?? "" }, { "fd", c.FaceDown }, { "tok", c.Token },
                    { "rev", c.Revealed }, { "pt", c.TokenPT }, { "col", c.TokenColors }, { "ctr", counters },
                });
            }
            int from = Mathf.Max(0, Log.Count - 60);
            return new Dictionary<string, object>
            {
                { "turn", Turn }, { "active", Active }, { "next", nextId },
                { "players", players }, { "cards", cards }, { "log", Log.GetRange(from, Log.Count - from).Cast<object>().ToList() },
            };
        }

        public static TableState FromJson(Dictionary<string, object> d)
        {
            var s = new TableState { Turn = d.Int("turn", 1), Active = d.Int("active") };
            s.nextId = d.Int("next", 1);
            foreach (var o in d.Arr("players"))
            {
                var j = o as Dictionary<string, object>;
                if (j == null) continue;
                var p = new TablePlayer { Id = j.Int("id"), Name = j.Str("name"), DeckName = j.Str("deck"), Life = j.Int("life"), Poison = j.Int("poison") };
                p.Library = Ints(j, "library"); p.Hand = Ints(j, "hand"); p.Graveyard = Ints(j, "graveyard");
                p.Exile = Ints(j, "exile"); p.Command = Ints(j, "command");
                foreach (var n in j.Arr("commanders")) p.CommanderNames.Add(n as string);
                s.Players.Add(p);
            }
            foreach (var o in d.Arr("cards"))
            {
                var j = o as Dictionary<string, object>;
                if (j == null) continue;
                var c = new TableCard
                {
                    Id = j.Int("id"), Name = j.Str("name"), Owner = j.Int("owner"), Controller = j.Int("ctrl"), Zone = j.Str("zone"),
                    Pos = new Vector2(F(j, "x"), F(j, "y")), Tapped = j.Bool("tap"), Rot = j.Int("rot"), Flipped = j.Bool("flip"), Note = j.Str("note"), FaceDown = j.Bool("fd"), Token = j.Bool("tok"),
                    Revealed = j.Bool("rev"), TokenPT = j.Str("pt"), TokenColors = j.Str("col"),
                };
                var ctr = j.Obj("ctr");
                if (ctr != null) foreach (var kv in ctr) if (kv.Value is double v) c.Counters[kv.Key] = (int)v;
                s.Cards[c.Id] = c;
            }
            foreach (var l in d.Arr("log")) s.Log.Add(l as string);
            return s;
        }

        static List<int> Ints(Dictionary<string, object> d, string k)
        {
            var list = new List<int>();
            foreach (var o in d.Arr(k)) if (o is double v) list.Add((int)v);
            return list;
        }
    }
}

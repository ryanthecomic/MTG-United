using System.Collections.Generic;

namespace MTGUnited
{
    /// <summary>Typed copy of the bridge's "state" message. Rebuilt from scratch on every update.</summary>
    public class GameState
    {
        public int Turn;
        public string Phase = "";
        public string PhaseLabel = "";
        public int ActivePlayer = -1;
        public int PriorityPlayer = -1;
        public int Me = -1;
        public bool GameOver;
        public bool Mulligan;
        public string Winner;
        public List<PlayerData> Players = new List<PlayerData>();
        public List<StackItemData> Stack = new List<StackItemData>();
        public List<CombatData> Combat = new List<CombatData>();
        public PromptData Prompt = new PromptData();

        public readonly Dictionary<int, CardData> CardsById = new Dictionary<int, CardData>();

        public PlayerData MePlayer => Players.Find(p => p.Id == Me);
        public PlayerData Player(int id) => Players.Find(p => p.Id == id);
        public IEnumerable<PlayerData> Opponents { get { foreach (var p in Players) if (p.Id != Me) yield return p; } }

        public static GameState FromMessage(Dictionary<string, object> msg)
        {
            var s = new GameState();
            var g = msg.Obj("game");
            if (g != null)
            {
                s.Turn = g.Int("turn");
                s.Phase = g.Str("phase");
                s.PhaseLabel = g.Str("phaseLabel");
                s.ActivePlayer = g.Int("activePlayer", -1);
                s.PriorityPlayer = g.Int("priorityPlayer", -1);
                s.Me = g.Int("me", -1);
                s.GameOver = g.Bool("gameOver");
                s.Mulligan = g.Bool("mulligan");
                s.Winner = g.Str("winner", null);
                foreach (var o in g.Arr("players")) s.Players.Add(PlayerData.From(o as Dictionary<string, object>, s));
                foreach (var o in g.Arr("stack")) s.Stack.Add(StackItemData.From(o as Dictionary<string, object>, s));
                foreach (var o in g.Arr("combat")) s.Combat.Add(CombatData.From(o as Dictionary<string, object>));
            }
            s.Prompt = PromptData.From(msg.Obj("prompt"), s);
            return s;
        }

        internal CardData Register(CardData c)
        {
            if (c != null && c.Id >= 0) CardsById[c.Id] = c;
            return c;
        }

        public CombatData CombatFor(int attackerId) => Combat.Find(c => c.Attacker == attackerId);
        public bool IsBlocking(int cardId) => Combat.Exists(c => c.Blockers.Contains(cardId));
    }

    public class PlayerData
    {
        public int Id;
        public string Name;
        public bool IsAI;
        public int Life;
        public bool HasLost;
        public bool HasPriority;
        public Dictionary<string, int> Counters = new Dictionary<string, int>();
        public Dictionary<string, int> Mana = new Dictionary<string, int>();
        public List<CommanderDamage> CommanderDamage = new List<CommanderDamage>();
        public List<CommanderInfo> Commanders = new List<CommanderInfo>();
        public List<CardData> Hand = new List<CardData>();
        public List<CardData> Battlefield = new List<CardData>();
        public List<CardData> Graveyard = new List<CardData>();
        public List<CardData> Exile = new List<CardData>();
        public List<CardData> Command = new List<CardData>();
        public int LibrarySize, HandSize, GraveyardSize, ExileSize;

        public string ShortName => Name != null && Name.Contains(" (") ? Name.Substring(0, Name.IndexOf(" (")) : Name;

        public int Poison => Counters.TryGetValue("Poison", out var v) ? v : Counters.TryGetValue("POISON", out var w) ? w : 0;

        public int TotalManaInPool
        {
            get { int n = 0; foreach (var kv in Mana) n += kv.Value; return n; }
        }

        public static PlayerData From(Dictionary<string, object> d, GameState s)
        {
            var p = new PlayerData
            {
                Id = d.Int("id"),
                Name = d.Str("name"),
                IsAI = d.Bool("isAI"),
                Life = d.Int("life"),
                HasLost = d.Bool("hasLost"),
                HasPriority = d.Bool("hasPriority"),
            };
            var counters = d.Obj("counters");
            if (counters != null) foreach (var kv in counters) p.Counters[kv.Key] = (int)(double)kv.Value;
            var mana = d.Obj("mana");
            if (mana != null) foreach (var kv in mana) p.Mana[kv.Key] = (int)(double)kv.Value;
            foreach (var o in d.Arr("commanderDamage"))
            {
                var cd = o as Dictionary<string, object>;
                p.CommanderDamage.Add(new CommanderDamage
                {
                    CommanderId = cd.Int("commanderId"),
                    CommanderName = cd.Str("commanderName"),
                    FromPlayer = cd.Int("fromPlayer"),
                    Damage = cd.Int("damage"),
                });
            }
            foreach (var o in d.Arr("commanders"))
            {
                var cd = o as Dictionary<string, object>;
                p.Commanders.Add(new CommanderInfo { Id = cd.Int("id"), Name = cd.Str("name"), TimesCast = cd.Int("timesCast") });
            }
            var zones = d.Obj("zones");
            p.Hand = Cards(zones, "hand", s);
            p.Battlefield = Cards(zones, "battlefield", s);
            p.Graveyard = Cards(zones, "graveyard", s);
            p.Exile = Cards(zones, "exile", s);
            p.Command = Cards(zones, "command", s);
            var sizes = d.Obj("zoneSizes");
            p.LibrarySize = sizes.Int("library");
            p.HandSize = sizes.Int("hand");
            p.GraveyardSize = sizes.Int("graveyard");
            p.ExileSize = sizes.Int("exile");
            return p;
        }

        static List<CardData> Cards(Dictionary<string, object> zones, string key, GameState s)
        {
            var list = new List<CardData>();
            if (zones == null) return list;
            foreach (var o in zones.Arr(key)) list.Add(s.Register(CardData.From(o as Dictionary<string, object>)));
            return list;
        }
    }

    public class CommanderDamage { public int CommanderId; public string CommanderName; public int FromPlayer; public int Damage; }
    public class CommanderInfo { public int Id; public string Name; public int TimesCast; }

    public class CardData
    {
        public int Id = -1;
        public bool Hidden;
        public string Name = "";
        public string OracleName = "";
        public string Set = "";
        public string ImageKey = "";
        public string ManaCost = "";
        public string TypeLine = "";
        public string Text = "";
        public string Colors = "";
        public string AltName;
        public int Owner = -1, Controller = -1;
        public string Zone = "";
        public bool Tapped, Sick, FaceDown, Token, Commander, Attacking, Blocking, PhasedOut, DoubleFaced;
        public bool IsCreature, IsLand, IsPlaneswalker, IsInstant, IsSorcery, IsArtifact, IsEnchantment, IsBattle;
        public bool HasPT;
        public int Power, Toughness, Damage;
        public string Loyalty, Defense;
        public int AttachedTo = -1, AttachedToPlayer = -1;
        public Dictionary<string, int> Counters = new Dictionary<string, int>();

        public static CardData From(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var c = new CardData
            {
                Id = d.Int("id", -1),
                Hidden = d.Bool("hidden"),
                Name = d.Str("name"),
                OracleName = d.Str("oracleName"),
                Set = d.Str("set"),
                ImageKey = d.Str("imageKey"),
                ManaCost = d.Str("manaCost"),
                TypeLine = d.Str("typeLine"),
                Text = d.Str("text"),
                Colors = d.Str("colors"),
                AltName = d.Str("altName", null),
                Owner = d.Int("owner", -1),
                Controller = d.Int("controller", -1),
                Zone = d.Str("zone"),
                Tapped = d.Bool("tapped"),
                Sick = d.Bool("sick"),
                FaceDown = d.Bool("faceDown"),
                Token = d.Bool("token"),
                Commander = d.Bool("commander"),
                Attacking = d.Bool("attacking"),
                Blocking = d.Bool("blocking"),
                PhasedOut = d.Bool("phasedOut"),
                DoubleFaced = d.Bool("doubleFaced"),
                IsCreature = d.Bool("isCreature"),
                IsLand = d.Bool("isLand"),
                IsPlaneswalker = d.Bool("isPlaneswalker"),
                IsInstant = d.Bool("isInstant"),
                IsSorcery = d.Bool("isSorcery"),
                IsArtifact = d.Bool("isArtifact"),
                IsEnchantment = d.Bool("isEnchantment"),
                IsBattle = d.Bool("isBattle"),
                HasPT = d.ContainsKey("power"),
                Power = d.Int("power"),
                Toughness = d.Int("toughness"),
                Damage = d.Int("damage"),
                Loyalty = d.Str("loyalty", null),
                Defense = d.Str("defense", null),
                AttachedTo = d.Int("attachedTo", -1),
                AttachedToPlayer = d.Int("attachedToPlayer", -1),
            };
            var counters = d.Obj("counters");
            if (counters != null) foreach (var kv in counters) c.Counters[kv.Key] = (int)(double)kv.Value;
            return c;
        }

        /// <summary>Key used for the image cache; identical cards share one download.</summary>
        public string ImageCacheKey => Hidden || FaceDown ? null : (Name + "|" + Set);
    }

    public class StackItemData
    {
        public int Id;
        public string Text;
        public bool IsAbility, IsTrigger;
        public int SourceCardId;
        public CardData Source;
        public int ActivatingPlayer;
        public List<int> TargetCards = new List<int>();
        public List<int> TargetPlayers = new List<int>();

        public static StackItemData From(Dictionary<string, object> d, GameState s)
        {
            return new StackItemData
            {
                Id = d.Int("id"),
                Text = d.Str("text"),
                IsAbility = d.Bool("isAbility"),
                IsTrigger = d.Bool("isTrigger"),
                SourceCardId = d.Int("sourceCard", -1),
                Source = CardData.From(d.Obj("source")),
                ActivatingPlayer = d.Int("activatingPlayer", -1),
                TargetCards = d.IntList("targetCards"),
                TargetPlayers = d.IntList("targetPlayers"),
            };
        }
    }

    public class CombatData
    {
        public int Attacker;
        public string DefenderKind;
        public int DefenderId = -1;
        public List<int> Blockers = new List<int>();

        public static CombatData From(Dictionary<string, object> d)
        {
            var def = d.Obj("defender");
            return new CombatData
            {
                Attacker = d.Int("attacker"),
                DefenderKind = def?.Str("kind"),
                DefenderId = def?.Int("id", -1) ?? -1,
                Blockers = d.IntList("blockers"),
            };
        }
    }

    public class EntityRef
    {
        public string Kind;
        public int Id;
        public static EntityRef From(Dictionary<string, object> d) => new EntityRef { Kind = d.Str("kind"), Id = d.Int("id") };
    }

    public class PromptData
    {
        public string Message = "";
        public int CardId = -1;
        public string OkLabel = "", CancelLabel = "";
        public bool OkEnabled, CancelEnabled, OkFocused;
        public HashSet<int> SelectableCards = new HashSet<int>();
        public List<CardData> SelectableCardData = new List<CardData>();
        public HashSet<int> ActionableCards = new HashSet<int>();
        public HashSet<int> TargetPlayers = new HashSet<int>();
        public HashSet<int> TargetStack = new HashSet<int>();
        public HashSet<int> ChosenCards = new HashSet<int>();
        public HashSet<int> ChosenPlayers = new HashSet<int>();
        public int SelMin, SelMax;

        public static PromptData From(Dictionary<string, object> d, GameState s)
        {
            var p = new PromptData();
            if (d == null) return p;
            p.Message = d.Str("message");
            p.CardId = d.Int("card", -1);
            var ok = d.Obj("ok");
            p.OkLabel = ok.Str("label");
            p.OkEnabled = ok.Bool("enabled");
            p.OkFocused = ok.Bool("focused");
            var cancel = d.Obj("cancel");
            p.CancelLabel = cancel.Str("label");
            p.CancelEnabled = cancel.Bool("enabled");
            var sel = d.Obj("selection");
            if (sel != null)
            {
                foreach (var id in sel.IntList("cards")) p.SelectableCards.Add(id);
                foreach (var o in sel.Arr("cardData"))
                {
                    var c = CardData.From(o as Dictionary<string, object>);
                    if (c != null) p.SelectableCardData.Add(c);
                }
                foreach (var id in sel.IntList("actionable")) p.ActionableCards.Add(id);
                p.SelMin = sel.Int("min");
                p.SelMax = sel.Int("max");
                foreach (var o in sel.Arr("targets"))
                {
                    var r = EntityRef.From(o as Dictionary<string, object>);
                    if (r.Kind == "player") p.TargetPlayers.Add(r.Id);
                    else if (r.Kind == "stack") p.TargetStack.Add(r.Id);
                    else if (r.Kind == "card") p.SelectableCards.Add(r.Id);
                }
                foreach (var o in sel.Arr("chosen"))
                {
                    var r = EntityRef.From(o as Dictionary<string, object>);
                    if (r.Kind == "player") p.ChosenPlayers.Add(r.Id);
                    else if (r.Kind == "card") p.ChosenCards.Add(r.Id);
                }
            }
            return p;
        }
    }
}

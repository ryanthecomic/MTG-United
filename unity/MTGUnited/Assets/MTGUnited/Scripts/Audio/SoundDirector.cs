using System.Collections.Generic;
using System.Linq;

namespace MTGUnited
{
    /// <summary>
    /// Compares each new game state with the previous one and plays the matching sounds. Each kind of sound
    /// plays at most once per update, so a board wipe or a big untap is one sound, not twenty.
    /// Combat impacts are played by BoardView at the moment the lunge lands.
    /// </summary>
    public static class SoundDirector
    {
        static GameState prev;

        public static void Reset() { prev = null; }

        public static void OnState(GameState s)
        {
            var p = prev;
            prev = s;
            if (p == null || s == null || p.Me != s.Me) return;
            if (s.Mulligan || p.Mulligan) { if (HandGrew(p, s)) Sound.Play(Sfx.Draw, 0.8f); return; }

            var played = new HashSet<Sfx>();
            void Once(Sfx sfx, float vol = 1f) { if (played.Add(sfx)) Sound.Play(sfx, vol); }

            // Turn start (my turn gets the brighter cue).
            if (s.ActivePlayer != p.ActivePlayer && s.ActivePlayer == s.Me) Once(Sfx.TurnStart, 0.7f);

            // New stack items: spells swell in their colour, abilities and triggers chime.
            var oldStack = new HashSet<int>(p.Stack.Select(x => x.Id));
            foreach (var si in s.Stack)
            {
                if (oldStack.Contains(si.Id)) continue;
                if (si.IsAbility || si.IsTrigger) Once(Sfx.Ability, 0.8f);
                else if (played.Add(Sfx.Cast)) Sound.PlayCast(si.Source?.Colors);
            }
            // Something resolved off the stack.
            var newStack = new HashSet<int>(s.Stack.Select(x => x.Id));
            bool resolved = p.Stack.Any(x => !newStack.Contains(x.Id));

            // Card movement.
            foreach (var kv in s.CardsById)
            {
                var d = kv.Value;
                p.CardsById.TryGetValue(d.Id, out var before);
                if (d.Zone == "Battlefield")
                {
                    if (before == null || before.Zone != "Battlefield")
                    {
                        if (d.Token && before == null) Once(Sfx.Token, 0.8f);
                        else if (d.IsLand) Once(Sfx.LandPlay);
                        else Once(Sfx.Resolve);
                    }
                    else
                    {
                        if (!before.Tapped && d.Tapped && !d.Attacking) Once(Sfx.Tap, 0.7f);
                        if (before.Tapped && !d.Tapped) Once(Sfx.Untap, 0.6f);
                        if (!before.Attacking && d.Attacking) Once(Sfx.Attack);
                        if (CounterTotal(d) != CounterTotal(before)) Once(Sfx.Counter, 0.8f);
                    }
                }
                else if (before != null && before.Zone != d.Zone)
                {
                    if (d.Zone == "Graveyard") Once(before.Zone == "Hand" ? Sfx.Discard : before.Zone == "Battlefield" ? Sfx.Die : Sfx.Discard);
                    else if (d.Zone == "Exile") Once(Sfx.Exile, 0.9f);
                }
            }
            // Cards that left view: went back to a library or a hidden hand (bounce, tuck, shuffle).
            foreach (var kv in p.CardsById)
                if (kv.Value.Zone == "Battlefield" && !s.CardsById.ContainsKey(kv.Key)) Once(Sfx.ToLibrary, 0.8f);

            if (resolved && !played.Contains(Sfx.Resolve) && !played.Contains(Sfx.LandPlay) && !played.Contains(Sfx.Token)
                && !played.Contains(Sfx.Die) && !played.Contains(Sfx.Exile))
                Once(Sfx.Resolve, 0.55f);   // instants / sorceries / abilities resolving get a lighter thump

            if (HandGrew(p, s)) Once(Sfx.Draw, 0.8f);

            // Life totals.
            foreach (var pl in s.Players)
            {
                var b = p.Player(pl.Id);
                if (b == null || b.Life == pl.Life) continue;
                Once(pl.Life < b.Life ? Sfx.LifeLoss : Sfx.LifeGain, pl.Id == s.Me ? 1f : 0.7f);
            }

            // Priority returns to me while something is on the stack: a soft ding so I notice I can respond.
            if (s.Stack.Count > 0 && s.PriorityPlayer == s.Me && p.PriorityPlayer != s.Me && played.Count == 0)
                Once(Sfx.Priority, 0.6f);
        }

        static bool HandGrew(GameState p, GameState s)
        {
            var a = p.MePlayer; var b = s.MePlayer;
            return a != null && b != null && b.HandSize > a.HandSize && b.LibrarySize < a.LibrarySize;
        }

        static int CounterTotal(CardData d)
        {
            int n = 0;
            foreach (var v in d.Counters.Values) n += v;
            return n;
        }
    }
}

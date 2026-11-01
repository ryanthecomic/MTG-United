package mtgunited.bridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.common.collect.Multiset;
import forge.card.MagicColor;
import forge.card.mana.ManaCost;
import forge.game.GameEntityView;
import forge.game.GameView;
import forge.game.card.CardView;
import forge.game.card.CardView.CardStateView;
import forge.game.card.CounterType;
import forge.game.combat.CombatView;
import forge.game.keyword.Keyword;
import forge.game.phase.PhaseType;
import forge.game.player.PlayerView;
import forge.game.spellability.StackItemView;
import forge.game.zone.ZoneType;
import forge.trackable.TrackableObject;

import java.util.ArrayList;
import java.util.List;

/**
 * Turns Forge's view objects into the JSON the Unity client renders. Everything is keyed by Forge's own ids, which
 * are stable for the life of a card or player and are what the client sends back when it clicks something.
 */
final class StateSerializer {
    private final BridgeGuiGame gui;
    private static final Keyword[] ANIMATED_KEYWORDS = {
        Keyword.FLYING, Keyword.VIGILANCE, Keyword.LIFELINK, Keyword.DEATHTOUCH, Keyword.TRAMPLE,
        Keyword.FIRST_STRIKE, Keyword.DOUBLE_STRIKE, Keyword.INDESTRUCTIBLE, Keyword.MENACE, Keyword.REACH,
    };

    StateSerializer(final BridgeGuiGame gui) {
        this.gui = gui;
    }

    JsonObject game(final GameView gv, final PlayerView me) {
        final JsonObject o = new JsonObject();
        if (gv == null) { return o; }
        o.addProperty("turn", gv.getTurn());
        final PhaseType phase = gv.getPhase();
        o.addProperty("phase", phase == null ? "" : phase.name());
        o.addProperty("phaseLabel", phase == null ? "" : phase.nameForUi);
        o.addProperty("activePlayer", gv.getPlayerTurn() == null ? -1 : gv.getPlayerTurn().getId());
        o.addProperty("me", me == null ? -1 : me.getId());
        o.addProperty("gameOver", gv.isGameOver());
        o.addProperty("mulligan", gv.isMulligan());
        o.addProperty("winner", gv.isGameOver() ? gv.getWinningPlayerName() : null);

        int priority = -1;
        final JsonArray players = new JsonArray();
        for (final PlayerView p : snapshot(gv.getPlayers())) {
            if (p.getHasPriority()) { priority = p.getId(); }
            try {
                players.add(player(p, gv));
            } catch (final RuntimeException ex) {
                // Never let one bad object stop the whole update; send what we can.
                gui.logOnce("Serializing player " + p.getName(), ex);
                final JsonObject minimal = new JsonObject();
                minimal.addProperty("id", p.getId());
                minimal.addProperty("name", p.getName());
                minimal.addProperty("life", p.getLife());
                players.add(minimal);
            }
        }
        o.addProperty("priorityPlayer", priority);
        o.add("players", players);

        final JsonArray stack = new JsonArray();
        for (final StackItemView si : snapshot(gv.getStack())) {
            try {
                stack.add(stackItem(si));
            } catch (final RuntimeException ex) {
                gui.logOnce("Serializing stack item", ex);
            }
        }
        o.add("stack", stack);
        o.add("combat", combat(gv.getCombat()));
        return o;
    }

    private JsonObject player(final PlayerView p, final GameView gv) {
        final JsonObject o = new JsonObject();
        o.addProperty("id", p.getId());
        o.addProperty("name", p.getName());
        o.addProperty("isAI", p.isAI());
        o.addProperty("life", p.getLife());
        o.addProperty("hasLost", p.getHasLost());
        o.addProperty("hasPriority", p.getHasPriority());
        o.addProperty("landsPlayedThisTurn", p.getNumLandThisTurn());
        o.add("counters", counters(p.getCounters()));

        final JsonObject mana = new JsonObject();
        mana.addProperty("W", p.getMana(MagicColor.WHITE));
        mana.addProperty("U", p.getMana(MagicColor.BLUE));
        mana.addProperty("B", p.getMana(MagicColor.BLACK));
        mana.addProperty("R", p.getMana(MagicColor.RED));
        mana.addProperty("G", p.getMana(MagicColor.GREEN));
        mana.addProperty("C", p.getMana(MagicColor.COLORLESS));
        o.add("mana", mana);

        // Commander damage this player has taken, keyed by the commander card's id.
        final JsonArray cmdDamage = new JsonArray();
        for (final PlayerView other : snapshot(gv.getPlayers())) {
            final List<CardView> commanders = other.getCommanders();
            if (commanders == null) { continue; }
            for (final CardView cmd : new ArrayList<>(commanders)) {
                final int dmg = p.getCommanderDamage(cmd);
                if (dmg > 0) {
                    final JsonObject d = new JsonObject();
                    d.addProperty("commanderId", cmd.getId());
                    d.addProperty("commanderName", cmd.getName());
                    d.addProperty("fromPlayer", other.getId());
                    d.addProperty("damage", dmg);
                    cmdDamage.add(d);
                }
            }
        }
        o.add("commanderDamage", cmdDamage);

        final JsonArray commanders = new JsonArray();
        final List<CardView> cmds = p.getCommanders();
        if (cmds != null) {
            for (final CardView c : new ArrayList<>(cmds)) {
                final JsonObject cj = new JsonObject();
                cj.addProperty("id", c.getId());
                cj.addProperty("name", c.getName());
                cj.addProperty("timesCast", p.getCommanderCast(c));
                commanders.add(cj);
            }
        }
        o.add("commanders", commanders);

        final JsonObject zones = new JsonObject();
        zones.add("hand", zone(p, ZoneType.Hand));
        zones.add("battlefield", zone(p, ZoneType.Battlefield));
        zones.add("graveyard", zone(p, ZoneType.Graveyard));
        zones.add("exile", zone(p, ZoneType.Exile));
        zones.add("command", zone(p, ZoneType.Command));
        zones.add("libraryKnown", knownLibrary(p));
        o.add("zones", zones);

        final JsonObject sizes = new JsonObject();
        sizes.addProperty("library", p.getZoneSize(ZoneType.Library));
        sizes.addProperty("hand", p.getZoneSize(ZoneType.Hand));
        sizes.addProperty("graveyard", p.getZoneSize(ZoneType.Graveyard));
        sizes.addProperty("exile", p.getZoneSize(ZoneType.Exile));
        o.add("zoneSizes", sizes);
        return o;
    }

    /** Library cards this player may see right now (top card revealed, looked at...), top first, with their depth. */
    private JsonArray knownLibrary(final PlayerView p) {
        final JsonArray arr = new JsonArray();
        try {
            int depth = 0;
            for (final CardView c : snapshot(p.getCards(ZoneType.Library))) {
                depth++;
                if (c == null || !gui.mayView(c)) { continue; }
                final JsonObject cj = card(c);
                cj.addProperty("depth", depth);
                arr.add(cj);
                if (arr.size() >= 30) { break; }
            }
        } catch (final RuntimeException ex) {
            gui.logOnce("Reading the library", ex);
        }
        return arr;
    }

    private JsonArray zone(final PlayerView p, final ZoneType zt) {
        final JsonArray arr = new JsonArray();
        for (final CardView c : snapshot(p.getCards(zt))) {
            if (zt == ZoneType.Hand && !gui.mayView(c)) {
                // Hidden card: the client only needs to know it exists.
                final JsonObject hidden = new JsonObject();
                hidden.addProperty("id", c.getId());
                hidden.addProperty("hidden", true);
                arr.add(hidden);
                continue;
            }
            arr.add(card(c));
        }
        return arr;
    }

    JsonObject card(final CardView c) {
        try {
            return cardUnsafe(c);
        } catch (final RuntimeException ex) {
            gui.logOnce("Serializing card " + c.getId(), ex);
            final JsonObject o = new JsonObject();
            o.addProperty("id", c.getId());
            o.addProperty("hidden", true);
            o.addProperty("name", "Card");
            o.addProperty("zone", c.getZone() == null ? "" : c.getZone().name());
            return o;
        }
    }

    private JsonObject cardUnsafe(final CardView c) {
        final JsonObject o = new JsonObject();
        o.addProperty("id", c.getId());
        final boolean visible = gui.mayView(c);
        o.addProperty("hidden", !visible);
        o.addProperty("owner", c.getOwner() == null ? -1 : c.getOwner().getId());
        o.addProperty("controller", c.getController() == null ? -1 : c.getController().getId());
        o.addProperty("zone", c.getZone() == null ? "" : c.getZone().name());
        o.addProperty("tapped", c.isTapped());
        o.addProperty("sick", c.isSick());
        o.addProperty("faceDown", c.isFaceDown());
        o.addProperty("token", c.isToken());
        o.addProperty("commander", c.isCommander());
        o.addProperty("attacking", c.isAttacking());
        o.addProperty("blocking", c.isBlocking());
        o.addProperty("phasedOut", c.isPhasedOut());
        o.addProperty("damage", c.getDamage());
        o.addProperty("doubleFaced", c.isDoubleFacedCard());
        o.add("counters", counters(c.getCounters()));
        final CardView attachedTo = c.getAttachedTo();
        o.addProperty("attachedTo", attachedTo == null ? -1 : attachedTo.getId());
        final GameEntityView attachedEntity = c.getEntityAttachedTo();
        o.addProperty("attachedToPlayer", attachedEntity instanceof PlayerView pv ? pv.getId() : -1);

        if (!visible) {
            o.addProperty("name", c.isFaceDown() ? "Face-down card" : "Hidden card");
            return o;
        }

        final CardStateView st = c.getCurrentState();
        gui.noteSeen(st.getName());
        o.addProperty("name", st.getName());
        o.addProperty("oracleName", c.getOracleName());
        o.addProperty("set", st.getSetCode());
        o.addProperty("imageKey", st.getImageKey());
        final ManaCost cost = st.getManaCost();
        o.addProperty("manaCost", cost == null ? "" : cost.getShortString());
        o.addProperty("typeLine", st.getType() == null ? "" : st.getType().toString());
        o.addProperty("text", c.getText());
        o.addProperty("colors", st.getColors() == null ? "" : st.getColors().toString());
        o.addProperty("isCreature", st.isCreature());
        o.addProperty("isLand", st.isLand());
        o.addProperty("isPlaneswalker", st.isPlaneswalker());
        o.addProperty("isInstant", st.isInstant());
        o.addProperty("isSorcery", st.isSorcery());
        o.addProperty("isArtifact", st.isArtifact());
        o.addProperty("isEnchantment", st.isEnchantment());
        o.addProperty("isBattle", st.isBattle());
        // Keywords the client animates (combat poses, impact colours).
        final JsonArray kw = new JsonArray();
        for (final Keyword k : ANIMATED_KEYWORDS) {
            if (st.hasKeyword(k)) { kw.add(k.name()); }
        }
        o.add("keywords", kw);
        if (st.isCreature() || st.isVehicle()) {
            o.addProperty("power", st.getPower());
            o.addProperty("toughness", st.getToughness());
        }
        if (st.isPlaneswalker()) {
            o.addProperty("loyalty", st.getLoyalty());
        }
        if (st.isBattle()) {
            o.addProperty("defense", st.getDefense());
        }
        if (c.hasAlternateState() && c.getAlternateState() != null) {
            o.addProperty("altName", c.getAlternateState().getName());
        }
        return o;
    }

    private JsonObject stackItem(final StackItemView si) {
        final JsonObject o = new JsonObject();
        o.addProperty("id", si.getId());
        o.addProperty("text", si.getText());
        o.addProperty("isAbility", si.isAbility());
        o.addProperty("isTrigger", si.isTrigger());
        final CardView src = si.getSourceCard();
        o.addProperty("sourceCard", src == null ? -1 : src.getId());
        o.add("source", src == null ? null : card(src));
        o.addProperty("activatingPlayer", si.getActivatingPlayer() == null ? -1 : si.getActivatingPlayer().getId());
        final JsonArray tc = new JsonArray();
        for (final CardView c : snapshot(si.getTargetCards())) { tc.add(c.getId()); }
        o.add("targetCards", tc);
        final JsonArray tp = new JsonArray();
        for (final PlayerView p : snapshot(si.getTargetPlayers())) { tp.add(p.getId()); }
        o.add("targetPlayers", tp);
        return o;
    }

    private JsonArray combat(final CombatView cv) {
        final JsonArray arr = new JsonArray();
        if (cv == null) { return arr; }
        for (final CardView attacker : snapshot(cv.getAttackers())) {
            final JsonObject a = new JsonObject();
            a.addProperty("attacker", attacker.getId());
            final GameEntityView def = cv.getDefender(attacker);
            a.add("defender", entityRef(def));
            final JsonArray blockers = new JsonArray();
            for (final CardView b : snapshot(cv.getBlockers(attacker))) { blockers.add(b.getId()); }
            a.add("blockers", blockers);
            arr.add(a);
        }
        return arr;
    }

    /** What the waiting prompt marks, in a form that doesn't depend on the Forge version. */
    static final class Sel {
        List<CardView> cards = new ArrayList<>();
        int min, max;
        List<Object> targets = new ArrayList<>();
        List<Object> chosen = new ArrayList<>();
        List<CardView> actionable = new ArrayList<>();

        Sel copy() {
            final Sel s = new Sel();
            s.cards = new ArrayList<>(cards); s.min = min; s.max = max;
            s.targets = new ArrayList<>(targets); s.chosen = new ArrayList<>(chosen); s.actionable = new ArrayList<>(actionable);
            return s;
        }
    }

    JsonObject selection(final Sel sel) {
        final JsonObject o = new JsonObject();
        final JsonArray cards = new JsonArray();
        final JsonArray cardData = new JsonArray();
        for (final CardView c : sel.cards) {
            cards.add(c.getId());
            // Full data too, so the client can show selectable cards from zones it doesn't otherwise draw (library...)
            cardData.add(card(c));
        }
        o.add("cards", cards);
        o.add("cardData", cardData);
        o.addProperty("min", sel.min);
        o.addProperty("max", sel.max);
        o.add("targets", refs(sel.targets));
        o.add("chosen", refs(sel.chosen));
        final JsonArray actionable = new JsonArray();
        for (final CardView c : sel.actionable) { actionable.add(c.getId()); }
        o.add("actionable", actionable);
        return o;
    }

    private JsonArray refs(final List<?> objs) {
        final JsonArray arr = new JsonArray();
        for (final Object t : objs) {
            final JsonObject r = entityRef(t);
            if (r != null) { arr.add(r); }
        }
        return arr;
    }

    static JsonObject entityRef(final Object t) {
        if (t == null) { return null; }
        final JsonObject r = new JsonObject();
        if (t instanceof CardView c) {
            r.addProperty("kind", "card");
            r.addProperty("id", c.getId());
        } else if (t instanceof PlayerView p) {
            r.addProperty("kind", "player");
            r.addProperty("id", p.getId());
        } else if (t instanceof StackItemView s) {
            r.addProperty("kind", "stack");
            r.addProperty("id", s.getId());
        } else {
            return null;
        }
        return r;
    }

    private static JsonObject counters(final Multiset<CounterType> counters) {
        final JsonObject o = new JsonObject();
        if (counters == null) { return o; }
        for (final Multiset.Entry<CounterType> e : counters.entrySet()) {
            o.addProperty(e.getElement().getName(), e.getCount());
        }
        return o;
    }

    /** Copies a Forge collection so the game thread can keep changing it while we serialize. */
    static <T> List<T> snapshot(final Iterable<T> items) {
        final List<T> list = new ArrayList<>();
        if (items == null) { return list; }
        for (final T t : items) {
            if (t != null) { list.add(t); }
        }
        return list;
    }
}

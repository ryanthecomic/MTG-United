package mtgunited.bridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import forge.deck.CardPool;
import forge.game.GameEntityView;
import forge.game.GameView;
import forge.game.card.CardFaceView;
import forge.game.card.CardView;
import forge.game.phase.PhaseType;
import forge.game.player.DelayedReveal;
import forge.game.player.PlayerView;
import forge.game.spellability.SpellAbilityView;
import forge.game.spellability.StackItemView;
import forge.gamemodes.match.AbstractGuiGame;
import forge.interfaces.IGameController;
import forge.item.PaperCard;
import forge.localinstance.skin.FSkinProp;
import forge.trackable.TrackableCollection;
import forge.trackable.TrackableTypes;
import forge.game.zone.ZoneType;
import forge.util.FSerializableFunction;
import forge.util.ITriggerEvent;

import java.util.ArrayList;
import java.util.Collections;
import java.util.EnumSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Forge's view of "the screen" for the human player. Forge calls these methods to show prompts, highlight cards and
 * ask questions; we turn each into a JSON message for Unity. Blocking questions (choose a card, pick a number...)
 * become a {@code request} that waits for the client's matching {@code response}.
 */
public final class BridgeGuiGame extends GuiGameCompat {
    private final Channel conn;
    private final StateSerializer serializer = new StateSerializer(this);
    private final ScheduledExecutorService sync = Executors.newSingleThreadScheduledExecutor(r -> {
        final Thread t = new Thread(r, "bridge-sync");
        t.setDaemon(true);
        return t;
    });
    private final AtomicBoolean flushScheduled = new AtomicBoolean(false);

    /** Names of every card the player has legitimately seen this game (theirs, or shown face up). */
    private final Set<String> seenNames = ConcurrentHashMap.newKeySet();

    private final AtomicInteger nextRequestId = new AtomicInteger(1);
    private final Map<Integer, CompletableFuture<JsonObject>> pending = new ConcurrentHashMap<>();
    private final Map<Integer, JsonObject> pendingMessages = new ConcurrentHashMap<>();

    // Latest prompt state, re-sent as a whole whenever any part changes.
    private volatile PlayerView me;
    private volatile String promptMessage = "";
    private volatile int promptCardId = -1;
    private volatile String okLabel = "OK";
    private volatile String cancelLabel = "Cancel";
    private volatile boolean okEnabled = false;
    private volatile boolean cancelEnabled = false;
    private volatile boolean okFocused = false;

    /** Phases at which the human gets to act when the stack is empty. Everything else is auto-passed, like Arena. */
    private final Set<PhaseType> myTurnStops = Collections.synchronizedSet(EnumSet.of(
            PhaseType.MAIN1, PhaseType.COMBAT_DECLARE_ATTACKERS, PhaseType.MAIN2));
    private final Set<PhaseType> opponentTurnStops = Collections.synchronizedSet(EnumSet.of(
            PhaseType.COMBAT_DECLARE_BLOCKERS, PhaseType.END_OF_TURN));

    public BridgeGuiGame(final Channel conn) {
        this.conn = conn;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Outgoing state

    /** Coalesces bursts of Forge updates into one state message every ~40 ms. */
    void markDirty() {
        if (flushScheduled == null || sync == null) { return; }   // called by Forge while still constructing
        if (flushScheduled.compareAndSet(false, true)) {
            sync.schedule(this::flushNow, 40, TimeUnit.MILLISECONDS);
        }
    }

    void flushNow() {
        flushScheduled.set(false);
        if (!conn.isConnected()) { return; }
        final GameView gv = getGameView();
        if (gv == null) { return; }
        RuntimeException last = null;
        for (int attempt = 0; attempt < 3; attempt++) {
            try {
                final JsonObject msg = new JsonObject();
                msg.addProperty("t", "state");
                msg.add("game", serializer.game(gv, me));
                msg.add("prompt", promptJson());
                conn.send(msg);
                return;
            } catch (final RuntimeException ex) {
                // Usually the game thread changed something while we were reading it; try again.
                last = ex;
                try { Thread.sleep(10); } catch (final InterruptedException ie) { Thread.currentThread().interrupt(); return; }
            }
        }
        // Still failing: log it, and at least send the prompt so the player is never left on a stale message.
        logOnce("State snapshot failed", last);
        try {
            final JsonObject p = new JsonObject();
            p.addProperty("t", "prompt");
            p.add("prompt", promptJson());
            conn.send(p);
        } catch (final RuntimeException ex) {
            logOnce("Prompt snapshot failed", ex);
        }
        if (flushScheduled.compareAndSet(false, true)) {
            sync.schedule(this::flushNow, 250, TimeUnit.MILLISECONDS);
        }
    }

    private final Set<String> loggedErrors = ConcurrentHashMap.newKeySet();

    /** Logs an exception with its stack trace the first time a given failure is seen. */
    void logOnce(final String what, final Throwable ex) {
        if (ex == null) { return; }
        final StackTraceElement top = ex.getStackTrace().length > 0 ? ex.getStackTrace()[0] : null;
        if (loggedErrors.add(what + ex.getClass().getName() + top)) {
            Log.error(what + ": " + ex, ex);
        }
    }

    /** Forge's visibility check can fail for a card that is mid-move; fall back to the plain rule instead of throwing. */
    public boolean mayView(final CardView c) {
        try {
            return super.mayView(c);
        } catch (final RuntimeException ex) {
            logOnce("mayView", ex);
            try {
                return c.canBeShownToAny(getLocalPlayers());
            } catch (final RuntimeException ex2) {
                return false;
            }
        }
    }

    private String lastLoggedPrompt = "";

    private JsonObject promptJson() {
        final String summary = promptMessage.replace('\n', ' ') + " [" + (okEnabled ? okLabel : "-") + " / " + (cancelEnabled ? cancelLabel : "-") + "]";
        if (!summary.equals(lastLoggedPrompt)) {
            lastLoggedPrompt = summary;
            Log.info(">> prompt: " + summary);
        }
        final JsonObject p = new JsonObject();
        p.addProperty("message", promptMessage);
        p.addProperty("card", promptCardId);
        final JsonObject ok = new JsonObject();
        ok.addProperty("label", okLabel);
        ok.addProperty("enabled", okEnabled);
        ok.addProperty("focused", okFocused);
        p.add("ok", ok);
        final JsonObject cancel = new JsonObject();
        cancel.addProperty("label", cancelLabel);
        cancel.addProperty("enabled", cancelEnabled);
        p.add("cancel", cancel);
        p.add("selection", serializer.selection(currentSelection()));
        // A cost asked for while something is resolving (Mana Vault's "you may pay {4}", unless costs, ward...)
        // is a choice, not a cast: the client must not auto-pay it.
        p.addProperty("duringResolution", isStackResolving());
        return p;
    }

    private boolean isStackResolving() {
        try {
            final IGameController c = controller();
            if (c instanceof forge.game.player.PlayerController pc && pc.getGame() != null) {
                return pc.getGame().getStack().isResolving();
            }
        } catch (final RuntimeException ex) {
            logOnce("resolving", ex);
        }
        return false;
    }

    private void sendEvent(final String type, final JsonObject body) {
        body.addProperty("t", type);
        conn.send(body);
    }

    /** Called when the client (re)connects: send everything it needs to draw the current screen. */
    void onClientConnected() {
        flushNow();
        for (final JsonObject req : pendingMessages.values()) {
            conn.send(req);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Blocking requests

    private JsonObject request(final String kind, final JsonObject body) {
        final int id = nextRequestId.getAndIncrement();
        body.addProperty("t", "request");
        body.addProperty("id", id);
        body.addProperty("kind", kind);
        final CompletableFuture<JsonObject> future = new CompletableFuture<>();
        pending.put(id, future);
        pendingMessages.put(id, body);
        flushNow(); // make sure the board the question refers to is on screen first
        Log.info(">> request #" + id + " " + kind + ": " + (body.has("title") ? body.get("title").getAsString() : ""));
        conn.send(body);
        try {
            return future.get();
        } catch (final Exception ex) {
            Log.warn("Request " + id + " aborted: " + ex);
            return new JsonObject();
        } finally {
            pending.remove(id);
            pendingMessages.remove(id);
        }
    }

    /** A {@code response} message arrived from the client. Runs on the socket thread, never on the EDT. */
    void onResponse(final JsonObject msg) {
        final int id = msg.get("id").getAsInt();
        Log.info("<< response #" + id);
        final CompletableFuture<JsonObject> f = pending.get(id);
        if (f == null) {
            Log.warn("Response for unknown request " + id);
            return;
        }
        f.complete(msg);
    }

    /** Unblocks every outstanding question with an empty answer (used when conceding or restarting). */
    void abortPendingRequests() {
        for (final CompletableFuture<JsonObject> f : pending.values()) {
            f.complete(new JsonObject());
        }
    }

    private JsonObject option(final Object o, final FSerializableFunction<Object, String> display) {
        final JsonObject j = new JsonObject();
        String label;
        try {
            label = display != null ? display.apply(o) : String.valueOf(o);
        } catch (final RuntimeException ex) {
            label = String.valueOf(o);
        }
        if (o instanceof CardView c) {
            j.add("card", serializer.card(c));
            if (display == null) { label = mayView(c) ? c.getName() : "Hidden card"; }
        } else if (o instanceof PlayerView p) {
            j.addProperty("playerId", p.getId());
            if (display == null) { label = p.getName(); }
        } else if (o instanceof SpellAbilityView sa) {
            if (sa.getHostCard() != null) { j.add("card", serializer.card(sa.getHostCard())); }
            if (display == null) { label = sa.getDescription() == null ? String.valueOf(sa) : sa.getDescription(); }
        } else if (o instanceof StackItemView si) {
            j.addProperty("stackId", si.getId());
            if (si.getSourceCard() != null) { j.add("card", serializer.card(si.getSourceCard())); }
            if (display == null) { label = si.getText(); }
        } else if (o instanceof PaperCard pc) {
            final JsonObject card = new JsonObject();
            card.addProperty("id", -1);
            card.addProperty("name", pc.getName());
            card.addProperty("set", pc.getEdition());
            j.add("card", card);
            if (display == null) { label = pc.getName(); }
        }
        j.addProperty("label", label);
        return j;
    }

    @SuppressWarnings("unchecked")
    private <T> JsonArray options(final List<T> items, final FSerializableFunction<T, String> display) {
        final JsonArray arr = new JsonArray();
        for (final T t : items) {
            arr.add(option(t, (FSerializableFunction<Object, String>) (FSerializableFunction<?, String>) display));
        }
        return arr;
    }

    private static List<Integer> indices(final JsonObject resp) {
        final List<Integer> out = new ArrayList<>();
        final JsonElement e = resp.get("indices");
        if (e != null && e.isJsonArray()) {
            for (final JsonElement i : e.getAsJsonArray()) { out.add(i.getAsInt()); }
        }
        return out;
    }

    void noteSeen(final String name) {
        if (name != null && !name.isEmpty()) { seenNames.add(name); }
    }

    public void resetForNewMatch() {
        super.resetForNewMatch();
        seenNames.clear();
    }

    /**
     * "Name a card" (Demonic Consultation, Pithing Needle...) lists every card face in Magic. Instead of tens of
     * thousands of options, send plain names plus the ones known in this game, and let the client search.
     */
    private <T> List<T> chooseName(final String message, final int min, final List<T> choices) {
        final List<String> names = new ArrayList<>(choices.size());
        final java.util.Map<String, Integer> index = new java.util.HashMap<>();
        for (int i = 0; i < choices.size(); i++) {
            final String n = String.valueOf(choices.get(i));
            names.add(n);
            index.putIfAbsent(n, i);
        }
        // Everything in my own deck counts as known, wherever it is now.
        final PlayerView mine = me;
        if (mine != null) {
            for (final ZoneType z : new ZoneType[] {ZoneType.Library, ZoneType.Hand, ZoneType.Battlefield, ZoneType.Graveyard,
                    ZoneType.Exile, ZoneType.Command}) {
                for (final CardView c : StateSerializer.snapshot(mine.getCards(z))) {
                    if (c.getOwner() != null && c.getOwner().getId() == mine.getId() && c.getCurrentState() != null) {
                        noteSeen(c.getCurrentState().getName());
                    }
                }
            }
        }
        final JsonArray suggested = new JsonArray();
        final List<String> known = new ArrayList<>(seenNames);
        java.util.Collections.sort(known);
        for (final String n : known) {
            if (index.containsKey(n)) { suggested.add(n); }
        }
        final JsonObject body = new JsonObject();
        body.addProperty("title", message);
        body.addProperty("min", min);
        final JsonArray all = new JsonArray();
        names.forEach(all::add);
        body.add("names", all);
        body.add("suggested", suggested);
        final JsonObject resp = request("name", body);
        final List<T> result = new ArrayList<>();
        final JsonElement chosen = resp.get("name");
        if (chosen != null && !chosen.isJsonNull()) {
            final Integer i = index.get(chosen.getAsString());
            if (i != null) { result.add(choices.get(i)); }
        }
        if (result.isEmpty() && min > 0) { result.add(choices.get(0)); }
        return result;
    }

    public <T> List<T> getChoices(final String message, final int min, final int max, final List<T> choices, final List<T> selected, final FSerializableFunction<T, String> display) {
        if (choices == null || choices.isEmpty()) { return new ArrayList<>(); }
        if (min >= 0 && max <= 1 && choices.get(0) instanceof CardFaceView) {
            return chooseName(message, min, choices);
        }
        // Plain reveals of cards ("reveals the top card of their library"): show them without stopping the game.
        if (min < 0 && choices.stream().allMatch(c -> c instanceof CardView)) {
            final JsonObject ev = new JsonObject();
            ev.addProperty("title", message == null ? "" : message);
            final JsonArray cards = new JsonArray();
            for (final T c : choices) {
                try { cards.add(serializer.card((CardView) c)); } catch (final RuntimeException ex) { logOnce("reveal", ex); }
            }
            ev.add("cards", cards);
            sendEvent("revealed", ev);
            return new ArrayList<>();
        }
        final JsonObject body = new JsonObject();
        body.addProperty("title", message);
        body.addProperty("min", min);
        body.addProperty("max", max);
        body.addProperty("reveal", min < 0); // Forge uses min=-1 to just show cards
        body.add("options", options(choices, display));
        final JsonObject resp = request("choose", body);
        final List<T> result = new ArrayList<>();
        if (min < 0) { return result; }
        for (final int i : indices(resp)) {
            if (i >= 0 && i < choices.size()) { result.add(choices.get(i)); }
        }
        // Never hand Forge an answer that breaks its own limits.
        if (result.size() < min) {
            for (final T t : choices) {
                if (result.size() >= min) { break; }
                if (!result.contains(t)) { result.add(t); }
            }
        }
        if (max > 0 && result.size() > max) { return new ArrayList<>(result.subList(0, max)); }
        return result;
    }

    public <T> OrderResult<T> order(final String title, final String top, final int remainingObjectsMin, final int remainingObjectsMax,
                                    final List<T> sourceChoices, final List<T> destChoices, final CardView referenceCard,
                                    final boolean sideboardingMode, final boolean showRememberCheckbox) {
        // Forge's "order" dialog doubles as a pick-several dialog: the player moves items into the result list until
        // between (size - remainingMax) and (size - remainingMin) items are left. We expose it as choose + order.
        final List<T> all = new ArrayList<>(sourceChoices);
        if (destChoices != null) { all.addAll(destChoices); }
        final int n = all.size();
        final int src = sourceChoices.size();
        final int prefilled = destChoices == null ? 0 : destChoices.size();
        final int pickMin = (remainingObjectsMax < 0 ? 0 : Math.max(0, src - remainingObjectsMax)) + prefilled;
        final int pickMax = Math.max(pickMin, (remainingObjectsMin < 0 ? src : Math.max(0, src - remainingObjectsMin)) + prefilled);
        final JsonObject body = new JsonObject();
        body.addProperty("title", title);
        body.addProperty("subtitle", top);
        body.addProperty("min", Math.min(pickMin, n));
        body.addProperty("max", Math.min(pickMax, n));
        body.addProperty("ordered", true);
        body.add("options", options(all, null));
        final JsonObject resp = request("order", body);
        final List<T> result = new ArrayList<>();
        for (final int i : indices(resp)) {
            if (i >= 0 && i < n && !result.contains(all.get(i))) { result.add(all.get(i)); }
        }
        if (result.size() < Math.min(pickMin, n)) {
            for (final T t : all) {
                if (result.size() >= pickMin) { break; }
                if (!result.contains(t)) { result.add(t); }
            }
        }
        return new OrderResult<>(result, false);
    }

    public boolean confirm(final CardView c, final String question, final boolean defaultIsYes, final List<String> options) {
        final JsonObject body = new JsonObject();
        body.addProperty("title", question);
        if (c != null) { body.add("card", serializer.card(c)); }
        final JsonArray opts = new JsonArray();
        final List<String> labels = options == null || options.size() < 2 ? List.of("Yes", "No") : options;
        opts.add(labels.get(0));
        opts.add(labels.get(1));
        body.add("buttons", opts);
        body.addProperty("default", defaultIsYes ? 0 : 1);
        final JsonObject resp = request("confirm", body);
        final JsonElement idx = resp.get("index");
        return idx == null ? defaultIsYes : idx.getAsInt() == 0;
    }

    public boolean showConfirmDialog(final String message, final String title, final String yesButtonText, final String noButtonText, final boolean defaultYes) {
        final JsonObject body = new JsonObject();
        body.addProperty("title", title == null || title.isEmpty() ? message : title + ": " + message);
        final JsonArray opts = new JsonArray();
        opts.add(yesButtonText == null ? "Yes" : yesButtonText);
        opts.add(noButtonText == null ? "No" : noButtonText);
        body.add("buttons", opts);
        body.addProperty("default", defaultYes ? 0 : 1);
        final JsonObject resp = request("confirm", body);
        final JsonElement idx = resp.get("index");
        return idx == null ? defaultYes : idx.getAsInt() == 0;
    }

    public int showOptionDialog(final String message, final String title, final FSkinProp icon, final List<String> options, final int defaultOption) {
        final JsonObject body = new JsonObject();
        body.addProperty("title", title == null || title.isEmpty() ? message : title + ": " + message);
        final JsonArray opts = new JsonArray();
        for (final String s : options) { opts.add(s); }
        body.add("buttons", opts);
        body.addProperty("default", defaultOption);
        final JsonObject resp = request("confirm", body);
        final JsonElement idx = resp.get("index");
        return idx == null ? defaultOption : idx.getAsInt();
    }

    public String showInputDialog(final String message, final String title, final FSkinProp icon, final String initialInput, final List<String> inputOptions, final boolean isNumeric) {
        if (inputOptions != null && !inputOptions.isEmpty()) {
            final String choice = one(title == null ? message : title + ": " + message, inputOptions);
            return choice;
        }
        final JsonObject body = new JsonObject();
        body.addProperty("title", title == null || title.isEmpty() ? message : title + ": " + message);
        body.addProperty("initial", initialInput == null ? "" : initialInput);
        body.addProperty("numeric", isNumeric);
        final JsonObject resp = request("text", body);
        final JsonElement v = resp.get("text");
        return v == null || v.isJsonNull() ? null : v.getAsString();
    }

    public SpellAbilityView getAbilityToPlay(final CardView hostCard, final List<SpellAbilityView> abilities, final ITriggerEvent triggerEvent) {
        if (abilities == null || abilities.isEmpty()) { return null; }
        if (abilities.size() == 1 && !abilities.get(0).promptIfOnlyPossibleAbility()) {
            return abilities.get(0);
        }
        final List<SpellAbilityView> playable = new ArrayList<>();
        for (final SpellAbilityView sa : abilities) {
            if (sa.canPlay()) { playable.add(sa); }
        }
        if (playable.isEmpty()) { return null; }
        final JsonObject body = new JsonObject();
        body.addProperty("title", "Choose an ability" + (hostCard == null ? "" : " of " + hostCard.getName()));
        body.addProperty("min", 0);
        body.addProperty("max", 1);
        final JsonArray opts = new JsonArray();
        for (final SpellAbilityView sa : playable) {
            final JsonObject j = new JsonObject();
            j.addProperty("label", sa.getDescription() == null ? String.valueOf(sa) : sa.getDescription());
            opts.add(j);
        }
        body.add("options", opts);
        final List<Integer> idx = indices(request("choose", body));
        return idx.isEmpty() || idx.get(0) < 0 || idx.get(0) >= playable.size() ? null : playable.get(idx.get(0));
    }

    public Map<CardView, Integer> assignCombatDamage(final CardView attacker, final List<CardView> blockers, final int damage,
                                                     final GameEntityView defender, final boolean overrideOrder, final boolean maySkip) {
        // Arena-style: assign lethal damage to each blocker in order, the rest to the player (trample) or last blocker.
        final Map<CardView, Integer> result = new LinkedHashMap<>();
        if (damage <= 0) { return result; }
        final boolean deathtouch = attacker.getCurrentState().hasDeathtouch();
        // Excess only goes past the blockers with trample (or when Forge explicitly lets us override the order).
        final boolean canHitDefender = defender != null && (attacker.getCurrentState().hasTrample() || overrideOrder);
        int left = damage;
        for (int i = 0; i < blockers.size() && left > 0; i++) {
            final CardView b = blockers.get(i);
            final int lethal = Math.max(deathtouch ? 1 : b.getLethalDamage(), 0);
            final int give = (i == blockers.size() - 1 && !canHitDefender) ? left : Math.min(left, lethal);
            if (give > 0) { result.put(b, give); }
            left -= give;
        }
        if (left > 0) {
            if (canHitDefender) {
                result.put(null, left);
            } else if (!blockers.isEmpty()) {
                result.merge(blockers.get(blockers.size() - 1), left, Integer::sum);
            }
        }
        return result;
    }

    public Map<Object, Integer> assignGenericAmount(final CardView effectSource, final Map<Object, Integer> target, final int amount,
                                                    final boolean atLeastOne, final String amountLabel) {
        final List<Object> keys = new ArrayList<>(target.keySet());
        final JsonObject body = new JsonObject();
        body.addProperty("title", "Distribute " + amount + " " + (amountLabel == null ? "" : amountLabel)
                + (effectSource == null ? "" : " from " + effectSource.getName()));
        body.addProperty("amount", amount);
        body.addProperty("atLeastOne", atLeastOne);
        final JsonArray opts = new JsonArray();
        for (final Object k : keys) {
            final JsonObject o;
            if (k instanceof Byte b) {
                o = new JsonObject();
                o.addProperty("label", forge.card.MagicColor.toLongString(b));
            } else {
                o = option(k, null);
            }
            o.addProperty("max", target.get(k) == null ? amount : target.get(k));
            opts.add(o);
        }
        body.add("options", opts);
        final JsonObject resp = request("distribute", body);
        final Map<Object, Integer> result = new LinkedHashMap<>();
        final JsonElement amounts = resp.get("amounts");
        if (amounts != null && amounts.isJsonArray() && amounts.getAsJsonArray().size() == keys.size()) {
            int total = 0;
            for (int i = 0; i < keys.size(); i++) {
                final int v = amounts.getAsJsonArray().get(i).getAsInt();
                if (v > 0) { result.put(keys.get(i), v); total += v; }
            }
            if (total == amount) { return result; }
            result.clear();
        }
        // Fallback: spread as evenly as possible.
        int left = amount;
        for (int i = 0; i < keys.size() && left > 0; i++) {
            final int share = (int) Math.ceil((double) left / (keys.size() - i));
            result.put(keys.get(i), share);
            left -= share;
        }
        return result;
    }

    public GameEntityView chooseSingleEntityForEffect(final String title, final List<? extends GameEntityView> optionList, final DelayedReveal delayedReveal, final boolean isOptional) {
        if (delayedReveal != null) {
            reveal(delayedReveal.getMessagePrefix(), new ArrayList<>(delayedReveal.getCards()));
        }
        final List<GameEntityView> list = new ArrayList<>(optionList);
        return isOptional ? oneOrNone(title, list) : one(title, list);
    }

    public List<GameEntityView> chooseEntitiesForEffect(final String title, final List<? extends GameEntityView> optionList, final int min, final int max, final DelayedReveal delayedReveal) {
        if (delayedReveal != null) {
            reveal(delayedReveal.getMessagePrefix(), new ArrayList<>(delayedReveal.getCards()));
        }
        return getChoices(title, min, max, new ArrayList<>(optionList), null, null);
    }

    public List<CardView> manipulateCardList(final String title, final Iterable<CardView> cards, final Iterable<CardView> manipulable,
                                             final boolean toTop, final boolean toBottom, final boolean toAnywhere) {
        final List<CardView> list = StateSerializer.snapshot(cards);
        final JsonObject body = new JsonObject();
        body.addProperty("title", title + " (first = top)");
        body.addProperty("min", list.size());
        body.addProperty("max", list.size());
        body.addProperty("ordered", true);
        body.add("options", options(list, null));
        final List<Integer> idx = indices(request("order", body));
        if (idx.size() != list.size()) { return list; }
        final List<CardView> result = new ArrayList<>();
        for (final int i : idx) { result.add(list.get(i)); }
        return result;
    }

    public List<PaperCard> sideboard(final CardPool sideboard, final CardPool main, final String message) {
        return null; // keep the main deck unchanged
    }

    public void message(final String message, final String title) {
        final JsonObject body = new JsonObject();
        body.addProperty("title", title);
        body.addProperty("text", message);
        sendEvent("message", body);
    }

    public void showErrorDialog(final String message, final String title) {
        final JsonObject body = new JsonObject();
        body.addProperty("title", title);
        body.addProperty("text", message);
        body.addProperty("error", true);
        sendEvent("message", body);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Prompt / buttons / highlighting

    public void showPromptMessage(final PlayerView playerView, final String message, final CardView card) {
        promptMessage = message == null ? "" : message;
        promptCardId = card == null ? -1 : card.getId();
        markDirty();
    }

    public void updateButtons(final PlayerView owner, final String label1, final String label2, final boolean enable1, final boolean enable2, final boolean focus1) {
        okLabel = label1 == null ? "" : label1;
        cancelLabel = label2 == null ? "" : label2;
        okEnabled = enable1;
        cancelEnabled = enable2;
        okFocused = focus1;
        markDirty();
    }


    public void flashIncorrectAction() { sendEvent("flash", new JsonObject()); }
    public void alertUser() { sendEvent("alert", new JsonObject()); }

    // ------------------------------------------------------------------------------------------------------------
    // State change notifications: all simply schedule a state push.

    public void setGameView(final GameView gameView0) {
        super.setGameView(gameView0);
        markDirty();
    }

    public void openView(final TrackableCollection<PlayerView> myPlayers) {
        if (myPlayers != null && !myPlayers.isEmpty()) {
            me = myPlayers.iterator().next();
        }
        final JsonObject body = new JsonObject();
        body.addProperty("me", me == null ? -1 : me.getId());
        sendEvent("gameStarted", body);
        markDirty();
    }

    PlayerView getMe() {
        return me;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Everything else Forge tells "the screen" (zones, stack, lives, phase, new calls in newer Forge versions...)
    // arrives here through the generated GuiGameCompat; for a client that redraws from state, a push is enough.

    protected Object onGuiCall(final String method, final Object[] args) {
        if (method.startsWith("update") || method.startsWith("refresh") || method.startsWith("show") || method.startsWith("set")) {
            markDirty();
        } else if (!QUIET.contains(method)) {
            Compat.unknownCall("IGuiGame", method);
        }
        return null;
    }

    private static final Set<String> QUIET = Set.of("getGamestate", "applyMacroState", "clearSelectables", "clearWeaklySelectable");

    /**
     * Selection changes. Newer Forge keeps one Selection object (read in {@link #currentSelection}); older Forge
     * (2.0.15 and before) sends separate calls, tracked here.
     */
    protected void onGuiEvent(final String method, final Object[] args) {
        if (tracked == null) { return; }
        synchronized (tracked) {
            switch (method) {
                case "setSelectables" -> {
                    tracked.cards = Compat.list(args[0]);
                    tracked.min = Compat.integer(args[1]);
                    tracked.max = Compat.integer(args[2]);
                }
                case "clearSelectables" -> { tracked.cards = new ArrayList<>(); tracked.min = 0; tracked.max = 0; tracked.chosen = new ArrayList<>(); }
                case "setHighlighted" -> {
                    final List<Object> es = Compat.list(args[0]);
                    final boolean on = args.length > 1 && Boolean.TRUE.equals(args[1]);
                    final List<Object> chosen = new ArrayList<>(tracked.chosen);
                    if (on) { for (final Object e : es) { if (!chosen.contains(e)) { chosen.add(e); } } } else { chosen.removeAll(es); }
                    tracked.chosen = chosen;
                }
                case "setWeaklySelectable" -> tracked.actionable = Compat.list(args[0]);
                case "clearWeaklySelectable" -> tracked.actionable = new ArrayList<>();
                default -> { }
            }
        }
        markDirty();
    }

    private final StateSerializer.Sel tracked = new StateSerializer.Sel();
    private static volatile java.lang.reflect.Method getSelection;
    private static volatile boolean lookedUp;

    StateSerializer.Sel currentSelection() {
        if (!lookedUp) {
            try { getSelection = AbstractGuiGame.class.getMethod("getSelection"); } catch (final NoSuchMethodException ex) { getSelection = null; }
            lookedUp = true;
        }
        if (getSelection != null) {
            try {
                final Object sel = getSelection.invoke(this);
                final StateSerializer.Sel out = new StateSerializer.Sel();
                out.cards = Compat.list(Compat.get(sel, "cards"));
                out.min = Compat.integer(Compat.get(sel, "min"));
                out.max = Compat.integer(Compat.get(sel, "max"));
                out.targets = Compat.list(Compat.get(sel, "targets"));
                out.chosen = Compat.list(Compat.get(sel, "chosen"));
                out.actionable = Compat.list(Compat.get(sel, "actionable"));
                return out;
            } catch (final Exception ex) {
                Log.warn("Couldn't read Forge's selection: " + ex);
            }
        }
        synchronized (tracked) { return tracked.copy(); }
    }

    /** Forge calls this once the game is decided (GameEventGameFinished). */
    public void finishGame() {
        final GameView gv = getGameView();
        final JsonObject body = new JsonObject();
        body.addProperty("winner", gv == null ? null : gv.getWinningPlayerName());
        flushNow();
        sendEvent("gameOver", body);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Phase stops

    public boolean isUiSetToSkipPhase(final PlayerView playerTurn, final PhaseType phase) {
        if (phase == null) { return false; }
        final boolean myTurn = me != null && playerTurn != null && playerTurn.getId() == me.getId();
        return myTurn ? !myTurnStops.contains(phase) : !opponentTurnStops.contains(phase);
    }

    void setStops(final JsonObject msg) {
        if (msg.has("myTurn")) { replaceStops(myTurnStops, msg.getAsJsonArray("myTurn")); }
        if (msg.has("opponentTurn")) { replaceStops(opponentTurnStops, msg.getAsJsonArray("opponentTurn")); }
    }

    private static void replaceStops(final Set<PhaseType> set, final JsonArray arr) {
        synchronized (set) {
            set.clear();
            for (final JsonElement e : arr) {
                try {
                    set.add(PhaseType.valueOf(e.getAsString()));
                } catch (final IllegalArgumentException ex) {
                    Log.warn("Unknown phase " + e);
                }
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Lookups used by the action dispatcher

    CardView findCard(final int id) {
        final GameView gv = getGameView();
        return gv == null ? null : gv.getTracker().getObj(TrackableTypes.CardViewType, id);
    }

    PlayerView findPlayer(final int id) {
        final GameView gv = getGameView();
        return gv == null ? null : gv.getTracker().getObj(TrackableTypes.PlayerViewType, id);
    }

    StackItemView findStackItem(final int id) {
        final GameView gv = getGameView();
        if (gv == null) { return null; }
        for (final StackItemView si : StateSerializer.snapshot(gv.getStack())) {
            if (si.getId() == id) { return si; }
        }
        return null;
    }

    IGameController controller() {
        return getGameController(me);
    }
}

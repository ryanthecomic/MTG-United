package mtgunited.bridge;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import forge.deck.Deck;
import forge.deck.DeckProxy;
import forge.deck.DeckgenUtil;
import forge.game.Game;
import forge.game.GameEndReason;
import forge.game.GameRules;
import forge.game.GameType;
import forge.game.card.CardView;
import forge.game.player.PlayerView;
import forge.game.player.RegisteredPlayer;
import forge.game.spellability.StackItemView;
import forge.gamemodes.match.HostedMatch;
import forge.gui.GuiBase;
import forge.gui.interfaces.IGuiGame;
import forge.interfaces.IGameController;
import forge.localinstance.properties.ForgePreferences;
import forge.localinstance.properties.ForgePreferences.FPref;
import forge.model.FModel;
import forge.player.GamePlayerUtil;
import forge.player.PlayerControllerHuman;
import forge.util.Aggregates;

import forge.item.PaperCard;

import java.io.File;
import java.util.Collections;
import java.util.ArrayList;
import java.util.EnumSet;
import java.util.List;
import java.util.Map;

/**
 * Entry point. Usage:
 * <pre>java -jar forge-bridge-all.jar --forge-gui "C:\path\to\forge\forge-gui" [--port 7777] [--verbose]</pre>
 * The bridge waits for the Unity client to connect and send a {@code start} message with the player's decklist.
 */
public final class BridgeMain {
    private static ClientConnection conn;
    private static BridgeGuiBase guiBase;
    private static volatile BridgeGuiGame gui;
    private static volatile HostedMatch match;
    /** LAN play with the rules engine: other players connect here while the host's lobby is open. */
    private static final LanServer lan = new LanServer(BridgeMain::onLanMessage, BridgeMain::onLanConnected, BridgeMain::onLanClosed);

    private BridgeMain() {}

    public static void main(final String[] args) throws Exception {
        String forgeGui = System.getenv("FORGE_GUI_DIR");
        int port = 7777;
        for (int i = 0; i < args.length; i++) {
            switch (args[i]) {
                case "--forge-gui" -> forgeGui = args[++i];
                case "--port" -> port = Integer.parseInt(args[++i]);
                case "--verbose" -> Log.verbose = true;
                default -> Log.warn("Unknown argument " + args[i]);
            }
        }
        if (forgeGui == null || !new File(forgeGui, "res").isDirectory()) {
            System.err.println("Point --forge-gui at the forge-gui folder of your Forge checkout (the one that contains 'res').");
            System.err.println("Got: " + forgeGui);
            System.exit(2);
        }
        System.setProperty("java.awt.headless", "true");
        isolateForgeUserData(new File(forgeGui));
        Log.toFile(new File(new File(forgeGui).getAbsoluteFile().getParentFile(), "mtgunited-data" + File.separator + "bridge.log"));

        guiBase = new BridgeGuiBase(new File(forgeGui).getAbsolutePath());
        GuiBase.setInterface(guiBase);

        Log.info("Loading Forge card database (first start can take a minute)...");
        final long t0 = System.currentTimeMillis();
        FModel.initialize(null, prefs -> {
            // Settings that make sense for a remote client; nothing here touches the user's desktop Forge setup
            // beyond what Forge itself would persist.
            prefs.setPref(FPref.UI_ENABLE_AI_CHEATS, false);
            prefs.setPref(FPref.MATCH_AI_TIMEOUT, "8");
            return null;
        });
        Log.info("Forge ready in " + (System.currentTimeMillis() - t0) / 1000 + "s");

        conn = new ClientConnection(port);
        gui = new BridgeGuiGame(conn);
        guiBase.setActiveGame(gui);
        Log.info("Listening on 127.0.0.1:" + conn.getPort() + " - start the Unity client now.");
        conn.acceptAndServe(BridgeMain::onMessage, BridgeMain::onConnected, () -> { });
    }

    /**
     * Keeps this bridge's Forge settings, decks and caches out of a desktop Forge install on the same PC, by writing
     * Forge's own profile file (forge-gui/forge.profile.properties) on first start. Must run before any Forge class
     * reads ForgeConstants.
     */
    private static void isolateForgeUserData(final File forgeGuiDir) {
        final File profile = new File(forgeGuiDir, "forge.profile.properties");
        if (profile.exists()) { return; }
        final File data = new File(forgeGuiDir.getAbsoluteFile().getParentFile(), "mtgunited-data");
        final String user = new File(data, "user").getAbsolutePath() + File.separator;
        final String cache = new File(data, "cache").getAbsolutePath() + File.separator;
        final String text = "userDir=" + user.replace("\\", "\\\\") + "\n"
                + "cacheDir=" + cache.replace("\\", "\\\\") + "\n";
        // Forge reads this with java.util.Properties (ISO-8859-1), so escape anything non-ASCII,
        // e.g. accented letters in the Windows user name.
        final StringBuilder escaped = new StringBuilder();
        for (final char ch : text.toCharArray()) {
            if (ch < 128) { escaped.append(ch); } else { escaped.append(String.format("\\u%04x", (int) ch)); }
        }
        try {
            java.nio.file.Files.writeString(profile.toPath(), escaped.toString(), java.nio.charset.StandardCharsets.US_ASCII);
            Log.info("Forge data for the bridge lives in " + data);
        } catch (final java.io.IOException ex) {
            Log.warn("Could not write " + profile + ": " + ex.getMessage());
        }
    }

    private static void onConnected() {
        final JsonObject hello = new JsonObject();
        hello.addProperty("t", "hello");
        hello.addProperty("protocol", 1);
        hello.addProperty("inGame", match != null && match.getGame() != null && !match.getGame().isGameOver());
        conn.send(hello);
        gui.onClientConnected();
    }

    private static final java.util.concurrent.ExecutorService CATALOG = java.util.concurrent.Executors.newSingleThreadExecutor(r -> {
        final Thread th = new Thread(r, "bridge-catalog");
        th.setDaemon(true);
        return th;
    });

    private static void onMessage(final JsonObject msg) {
        final String t = msg.has("t") ? msg.get("t").getAsString() : "";
        Log.debug("<< " + msg);
        switch (t) {
            case "ping" -> {
                final JsonObject pong = new JsonObject();
                pong.addProperty("t", "pong");
                conn.send(pong);
            }
            case "response" -> gui.onResponse(msg);
            case "start" -> {
                final Thread th = new Thread(() -> startGame(msg), "bridge-start");
                th.setDaemon(true);
                th.start();
            }
            case "stops" -> gui.setStops(msg);
            case "lanOpen" -> setLanOpen(msg.has("open") && msg.get("open").getAsBoolean());
            case "lanKick" -> {
                final int id = msg.has("id") ? msg.get("id").getAsInt() : -1;
                for (final LanServer.Peer p : lan.peers()) {
                    if (p.id == id) { p.send(bye("The host removed you from the table.")); p.close(); }
                }
            }
            // Deck builder queries: answered off the game threads so searching never stalls a match.
            case "cardSearch" -> CATALOG.execute(() -> conn.send(CardCatalog.search(
                    msg.has("q") ? msg.get("q").getAsString() : "", msg.has("limit") ? msg.get("limit").getAsInt() : 60)));
            case "cardInfo" -> CATALOG.execute(() -> conn.send(CardCatalog.info(
                    msg.has("names") ? msg.getAsJsonArray("names") : null, msg.has("req") ? msg.get("req").getAsString() : null)));
            default -> guiBase.invokeInEdtLater(() -> handleAction(gui, t, msg));
        }
    }

    // ================================================================================================ LAN play

    private static void setLanOpen(final boolean open) {
        try {
            if (open) { lan.open(); } else { lan.close(); }
        } catch (final java.io.IOException ex) {
            Log.warn("Could not open LAN play: " + ex.getMessage());
            sendInfo("Couldn't open LAN play on port " + LanServer.PORT + ": " + ex.getMessage());
        }
        sendLanLobby();
    }

    private static JsonObject bye(final String text) {
        final JsonObject m = new JsonObject();
        m.addProperty("t", "bye");
        m.addProperty("text", text);
        return m;
    }

    /** Who is at the table: sent to the host and to every LAN player after any change. */
    private static void sendLanLobby() {
        final JsonObject m = new JsonObject();
        m.addProperty("t", "lanLobby");
        m.addProperty("open", lan.isOpen());
        m.addProperty("port", LanServer.PORT);
        final JsonArray arr = new JsonArray();
        for (final LanServer.Peer p : lan.peers()) {
            if (p.join == null) { continue; }
            final JsonObject o = new JsonObject();
            o.addProperty("id", p.id);
            o.addProperty("name", p.name);
            o.addProperty("deck", p.join.has("deckName") ? p.join.get("deckName").getAsString() : "");
            arr.add(o);
        }
        m.add("players", arr);
        conn.send(m);
        for (final LanServer.Peer p : lan.peers()) { if (p.join != null) { p.send(m); } }
    }

    private static void onLanConnected(final LanServer.Peer peer) {
        final JsonObject hello = new JsonObject();
        hello.addProperty("t", "hello");
        hello.addProperty("protocol", 1);
        hello.addProperty("remote", true);
        hello.addProperty("inGame", false);
        peer.send(hello);
    }

    private static void onLanClosed(final LanServer.Peer peer) {
        // A player who leaves mid-game concedes (the game carries on for everyone else).
        final BridgeGuiGame g = peer.gui;
        if (g != null && match != null && match.getGame() != null && !match.getGame().isGameOver()) {
            guiBase.invokeInEdtLater(() -> {
                final IGameController c = g.controller();
                if (c != null) { c.concede(); }
                g.abortPendingRequests();
            });
            sendInfo(peer.name + " left the game.");
        } else if (peer.join != null) {
            sendInfo(peer.name + " left the table.");
        }
        sendLanLobby();
    }

    private static void onLanMessage(final LanServer.Peer peer, final JsonObject msg) {
        final String t = msg.has("t") ? msg.get("t").getAsString() : "";
        switch (t) {
            case "ping" -> {
                final JsonObject pong = new JsonObject();
                pong.addProperty("t", "pong");
                peer.send(pong);
            }
            case "join" -> {
                if (match != null && match.getGame() != null && !match.getGame().isGameOver()) {
                    peer.send(bye("A game is already running at that table."));
                    peer.close();
                    return;
                }
                peer.name = msg.has("name") ? msg.get("name").getAsString() : "Player " + peer.id;
                peer.join = msg;
                if (peer.gui == null) { peer.gui = new BridgeGuiGame(peer); }
                sendInfo(peer.name + " joined the table.");
                sendLanLobby();
            }
            case "response" -> { if (peer.gui != null) { peer.gui.onResponse(msg); } }
            case "stops" -> { if (peer.gui != null) { peer.gui.setStops(msg); } }
            case "cardSearch" -> CATALOG.execute(() -> peer.send(CardCatalog.search(
                    msg.has("q") ? msg.get("q").getAsString() : "", msg.has("limit") ? msg.get("limit").getAsInt() : 60)));
            case "cardInfo" -> CATALOG.execute(() -> peer.send(CardCatalog.info(
                    msg.has("names") ? msg.getAsJsonArray("names") : null, msg.has("req") ? msg.get("req").getAsString() : null)));
            case "start", "lanOpen", "lanKick" -> { /* only the host decides */ }
            default -> {
                final BridgeGuiGame g = peer.gui;
                if (g != null) { guiBase.invokeInEdtLater(() -> handleAction(g, t, msg)); }
            }
        }
    }

    /** Player actions. Always run on the bridge EDT, as Forge expects. */
    private static void handleAction(final BridgeGuiGame gui, final String t, final JsonObject msg) {
        Log.info("<< " + t + (msg.has("id") ? " " + msg.get("id") : "") + (msg.has("color") ? " " + msg.get("color") : ""));
        final IGameController c = gui.controller();
        if (c == null) {
            Log.warn("Action '" + t + "' ignored: no game in progress");
            return;
        }
        switch (t) {
            case "ok" -> c.selectButtonOk();
            case "cancel" -> c.selectButtonCancel();
            case "card" -> {
                final CardView cv = gui.findCard(msg.get("id").getAsInt());
                if (cv == null) { Log.warn("Unknown card " + msg.get("id")); return; }
                if (!c.selectCard(cv, null, null)) {
                    gui.flashIncorrectAction();
                }
            }
            case "player" -> {
                final PlayerView pv = gui.findPlayer(msg.get("id").getAsInt());
                if (pv != null) { c.selectPlayer(pv, null); }
            }
            case "stack" -> {
                final StackItemView si = gui.findStackItem(msg.get("id").getAsInt());
                if (si != null) { Compat.call(c, "selectStackItem", si); }   // newer Forge only
            }
            case "mana" -> c.useMana(msg.get("color").getAsByte());
            case "undo" -> c.undoLastAction();
            case "alphaStrike" -> c.alphaStrike();
            case "passTurn" -> {
                if (c instanceof PlayerControllerHuman pch) { pch.autoPassUntilEndOfTurn(); }
                c.selectButtonOk();
            }
            case "concede" -> concede(gui);
            default -> Log.warn("Unknown message type '" + t + "'");
        }
    }

    private static void concede() { concede(gui); }

    /** A player concedes. Alone against AIs that ends the game; with other people at the table they play on. */
    private static void concede(final BridgeGuiGame who) {
        final HostedMatch m = match;
        if (m == null || m.getGame() == null) { return; }
        final Game game = m.getGame();
        if (game.isGameOver()) { return; }
        final IGameController c = who.controller();
        if (c != null) { c.concede(); }
        who.abortPendingRequests();
        if (humansInGame > 1) { return; }
        game.getAction().invoke(() -> {
            if (!game.isGameOver()) { game.setGameOver(GameEndReason.AllHumansLost); }
        });
    }

    private static volatile int humansInGame = 1;

    private static synchronized void startGame(final JsonObject msg) {
        try {
            if (match != null && match.getGame() != null && !match.getGame().isGameOver()) {
                Log.info("Conceding the running game before starting a new one");
                guiBase.invokeInEdtAndWait(BridgeMain::concede);
                Thread.sleep(500);
            }

            final String deckText = msg.has("deck") ? msg.get("deck").getAsString() : "";
            final String playerName = msg.has("name") ? msg.get("name").getAsString() : "Player";
            final int opponents = msg.has("opponents") ? Math.max(1, Math.min(5, msg.get("opponents").getAsInt())) : 3;
            final String aiDeckMode = msg.has("aiDecks") ? msg.get("aiDecks").getAsString() : "precon";
            // fast: plain heuristic AI. normal: adds simulation for spell choices. strong: Forge's maximum
            // (simulates mulligans, attacks and plays; slow with several opponents).
            final String aiStrength = msg.has("aiStrength") ? msg.get("aiStrength").getAsString() : "normal";
            final List<String> commanders = new ArrayList<>();
            if (msg.has("commanders") && msg.get("commanders").isJsonArray()) {
                for (final JsonElement e : msg.getAsJsonArray("commanders")) {
                    if (!e.getAsString().isBlank()) { commanders.add(e.getAsString()); }
                }
            }

            // Format: commander (40 life, command zone), constructed (60 cards, 20 life) or limited (40 cards, 20 life).
            final String format = msg.has("format") ? msg.get("format").getAsString() : "commander";
            final boolean commanderGame = !"constructed".equals(format) && !"limited".equals(format);

            final Deck myDeck;
            if (deckText.isBlank()) {
                myDeck = aiDeck(aiDeckMode, format);
                sendInfo("No decklist sent; you are playing " + myDeck.getName());
            } else {
                final DeckLoader.Result r = DeckLoader.load(deckText, msg.has("deckName") ? msg.get("deckName").getAsString() : null,
                        commanderGame ? commanders : Collections.emptyList(), commanderGame ? "commander" : format);
                if (!r.errors.isEmpty()) {
                    conn.send(deckError(r.errors));
                    return;
                }
                for (final String w : r.warnings) { sendInfo("Deck warning: " + w); }
                myDeck = r.deck;
            }

            final ForgePreferences prefs = FModel.getPreferences();
            prefs.setPref(FPref.PLAYER_NAME, playerName);
            Compat.setPref(prefs, "MATCH_AI_FLAT_MONTE_CARLO", !"fast".equals(aiStrength));

            final List<RegisteredPlayer> players = new ArrayList<>();
            final Map<RegisteredPlayer, IGuiGame> guis = new java.util.HashMap<>();
            final RegisteredPlayer human = registered(myDeck, commanderGame);
            human.setPlayer(GamePlayerUtil.getGuiPlayer(playerName, 0, 0, false));
            players.add(human);
            guis.put(human, gui);
            final JsonArray names = new JsonArray();

            // LAN players at the host's table (each on their own PC, with their own view of the game).
            final List<BridgeGuiGame> peerGuis = new ArrayList<>();
            if (msg.has("lan") && msg.get("lan").getAsBoolean()) {
                int seat = 1;
                for (final LanServer.Peer peer : lan.peers()) {
                    if (peer.join == null || !peer.isConnected()) { continue; }
                    final JsonObject j = peer.join;
                    final List<String> theirCmds = new ArrayList<>();
                    if (commanderGame && j.has("commanders") && j.get("commanders").isJsonArray()) {
                        for (final JsonElement e : j.getAsJsonArray("commanders")) { if (!e.getAsString().isBlank()) { theirCmds.add(e.getAsString()); } }
                    }
                    final String theirText = j.has("deck") ? j.get("deck").getAsString() : "";
                    final Deck theirDeck;
                    if (theirText.isBlank()) {
                        theirDeck = aiDeck(aiDeckMode, format);
                    } else {
                        final DeckLoader.Result r = DeckLoader.load(theirText, j.has("deckName") ? j.get("deckName").getAsString() : null,
                                theirCmds, commanderGame ? "commander" : format);
                        if (!r.errors.isEmpty()) {
                            peer.send(deckError(r.errors));
                            sendInfo(peer.name + "'s deck can't be played (" + r.errors.get(0) + "); they sit this one out.");
                            continue;
                        }
                        theirDeck = r.deck;
                    }
                    final RegisteredPlayer rp = registered(theirDeck, commanderGame);
                    rp.setPlayer(GamePlayerUtil.getGuiPlayer(peer.name, seat, seat, false));
                    seat++;
                    if (peer.gui == null) { peer.gui = new BridgeGuiGame(peer); }
                    players.add(rp);
                    guis.put(rp, peer.gui);
                    peerGuis.add(peer.gui);
                    names.add(peer.name);
                }
            }

            // AI opponents fill the remaining seats (none needed if people are playing each other).
            final int aiCount = peerGuis.isEmpty() ? Math.max(1, opponents) : Math.max(0, Math.min(5 - peerGuis.size(), msg.has("opponents") ? msg.get("opponents").getAsInt() : 0));
            for (int i = 0; i < aiCount; i++) {
                final Deck d = aiDeck(aiDeckMode, format);
                final String aiName = "AI " + (i + 1) + " (" + d.getName() + ")";
                final RegisteredPlayer rp = registered(d, commanderGame);
                rp.setPlayer(GamePlayerUtil.createAiPlayer(aiName, players.size()));
                players.add(rp);
                names.add(aiName);
            }

            final GameType type = commanderGame ? GameType.Commander : GameType.Constructed;
            final EnumSet<GameType> variants = commanderGame ? EnumSet.of(GameType.Commander) : EnumSet.noneOf(GameType.class);
            final GameRules rules = new GameRules(type);
            rules.setAppliedVariants(variants);
            rules.setGamesPerMatch(1);

            gui.abortPendingRequests();
            gui.resetForNewMatch();
            for (final BridgeGuiGame g : peerGuis) { g.abortPendingRequests(); g.resetForNewMatch(); }
            humansInGame = 1 + peerGuis.size();
            final HostedMatch hm = new HostedMatch();
            match = hm;
            Log.info("Starting " + format + " game: " + playerName + " (" + myDeck.getName() + ") vs " + names);
            hm.startMatch(rules, variants, players, guis, null);
            // HostedMatch always turns on maximum AI strength on desktop. The game thread is still dealing and
            // shuffling at this point, so this takes effect before the AI's first decision (its mulligan).
            final Game started = hm.getGame();
            if (started != null) {
                Compat.setField(started, "AI_FLAT_MONTE_CARLO", !"fast".equals(aiStrength));
                Compat.setField(started, "AI_FLAT_MONTE_CARLO_MAX", "strong".equals(aiStrength));
            }
            Log.info("AI strength: " + aiStrength);
        } catch (final Exception ex) {
            Log.error("Could not start game", ex);
            final JsonObject err = new JsonObject();
            err.addProperty("t", "deckError");
            final JsonArray arr = new JsonArray();
            arr.add("Could not start the game: " + ex);
            err.add("errors", arr);
            conn.send(err);
        }
    }

    private static RegisteredPlayer registered(final Deck deck, final boolean commanderGame) {
        return commanderGame ? RegisteredPlayer.forCommander(deck) : new RegisteredPlayer(deck);
    }

    private static JsonObject deckError(final List<String> errors) {
        final JsonObject err = new JsonObject();
        err.addProperty("t", "deckError");
        final JsonArray arr = new JsonArray();
        errors.forEach(arr::add);
        err.add("errors", arr);
        return err;
    }

    /** An AI deck for the format: precons (Commander / Forge's theme and preconstructed decks) or random. */
    private static Deck aiDeck(final String mode, final String format) {
        if ("constructed".equals(format) || "limited".equals(format)) {
            Deck d = null;
            if (!"random".equalsIgnoreCase(mode)) {
                try { d = DeckgenUtil.getRandomPreconDeck(); } catch (final RuntimeException ex) { d = null; }
                if (d == null || d.getMain().countAll() < 40) {
                    try { d = DeckgenUtil.getRandomThemeDeck(); } catch (final RuntimeException ex) { d = null; }
                }
            }
            if (d == null || d.getMain().countAll() < 40) { d = DeckgenUtil.getRandomColorDeck(true); }
            return "limited".equals(format) ? toForty(d) : d;
        }
        return aiDeck(mode);
    }

    /** Cuts a deck down to a 40-card limited deck: 17 lands, 23 spells, keeping its proportions. */
    private static Deck toForty(final Deck full) {
        final List<PaperCard> lands = new ArrayList<>(), spells = new ArrayList<>();
        for (final PaperCard c : full.getMain().toFlatList()) {
            if (c.getRules().getType().isLand()) { lands.add(c); } else { spells.add(c); }
        }
        if (lands.size() + spells.size() <= 40) { return full; }
        Collections.shuffle(lands);
        Collections.shuffle(spells);
        final Deck d = new Deck(full.getName() + " (40)");
        final int landCount = Math.min(17, lands.size());
        final int spellCount = Math.min(40 - landCount, spells.size());
        for (int i = 0; i < landCount; i++) { d.getMain().add(lands.get(i)); }
        for (int i = 0; i < spellCount; i++) { d.getMain().add(spells.get(i)); }
        return d;
    }

    private static Deck aiDeck(final String mode) {
        if ("random".equalsIgnoreCase(mode)) {
            final Deck d = DeckgenUtil.generateCommanderDeck(true, GameType.Commander);
            if (d != null) { return d; }
        }
        final Iterable<DeckProxy> precons = DeckProxy.getAllCommanderPreconDecks();
        final DeckProxy pick = Aggregates.random(precons);
        if (pick != null) { return pick.getDeck(); }
        return DeckgenUtil.generateCommanderDeck(true, GameType.Commander);
    }

    private static void sendInfo(final String text) {
        final JsonObject m = new JsonObject();
        m.addProperty("t", "message");
        m.addProperty("title", "Info");
        m.addProperty("text", text);
        conn.send(m);
    }
}

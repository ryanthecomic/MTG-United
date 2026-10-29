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

import java.io.File;
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
            default -> guiBase.invokeInEdtLater(() -> handleAction(t, msg));
        }
    }

    /** Player actions. Always run on the bridge EDT, as Forge expects. */
    private static void handleAction(final String t, final JsonObject msg) {
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
                if (si != null) { c.selectStackItem(si); }
            }
            case "mana" -> c.useMana(msg.get("color").getAsByte());
            case "undo" -> c.undoLastAction();
            case "alphaStrike" -> c.alphaStrike();
            case "passTurn" -> {
                if (c instanceof PlayerControllerHuman pch) { pch.autoPassUntilEndOfTurn(); }
                c.selectButtonOk();
            }
            case "concede" -> concede();
            default -> Log.warn("Unknown message type '" + t + "'");
        }
    }

    private static void concede() {
        final HostedMatch m = match;
        if (m == null || m.getGame() == null) { return; }
        final Game game = m.getGame();
        if (game.isGameOver()) { return; }
        final IGameController c = gui.controller();
        if (c != null) { c.concede(); }
        gui.abortPendingRequests();
        game.getAction().invoke(() -> {
            if (!game.isGameOver()) { game.setGameOver(GameEndReason.AllHumansLost); }
        });
    }

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

            final Deck myDeck;
            if (deckText.isBlank()) {
                myDeck = aiDeck("precon");
                sendInfo("No decklist sent; you are playing a random precon: " + myDeck.getName());
            } else {
                final DeckLoader.Result r = DeckLoader.load(deckText, msg.has("deckName") ? msg.get("deckName").getAsString() : null, commanders);
                if (!r.errors.isEmpty()) {
                    final JsonObject err = new JsonObject();
                    err.addProperty("t", "deckError");
                    final JsonArray arr = new JsonArray();
                    r.errors.forEach(arr::add);
                    err.add("errors", arr);
                    conn.send(err);
                    return;
                }
                for (final String w : r.warnings) { sendInfo("Deck warning: " + w); }
                myDeck = r.deck;
            }

            final ForgePreferences prefs = FModel.getPreferences();
            prefs.setPref(FPref.PLAYER_NAME, playerName);
            prefs.setPref(FPref.MATCH_AI_FLAT_MONTE_CARLO, !"fast".equals(aiStrength));

            final List<RegisteredPlayer> players = new ArrayList<>();
            final RegisteredPlayer human = RegisteredPlayer.forCommander(myDeck);
            human.setPlayer(GamePlayerUtil.getGuiPlayer(playerName, 0, 0, false));
            players.add(human);
            final JsonArray aiNames = new JsonArray();
            for (int i = 0; i < opponents; i++) {
                final Deck d = aiDeck(aiDeckMode);
                final String aiName = "AI " + (i + 1) + " (" + d.getName() + ")";
                final RegisteredPlayer rp = RegisteredPlayer.forCommander(d);
                rp.setPlayer(GamePlayerUtil.createAiPlayer(aiName, i + 1));
                players.add(rp);
                aiNames.add(aiName);
            }

            final GameRules rules = new GameRules(GameType.Commander);
            rules.setAppliedVariants(EnumSet.of(GameType.Commander));
            rules.setGamesPerMatch(1);

            gui.abortPendingRequests();
            gui.resetForNewMatch();
            final HostedMatch hm = new HostedMatch();
            match = hm;
            Log.info("Starting Commander game: " + playerName + " (" + myDeck.getName() + ") vs " + aiNames);
            hm.startMatch(rules, EnumSet.of(GameType.Commander), players, Map.<RegisteredPlayer, IGuiGame>of(human, gui), null);
            // HostedMatch always turns on maximum AI strength on desktop. The game thread is still dealing and
            // shuffling at this point, so this takes effect before the AI's first decision (its mulligan).
            final Game started = hm.getGame();
            if (started != null) {
                started.AI_FLAT_MONTE_CARLO = !"fast".equals(aiStrength);
                started.AI_FLAT_MONTE_CARLO_MAX = "strong".equals(aiStrength);
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

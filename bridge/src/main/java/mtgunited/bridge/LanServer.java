package mtgunited.bridge;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.BufferedReader;
import java.io.BufferedWriter;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStreamWriter;
import java.net.InetAddress;
import java.net.ServerSocket;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.function.BiConsumer;
import java.util.function.Consumer;

/**
 * LAN play with the rules engine: other MTG United players on the network connect here (they don't need Forge).
 * Only open while the host has a LAN lobby up. Each connection is a {@link Peer} with its own game view.
 */
public final class LanServer {
    public static final int PORT = 7792;

    /** One remote player. */
    public static final class Peer implements Channel {
        private static int nextId = 1;
        public final int id;
        private final Socket socket;
        private final BufferedWriter out;
        private final Object writeLock = new Object();
        private volatile boolean open = true;
        public volatile String name = "Player";
        public volatile JsonObject join;           // the join message: name, deck, deckName, commanders
        public volatile BridgeGuiGame gui;

        Peer(final Socket s) throws IOException {
            synchronized (Peer.class) { id = nextId++; }
            socket = s;
            out = new BufferedWriter(new OutputStreamWriter(s.getOutputStream(), StandardCharsets.UTF_8));
        }

        @Override
        public void send(final JsonObject msg) {
            final String line = ClientConnection.GSON.toJson(msg);
            synchronized (writeLock) {
                if (!open) { return; }
                try {
                    out.write(line);
                    out.write('\n');
                    out.flush();
                } catch (final IOException ex) {
                    Log.warn("Send to " + name + " failed: " + ex.getMessage());
                }
            }
        }

        @Override
        public boolean isConnected() { return open; }

        public String address() { return String.valueOf(socket.getRemoteSocketAddress()); }

        void close() {
            open = false;
            try { socket.close(); } catch (final IOException ignored) { }
        }
    }

    private ServerSocket server;
    private final List<Peer> peers = new CopyOnWriteArrayList<>();
    private final BiConsumer<Peer, JsonObject> onMessage;
    private final Consumer<Peer> onConnected, onClosed;

    public LanServer(final BiConsumer<Peer, JsonObject> onMessage, final Consumer<Peer> onConnected, final Consumer<Peer> onClosed) {
        this.onMessage = onMessage;
        this.onConnected = onConnected;
        this.onClosed = onClosed;
    }

    public synchronized boolean isOpen() { return server != null && !server.isClosed(); }

    public List<Peer> peers() { return peers; }

    /** Starts accepting LAN players (all network interfaces). */
    public synchronized void open() throws IOException {
        if (isOpen()) { return; }
        server = new ServerSocket(PORT, 8, (InetAddress) null);
        final ServerSocket s = server;
        final Thread t = new Thread(() -> acceptLoop(s), "bridge-lan-accept");
        t.setDaemon(true);
        t.start();
        Log.info("LAN play open on port " + PORT);
    }

    /** Stops accepting and disconnects everyone. */
    public synchronized void close() {
        if (server != null) {
            try { server.close(); } catch (final IOException ignored) { }
            server = null;
        }
        for (final Peer p : peers) { p.close(); }
        peers.clear();
        Log.info("LAN play closed");
    }

    private void acceptLoop(final ServerSocket s) {
        while (!s.isClosed()) {
            final Socket socket;
            try { socket = s.accept(); } catch (final IOException ex) { break; }
            try {
                socket.setTcpNoDelay(true);
                final Peer peer = new Peer(socket);
                peers.add(peer);
                Log.info("LAN player connected from " + peer.address());
                final Thread t = new Thread(() -> readLoop(peer, socket), "bridge-lan-" + peer.id);
                t.setDaemon(true);
                t.start();
            } catch (final IOException ex) {
                Log.warn("LAN connection failed: " + ex.getMessage());
            }
        }
    }

    private void readLoop(final Peer peer, final Socket socket) {
        try {
            onConnected.accept(peer);
            try (BufferedReader in = new BufferedReader(new InputStreamReader(socket.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = in.readLine()) != null) {
                    if (line.isBlank()) { continue; }
                    final JsonObject msg;
                    try { msg = JsonParser.parseString(line).getAsJsonObject(); }
                    catch (final RuntimeException ex) { continue; }
                    try { onMessage.accept(peer, msg); }
                    catch (final RuntimeException ex) { Log.error("Error handling LAN message " + line, ex); }
                }
            }
        } catch (final IOException ex) {
            Log.info("LAN player " + peer.name + " disconnected: " + ex.getMessage());
        } finally {
            peer.close();
            peers.remove(peer);
            onClosed.accept(peer);
        }
    }
}

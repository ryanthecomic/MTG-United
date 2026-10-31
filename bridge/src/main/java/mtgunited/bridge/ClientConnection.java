package mtgunited.bridge;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
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
import java.util.function.Consumer;

/**
 * One Unity client connected over localhost TCP. Messages are single-line JSON objects terminated by '\n'
 * in both directions. See PROTOCOL.md in the repository root for the message catalogue.
 */
public final class ClientConnection implements Channel {
    public static final Gson GSON = new GsonBuilder().disableHtmlEscaping().serializeNulls().create();

    private final ServerSocket server;
    private volatile Socket socket;
    private volatile BufferedWriter out;
    private final Object writeLock = new Object();

    public ClientConnection(final int port) throws IOException {
        server = new ServerSocket(port, 1, InetAddress.getLoopbackAddress());
    }

    public int getPort() {
        return server.getLocalPort();
    }

    /** Blocks until a client connects, then reads messages on the calling thread until the client disconnects. */
    public void acceptAndServe(final Consumer<JsonObject> onMessage, final Runnable onConnected, final Runnable onDisconnected) throws IOException {
        while (true) {
            final Socket s = server.accept();
            s.setTcpNoDelay(true);
            synchronized (writeLock) {
                socket = s;
                out = new BufferedWriter(new OutputStreamWriter(s.getOutputStream(), StandardCharsets.UTF_8));
            }
            Log.info("Client connected from " + s.getRemoteSocketAddress());
            onConnected.run();
            try (BufferedReader in = new BufferedReader(new InputStreamReader(s.getInputStream(), StandardCharsets.UTF_8))) {
                String line;
                while ((line = in.readLine()) != null) {
                    if (line.isBlank()) { continue; }
                    final JsonObject msg;
                    try {
                        msg = JsonParser.parseString(line).getAsJsonObject();
                    } catch (final RuntimeException ex) {
                        Log.warn("Ignoring malformed message: " + line);
                        continue;
                    }
                    try {
                        onMessage.accept(msg);
                    } catch (final RuntimeException ex) {
                        Log.error("Error handling message " + line, ex);
                    }
                }
            } catch (final IOException ex) {
                Log.warn("Connection lost: " + ex.getMessage());
            } finally {
                synchronized (writeLock) {
                    out = null;
                    socket = null;
                }
                Log.info("Client disconnected");
                onDisconnected.run();
            }
        }
    }

    @Override
    public boolean isConnected() {
        return out != null;
    }

    @Override
    public void send(final JsonObject msg) {
        final String line = GSON.toJson(msg);
        synchronized (writeLock) {
            if (out == null) { return; }
            try {
                out.write(line);
                out.write('\n');
                out.flush();
            } catch (final IOException ex) {
                Log.warn("Send failed: " + ex.getMessage());
            }
        }
    }
}

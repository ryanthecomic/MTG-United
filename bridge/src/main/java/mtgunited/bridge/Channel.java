package mtgunited.bridge;

import com.google.gson.JsonObject;

/** Where a player's game messages go: the local Unity client, or a remote one on the LAN. */
public interface Channel {
    void send(JsonObject msg);

    boolean isConnected();
}

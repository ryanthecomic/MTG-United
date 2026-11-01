package mtgunited.bridge;

import forge.gamemodes.match.HostedMatch;
import forge.gui.interfaces.IGuiGame;
import forge.item.PaperCard;
import forge.localinstance.skin.FSkinProp;
import forge.util.FSerializableFunction;

import java.io.File;
import java.util.ArrayList;
import java.util.Collection;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.function.Consumer;

/**
 * Forge's platform interface for a headless host. Forge expects a "GUI thread" (the EDT on desktop); here that is a
 * single dedicated thread, and every player action from the Unity client is run on it, just as desktop Forge runs
 * clicks on the Swing EDT.
 */
@SuppressWarnings("unchecked")
public final class BridgeGuiBase extends GuiBaseCompat {
    private final String assetsDir;
    private volatile Thread edtThread;
    private final ExecutorService edt = Executors.newSingleThreadExecutor(r -> {
        final Thread t = new Thread(r, "bridge-edt");
        t.setDaemon(true);
        edtThread = t;
        return t;
    });
    private volatile BridgeGuiGame activeGame;

    public BridgeGuiBase(final String assetsDir) {
        this.assetsDir = assetsDir.endsWith("/") || assetsDir.endsWith("\\") ? assetsDir : assetsDir + File.separator;
    }

    void setActiveGame(final BridgeGuiGame game) {
        activeGame = game;
    }

    public boolean isRunningOnDesktop() { return true; }
    public boolean isLibgdxPort() { return false; }
    public String getCurrentVersion() { return "mtg-united-bridge"; }
    public String getUserAgent() { return "Forge/MTGUnitedBridge"; }

    public void invokeInEdtNow(final Runnable runnable) {
        runnable.run();
    }

    public void invokeInEdtLater(final Runnable runnable) {
        edt.execute(() -> {
            try {
                runnable.run();
            } catch (final RuntimeException ex) {
                Log.error("Exception on bridge EDT", ex);
            }
        });
    }

    public void invokeInEdtAndWait(final Runnable proc) {
        if (isGuiThread()) {
            proc.run();
            return;
        }
        final Future<?> f = edt.submit(proc);
        try {
            f.get();
        } catch (final Exception ex) {
            throw new RuntimeException(ex);
        }
    }

    public void runBackgroundTask(final String message, final Runnable task) {
        final Thread t = new Thread(task, "bridge-bg");
        t.setDaemon(true);
        t.start();
    }

    public boolean isGuiThread() {
        return Thread.currentThread() == edtThread;
    }

    public String getAssetsDir() { return assetsDir; }

    /**
     * Every IGuiBase method not written out here (images, sounds, skins, dialogs a headless host doesn't show...)
     * lands here through the generated GuiBaseCompat, with a safe default; the few that need a value are answered by name.
     */
    protected Object onGuiCall(final String method, final Object[] args) {
        switch (method) {
            case "encodeSymbols": return args.length > 0 ? args[0] : "";
            case "getScreenScale": return 1f;
            case "showBoxedProduct": return Boolean.TRUE;
            case "getSaveFile": return args.length > 0 ? args[0] : null;
            case "download":
                for (final Object a : args) { if (a instanceof Consumer<?> c) { ((Consumer<Object>) c).accept(Boolean.FALSE); } }
                return null;
            case "showBugReportDialog":
                Log.warn("Forge reported a problem: " + (args.length > 0 ? args[0] : "") + "\n" + (args.length > 1 ? args[1] : ""));
                return null;
            case "showImageDialog": {
                final BridgeGuiGame g = activeGame;
                if (g != null && args.length > 1) { g.message(String.valueOf(args[1]), args.length > 2 ? String.valueOf(args[2]) : ""); }
                return null;
            }
            default:
                Compat.unknownCall("IGuiBase", method);
                return null;
        }
    }

    public int showOptionDialog(final String message, final String title, final FSkinProp icon, final List<String> options, final int defaultOption) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return defaultOption; }
        return g.showOptionDialog(message, title, icon, options, defaultOption);
    }

    public String showInputDialog(final String message, final String title, final FSkinProp icon, final String initialInput, final List<String> inputOptions, final boolean isNumeric) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return initialInput; }
        return g.showInputDialog(message, title, icon, initialInput, inputOptions, isNumeric);
    }


    public <T> List<T> order(final String title, final String top, final int remainingObjectsMin, final int remainingObjectsMax, final List<T> sourceChoices, final List<T> destChoices) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return new ArrayList<>(sourceChoices); }
        return g.order(title, top, remainingObjectsMin, remainingObjectsMax, sourceChoices, destChoices, null, false, false).ordered();
    }

    public <T> List<T> getChoices(final String message, final int min, final int max, final Collection<T> choices, final Collection<T> selected, final FSerializableFunction<T, String> display) {
        final BridgeGuiGame g = activeGame;
        final List<T> list = new ArrayList<>(choices);
        if (g == null) { return min > 0 ? list.subList(0, Math.min(min, list.size())) : new ArrayList<>(); }
        return g.getChoices(message, min, max, list, selected == null ? null : new ArrayList<>(selected), display);
    }

    public PaperCard chooseCard(final String title, final String message, final List<PaperCard> list) {
        final BridgeGuiGame g = activeGame;
        if (g == null || list.isEmpty()) { return list.isEmpty() ? null : list.get(0); }
        return g.one(title + (message == null ? "" : " - " + message), list);
    }


    public IGuiGame getNewGuiGame() {
        // Only used by Forge when no human is in the game (spectating). Reuse the active GUI.
        return activeGame;
    }

    public HostedMatch hostMatch() {
        return new HostedMatch();
    }

}

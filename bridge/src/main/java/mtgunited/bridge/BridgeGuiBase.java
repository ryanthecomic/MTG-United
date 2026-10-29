package mtgunited.bridge;

import forge.gamemodes.match.HostedMatch;
import forge.gui.download.GuiDownloadService;
import forge.gui.interfaces.IGuiGame;
import forge.gui.interfaces.IGuiBase;
import forge.item.PaperCard;
import forge.localinstance.skin.FSkinProp;
import forge.localinstance.skin.ISkinImage;
import forge.sound.IAudioClip;
import forge.sound.IAudioMusic;
import forge.util.FSerializableFunction;
import forge.util.ImageFetcher;
import org.jupnp.UpnpServiceConfiguration;

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
public final class BridgeGuiBase implements IGuiBase {
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

    @Override public boolean isRunningOnDesktop() { return true; }
    @Override public boolean isLibgdxPort() { return false; }
    @Override public String getCurrentVersion() { return "mtg-united-bridge"; }
    @Override public String getUserAgent() { return "Forge/MTGUnitedBridge"; }

    @Override
    public void invokeInEdtNow(final Runnable runnable) {
        runnable.run();
    }

    @Override
    public void invokeInEdtLater(final Runnable runnable) {
        edt.execute(() -> {
            try {
                runnable.run();
            } catch (final RuntimeException ex) {
                Log.error("Exception on bridge EDT", ex);
            }
        });
    }

    @Override
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

    @Override
    public void runBackgroundTask(final String message, final Runnable task) {
        final Thread t = new Thread(task, "bridge-bg");
        t.setDaemon(true);
        t.start();
    }

    @Override
    public boolean isGuiThread() {
        return Thread.currentThread() == edtThread;
    }

    @Override public String getAssetsDir() { return assetsDir; }
    @Override public ImageFetcher getImageFetcher() { return null; }
    @Override public ISkinImage getSkinIcon(final FSkinProp skinProp) { return null; }
    @Override public ISkinImage getUnskinnedIcon(final String path) { return null; }
    @Override public ISkinImage getCardArt(final PaperCard card, final boolean backFace) { return null; }
    @Override public ISkinImage createLayeredImage(final PaperCard card, final FSkinProp background, final String overlayFilename, final float opacity) { return null; }
    @Override public void clearImageCache() { }
    @Override public String encodeSymbols(final String str, final boolean formatReminderText) { return str; }
    @Override public int getAvatarCount() { return 0; }
    @Override public int getSleevesCount() { return 0; }
    @Override public float getScreenScale() { return 1f; }
    @Override public void preventSystemSleep(final boolean preventSleep) { }
    @Override public void download(final GuiDownloadService service, final Consumer<Boolean> callback) { if (callback != null) { callback.accept(false); } }
    @Override public void copyToClipboard(final String text) { }
    @Override public void browseToUrl(final String url) { }

    @Override public void showCardList(final String title, final String message, final List<PaperCard> list) { }
    @Override public boolean showBoxedProduct(final String title, final String message, final List<PaperCard> list) { return true; }
    @Override public void showBugReportDialog(final String title, final String text, final boolean showExitAppBtn) {
        Log.warn("Forge reported a problem: " + title + "\n" + text);
    }
    @Override public void showImageDialog(final ISkinImage image, final String message, final String title) {
        final BridgeGuiGame g = activeGame;
        if (g != null) { g.message(message, title); }
    }

    @Override
    public int showOptionDialog(final String message, final String title, final FSkinProp icon, final List<String> options, final int defaultOption) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return defaultOption; }
        return g.showOptionDialog(message, title, icon, options, defaultOption);
    }

    @Override
    public String showInputDialog(final String message, final String title, final FSkinProp icon, final String initialInput, final List<String> inputOptions, final boolean isNumeric) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return initialInput; }
        return g.showInputDialog(message, title, icon, initialInput, inputOptions, isNumeric);
    }

    @Override public String showFileDialog(final String title, final String defaultDir) { return null; }
    @Override public File getSaveFile(final File defaultFile) { return defaultFile; }

    @Override
    public <T> List<T> order(final String title, final String top, final int remainingObjectsMin, final int remainingObjectsMax, final List<T> sourceChoices, final List<T> destChoices) {
        final BridgeGuiGame g = activeGame;
        if (g == null) { return new ArrayList<>(sourceChoices); }
        return g.order(title, top, remainingObjectsMin, remainingObjectsMax, sourceChoices, destChoices, null, false, false).ordered();
    }

    @Override
    public <T> List<T> getChoices(final String message, final int min, final int max, final Collection<T> choices, final Collection<T> selected, final FSerializableFunction<T, String> display) {
        final BridgeGuiGame g = activeGame;
        final List<T> list = new ArrayList<>(choices);
        if (g == null) { return min > 0 ? list.subList(0, Math.min(min, list.size())) : new ArrayList<>(); }
        return g.getChoices(message, min, max, list, selected == null ? null : new ArrayList<>(selected), display);
    }

    @Override
    public PaperCard chooseCard(final String title, final String message, final List<PaperCard> list) {
        final BridgeGuiGame g = activeGame;
        if (g == null || list.isEmpty()) { return list.isEmpty() ? null : list.get(0); }
        return g.one(title + (message == null ? "" : " - " + message), list);
    }

    @Override public boolean isSupportedAudioFormat(final File file) { return false; }
    @Override public IAudioClip createAudioClip(final String filename) { return null; }
    @Override public IAudioMusic createAudioMusic(final String filename) { return null; }
    @Override public void startAltSoundSystem(final String filename, final boolean isSynchronized) { }
    @Override public void showSpellShop() { }
    @Override public void showBazaar() { }

    @Override
    public IGuiGame getNewGuiGame() {
        // Only used by Forge when no human is in the game (spectating). Reuse the active GUI.
        return activeGame;
    }

    @Override
    public HostedMatch hostMatch() {
        return new HostedMatch();
    }

    @Override public UpnpServiceConfiguration getUpnpPlatformService() { return null; }
    @Override public boolean hasNetGame() { return false; }
}

package mtgunited.bridge;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

/** Minimal console logger so bridge output is easy to tell apart from Forge's own logging. */
final class Log {
    private static java.io.PrintStream file;

    /** Also write everything (including Forge's own console output) to this file, replaced on each start. */
    static synchronized void toFile(final java.io.File f) {
        try {
            f.getParentFile().mkdirs();
            file = new java.io.PrintStream(new java.io.FileOutputStream(f, false), true, "UTF-8");
            final java.io.PrintStream out = System.out, err = System.err;
            System.setOut(new java.io.PrintStream(new Tee(out, file), true, "UTF-8"));
            System.setErr(new java.io.PrintStream(new Tee(err, file), true, "UTF-8"));
            info("Logging to " + f.getAbsolutePath());
        } catch (final java.io.IOException ex) {
            warn("Could not open log file " + f + ": " + ex.getMessage());
        }
    }

    private static final class Tee extends java.io.OutputStream {
        private final java.io.OutputStream a, b;
        Tee(final java.io.OutputStream a, final java.io.OutputStream b) { this.a = a; this.b = b; }
        @Override public void write(final int c) throws java.io.IOException { a.write(c); b.write(c); }
        @Override public void write(final byte[] buf, final int off, final int len) throws java.io.IOException { a.write(buf, off, len); b.write(buf, off, len); }
        @Override public void flush() throws java.io.IOException { a.flush(); b.flush(); }
    }

    private static final DateTimeFormatter TIME = DateTimeFormatter.ofPattern("HH:mm:ss.SSS");
    static volatile boolean verbose = false;

    private Log() {}

    static void info(final String msg) {
        System.out.println("[bridge " + LocalTime.now().format(TIME) + "] " + msg);
    }

    static void debug(final String msg) {
        if (verbose) { info(msg); }
    }

    static void warn(final String msg) {
        System.out.println("[bridge " + LocalTime.now().format(TIME) + "] WARN " + msg);
    }

    static void error(final String msg, final Throwable t) {
        System.out.println("[bridge " + LocalTime.now().format(TIME) + "] ERROR " + msg);
        if (t != null) { t.printStackTrace(System.out); }
    }
}

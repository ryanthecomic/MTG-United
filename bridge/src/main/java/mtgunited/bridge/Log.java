package mtgunited.bridge;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

/** Minimal console logger so bridge output is easy to tell apart from Forge's own logging. */
final class Log {
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

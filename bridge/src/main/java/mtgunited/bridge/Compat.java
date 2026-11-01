package mtgunited.bridge;

import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/**
 * Small reflection helpers for the parts of Forge that change between versions (preference names, fields, methods
 * added or renamed). Each one fails soft: if this Forge doesn't have the thing, nothing happens and it's logged once.
 */
final class Compat {
    private Compat() { }

    private static final Set<String> warned = ConcurrentHashMap.newKeySet();

    static void once(final String key, final String message) {
        if (warned.add(key)) { Log.info(message); }
    }

    /** A Forge GUI call the bridge doesn't know (usually added in a newer Forge): answered with a default. */
    static void unknownCall(final String iface, final String method) {
        once(iface + "." + method, "Forge called " + iface + "." + method + "(), which this bridge doesn't handle; using a default.");
    }

    /** Calls a public method by name if this Forge has one that takes these arguments. Returns null otherwise. */
    static Object call(final Object target, final String name, final Object... args) {
        if (target == null) { return null; }
        for (final Method m : target.getClass().getMethods()) {
            if (!m.getName().equals(name) || m.getParameterCount() != args.length) { continue; }
            final Class<?>[] ps = m.getParameterTypes();
            boolean ok = true;
            for (int i = 0; i < ps.length && ok; i++) { ok = args[i] == null ? !ps[i].isPrimitive() : box(ps[i]).isInstance(args[i]); }
            if (!ok) { continue; }
            try {
                return m.invoke(target, args);
            } catch (final Exception ex) {
                Log.warn("Forge call " + name + " failed: " + ex);
                return null;
            }
        }
        once("call." + target.getClass().getSimpleName() + "." + name, "This Forge has no " + target.getClass().getSimpleName() + "." + name + "(); skipped.");
        return null;
    }

    /** Sets a public field by name if it exists (e.g. AI settings on Game). */
    static void setField(final Object target, final String name, final Object value) {
        if (target == null) { return; }
        try {
            final Field f = target.getClass().getField(name);
            f.set(target, value);
        } catch (final Exception ex) {
            once("field." + name, "This Forge has no " + target.getClass().getSimpleName() + "." + name + "; skipped.");
        }
    }

    /** Sets a Forge preference by its enum name, if this Forge has it. */
    static void setPref(final Object prefs, final String prefName, final Object value) {
        try {
            final Class<?> enumType = Class.forName("forge.localinstance.properties.ForgePreferences$FPref");
            final Object key = Enum.valueOf((Class) enumType, prefName);
            for (final Method m : prefs.getClass().getMethods()) {
                if (m.getName().equals("setPref") && m.getParameterCount() == 2 && m.getParameterTypes()[0].isInstance(key)
                    && box(m.getParameterTypes()[1]).isInstance(value)) {
                    m.invoke(prefs, key, value);
                    return;
                }
            }
        } catch (final Exception ex) {
            // missing preference: fall through
        }
        once("pref." + prefName, "This Forge has no preference " + prefName + "; skipped.");
    }

    /** Reads a no-argument accessor (record component, getter) if present. */
    static Object get(final Object target, final String name) {
        if (target == null) { return null; }
        try {
            return target.getClass().getMethod(name).invoke(target);
        } catch (final Exception ex) {
            return null;
        }
    }

    /** A boolean accessor (isInstant, isTrigger...) if this Forge has it; else false. */
    static boolean is(final Object target, final String name) {
        final Object v = get(target, name);
        if (v instanceof Boolean b) { return b; }
        if (v == null && target != null && (name.equals("isInstant") || name.equals("isSorcery"))) {
            // Older Forge: ask the card type instead.
            final Object type = get(target, "getType");
            return Boolean.TRUE.equals(get(type, name));
        }
        return false;
    }

    /** hasKeyword(Keyword), or hasKeyword(String) in Forge versions that take the keyword's name. */
    static boolean hasKeyword(final Object state, final Enum<?> keyword) {
        if (state == null) { return false; }
        for (final Method m : state.getClass().getMethods()) {
            if (!m.getName().equals("hasKeyword") || m.getParameterCount() != 1) { continue; }
            try {
                final Class<?> p = m.getParameterTypes()[0];
                if (p.isInstance(keyword)) { return Boolean.TRUE.equals(m.invoke(state, keyword)); }
                if (p == String.class) { return Boolean.TRUE.equals(m.invoke(state, keyword.toString())); }
            } catch (final Exception ex) {
                return false;
            }
        }
        return false;
    }

    /** Wraps a list in Forge's IGuiGame.OrderResult (newer Forge); older Forge wants the list itself. */
    static Object orderResult(final List<?> picked) {
        try {
            final Class<?> rec = Class.forName("forge.gui.interfaces.IGuiGame$OrderResult");
            for (final java.lang.reflect.Constructor<?> c : rec.getConstructors()) {
                if (c.getParameterCount() == 2) { return c.newInstance(picked, false); }
            }
        } catch (final Exception ex) {
            once("orderResult", "Couldn't build Forge's OrderResult: " + ex);
        }
        return picked;
    }

    @SuppressWarnings("unchecked")
    static <T> List<T> list(final Object o) {
        final List<T> out = new ArrayList<>();
        if (o instanceof Iterable<?> it) { for (final Object x : it) { out.add((T) x); } }
        return out;
    }

    static int integer(final Object o) {
        return o instanceof Number n ? n.intValue() : 0;
    }

    private static Class<?> box(final Class<?> c) {
        if (!c.isPrimitive()) { return c; }
        if (c == boolean.class) { return Boolean.class; }
        if (c == int.class) { return Integer.class; }
        if (c == long.class) { return Long.class; }
        if (c == float.class) { return Float.class; }
        if (c == double.class) { return Double.class; }
        if (c == byte.class) { return Byte.class; }
        if (c == short.class) { return Short.class; }
        return Character.class;
    }
}

package mtgunited.bridge;

import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.PriorityQueue;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import forge.StaticData;
import forge.card.CardRules;
import forge.card.CardType;
import forge.card.ColorSet;
import forge.item.PaperCard;

/**
 * Card search for the deck builder. Builds a compact index of every unique card once (name, set, cost, type,
 * colours) and answers "find the N cards closest to this text" and "describe these card names". Nothing is sent
 * to the client until it asks, so the deck builder never has to load the whole card pool.
 */
final class CardCatalog {
    private static final class Entry {
        final String name, lower, set, cost, type, colors, text;
        final int cmc;
        final boolean land, legendary, creature, basic;

        Entry(final PaperCard pc) {
            final CardRules r = pc.getRules();
            name = r.getName();
            lower = name.toLowerCase(Locale.ROOT);
            set = pc.getEdition();
            cost = r.getManaCost() == null ? "" : r.getManaCost().getSimpleString();
            cmc = r.getManaCost() == null ? 0 : r.getManaCost().getCMC();
            final CardType t = r.getType();
            type = t == null ? "" : t.toString();
            land = t != null && t.isLand();
            legendary = t != null && t.isLegendary();
            creature = t != null && t.isCreature();
            basic = t != null && t.isBasic();
            colors = colorString(r.getColor());
            final String o = r.getOracleText();
            text = o == null ? "" : o.replace("\\n", "\n");
        }
    }

    private static volatile List<Entry> entries;
    private static volatile Map<String, Entry> byName;

    private CardCatalog() { }

    private static synchronized void ensureLoaded() {
        if (entries != null) { return; }
        final Collection<PaperCard> all = StaticData.instance().getCommonCards().getUniqueCards();
        final List<Entry> list = new ArrayList<>(all.size());
        final Map<String, Entry> map = new HashMap<>(all.size() * 2);
        for (final PaperCard pc : all) {
            try {
                if (pc.getRules().isVariant()) { continue; }        // planes, schemes, vanguards...
                if (pc.getName().startsWith("A-")) { continue; }    // digital rebalanced copies
                final Entry e = new Entry(pc);
                if (map.putIfAbsent(e.lower, e) == null) { list.add(e); }
            } catch (final RuntimeException ex) {
                // skip odd cards rather than failing the whole index
            }
        }
        byName = map;
        entries = list;
        Log.info("Card catalog ready: " + list.size() + " cards");
    }

    /** The {@code limit} best matches for {@code query}: exact, then prefix, word start, substring, then fuzzy. */
    static JsonObject search(final String query, final int limit) {
        ensureLoaded();
        final String q = query == null ? "" : query.trim().toLowerCase(Locale.ROOT);
        final JsonObject out = new JsonObject();
        out.addProperty("t", "cardResults");
        out.addProperty("q", query == null ? "" : query);
        final JsonArray arr = new JsonArray();
        out.add("cards", arr);
        if (q.isEmpty()) { return out; }

        final int n = Math.max(1, Math.min(limit, 200));
        // Keep the n best (lowest score) in a bounded max-heap.
        final PriorityQueue<Object[]> best = new PriorityQueue<>((a, b) -> Double.compare((double) b[0], (double) a[0]));
        for (final Entry e : entries) {
            final double s = score(e.lower, q);
            if (s >= 1000) { continue; }
            if (best.size() < n) {
                best.add(new Object[] { s, e });
            } else if (s < (double) best.peek()[0]) {
                best.poll();
                best.add(new Object[] { s, e });
            }
        }
        final List<Object[]> sorted = new ArrayList<>(best);
        sorted.sort((a, b) -> {
            final int c = Double.compare((double) a[0], (double) b[0]);
            return c != 0 ? c : ((Entry) a[1]).name.compareToIgnoreCase(((Entry) b[1]).name);
        });
        for (final Object[] o : sorted) { arr.add(json((Entry) o[1])); }
        return out;
    }

    /** Card details for names already in a deck (used by the deck builder's layouts). */
    static JsonObject info(final JsonArray names, final String reqId) {
        ensureLoaded();
        final JsonObject out = new JsonObject();
        out.addProperty("t", "cardInfo");
        if (reqId != null) { out.addProperty("req", reqId); }
        final JsonArray arr = new JsonArray();
        out.add("cards", arr);
        if (names == null) { return out; }
        for (final JsonElement el : names) {
            if (el == null || el.isJsonNull()) { continue; }
            String name = el.getAsString().trim();
            Entry e = byName.get(name.toLowerCase(Locale.ROOT));
            if (e == null && name.contains(" // ")) { e = byName.get(name.substring(0, name.indexOf(" // ")).toLowerCase(Locale.ROOT)); }
            if (e == null) {
                // Front face of a double-faced card typed on its own.
                for (final Entry x : entries) {
                    if (x.lower.startsWith(name.toLowerCase(Locale.ROOT) + " // ")) { e = x; break; }
                }
            }
            if (e == null) {
                final JsonObject unknown = new JsonObject();
                unknown.addProperty("name", name);
                unknown.addProperty("unknown", true);
                arr.add(unknown);
            } else {
                final JsonObject j = json(e);
                j.addProperty("asked", name);
                arr.add(j);
            }
        }
        return out;
    }

    private static double score(final String name, final String q) {
        if (name.equals(q)) { return 0; }
        if (name.startsWith(q)) { return 1 + name.length() * 0.001; }
        final int idx = name.indexOf(q);
        if (idx > 0) {
            final char before = name.charAt(idx - 1);
            final boolean wordStart = before == ' ' || before == '-' || before == ',' || before == '\'';
            return (wordStart ? 2 : 3) + idx * 0.01 + name.length() * 0.001;
        }
        // Every query word somewhere in the name ("bolt light" finds "Lightning Bolt").
        final String[] words = q.split("\\s+");
        if (words.length > 1) {
            boolean all = true;
            for (final String w : words) { if (!name.contains(w)) { all = false; break; } }
            if (all) { return 4 + name.length() * 0.001; }
        }
        // Fuzzy: the query's letters in order (typos with a dropped letter, abbreviations).
        if (q.length() >= 3) {
            int j = 0, gaps = 0, last = -1;
            for (int i = 0; i < name.length() && j < q.length(); i++) {
                if (name.charAt(i) == q.charAt(j)) {
                    if (last >= 0 && i - last > 1) { gaps++; }
                    last = i;
                    j++;
                }
            }
            if (j == q.length()) { return 10 + gaps + name.length() * 0.01; }
            // Small typo: edit distance against the start of the name.
            final String head = name.substring(0, Math.min(name.length(), q.length()));
            final int d = editDistance(head, q);
            if (d <= Math.max(1, q.length() / 4)) { return 20 + d + name.length() * 0.01; }
        }
        return 1000;
    }

    private static int editDistance(final String a, final String b) {
        final int[] prev = new int[b.length() + 1];
        final int[] cur = new int[b.length() + 1];
        for (int j = 0; j <= b.length(); j++) { prev[j] = j; }
        for (int i = 1; i <= a.length(); i++) {
            cur[0] = i;
            for (int j = 1; j <= b.length(); j++) {
                final int c = a.charAt(i - 1) == b.charAt(j - 1) ? 0 : 1;
                cur[j] = Math.min(Math.min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + c);
            }
            System.arraycopy(cur, 0, prev, 0, cur.length);
        }
        return prev[b.length()];
    }

    private static JsonObject json(final Entry e) {
        final JsonObject j = new JsonObject();
        j.addProperty("name", e.name);
        j.addProperty("set", e.set);
        j.addProperty("cost", e.cost);
        j.addProperty("cmc", e.cmc);
        j.addProperty("type", e.type);
        j.addProperty("colors", e.colors);
        j.addProperty("land", e.land);
        j.addProperty("legendary", e.legendary);
        j.addProperty("creature", e.creature);
        j.addProperty("basic", e.basic);
        j.addProperty("text", e.text);
        return j;
    }

    private static String colorString(final ColorSet c) {
        if (c == null) { return ""; }
        final StringBuilder sb = new StringBuilder();
        if (c.hasWhite()) { sb.append('W'); }
        if (c.hasBlue()) { sb.append('U'); }
        if (c.hasBlack()) { sb.append('B'); }
        if (c.hasRed()) { sb.append('R'); }
        if (c.hasGreen()) { sb.append('G'); }
        return sb.toString();
    }
}

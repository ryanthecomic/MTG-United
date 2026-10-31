package mtgunited.bridge;

import forge.StaticData;
import forge.card.CardDb;
import forge.deck.Deck;
import forge.deck.DeckFormat;
import forge.deck.DeckSection;
import forge.deck.io.DeckSerializer;
import forge.item.PaperCard;
import forge.util.FileSection;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Reads a decklist pasted from Arena, Moxfield, Archidekt, MTGO or a Forge .dck file.
 *
 * <p>Accepted line formats: {@code 1 Sol Ring}, {@code 1x Sol Ring}, {@code 1 Sol Ring (CMR) 472},
 * {@code 1 Sol Ring (CMR) 472 *F*}. Section headers such as {@code Commander}, {@code Deck}, {@code Sideboard}
 * are recognised. If no commander section exists, a 1-2 card sideboard made of legal commanders is used (that is
 * how MTGO-style exports carry the commander).</p>
 */
final class DeckLoader {
    private static final Pattern LINE = Pattern.compile(
            "^(\\d+)\\s*[xX]?\\s+(.+?)(?:\\s+\\(([A-Za-z0-9]{2,6})\\)(?:\\s+[A-Za-z0-9\\-\\u2605]+)?)?(?:\\s+\\*[A-Za-z]+\\*)*\\s*$");

    static final class Result {
        Deck deck;
        final List<String> errors = new ArrayList<>();
        final List<String> warnings = new ArrayList<>();
    }

    private DeckLoader() {}

    static Result load(final String text, final String name, final List<String> explicitCommanders) {
        return load(text, name, explicitCommanders, "commander");
    }

    /**
     * @param format "commander" (needs a commander), "constructed" (60+) or "limited" (40+): in the last two a
     *               Commander section is simply played as part of the main deck.
     */
    static Result load(final String text, final String name, final List<String> explicitCommanders, final String format) {
        final boolean commanderGame = format == null || "commander".equals(format);
        final Result r = new Result();
        final List<String> lines = Arrays.asList(text.replace("\r", "").split("\n"));

        if (text.contains("[Main]") || text.contains("[metadata]") || text.contains("[Commander]")) {
            r.deck = DeckSerializer.fromSections(FileSection.parseSections(lines));
            if (r.deck == null) {
                r.errors.add("Could not read the Forge .dck text.");
                return r;
            }
        } else {
            r.deck = new Deck(name == null || name.isBlank() ? "My Deck" : name);
            String section = "main";
            boolean sawBlankAfterMain = false;
            for (final String raw : lines) {
                final String line = raw.trim();
                if (line.isEmpty()) {
                    if (r.deck.getMain().countAll() > 0) { sawBlankAfterMain = true; }
                    continue;
                }
                if (line.startsWith("//") || line.startsWith("#")) { continue; }
                final String lower = line.toLowerCase(Locale.ROOT).replace(":", "").trim();
                switch (lower) {
                    case "commander": case "commanders": section = "commander"; continue;
                    case "deck": case "main": case "mainboard": section = "main"; sawBlankAfterMain = false; continue;
                    case "sideboard": section = "side"; continue;
                    case "companion": section = "side"; continue;
                    case "maybeboard": case "considering": section = "skip"; continue;
                    case "about": section = "skip"; continue;
                    default: break;
                }
                if (lower.startsWith("name ")) { continue; }
                if ("skip".equals(section)) { continue; }

                final Matcher m = LINE.matcher(line);
                final int count;
                final String cardName;
                final String set;
                if (m.matches()) {
                    count = Integer.parseInt(m.group(1));
                    cardName = m.group(2).trim();
                    set = m.group(3);
                } else {
                    count = 1;
                    cardName = line;
                    set = null;
                }
                final PaperCard card = findCard(cardName, set);
                if (card == null) {
                    r.errors.add("Unknown card: " + cardName);
                    continue;
                }
                // Moxfield/MTGO put the commander after a blank line at the end of the main list.
                final String target = sawBlankAfterMain && "main".equals(section) ? "side" : section;
                switch (target) {
                    case "commander" -> r.deck.getOrCreate(DeckSection.Commander).add(card, count);
                    case "side" -> r.deck.getOrCreate(DeckSection.Sideboard).add(card, count);
                    default -> r.deck.getMain().add(card, count);
                }
            }
        }

        if (!commanderGame) {
            // 60 / 40 card formats: commanders are just cards in the deck; the sideboard isn't played.
            if (r.deck.has(DeckSection.Commander)) {
                r.deck.getMain().addAll(r.deck.get(DeckSection.Commander));
                r.deck.get(DeckSection.Commander).clear();
            }
            if (r.deck.getMain().countAll() == 0) { r.errors.add("The deck has no cards."); }
            if (r.errors.isEmpty()) {
                final DeckFormat df = "limited".equals(format) ? DeckFormat.Limited : DeckFormat.Constructed;
                final String problem = df.getDeckConformanceProblem(r.deck);
                if (problem != null) { r.warnings.add(problem); }
            }
            return r;
        }

        // Explicit commander names from the client win over anything in the list.
        if (explicitCommanders != null && !explicitCommanders.isEmpty()) {
            r.deck.getOrCreate(DeckSection.Commander).clear();
            for (final String cmd : explicitCommanders) {
                final PaperCard card = findCard(cmd.trim(), null);
                if (card == null) {
                    r.errors.add("Unknown commander: " + cmd);
                    continue;
                }
                r.deck.getOrCreate(DeckSection.Commander).add(card, 1);
                for (final PaperCard inMain : r.deck.getMain().toFlatList()) {
                    if (inMain.getName().equals(card.getName())) {
                        r.deck.getMain().remove(inMain, 1);
                        break;
                    }
                }
            }
        }

        // No commander section: promote a small sideboard of legal commanders.
        if (!r.deck.has(DeckSection.Commander) || r.deck.get(DeckSection.Commander).isEmpty()) {
            if (r.deck.has(DeckSection.Sideboard)) {
                final List<PaperCard> side = r.deck.get(DeckSection.Sideboard).toFlatList();
                final boolean allLegal = !side.isEmpty() && side.size() <= 2
                        && side.stream().allMatch(c -> c.getRules().canBeCommander());
                if (allLegal) {
                    r.deck.getOrCreate(DeckSection.Commander).addAllFlat(side);
                    r.deck.getOrCreate(DeckSection.Sideboard).clear();
                }
            }
        }
        if (!r.deck.has(DeckSection.Commander) || r.deck.get(DeckSection.Commander).isEmpty()) {
            r.errors.add("No commander found. Put it under a 'Commander' heading, or type its name in the Commander field.");
        }

        if (r.errors.isEmpty()) {
            final String problem = DeckFormat.Commander.getDeckConformanceProblem(r.deck);
            if (problem != null) {
                // Still playable for testing; just tell the player.
                r.warnings.add(problem);
            }
        }
        return r;
    }

    private static PaperCard findCard(final String name, final String set) {
        final CardDb db = StaticData.instance().getCommonCards();
        String n = name;
        // Arena writes split/adventure/MDFC cards with " // "; Forge accepts that, but try the front face too.
        PaperCard pc = null;
        if (set != null) {
            pc = db.getCard(n, set.toUpperCase(Locale.ROOT));
        }
        if (pc == null) { pc = db.getCard(n); }
        if (pc == null && n.contains(" // ")) {
            n = n.substring(0, n.indexOf(" // "));
            pc = db.getCard(n);
        }
        if (pc == null && n.contains("/") && !n.contains(" // ")) {
            pc = db.getCard(n.replace("/", " // "));
        }
        return pc;
    }
}

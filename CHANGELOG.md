# MTG United changelog

## 0.1.3

**Limited: draft and sealed**
- New Limited tab (and a carousel slide with the Gelatinous Cube): start a draft or sealed against bots, read how it
  works, and keep your cubes (import a .txt, paste a list, keep a deck as a cube, export, delete).
- DRAFT / SEALED is also in the LAN lobby when hosting (everyone at the table, bots fill the empty seats).
- Packs from an official set (type its code; cards and rarities come from Scryfall), a custom set from
  Magic Set Editor, or a cube (a saved cube, the clipboard or one of your decks). Each draft pack can come from
  a different source. CARDS… chooses which of the set's cards can be opened.
- Draft: three packs of 15, pick and pass (left, right, left); bots stick to their colours. Sealed: six
  packs each. Then BUILD YOUR DECK opens the builder with only the cards you opened, plus basic lands; diamonds
  above each card show how many you opened and how many are in the deck, like Arena.
- At a LAN table, the deck you build becomes your deck in the host's lobby.

**Translation**
- Much more of the interface is in Português (Brasil) and Español: the Forge tab and its setup guide, the
  Kitchen Table guide and log, LAN lobbies, deck builder hints, notices and shortcuts.

**Forge setup (also in the 0.1.2 hotfix)**
- Setup no longer needs the `jar` tool, and picks Forge's desktop jar (not the adventure editor).
- Works with older Forge releases too (tested back to 2.0.09).
- No more "port already in use" from a second bridge; Uninstall stops the bridge before removing files.
- A found Forge gets a refresh button next to the green one to update it.

## 0.1.2

**Forge**
- Works with any Forge version: the bridge adapts itself to the Forge it's built against (official releases,
  newer releases, nightly code). Install / Update get Forge's newest code and fall back to the tested version
  if it doesn't build. "Use my Forge" accepts any version.
- Forge tab: COPY OUTPUT button (or right-click the output), and a DOWNLOAD JAVA button.

**Look and feel (Arena style)**
- Play opens a panel from the right: mode tabs, format, players, AI skill and decks, and your decks to pick
  from on the left (double-click one to play).
- Options and the in-game menu are a band across the screen over Jodah, the Unifier, with Gameplay /
  Graphics / Audio / Shortcuts pages. Clicking outside closes them.
- Every other window (zone viewers, choices, dialogs, LAN lobbies, custom sets) uses the same dark band;
  the blue tint is gone everywhere.
- Home carousel: GitHub first, then the Forge and Kitchen Table slides with new art; your deck stays in its tile.
- Hold Alt over any card on any screen for the big view.

**Rules engine**
- The mulligan hand and card-choice screens dim the board and sit above the buttons.
- Cards playable from your graveyard, exile or library make that zone's symbol glow; a castable commander
  lights up your whole panel. Right-click your library to see its revealed cards.
- Set codes and years are hidden from AI deck names (Options → Gameplay to show them).

**Kitchen Table**
- Arrows: point a card at another card or a player (A, or the card's menu) so everyone at the table, LAN included,
  sees what's affecting what. Pointing again removes it; Shift+A clears yours; they clear each turn.

**Everything else**
- Language: Português (Brasil), English and Español (Options → Gameplay → Language). Switches live.
- Animation speed: Normal plays the motion doc's exact timings; Fast caps every animation at 400 ms.
- Kitchen Table: interface size and card size apply there too.
- Deck builder: the colour filters sit above the card grid again (they were covered and couldn't be clicked).
- Errors: real errors go to `logs/errors.log` (once each, with what you were doing) and show one friendly
  message; a crashed session is noticed on the next start. A table action that fails is rolled back.

## 0.1.1

**Rules engine (Forge)**
- Formats: Commander, 60-card and 40-card games against the AI (AI decks follow the format).
- LAN play with the rules engine: people and AI opponents at one table. Only the host needs Forge.
- Focus your own board (F, or scroll over it); your cards always fit inside your area.

**Kitchen table**
- LAN play with automatic custom-set sharing, Host / Join lobby with network discovery and Refresh.
- Cockatrice-style shortcuts for cards, piles and the table; multi-select (Shift + click or drag a box).
- Notes on cards, summoning sickness, counters (+1/+1 and −1/−1 cancel out), double-faced custom cards (F).
- Drag the top card off any pile, hold Alt to zoom, Q / E to turn cards, click a life total to type it.
- Choose the other seats' decks when playing alone; Ctrl + click a nameplate to switch seats.

**Decks**
- Import from MTGO, Moxfield, Archidekt or plain text (clipboard), or from a file; export to .txt / .dck.
- Scryfall search syntax, colour and land filters, card pools, deck covers, favourites, duplicate, rename.
- Column view with − / + per card; drag cards between the collection and the list.

**Other**
- Kitchen Table tab with a guide to custom sets; Forge's own mana symbols; options for resolution and window mode.
- Fixes for folders with accented letters, the menu video, and many smaller things.

## 0.1.0
- First build: Arena-style client for Forge's Commander rules engine, deck builder, kitchen table.

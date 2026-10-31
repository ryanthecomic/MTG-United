# MTG United 0.1.0

## First release

MTG United is a free, community-built Magic: The Gathering client with a modern,
Arena-inspired interface. This first release brings together a Unity front end,
the Forge rules engine, and a local bridge so you can play Commander against AI
opponents or use the Kitchen Table mode for custom cards and local network games.

## Highlights

- **Commander games against Forge AI**
  - Play with one to five AI opponents.
  - Use a preconstructed AI deck or generate a random deck.
  - Forge handles the game rules, including the stack, combat, triggers, choices,
    commander tax, and commander damage.
- **Modern table interface**
  - Animated cards, hover previews, stack display, phase track, and contextual
    card and target highlights.
  - Mulligans, game-over state, graveyard and exile views, undo, phase stops,
    automatic mana payment, and manual mana payment.
- **Flexible deck import**
  - Paste Arena, Moxfield, Archidekt, MTGO, or Forge `.dck` decklists.
  - Specify commanders in a `Commander` section or in the Commander field.
  - Start with a random Commander precon when no decklist is provided.
- **Kitchen Table mode**
  - Import custom sets exported from Magic Set Editor.
  - Play with custom cards without installing Forge.
  - Host or join games over a local network.
  - Custom sets are copied automatically between LAN players.
- **Automatic setup**
  - The packaged application guides you through installing Java, Git, and Forge
    when they are needed.
  - Card images are downloaded from Scryfall and cached locally.

## Installation

1. Download and extract `MTG United 0.1.0.zip` to a writable folder.
2. Open `MTG United.exe`.
3. If prompted by Windows SmartScreen, choose **More info** and then **Run
   anyway**.
4. For Forge mode, install Java and Forge from the **Forge** setup page, then
   choose **Home > Play**.

Do not install the application under `Program Files`; it writes Forge files and
configuration beside the executable.

## Known limitations

- This release is primarily tested on Windows.
- Forge Commander games are local play against AI; online Commander play is not
  included.
- Combat damage assignment among multiple blockers is automatic.
- Tokens are displayed as text frames rather than token art.
- Sound, sideboarding, and the deck builder are not included yet.
- Forge may take 30–60 seconds to load its card database on first launch.
- LAN play requires all players to be on the same local network and may require
  allowing the application through the Windows private-network firewall.

## Credits and licensing

MTG United uses the open-source Forge rules engine and Scryfall for card images.
Forge is licensed under GPL-3.0, and the bridge is distributed under a
GPL-3.0-compatible license. This project is not affiliated with Wizards of the
Coast and does not use assets from MTG Arena.

# MTG United

A Commander client with a modern, Arena-style interface, built on the open-source
[Forge](https://github.com/Card-Forge/forge) rules engine. You play a 2–6 player Commander pod
against Forge's AI. Forge enforces the rules; the Unity client draws the game and takes your clicks.

```
┌──────────────────────┐   JSON over 127.0.0.1:7777    ┌──────────────────────────┐
│ Unity client (C#)    │  ───────────────────────────▶ │ Forge bridge (Java)      │
│ board, hand, stack,  │  ◀─────────────────────────── │ headless Forge engine    │
│ prompts, card images │      state / prompts / asks   │ + Forge AI opponents     │
└──────────────────────┘                               └──────────────────────────┘
```

* `bridge/` runs Forge with no window and implements Forge's GUI interface (`IGuiGame`). Every
  prompt, highlight and question Forge would normally show on screen becomes a JSON message.
* `unity/MTGUnited/` is the client. The whole UI is built from code, so you don't need to set up a
  scene. Card images come from [Scryfall](https://scryfall.com) and are cached on disk.
* `PROTOCOL.md` documents the messages, in case you want to build another front end.

## Setup (Windows)

You need:

1. **Git**: `winget install Git.Git`. GitHub Desktop's built-in Git isn't on PATH.
2. **Java JDK 17 or newer**: `winget install EclipseAdoptium.Temurin.21.JDK`
3. **Unity 2022.3 LTS or Unity 6**, from Unity Hub.

Then:

1. Double-click **`scripts\setup.bat`**. It will:
   * download Forge's source at a pinned commit into `forge\` (about 500 MB, git-ignored)
   * download Maven into `.tools\` if you don't have it
   * build the Forge engine and then the bridge, producing `bridge\target\forge-bridge-all.jar`

   The first run takes several minutes. Running it again skips the steps that are already done.
2. In Unity Hub, choose **Add → Add project from disk** and pick `unity\MTGUnited`. Open it. Unity
   generates its project files the first time.
3. Press **Play** in any scene, including the empty default one. The client starts the bridge
   automatically and connects. Forge takes 30–60 s to load its card database.
4. Paste a decklist, choose the number of opponents, and press **PLAY**.

To see the bridge's log, run `scripts\start-bridge.bat` yourself before pressing Play. The client
connects to a bridge that's already running instead of starting its own.

## Playing

| Action | How |
|---|---|
| Play a land / cast a spell / activate | Click the card. Blue glow = you can play it now. |
| Pay mana | Automatic (Forge's "Auto" payment). Turn it off in the ≡ menu to tap lands yourself. |
| Targets, choices | Yellow glow = valid choice. Click it, then press the big button if needed. |
| Attack | In declare attackers, click an opponent's nameplate to choose whom to attack, then click creatures. Press **OK** to confirm. |
| Block | Click an attacker, then your blocker. |
| Pass / resolve | Big orange button (or **Space**). |
| End turn | **End Turn** passes until your next decision point. |
| Undo | **Undo** (or Ctrl+Z) for undoable actions, such as tapping a land by mistake. |
| Graveyard / exile | Click your piles on the right. Opponents' counts are on their nameplates. |
| Phase stops | Click a phase on the phase track to stop there on your turn (orange dot). Right-click to stop there on opponents' turns (blue dot). |
| Look at the board during a choice | **View board** in the dialog |

Accepted decklist formats are Arena, Moxfield, Archidekt and MTGO exports (`1 Sol Ring`,
`1 Sol Ring (CMR) 472`), and Forge `.dck` files. Put the commander under a `Commander` heading, or
type its name in the Commander field. If you leave the list empty, you play a random Commander
precon.

## Smoke test without Unity

With the bridge running:

```
python tools\smoke_test.py --opponents 1
```

This starts a game and plays it on autopilot, printing the turn, phase, life totals and prompts.
It's a quick way to check that the engine and protocol work.

## Status and next steps

Working in this version:

* full rules through Forge, including stack, combat, triggers, choices and commander tax/damage
* 1–5 AI opponents using precon or random decks
* Arena-style layout with animated cards, glow highlights, hover preview, stack panel, phase track
  with stops, mulligans, game-over screen and auto-launching the bridge

Not done yet, or deliberately simple:

* combat damage assignment among multiple blockers is automatic (lethal to each blocker in order,
  the rest to the player when trampling)
* tokens are shown as text frames rather than token art
* no sound, no sideboarding, no deck builder
* local play against AI only. Forge supports network play, and multiple Unity clients on one
  bridge would be the next step for playing with friends.

## Licensing

Forge is licensed under GPL-3.0. The bridge links against Forge, so if you **distribute** the
bridge, it must be under a GPL-3.0-compatible license. Keeping this repository GPL-3.0 is the
simplest way to do that. Private use has no restrictions. Card images belong to Wizards of the
Coast and are fetched from Scryfall for personal, non-commercial use, under Wizards' Fan Content
Policy. This project isn't affiliated with Wizards of the Coast. It uses no assets from MTG
Arena.

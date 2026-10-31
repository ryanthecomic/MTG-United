# Bridge protocol

The Forge bridge (`bridge/`) and the Unity client (`unity/`) talk over a local TCP socket
(`127.0.0.1:7777` by default). Every message is one JSON object on one line, ending in `\n`.
Every message has a `t` field with its type.

Forge identifies cards, players and stack items by integer ids. These ids stay the same for as
long as the object exists, so the client keys everything on them and sends them back when the
player clicks something.

## Bridge → client

| `t` | When | Fields |
|---|---|---|
| `hello` | On connect | `protocol` (1), `inGame` |
| `state` | After any change, at most every ~40 ms | `game`, `prompt` (see below) |
| `request` | Forge needs an answer before it can continue | `id`, `kind`, plus fields per kind |
| `gameStarted` | A new game began | `me` (your player id) |
| `gameOver` | The game ended | `winner` (player name or null) |
| `message` | Information or an error to show | `title`, `text`, `error` |
| `deckError` | `start` was rejected | `errors` (list of strings) |
| `flash` | The last click wasn't valid | none |
| `pong` | Reply to `ping` | none |

### `state.game`

```json
{
  "turn": 3, "phase": "MAIN1", "phaseLabel": "Main phase 1",
  "activePlayer": 1, "priorityPlayer": 1, "me": 1,
  "gameOver": false, "mulligan": false, "winner": null,
  "players": [ {
      "id": 1, "name": "Player", "isAI": false, "life": 40, "hasLost": false, "hasPriority": true,
      "landsPlayedThisTurn": 1,
      "counters": {"Poison": 0},
      "mana": {"W":0,"U":0,"B":0,"R":0,"G":0,"C":0},
      "commanderDamage": [ {"commanderId": 77, "commanderName": "...", "fromPlayer": 2, "damage": 6} ],
      "commanders": [ {"id": 12, "name": "...", "timesCast": 1} ],
      "zones": { "hand": [card], "battlefield": [card], "graveyard": [card], "exile": [card], "command": [card] },
      "zoneSizes": { "library": 88, "hand": 6, "graveyard": 2, "exile": 0 }
  } ],
  "stack": [ { "id": 5, "text": "...", "isAbility": false, "isTrigger": false, "sourceCard": 31,
               "source": card, "activatingPlayer": 2, "targetCards": [..], "targetPlayers": [..] } ],
  "combat": [ { "attacker": 40, "defender": {"kind": "player", "id": 1}, "blockers": [52] } ]
}
```

A **card** object has these fields: `id`, `hidden`, `name`, `oracleName`, `set`, `imageKey`,
`manaCost`, `typeLine`, `text`, `colors`, `owner`, `controller`, `zone`, `tapped`, `sick`,
`faceDown`, `token`, `commander`, `attacking`, `blocking`, `phasedOut`, `damage`, `doubleFaced`,
`counters`, `attachedTo`, `attachedToPlayer`, `isCreature`, `isLand`, `isPlaneswalker`,
`isInstant`, `isSorcery`, `isArtifact`, `isEnchantment` and `isBattle`. It also has `power` and
`toughness` (creatures and vehicles), `loyalty` (planeswalkers), `defense` (battles) and `altName`
(cards with another face). Cards in an opponent's hand only include `id` and `hidden: true`.

### `state.prompt`

This describes what Forge is waiting for. It works like Forge's own desktop UI: a message, two
buttons, and the objects that can be clicked.

```json
{
  "message": "Declare attackers: ...", "card": -1,
  "ok": {"label": "OK", "enabled": true, "focused": true},
  "cancel": {"label": "Alpha Strike", "enabled": true},
  "selection": {
    "cards": [ids], "cardData": [card], "min": 0, "max": 1,
    "targets": [ {"kind": "player"|"card"|"stack", "id": 3} ],
    "chosen":  [ {"kind": ..., "id": ...} ],
    "actionable": [ids]
  }
}
```

* `actionable`: cards you can play or activate right now. The client highlights them.
* `cards` and `targets`: valid choices for the current prompt, such as targets, attackers,
  blockers or cards to discard.
* `cardData`: full data for the selectable cards, so the client can show cards that are not
  otherwise visible, such as cards in your library.

### `request` kinds

| `kind` | Extra fields | Answer (`response`) |
|---|---|---|
| `choose` | `title`, `min`, `max`, `reveal`, `options[]` | `indices: [..]` |
| `order` | `title`, `subtitle`, `min`, `max`, `ordered`, `options[]` | `indices` in the chosen order (first = top) |
| `confirm` | `title`, `buttons[]`, `default`, optional `card` | `index` |
| `text` | `title`, `initial`, `numeric` | `text` (null = cancel) |
| `distribute` | `title`, `amount`, `atLeastOne`, `options[]` (each has a `max`) | `amounts: [..]` (same order, summing to `amount`) |

Each option has a `label`. It may also have `card` (a card object), `playerId` or `stackId`.
When `reveal` is true the dialog only shows the cards. Answer it with an empty `indices` list.

## Client → bridge

| `t` | Fields | Meaning |
|---|---|---|
| `start` | `deck` (decklist text), `commanders` (optional names), `name`, `opponents` (1-5), `aiDecks` (`precon` or `random`) | Start a new Commander game. Any running game is conceded first. |
| `ok` / `cancel` | none | Press the prompt's buttons |
| `card` | `id` | Click a card |
| `player` | `id` | Click a player |
| `stack` | `id` | Click a stack item, when it's a valid target |
| `mana` | `color` (W=1, U=2, B=4, R=8, G=16, C=0) | Spend floating mana |
| `passTurn` | none | Pass priority until end of turn |
| `alphaStrike` | none | Declare all possible attackers |
| `undo` | none | Undo the last undoable action |
| `concede` | none | Concede the current game |
| `stops` | `myTurn: [phase]`, `opponentTurn: [phase]` | Phases where you want priority when the stack is empty. Phase names are Forge's `PhaseType` names. |
| `response` | `id`, plus the answer fields | Answer a `request` |
| `ping` | none | Keep-alive |

## Formats and LAN play (rules engine)

`start` also takes:

- `format`: `"commander"` (default, 40 life, command zone), `"constructed"` (60-card decks, 20 life) or `"limited"` (40-card decks, 20 life). AI decks follow it: Commander precons / random commander decks, or Forge's preconstructed / theme / random color decks (cut to 40 cards for limited).
- `lan`: `true` to seat every player who joined the host's LAN table (see below) next to the host. `opponents` is then the number of AI players added.

LAN players (other PCs, no Forge needed) connect to the host's bridge on TCP port **7792** while the host has the table open:

| Direction | Message | Meaning |
|---|---|---|
| host → bridge | `{"t":"lanOpen","open":true}` | start / stop accepting LAN players |
| host → bridge | `{"t":"lanKick","id":3}` | remove a player from the table |
| bridge → peer | `{"t":"hello","protocol":1,"remote":true}` | sent on connect |
| peer → bridge | `{"t":"join","name":"Ana","deck":"...","deckName":"...","commanders":["..."]}` | take a seat |
| bridge → host & peers | `{"t":"lanLobby","open":true,"players":[{"id":1,"name":"Ana","deck":"..."}]}` | who is seated |
| bridge → peer | `{"t":"bye","text":"..."}` | removed, or a game was already running |

During the game each LAN player gets the same messages as the host (`state`, `request`, `gameStarted`...) for their own seat, and sends the same actions and `response`s. A player who disconnects mid-game concedes; the others play on. Tables are announced on UDP port 7791 as `MTGF|name|7792|people|max|started`.

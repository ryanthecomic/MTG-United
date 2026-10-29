"""
Smoke test for the Forge bridge, no Unity needed.

    python tools/smoke_test.py [--port 7777] [--deck path/to/deck.txt] [--seconds 120]

Connects to a running bridge, starts a Commander game (a random precon if no deck is given) and plays it on
autopilot: it clicks OK whenever it can and answers every question with the first allowed option. It prints
the turn, phase, life totals and prompt as the game goes, so you can see the engine and protocol working.
"""
import argparse
import json
import socket
import sys
import time


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=7777)
    ap.add_argument("--deck", help="decklist text file (Arena/Moxfield export)")
    ap.add_argument("--opponents", type=int, default=1)
    ap.add_argument("--seconds", type=int, default=120)
    args = ap.parse_args()

    deck = open(args.deck, encoding="utf-8").read() if args.deck else ""
    sock = socket.create_connection(("127.0.0.1", args.port))
    f = sock.makefile("rw", encoding="utf-8", newline="\n")

    def send(obj):
        f.write(json.dumps(obj) + "\n")
        f.flush()

    send({"t": "start", "deck": deck, "name": "Tester", "opponents": args.opponents, "aiDecks": "precon"})
    deadline = time.time() + args.seconds
    last_line = None
    last_ok_sent = 0.0

    for line in f:
        if time.time() > deadline:
            print("Time limit reached; smoke test OK.")
            break
        msg = json.loads(line)
        t = msg.get("t")
        if t == "state":
            g = msg["game"]
            prompt = msg["prompt"]
            lives = ", ".join(f"{p['name'].split(' (')[0]}={p['life']}" for p in g.get("players", []))
            summary = f"T{g.get('turn')} {g.get('phase'):<28} {lives} | {prompt['message'][:70]!r} [{prompt['ok']['label']}/{prompt['cancel']['label']}]"
            if summary != last_line:
                print(summary)
                last_line = summary
            if g.get("gameOver"):
                print("Game over, winner:", g.get("winner"))
                break
            # Autopilot: press OK when it is enabled, but not more than ~3 times a second.
            if prompt["ok"]["enabled"] and time.time() - last_ok_sent > 0.3:
                send({"t": "ok"})
                last_ok_sent = time.time()
        elif t == "request":
            kind = msg["kind"]
            print(f"  request #{msg['id']} {kind}: {msg.get('title')!r}")
            if kind in ("choose", "order"):
                n = len(msg.get("options", []))
                lo = max(msg.get("min", 0), 0)
                count = n if msg.get("ordered") and msg.get("max", 0) >= n else lo
                send({"t": "response", "id": msg["id"], "indices": list(range(min(count, n)))})
            elif kind == "confirm":
                send({"t": "response", "id": msg["id"], "index": msg.get("default", 0)})
            elif kind == "text":
                send({"t": "response", "id": msg["id"], "text": msg.get("initial", "0") or "0"})
            elif kind == "distribute":
                opts = msg["options"]
                amounts = [0] * len(opts)
                amounts[0] = msg["amount"]
                send({"t": "response", "id": msg["id"], "amounts": amounts})
        elif t == "deckError":
            print("Deck error:", msg["errors"])
            sys.exit(1)
        elif t in ("message", "gameStarted", "gameOver", "hello"):
            print(f"<{t}>", {k: v for k, v in msg.items() if k != "t"})
            if t == "gameOver":
                break
    sock.close()


if __name__ == "__main__":
    main()

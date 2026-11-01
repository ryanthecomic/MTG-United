"""Renders the README guide images (docs/images/) from HTML with Playwright (pip install playwright).

Put next to this script: title.ttf (Resources/Visuals/Fonts/NightGeorgia), jodah.jpg, forge.jpg and trivia.jpg
(the art in Resources/Visuals/Images). Output goes to ./out; banner and modes are saved as PNG (convert to JPG).
"""
import asyncio, html, os, sys
from playwright.async_api import async_playwright

W = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(W, "out")

CSS = """
@font-face { font-family: Title; src: url('title.ttf'); }
:root { --bg:#0b0b0d; --panel:#141416; --line:rgba(255,255,255,.09); --text:#efeff1; --dim:#a7a7ae;
        --accent:#f99e2e; --accent2:#ffb35c; --blue:#3fc0ff; --cream:#ede6d6; }
* { box-sizing:border-box; margin:0; padding:0; }
body { background:var(--bg); color:var(--text); font-family:Inter, 'DejaVu Sans', sans-serif; -webkit-font-smoothing:antialiased; }
.frame { width:1600px; position:relative; overflow:hidden; background:linear-gradient(180deg,#151517,#0a0a0b); }
.title { font-family:Title, Georgia, serif; color:var(--cream); letter-spacing:.5px; }
.kicker { font-weight:700; font-size:15px; letter-spacing:3px; color:var(--accent); text-transform:uppercase; }
.band { border-top:1.5px solid rgba(255,217,153,.28); border-bottom:1.5px solid rgba(255,217,153,.28); }
.hair { height:1.5px; background:rgba(255,255,255,.12); }
"""

def page(body, height=None):
    h = f"height:{height}px;" if height else ""
    return f"<!doctype html><html><head><meta charset='utf-8'><style>{CSS}</style></head><body><div class='frame' style='{h}'>{body}</div></body></html>"

# ------------------------------------------------------------------ 1. banner
BANNER = page("""
<div style="position:absolute;inset:0;background:url('jodah.jpg') center 22%/cover;filter:brightness(.62)"></div>
<div style="position:absolute;inset:0;background:linear-gradient(90deg,rgba(8,8,10,.96) 0%,rgba(8,8,10,.86) 38%,rgba(8,8,10,.15) 75%,rgba(8,8,10,.35) 100%)"></div>
<div class="band" style="position:absolute;left:0;right:0;top:0;bottom:0"></div>
<div style="position:absolute;left:90px;top:96px;width:820px">
  <div class="kicker">Forge rules engine · Arena-style client</div>
  <div class="title" style="font-size:104px;line-height:1.05;margin-top:18px;color:#f6c77e;text-shadow:0 4px 30px rgba(0,0,0,.6)">MTG UNITED</div>
  <div style="font-size:26px;line-height:1.45;color:#e2e2e6;margin-top:22px;max-width:760px">
    Commander pods against Forge's AI, LAN tables with friends, and a free Kitchen Table for your own custom sets.</div>
  <div style="display:flex;gap:12px;margin-top:34px">
    <span style="padding:10px 18px;border-radius:30px;background:linear-gradient(180deg,#ffa03a,#e2650f);font-weight:700;font-size:17px">Windows · free · open source</span>
    <span style="padding:10px 18px;border-radius:30px;border:1.4px solid rgba(255,255,255,.45);font-size:17px;color:#e8e8ec">PT-BR · EN · ES</span>
  </div>
</div>
<div style="position:absolute;right:28px;bottom:18px;font-size:13px;color:rgba(255,255,255,.55)">Art: <i>Jodah, the Unifier</i> by Ryan Pancoast</div>
""", 560)

# ------------------------------------------------------------------ 2. how it works
def box(x, y, w, h, kicker, title, lines, color="var(--accent)", extra=""):
    lis = "".join(f"<li>{html.escape(l)}</li>" for l in lines)
    return f"""<div style="position:absolute;left:{x}px;top:{y}px;width:{w}px;height:{h}px;border-radius:18px;background:#18181b;
      border:1.5px solid {color};box-shadow:0 0 0 6px rgba(0,0,0,.25),0 18px 40px rgba(0,0,0,.45);padding:24px 28px;{extra}">
      <div class="kicker" style="color:{color}">{kicker}</div>
      <div class="title" style="font-size:36px;margin:8px 0 12px">{title}</div>
      <ul style="font-size:17px;line-height:1.6;color:#cfcfd5;padding-left:20px">{lis}</ul></div>"""

def arrow(x1, y, x2, label, sub="", color="#f99e2e", both=True):
    w = x2 - x1
    heads = f"""<div style="position:absolute;right:-2px;top:-7px;border-left:14px solid {color};border-top:8px solid transparent;border-bottom:8px solid transparent"></div>"""
    if both:
        heads += f"""<div style="position:absolute;left:-2px;top:-7px;border-right:14px solid {color};border-top:8px solid transparent;border-bottom:8px solid transparent"></div>"""
    return f"""<div style="position:absolute;left:{x1}px;top:{y}px;width:{w}px;height:2.5px;background:{color}">{heads}
      <div style="position:absolute;left:0;right:0;top:-34px;text-align:center;font-size:15px;font-weight:700;color:{color}">{label}</div>
      <div style="position:absolute;left:0;right:0;top:12px;text-align:center;font-size:13px;color:var(--dim)">{sub}</div></div>"""

HOW = page(f"""
<div style="position:absolute;left:70px;top:46px"><div class="kicker">How it works</div>
<div class="title" style="font-size:46px;margin-top:6px">Two programs, one table</div></div>
{box(70, 170, 470, 240, "Unity · what you see", "MTG United", ["Board, hand, stack, animations, sound", "Menus, deck builder, Kitchen Table", "Starts the bridge for you"])}
{box(1060, 170, 470, 240, "Java · the rules", "Forge bridge", ["Runs Forge with no window", "Forge's AI plays the opponents", "Adapts to any Forge version"], "#3fc0ff")}
{arrow(560, 290, 1040, "JSON over TCP · port 7777", "state & prompts → / ← your clicks")}
<div style="position:absolute;left:70px;top:450px;right:70px" class="hair"></div>
<div style="position:absolute;left:70px;top:476px;width:700px;font-size:17px;line-height:1.6;color:#cfcfd5">
  <b style="color:#3fc0ff">LAN, rules engine:</b> friends' MTG United joins the host's bridge (port 7792). Only the host needs Forge.</div>
<div style="position:absolute;left:830px;top:476px;width:700px;font-size:17px;line-height:1.6;color:#cfcfd5">
  <b style="color:var(--accent)">LAN, Kitchen Table:</b> MTG United to MTG United (port 7790). No Forge at all; custom sets are shared automatically.</div>
""", 560)

# ------------------------------------------------------------------ 3. getting started
def step(n, x, title, body, tip=""):
    return f"""<div style="position:absolute;left:{x}px;top:150px;width:345px;height:290px;border-radius:18px;background:#18181b;
      border:1px solid var(--line);padding:26px 26px">
      <div style="width:46px;height:46px;border-radius:50%;background:linear-gradient(180deg,#ffa03a,#e2650f);display:flex;align-items:center;justify-content:center;
           font-family:Title;font-size:26px;color:#1a0d02">{n}</div>
      <div class="title" style="font-size:28px;margin:18px 0 10px">{title}</div>
      <div style="font-size:16.5px;line-height:1.55;color:#cfcfd5">{body}</div>
      <div style="position:absolute;left:26px;right:26px;bottom:22px;font-size:14px;color:var(--accent2)">{tip}</div></div>"""

START = page(f"""
<div style="position:absolute;left:70px;top:46px"><div class="kicker">Getting started</div>
<div class="title" style="font-size:46px;margin-top:6px">From download to your first game</div></div>
{step(1, 70, "Get Java", "Forge needs Java 17 or newer. In the <b>Forge</b> tab press <b>Download Java</b> (or Install Java).", "Restart MTG United after installing.")}
{step(2, 440, "Set up Forge", "<b>Install Forge</b> builds the newest Forge for you. Already have Forge? <b>Use my Forge…</b>", "Any Forge version works.")}
{step(3, 810, "Pick a deck", "<b>Decks</b> tab: build one, or import from MTGO, Moxfield, Archidekt or plain text.", "Hold Alt over a card for the big view.")}
{step(4, 1180, "Play", "Press <b>Play</b>: choose the mode, players and AI skill in the side panel, then <b>Play</b> again.", "Double-click a deck to start right away.")}
""", 490)

# ------------------------------------------------------------------ 4. modes
def mode(x, img, pos, kicker, color, title, lines, credit):
    lis = "".join(f"<li>{l}</li>" for l in lines)
    return f"""<div style="position:absolute;left:{x}px;top:150px;width:715px;height:500px;border-radius:20px;overflow:hidden;background:#141416;border:1px solid var(--line)">
      <div style="height:280px;background:url('{img}') {pos}/cover;position:relative">
        <div style="position:absolute;inset:0;background:linear-gradient(180deg,rgba(0,0,0,.05),rgba(20,20,22,1))"></div>
        <div style="position:absolute;right:14px;top:12px;font-size:12px;color:rgba(255,255,255,.7);text-shadow:0 1px 4px #000">{credit}</div></div>
      <div style="padding:6px 34px 30px;margin-top:-40px;position:relative">
        <div class="kicker" style="color:{color}">{kicker}</div>
        <div class="title" style="font-size:40px;margin:6px 0 14px">{title}</div>
        <ul style="font-size:18px;line-height:1.65;color:#d2d2d8;padding-left:22px">{lis}</ul></div></div>"""

MODES = page(f"""
<div style="position:absolute;left:70px;top:46px"><div class="kicker">Two ways to play</div>
<div class="title" style="font-size:46px;margin-top:6px">Full rules, or a free table</div></div>
{mode(70, "forge.jpg", "center 35%", "Rules engine", "var(--accent)", "Forge games",
      ["Commander with every rule enforced by Forge", "Up to 5 AI opponents · Fast, Normal or Strong", "Mix friends and AIs on your network", "Playable zones glow; your castable commander lights up"],
      "Art: <i>Urabrask's Forge</i> by Lie Setiawan")}
{mode(815, "trivia.jpg", "center 22%", "Kitchen Table", "#4be0b0", "Your own cards",
      ["Like Cockatrice: you move the cards, nothing is enforced", "Custom sets from Magic Set Editor, tokens, double faces", "Notes, counters, multi-select, Cockatrice-style keys", "LAN with friends · no Forge needed"],
      "Art: <i>Trivia Contest</i> by Caroline Gariba")}
""", 700)

# ------------------------------------------------------------------ 5. shortcuts
def key(k):
    parts = [p.strip() for p in k.replace(" + ", "+").split("+")] if "+" in k and "/" not in k and len(k) < 14 else [k]
    caps = "<span style='color:var(--dim);margin:0 4px'>+</span>".join(
        f"<span style='display:inline-block;min-width:34px;padding:5px 10px;border-radius:8px;background:linear-gradient(180deg,#2c2c30,#1e1e21);"
        f"border:1px solid rgba(255,255,255,.16);border-bottom-width:3px;font-weight:700;font-size:15px;text-align:center;color:#f2f2f4'>{html.escape(p)}</span>"
        for p in parts)
    return caps

def sheet(title, kicker, groups, cols=2, height=900):
    colw = (1600 - 140 - (cols - 1) * 40) // cols
    cols_html = []
    for col in groups:
        rows = []
        for item in col:
            if item[0].endswith(":"):
                rows.append(f"<div class='kicker' style='margin:18px 0 8px;font-size:13px'>{html.escape(item[0][:-1])}</div>")
                continue
            rows.append(f"<div style='display:flex;align-items:center;gap:16px;padding:7px 0;border-bottom:1px solid rgba(255,255,255,.05)'>"
                        f"<div style='width:175px;flex:none'>{key(item[0])}</div><div style='font-size:16.5px;color:#d6d6dc'>{html.escape(item[1])}</div></div>")
        cols_html.append(f"<div style='width:{colw}px'>{''.join(rows)}</div>")
    return page(f"""<div style="position:absolute;left:70px;top:46px"><div class="kicker">{kicker}</div>
      <div class="title" style="font-size:46px;margin-top:6px">{title}</div></div>
      <div style="position:absolute;left:70px;top:150px;display:flex;gap:40px">{''.join(cols_html)}</div>
      <div style="position:absolute;right:40px;bottom:22px;font-size:14px;color:var(--dim)">In the game: F1, or Options → Shortcuts</div>""", height)

FORGE_KEYS = sheet("Rules engine shortcuts", "Keyboard", [
    [("Game:", ""), ("Space / Enter", "Pass, OK, Resolve"), ("Esc", "Cancel / open the menu"), ("Ctrl + Z", "Undo"),
     ("E", "End turn"), ("A", "Attack with everything"), ("Hold Alt", "Big view of the card under the mouse")],
    [("Boards:", ""), ("1 – 5", "Focus that opponent's board"), ("Tab", "Focus the next opponent"), ("F", "Focus your own board"),
     ("` or 0", "Show every board again"), ("G / X / C", "Graveyard / exile / commander"), ("R", "Cards revealed this game")],
], height=560)

TABLE_KEYS = sheet("Kitchen Table shortcuts", "Keyboard", [
    [("Table (mouse over empty table):", ""), ("C", "Draw a card"), ("X", "Untap all"), ("V", "Shuffle your library"), ("M", "Mulligan"),
     ("N", "Next turn"), ("G", "Create a token"), ("R", "Dice, coin, random player"), ("Ctrl + Z", "Undo"),
     ("Selecting:", ""), ("Shift + click", "Add a card to the selection"), ("Drag the table", "Select every card in the box"),
     ("Right-click", "Every action for a card or pile")],
    [("Cards (under the mouse, or selected):", ""), ("Space", "Tap / untap"), ("Q / E", "Turn left / right"), ("F", "Transform / face down"),
     ("U / I", "+1/+1 / −1/−1 counter"), ("O / Shift+O", "Add / remove a counter"), ("Z", "Summoning sick"), ("K", "Copy (token)"),
     ("H / D / S", "To hand / graveyard / exile"), ("T / Y", "Top / bottom of the deck"), ("Shift+N", "Note on the card"),
     ("Hold Alt", "Big view of the card")],
], height=850)

JOBS = [("banner", BANNER), ("how-it-works", HOW), ("getting-started", START), ("modes", MODES),
        ("shortcuts-rules-engine", FORGE_KEYS), ("shortcuts-kitchen-table", TABLE_KEYS)]

async def main():
    async with async_playwright() as p:
        b = await p.chromium.launch()
        pg = await b.new_page(viewport={"width": 1600, "height": 1000}, device_scale_factor=1)
        for name, doc in JOBS:
            path = os.path.join(W, f"_{name}.html")
            open(path, "w", encoding="utf-8").write(doc)
            await pg.goto("file://" + path)
            await pg.wait_for_timeout(300)
            await pg.locator(".frame").screenshot(path=os.path.join(OUT, f"{name}.png"))
            print("wrote", name)
        await b.close()

asyncio.run(main())

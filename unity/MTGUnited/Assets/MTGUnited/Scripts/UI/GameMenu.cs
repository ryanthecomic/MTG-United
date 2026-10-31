using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Every keyboard shortcut, shown on Options > Shortcuts and handled by GameController.</summary>
    public static class Shortcuts
    {
        /// <summary>One list per mode, each its own tab in Options > Shortcuts.</summary>
        public static readonly (string tab, (string key, string what)[] list)[] Tabs =
        {
            ("FORGE GAME", new[]
            {
                ("Space / Enter", "Pass, OK, Resolve (main button)"),
                ("Esc", "Cancel what Forge asks / open the menu"),
                ("Ctrl + Z", "Undo"),
                ("E", "End turn (pass until your next turn)"),
                ("A", "Attack with everything"),
                ("1 – 5", "Focus that opponent's board"),
                ("Tab", "Focus the next opponent"),
                ("F", "Focus your own board (or scroll up over it)"),
                ("` or 0", "Show every board again"),
                ("G / X / C", "Your graveyard / exile / commander"),
                ("R", "Cards revealed this game"),
            }),
            ("KITCHEN TABLE", new[]
            {
                ("Table:", "(when the mouse isn't over a card or pile)"),
                ("C", "Draw a card"),
                ("X", "Untap all your permanents"),
                ("V", "Shuffle your library"),
                ("M", "Mulligan (new seven)"),
                ("N", "Next turn"),
                ("G", "Create a token"),
                ("R", "Dice, coin and random player"),
                ("Ctrl + Z", "Undo the last action"),
                ("Esc", "Clear the selection / open the menu"),
                ("Cards:", "(the card under the mouse, or every selected card)"),
                ("Space", "Tap / untap"),
                ("Z", "Summoning sick (clears on its controller's turn)"),
                ("F", "Flip: transform a double-faced card, or face down / up"),
                ("Q / E", "Turn left / right"),
                ("U / I", "+1/+1 / −1/−1 counter (they cancel out)"),
                ("O / Shift+O", "Add / remove a counter"),
                ("K", "Copy it (token)"),
                ("Backspace", "Delete it from the game"),
                ("P", "Play it"),
                ("H / D / S", "To hand / discard (graveyard) / exile"),
                ("T / Y", "To the top / bottom of its deck"),
                ("B", "To the command zone"),
                ("Shift+N", "Add or edit a note"),
                ("W", "Reveal it (from your hand)"),
                ("Hold Alt", "Zoom in on it"),
                ("Piles:", "(the mouse over your library, graveyard, exile...)"),
                ("H P D S T Y B", "Move the pile's top card (like a card)"),
                ("V / M", "Over the library: shuffle / mulligan"),
                ("Selecting:", ""),
                ("Shift + click", "Add a card to the selection"),
                ("Drag the table", "Select every card in the box"),
                ("Drag a selected card", "Moves the whole selection"),
                ("Other:", ""),
                ("Click / Dbl-click", "Tap / play a card from your hand"),
                ("Right-click", "Every action for that card or pile"),
                ("Drag a pile", "Take its top card"),
                ("Ctrl + click", "A seat's nameplate: look from their side (alone)"),
            }),
            ("DECK BUILDER", new[]
            {
                ("Click", "Add a card (all cards) / remove one copy (your deck)"),
                ("Drag", "To the list: add  ·  out of the deck: remove one"),
                ("Drag to art", "Make that card the deck's cover"),
                ("Right-click", "Zoom in on the card"),
                ("Ctrl + S", "Save without closing"),
                ("Esc", "Close the editor (asks to save changes)"),
            }),
            ("EVERYWHERE", new[]
            {
                ("M", "Mute / unmute sound"),
                ("F1", "These shortcuts"),
                ("Esc", "Close the top pop-up"),
            }),
        };
    }

    /// <summary>
    /// In-game menu: opens in the middle of the screen over a darkened board (gear button or Esc).
    /// </summary>
    public class GameMenu : MonoBehaviour
    {
        GameController gc;
        RectTransform panel;

        public bool IsOpen => gameObject.activeSelf;

        public static GameMenu Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("GameMenu", parent, new Color(0f, 0f, 0f, 0.68f), false);
            shade.rectTransform.Stretch();
            var m = shade.gameObject.AddComponent<GameMenu>();
            m.gc = gc;
            shade.gameObject.AddComponent<PointerEvents>().OnClick = m.Close;

            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.08f, 0.12f, 0.97f));
            m.panel = p.rectTransform;
            m.panel.anchorMin = m.panel.anchorMax = new Vector2(0.5f, 0.5f);
            m.panel.sizeDelta = new Vector2(460, 540);
            p.gameObject.AddComponent<PointerEvents>();   // clicks on the panel don't close it
            var rim = p.gameObject.AddComponent<UnityEngine.UI.Outline>();
            rim.effectColor = new Color(0.95f, 0.7f, 0.35f, 0.45f); rim.effectDistance = new Vector2(1.5f, -1.5f);
            UiScaled.Add(p, Vector2.zero);

            var title = UIKit.Label("Title", p.transform, "MENU", 30, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            title.rectTransform.Stretch(20, 0, 20, 22);

            float y = 540 - 130;
            void Item(string label, Color color, UnityEngine.Events.UnityAction a)
            {
                var b = UIKit.Button(label, p.transform, label, color, a, 20);
                ((RectTransform)b.transform).Place(50, y, 360, 56);
                y -= 66;
            }
            Item("Resume", Theme.Accent, m.Close);
            Item("Options", Theme.Secondary, () => { m.Close(); gc.Options.Open(false); });
            Item("Shortcuts", Theme.Secondary, () => { m.Close(); gc.Options.Open(true); });
            Item("Attack with everything", Theme.Secondary, () => { m.Close(); if (!gc.Table.IsOpen) gc.Send("alphaStrike"); });
            Item("Concede", new Color(0.45f, 0.14f, 0.14f), () => { m.Close(); if (gc.Table.IsOpen) gc.Table.Leave(); else gc.Send("concede"); });
            Item("Back to main menu", Theme.Secondary, () =>
            {
                m.Close();
                if (gc.Table.IsOpen) gc.Table.Leave();
                else if (gc.RemoteBridge) gc.LeaveForgeLan();   // leaving someone's LAN table
                else gc.Menu.Show();
            });
            UIKit.CloseX(p.transform, m.Close);
            shade.gameObject.SetActive(false);
            return m;
        }

        public void Toggle() { if (IsOpen) Close(); else Open(); }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Short, 1f);
            FadeIn.Play(panel.gameObject, Motion.Short, 0.94f);
        }

        public void Close() => gameObject.SetActive(false);
    }
}

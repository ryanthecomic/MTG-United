using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>Every keyboard shortcut, shown on Options > Shortcuts and handled by GameController.</summary>
    public static class Shortcuts
    {
        public static readonly (string key, string what)[] List =
        {
            ("Space / Enter", "Pass, OK, Resolve (main button)"),
            ("Esc", "Cancel / close pop-ups / open the menu"),
            ("Ctrl + Z", "Undo"),
            ("E", "End turn (pass until your next turn)"),
            ("A", "Attack with everything"),
            ("1 – 5", "Focus that opponent's board"),
            ("Tab", "Focus the next opponent"),
            ("` or 0", "Show every board again"),
            ("G / X / C", "Your graveyard / exile / commander"),
            ("R", "Cards revealed this game"),
            ("M", "Mute / unmute sound"),
            ("F1", "This list"),
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
            Item("Attack with everything", Theme.Secondary, () => { m.Close(); gc.Send("alphaStrike"); });
            Item("Concede", new Color(0.45f, 0.14f, 0.14f), () => { m.Close(); gc.Send("concede"); });
            Item("Back to main menu", Theme.Secondary, () => { m.Close(); gc.Menu.Show(); });
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

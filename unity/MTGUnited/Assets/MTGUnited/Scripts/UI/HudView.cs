using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Everything that isn't a card: the prompt, the two action buttons, the phase track (click a phase to set a stop),
    /// the stack, the turn banner and the in-game menu.
    /// </summary>
    public class HudView : MonoBehaviour
    {
        GameController gc;
        Text promptText, turnText;
        Button primary, secondary, endTurn, menuButton;
        Image promptBg, stackBg;
        RectTransform stackList;
        readonly List<GameObject> stackRows = new List<GameObject>();
        readonly List<(string phase, Image pill, Text label, Image myDot, Image oppDot)> phasePills = new List<(string, Image, Text, Image, Image)>();
        RectTransform ingameMenu;
        float flash;
        GameState state;

        public readonly HashSet<string> MyStops = new HashSet<string>();
        public readonly HashSet<string> OppStops = new HashSet<string>();

        static readonly (string id, string label)[] Phases =
        {
            ("UNTAP", "Untap"), ("UPKEEP", "Upkeep"), ("DRAW", "Draw"), ("MAIN1", "Main 1"),
            ("COMBAT_BEGIN", "Combat"), ("COMBAT_DECLARE_ATTACKERS", "Attack"), ("COMBAT_DECLARE_BLOCKERS", "Block"),
            ("COMBAT_DAMAGE", "Damage"), ("COMBAT_END", "End cbt"), ("MAIN2", "Main 2"), ("END_OF_TURN", "End"), ("CLEANUP", "Cleanup"),
        };

        public static HudView Create(RectTransform board, GameController gc)
        {
            var rt = UIKit.Rect("Hud", board).Stretch();
            var h = rt.gameObject.AddComponent<HudView>();
            h.gc = gc;
            h.LoadStops();
            h.Build(rt);
            return h;
        }

        void LoadStops()
        {
            string my = PlayerPrefs.GetString("stopsMine", "MAIN1,COMBAT_DECLARE_ATTACKERS,MAIN2");
            string opp = PlayerPrefs.GetString("stopsOpp", "COMBAT_DECLARE_BLOCKERS,END_OF_TURN");
            foreach (var s in my.Split(',')) if (s.Length > 0) MyStops.Add(s);
            foreach (var s in opp.Split(',')) if (s.Length > 0) OppStops.Add(s);
        }

        void SaveStops()
        {
            PlayerPrefs.SetString("stopsMine", string.Join(",", MyStops));
            PlayerPrefs.SetString("stopsOpp", string.Join(",", OppStops));
            gc.SendStops();
        }

        void Build(RectTransform rt)
        {
            // Prompt + buttons, bottom right.
            promptBg = UIKit.Panel("Prompt", rt, Theme.Panel);
            promptBg.rectTransform.Place(1590, 230, 310, 130);
            promptBg.raycastTarget = false;
            promptText = UIKit.Label("Text", promptBg.transform, "", 17, Theme.Text, TextAnchor.MiddleCenter);
            promptText.rectTransform.Stretch(12, 8, 12, 8);
            promptText.resizeTextForBestFit = true; promptText.resizeTextMinSize = 10; promptText.resizeTextMaxSize = 18;

            secondary = UIKit.Button("Secondary", rt, "", Theme.Secondary, PressSecondary, 20);
            ((RectTransform)secondary.transform).Place(1590, 165, 310, 54);

            primary = UIKit.Button("Primary", rt, "", Theme.Accent, PressPrimary, 28);
            ((RectTransform)primary.transform).Place(1590, 80, 310, 76);

            endTurn = UIKit.Button("EndTurn", rt, "End Turn", new Color(0.35f, 0.22f, 0.22f), () => gc.Send("passTurn"), 16);
            ((RectTransform)endTurn.transform).Place(1590, 20, 150, 48);
            var undo = UIKit.Button("Undo", rt, "Undo", Theme.Secondary, () => gc.Send("undo"), 16);
            ((RectTransform)undo.transform).Place(1750, 20, 150, 48);

            // Turn banner + phase track across the middle of the board.
            turnText = UIKit.Label("Turn", rt, "", 18, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold).Titled();
            turnText.rectTransform.Place(24, 556, 250, 36);
            float x = 280;
            float w = 1140f / Phases.Length;
            foreach (var (id, label) in Phases)
            {
                var pill = UIKit.Panel("Phase " + id, rt, new Color(1, 1, 1, 0.06f));
                pill.rectTransform.Place(x + 2, 558, w - 4, 32);
                var t = UIKit.Label("Label", pill.transform, label, 13, Theme.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
                t.rectTransform.Stretch();
                // Small dots: left = I stop here on my turn, right = I stop here on opponents' turns.
                var myDot = UIKit.Panel("MyStop", pill.transform, Theme.Accent);
                myDot.rectTransform.anchorMin = myDot.rectTransform.anchorMax = new Vector2(0, 1);
                myDot.rectTransform.sizeDelta = new Vector2(8, 8); myDot.rectTransform.anchoredPosition = new Vector2(7, -7);
                myDot.raycastTarget = false;
                var oppDot = UIKit.Panel("OppStop", pill.transform, Theme.Playable);
                oppDot.rectTransform.anchorMin = oppDot.rectTransform.anchorMax = new Vector2(1, 1);
                oppDot.rectTransform.sizeDelta = new Vector2(8, 8); oppDot.rectTransform.anchoredPosition = new Vector2(-7, -7);
                oppDot.raycastTarget = false;
                string phaseId = id;
                var pe = pill.gameObject.AddComponent<PointerEvents>();
                pe.OnClick = () => { Toggle(MyStops, phaseId); };
                pe.OnRightClick = () => { Toggle(OppStops, phaseId); };
                phasePills.Add((id, pill, t, myDot, oppDot));
                x += w;
            }
            var hint = UIKit.Label("StopsHint", rt, "Phase stops: click = your turn (orange), right-click = opponents' turns (blue)", 11, new Color(1, 1, 1, 0.3f), TextAnchor.MiddleCenter);
            hint.rectTransform.Place(280, 592, 1140, 14);

            // Stack, right side above the prompt.
            stackBg = UIKit.Panel("Stack", rt, new Color(0.08f, 0.07f, 0.12f, 0.95f));
            stackBg.rectTransform.Place(1590, 372, 310, 220);
            var title = UIKit.Label("Title", stackBg.transform, "STACK", 14, Theme.Accent, TextAnchor.UpperCenter, FontStyle.Bold).Titled();
            title.rectTransform.Stretch(0, 0, 0, 6);
            stackList = UIKit.Rect("List", stackBg.transform).Stretch(8, 8, 8, 28);
            stackBg.gameObject.SetActive(false);

            // In-game menu (top right corner of the screen).
            menuButton = UIKit.Button("Menu", rt, "≡", Theme.Secondary, ToggleMenu, 26);
            ((RectTransform)menuButton.transform).Place(1862, 1022, 50, 50);
            menuButton.transform.SetAsLastSibling();
            ingameMenu = UIKit.Panel("IngameMenu", rt, Theme.PanelLight).rectTransform;
            ingameMenu.Place(1640, 790, 270, 226);
            MenuButton("Concede", 0, () => { gc.Send("concede"); ToggleMenu(); });
            MenuButton("Alpha strike (attack with all)", 1, () => { gc.Send("alphaStrike"); ToggleMenu(); });
            var autoPay = MenuButton(AutoPayLabel(), 2, null);
            autoPay.onClick.AddListener(() =>
            {
                gc.AutoPayMana = !gc.AutoPayMana;
                PlayerPrefs.SetInt("autoPay", gc.AutoPayMana ? 1 : 0);
                UIKit.LabelOf(autoPay).text = AutoPayLabel();
            });
            MenuButton("Back to main menu", 3, () => { ToggleMenu(); gc.Menu.Show(); });
            ingameMenu.gameObject.SetActive(false);
        }

        string AutoPayLabel() => "Auto-pay mana: " + (gc.AutoPayMana ? "ON" : "OFF");

        Button MenuButton(string label, int row, UnityEngine.Events.UnityAction onClick)
        {
            var b = UIKit.Button(label, ingameMenu, label, Theme.Secondary, onClick, 16);
            ((RectTransform)b.transform).Place(10, 226 - 10 - (row + 1) * 52, 250, 46);
            return b;
        }

        void ToggleMenu()
        {
            ingameMenu.gameObject.SetActive(!ingameMenu.gameObject.activeSelf);
            ingameMenu.SetAsLastSibling();
            menuButton.transform.SetAsLastSibling();
        }

        void Toggle(HashSet<string> set, string phase)
        {
            if (!set.Add(phase)) set.Remove(phase);
            SaveStops();
            RenderPhases();
        }

        public void PressPrimary()
        {
            if (state != null && state.Prompt.OkEnabled) gc.Send("ok");
        }

        public void PressSecondary()
        {
            if (state != null && state.Prompt.CancelEnabled) gc.Send("cancel");
        }

        public void Flash() => flash = 1f;

        public void Render(GameState s)
        {
            state = s;
            var p = s.Prompt;
            promptText.text = string.IsNullOrEmpty(p.Message) ? "" : p.Message.Trim();

            primary.SetVisible(p.OkEnabled || !string.IsNullOrEmpty(p.OkLabel));
            primary.interactable = p.OkEnabled;
            UIKit.LabelOf(primary).text = FriendlyOk(p.OkLabel, s);
            primary.GetComponent<Image>().color = p.OkEnabled ? Theme.Accent : Theme.AccentDim;

            secondary.SetVisible(p.CancelEnabled);
            secondary.interactable = p.CancelEnabled;
            UIKit.LabelOf(secondary).text = p.CancelLabel;

            bool myPriority = s.PriorityPlayer == s.Me;
            endTurn.interactable = myPriority && !s.Mulligan;

            var active = s.Player(s.ActivePlayer);
            turnText.text = "Turn " + s.Turn + " · " + (s.ActivePlayer == s.Me ? "Your turn" : (active?.ShortName ?? "") + "'s turn");
            RenderPhases();
            RenderStack(s);
        }

        /// <summary>Forge's generic "OK" reads better as "Pass" when it just passes priority.</summary>
        static string FriendlyOk(string label, GameState s)
        {
            if (label == "OK" && s.Prompt.SelMax == 0 && s.Prompt.TargetPlayers.Count == 0
                && s.PriorityPlayer == s.Me && !s.Mulligan)
                return s.Stack.Count > 0 ? "Resolve" : "Pass";
            return label;
        }

        void RenderPhases()
        {
            string current = state?.Phase ?? "";
            bool myTurn = state != null && state.ActivePlayer == state.Me;
            foreach (var (id, pill, label, myDot, oppDot) in phasePills)
            {
                bool on = id == current;
                pill.color = on ? (myTurn ? new Color(0.98f, 0.62f, 0.18f, 0.85f) : new Color(0.25f, 0.6f, 0.95f, 0.75f)) : new Color(1, 1, 1, 0.06f);
                label.color = on ? Color.black : Theme.TextDim;
                myDot.SetVisible(MyStops.Contains(id));
                oppDot.SetVisible(OppStops.Contains(id));
            }
        }

        void RenderStack(GameState s)
        {
            stackBg.SetVisible(s.Stack.Count > 0);
            foreach (var go in stackRows) Destroy(go);
            stackRows.Clear();
            float rowH = Mathf.Min(62, 184f / Mathf.Max(1, s.Stack.Count));
            for (int i = 0; i < s.Stack.Count; i++)
            {
                var item = s.Stack[i];
                bool targetable = s.Prompt.TargetStack.Contains(item.Id);
                var row = UIKit.Panel("Item", stackList, targetable ? new Color(0.45f, 0.38f, 0.1f, 0.95f) : Theme.PanelLight);
                var r = row.rectTransform;
                r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
                r.sizeDelta = new Vector2(0, rowH - 4); r.anchoredPosition = new Vector2(0, -i * rowH);
                var art = UIKit.Rect("Art", row.transform);
                art.anchorMin = new Vector2(0, 0); art.anchorMax = new Vector2(0, 1); art.pivot = new Vector2(0, 0.5f);
                art.sizeDelta = new Vector2((rowH - 8) * 0.714f, -6); art.anchoredPosition = new Vector2(4, 0);
                var raw = art.gameObject.AddComponent<RawImage>();
                raw.color = new Color(1, 1, 1, 0);
                raw.raycastTarget = false;
                if (item.Source != null && !item.Source.Hidden && CardImageCache.Instance != null)
                    CardImageCache.Instance.Get(item.Source.Name, item.Source.Set, tex => { if (raw != null) { raw.texture = tex; raw.color = Color.white; } });
                string who = s.Player(item.ActivatingPlayer)?.ShortName ?? "";
                var txt = UIKit.Label("Text", row.transform, "<b>" + who + ":</b> " + item.Text, 12, Theme.Text, TextAnchor.MiddleLeft);
                txt.rectTransform.Stretch((rowH - 8) * 0.714f + 10, 2, 6, 2);
                txt.resizeTextForBestFit = true; txt.resizeTextMinSize = 8; txt.resizeTextMaxSize = 13;
                var pe = row.gameObject.AddComponent<PointerEvents>();
                int id = item.Id;
                var src = item.Source;
                pe.OnClick = () => gc.ClickStack(id);
                pe.OnEnter = () => { if (src != null) gc.Preview.Show(src, false); };
                pe.OnExit = () => gc.Preview.Hide();
                stackRows.Add(row.gameObject);
            }
        }

        void Update()
        {
            if (flash > 0)
            {
                flash = Mathf.Max(0, flash - Time.unscaledDeltaTime * 2.5f);
                promptBg.color = Color.Lerp(Theme.Panel, Theme.Danger, flash);
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// LAN play with the rules engine (Forge): people and AIs at one table.
    ///  • HOST: your bridge opens to the network (port 7792) and your table is announced. Friends join from their PCs
    ///    (they don't need Forge installed); you pick how many AI opponents fill the other seats, then Start.
    ///  • JOIN: tables found on the network (or type the host's address). You play with the deck selected on Home.
    /// The game uses the host's format (Commander, 60 or 40 cards) and the host's Forge for the rules.
    /// </summary>
    public class ForgeLan : MonoBehaviour
    {
        public static ForgeLan Instance { get; private set; }
        GameController gc;
        RectTransform hostView, joinView, players, hosts;
        Text status, address, botsText, info;
        Image hostTab, joinTab;
        Button startButton, openButton;
        InputField ipInput;
        bool joinMode, hosting;
        int bots = 1;
        readonly List<(int id, string name, string deck)> lobby = new List<(int, string, string)>();

        public bool IsOpen => gameObject.activeSelf;

        public static ForgeLan Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("ForgeLan", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.anchorMin = shade.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            shade.rectTransform.sizeDelta = new Vector2(4000, 3000);
            var l = shade.gameObject.AddComponent<ForgeLan>();
            Instance = l;
            l.gc = gc;
            l.bots = PlayerPrefs.GetInt("forgeLanBots", 1);
            shade.gameObject.AddComponent<PointerEvents>();
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(1000, 840);
            p.gameObject.AddComponent<PointerEvents>();
            UiScaled.Add(p, Vector2.zero).MaxScale = 1040f / 840f;

            var t = UIKit.Label("Title", p.transform, "LAN GAME · RULES ENGINE", 28, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 80, 26);
            l.info = UIKit.Label("Info", p.transform, "", 16, Theme.TextDim, TextAnchor.UpperLeft);
            l.info.rectTransform.Place(40, 712, 900, 44);
            l.info.supportRichText = true;

            var ht = UIKit.Button("HostTab", p.transform, "HOST A TABLE", Theme.Accent, () => l.SetMode(false), 17);
            ((RectTransform)ht.transform).Place(40, 650, 300, 50);
            l.hostTab = ht.GetComponent<Image>();
            var jt = UIKit.Button("JoinTab", p.transform, "JOIN A TABLE", Theme.Secondary, () => l.SetMode(true), 17);
            ((RectTransform)jt.transform).Place(356, 650, 300, 50);
            l.joinTab = jt.GetComponent<Image>();

            l.status = UIKit.Label("Status", p.transform, "", 15, Theme.TextDim, TextAnchor.MiddleLeft);
            l.status.rectTransform.Place(40, 34, 630, 50);
            l.status.supportRichText = true;

            // ---------------------------------------------------------------- host
            l.hostView = UIKit.Rect("Host", p.transform).Stretch();
            l.address = UIKit.Label("Address", l.hostView, "", 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            l.address.rectTransform.Place(40, 548, 920, 84);
            l.address.supportRichText = true;
            l.botsText = UIKit.Label("Bots", l.hostView, "", 17, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.botsText.rectTransform.Place(40, 486, 260, 46);
            var minus = UIKit.Button("Minus", l.hostView, "−", Theme.Secondary, () => l.SetBots(l.bots - 1), 24);
            ((RectTransform)minus.transform).Place(300, 488, 56, 44);
            var plus = UIKit.Button("Plus", l.hostView, "+", Theme.Secondary, () => l.SetBots(l.bots + 1), 24);
            ((RectTransform)plus.transform).Place(366, 488, 56, 44);
            l.openButton = UIKit.Button("Open", l.hostView, "OPEN TABLE", new Color(0.16f, 0.3f, 0.72f), l.OpenTable, 17);
            ((RectTransform)l.openButton.transform).Place(700, 488, 260, 44);
            l.players = List(l.hostView, new Rect(40, 126, 920, 340));
            l.startButton = UIKit.Pill("Start", l.hostView, "Start game", new Color(0.95f, 0.43f, 0.1f), l.StartGame, 700, 26, 260, 76, 30);

            // ---------------------------------------------------------------- join
            l.joinView = UIKit.Rect("Join", p.transform).Stretch();
            var found = UIKit.Label("Found", l.joinView, "TABLES ON YOUR NETWORK", 14, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            found.rectTransform.Place(40, 584, 600, 40);
            var refresh = UIKit.Button("Refresh", l.joinView, "REFRESH", Theme.Secondary, () => { gc.Net.Browse(); l.Refresh(); }, 16);
            ((RectTransform)refresh.transform).Place(780, 584, 180, 42);
            l.hosts = List(l.joinView, new Rect(40, 214, 920, 356));
            l.ipInput = UIKit.Input("Ip", l.joinView, "…or type the host's address (e.g. 192.168.0.12)", false, 18);
            ((RectTransform)l.ipInput.transform).Place(40, 126, 640, 56);
            l.ipInput.text = PlayerPrefs.GetString("forgeLanIp", "");
            var joinBtn = UIKit.Button("JoinIp", l.joinView, "JOIN", Theme.Accent, () => l.JoinAt(l.ipInput.text.Trim()), 18);
            ((RectTransform)joinBtn.transform).Place(700, 126, 260, 56);

            UIKit.CloseX(p.transform, l.Close);
            gc.Net.LobbyChanged += () => { if (l != null && l.IsOpen) l.Refresh(); };
            gc.LanLobbyChanged += msg => { if (l != null) l.OnLobby(msg); };
            MenuKit.Bandify(p);   // Arena look: a full-width band, dim shadow behind
            shade.gameObject.SetActive(false);
            return l;
        }

        static RectTransform List(RectTransform parent, Rect r)
        {
            var scroll = UIKit.Rect("Scroll", parent);
            scroll.Place(r.x, r.y, r.width, r.height);
            scroll.gameObject.AddComponent<RectMask2D>();
            scroll.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.25f);
            var sr = scroll.gameObject.AddComponent<ScrollRect>();
            var list = UIKit.Rect("List", scroll);
            list.anchorMin = new Vector2(0, 1); list.anchorMax = new Vector2(1, 1); list.pivot = new Vector2(0.5f, 1);
            var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 10; vl.padding = new RectOffset(10, 10, 10, 10); vl.childControlHeight = false; vl.childControlWidth = true;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = list; sr.horizontal = false; sr.scrollSensitivity = 30; sr.movementType = ScrollRect.MovementType.Clamped;
            UIKit.AddScrollbar(sr);
            return list;
        }

        // =================================================================================================== open / close

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Short, 0.96f);
            SetMode(gc.RemoteBridge || joinMode);
        }

        public void Close()
        {
            // Leaving the lobby closes your table / leaves theirs, unless a game is running.
            if (!gc.InGame)
            {
                if (hosting) { gc.Send("lanOpen", "open", false); gc.Net.StopForgeBeacon(); hosting = false; }
                if (gc.RemoteBridge) gc.LeaveForgeLan();
            }
            gameObject.SetActive(false);
        }

        void Update()
        {
            // The game started (host pressed Start): get out of the way.
            if (gc.InGame && IsOpen) gameObject.SetActive(false);
        }

        void SetMode(bool join)
        {
            if (join != joinMode)
            {
                if (hosting && join) { gc.Send("lanOpen", "open", false); gc.Net.StopForgeBeacon(); hosting = false; lobby.Clear(); }
                if (gc.RemoteBridge && !join) gc.LeaveForgeLan();
            }
            joinMode = join;
            if (join && !gc.RemoteBridge) gc.Net.Browse();
            Refresh();
        }

        void SetBots(int n)
        {
            bots = Mathf.Clamp(n, 0, 5 - lobby.Count);
            PlayerPrefs.SetInt("forgeLanBots", bots);
            Refresh();
        }

        // =================================================================================================== host

        void OpenTable()
        {
            if (!gc.Bridge.Connected) { gc.Toasts.Show("Start Forge first (Forge tab): the host's Forge runs the game.", true); return; }
            if (gc.RemoteBridge) { gc.Toasts.Show("Leave the other table first.", true); return; }
            gc.Send("lanOpen", "open", true);
            hosting = true;
            gc.Net.StartForgeBeacon(1, 6);
            Sound.Play(Sfx.Popup, 0.7f);
            Refresh();
        }

        void StartGame()
        {
            if (!hosting) return;
            if (lobby.Count == 0 && bots == 0) { gc.Toasts.Show("Nobody to play against yet: wait for a friend or add an AI.", true); return; }
            gc.Net.UpdateForgeBeacon(1 + lobby.Count, true);
            gc.BoardView.Clear();
            gc.StartGame(gc.Menu.ForgeStartMessage(true, bots));
        }

        /// <summary>The bridge's list of people at the table (host and joined players both get it).</summary>
        void OnLobby(Dictionary<string, object> msg)
        {
            lobby.Clear();
            if (msg != null)
                foreach (var o in msg.Arr("players"))
                    if (o is Dictionary<string, object> p) lobby.Add((p.Int("id"), p.Str("name"), p.Str("deck")));
            if (hosting) gc.Net.UpdateForgeBeacon(1 + lobby.Count, false);
            bots = Mathf.Clamp(bots, 0, 5 - lobby.Count);
            if (IsOpen) Refresh();
        }

        // =================================================================================================== join

        void JoinAt(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { gc.Toasts.Show("Type the host's address first.", true); return; }
            if (gc.Menu.SelectedDeck == null) { gc.Toasts.Show("Pick a deck first (Decks tab).", true); return; }
            PlayerPrefs.SetString("forgeLanIp", ip);
            gc.JoinForgeLan(ip, gc.Menu.ForgeJoinMessage());
            Sound.Play(Sfx.Popup, 0.6f);
            Refresh();
        }

        // =================================================================================================== view

        void Refresh()
        {
            hostTab.color = joinMode ? Theme.Secondary : Theme.Accent;
            joinTab.color = joinMode ? Theme.Accent : Theme.Secondary;
            var deck = gc.Menu.SelectedDeck;
            string fmt = DeckFormat.Label(gc.Menu.ForgeFormat);
            info.text = "Your deck: " + (deck?.Name ?? "—") + "  ·  name: " + TableNet.MyName +
                        (joinMode ? "  ·  the host picks the format" : "  ·  format: <color=#f7a540>" + fmt + "</color> (change it on Home)");
            bool seated = gc.RemoteBridge;
            hostView.gameObject.SetActive(!joinMode || seated);
            joinView.gameObject.SetActive(joinMode && !seated);

            if (!joinMode || seated)
            {
                address.text = hosting
                    ? "Your address: <color=#f7a540>" + TableNet.LocalAddress() + "</color>\n<size=15>Friends open PLAY ON LAN → JOIN A TABLE (Forge mode) and pick you, or type this address.</size>"
                    : seated ? (gc.Bridge.Connected ? "Seated at " + gc.RemoteAddress + ". The host starts the game." : "Connecting to " + gc.RemoteAddress + "...")
                    : "Open your table, wait for friends to join, choose how many AIs fill the other seats, then Start.";
                botsText.text = "AI OPPONENTS   " + bots;
                botsText.gameObject.SetActive(!seated);
                hostView.Find("Minus")?.gameObject.SetActive(!seated);
                hostView.Find("Plus")?.gameObject.SetActive(!seated);
                openButton.gameObject.SetActive(!hosting && !seated);
                startButton.gameObject.SetActive(hosting);
                startButton.interactable = hosting && (lobby.Count + bots) >= 1;

                foreach (Transform c in players) Destroy(c.gameObject);
                if (hosting) Row(players, "1.  " + TableNet.MyName + "  (host)", deck?.Name ?? "", Theme.PlayerColor(0), null);
                if (seated) Row(players, "1.  The host", gc.RemoteAddress, Theme.PlayerColor(0), null);
                for (int i = 0; i < lobby.Count; i++)
                {
                    var lp = lobby[i];
                    int id = lp.id;
                    Row(players, (i + 2) + ".  " + lp.name, lp.deck, Theme.PlayerColor(i + 1),
                        hosting ? (System.Action)(() => gc.Send("lanKick", "id", id)) : null, "REMOVE");
                }
                for (int b = 0; b < bots && !seated; b++)
                    Row(players, (lobby.Count + b + 2) + ".  AI opponent", "A " + fmt + " deck from Forge", new Color(0.4f, 0.4f, 0.45f), null);
                if (!hosting && !seated) Row(players, "Table closed", "Press OPEN TABLE to let others join.", Theme.Secondary, null);
                status.text = hosting ? "Hosting a " + fmt + " table · " + (1 + lobby.Count) + " people, " + bots + " AI"
                    : seated ? "In the lobby" : (gc.Bridge.Connected ? "" : "<color=#f7a540>Forge isn't running: start it in the Forge tab to host.</color>");
            }
            else
            {
                foreach (Transform c in hosts) Destroy(c.gameObject);
                foreach (var h in gc.Net.ForgeHosts.Values.OrderBy(h => h.Name))
                {
                    var host = h;
                    string sub = host.Address + "  ·  " + host.Players + " people" + (host.Started ? "  ·  in a game" : "");
                    Row(hosts, host.Name + "'s table", sub, Theme.Accent, host.Started ? null : (System.Action)(() => JoinAt(host.Address)), "JOIN");
                }
                if (gc.Net.ForgeHosts.Count == 0)
                    Row(hosts, "Looking for tables...", "Same network as the host? Press REFRESH, or type the host's address below. The host must allow MTG United and Java through the Windows firewall.", Theme.Secondary, null);
                status.text = "You don't need Forge to join: the host's Forge plays the rules.";
            }
        }

        void Row(RectTransform list, string title, string sub, Color color, System.Action onButton, string buttonLabel = "JOIN")
        {
            var row = UIKit.Panel("Row", list, new Color(1, 1, 1, 0.05f));
            row.rectTransform.sizeDelta = new Vector2(0, 78);
            var bar = UIKit.Panel("Bar", row.transform, color);
            bar.rectTransform.Place(0, 0, 6, 78);
            var t = UIKit.Label("Title", row.transform, title, 19, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.Stretch(22, 40, 200, 10);
            var s = UIKit.Label("Sub", row.transform, sub, 14, Theme.TextDim, TextAnchor.LowerLeft);
            s.rectTransform.Stretch(22, 10, 200, 40);
            if (onButton != null)
            {
                var b = UIKit.Button(buttonLabel, row.transform, buttonLabel, buttonLabel == "REMOVE" ? ConfirmDialog.Danger : Theme.Accent, () => onButton(), 16);
                var br = (RectTransform)b.transform;
                br.anchorMin = br.anchorMax = new Vector2(1, 0.5f); br.pivot = new Vector2(1, 0.5f);
                br.sizeDelta = new Vector2(150, 44); br.anchoredPosition = new Vector2(-12, 0);
            }
        }
    }
}

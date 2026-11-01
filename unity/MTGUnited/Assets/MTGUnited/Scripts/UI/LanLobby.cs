using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// LAN lobby for the kitchen table. HOST opens a table on this PC (others see it in their list, or type your
    /// address); JOIN lists tables found on the network. Custom sets are copied between players automatically,
    /// so everybody plays with the same card base. The host starts the game when everyone is in.
    /// </summary>
    public class LanLobby : MonoBehaviour
    {
        GameController gc;
        SavedDeck deck;
        RectTransform hostView, joinView, players, hosts;
        Text status, address, maxText, deckText;
        Button startButton;
        Image hostTab, joinTab;
        InputField ipInput;
        bool joinMode;
        int maxPlayers = 4;

        public bool IsOpen => gameObject.activeSelf;

        public static LanLobby Create(RectTransform parent, GameController gc)
        {
            var shade = UIKit.Panel("LanLobby", parent, new Color(0, 0, 0, 0.62f), false);
            shade.rectTransform.Stretch();
            var l = shade.gameObject.AddComponent<LanLobby>();
            l.gc = gc;
            l.maxPlayers = PlayerPrefs.GetInt("lanMax", 4);
            shade.gameObject.AddComponent<PointerEvents>();
            var p = UIKit.Panel("Panel", shade.transform, new Color(0.07f, 0.07f, 0.08f, 0.98f));
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            p.rectTransform.sizeDelta = new Vector2(1000, 840);
            UiScaled.Add(p, Vector2.zero).MaxScale = 1040f / 840f;
            p.gameObject.AddComponent<PointerEvents>();
            var t = UIKit.Label("Title", p.transform, "LAN TABLE", 28, Theme.Accent, TextAnchor.UpperLeft, FontStyle.Bold).Titled();
            t.rectTransform.Stretch(40, 0, 40, 26);
            l.deckText = UIKit.Label("Deck", p.transform, "", 16, Theme.TextDim, TextAnchor.UpperLeft);
            l.deckText.rectTransform.Place(40, 724, 860, 30);

            var ht = UIKit.Button("HostTab", p.transform, "HOST A TABLE", Theme.Accent, () => l.SetMode(false), 17);
            ((RectTransform)ht.transform).Place(40, 650, 300, 50);
            l.hostTab = ht.GetComponent<Image>();
            var jt = UIKit.Button("JoinTab", p.transform, "JOIN A TABLE", Theme.Secondary, () => l.SetMode(true), 17);
            ((RectTransform)jt.transform).Place(356, 650, 300, 50);
            l.joinTab = jt.GetComponent<Image>();

            l.status = UIKit.Label("Status", p.transform, "", 15, Theme.TextDim, TextAnchor.MiddleLeft);
            l.status.rectTransform.Place(40, 34, 630, 50);
            l.status.resizeTextForBestFit = true; l.status.resizeTextMinSize = 11; l.status.resizeTextMaxSize = 15;

            // ---------------------------------------------------------------- host
            l.hostView = UIKit.Rect("Host", p.transform).Stretch();
            l.address = UIKit.Label("Address", l.hostView, "", 22, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            l.address.rectTransform.Place(40, 548, 920, 84);
            l.address.supportRichText = true;
            l.maxText = UIKit.Label("Max", l.hostView, "", 17, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            l.maxText.rectTransform.Place(40, 486, 260, 46);
            var minus = UIKit.Button("Minus", l.hostView, "−", Theme.Secondary, () => l.SetMax(l.maxPlayers - 1), 24);
            ((RectTransform)minus.transform).Place(300, 488, 56, 44);
            var plus = UIKit.Button("Plus", l.hostView, "+", Theme.Secondary, () => l.SetMax(l.maxPlayers + 1), 24);
            ((RectTransform)plus.transform).Place(366, 488, 56, 44);
            l.players = l.List(l.hostView, new Rect(40, 126, 920, 340));
            var openBtn = UIKit.Button("Open", l.hostView, "OPEN TABLE", new Color(0.16f, 0.3f, 0.72f), l.HostNow, 17);
            ((RectTransform)openBtn.transform).Place(700, 488, 260, 44);
            l.startButton = UIKit.Pill("Start", l.hostView, "Start game", new Color(0.95f, 0.43f, 0.1f), l.StartGame, 700, 26, 260, 76, 30);

            // ---------------------------------------------------------------- join
            l.joinView = UIKit.Rect("Join", p.transform).Stretch();
            var found = UIKit.Label("Found", l.joinView, "TABLES ON YOUR NETWORK", 14, Theme.TextDim, TextAnchor.MiddleLeft, FontStyle.Bold);
            found.rectTransform.Place(40, 584, 600, 40);
            // Look again: forget the list and listen for tables afresh (also restarts listening if it failed).
            var refresh = UIKit.Button("Refresh", l.joinView, "REFRESH", Theme.Secondary, () =>
            {
                l.gc.Net.Browse();
                Sound.Play(Sfx.UiClick, 0.6f);
                l.Refresh();
            }, 16);
            ((RectTransform)refresh.transform).Place(780, 584, 180, 42);
            l.hosts = l.List(l.joinView, new Rect(40, 214, 920, 356));
            l.ipInput = UIKit.Input("Ip", l.joinView, "…or type the host's address (e.g. 192.168.0.12)", false, 18);
            ((RectTransform)l.ipInput.transform).Place(40, 126, 640, 56);
            l.ipInput.text = PlayerPrefs.GetString("lanLastIp", "");
            var joinBtn = UIKit.Button("JoinIp", l.joinView, "JOIN", Theme.Accent, () => l.JoinAt(l.ipInput.text.Trim()), 18);
            ((RectTransform)joinBtn.transform).Place(700, 126, 260, 56);

            UIKit.CloseX(p.transform, l.Close);
            gc.Net.LobbyChanged += () => { if (l != null && l.IsOpen) l.Refresh(); };
            gc.Net.Disconnected += () => { if (l != null && gc.Table.IsOpen) gc.Table.Leave(); };
            MenuKit.Bandify(p);   // Arena look: a full-width band, dim shadow behind
            shade.gameObject.SetActive(false);
            return l;
        }

        RectTransform List(RectTransform parent, Rect r)
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

        public void Open(SavedDeck d)
        {
            deck = d;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            FadeIn.Play(gameObject, Motion.Short);
            SetMode(joinMode);
        }

        /// <summary>Hides the lobby (the game opened); the connection stays up.</summary>
        public void Hide() => gameObject.SetActive(false);

        /// <summary>X / Esc: leaves the lobby (closes your table or disconnects) unless a game is running.</summary>
        public void Close()
        {
            if (!gc.Net.Started) gc.Net.Stop();   // leaves the lobby and stops listening for tables
            gameObject.SetActive(false);
        }

        void SetMode(bool join)
        {
            if (gc.Net.Mode != TableNet.Role.None && join != joinMode) gc.Net.Stop();   // switching tabs leaves the lobby
            joinMode = join;
            if (join && gc.Net.Mode == TableNet.Role.None) gc.Net.Browse();
            Refresh();
        }

        void SetMax(int n)
        {
            maxPlayers = Mathf.Clamp(n, 2, 6);
            PlayerPrefs.SetInt("lanMax", maxPlayers);
            if (gc.Net.Mode == TableNet.Role.Host) gc.Net.MaxPlayers = maxPlayers;
            Refresh();
        }

        // =================================================================================================== actions

        void HostNow()
        {
            if (gc.Net.Mode == TableNet.Role.Host) return;
            gc.Net.Host(deck, maxPlayers);
            if (gc.Net.Mode == TableNet.Role.Host) Sound.Play(Sfx.Popup, 0.7f);
            Refresh();
        }

        void StartGame()
        {
            if (gc.Net.Mode != TableNet.Role.Host || gc.Net.Lobby.Count < 2) return;
            var state = gc.Net.StartGame();
            if (state == null) return;
            Hide();
            gc.Table.BeginNetwork(state);
        }

        void JoinAt(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { gc.Toasts.Show("Type the host's address first.", true); return; }
            PlayerPrefs.SetString("lanLastIp", ip);
            gc.Net.Join(ip, deck);
            Refresh();
        }

        // =================================================================================================== view

        void Refresh()
        {
            var net = gc.Net;
            hostTab.color = joinMode ? Theme.Secondary : Theme.Accent;
            joinTab.color = joinMode ? Theme.Accent : Theme.Secondary;
            deckText.text = "Your deck: " + (deck?.Name ?? "—") + "  ·  " + DeckFormat.Label(deck?.Format ?? DeckFormat.Commander) + "  ·  name: " + TableNet.MyName;
            bool inLobby = net.Mode == TableNet.Role.Client;
            hostView.gameObject.SetActive(!joinMode || inLobby);
            joinView.gameObject.SetActive(joinMode && !inLobby);
            status.text = net.Status;

            if (!joinMode || inLobby)
            {
                bool hosting = net.Mode == TableNet.Role.Host;
                address.text = hosting
                    ? "Your address: <color=#f7a540>" + TableNet.LocalAddress() + "</color>\n<size=15>Other players open JOIN A TABLE and pick you from the list, or type this address.</size>"
                    : inLobby ? "Joined. The host starts the game when everyone is in."
                    : "Open a table, then have the others join from their PCs (same Wi-Fi / network).";
                maxText.text = "SEATS   " + (hosting ? net.MaxPlayers : maxPlayers);
                maxText.transform.parent.Find("Minus")?.gameObject.SetActive(!inLobby);
                maxText.transform.parent.Find("Plus")?.gameObject.SetActive(!inLobby);
                maxText.gameObject.SetActive(!inLobby);
                hostView.Find("Open")?.gameObject.SetActive(!hosting && !inLobby);
                startButton.gameObject.SetActive(hosting);
                startButton.interactable = hosting && net.Lobby.Count >= 2;

                foreach (Transform c in players) Destroy(c.gameObject);
                for (int i = 0; i < net.Lobby.Count; i++)
                {
                    var lp = net.Lobby[i];
                    Row(players, (i + 1) + ".  " + lp.Name + (i == 0 ? "  (host)" : ""),
                        (lp.DeckName ?? "") + "  ·  " + DeckFormat.Label(string.IsNullOrEmpty(lp.Format) ? DeckFormat.Commander : lp.Format), Theme.PlayerColor(i), null);
                }
                if (net.Lobby.Count == 0) Row(players, "Nobody here yet", "", Theme.Secondary, null);
            }
            else
            {
                foreach (Transform c in hosts) Destroy(c.gameObject);
                foreach (var h in net.Hosts.Values.OrderBy(h => h.Name))
                {
                    var host = h;
                    string sub = host.Address + "  ·  " + host.Players + "/" + host.Max + " players" + (host.Started ? "  ·  in progress" : "");
                    Row(hosts, host.Name + "'s table", sub, Theme.Accent, host.Started || host.Players >= host.Max ? null : (System.Action)(() => JoinAt(host.Address)));
                }
                if (net.Hosts.Count == 0)
                    Row(hosts, "Looking for tables...", "No luck? Make sure you're on the same network and the host allowed MTG United through the Windows firewall, or type the address below.", Theme.Secondary, null);
            }
        }

        void Row(RectTransform list, string title, string sub, Color color, System.Action onJoin)
        {
            var row = UIKit.Panel("Row", list, new Color(1, 1, 1, 0.05f));
            row.rectTransform.sizeDelta = new Vector2(0, 78);
            var bar = UIKit.Panel("Bar", row.transform, color);
            bar.rectTransform.Place(0, 0, 6, 78);
            var t = UIKit.Label("Title", row.transform, title, 19, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            t.rectTransform.Stretch(22, 40, 200, 10);
            var s = UIKit.Label("Sub", row.transform, sub, 14, Theme.TextDim, TextAnchor.LowerLeft);
            s.rectTransform.Stretch(22, 10, 200, 40);
            if (onJoin != null)
            {
                var b = UIKit.Button("Join", row.transform, "JOIN", Theme.Accent, () => onJoin(), 16);
                var br = (RectTransform)b.transform;
                br.anchorMin = br.anchorMax = new Vector2(1, 0.5f); br.pivot = new Vector2(1, 0.5f);
                br.sizeDelta = new Vector2(150, 44); br.anchoredPosition = new Vector2(-12, 0);
            }
        }
    }
}

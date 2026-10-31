using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// LAN play for the kitchen table. One player hosts; the others join over the local network.
    ///  • The host owns the table: everyone's actions go to the host, which applies them and sends each player
    ///    the updated table (with other players' hands and libraries hidden).
    ///  • Hosts announce themselves on the LAN (UDP broadcast) so joiners see them in a list; joining by IP works too.
    ///  • Card base: when someone joins, any custom set one side has and the other lacks is copied across
    ///    (cards.xml + images), so everyone sees the same cards.
    /// Messages are JSON lines over TCP (port 7790); discovery uses UDP port 7791.
    /// </summary>
    public class TableNet : MonoBehaviour
    {
        public const int GamePort = 7790, DiscoveryPort = 7791;
        public enum Role { None, Host, Client }

        public class LobbyPlayer { public string Name, DeckName, Deck, Format; public int Conn = -1; public bool Ready; }
        public class FoundHost { public string Name, Address; public int Players, Max; public bool Started; public float Seen; }

        public Role Mode { get; private set; }
        public readonly List<LobbyPlayer> Lobby = new List<LobbyPlayer>();
        public readonly Dictionary<string, FoundHost> Hosts = new Dictionary<string, FoundHost>();
        public int MySeatId = 1;
        public bool Started;
        public string Status = "";
        public event Action LobbyChanged, GameStarted, Disconnected;
        public int MaxPlayers = 4;

        GameController gc;
        readonly ConcurrentQueue<(int conn, Dictionary<string, object> msg)> inbox = new ConcurrentQueue<(int, Dictionary<string, object>)>();

        // host side
        TcpListener listener;
        readonly Dictionary<int, Conn> conns = new Dictionary<int, Conn>();
        int nextConn = 1;
        Thread acceptThread, beaconThread;
        // client side
        Conn server;
        Thread listenThread;
        UdpClient discovery;
        volatile bool running;

        class Conn
        {
            public TcpClient Tcp;
            public StreamWriter Writer;
            public readonly object Lock = new object();
            public void Send(Dictionary<string, object> m)
            {
                string line = Json.Write(m);
                lock (Lock) { try { Writer.WriteLine(line); Writer.Flush(); } catch { } }
            }
        }

        public static TableNet Create(GameController gc)
        {
            var n = gc.gameObject.AddComponent<TableNet>();
            n.gc = gc;
            return n;
        }

        // =================================================================================================== host

        /// <summary>Opens a LAN lobby with you in seat 1.</summary>
        public void Host(SavedDeck deck, int maxPlayers)
        {
            Stop();
            Mode = Role.Host;
            MaxPlayers = Mathf.Clamp(maxPlayers, 2, 6);
            Started = false;
            Lobby.Add(new LobbyPlayer { Name = MyName, DeckName = deck.Name, Deck = deck.List, Format = deck.Format, Conn = -1 });
            try
            {
                listener = new TcpListener(IPAddress.Any, GamePort);
                listener.Start();
            }
            catch (Exception ex) { Status = "Couldn't open port " + GamePort + ": " + ex.Message; Mode = Role.None; Lobby.Clear(); return; }
            running = true;
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "lan-accept" };
            acceptThread.Start();
            beaconName = MyName;
            beaconPlayers = Lobby.Count;
            beaconThread = new Thread(BeaconLoop) { IsBackground = true, Name = "lan-beacon" };
            beaconThread.Start();
            Status = "Hosting on " + LocalAddress() + ":" + GamePort;
            LobbyChanged?.Invoke();
        }

        void AcceptLoop()
        {
            while (running)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); } catch { break; }
                tcp.NoDelay = true;
                var c = new Conn { Tcp = tcp, Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) { AutoFlush = false } };
                int id;
                lock (conns) { id = nextConn++; conns[id] = c; }
                var t = new Thread(() => ReadLoop(tcp, id)) { IsBackground = true, Name = "lan-read-" + id };
                t.Start();
            }
        }

        // Read by the beacon thread (PlayerPrefs / Unity calls are main-thread only).
        volatile string beaconName = "Player";
        volatile int beaconPlayers;

        void BeaconLoop()
        {
            using (var udp = new UdpClient { EnableBroadcast = true })
            {
                // Every network the PC is on (Wi-Fi, cable...), plus this PC itself for a second copy of the game.
                var targets = new List<IPEndPoint> { new IPEndPoint(IPAddress.Broadcast, DiscoveryPort), new IPEndPoint(IPAddress.Loopback, DiscoveryPort) };
                foreach (var b in SubnetBroadcasts()) targets.Add(new IPEndPoint(b, DiscoveryPort));
                while (running)
                {
                    string msg = "MTGU|" + beaconName.Replace("|", "/") + "|" + GamePort + "|" + beaconPlayers + "|" + MaxPlayers + "|" + (Started ? 1 : 0);
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    foreach (var ep in targets) { try { udp.Send(bytes, bytes.Length, ep); } catch { } }
                    Thread.Sleep(1000);
                }
            }
        }

        /// <summary>The broadcast address of each local network (e.g. 192.168.0.255), which reaches past some routers' rules for 255.255.255.255.</summary>
        static IEnumerable<IPAddress> SubnetBroadcasts()
        {
            var list = new List<IPAddress>();
            try
            {
                foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address) || ua.IPv4Mask == null) continue;
                        var ip = ua.Address.GetAddressBytes();
                        var mask = ua.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        list.Add(new IPAddress(b));
                    }
                }
            }
            catch { }
            return list;
        }

        void SendTo(int conn, Dictionary<string, object> m)
        {
            Conn c;
            lock (conns) conns.TryGetValue(conn, out c);
            c?.Send(m);
        }

        void BroadcastLobby()
        {
            beaconPlayers = Lobby.Count;
            var players = Lobby.Select(p => (object)new Dictionary<string, object> { { "name", p.Name }, { "deck", p.DeckName ?? "" }, { "format", p.Format ?? "" } }).ToList();
            foreach (var p in Lobby.Where(p => p.Conn >= 0))
                SendTo(p.Conn, new Dictionary<string, object> { { "t", "lobby" }, { "players", players }, { "max", MaxPlayers } });
            LobbyChanged?.Invoke();
        }

        /// <summary>Host: deal everyone in. Seat order = lobby order (host first).</summary>
        public TableState StartGame()
        {
            if (Mode != Role.Host) return null;
            Started = true;
            var state = new TableState();
            foreach (var p in Lobby) state.AddPlayer(p.Name, new SavedDeck { Name = p.DeckName, List = p.Deck, Format = p.Format });
            foreach (var p in state.Players) state.Draw(p, 7);
            state.Say("— Turn 1: " + state.Players[0].Name + " —");
            MySeatId = state.Players[0].Id;
            for (int i = 0; i < Lobby.Count; i++)
                if (Lobby[i].Conn >= 0) SendTo(Lobby[i].Conn, new Dictionary<string, object> { { "t", "start" }, { "seat", state.Players[i].Id } });
            return state;
        }

        /// <summary>Host: send every remote player their view of the table.</summary>
        public void BroadcastState(TableState s)
        {
            if (Mode != Role.Host) return;
            for (int i = 0; i < Lobby.Count && i < s.Players.Count; i++)
            {
                if (Lobby[i].Conn < 0) continue;
                SendTo(Lobby[i].Conn, new Dictionary<string, object> { { "t", "state" }, { "s", s.ToJson(s.Players[i].Id) } });
            }
        }

        public void BroadcastEvent(Dictionary<string, object> e)
        {
            if (Mode != Role.Host) return;
            foreach (var p in Lobby.Where(p => p.Conn >= 0)) SendTo(p.Conn, new Dictionary<string, object> { { "t", "event" }, { "e", e } });
        }

        // =================================================================================================== client

        public void Browse()
        {
            StopDiscovery();
            Hosts.Clear();
            try
            {
                // Shared port, so a second copy of the game on this PC can listen too.
                discovery = new UdpClient();
                discovery.ExclusiveAddressUse = false;
                discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discovery.EnableBroadcast = true;
                discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                discovery.BeginReceive(OnBeacon, null);
            }
            catch (Exception ex) { Status = "Can't listen for LAN games: " + ex.Message; }
        }

        void OnBeacon(IAsyncResult r)
        {
            try
            {
                IPEndPoint ep = null;
                var bytes = discovery.EndReceive(r, ref ep);
                var parts = Encoding.UTF8.GetString(bytes).Split('|');
                if (parts.Length >= 6 && parts[0] == "MTGU")
                {
                    var h = new FoundHost
                    {
                        Name = parts[1], Address = ep.Address.ToString(), Players = int.Parse(parts[3]), Max = int.Parse(parts[4]),
                        Started = parts[5] == "1",
                    };
                    inbox.Enqueue((-2, new Dictionary<string, object> { { "t", "_beacon" }, { "name", h.Name }, { "addr", h.Address }, { "players", (double)h.Players }, { "max", (double)h.Max }, { "started", h.Started } }));
                }
                discovery.BeginReceive(OnBeacon, null);
            }
            catch { }
        }

        void StopDiscovery()
        {
            try { discovery?.Close(); } catch { }
            discovery = null;
        }

        /// <summary>Joins a host by IP address with your deck.</summary>
        public void Join(string address, SavedDeck deck)
        {
            Stop();
            Mode = Role.Client;
            Started = false;
            Status = "Connecting to " + address + "...";
            var deckCopy = deck;
            var t = new Thread(() =>
            {
                try
                {
                    var tcp = new TcpClient();
                    tcp.NoDelay = true;
                    tcp.Connect(address, GamePort);
                    server = new Conn { Tcp = tcp, Writer = new StreamWriter(tcp.GetStream(), new UTF8Encoding(false)) };
                    running = true;
                    server.Send(new Dictionary<string, object>
                    {
                        { "t", "hello" }, { "name", MyName }, { "deckName", deckCopy.Name }, { "deck", deckCopy.List }, { "format", deckCopy.Format },
                        { "sets", CustomSets.All.Select(s => (object)s.Code).ToList() },
                    });
                    ReadLoop(tcp, 0);
                }
                catch (Exception ex)
                {
                    inbox.Enqueue((0, new Dictionary<string, object> { { "t", "_error" }, { "text", "Couldn't connect to " + address + ": " + ex.Message } }));
                }
            }) { IsBackground = true, Name = "lan-join" };
            listenThread = t;
            t.Start();
        }

        public void SendAction(Dictionary<string, object> act)
        {
            if (Mode == Role.Client) server?.Send(new Dictionary<string, object> { { "t", "act" }, { "a", act } });
        }

        // =================================================================================================== shared

        void ReadLoop(TcpClient tcp, int conn)
        {
            try
            {
                using (var reader = new StreamReader(tcp.GetStream(), Encoding.UTF8))
                {
                    string line;
                    while (running && (line = reader.ReadLine()) != null)
                    {
                        Dictionary<string, object> m;
                        try { m = Json.ParseObject(line); } catch { continue; }
                        if (m != null) inbox.Enqueue((conn, m));
                    }
                }
            }
            catch { }
            inbox.Enqueue((conn, new Dictionary<string, object> { { "t", "_closed" } }));
        }

        void Update()
        {
            int budget = 50;
            while (budget-- > 0 && inbox.TryDequeue(out var item)) Handle(item.conn, item.msg);
            // Forget hosts we haven't heard from in a while.
            foreach (var k in Hosts.Where(h => Time.unscaledTime - h.Value.Seen > 4f).Select(h => h.Key).ToList()) { Hosts.Remove(k); LobbyChanged?.Invoke(); }
        }

        void Handle(int conn, Dictionary<string, object> m)
        {
            string t = m.Str("t");
            if (t == "_beacon")
            {
                string addr = m.Str("addr");
                bool isNew = !Hosts.ContainsKey(addr);
                Hosts[addr] = new FoundHost { Name = m.Str("name"), Address = addr, Players = m.Int("players"), Max = m.Int("max"), Started = m.Bool("started"), Seen = Time.unscaledTime };
                if (isNew) LobbyChanged?.Invoke();
                return;
            }
            if (Mode == Role.Host) HandleAsHost(conn, t, m);
            else if (Mode == Role.Client) HandleAsClient(t, m);
        }

        void HandleAsHost(int conn, string t, Dictionary<string, object> m)
        {
            switch (t)
            {
                case "hello":
                    if (Started || Lobby.Count >= MaxPlayers)
                    {
                        SendTo(conn, new Dictionary<string, object> { { "t", "bye" }, { "text", Started ? "That game has already started." : "That table is full." } });
                        return;
                    }
                    Lobby.Add(new LobbyPlayer { Name = m.Str("name"), DeckName = m.Str("deckName"), Deck = m.Str("deck"), Format = m.Str("format"), Conn = conn });
                    // Same card base: send them our sets they lack; ask for theirs we lack.
                    var theirs = new HashSet<string>(m.Arr("sets").Select(o => o as string));
                    foreach (var set in CustomSets.All.Where(s => !theirs.Contains(s.Code))) SendSet(set, msg => SendTo(conn, msg));
                    var mine = new HashSet<string>(CustomSets.All.Select(s => s.Code));
                    var want = theirs.Where(code => !mine.Contains(code)).Select(c => (object)c).ToList();
                    if (want.Count > 0) SendTo(conn, new Dictionary<string, object> { { "t", "needsets" }, { "codes", want } });
                    gc.Toasts.Show(m.Str("name") + " joined the table.");
                    Sound.Play(Sfx.Popup, 0.7f);
                    BroadcastLobby();
                    break;
                case "act":
                    var a = m.Obj("a");
                    int seat = Lobby.FindIndex(p => p.Conn == conn);
                    if (a == null || seat < 0 || gc.Table.State == null) return;
                    a["by"] = (double)gc.Table.State.Players[seat].Id;
                    gc.Table.ApplyRemote(a);
                    break;
                case "setfile": ReceiveSetFile(m); break;
                case "_closed":
                    int i = Lobby.FindIndex(p => p.Conn == conn);
                    lock (conns) conns.Remove(conn);
                    if (i >= 0)
                    {
                        gc.Toasts.Show(Lobby[i].Name + " left the table.", true);
                        if (!Started) Lobby.RemoveAt(i); else Lobby[i].Conn = -2;   // keep their seat in a running game
                        BroadcastLobby();
                    }
                    break;
            }
        }

        void HandleAsClient(string t, Dictionary<string, object> m)
        {
            switch (t)
            {
                case "lobby":
                    Lobby.Clear();
                    foreach (var o in m.Arr("players"))
                        if (o is Dictionary<string, object> p) Lobby.Add(new LobbyPlayer { Name = p.Str("name"), DeckName = p.Str("deck"), Format = p.Str("format") });
                    MaxPlayers = m.Int("max", 4);
                    Status = "In the lobby. Waiting for the host to start...";
                    LobbyChanged?.Invoke();
                    break;
                case "needsets":
                    foreach (var o in m.Arr("codes"))
                    {
                        var set = CustomSets.All.FirstOrDefault(s => s.Code == o as string);
                        if (set != null) SendSet(set, msg => server?.Send(msg));
                    }
                    break;
                case "setfile": ReceiveSetFile(m); break;
                case "start":
                    MySeatId = m.Int("seat", 2);
                    Started = true;
                    GameStarted?.Invoke();
                    break;
                case "state":
                    var s = m.Obj("s");
                    if (s != null) gc.Table.ApplySnapshot(TableState.FromJson(s), MySeatId);
                    break;
                case "event":
                    var e = m.Obj("e");
                    if (e != null) gc.Table.ShowEvent(e);
                    break;
                case "bye":
                    Status = m.Str("text");
                    gc.Toasts.Show(Status, true);
                    Stop();
                    break;
                case "_error":
                    Status = m.Str("text");
                    gc.Toasts.Show(Status, true);
                    Mode = Role.None;
                    LobbyChanged?.Invoke();
                    break;
                case "_closed":
                    if (Mode != Role.Client) return;
                    gc.Toasts.Show("Disconnected from the host.", true);
                    Stop();
                    Disconnected?.Invoke();
                    break;
            }
        }

        // ---------------------------------------------------------------- custom set transfer

        int receivingFiles;

        /// <summary>Sends a set as one message per file (cards.xml, then each image).</summary>
        static void SendSet(CustomSet set, Action<Dictionary<string, object>> send)
        {
            var files = new List<string> { Path.Combine(set.Folder, "cards.xml") };
            string img = Path.Combine(set.Folder, "images");
            if (Directory.Exists(img)) files.AddRange(Directory.GetFiles(img));
            for (int i = 0; i < files.Count; i++)
            {
                string rel = files[i].Substring(set.Folder.Length).TrimStart('\\', '/').Replace('\\', '/');
                byte[] data;
                try { data = File.ReadAllBytes(files[i]); } catch { continue; }
                send(new Dictionary<string, object>
                {
                    { "t", "setfile" }, { "code", set.Code }, { "name", set.Name }, { "file", rel }, { "data", Convert.ToBase64String(data) },
                    { "index", i }, { "count", files.Count },
                });
            }
        }

        void ReceiveSetFile(Dictionary<string, object> m)
        {
            string code = m.Str("code"), rel = m.Str("file");
            if (string.IsNullOrEmpty(code) || rel.Contains("..")) return;
            string dir = Path.Combine(CustomSets.Root, Safe(code));
            string path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, Convert.FromBase64String(m.Str("data")));
            }
            catch (Exception ex) { Debug.LogWarning("[MTGUnited] Set file failed: " + ex.Message); return; }
            int index = m.Int("index"), count = m.Int("count");
            Status = "Receiving set " + m.Str("name") + " (" + (index + 1) + "/" + count + ")";
            LobbyChanged?.Invoke();
            if (index == count - 1)
            {
                CustomSets.Reload();
                gc.Toasts.Show("Got the custom set " + m.Str("name") + " (" + code + ").");
                Status = Mode == Role.Host ? "Hosting on " + LocalAddress() + ":" + GamePort : "In the lobby. Waiting for the host to start...";
                LobbyChanged?.Invoke();
            }
        }

        static string Safe(string s)
        {
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s;
        }

        // =================================================================================================== teardown / helpers

        public void Stop()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            listener = null;
            lock (conns)
            {
                foreach (var c in conns.Values) { try { c.Tcp.Close(); } catch { } }
                conns.Clear();
            }
            try { server?.Tcp.Close(); } catch { }
            server = null;
            StopDiscovery();
            Mode = Role.None;
            Started = false;
            Lobby.Clear();
            LobbyChanged?.Invoke();
        }

        void OnApplicationQuit() => Stop();

        public static string MyName => PlayerPrefs.GetString("playerName", "Player");

        /// <summary>This PC's address on the local network (what other players type to join).</summary>
        public static string LocalAddress()
        {
            try
            {
                var addrs = Dns.GetHostEntry(Dns.GetHostName()).AddressList.Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString()).ToList();
                return addrs.FirstOrDefault(a => a.StartsWith("192.168.")) ?? addrs.FirstOrDefault(a => a.StartsWith("10.")) ?? addrs.FirstOrDefault() ?? "127.0.0.1";
            }
            catch { return "127.0.0.1"; }
        }
    }
}

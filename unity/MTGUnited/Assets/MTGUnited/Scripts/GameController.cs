using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MTGUnited
{
    /// <summary>
    /// Builds the whole client at startup (no scene setup needed) and routes bridge messages to the views.
    /// Press Play in any scene: this object creates the canvas, event system and camera it needs.
    /// </summary>
    public class GameController : MonoBehaviour
    {
        public static GameController Instance { get; private set; }

        public BridgeClient Bridge { get; private set; }
        public GameState State { get; private set; }
        public RectTransform Board { get; private set; }
        public RectTransform OverlayLayer { get; private set; }

        public BoardView BoardView { get; private set; }
        public HudView Hud { get; private set; }
        public SelectionTray Tray { get; private set; }
        public RequestDialog Dialog { get; private set; }
        public MainMenu Menu { get; private set; }
        public CardPreview Preview { get; private set; }
        public CardInspector Inspector { get; private set; }
        public OptionsPanel Options { get; private set; }
        public RevealPopup Reveals { get; private set; }
        public ScreenFx ScreenFx { get; private set; }
        public GameMenu GameMenu { get; private set; }
        public TableView Table { get; private set; }
        public TableNet Net { get; private set; }
        public LanLobby Lan { get; private set; }
        public Toasts Toasts { get; private set; }
        public ZoneViewer ZoneViewer { get; private set; }
        public GameOverPanel GameOver { get; private set; }
        public BridgeLauncher Launcher { get; private set; }

        public bool AutoPayMana = true;
        public bool InGame;
        Dictionary<string, object> lastStartMessage;

        // ------------------------------------------------------------------ LAN play with the rules engine
        /// <summary>True while this client is connected to another player's bridge (their Forge table).</summary>
        public bool RemoteBridge { get; private set; }
        public string RemoteAddress { get; private set; }
        Dictionary<string, object> pendingJoin;
        float remoteSince;
        public event Action<Dictionary<string, object>> LanLobbyChanged;

        /// <summary>Joins a Forge table on the LAN: this client talks to the host's bridge instead of its own.</summary>
        public void JoinForgeLan(string address, Dictionary<string, object> join)
        {
            pendingJoin = join;
            RemoteBridge = true;
            RemoteAddress = address;
            remoteSince = Time.unscaledTime;
            Bridge.Close();
            Bridge = new BridgeClient { Host = address, Port = 7792 };
            reconnectTimer = 0;
        }

        /// <summary>Back to this PC's own bridge (leaving a LAN table).</summary>
        public void LeaveForgeLan(string why = null)
        {
            if (!RemoteBridge) return;
            RemoteBridge = false;
            pendingJoin = null;
            Bridge.Close();
            Bridge = new BridgeClient { Port = PlayerPrefs.GetInt("port", 7777) };
            reconnectTimer = 0;
            if (InGame) { InGame = false; Menu.Show(); }
            if (!string.IsNullOrEmpty(why)) Toasts.Show(why, true);
            LanLobbyChanged?.Invoke(null);
        }
        float reconnectTimer;
        float pingTimer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Find<GameController>() != null) return;
            var go = new GameObject("MTG United");
            go.AddComponent<GameController>();
        }

        static T Find<T>() where T : UnityEngine.Object
        {
#if UNITY_2022_2_OR_NEWER
            return FindAnyObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            Application.targetFrameRate = 120;   // drops to 30 while the window isn't focused (see OnApplicationFocus)
            AutoPayMana = PlayerPrefs.GetInt("autoPay", 1) == 1;

            EnsureCamera();
            GameArt.ApplyCursor();
            EnsureEventSystem();
            gameObject.AddComponent<CardImageCache>();
            Sound.Create(gameObject);
            CardCatalog.Gc = this;
            Launcher = gameObject.AddComponent<BridgeLauncher>();

            Bridge = new BridgeClient { Port = PlayerPrefs.GetInt("port", 7777) };
            BuildCanvas();
        }

        void EnsureCamera()
        {
            if (Camera.main != null) { Camera.main.backgroundColor = Theme.BgBottom; Camera.main.clearFlags = CameraClearFlags.SolidColor; return; }
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Theme.BgBottom;
            cam.orthographic = true;
            DontDestroyOnLoad(cam.gameObject);
        }

        void EnsureEventSystem()
        {
            if (Find<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            // Project uses only the new Input System package: its UI module lives in that package's assembly.
            var t = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (t != null) es.AddComponent(t);
            else Debug.LogError("[MTGUnited] Input System UI module not found. Set Player Settings > Active Input Handling to 'Both'.");
#else
            es.AddComponent<StandaloneInputModule>();
#endif
            DontDestroyOnLoad(es);
        }

        void BuildCanvas()
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Background fills the whole screen; the board is a fixed 1920x1080 space centred inside it.
            var bg = UIKit.Rect("Background", canvasGo.transform).Stretch();
            var raw = bg.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            if (GameArt.TableBackground != null)
            {
                // Table art from Resources/Visuals, slightly darkened so cards stay readable on top of it.
                raw.texture = GameArt.TableBackground;
                bg.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                bg.GetComponent<AspectRatioFitter>().aspectRatio = GameArt.TableBackground.width / (float)GameArt.TableBackground.height;
                var shade = UIKit.Rect("Shade", canvasGo.transform).Stretch();
                var shadeImg = shade.gameObject.AddComponent<RawImage>();
                shadeImg.texture = UIKit.Gradient(new Color(0, 0, 0, 0.25f), new Color(0, 0, 0, 0.45f));
                shadeImg.raycastTarget = false;
            }
            else
            {
                raw.texture = UIKit.Gradient(Theme.BgTop, Theme.BgBottom);
            }

            Board = UIKit.Rect("Board", canvasGo.transform);
            Board.anchorMin = Board.anchorMax = new Vector2(0.5f, 0.5f);
            Board.pivot = new Vector2(0.5f, 0.5f);
            Board.sizeDelta = new Vector2(1920, 1080);
            Board.anchoredPosition = Vector2.zero;

            BoardView = BoardView.Create(Board, this);
            Hud = HudView.Create(Board, this);
            Tray = SelectionTray.Create(Board, this);
            ScreenFx = ScreenFx.Create(Board, this);
            Table = TableView.Create(Board, this);

            OverlayLayer = UIKit.Rect("Overlays", canvasGo.transform).Stretch().SubCanvas();
            ZoneViewer = ZoneViewer.Create(OverlayLayer, this);
            Dialog = RequestDialog.Create(OverlayLayer, this);
            GameOver = GameOverPanel.Create(OverlayLayer, this);
            Menu = MainMenu.Create(OverlayLayer, this);
            GameMenu = GameMenu.Create(OverlayLayer, this);
            Net = TableNet.Create(this);
            Lan = LanLobby.Create(OverlayLayer, this);
            ForgeLan.Create(OverlayLayer, this);
            Options = OptionsPanel.Create(OverlayLayer, this);
            Reveals = RevealPopup.Create(OverlayLayer, this);
            Toasts = Toasts.Create(OverlayLayer);
            PopupMenu.Create(OverlayLayer);
            ConfirmDialog.Create(OverlayLayer);
            ImportDialog.Create(OverlayLayer, this);

            // Hover preview sits above everything, in the same 1920x1080 space as the board.
            var top = UIKit.Rect("TopBoard", canvasGo.transform).SubCanvas();
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 0.5f);
            top.sizeDelta = new Vector2(1920, 1080);
            Preview = CardPreview.Create(top);
            Inspector = CardInspector.Create(top);
        }

        void Update()
        {
            ForgeSetup.Poll();          // setup tasks and file pickers report back on the main thread
            ForgeSetup.PollBrowse();
            if (Bridge == null)
            {
                // Scripts were reloaded during Play mode (fields reset). Reconnect instead of failing every frame.
                Bridge = new BridgeClient { Port = PlayerPrefs.GetInt("port", 7777) };
                Debug.LogWarning("[MTGUnited] Scripts reloaded while playing; reconnecting to the bridge.");
            }
            if (RemoteBridge && !Bridge.Connected && !Bridge.Connecting && pendingJoin != null && Time.unscaledTime - remoteSince > 10f)
                LeaveForgeLan("Couldn't reach the table at " + RemoteAddress + (string.IsNullOrEmpty(Bridge.LastError) ? "." : ": " + Bridge.LastError));
            if (!Bridge.Connected && !Bridge.Connecting)
            {
                reconnectTimer -= Time.unscaledDeltaTime;
                if (reconnectTimer <= 0) { reconnectTimer = 1.5f; Bridge.ConnectAsync(); }
            }
            if (Bridge.Connected)
            {
                pingTimer -= Time.unscaledDeltaTime;
                if (pingTimer <= 0) { pingTimer = 10f; Bridge.Send("ping"); }
            }

            int budget = 50;
            while (budget-- > 0 && Bridge.TryReceive(out var msg))
            {
                try { Handle(msg); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            HandleKeys();
        }

        /// <summary>Save power in the background: the bridge keeps running, the client draws less often.</summary>
        void OnApplicationFocus(bool focused) => Application.targetFrameRate = focused ? 120 : 30;

        void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            // Typing in a field: no shortcuts.
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (Input.GetKeyDown(KeyCode.F1)) { Options.Open(true); return; }
            if (Input.GetKeyDown(KeyCode.M) && !ctrl && !Table.IsOpen)
            {
                Sound.Enabled = !Sound.Enabled;
                Toasts.Show(Sound.Enabled ? "Sound on" : "Sound muted");
            }

            // Esc closes the top-most pop-up first.
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (ConfirmDialog.Instance != null && ConfirmDialog.Instance.IsOpen) { ConfirmDialog.Instance.Cancel(); return; }
                if (ImportDialog.Instance != null && ImportDialog.Instance.IsOpen) { ImportDialog.Instance.Close(); return; }
                if (Inspector != null && Inspector.IsOpen) { Inspector.Hide(); return; }
                if (Options.IsOpen) { Options.Close(); return; }
                if (GameMenu.IsOpen) { GameMenu.Close(); return; }
                if (ZoneViewer.IsOpen) { ZoneViewer.Close(); return; }
                if (PopupMenu.Instance != null && PopupMenu.Instance.IsOpen) { PopupMenu.Instance.Close(); return; }
                if (Lan.IsOpen) { Lan.Close(); return; }
                if (ForgeLan.Instance != null && ForgeLan.Instance.IsOpen) { ForgeLan.Instance.Close(); return; }
                if (Menu.IsOpen && Menu.Builder != null && Menu.Builder.IsOpen) { Menu.Builder.Escape(); return; }
                if (Table.IsOpen && !Menu.IsOpen) { if (!Table.CloseTopDialog()) GameMenu.Open(); return; }   // kitchen table: Esc opens the menu
            }
            if (!InGame || Menu.IsOpen || Dialog.IsOpen || Options.IsOpen || GameMenu.IsOpen) return;

            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Hud.PressPrimary();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // Cancel what Forge is asking if it can be cancelled; otherwise open the menu.
                if (State != null && State.Prompt.CancelEnabled) Hud.PressSecondary();
                else GameMenu.Open();
            }
            if (Input.GetKeyDown(KeyCode.Z) && ctrl) Send("undo");
            if (ctrl) return;
            if (Input.GetKeyDown(KeyCode.E)) Send("passTurn");
            if (Input.GetKeyDown(KeyCode.A)) Send("alphaStrike");
            if (State != null && State.Me >= 0)
            {
                if (Input.GetKeyDown(KeyCode.G)) ZoneViewer.Open(State.Me, "Graveyard");
                if (Input.GetKeyDown(KeyCode.X)) ZoneViewer.Open(State.Me, "Exile");
                if (Input.GetKeyDown(KeyCode.C)) ZoneViewer.Open(State.Me, "Command");
            }
            if (Input.GetKeyDown(KeyCode.R)) ZoneViewer.Open(-1, "Revealed");
            for (int i = 0; i < 5; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) BoardView.FocusOpponent(i);
            if (Input.GetKeyDown(KeyCode.Tab)) BoardView.FocusNext();
            if (Input.GetKeyDown(KeyCode.F)) BoardView.ToggleFocusMe();
            if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.Keypad0)) BoardView.FocusOpponent(-1);
#endif
        }

        void Handle(Dictionary<string, object> msg)
        {
            switch (msg.Str("t"))
            {
                case "hello":
                    if (RemoteBridge && pendingJoin != null)
                    {
                        Bridge.Send(pendingJoin);   // take a seat at the host's table
                        pendingJoin = null;
                    }
                    Menu.SetConnection(true);
                    if (msg.Bool("inGame")) { InGame = true; Menu.Hide(); }
                    SendStops();
                    break;
                case "_disconnected":
                    if (RemoteBridge && pendingJoin == null) { LeaveForgeLan("Disconnected from the host's table."); break; }
                    Menu.SetConnection(false);
                    if (InGame) Toasts.Show("Lost connection to the Forge bridge. Reconnecting...", true);
                    break;
                case "state":
                    State = GameState.FromMessage(msg);
                    SoundDirector.OnState(State);
                    ScreenFx.OnState(State);
                    if (State.Me >= 0 && !State.GameOver && !InGame) { InGame = true; Menu.Hide(); }
                    BoardView.Render(State);
                    Hud.Render(State);
                    ZoneViewer.Refresh(State);
                    MaybeAutoPay(State);
                    break;
                case "prompt":
                    // The bridge couldn't package the full board this time; still show what Forge is asking for.
                    if (State != null)
                    {
                        State.Prompt = PromptData.From(msg.Obj("prompt"), State);
                        BoardView.Render(State);
                        Hud.Render(State);
                        MaybeAutoPay(State);
                    }
                    break;
                case "gameStarted":
                    InGame = true;
                    SoundDirector.Reset();
                    ZoneViewer.Revealed.Clear();
                    ScreenFx.MatchIntro();
                    GameOver.Hide();
                    Menu.Hide();
                    Dialog.Close();
                    Toasts.Show("Game started. Good luck!");
                    break;
                case "request":
                    Sound.Play(Sfx.Popup, 0.7f);
                    Dialog.Open(msg);
                    break;
                case "message":
                    Toasts.Show(string.IsNullOrEmpty(msg.Str("title")) || msg.Str("title") == "Forge"
                        ? msg.Str("text") : msg.Str("title") + ": " + msg.Str("text"), msg.Bool("error"));
                    break;
                case "flash":
                    Hud.Flash();
                    break;
                case "alert":
                    break;
                case "deckError":
                    var errs = new List<string>();
                    foreach (var e in msg.Arr("errors")) errs.Add(e.ToString());
                    Menu.Show();
                    Menu.ShowErrors(errs);
                    break;
                case "gameOver":
                    InGame = false;
                    Dialog.Close();
                    GameOver.Show(msg.Str("winner", null), State);
                    break;
                case "pong":
                    break;
                case "lanLobby":
                    LanLobbyChanged?.Invoke(msg);
                    break;
                case "bye":
                    LeaveForgeLan(msg.Str("text"));
                    break;
                case "revealed":
                {
                    var shown = new List<CardData>();
                    foreach (var o in msg.Arr("cards"))
                        if (o is Dictionary<string, object> cd) { var c = CardData.From(cd); if (c != null && !c.Hidden) shown.Add(c); }
                    Reveals.Show(msg.Str("title"), shown);
                    break;
                }
                case "cardResults":
                case "cardInfo":
                    CardCatalog.OnMessage(msg);
                    break;
            }
        }

        // Arena pays mana for you. Forge asks for payment with an "Auto" button; press it unless the player opted out.
        string lastAutoPayPrompt;
        void MaybeAutoPay(GameState s)
        {
            var p = s.Prompt;
            // Never auto-pay a cost asked for during resolution: that is a "may pay" choice, not casting a spell.
            if (!AutoPayMana || !p.OkEnabled || p.OkLabel != "Auto" || p.DuringResolution) { lastAutoPayPrompt = null; return; }
            string key = p.Message + "|" + s.Stack.Count + "|" + s.Turn;
            if (key == lastAutoPayPrompt) return;
            lastAutoPayPrompt = key;
            Send("ok");
        }

        // ------------------------------------------------------------------ actions

        public void Send(string type, params object[] kv) => Bridge.Send(type, kv);

        public void ClickCard(int id) => Send("card", "id", id);
        public void ClickPlayer(int id) => Send("player", "id", id);
        public void ClickStack(int id) => Send("stack", "id", id);

        public void StartGame(Dictionary<string, object> start)
        {
            lastStartMessage = start;
            if (!Bridge.Connected)
            {
                Toasts.Show("Not connected to the Forge bridge yet.", true);
                return;
            }
            Bridge.Send(start);
            Menu.SetBusy("Starting game... (Forge is shuffling up)");
        }

        public void PlayAgain()
        {
            if (lastStartMessage != null) StartGame(lastStartMessage);
            else Menu.Show();
        }

        public void SendStops()
        {
            var my = new List<object>(); var opp = new List<object>();
            foreach (var p in Hud.MyStops) my.Add(p);
            foreach (var p in Hud.OppStops) opp.Add(p);
            Bridge.Send(new Dictionary<string, object> { { "t", "stops" }, { "myTurn", my }, { "opponentTurn", opp } });
        }

        void OnApplicationQuit()
        {
            Bridge?.Close();
        }
    }
}

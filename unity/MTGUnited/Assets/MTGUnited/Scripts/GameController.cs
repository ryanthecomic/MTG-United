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
        public RequestDialog Dialog { get; private set; }
        public MainMenu Menu { get; private set; }
        public CardPreview Preview { get; private set; }
        public Toasts Toasts { get; private set; }
        public ZoneViewer ZoneViewer { get; private set; }
        public GameOverPanel GameOver { get; private set; }
        public BridgeLauncher Launcher { get; private set; }

        public bool AutoPayMana = true;
        public bool InGame;
        Dictionary<string, object> lastStartMessage;
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
            Application.targetFrameRate = 120;
            AutoPayMana = PlayerPrefs.GetInt("autoPay", 1) == 1;

            EnsureCamera();
            GameArt.ApplyCursor();
            EnsureEventSystem();
            gameObject.AddComponent<CardImageCache>();
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

            OverlayLayer = UIKit.Rect("Overlays", canvasGo.transform).Stretch();
            ZoneViewer = ZoneViewer.Create(OverlayLayer, this);
            Dialog = RequestDialog.Create(OverlayLayer, this);
            GameOver = GameOverPanel.Create(OverlayLayer, this);
            Menu = MainMenu.Create(OverlayLayer, this);
            Toasts = Toasts.Create(OverlayLayer);

            // Hover preview sits above everything, in the same 1920x1080 space as the board.
            var top = UIKit.Rect("TopBoard", canvasGo.transform);
            top.anchorMin = top.anchorMax = new Vector2(0.5f, 0.5f);
            top.sizeDelta = new Vector2(1920, 1080);
            Preview = CardPreview.Create(top);
        }

        void Update()
        {
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

        void HandleKeys()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!InGame || Menu.IsOpen || Dialog.IsOpen) return;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null
                && EventSystem.current.currentSelectedGameObject.GetComponent<InputField>() != null) return;
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) Hud.PressPrimary();
            if (Input.GetKeyDown(KeyCode.Escape)) Hud.PressSecondary();
            if (Input.GetKeyDown(KeyCode.Z) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) Send("undo");
#endif
        }

        void Handle(Dictionary<string, object> msg)
        {
            switch (msg.Str("t"))
            {
                case "hello":
                    Menu.SetConnection(true);
                    if (msg.Bool("inGame")) { InGame = true; Menu.Hide(); }
                    SendStops();
                    break;
                case "_disconnected":
                    Menu.SetConnection(false);
                    if (InGame) Toasts.Show("Lost connection to the Forge bridge. Reconnecting...", true);
                    break;
                case "state":
                    State = GameState.FromMessage(msg);
                    if (State.Me >= 0 && !State.GameOver && !InGame) { InGame = true; Menu.Hide(); }
                    BoardView.Render(State);
                    Hud.Render(State);
                    ZoneViewer.Refresh(State);
                    MaybeAutoPay(State);
                    break;
                case "gameStarted":
                    InGame = true;
                    GameOver.Hide();
                    Menu.Hide();
                    Dialog.Close();
                    Toasts.Show("Game started. Good luck!");
                    break;
                case "request":
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
            }
        }

        // Arena pays mana for you. Forge asks for payment with an "Auto" button; press it unless the player opted out.
        string lastAutoPayPrompt;
        void MaybeAutoPay(GameState s)
        {
            var p = s.Prompt;
            if (!AutoPayMana || !p.OkEnabled || p.OkLabel != "Auto") { lastAutoPayPrompt = null; return; }
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

using System;
using AIRTS.Lockstep.Client;
using AIRTS.Lockstep.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace AIRTS.Lockstep.Unity
{
    public sealed class LockstepClientBehaviour : MonoBehaviour
    {
        private const int LogicFrameRate = 30;
        private const int LogicFramesPerNetworkFrame = 2;
        private const float LogicFrameInterval = 1f / LogicFrameRate;

        [SerializeField] private string host = "127.0.0.1";
        [SerializeField] private int port = 7777;
        [SerializeField] private bool connectOnAwake = true;
        [SerializeField] private bool sendSpaceAsTestCommand = true;
        [SerializeField] private bool logFrames = false;
        [SerializeField] private bool showDebugOverlay = true;

        public event Action<LockstepFrame> LogicFrameReady;

        public int LogicFrame { get; private set; }
        public int NetworkFrame { get; private set; }
        public LockstepClient Client { get; private set; }

        private float _logicAccumulator;
        private int _nextNetworkFrame = 1;
        private int _logicFramesRemainingInNetworkFrame;
        private LockstepFrame _currentNetworkFrame;
        private bool _readySendInFlight;
        private GameObject _debugOverlayRoot;
        private Image _debugStatusDot;
        private Text _debugStatusText;
        private Text _debugPlayersText;
        private Text _debugReadyText;
        private Text _debugFrameText;
        private Text _debugHintText;

        private async void Awake()
        {
            Client = new LockstepClient();
            CreateDebugOverlay();
            UpdateDebugOverlay();

            Client.Log += Debug.Log;
            Client.Connected += playerId =>
            {
                ResetLogicCursor(Client.ServerFrame);
                Debug.Log("Lockstep connected, player id: " + playerId);
            };
            Client.Disconnected += reason => Debug.Log("Lockstep disconnected: " + reason);
            Client.PlayerJoined += playerId => Debug.Log("Player joined: " + playerId);
            Client.PlayerLeft += playerId => Debug.Log("Player left: " + playerId);
            Client.GameStarted += startFrame =>
            {
                ResetLogicCursor(startFrame);
                Debug.Log("Lockstep game started at frame: " + startFrame);
            };

            if (connectOnAwake)
            {
                try
                {
                    await Client.ConnectAsync(host, port);
                }
                catch (Exception ex)
                {
                    Debug.LogError("Lockstep connect failed: " + ex.Message);
                }
            }
        }

        private async void Update()
        {
            UpdateDebugOverlay();

            if (Client == null || !Client.IsConnected)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.F5) && !Client.LocalPlayerReady && !_readySendInFlight)
            {
                _readySendInFlight = true;
                try
                {
                    await Client.SendReadyAsync();
                }
                catch (Exception ex)
                {
                    Debug.LogError("Lockstep ready failed: " + ex.Message);
                }
                finally
                {
                    _readySendInFlight = false;
                }
            }

            if (!Client.IsGameStarted)
            {
                _logicAccumulator = 0f;
                return;
            }

            if (sendSpaceAsTestCommand && Input.GetKeyDown(KeyCode.Space))
            {
                await Client.SendCommandAsync(commandType: 1, targetId: 0, x: 0, y: 0, z: 0);
            }

            _logicAccumulator += Time.deltaTime;
            while (_logicAccumulator >= LogicFrameInterval)
            {
                if (!TryAdvanceLogicFrame())
                {
                    _logicAccumulator = Mathf.Min(_logicAccumulator, LogicFrameInterval);
                    break;
                }

                _logicAccumulator -= LogicFrameInterval;
            }
        }

        private bool TryAdvanceLogicFrame()
        {
            if (_logicFramesRemainingInNetworkFrame <= 0)
            {
                if (!Client.TryPopFrame(_nextNetworkFrame, out _currentNetworkFrame))
                {
                    return false;
                }

                NetworkFrame = _currentNetworkFrame.FrameIndex;
                _nextNetworkFrame = _currentNetworkFrame.FrameIndex + 1;
                _logicFramesRemainingInNetworkFrame = LogicFramesPerNetworkFrame;
            }

            LogicFrame++;
            _logicFramesRemainingInNetworkFrame--;

            if (logFrames)
            {
                Debug.Log(
                    "Client logic frame " + LogicFrame +
                    ", network frame " + _currentNetworkFrame.FrameIndex +
                    ", commands: " + _currentNetworkFrame.Commands.Count);
            }

            LogicFrameReady?.Invoke(_currentNetworkFrame);

            if (_logicFramesRemainingInNetworkFrame <= 0)
            {
                _currentNetworkFrame = null;
            }

            return true;
        }

        private void ResetLogicCursor(int networkFrame)
        {
            NetworkFrame = networkFrame;
            LogicFrame = networkFrame * LogicFramesPerNetworkFrame;
            _nextNetworkFrame = networkFrame + 1;
            _logicFramesRemainingInNetworkFrame = 0;
            _currentNetworkFrame = null;
            _logicAccumulator = 0f;
        }

        public async void SendCommand(int commandType, int targetId, Vector3 fixedPointPosition)
        {
            if (Client == null || !Client.IsConnected)
            {
                return;
            }

            var position = fixedPointPosition.ToFixedVector3();
            await Client.SendCommandAsync(
                commandType,
                targetId,
                (int)position.X.RawValue,
                (int)position.Y.RawValue,
                (int)position.Z.RawValue);
        }

        private void OnDestroy()
        {
            if (_debugOverlayRoot != null)
            {
                Destroy(_debugOverlayRoot);
                _debugOverlayRoot = null;
            }

            Client?.Dispose();
            Client = null;
        }

        private void CreateDebugOverlay()
        {
            if (!showDebugOverlay || _debugOverlayRoot != null)
            {
                return;
            }

            _debugOverlayRoot = new GameObject("Lockstep Debug Overlay");
            DontDestroyOnLoad(_debugOverlayRoot);

            Canvas canvas = _debugOverlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            var scaler = _debugOverlayRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _debugOverlayRoot.AddComponent<GraphicRaycaster>();

            GameObject panelObject = CreateUiObject("Panel", _debugOverlayRoot.transform);
            var panel = panelObject.AddComponent<Image>();
            panel.color = new Color(0.05f, 0.07f, 0.09f, 0.82f);

            RectTransform panelTransform = panelObject.GetComponent<RectTransform>();
            panelTransform.anchorMin = new Vector2(0f, 1f);
            panelTransform.anchorMax = new Vector2(0f, 1f);
            panelTransform.pivot = new Vector2(0f, 1f);
            panelTransform.anchoredPosition = new Vector2(16f, -16f);
            panelTransform.sizeDelta = new Vector2(340f, 170f);

            GameObject accentObject = CreateUiObject("Accent", panelObject.transform);
            var accent = accentObject.AddComponent<Image>();
            accent.color = new Color(0.16f, 0.68f, 0.95f, 1f);
            RectTransform accentTransform = accentObject.GetComponent<RectTransform>();
            accentTransform.anchorMin = new Vector2(0f, 1f);
            accentTransform.anchorMax = new Vector2(1f, 1f);
            accentTransform.pivot = new Vector2(0.5f, 1f);
            accentTransform.anchoredPosition = Vector2.zero;
            accentTransform.sizeDelta = new Vector2(0f, 3f);

            GameObject titleObject = CreateUiObject("Title", panelObject.transform);
            Text title = CreateText(titleObject, "锁步调试", 14, FontStyle.Bold, new Color(0.88f, 0.95f, 1f, 1f));
            RectTransform titleTransform = titleObject.GetComponent<RectTransform>();
            titleTransform.anchorMin = new Vector2(0f, 1f);
            titleTransform.anchorMax = new Vector2(1f, 1f);
            titleTransform.pivot = new Vector2(0.5f, 1f);
            titleTransform.anchoredPosition = new Vector2(18f, -15f);
            titleTransform.sizeDelta = new Vector2(-36f, 24f);

            GameObject dotObject = CreateUiObject("Status Dot", panelObject.transform);
            _debugStatusDot = dotObject.AddComponent<Image>();
            RectTransform dotTransform = dotObject.GetComponent<RectTransform>();
            dotTransform.anchorMin = new Vector2(0f, 1f);
            dotTransform.anchorMax = new Vector2(0f, 1f);
            dotTransform.pivot = new Vector2(0.5f, 0.5f);
            dotTransform.anchoredPosition = new Vector2(24f, -48f);
            dotTransform.sizeDelta = new Vector2(10f, 10f);

            _debugStatusText = CreateDebugLine(panelObject.transform, "状态", 42f);
            _debugPlayersText = CreateDebugLine(panelObject.transform, "玩家", 66f);
            _debugReadyText = CreateDebugLine(panelObject.transform, "准备", 90f);
            _debugFrameText = CreateDebugLine(panelObject.transform, "帧", 114f);

            GameObject hintObject = CreateUiObject("Hint", panelObject.transform);
            _debugHintText = CreateText(hintObject, string.Empty, 12, FontStyle.Italic, new Color(0.62f, 0.72f, 0.82f, 1f));
            RectTransform hintTransform = hintObject.GetComponent<RectTransform>();
            hintTransform.anchorMin = new Vector2(0f, 0f);
            hintTransform.anchorMax = new Vector2(1f, 0f);
            hintTransform.pivot = new Vector2(0.5f, 0f);
            hintTransform.anchoredPosition = new Vector2(18f, 12f);
            hintTransform.sizeDelta = new Vector2(-36f, 22f);
        }

        private Text CreateDebugLine(Transform parent, string name, float topOffset)
        {
            GameObject textObject = CreateUiObject(name, parent);
            Text text = CreateText(textObject, string.Empty, 13, FontStyle.Normal, new Color(0.84f, 0.89f, 0.94f, 1f));
            RectTransform transform = textObject.GetComponent<RectTransform>();
            transform.anchorMin = new Vector2(0f, 1f);
            transform.anchorMax = new Vector2(1f, 1f);
            transform.pivot = new Vector2(0.5f, 1f);
            transform.anchoredPosition = new Vector2(42f, -topOffset);
            transform.sizeDelta = new Vector2(-60f, 22f);
            return text;
        }

        private static GameObject CreateUiObject(string name, Transform parent)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.AddComponent<RectTransform>();
            return gameObject;
        }

        private static Text CreateText(GameObject gameObject, string text, int fontSize, FontStyle fontStyle, Color color)
        {
            Text uiText = gameObject.AddComponent<Text>();
            uiText.text = text;
            uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            uiText.fontSize = fontSize;
            uiText.fontStyle = fontStyle;
            uiText.color = color;
            uiText.alignment = TextAnchor.MiddleLeft;
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Truncate;
            uiText.raycastTarget = false;
            return uiText;
        }

        private void UpdateDebugOverlay()
        {
            if (_debugOverlayRoot == null)
            {
                return;
            }

            _debugOverlayRoot.SetActive(showDebugOverlay);
            if (!showDebugOverlay)
            {
                return;
            }

            bool connected = Client != null && Client.IsConnected;
            bool running = Client != null && Client.IsGameStarted;
            bool localReady = Client != null && Client.LocalPlayerReady;
            int connectedPlayers = Client != null ? Client.ConnectedPlayerCount : 0;
            int requiredPlayers = Client != null ? Client.RequiredPlayerCount : 2;
            int readyPlayers = Client != null ? Client.ReadyPlayerCount : 0;

            if (_debugStatusDot != null)
            {
                _debugStatusDot.color = running
                    ? new Color(0.23f, 0.84f, 0.48f, 1f)
                    : connected ? new Color(1f, 0.73f, 0.22f, 1f) : new Color(0.95f, 0.25f, 0.25f, 1f);
            }

            SetDebugText(_debugStatusText, "状态   " + (connected ? running ? "运行中" : "等待中" : "未连接"));
            SetDebugText(_debugPlayersText, "玩家   " + connectedPlayers + "/" + requiredPlayers);
            SetDebugText(_debugReadyText, "准备   " + readyPlayers + "/" + connectedPlayers + "  F5 " + (localReady ? "已准备" : "未准备"));
            SetDebugText(_debugFrameText, "帧     网络 " + NetworkFrame + "   逻辑 " + LogicFrame);
            SetDebugText(_debugHintText, running ? "模拟正在推进。" : "等待两个客户端连接并准备。");
        }

        private static void SetDebugText(Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }
    }
}

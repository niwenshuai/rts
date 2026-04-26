using System;
using AIRTS.Lockstep.Client;
using AIRTS.Lockstep.Shared;
using UnityEngine;

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

        public event Action<LockstepFrame> LogicFrameReady;

        public int LogicFrame { get; private set; }
        public int NetworkFrame { get; private set; }
        public LockstepClient Client { get; private set; }

        private float _logicAccumulator;
        private int _nextNetworkFrame = 1;
        private int _logicFramesRemainingInNetworkFrame;
        private LockstepFrame _currentNetworkFrame;

        private async void Awake()
        {
            Client = new LockstepClient();
            Client.Log += Debug.Log;
            Client.Connected += playerId =>
            {
                ResetLogicCursor(Client.ServerFrame);
                Debug.Log("Lockstep connected, player id: " + playerId);
            };
            Client.Disconnected += reason => Debug.Log("Lockstep disconnected: " + reason);
            Client.PlayerJoined += playerId => Debug.Log("Player joined: " + playerId);
            Client.PlayerLeft += playerId => Debug.Log("Player left: " + playerId);

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
            if (Client == null || !Client.IsConnected)
            {
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
            Client?.Dispose();
            Client = null;
        }
    }
}

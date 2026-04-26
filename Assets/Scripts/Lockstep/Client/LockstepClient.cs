using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AIRTS.Lockstep.Shared;

namespace AIRTS.Lockstep.Client
{
    public sealed class LockstepClient : IDisposable
    {
        public event Action<int> Connected;
        public event Action<LockstepFrame> FrameReceived;
        public event Action<int> PlayerJoined;
        public event Action<int> PlayerLeft;
        public event Action SessionStateChanged;
        public event Action<int> GameStarted;
        public event Action<string> Disconnected;
        public event Action<string> Log;

        public int PlayerId { get; private set; }
        public int ServerFrame { get; private set; }
        public int FrameRate { get; private set; }
        public int InputDelay { get; private set; }
        public int ConnectedPlayerCount { get; private set; }
        public int ReadyPlayerCount { get; private set; }
        public int RequiredPlayerCount { get; private set; } = 2;
        public bool LocalPlayerReady { get; private set; }
        public bool IsGameStarted { get; private set; }
        public bool AllPlayersReady => ConnectedPlayerCount >= RequiredPlayerCount && ConnectedPlayerCount == ReadyPlayerCount;
        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;
        public FrameBuffer Frames { get; } = new FrameBuffer();

        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        public async Task ConnectAsync(string host, int port)
        {
            if (_tcpClient != null)
            {
                throw new InvalidOperationException("Client is already connected.");
            }

            _cts = new CancellationTokenSource();
            _tcpClient = new TcpClient();
            _tcpClient.NoDelay = true;
            await _tcpClient.ConnectAsync(host, port);
            _stream = _tcpClient.GetStream();
            _ = ReceiveLoopAsync(_cts.Token);
            Log?.Invoke("Connected to " + host + ":" + port);
        }

        public async Task SendCommandAsync(int commandType, int targetId, int x, int y, int z, byte[] payload = null)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            int targetFrame = ServerFrame + InputDelay;
            var command = new PlayerCommand(targetFrame, PlayerId, commandType, targetId, x, y, z, payload);
            await SendPacketAsync(LockstepProtocol.CreateInputPacket(command));
        }

        public async Task SendReadyAsync(bool isReady = true)
        {
            if (_stream == null)
            {
                throw new InvalidOperationException("Client is not connected.");
            }

            await SendPacketAsync(LockstepProtocol.CreateReadyPacket(isReady));
            LocalPlayerReady = isReady;
        }

        private async Task SendPacketAsync(byte[] packet)
        {
            await _sendLock.WaitAsync(_cts.Token);
            try
            {
                await LockstepProtocol.WritePacketAsync(_stream, packet, _cts.Token);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public bool TryPopFrame(int frameIndex, out LockstepFrame frame)
        {
            return Frames.TryPop(frameIndex, out frame);
        }

        public void Disconnect()
        {
            if (_tcpClient == null)
            {
                return;
            }

            _cts.Cancel();
            _stream?.Dispose();
            _tcpClient.Close();
            _tcpClient.Dispose();
            _tcpClient = null;
            _stream = null;
            _cts.Dispose();
            _cts = null;
            ConnectedPlayerCount = 0;
            ReadyPlayerCount = 0;
            LocalPlayerReady = false;
            IsGameStarted = false;
            Frames.ClearBefore(int.MaxValue);
        }

        public void Dispose()
        {
            Disconnect();
            _sendLock.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            string reason = "Closed";
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    NetworkPacket packet = await LockstepProtocol.ReadPacketAsync(_stream, cancellationToken);
                    HandlePacket(packet);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    reason = ex.Message;
                }
            }
            finally
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Disconnect();
                    Disconnected?.Invoke(reason);
                }
            }
        }

        private void HandlePacket(NetworkPacket packet)
        {
            using (BinaryReader reader = LockstepProtocol.CreatePayloadReader(packet))
            {
                switch (packet.Type)
                {
                    case NetMessageType.Welcome:
                        PlayerId = reader.ReadInt32();
                        ServerFrame = reader.ReadInt32();
                        FrameRate = reader.ReadInt32();
                        InputDelay = reader.ReadInt32();
                        Connected?.Invoke(PlayerId);
                        break;
                    case NetMessageType.Frame:
                        LockstepFrame frame = LockstepFrame.Read(reader);
                        ServerFrame = frame.FrameIndex;
                        Frames.Add(frame);
                        FrameReceived?.Invoke(frame);
                        break;
                    case NetMessageType.PlayerJoined:
                        PlayerJoined?.Invoke(reader.ReadInt32());
                        break;
                    case NetMessageType.PlayerLeft:
                        PlayerLeft?.Invoke(reader.ReadInt32());
                        break;
                    case NetMessageType.SessionState:
                        ConnectedPlayerCount = reader.ReadInt32();
                        ReadyPlayerCount = reader.ReadInt32();
                        RequiredPlayerCount = reader.ReadInt32();
                        IsGameStarted = reader.ReadBoolean();
                        SessionStateChanged?.Invoke();
                        break;
                    case NetMessageType.GameStarted:
                        int startFrame = reader.ReadInt32();
                        ServerFrame = System.Math.Max(ServerFrame, startFrame);
                        IsGameStarted = true;
                        GameStarted?.Invoke(startFrame);
                        break;
                }
            }
        }
    }
}

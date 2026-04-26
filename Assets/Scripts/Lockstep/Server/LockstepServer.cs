using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AIRTS.Lockstep.Shared;

namespace AIRTS.Lockstep.Server
{
    public sealed class LockstepServer : IDisposable
    {
        public event Action<int> ClientConnected;
        public event Action<int> ClientDisconnected;
        public event Action<int, int> FrameAdvanced;
        public event Action<int> GameStarted;
        public event Action<string> Log;

        public int FrameRate { get; }
        public int InputDelay { get; }
        public int RequiredPlayers { get; }
        public int CurrentFrame => _currentFrame;
        public int ConnectedPlayerCount
        {
            get
            {
                lock (_gate)
                {
                    return _clients.Count;
                }
            }
        }

        public int ReadyPlayerCount
        {
            get
            {
                lock (_gate)
                {
                    return CountReadyPlayersLocked();
                }
            }
        }

        public bool IsGameStarted
        {
            get
            {
                lock (_gate)
                {
                    return _isGameStarted;
                }
            }
        }

        private readonly object _gate = new object();
        private readonly Dictionary<int, ClientPeer> _clients = new Dictionary<int, ClientPeer>();
        private readonly SortedDictionary<int, List<PlayerCommand>> _pendingCommands = new SortedDictionary<int, List<PlayerCommand>>();

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _nextPlayerId = 1;
        private int _currentFrame;
        private bool _isGameStarted;

        public LockstepServer(int frameRate = 15, int inputDelay = 2, int requiredPlayers = 2)
        {
            if (frameRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }

            FrameRate = frameRate;
            InputDelay = System.Math.Max(0, inputDelay);
            RequiredPlayers = System.Math.Max(1, requiredPlayers);
        }

        public void Start(int port)
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("Server is already running.");
            }

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            _ = AcceptLoopAsync(_cts.Token);
            _ = TickLoopAsync(_cts.Token);
            Log?.Invoke("Lockstep server started on port " + port);
        }

        public void Stop()
        {
            if (_cts == null)
            {
                return;
            }

            _cts.Cancel();
            _listener?.Stop();

            lock (_gate)
            {
                foreach (var client in _clients.Values)
                {
                    client.Dispose();
                }

                _clients.Clear();
                _pendingCommands.Clear();
                _isGameStarted = false;
            }

            _listener = null;
            _cts.Dispose();
            _cts = null;
            Log?.Invoke("Lockstep server stopped.");
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient tcpClient = await _listener.AcceptTcpClientAsync();
                    tcpClient.NoDelay = true;

                    int playerId;
                    lock (_gate)
                    {
                        playerId = _nextPlayerId++;
                        _clients.Add(playerId, new ClientPeer(playerId, tcpClient));
                    }

                    await SendToClientAsync(playerId, LockstepProtocol.CreateWelcomePacket(playerId, _currentFrame, FrameRate, InputDelay), cancellationToken);
                    await BroadcastAsync(LockstepProtocol.CreatePacket(NetMessageType.PlayerJoined, writer => writer.Write(playerId)), cancellationToken);
                    await BroadcastSessionStateAsync(cancellationToken);
                    ClientConnected?.Invoke(playerId);
                    _ = ReceiveLoopAsync(playerId, tcpClient, cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        Log?.Invoke("Accept failed: " + ex.Message);
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync(int playerId, TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                NetworkStream stream = tcpClient.GetStream();
                while (!cancellationToken.IsCancellationRequested)
                {
                    NetworkPacket packet = await LockstepProtocol.ReadPacketAsync(stream, cancellationToken);
                    HandleClientPacket(playerId, packet);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Log?.Invoke("Client " + playerId + " disconnected: " + ex.Message);
                }
            }
            finally
            {
                RemoveClient(playerId);
            }
        }

        private void HandleClientPacket(int playerId, NetworkPacket packet)
        {
            if (packet.Type == NetMessageType.Input)
            {
                using (BinaryReader reader = LockstepProtocol.CreatePayloadReader(packet))
                {
                    PlayerCommand command = PlayerCommand.Read(reader);
                    command.PlayerId = playerId;
                    int targetFrame = System.Math.Max(command.Frame, _currentFrame + InputDelay);
                    command.Frame = targetFrame;

                    lock (_gate)
                    {
                        if (!_isGameStarted)
                        {
                            return;
                        }

                        if (!_pendingCommands.TryGetValue(targetFrame, out var commands))
                        {
                            commands = new List<PlayerCommand>();
                            _pendingCommands.Add(targetFrame, commands);
                        }

                        commands.Add(command);
                    }
                }
            }
            else if (packet.Type == NetMessageType.Ping)
            {
                _ = SendToClientAsync(playerId, LockstepProtocol.CreatePacket(NetMessageType.Pong), _cts.Token);
            }
            else if (packet.Type == NetMessageType.Ready)
            {
                using (BinaryReader reader = LockstepProtocol.CreatePayloadReader(packet))
                {
                    SetClientReady(playerId, reader.ReadBoolean());
                }
            }
        }

        private async Task TickLoopAsync(CancellationToken cancellationToken)
        {
            int frameTimeMs = System.Math.Max(1, 1000 / FrameRate);
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(frameTimeMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                List<PlayerCommand> commands;
                int frame;
                lock (_gate)
                {
                    if (!_isGameStarted)
                    {
                        continue;
                    }

                    frame = ++_currentFrame;
                    if (_pendingCommands.TryGetValue(frame, out commands))
                    {
                        _pendingCommands.Remove(frame);
                    }
                    else
                    {
                        commands = new List<PlayerCommand>();
                    }
                }

                var lockstepFrame = new LockstepFrame(frame, commands);
                await BroadcastAsync(LockstepProtocol.CreateFramePacket(lockstepFrame), cancellationToken);
                FrameAdvanced?.Invoke(frame, commands.Count);
            }
        }

        public bool ForceStart()
        {
            lock (_gate)
            {
                if (_isGameStarted)
                {
                    return false;
                }

                _isGameStarted = true;
            }

            NotifyGameStarted();
            BroadcastSessionState();
            return true;
        }

        private void SetClientReady(int playerId, bool isReady)
        {
            bool changed;
            bool shouldStart;
            int readyPlayers;

            lock (_gate)
            {
                if (!_clients.TryGetValue(playerId, out var client))
                {
                    return;
                }

                changed = client.IsReady != isReady;
                client.IsReady = isReady;
                readyPlayers = CountReadyPlayersLocked();
                shouldStart = CanStartGameLocked();
                if (shouldStart)
                {
                    _isGameStarted = true;
                }
            }

            if (changed)
            {
                Log?.Invoke("Player " + playerId + (isReady ? " ready" : " not ready") + " (" + readyPlayers + "/" + RequiredPlayers + ").");
            }

            if (shouldStart)
            {
                NotifyGameStarted();
            }

            BroadcastSessionState();
        }

        private bool CanStartGameLocked()
        {
            return !_isGameStarted && _clients.Count >= RequiredPlayers && CountReadyPlayersLocked() == _clients.Count;
        }

        private int CountReadyPlayersLocked()
        {
            int readyPlayers = 0;
            foreach (var client in _clients.Values)
            {
                if (client.IsReady)
                {
                    readyPlayers++;
                }
            }

            return readyPlayers;
        }

        private void NotifyGameStarted()
        {
            int startFrame = _currentFrame;
            Log?.Invoke("Game started at frame " + startFrame + ".");
            GameStarted?.Invoke(startFrame);

            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _ = BroadcastAsync(LockstepProtocol.CreateGameStartedPacket(startFrame), _cts.Token);
            }
        }

        private void BroadcastSessionState()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _ = BroadcastSessionStateAsync(_cts.Token);
            }
        }

        private Task BroadcastSessionStateAsync(CancellationToken cancellationToken)
        {
            int connectedPlayers;
            int readyPlayers;
            bool isGameStarted;
            lock (_gate)
            {
                connectedPlayers = _clients.Count;
                readyPlayers = CountReadyPlayersLocked();
                isGameStarted = _isGameStarted;
            }

            return BroadcastAsync(
                LockstepProtocol.CreateSessionStatePacket(connectedPlayers, readyPlayers, RequiredPlayers, isGameStarted),
                cancellationToken);
        }

        private async Task BroadcastAsync(byte[] packet, CancellationToken cancellationToken)
        {
            List<ClientPeer> clients;
            lock (_gate)
            {
                clients = new List<ClientPeer>(_clients.Values);
            }

            for (int i = 0; i < clients.Count; i++)
            {
                try
                {
                    await clients[i].SendAsync(packet, cancellationToken);
                }
                catch (Exception ex)
                {
                    Log?.Invoke("Send to client " + clients[i].PlayerId + " failed: " + ex.Message);
                    RemoveClient(clients[i].PlayerId);
                }
            }
        }

        private bool TryGetClient(int playerId, out ClientPeer client)
        {
            lock (_gate)
            {
                return _clients.TryGetValue(playerId, out client);
            }
        }

        private async Task SendToClientAsync(int playerId, byte[] packet, CancellationToken cancellationToken)
        {
            if (TryGetClient(playerId, out var client))
            {
                await client.SendAsync(packet, cancellationToken);
            }
        }

        private void RemoveClient(int playerId)
        {
            ClientPeer client = null;
            lock (_gate)
            {
                if (_clients.TryGetValue(playerId, out client))
                {
                    _clients.Remove(playerId);
                }
            }

            if (client == null)
            {
                return;
            }

            client.Dispose();
            ClientDisconnected?.Invoke(playerId);

            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _ = BroadcastAsync(LockstepProtocol.CreatePacket(NetMessageType.PlayerLeft, writer => writer.Write(playerId)), _cts.Token);
                BroadcastSessionState();
            }
        }

        private sealed class ClientPeer : IDisposable
        {
            private readonly TcpClient _tcpClient;
            private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

            public int PlayerId { get; }
            public bool IsReady { get; set; }

            public ClientPeer(int playerId, TcpClient tcpClient)
            {
                PlayerId = playerId;
                _tcpClient = tcpClient;
            }

            public async Task SendAsync(byte[] packet, CancellationToken cancellationToken)
            {
                await _sendLock.WaitAsync(cancellationToken);
                try
                {
                    await LockstepProtocol.WritePacketAsync(_tcpClient.GetStream(), packet, cancellationToken);
                }
                finally
                {
                    _sendLock.Release();
                }
            }

            public void Dispose()
            {
                _sendLock.Dispose();
                _tcpClient.Close();
                _tcpClient.Dispose();
            }
        }
    }
}

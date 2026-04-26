using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AIRTS.Lockstep.Shared
{
    public static class LockstepProtocol
    {
        public const int MaxPacketSize = 1024 * 64;

        public static byte[] CreatePacket(NetMessageType type, Action<BinaryWriter> writePayload = null)
        {
            using (var payloadStream = new MemoryStream())
            {
                using (var payloadWriter = new BinaryWriter(payloadStream))
                {
                    payloadWriter.Write((ushort)type);
                    writePayload?.Invoke(payloadWriter);
                    payloadWriter.Flush();

                    byte[] payload = payloadStream.ToArray();
                    using (var packetStream = new MemoryStream(payload.Length + sizeof(int)))
                    using (var packetWriter = new BinaryWriter(packetStream))
                    {
                        packetWriter.Write(payload.Length);
                        packetWriter.Write(payload);
                        packetWriter.Flush();
                        return packetStream.ToArray();
                    }
                }
            }
        }

        public static byte[] CreateInputPacket(PlayerCommand command)
        {
            return CreatePacket(NetMessageType.Input, command.Write);
        }

        public static byte[] CreateFramePacket(LockstepFrame frame)
        {
            return CreatePacket(NetMessageType.Frame, frame.Write);
        }

        public static byte[] CreateReadyPacket(bool isReady)
        {
            return CreatePacket(NetMessageType.Ready, writer => writer.Write(isReady));
        }

        public static byte[] CreateSessionStatePacket(int connectedPlayers, int readyPlayers, int requiredPlayers, bool gameStarted)
        {
            return CreatePacket(NetMessageType.SessionState, writer =>
            {
                writer.Write(connectedPlayers);
                writer.Write(readyPlayers);
                writer.Write(requiredPlayers);
                writer.Write(gameStarted);
            });
        }

        public static byte[] CreateGameStartedPacket(int startFrame)
        {
            return CreatePacket(NetMessageType.GameStarted, writer => writer.Write(startFrame));
        }

        public static byte[] CreateWelcomePacket(int playerId, int startFrame, int frameRate, int inputDelay)
        {
            return CreatePacket(NetMessageType.Welcome, writer =>
            {
                writer.Write(playerId);
                writer.Write(startFrame);
                writer.Write(frameRate);
                writer.Write(inputDelay);
            });
        }

        public static async Task<NetworkPacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] lengthBytes = await ReadExactAsync(stream, sizeof(int), cancellationToken);
            int length = BitConverter.ToInt32(lengthBytes, 0);
            if (length <= sizeof(ushort) || length > MaxPacketSize)
            {
                throw new InvalidDataException("Invalid lockstep packet length: " + length);
            }

            byte[] body = await ReadExactAsync(stream, length, cancellationToken);
            using (var memory = new MemoryStream(body))
            using (var reader = new BinaryReader(memory))
            {
                var type = (NetMessageType)reader.ReadUInt16();
                byte[] payload = reader.ReadBytes((int)(memory.Length - memory.Position));
                return new NetworkPacket(type, payload);
            }
        }

        public static async Task WritePacketAsync(NetworkStream stream, byte[] packet, CancellationToken cancellationToken)
        {
            await stream.WriteAsync(packet, 0, packet.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        public static BinaryReader CreatePayloadReader(NetworkPacket packet)
        {
            return new BinaryReader(new MemoryStream(packet.Payload));
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int length, CancellationToken cancellationToken)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException("TCP connection closed.");
                }

                offset += read;
            }

            return buffer;
        }
    }
}

using System;
using System.IO;

namespace AIRTS.Lockstep.Shared
{
    [Serializable]
    public struct PlayerCommand
    {
        public int Frame;
        public int PlayerId;
        public int CommandType;
        public int TargetId;
        public int X;
        public int Y;
        public int Z;
        public byte[] Payload;

        public PlayerCommand(int frame, int playerId, int commandType, int targetId, int x, int y, int z, byte[] payload = null)
        {
            Frame = frame;
            PlayerId = playerId;
            CommandType = commandType;
            TargetId = targetId;
            X = x;
            Y = y;
            Z = z;
            Payload = payload ?? Array.Empty<byte>();
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(Frame);
            writer.Write(PlayerId);
            writer.Write(CommandType);
            writer.Write(TargetId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(Payload == null ? 0 : Payload.Length);
            if (Payload != null && Payload.Length > 0)
            {
                writer.Write(Payload);
            }
        }

        public static PlayerCommand Read(BinaryReader reader)
        {
            var command = new PlayerCommand
            {
                Frame = reader.ReadInt32(),
                PlayerId = reader.ReadInt32(),
                CommandType = reader.ReadInt32(),
                TargetId = reader.ReadInt32(),
                X = reader.ReadInt32(),
                Y = reader.ReadInt32(),
                Z = reader.ReadInt32()
            };

            int payloadLength = reader.ReadInt32();
            command.Payload = payloadLength > 0 ? reader.ReadBytes(payloadLength) : Array.Empty<byte>();
            return command;
        }
    }
}

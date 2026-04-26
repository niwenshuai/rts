namespace AIRTS.Lockstep.Shared
{
    public readonly struct NetworkPacket
    {
        public NetMessageType Type { get; }
        public byte[] Payload { get; }

        public NetworkPacket(NetMessageType type, byte[] payload)
        {
            Type = type;
            Payload = payload ?? System.Array.Empty<byte>();
        }
    }
}

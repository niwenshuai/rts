namespace AIRTS.Lockstep.Shared
{
    public enum NetMessageType : ushort
    {
        Welcome = 1,
        Input = 2,
        Frame = 3,
        Ping = 4,
        Pong = 5,
        PlayerJoined = 6,
        PlayerLeft = 7,
        Disconnect = 8,
        Ready = 9,
        SessionState = 10,
        GameStarted = 11
    }
}

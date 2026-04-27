namespace AIRTS.Lockstep.Gameplay
{
    public sealed class RtsPlayerState
    {
        public int PlayerId { get; }
        public int Gold { get; set; }
        public int TownHallId { get; set; }
        public int GoldMineId { get; set; }
        public bool IsDefeated { get; set; }

        public RtsPlayerState(int playerId, int startingGold)
        {
            PlayerId = playerId;
            Gold = startingGold;
        }
    }
}

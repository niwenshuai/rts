using System;
using System.IO;
using AIRTS.Lockstep.Math;
using AIRTS.Lockstep.Shared;

namespace AIRTS.Lockstep.Gameplay
{
    public static class RtsCommandCodec
    {
        public static PlayerCommand CreateMoveCommand(int playerId, int actorId, FixedVector3 targetPosition)
        {
            return new PlayerCommand(
                0,
                playerId,
                (int)RtsCommandType.Move,
                actorId,
                (int)targetPosition.X.RawValue,
                (int)targetPosition.Y.RawValue,
                (int)targetPosition.Z.RawValue);
        }

        public static PlayerCommand CreateAttackCommand(int playerId, int actorId, int targetId)
        {
            return new PlayerCommand(0, playerId, (int)RtsCommandType.Attack, actorId, targetId, 0, 0);
        }

        public static PlayerCommand CreateProduceCommand(int playerId, int buildingId, RtsUnitType unitType)
        {
            return new PlayerCommand(0, playerId, (int)RtsCommandType.ProduceUnit, buildingId, (int)unitType, 0, 0);
        }

        public static PlayerCommand CreateBuildCommand(int playerId, int workerId, RtsBuildingType buildingType, FixedVector3 targetPosition)
        {
            return new PlayerCommand(
                0,
                playerId,
                (int)RtsCommandType.BuildBuilding,
                workerId,
                (int)targetPosition.X.RawValue,
                (int)targetPosition.Y.RawValue,
                (int)targetPosition.Z.RawValue,
                WriteIntPayload((int)buildingType));
        }

        public static FixedVector3 ReadPosition(PlayerCommand command)
        {
            return new FixedVector3(
                Fix64.FromRaw(command.X),
                Fix64.FromRaw(command.Y),
                Fix64.FromRaw(command.Z));
        }

        public static bool TryReadBuildingType(PlayerCommand command, out RtsBuildingType buildingType)
        {
            if (TryReadIntPayload(command.Payload, out int value))
            {
                buildingType = (RtsBuildingType)value;
                return true;
            }

            buildingType = RtsBuildingType.None;
            return false;
        }

        private static byte[] WriteIntPayload(int value)
        {
            using (var stream = new MemoryStream(sizeof(int)))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(value);
                writer.Flush();
                return stream.ToArray();
            }
        }

        private static bool TryReadIntPayload(byte[] payload, out int value)
        {
            if (payload == null || payload.Length < sizeof(int))
            {
                value = 0;
                return false;
            }

            using (var stream = new MemoryStream(payload))
            using (var reader = new BinaryReader(stream))
            {
                value = reader.ReadInt32();
                return true;
            }
        }
    }
}

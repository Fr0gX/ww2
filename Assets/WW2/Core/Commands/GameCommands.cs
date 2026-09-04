using WW2.Core.Model;

namespace WW2.Core.Commands
{
    public abstract class GameCommand
    {
        protected GameCommand(CommandType type, int nationId)
        {
            Type = type;
            NationId = nationId;
        }

        public CommandType Type { get; }
        public int NationId { get; }
    }

    public sealed class MoveCommand : GameCommand
    {
        public MoveCommand(int nationId, int unitId, HexCoord destination) : base(CommandType.Move, nationId)
        {
            UnitId = unitId;
            Destination = destination;
        }

        public int UnitId { get; }
        public HexCoord Destination { get; }
    }

    public sealed class AttackCommand : GameCommand
    {
        public AttackCommand(int nationId, int attackerId, int defenderId) : base(CommandType.Attack, nationId)
        {
            AttackerId = attackerId;
            DefenderId = defenderId;
        }

        public int AttackerId { get; }
        public int DefenderId { get; }
    }

    public sealed class HealCommand : GameCommand
    {
        public HealCommand(int nationId, int healerId, int targetId) : base(CommandType.Heal, nationId)
        {
            HealerId = healerId;
            TargetId = targetId;
        }

        public int HealerId { get; }
        public int TargetId { get; }
    }

    public sealed class GarrisonCommand : GameCommand
    {
        public GarrisonCommand(int nationId, int unitId) : base(CommandType.Garrison, nationId)
        {
            UnitId = unitId;
        }

        public int UnitId { get; }
    }

    public sealed class AttackWallCommand : GameCommand
    {
        public AttackWallCommand(int nationId, int attackerId, int wallId) : base(CommandType.AttackWall, nationId)
        {
            AttackerId = attackerId;
            WallId = wallId;
        }

        public int AttackerId { get; }
        public int WallId { get; }
    }

    public sealed class OccupyCityCommand : GameCommand
    {
        public OccupyCityCommand(int nationId, int unitId, int cityId) : base(CommandType.OccupyCity, nationId)
        {
            UnitId = unitId;
            CityId = cityId;
        }

        public int UnitId { get; }
        public int CityId { get; }
    }

    public sealed class PromoteUnitCommand : GameCommand
    {
        public PromoteUnitCommand(int nationId, int unitId) : base(CommandType.PromoteUnit, nationId)
        {
            UnitId = unitId;
        }

        public int UnitId { get; }
    }

    public sealed class EndTurnCommand : GameCommand
    {
        public EndTurnCommand(int nationId, int nextNationId) : base(CommandType.EndTurn, nationId)
        {
            NextNationId = nextNationId;
        }

        public int NextNationId { get; }
    }

    public sealed class RecruitUnitCommand : GameCommand
    {
        public RecruitUnitCommand(int nationId, int cityId, UnitType unitType)
            : base(CommandType.RecruitUnit, nationId)
        {
            CityId = cityId;
            UnitType = unitType;
        }

        public int CityId { get; }
        public UnitType UnitType { get; }
    }

    public sealed class ManufactureUnitCommand : GameCommand
    {
        public ManufactureUnitCommand(int nationId, int factoryId, UnitType unitType)
            : base(CommandType.ManufactureUnit, nationId)
        {
            FactoryId = factoryId;
            UnitType = unitType;
        }

        public int FactoryId { get; }
        public UnitType UnitType { get; }
    }
}

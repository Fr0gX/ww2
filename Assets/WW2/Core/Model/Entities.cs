using System;
using System.Collections.Generic;

namespace WW2.Core.Model
{
    [Serializable]
    public sealed class HexCell
    {
        public HexCell(HexCoord coord, TerrainType terrain, int ownerNationId = 0)
        {
            Coord = coord;
            Terrain = terrain;
            OwnerNationId = ownerNationId;
        }

        public HexCoord Coord { get; }
        public TerrainType Terrain { get; set; }
        public int OwnerNationId { get; set; }
        public int? UnitId { get; set; }
        public int? CityId { get; set; }
        public int? BuildingId { get; set; }
        public HashSet<HexCoord> RoadNeighbors { get; } = new HashSet<HexCoord>();
        // Roads remain physically usable by every unit, but their logistics owner
        // determines which nation receives the zero-cost supply benefit.
        public Dictionary<HexCoord, int> RoadOwnerNationIds { get; } = new Dictionary<HexCoord, int>();
        // Turn-start snapshot. More than one nation may cover the same cell.
        public HashSet<int> SupplyNationIds { get; } = new HashSet<int>();
        public Dictionary<int, int> SupplySourceCityIds { get; } = new Dictionary<int, int>();
        public Dictionary<int, float> SupplyCosts { get; } = new Dictionary<int, float>();

        public bool HasRoadTo(HexCoord neighbor) => RoadNeighbors.Contains(neighbor);
        public bool HasOwnedRoadTo(HexCoord neighbor, int nationId) =>
            RoadNeighbors.Contains(neighbor) &&
            RoadOwnerNationIds.TryGetValue(neighbor, out var ownerNationId) && ownerNationId == nationId;
    }

    [Serializable]
    public sealed class UnitState
    {
        public int Id { get; set; }
        public int NationId { get; set; }
        public UnitType Type { get; set; }
        public int Level { get; set; } = 1;
        public int PromotionKills { get; set; }
        public int Health { get; set; }
        public HexCoord Position { get; set; }
        public int RemainingMovement { get; set; }
        public bool HasMoved { get; set; }
        public bool HasAttacked { get; set; }
        public bool IsSuppressed { get; set; }
        public bool IsGarrisoned { get; set; }
        public bool IsPinnedByEnemyControl { get; set; }
        // Entering enemy control can erase movement without spending an attack that
        // was already available. This flag preserves that existing attack only; it
        // never grants or refreshes one.
        public bool HasUnspentAttackAfterControlStop { get; set; }
        public int ConsecutiveTurnsWithoutSupply { get; set; }
        public int LockedSupplyTier { get; set; }
        public bool WasInSupplyAtTurnStart { get; set; } = true;
        public int? LockedSupplySourceCityId { get; set; }
        public float LockedSupplyCost { get; set; }

        public bool CanAttackThisTurn => Health > 0 && !HasAttacked &&
                                         (RemainingMovement > 0 || HasUnspentAttackAfterControlStop);
        public bool CanActThisTurn => Health > 0 && (RemainingMovement > 0 || CanAttackThisTurn);
    }

    [Serializable]
    public sealed class CityState
    {
        public int Id { get; set; }
        public int NationId { get; set; }
        public HexCoord Center { get; set; }
        public int Level { get; set; } = 1;
        public CitySpecialization Specialization { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsFormalOccupation { get; set; } = true;
        public int? OccupyingUnitId { get; set; }
        public int OccupationReadyRound { get; set; }
    }

    [Serializable]
    public sealed class CityWallState
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public HexCoord InnerPosition { get; set; }
        public HexCoord Position => InnerPosition;
        public int Health { get; set; }
        public int MaxHealth { get; set; }
    }

    [Serializable]
    public sealed class BuildingState
    {
        public int Id { get; set; }
        public int CityId { get; set; }
        public int NationId { get; set; }
        public BuildingType Type { get; set; }
        public int Level { get; set; } = 1;
        public HexCoord Position { get; set; }
        public bool IsDisabled { get; set; }
    }

    [Serializable]
    public sealed class NationState
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Economy { get; set; }
        public int Industry { get; set; }
        public int Research { get; set; }
        public Dictionary<int, int> Relations { get; } = new Dictionary<int, int>();
        public Dictionary<int, DiplomaticState> Diplomacy { get; } = new Dictionary<int, DiplomaticState>();
        public HashSet<string> Technologies { get; } = new HashSet<string>();
    }
}

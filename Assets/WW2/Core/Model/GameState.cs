using System.Collections.Generic;

namespace WW2.Core.Model
{
    public sealed class GameState
    {
        public GameState(HexMap map)
        {
            Map = map;
        }

        public HexMap Map { get; }
        public Dictionary<int, UnitState> Units { get; } = new Dictionary<int, UnitState>();
        public Dictionary<int, CityState> Cities { get; } = new Dictionary<int, CityState>();
        public Dictionary<int, CityWallState> CityWalls { get; } = new Dictionary<int, CityWallState>();
        public Dictionary<int, BuildingState> Buildings { get; } = new Dictionary<int, BuildingState>();
        public Dictionary<int, NationState> Nations { get; } = new Dictionary<int, NationState>();
        public int Round { get; set; } = 1;
        public int ActiveNationId { get; set; }
        public int SupplySnapshotGeneration { get; set; }
    }
}

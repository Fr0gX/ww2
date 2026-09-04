using System;
using System.Collections.Generic;
using WW2.Core.Model;

namespace WW2.Core.Systems
{
    public sealed class NationIncome
    {
        public int CityBaseEconomy { get; set; }
        public int DomesticTradeEconomy { get; set; }
        public int EnterpriseEconomy { get; set; }
        public int FactoryIndustry { get; set; }
        public int Economy => CityBaseEconomy + DomesticTradeEconomy + EnterpriseEconomy;
        public int Industry => FactoryIndustry;
    }

    public sealed class EconomySystem
    {
        private static readonly int[] CityEconomy = { 0, 4, 6, 9 };
        private static readonly int[] EnterpriseEconomy = { 0, 5, 8, 12 };
        private static readonly int[] FactoryIndustry = { 0, 9, 15, 24 };
        private const int TradePerConnectedCity = 1;
        private const int MaximumTradePartners = 3;

        private readonly ControlSystem _control;

        public EconomySystem(ControlSystem control)
        {
            _control = control;
        }

        public NationIncome CalculateIncome(GameState state, int nationId)
        {
            var income = new NationIncome();
            foreach (var city in state.Cities.Values)
            {
                if (!IsCityOperational(city, nationId)) continue;
                income.CityBaseEconomy += AtLevel(CityEconomy, city.Level);
                var partners = CountConnectedCities(state, city, nationId);
                income.DomesticTradeEconomy += Math.Min(MaximumTradePartners, partners) * TradePerConnectedCity;
            }

            foreach (var building in state.Buildings.Values)
            {
                if (!IsBuildingOperational(state, building, nationId)) continue;
                switch (building.Type)
                {
                    case BuildingType.CivilEnterprise:
                        income.EnterpriseEconomy += AtLevel(EnterpriseEconomy, building.Level);
                        break;
                    case BuildingType.MilitaryFactory:
                        income.FactoryIndustry += AtLevel(FactoryIndustry, building.Level);
                        break;
                }
            }
            return income;
        }

        public void Collect(GameState state, int nationId)
        {
            if (!state.Nations.TryGetValue(nationId, out var nation)) return;
            var income = CalculateIncome(state, nationId);
            nation.Economy += income.Economy;
            nation.Industry += income.Industry;
        }

        public int GetCityBaseEconomy(CityState city)
        {
            return city == null ? 0 : AtLevel(CityEconomy, city.Level);
        }

        public int GetCityTradeEconomy(GameState state, CityState city)
        {
            return city == null || !IsCityOperational(city, city.NationId)
                ? 0
                : Math.Min(MaximumTradePartners, CountConnectedCities(state, city, city.NationId)) *
                  TradePerConnectedCity;
        }

        public int GetBuildingOutput(BuildingState building)
        {
            if (building == null) return 0;
            return building.Type == BuildingType.CivilEnterprise
                ? AtLevel(EnterpriseEconomy, building.Level)
                : building.Type == BuildingType.MilitaryFactory
                    ? AtLevel(FactoryIndustry, building.Level)
                    : 0;
        }

        public bool IsBuildingOperational(GameState state, BuildingState building, int nationId)
        {
            if (building == null || building.NationId != nationId || building.IsDisabled ||
                !state.Cities.TryGetValue(building.CityId, out var city) ||
                !IsCityOperational(city, nationId)) return false;
            if (!state.Map.TryGet(building.Position, out var cell) || !cell.UnitId.HasValue) return true;
            return !state.Units.TryGetValue(cell.UnitId.Value, out var occupant) ||
                   occupant.Health <= 0 || occupant.NationId == nationId;
        }

        public static bool IsCityOperational(CityState city, int nationId)
        {
            return city != null && city.NationId == nationId && city.IsFormalOccupation && !city.IsDisabled;
        }

        private int CountConnectedCities(GameState state, CityState source, int nationId)
        {
            var visited = new HashSet<HexCoord> { source.Center };
            var frontier = new Queue<HexCoord>();
            frontier.Enqueue(source.Center);
            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                if (!state.Map.TryGet(current, out var cell)) continue;
                foreach (var neighborCoord in cell.RoadNeighbors)
                {
                    if (visited.Contains(neighborCoord) || !state.Map.TryGet(neighborCoord, out var neighbor) ||
                        neighbor.OwnerNationId != 0 && neighbor.OwnerNationId != nationId ||
                        _control.HasEnemyControl(state, neighborCoord, nationId)) continue;
                    visited.Add(neighborCoord);
                    frontier.Enqueue(neighborCoord);
                }
            }

            var count = 0;
            foreach (var city in state.Cities.Values)
                if (city.Id != source.Id && IsCityOperational(city, nationId) && visited.Contains(city.Center)) count++;
            return count;
        }

        private static int AtLevel(IReadOnlyList<int> values, int level)
        {
            return values[Math.Max(1, Math.Min(values.Count - 1, level))];
        }
    }
}

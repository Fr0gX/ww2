using System;
using System.Collections.Generic;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class ProductionSystem
    {
        private readonly RulesCatalog _rules;
        private readonly EconomySystem _economy;

        public ProductionSystem(RulesCatalog rules, EconomySystem economy)
        {
            _rules = rules;
            _economy = economy;
        }

        public bool CanRecruit(GameState state, int nationId, int cityId, UnitType type, out HexCoord deployment,
            out string reason)
        {
            deployment = default;
            if (!state.Cities.TryGetValue(cityId, out var city) ||
                !EconomySystem.IsCityOperational(city, nationId))
            {
                reason = "城市必须由本方稳定控制";
                return false;
            }
            if (_rules.Unit(type).Branch != UnitBranch.Infantry)
            {
                reason = "城市只能制造步兵方向单位";
                return false;
            }
            return CanPayAndDeploy(state, nationId, type, city.Center, out deployment, out reason);
        }

        public bool CanManufacture(GameState state, int nationId, int factoryId, UnitType type,
            out HexCoord deployment, out string reason)
        {
            deployment = default;
            if (!state.Buildings.TryGetValue(factoryId, out var factory) ||
                factory.Type != BuildingType.MilitaryFactory ||
                !_economy.IsBuildingOperational(state, factory, nationId))
            {
                reason = "工厂必须由本方稳定控制且未被占据";
                return false;
            }
            var branch = _rules.Unit(type).Branch;
            if (branch != UnitBranch.Armor && branch != UnitBranch.Artillery)
            {
                reason = "工厂只能制造装甲或火炮单位";
                return false;
            }
            return CanPayAndDeploy(state, nationId, type, factory.Position, out deployment, out reason);
        }

        public bool Recruit(GameState state, int nationId, int cityId, UnitType type)
        {
            return CanRecruit(state, nationId, cityId, type, out var deployment, out _) &&
                   CreateUnit(state, nationId, type, deployment);
        }

        public bool Manufacture(GameState state, int nationId, int factoryId, UnitType type)
        {
            return CanManufacture(state, nationId, factoryId, type, out var deployment, out _) &&
                   CreateUnit(state, nationId, type, deployment);
        }

        private bool CanPayAndDeploy(GameState state, int nationId, UnitType type, HexCoord source,
            out HexCoord deployment, out string reason)
        {
            deployment = default;
            if (!state.Nations.TryGetValue(nationId, out var nation))
            {
                reason = "国家资源不存在";
                return false;
            }
            var definition = _rules.Unit(type);
            if (nation.Economy < definition.EconomyCost || nation.Industry < definition.IndustryCost)
            {
                reason = $"需要经济 {definition.EconomyCost}、工业 {definition.IndustryCost}";
                return false;
            }
            if (!TryFindDeployment(state, nationId, source, out deployment))
            {
                reason = "生产设施及相邻格没有合法部署位置";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool CreateUnit(GameState state, int nationId, UnitType type, HexCoord position)
        {
            if (!state.Nations.TryGetValue(nationId, out var nation)) return false;
            var definition = _rules.Unit(type);
            nation.Economy -= definition.EconomyCost;
            nation.Industry -= definition.IndustryCost;
            var id = 1;
            foreach (var existing in state.Units.Keys) id = Math.Max(id, existing + 1);
            state.Units.Add(id, new UnitState
            {
                Id = id,
                NationId = nationId,
                Type = type,
                Position = position,
                Health = definition.MaxHealth,
                RemainingMovement = 0,
                HasMoved = true,
                HasAttacked = true
            });
            state.Map.Get(position).UnitId = id;
            return true;
        }

        private static bool TryFindDeployment(GameState state, int nationId, HexCoord source, out HexCoord result)
        {
            var candidates = new List<HexCoord> { source };
            for (var direction = 0; direction < 6; direction++) candidates.Add(source.Neighbor(direction));
            foreach (var candidate in candidates)
            {
                if (!state.Map.TryGet(candidate, out var cell) || cell.UnitId.HasValue ||
                    cell.OwnerNationId != nationId) continue;
                result = candidate;
                return true;
            }
            result = default;
            return false;
        }
    }
}

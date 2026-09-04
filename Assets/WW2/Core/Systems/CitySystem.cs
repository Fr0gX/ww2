using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class CitySystem
    {
        private readonly RulesCatalog _rules;

        public CitySystem(RulesCatalog rules)
        {
            _rules = rules;
        }

        public CityState CityAtCenter(GameState state, HexCoord coord)
        {
            foreach (var city in state.Cities.Values)
            {
                if (city.Center.Equals(coord)) return city;
            }
            return null;
        }

        public void BeginOccupationIfCenter(GameState state, UnitState entrant)
        {
            var city = CityAtCenter(state, entrant.Position);
            if (city == null || city.NationId == entrant.NationId) return;

            city.IsDisabled = true;
            city.IsFormalOccupation = false;
            city.OccupyingUnitId = entrant.Id;
            city.OccupationReadyRound = _rules.HasAbility(entrant.Type, UnitAbility.RapidOccupation)
                ? state.Round
                : state.Round + 1;
            SetZoneOwner(state, city, 0);
            foreach (var building in state.Buildings.Values)
            {
                if (building.CityId == city.Id) building.IsDisabled = true;
            }
        }

        public bool CanOccupy(GameState state, UnitState unit, CityState city, out string reason)
        {
            if (unit == null || city == null || unit.NationId == city.NationId ||
                !unit.Position.Equals(city.Center) || city.OccupyingUnitId != unit.Id)
            {
                reason = "单位必须控制敌方市中心";
                return false;
            }

            if (!_rules.HasAbility(unit.Type, UnitAbility.RapidOccupation) &&
                state.Round < city.OccupationReadyRound)
            {
                reason = $"需等到第 {city.OccupationReadyRound} 轮再次行动";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool CompleteOccupation(GameState state, UnitState unit, CityState city)
        {
            if (!CanOccupy(state, unit, city, out _)) return false;
            city.NationId = unit.NationId;
            city.IsFormalOccupation = true;
            city.IsDisabled = false;
            city.OccupyingUnitId = null;
            city.OccupationReadyRound = 0;
            SetZoneOwner(state, city, unit.NationId);
            foreach (var building in state.Buildings.Values)
            {
                if (building.CityId != city.Id) continue;
                building.NationId = unit.NationId;
                building.IsDisabled = false;
            }

            unit.RemainingMovement = 0;
            unit.HasAttacked = true;
            unit.IsPinnedByEnemyControl = false;
            unit.HasUnspentAttackAfterControlStop = false;
            return true;
        }

        public void CancelOccupationIfLeaving(GameState state, UnitState unit, HexCoord destination)
        {
            foreach (var city in state.Cities.Values)
            {
                if (city.OccupyingUnitId == unit.Id && !destination.Equals(city.Center)) RestoreOwnerControl(state, city);
            }
        }

        public void CancelOccupationForUnit(GameState state, int unitId)
        {
            foreach (var city in state.Cities.Values)
            {
                if (city.OccupyingUnitId == unitId) RestoreOwnerControl(state, city);
            }
        }

        private static void RestoreOwnerControl(GameState state, CityState city)
        {
            city.IsDisabled = false;
            city.IsFormalOccupation = true;
            city.OccupyingUnitId = null;
            city.OccupationReadyRound = 0;
            SetZoneOwner(state, city, city.NationId);
            foreach (var building in state.Buildings.Values)
            {
                if (building.CityId == city.Id) building.IsDisabled = false;
            }
        }

        private static void SetZoneOwner(GameState state, CityState city, int nationId)
        {
            foreach (var cell in state.Map.Cells.Values)
            {
                if (city.Center.DistanceTo(cell.Coord) <= city.Level) cell.OwnerNationId = nationId;
            }
        }
    }
}

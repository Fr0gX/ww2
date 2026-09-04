using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class ControlSystem
    {
        private readonly RulesCatalog _rules;

        public ControlSystem(RulesCatalog rules)
        {
            _rules = rules;
        }

        public bool IsControlledBy(GameState state, HexCoord coord, int nationId)
        {
            if (!state.Map.TryGet(coord, out var cell))
            {
                return false;
            }

            if (cell.OwnerNationId == nationId)
            {
                return true;
            }

            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || unit.Health <= 0)
                {
                    continue;
                }

                if (unit.Position.Equals(coord)) return true;
                if (!_rules.HasAbility(unit.Type, UnitAbility.FormsControlZone) || !unit.IsGarrisoned) continue;
                if (unit.Position.DistanceTo(coord) <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        public bool HasEnemyControl(GameState state, HexCoord coord, int nationId)
        {
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId == nationId || unit.Health <= 0)
                {
                    continue;
                }

                if (unit.Position.Equals(coord)) return true;
                if (!_rules.HasAbility(unit.Type, UnitAbility.FormsControlZone) || !unit.IsGarrisoned) continue;
                if (unit.Position.DistanceTo(coord) <= 1)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsInsideFriendlyCity(GameState state, UnitState unit)
        {
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == unit.NationId && city.Center.DistanceTo(unit.Position) <= city.Level)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

using System.Linq;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class TurnSystem
    {
        private readonly RulesCatalog _rules;
        private readonly EconomySystem _economy;
        private readonly SupplySystem _supply;
        private readonly CityWallSystem _walls;

        public TurnSystem(RulesCatalog rules, EconomySystem economy, SupplySystem supply, CityWallSystem walls)
        {
            _rules = rules;
            _economy = economy;
            _supply = supply;
            _walls = walls;
        }

        public void BeginNationTurn(GameState state, int nationId)
        {
            state.ActiveNationId = nationId;
            _economy.Collect(state, nationId);
            // The map-wide coverage snapshot is immutable until the next nation turn.
            _supply.RecalculateCoverage(state);

            foreach (var unit in state.Units.Values.Where(unit => unit.NationId == nationId && unit.Health > 0))
            {
                unit.HasMoved = false;
                unit.HasAttacked = false;
                unit.IsPinnedByEnemyControl = false;
                unit.HasUnspentAttackAfterControlStop = false;
                var supply = _supply.LockTurnStatus(state, unit);
                unit.RemainingMovement = System.Math.Max(1,
                    RuleMath.Round(_rules.Unit(unit.Type).Movement * supply.MovementMultiplier));
                Recover(state, unit);
            }
            _walls.Recover(state, nationId);
        }

        public void EndNationTurn(GameState state, int nextNationId)
        {
            foreach (var unit in state.Units.Values.Where(unit => unit.NationId == state.ActiveNationId))
            {
                unit.IsSuppressed = false;
            }

            if (nextNationId <= state.ActiveNationId)
            {
                state.Round++;
            }

            state.ActiveNationId = nextNationId;
        }

        private void Recover(GameState state, UnitState unit)
        {
            if (!_supply.IsUnitSupplied(state, unit))
            {
                return;
            }

            var recovery = 1;
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == unit.NationId && city.Center.DistanceTo(unit.Position) <= city.Level)
                {
                    recovery = city.Level == 1 ? 2 : city.Level == 2 ? 3 : 4;
                    if (city.Specialization == CitySpecialization.Fortress)
                    {
                        recovery += city.Level >= 3 ? 2 : 1;
                    }

                    break;
                }
            }

            var maxHealth = RuleMath.Round(_rules.Unit(unit.Type).MaxHealth * RuleMath.LevelMultiplier(unit.Level));
            unit.Health = System.Math.Min(maxHealth, unit.Health + recovery);
        }

    }
}

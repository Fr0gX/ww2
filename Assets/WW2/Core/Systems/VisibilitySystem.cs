using System.Collections.Generic;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class VisibilitySystem
    {
        private readonly RulesCatalog _rules;
        private readonly SupplySystem _supply;

        public VisibilitySystem(RulesCatalog rules, SupplySystem supply)
        {
            _rules = rules;
            _supply = supply;
        }

        public HashSet<HexCoord> CalculateVisibleCells(GameState state, int nationId)
        {
            var visible = new HashSet<HexCoord>();
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId == nationId && unit.Health > 0)
                {
                    AddRadius(state.Map, visible, unit.Position, _rules.Unit(unit.Type).Vision);
                }
            }

            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == nationId && !city.IsDisabled)
                {
                    AddRadius(state.Map, visible, city.Center, city.Level + 1);
                }
            }

            // Supply coverage is formal visibility, not merely an intelligence hint.
            foreach (var coord in _supply.CalculateSupplyReach(state, nationId))
            {
                visible.Add(coord);
            }
            return visible;
        }

        private static void AddRadius(HexMap map, HashSet<HexCoord> result, HexCoord center, int radius)
        {
            foreach (var pair in map.Cells)
            {
                if (center.DistanceTo(pair.Key) <= radius)
                {
                    result.Add(pair.Key);
                }
            }
        }
    }
}

using System.Collections.Generic;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class SupplyStatus
    {
        // 0 = normal, 1 = abnormal (one or two missed turn starts),
        // 2 = extreme (three or more consecutive missed turn starts).
        public int Tier { get; set; }
        public int ConsecutiveTurnsWithoutSupply { get; set; }
        public bool IsCovered { get; set; }
        public int? SourceCityId { get; set; }
        public float Cost { get; set; }
        public PathResult Path { get; set; }

        public float AttackMultiplier => Tier == 0 ? 1f : Tier == 1 ? 0.50f : 0.10f;
        public float DefenseMultiplier => AttackMultiplier;
        public float MovementMultiplier => AttackMultiplier;
    }

    public sealed class SupplySystem
    {
        private readonly RulesCatalog _rules;
        private readonly ControlSystem _control;

        public SupplySystem(RulesCatalog rules, ControlSystem control, HexPathfinder pathfinder)
        {
            _rules = rules;
            _control = control;
        }

        public HashSet<int> FindSuppliedCities(GameState state, int nationId)
        {
            var supplied = new HashSet<int>();
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == nationId && !city.IsDisabled && city.IsFormalOccupation)
                    supplied.Add(city.Id);
            }
            return supplied;
        }

        /// <summary>
        /// Rebuilds the complete, overlapping map coverage snapshot for every nation.
        /// This is called once at each nation's turn start and never during actions.
        /// </summary>
        public void RecalculateCoverage(GameState state)
        {
            foreach (var cell in state.Map.Cells.Values)
            {
                cell.SupplyNationIds.Clear();
                cell.SupplySourceCityIds.Clear();
                cell.SupplyCosts.Clear();
            }

            foreach (var city in state.Cities.Values)
            {
                if (city.IsDisabled || !city.IsFormalOccupation ||
                    !state.Map.TryGet(city.Center, out var source) ||
                    !CanCarrySupply(state, source, city.NationId))
                {
                    continue;
                }
                AddCityCoverage(state, city);
            }

            state.SupplySnapshotGeneration++;
        }

        /// <summary>
        /// Locks one unit's supply state for its entire upcoming turn.
        /// Enemy coverage is deliberately ignored.
        /// </summary>
        public SupplyStatus LockTurnStatus(GameState state, UnitState unit)
        {
            EnsureCoverageSnapshot(state);
            var covered = state.Map.TryGet(unit.Position, out var cell) &&
                          cell.SupplyNationIds.Contains(unit.NationId);
            if (covered)
            {
                unit.ConsecutiveTurnsWithoutSupply = 0;
                unit.LockedSupplyTier = 0;
                unit.WasInSupplyAtTurnStart = true;
                unit.LockedSupplySourceCityId = cell.SupplySourceCityIds.TryGetValue(unit.NationId, out var sourceId)
                    ? sourceId
                    : (int?)null;
                unit.LockedSupplyCost = cell.SupplyCosts.TryGetValue(unit.NationId, out var cost) ? cost : 0;
            }
            else
            {
                unit.ConsecutiveTurnsWithoutSupply++;
                unit.LockedSupplyTier = unit.ConsecutiveTurnsWithoutSupply >= 3 ? 2 : 1;
                unit.WasInSupplyAtTurnStart = false;
                unit.LockedSupplySourceCityId = null;
                unit.LockedSupplyCost = float.PositiveInfinity;
            }

            return GetStatus(state, unit);
        }

        public bool IsUnitSupplied(GameState state, UnitState unit)
        {
            return unit != null && unit.LockedSupplyTier == 0;
        }

        /// <summary>Returns the unit's locked turn state, never its live position.</summary>
        public SupplyStatus GetStatus(GameState state, UnitState unit)
        {
            if (unit == null) return null;
            return new SupplyStatus
            {
                Tier = unit.LockedSupplyTier,
                ConsecutiveTurnsWithoutSupply = unit.ConsecutiveTurnsWithoutSupply,
                IsCovered = unit.WasInSupplyAtTurnStart,
                SourceCityId = unit.LockedSupplySourceCityId,
                Cost = unit.LockedSupplyCost
            };
        }

        /// <summary>
        /// Returns coverage of a cell in the frozen snapshot. An uncovered cell has
        /// tier 1 because persistence belongs to units, not terrain.
        /// </summary>
        public SupplyStatus GetStatusAt(GameState state, int nationId, HexCoord position)
        {
            EnsureCoverageSnapshot(state);
            if (!state.Map.TryGet(position, out var cell) || !cell.SupplyNationIds.Contains(nationId))
            {
                return new SupplyStatus { Tier = 1, IsCovered = false, Cost = float.PositiveInfinity };
            }

            return new SupplyStatus
            {
                Tier = 0,
                IsCovered = true,
                SourceCityId = cell.SupplySourceCityIds.TryGetValue(nationId, out var sourceId)
                    ? sourceId
                    : (int?)null,
                Cost = cell.SupplyCosts.TryGetValue(nationId, out var cost) ? cost : 0
            };
        }

        public HashSet<int> GetCoverageNations(GameState state, HexCoord position)
        {
            EnsureCoverageSnapshot(state);
            return state.Map.TryGet(position, out var cell)
                ? new HashSet<int>(cell.SupplyNationIds)
                : new HashSet<int>();
        }

        public HashSet<HexCoord> CalculateSupplyReach(GameState state, int nationId)
        {
            EnsureCoverageSnapshot(state);
            var result = new HashSet<HexCoord>();
            foreach (var cell in state.Map.Cells.Values)
                if (cell.SupplyNationIds.Contains(nationId)) result.Add(cell.Coord);
            return result;
        }

        // Compatibility boundaries for the static AI planner. The turn snapshot
        // already is the fast field, so these calls perform no extra analysis.
        public void BeginFastEvaluation(GameState state) => EnsureCoverageSnapshot(state);
        public void EndFastEvaluation() { }

        private void EnsureCoverageSnapshot(GameState state)
        {
            if (state.SupplySnapshotGeneration <= 0) RecalculateCoverage(state);
        }

        private void AddCityCoverage(GameState state, CityState city)
        {
            var costs = new Dictionary<HexCoord, float> { [city.Center] = 0f };
            var frontier = new List<HexCoord> { city.Center };
            var maximumCost = SupplyRange(city.Level);
            while (frontier.Count > 0)
            {
                var currentIndex = 0;
                for (var i = 1; i < frontier.Count; i++)
                    if (costs[frontier[i]] < costs[frontier[currentIndex]]) currentIndex = i;
                var current = frontier[currentIndex];
                frontier.RemoveAt(currentIndex);
                var currentCost = costs[current];
                var cell = state.Map.Get(current);
                RegisterCoverage(cell, city, currentCost);

                foreach (var neighbor in state.Map.GetNeighbors(current))
                {
                    if (!CanCarrySupply(state, neighbor, city.NationId)) continue;
                    if (CityWallSystem.FindIntactBlockingEntry(state, city.NationId, current,
                            neighbor.Coord) != null)
                    {
                        continue;
                    }
                    var nextCost = currentCost + SupplyEdgeCost(cell, neighbor, city.NationId);
                    if (nextCost > maximumCost ||
                        costs.TryGetValue(neighbor.Coord, out var known) && known <= nextCost)
                    {
                        continue;
                    }
                    costs[neighbor.Coord] = nextCost;
                    if (!frontier.Contains(neighbor.Coord)) frontier.Add(neighbor.Coord);
                }
            }
        }

        private static void RegisterCoverage(HexCell cell, CityState city, float cost)
        {
            cell.SupplyNationIds.Add(city.NationId);
            if (cell.SupplyCosts.TryGetValue(city.NationId, out var knownCost) &&
                (knownCost < cost || knownCost == cost &&
                 cell.SupplySourceCityIds.TryGetValue(city.NationId, out var knownSource) && knownSource <= city.Id))
            {
                return;
            }
            cell.SupplyCosts[city.NationId] = cost;
            cell.SupplySourceCityIds[city.NationId] = city.Id;
        }

        private bool CanCarrySupply(GameState state, HexCell cell, int nationId)
        {
            // Ownership alone does not block coverage. Hostile bodies and projected
            // control do, allowing overlap while naturally producing pockets.
            return !_control.HasEnemyControl(state, cell.Coord, nationId);
        }

        private float SupplyEdgeCost(HexCell from, HexCell to, int nationId)
        {
            // A road is still a movement road for either side. Its zero-cost
            // logistics capacity belongs to the nation that built it, preventing
            // an enemy supply field from jumping through the opposing rear network.
            if (from.HasOwnedRoadTo(to.Coord, nationId) && to.HasOwnedRoadTo(from.Coord, nationId)) return 0.5f;
            return _rules.Terrain(to.Terrain).SupplyCost;
        }

        public static float SupplyRange(int cityLevel)
        {
            switch (cityLevel)
            {
                case 1: return 9f;
                case 2: return 12f;
                default: return 15f;
            }
        }
    }
}

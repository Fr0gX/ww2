using System.Collections.Generic;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class MovementSystem
    {
        private readonly RulesCatalog _rules;
        private readonly ControlSystem _control;
        private readonly HexPathfinder _pathfinder;
        private readonly CityWallSystem _walls;
        private readonly VisibilitySystem _visibility;

        public MovementSystem(RulesCatalog rules, ControlSystem control, HexPathfinder pathfinder,
            CityWallSystem walls, VisibilitySystem visibility)
        {
            _rules = rules;
            _control = control;
            _pathfinder = pathfinder;
            _walls = walls;
            _visibility = visibility;
        }

        public PathResult FindPath(GameState state, UnitState unit, HexCoord destination)
        {
            return FindReachablePaths(state, unit).TryGetValue(destination, out var path) ? path : null;
        }

        public bool TryMove(GameState state, UnitState unit, HexCoord destination)
        {
            if (!CanMove(state, unit, destination, out var path))
            {
                return false;
            }

            return TryMove(state, unit, destination, path);
        }

        internal bool TryMove(GameState state, UnitState unit, HexCoord destination, PathResult path)
        {
            if (path == null || path.Cells.Count < 2 || !path.Cells[0].Equals(unit.Position) ||
                !path.Cells[path.Cells.Count - 1].Equals(destination) || path.Cost > unit.RemainingMovement ||
                !state.Map.TryGet(destination, out var destinationCell) || destinationCell.UnitId.HasValue ||
                CrossesEnemyControlBeforeDestination(state, unit, path))
            {
                return false;
            }

            var origin = state.Map.Get(unit.Position);
            origin.UnitId = null;
            destinationCell.UnitId = unit.Id;
            unit.Position = destination;
            unit.RemainingMovement = System.Math.Max(0, unit.RemainingMovement - path.Cost);
            unit.HasMoved = true;
            unit.IsGarrisoned = false;
            unit.IsPinnedByEnemyControl = false;
            unit.HasUnspentAttackAfterControlStop = false;
            if (unit.IsSuppressed)
            {
                unit.IsSuppressed = false;
            }

            if (_control.HasEnemyControl(state, destination, unit.NationId))
            {
                // Control removes only movement. If this move would have left action points,
                // the unit's already-unspent attack remains available; control grants nothing.
                unit.IsPinnedByEnemyControl = unit.RemainingMovement > 0;
                unit.HasUnspentAttackAfterControlStop = !unit.HasAttacked && unit.RemainingMovement > 0;
                unit.RemainingMovement = 0;
            }

            return true;
        }

        public bool CanMove(GameState state, UnitState unit, HexCoord destination, out PathResult path)
        {
            path = null;
            if (unit.RemainingMovement <= 0 || !state.Map.TryGet(destination, out var target) || target.UnitId.HasValue)
            {
                return false;
            }
            return FindReachablePaths(state, unit).TryGetValue(destination, out path);
        }

        public Dictionary<HexCoord, PathResult> FindReachablePaths(GameState state, UnitState unit)
        {
            var result = new Dictionary<HexCoord, PathResult>();
            if (unit == null || unit.RemainingMovement <= 0 || !state.Map.TryGet(unit.Position, out _))
            {
                return result;
            }

            var costs = new Dictionary<HexCoord, int> { [unit.Position] = 0 };
            var previous = new Dictionary<HexCoord, HexCoord>();
            var open = new List<HexCoord> { unit.Position };
            var visible = _visibility.CalculateVisibleCells(state, unit.NationId);
            while (open.Count > 0)
            {
                var currentIndex = 0;
                for (var i = 1; i < open.Count; i++)
                {
                    if (costs[open[i]] < costs[open[currentIndex]]) currentIndex = i;
                }

                var current = open[currentIndex];
                open.RemoveAt(currentIndex);
                var currentCost = costs[current];
                if (!current.Equals(unit.Position) && _control.HasEnemyControl(state, current, unit.NationId))
                {
                    continue;
                }

                var fromCell = state.Map.Get(current);
                foreach (var targetCell in state.Map.GetNeighbors(current))
                {
                    if (!visible.Contains(targetCell.Coord)) continue;
                    if (targetCell.UnitId.HasValue &&
                        state.Units.TryGetValue(targetCell.UnitId.Value, out var occupant) &&
                        occupant.NationId != unit.NationId)
                    {
                        continue;
                    }
                    var nextCost = currentCost + MovementCost(fromCell, targetCell, unit.Type);
                    if (nextCost > unit.RemainingMovement ||
                        costs.TryGetValue(targetCell.Coord, out var known) && known <= nextCost)
                    {
                        continue;
                    }

                    if (CityWallSystem.FindIntactBlockingEntry(state, unit.NationId, current,
                            targetCell.Coord) != null)
                    {
                        continue;
                    }

                    costs[targetCell.Coord] = nextCost;
                    previous[targetCell.Coord] = current;
                    if (!open.Contains(targetCell.Coord)) open.Add(targetCell.Coord);
                }
            }

            foreach (var pair in costs)
            {
                if (pair.Key.Equals(unit.Position)) continue;
                // Friendly units may be crossed but remain invalid destinations.
                if (state.Map.Get(pair.Key).UnitId.HasValue) continue;
                var cells = new List<HexCoord> { pair.Key };
                var cursor = pair.Key;
                while (!cursor.Equals(unit.Position))
                {
                    cursor = previous[cursor];
                    cells.Add(cursor);
                }
                cells.Reverse();
                result[pair.Key] = new PathResult(cells, pair.Value);
            }
            return result;
        }

        private bool CrossesEnemyControlBeforeDestination(GameState state, UnitState unit, PathResult path)
        {
            for (var i = 1; i < path.Cells.Count - 1; i++)
            {
                if (_control.HasEnemyControl(state, path.Cells[i], unit.NationId)) return true;
            }
            return false;
        }

        private int MovementCost(HexCell from, HexCell to, UnitType type)
        {
            if (from.HasRoadTo(to.Coord) && to.HasRoadTo(from.Coord))
            {
                return 1;
            }

            if (_rules.HasAbility(type, UnitAbility.IgnoresTerrainMovement))
            {
                return 1;
            }

            var terrain = _rules.Terrain(to.Terrain);
            return _rules.Unit(type).Branch == UnitBranch.Infantry ? terrain.FootCost : terrain.MechanicalCost;
        }
    }
}

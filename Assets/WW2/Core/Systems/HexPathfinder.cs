using System;
using System.Collections.Generic;
using WW2.Core.Model;

namespace WW2.Core.Systems
{
    public sealed class PathResult
    {
        public PathResult(IReadOnlyList<HexCoord> cells, int cost)
        {
            Cells = cells;
            Cost = cost;
        }

        public IReadOnlyList<HexCoord> Cells { get; }
        public int Cost { get; }
    }

    public sealed class HexPathfinder
    {
        public PathResult FindLowestCost(
            HexMap map,
            HexCoord start,
            HexCoord goal,
            Func<HexCell, bool> canEnter,
            Func<HexCell, HexCell, int> edgeCost)
        {
            var costs = new Dictionary<HexCoord, int> { [start] = 0 };
            var previous = new Dictionary<HexCoord, HexCoord>();
            var open = new List<HexCoord> { start };

            while (open.Count > 0)
            {
                var currentIndex = 0;
                for (var i = 1; i < open.Count; i++)
                {
                    if (costs[open[i]] < costs[open[currentIndex]])
                    {
                        currentIndex = i;
                    }
                }

                var current = open[currentIndex];
                open.RemoveAt(currentIndex);
                if (current.Equals(goal))
                {
                    return BuildPath(previous, start, goal, costs[goal]);
                }

                var from = map.Get(current);
                foreach (var neighbor in map.GetNeighbors(current))
                {
                    if (!neighbor.Coord.Equals(goal) && !canEnter(neighbor))
                    {
                        continue;
                    }

                    var candidate = costs[current] + edgeCost(from, neighbor);
                    if (costs.TryGetValue(neighbor.Coord, out var known) && candidate >= known)
                    {
                        continue;
                    }

                    costs[neighbor.Coord] = candidate;
                    previous[neighbor.Coord] = current;
                    if (!open.Contains(neighbor.Coord))
                    {
                        open.Add(neighbor.Coord);
                    }
                }
            }

            return null;
        }

        private static PathResult BuildPath(Dictionary<HexCoord, HexCoord> previous, HexCoord start, HexCoord goal, int cost)
        {
            var path = new List<HexCoord> { goal };
            var cursor = goal;
            while (!cursor.Equals(start))
            {
                cursor = previous[cursor];
                path.Add(cursor);
            }

            path.Reverse();
            return new PathResult(path, cost);
        }
    }
}


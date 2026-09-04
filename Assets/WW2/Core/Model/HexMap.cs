using System.Collections.Generic;

namespace WW2.Core.Model
{
    public sealed class HexMap
    {
        private readonly Dictionary<HexCoord, HexCell> _cells = new Dictionary<HexCoord, HexCell>();

        public IReadOnlyDictionary<HexCoord, HexCell> Cells => _cells;

        public void Add(HexCell cell) => _cells.Add(cell.Coord, cell);

        public bool TryGet(HexCoord coord, out HexCell cell) => _cells.TryGetValue(coord, out cell);

        public HexCell Get(HexCoord coord) => _cells[coord];

        public IEnumerable<HexCell> GetNeighbors(HexCoord coord)
        {
            for (var direction = 0; direction < 6; direction++)
            {
                if (_cells.TryGetValue(coord.Neighbor(direction), out var neighbor))
                {
                    yield return neighbor;
                }
            }
        }

        public static HexMap CreateRectangle(int width, int height, int defaultOwner = 0)
        {
            var map = new HexMap();
            for (var r = 0; r < height; r++)
            {
                for (var q = 0; q < width; q++)
                {
                    map.Add(new HexCell(new HexCoord(q, r), TerrainType.Plain, defaultOwner));
                }
            }

            return map;
        }

        public static HexMap CreateHexagon(int radius, int defaultOwner = 0)
        {
            var map = new HexMap();
            for (var q = -radius; q <= radius; q++)
            {
                var minimumR = System.Math.Max(-radius, -q - radius);
                var maximumR = System.Math.Min(radius, -q + radius);
                for (var r = minimumR; r <= maximumR; r++)
                {
                    map.Add(new HexCell(new HexCoord(q, r), TerrainType.Plain, defaultOwner));
                }
            }
            return map;
        }
    }
}

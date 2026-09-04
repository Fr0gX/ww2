using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;

namespace WW2.Runtime
{
    public static class PrototypeScenario
    {
        public static GameState Create(RulesCatalog rules = null)
        {
            rules = rules ?? RulesCatalog.CreateDefault();
            // Radius 14 yields 631 cells: more than twice the former radius-9 board.
            // The battlefield is split into mirrored northern and southern fronts,
            // with enough depth behind each front for supply warfare and maneuver.
            var map = HexMap.CreateHexagon(14);
            PaintTerrain(map);
            var state = new GameState(map) { ActiveNationId = 1 };

            state.Nations.Add(1, new NationState { Id = 1, Name = "Blue" });
            state.Nations.Add(2, new NationState { Id = 2, Name = "Red" });

            AddCity(state, 1, 1, new HexCoord(-12, 6), 2);
            AddCity(state, 2, 2, new HexCoord(12, -6), 2);
            AddCity(state, 3, 1, new HexCoord(-4, -6), 1);
            AddCity(state, 4, 2, new HexCoord(4, -10), 1);
            AddCity(state, 5, 1, new HexCoord(-4, 10), 1);
            AddCity(state, 6, 2, new HexCoord(4, 6), 1);
            // Roads represent infrastructure already built by each side. The prototype
            // keeps only a sparse domestic network and never connects hostile cities.
            AddRoad(state, 1, state.Cities[1].Center, state.Cities[3].Center);
            AddRoad(state, 1, state.Cities[1].Center, state.Cities[5].Center);
            AddRoad(state, 2, state.Cities[2].Center, state.Cities[4].Center);
            AddRoad(state, 2, state.Cities[2].Center, state.Cities[6].Center);

            AddBuilding(state, 1, 1, 1, BuildingType.CivilEnterprise, new HexCoord(-11, 5));
            AddBuilding(state, 2, 1, 1, BuildingType.MilitaryFactory, new HexCoord(-11, 6));
            AddBuilding(state, 3, 2, 2, BuildingType.CivilEnterprise, new HexCoord(11, -6));
            AddBuilding(state, 4, 2, 2, BuildingType.MilitaryFactory, new HexCoord(11, -5));
            AddBuilding(state, 5, 3, 1, BuildingType.CivilEnterprise, new HexCoord(-5, -5));
            AddBuilding(state, 6, 4, 2, BuildingType.CivilEnterprise, new HexCoord(5, -10));
            AddBuilding(state, 7, 5, 1, BuildingType.MilitaryFactory, new HexCoord(-5, 10));
            AddBuilding(state, 8, 6, 2, BuildingType.MilitaryFactory, new HexCoord(5, 5));

            AddUnit(state, rules, 1, 1, UnitType.MainInfantry, new HexCoord(-3, -6));
            AddUnit(state, rules, 2, 1, UnitType.LightArtillery, new HexCoord(-5, -6));
            AddUnit(state, rules, 3, 1, UnitType.LightArmor, new HexCoord(-6, -4));
            AddUnit(state, rules, 4, 1, UnitType.Medic, new HexCoord(-5, -7));
            AddUnit(state, rules, 5, 1, UnitType.MainInfantry, new HexCoord(-4, -6), true);
            AddUnit(state, rules, 6, 1, UnitType.MainInfantry, new HexCoord(-2, -7));
            AddUnit(state, rules, 7, 1, UnitType.MainInfantry, new HexCoord(-3, 9));
            AddUnit(state, rules, 8, 1, UnitType.LightArmor, new HexCoord(-5, 9));
            AddUnit(state, rules, 9, 1, UnitType.LightArtillery, new HexCoord(-6, 10));
            AddUnit(state, rules, 10, 1, UnitType.MainInfantry, new HexCoord(-4, -4));
            AddUnit(state, rules, 11, 1, UnitType.MainInfantry, new HexCoord(-3, -5));
            AddUnit(state, rules, 12, 1, UnitType.LightArtillery, new HexCoord(-6, -5));
            AddUnit(state, rules, 25, 1, UnitType.MainInfantry, new HexCoord(-4, 10), true);
            AddUnit(state, rules, 26, 1, UnitType.MainInfantry, new HexCoord(-2, 8));
            AddUnit(state, rules, 27, 1, UnitType.MainInfantry, new HexCoord(-3, 8));
            AddUnit(state, rules, 28, 1, UnitType.MainInfantry, new HexCoord(-4, 8));
            AddUnit(state, rules, 29, 1, UnitType.LightArtillery, new HexCoord(-6, 11));
            AddUnit(state, rules, 30, 1, UnitType.LightArmor, new HexCoord(-5, 11));
            AddUnit(state, rules, 31, 1, UnitType.Medic, new HexCoord(-2, 9));

            AddMirroredUnit(state, rules, 13, UnitType.MainInfantry, new HexCoord(-3, -6));
            AddMirroredUnit(state, rules, 14, UnitType.LightArtillery, new HexCoord(-5, -6));
            AddMirroredUnit(state, rules, 15, UnitType.LightArmor, new HexCoord(-6, -4));
            AddMirroredUnit(state, rules, 16, UnitType.Medic, new HexCoord(-5, -7));
            AddMirroredUnit(state, rules, 17, UnitType.MainInfantry, new HexCoord(-4, -6), true);
            AddMirroredUnit(state, rules, 18, UnitType.MainInfantry, new HexCoord(-2, -7));
            AddMirroredUnit(state, rules, 19, UnitType.MainInfantry, new HexCoord(-3, 9));
            AddMirroredUnit(state, rules, 20, UnitType.LightArmor, new HexCoord(-5, 9));
            AddMirroredUnit(state, rules, 21, UnitType.LightArtillery, new HexCoord(-6, 10));
            AddMirroredUnit(state, rules, 22, UnitType.MainInfantry, new HexCoord(-4, -4));
            AddMirroredUnit(state, rules, 23, UnitType.MainInfantry, new HexCoord(-3, -5));
            AddMirroredUnit(state, rules, 24, UnitType.LightArtillery, new HexCoord(-6, -5));
            AddMirroredUnit(state, rules, 32, UnitType.MainInfantry, new HexCoord(-4, 10), true);
            AddMirroredUnit(state, rules, 33, UnitType.MainInfantry, new HexCoord(-2, 8));
            AddMirroredUnit(state, rules, 34, UnitType.MainInfantry, new HexCoord(-3, 8));
            AddMirroredUnit(state, rules, 35, UnitType.MainInfantry, new HexCoord(-4, 8));
            AddMirroredUnit(state, rules, 36, UnitType.LightArtillery, new HexCoord(-6, 11));
            AddMirroredUnit(state, rules, 37, UnitType.LightArmor, new HexCoord(-5, 11));
            AddMirroredUnit(state, rules, 38, UnitType.Medic, new HexCoord(-2, 9));

            CityWallSystem.InitializeCityWalls(state);

            return state;
        }

        private static void PaintTerrain(HexMap map)
        {
            // Every seed is reflected across both the horizontal and vertical map
            // axes. Terrain therefore creates real choices without giving either
            // nation or either front an accidental geometric advantage.
            PaintSymmetric(map, TerrainType.Forest,
                new HexCoord(-12, 0), new HexCoord(-11, -1), new HexCoord(-10, -1),
                new HexCoord(-9, -2), new HexCoord(-8, -2), new HexCoord(-8, -3),
                new HexCoord(-7, -3), new HexCoord(-3, -8), new HexCoord(-2, -9),
                new HexCoord(-1, -10));
            PaintSymmetric(map, TerrainType.Hill,
                new HexCoord(-7, -1), new HexCoord(-6, -2), new HexCoord(-5, -2),
                new HexCoord(-5, -3), new HexCoord(-3, -7), new HexCoord(-2, -7));
            PaintSymmetric(map, TerrainType.Mountain,
                new HexCoord(-10, 2), new HexCoord(-9, 1), new HexCoord(-8, 0),
                new HexCoord(-7, 0), new HexCoord(-6, -1), new HexCoord(-5, -1));
            PaintSymmetric(map, TerrainType.Marsh,
                new HexCoord(-4, 1), new HexCoord(-3, 0), new HexCoord(-2, -1),
                new HexCoord(-1, -1), new HexCoord(-1, -8));
        }

        private static void PaintSymmetric(HexMap map, TerrainType terrain, params HexCoord[] seeds)
        {
            foreach (var seed in seeds)
            {
                PaintIfPresent(map, seed, terrain);
                PaintIfPresent(map, MirrorHorizontal(seed), terrain);
                PaintIfPresent(map, new HexCoord(seed.Q, -seed.R - seed.Q), terrain);
                PaintIfPresent(map, new HexCoord(-seed.Q, -seed.R), terrain);
            }
        }

        private static void PaintIfPresent(HexMap map, HexCoord coord, TerrainType terrain)
        {
            if (map.TryGet(coord, out var cell)) cell.Terrain = terrain;
        }

        private static HexCoord MirrorHorizontal(HexCoord coord)
        {
            return new HexCoord(-coord.Q, coord.R + coord.Q);
        }

        private static void AddCity(GameState state, int id, int nationId, HexCoord center, int level)
        {
            state.Cities.Add(id, new CityState
            {
                Id = id,
                NationId = nationId,
                Center = center,
                Level = level,
                IsFormalOccupation = true
            });
            state.Map.Get(center).CityId = id;
            state.Map.Get(center).OwnerNationId = nationId;

            foreach (var cell in state.Map.Cells.Values)
            {
                if (center.DistanceTo(cell.Coord) <= level)
                {
                    cell.OwnerNationId = nationId;
                }
            }
        }

        private static void AddRoad(GameState state, int nationId, HexCoord start, HexCoord end)
        {
            var cursor = start;
            while (!cursor.Equals(end))
            {
                var next = cursor;
                var bestDistance = cursor.DistanceTo(end);
                for (var direction = 0; direction < 6; direction++)
                {
                    var candidate = cursor.Neighbor(direction);
                    if (!state.Map.TryGet(candidate, out _) || candidate.DistanceTo(end) >= bestDistance) continue;
                    next = candidate;
                    bestDistance = candidate.DistanceTo(end);
                }
                if (next.Equals(cursor)) break;
                state.Map.Get(cursor).RoadNeighbors.Add(next);
                state.Map.Get(next).RoadNeighbors.Add(cursor);
                state.Map.Get(cursor).RoadOwnerNationIds[next] = nationId;
                state.Map.Get(next).RoadOwnerNationIds[cursor] = nationId;
                cursor = next;
            }
        }

        private static void AddBuilding(GameState state, int id, int cityId, int nationId, BuildingType type,
            HexCoord position)
        {
            state.Buildings.Add(id, new BuildingState
            {
                Id = id,
                CityId = cityId,
                NationId = nationId,
                Type = type,
                Level = 1,
                Position = position
            });
            state.Map.Get(position).BuildingId = id;
        }

        private static void AddUnit(GameState state, RulesCatalog rules, int id, int nationId, UnitType type, HexCoord position,
            bool garrisoned = false)
        {
            var definition = rules.Unit(type);
            state.Units.Add(id, new UnitState
            {
                Id = id,
                NationId = nationId,
                Type = type,
                Position = position,
                Health = definition.MaxHealth,
                RemainingMovement = garrisoned ? 0 : definition.Movement,
                HasAttacked = garrisoned,
                IsGarrisoned = garrisoned
            });
            state.Map.Get(position).UnitId = id;
        }

        private static void AddMirroredUnit(GameState state, RulesCatalog rules, int id, UnitType type,
            HexCoord bluePosition, bool garrisoned = false)
        {
            AddUnit(state, rules, id, 2, type, MirrorHorizontal(bluePosition), garrisoned);
        }
    }
}

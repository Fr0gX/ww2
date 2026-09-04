namespace WW2.Core.Model
{
    public static class GameStateCloner
    {
        public static GameState Clone(GameState source)
        {
            var map = new HexMap();
            foreach (var sourceCell in source.Map.Cells.Values)
            {
                map.Add(new HexCell(sourceCell.Coord, sourceCell.Terrain, sourceCell.OwnerNationId)
                {
                    UnitId = sourceCell.UnitId,
                    CityId = sourceCell.CityId,
                    BuildingId = sourceCell.BuildingId
                });
            }
            foreach (var sourceCell in source.Map.Cells.Values)
            {
                var targetCell = map.Get(sourceCell.Coord);
                foreach (var neighbor in sourceCell.RoadNeighbors) targetCell.RoadNeighbors.Add(neighbor);
                foreach (var pair in sourceCell.RoadOwnerNationIds)
                    targetCell.RoadOwnerNationIds.Add(pair.Key, pair.Value);
                foreach (var nationId in sourceCell.SupplyNationIds) targetCell.SupplyNationIds.Add(nationId);
                foreach (var pair in sourceCell.SupplySourceCityIds)
                    targetCell.SupplySourceCityIds.Add(pair.Key, pair.Value);
                foreach (var pair in sourceCell.SupplyCosts) targetCell.SupplyCosts.Add(pair.Key, pair.Value);
            }

            var clone = new GameState(map)
            {
                Round = source.Round,
                ActiveNationId = source.ActiveNationId,
                SupplySnapshotGeneration = source.SupplySnapshotGeneration
            };
            foreach (var pair in source.Units)
            {
                var unit = pair.Value;
                clone.Units.Add(pair.Key, new UnitState
                {
                    Id = unit.Id,
                    NationId = unit.NationId,
                    Type = unit.Type,
                    Level = unit.Level,
                    PromotionKills = unit.PromotionKills,
                    Health = unit.Health,
                    Position = unit.Position,
                    RemainingMovement = unit.RemainingMovement,
                    HasMoved = unit.HasMoved,
                    HasAttacked = unit.HasAttacked,
                    IsSuppressed = unit.IsSuppressed,
                    IsGarrisoned = unit.IsGarrisoned,
                    IsPinnedByEnemyControl = unit.IsPinnedByEnemyControl,
                    HasUnspentAttackAfterControlStop = unit.HasUnspentAttackAfterControlStop,
                    ConsecutiveTurnsWithoutSupply = unit.ConsecutiveTurnsWithoutSupply,
                    LockedSupplyTier = unit.LockedSupplyTier,
                    WasInSupplyAtTurnStart = unit.WasInSupplyAtTurnStart,
                    LockedSupplySourceCityId = unit.LockedSupplySourceCityId,
                    LockedSupplyCost = unit.LockedSupplyCost
                });
            }
            foreach (var pair in source.Cities)
            {
                var city = pair.Value;
                clone.Cities.Add(pair.Key, new CityState
                {
                    Id = city.Id,
                    NationId = city.NationId,
                    Center = city.Center,
                    Level = city.Level,
                    Specialization = city.Specialization,
                    IsDisabled = city.IsDisabled,
                    IsFormalOccupation = city.IsFormalOccupation,
                    OccupyingUnitId = city.OccupyingUnitId,
                    OccupationReadyRound = city.OccupationReadyRound
                });
            }
            foreach (var pair in source.CityWalls)
            {
                var wall = pair.Value;
                clone.CityWalls.Add(pair.Key, new CityWallState
                {
                    Id = wall.Id,
                    CityId = wall.CityId,
                    InnerPosition = wall.InnerPosition,
                    Health = wall.Health,
                    MaxHealth = wall.MaxHealth
                });
            }
            foreach (var pair in source.Buildings)
            {
                var building = pair.Value;
                clone.Buildings.Add(pair.Key, new BuildingState
                {
                    Id = building.Id,
                    CityId = building.CityId,
                    NationId = building.NationId,
                    Type = building.Type,
                    Level = building.Level,
                    Position = building.Position,
                    IsDisabled = building.IsDisabled
                });
            }
            foreach (var pair in source.Nations)
            {
                var nation = pair.Value;
                var target = new NationState
                {
                    Id = nation.Id,
                    Name = nation.Name,
                    Economy = nation.Economy,
                    Industry = nation.Industry,
                    Research = nation.Research
                };
                foreach (var relation in nation.Relations) target.Relations.Add(relation.Key, relation.Value);
                foreach (var diplomacy in nation.Diplomacy) target.Diplomacy.Add(diplomacy.Key, diplomacy.Value);
                foreach (var technology in nation.Technologies) target.Technologies.Add(technology);
                clone.Nations.Add(pair.Key, target);
            }
            return clone;
        }
    }
}

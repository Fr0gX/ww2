using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using WW2.Core.AI;
using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;
using WW2.Runtime;

namespace WW2.Editor
{
    public static class AiDiagnostics
    {
        public static void Run()
        {
            var simulation = new GameSimulation(RulesCatalog.CreateDefault());
            var state = PrototypeScenario.Create();
            var planner = new AiPlanner(simulation, new StrategicEvaluator());
            var lines = new List<string>();
            VerifyEdgeWallRules(simulation, lines);
            VerifySupplyRules(simulation, lines);
            VerifyUnitFramework(simulation, lines);
            VerifyPromotionRules(simulation, lines);
            VerifyCombatOutcomes(simulation, lines);
            VerifyEnemyUnitVisuals(lines);
            VerifyControlPinning(simulation, lines);
            VerifyDevelopmentFramework(simulation, lines);
            VerifyDefensivePlanning(simulation, lines);

            simulation.Turns.BeginNationTurn(state, 2);
            long planningMilliseconds = 0;
            var plannedActions = 0;
            var skippedActions = 0;
            for (var turn = 0; turn < 4; turn++)
            {
                var acted = new HashSet<int>();
                var actions = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                List<AiTurnPlanEntry> plan;
                simulation.Supply.BeginFastEvaluation(state);
                try
                {
                    plan = planner.PlanTurnStatic(state, 2);
                }
                finally
                {
                    simulation.Supply.EndFastEvaluation();
                    watch.Stop();
                    planningMilliseconds += watch.ElapsedMilliseconds;
                }
                lines.Add($"STATIC_PLAN round={state.Round} commands={plan.Count} planMs={watch.ElapsedMilliseconds}");
                foreach (var entry in plan)
                {
                    var command = entry.Command;
                    if (!simulation.TryExecute(state, command))
                    {
                        skippedActions++;
                        lines.Add($"STATIC_COMMAND_SKIPPED round={state.Round} {CommandDetails(command)}");
                        continue;
                    }
                    var unitId = UnitId(command);
                    lines.Add($"round={state.Round} action={actions + 1} unit={unitId} {entry.DecisionTrace}");
                    if (unitId.HasValue && state.Units.TryGetValue(unitId.Value, out var actedUnit))
                    {
                        var supply = simulation.Supply.GetStatus(state, actedUnit);
                        lines.Add($"SUPPLY unit={actedUnit.Id} source={supply.SourceCityId} tier={supply.Tier} " +
                                  $"missed={supply.ConsecutiveTurnsWithoutSupply} cost={supply.Cost}");
                    }
                    if (unitId.HasValue) acted.Add(unitId.Value);
                    actions++;
                    plannedActions++;
                }

                var survivors = 0;
                foreach (var unit in state.Units.Values)
                    if (unit.NationId == 2 && unit.Health > 0) survivors++;
                lines.Add($"ROUND_END round={state.Round} actions={actions} distinctUnits={acted.Count} survivors={survivors}");

                simulation.TryExecute(state, new EndTurnCommand(2, 1));
                simulation.TryExecute(state, new EndTurnCommand(1, 2));
            }
            lines.Add($"STATIC_PLAN_PERFORMANCE turns=4 committed={plannedActions} skipped={skippedActions} " +
                      $"totalMs={planningMilliseconds} averageTurnMs={planningMilliseconds / 4}");

            var output = Path.GetFullPath("Tools/ai-diagnostic.log");
            File.WriteAllLines(output, lines);
            Debug.Log($"AI diagnostic written to {output}");
        }

        private static void VerifyEdgeWallRules(GameSimulation simulation, List<string> lines)
        {
            var state = new GameState(HexMap.CreateRectangle(7, 5, 0)) { ActiveNationId = 1 };
            state.Cities.Add(1, new CityState
            {
                Id = 1,
                NationId = 2,
                Center = new HexCoord(4, 2),
                Level = 1,
                IsFormalOccupation = true
            });
            state.Map.Get(new HexCoord(4, 2)).CityId = 1;
            AddUnit(state, 1, 1, UnitType.MainInfantry, new HexCoord(2, 2), 100, false);
            CityWallSystem.InitializeCityWalls(state);
            var wall = simulation.Walls.FindWallBetween(state, new HexCoord(2, 2), new HexCoord(3, 2));
            Require(wall != null, "expected wall on city control edge");
            Require(state.CityWalls.Count == 6,
                $"level-one city should have one wall per boundary cell, got {state.CityWalls.Count}");
            var alternateOutside = new HexCoord(2, 3);
            Require(simulation.Walls.FindWallBetween(state, alternateOutside, new HexCoord(3, 2))?.Id == wall.Id,
                "all exterior faces of one boundary cell must share one wall state");
            Require(!simulation.Movement.CanMove(state, state.Units[1], new HexCoord(3, 2), out _),
                "intact edge wall must block outside-to-inside movement");
            var basePreview = simulation.Walls.Preview(state, state.Units[1], wall);
            Require(wall.MaxHealth == 20 && basePreview.BaseDefense == 12,
                "a level-one city wall must use the reinforced 20-health/12-defense baseline");
            wall.Health = 0;
            Require(simulation.Movement.CanMove(state, state.Units[1], new HexCoord(3, 2), out _),
                "destroyed boundary-cell wall must open entry from its exterior faces");
            state.Map.Get(state.Units[1].Position).UnitId = null;
            state.Units[1].Position = alternateOutside;
            state.Map.Get(alternateOutside).UnitId = 1;
            Require(simulation.Movement.CanMove(state, state.Units[1], new HexCoord(3, 2), out _),
                "one breach must open every exterior face of the same boundary cell");

            var supplyWall = new GameState(HexMap.CreateRectangle(7, 5, 0));
            AddCity(supplyWall, 1, 1, new HexCoord(0, 2), 1);
            AddCity(supplyWall, 2, 2, new HexCoord(4, 2), 1);
            CityWallSystem.InitializeCityWalls(supplyWall);
            var supplyGate = simulation.Walls.FindWallBetween(supplyWall, new HexCoord(2, 2),
                new HexCoord(3, 2));
            simulation.Supply.RecalculateCoverage(supplyWall);
            Require(supplyGate != null &&
                    !simulation.Supply.GetStatusAt(supplyWall, 1, new HexCoord(3, 2)).IsCovered,
                "an intact hostile wall must block supply at the same edge that blocks movement");
            supplyGate.Health = 0;
            simulation.Supply.RecalculateCoverage(supplyWall);
            Require(simulation.Supply.GetStatusAt(supplyWall, 1, new HexCoord(3, 2)).IsCovered,
                "destroying the wall must open that edge to both movement and supply");
            state.Map.Get(alternateOutside).UnitId = null;
            state.Units[1].Position = new HexCoord(2, 2);
            state.Map.Get(state.Units[1].Position).UnitId = 1;
            wall.Health = wall.MaxHealth;
            AddUnit(state, 2, 2, UnitType.MainInfantry, new HexCoord(3, 2), 100, true);
            var reinforced = simulation.Walls.Preview(state, state.Units[1], wall);
            Require(reinforced.GarrisonUnitId == 2 && reinforced.GarrisonDefense > 0 &&
                    reinforced.Damage <= basePreview.Damage,
                "garrison control must reinforce covered edge wall");
            Require(reinforced.CounterDamage == System.Math.Min(state.Units[1].Health,
                        reinforced.BaseCounterDamage + reinforced.GarrisonCounterDamage) &&
                    reinforced.BaseCounterDamage > 0 && reinforced.GarrisonCounterDamage > 0,
                $"ordinary wall assault must add separate retaliation; total={reinforced.CounterDamage} " +
                $"wall={reinforced.BaseCounterDamage} defender={reinforced.GarrisonCounterDamage} " +
                $"forces={reinforced.BaseCounterAttack}/{reinforced.GarrisonCounterAttack}");
            var protectedAttack = simulation.Combat.Preview(state, state.Units[2], state.Units[1]);
            Require(!protectedAttack.CanCounter && protectedAttack.CounterDamage == 0 &&
                    protectedAttack.CounterBlockedReason.Contains("城墙掩护"),
                "a unit attacking outward from its intact friendly wall must not receive an outside counterattack");
            state.Units[2].IsGarrisoned = false;
            var occupiedButNotGarrisoned = simulation.Walls.Preview(state, state.Units[1], wall);
            Require(occupiedButNotGarrisoned.GarrisonUnitId == 2 &&
                    occupiedButNotGarrisoned.GarrisonCounterDamage > 0,
                "a unit physically occupying a wall cell must be part of that combined target even before garrisoning");
            var artillery = new GameState(HexMap.CreateRectangle(7, 5, 0)) { ActiveNationId = 1 };
            artillery.Cities.Add(1, new CityState
            {
                Id = 1, NationId = 1, Center = new HexCoord(0, 2), Level = 1, IsFormalOccupation = true
            });
            artillery.Cities.Add(2, new CityState
            {
                Id = 2, NationId = 2, Center = new HexCoord(4, 2), Level = 1, IsFormalOccupation = true
            });
            artillery.Map.Get(new HexCoord(0, 2)).CityId = 1;
            artillery.Map.Get(new HexCoord(0, 2)).OwnerNationId = 1;
            artillery.Map.Get(new HexCoord(4, 2)).CityId = 2;
            artillery.Map.Get(new HexCoord(4, 2)).OwnerNationId = 2;
            CityWallSystem.InitializeCityWalls(artillery);
            AddUnit(artillery, 1, 1, UnitType.LightArtillery, new HexCoord(1, 2), 10, false);
            AddUnit(artillery, 2, 2, UnitType.MainInfantry, new HexCoord(3, 2), 12, true);
            var artilleryWall = simulation.Walls.FindWallAt(artillery, new HexCoord(3, 2));
            var bombardment = simulation.Walls.Preview(artillery, artillery.Units[1], artilleryWall);
            Require(bombardment.Damage > 0 && bombardment.GarrisonDamage > 0 &&
                    bombardment.CounterDamage == 0 && bombardment.BaseCounterDamage == 0 &&
                    bombardment.GarrisonCounterDamage == 0 && bombardment.AppliesSuppression,
                "range-two artillery must suppress wall and infantry garrison without receiving range-one retaliation");
            var lethalGarrison = GameStateCloner.Clone(artillery);
            MoveUnit(lethalGarrison, lethalGarrison.Units[1], new HexCoord(2, 2));
            lethalGarrison.Units[2].Health = 1;
            var lethalBombardment = simulation.Walls.Preview(lethalGarrison, lethalGarrison.Units[1],
                lethalGarrison.CityWalls[artilleryWall.Id]);
            Require(lethalBombardment.GarrisonDamage == 1 && lethalBombardment.GarrisonCounterDamage == 0,
                "a garrison destroyed by the combined shot must not contribute retaliation");
            Require(!simulation.TryExecute(artillery, new AttackCommand(1, 1, 2)),
                "a wall-cell defender must not be targetable separately from its intact wall");
            lines.Add("EDGE_WALL_RULES passed count=6 shared-faces/move-supply-block/breach/garrison=true");
        }

        private static void VerifyEnemyUnitVisuals(List<string> lines)
        {
            var state = new GameState(HexMap.CreateRectangle(6, 3, 0)) { ActiveNationId = 1 };
            AddUnit(state, 1, 1, UnitType.MainInfantry, new HexCoord(0, 1), 12, false);
            AddUnit(state, 2, 2, UnitType.LightArtillery, new HexCoord(2, 1), 10, false);
            AddUnit(state, 3, 2, UnitType.LightArmor, new HexCoord(3, 1), 22, false);
            AddUnit(state, 4, 2, UnitType.Medic, new HexCoord(4, 1), 9, false);
            var visible = new HashSet<HexCoord>();
            foreach (var coord in state.Map.Cells.Keys) visible.Add(coord);
            var root = new GameObject("Enemy visual diagnostic");
            try
            {
                var view = root.AddComponent<HexMapView>();
                view.Build(state, 1, visible, null, null, new HashSet<int>(), new HashSet<HexCoord>(),
                    new HashSet<HexCoord>(), new HashSet<HexCoord>(), new HashSet<int>(),
                    new HashSet<HexCoord>(), new HashSet<HexCoord>(), null, 0);
                Require(view.HasUnitMarker(2) && view.HasUnitMarker(3) && view.HasUnitMarker(4),
                    "every visible enemy branch must create a map model using the formal visibility result");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            lines.Add("ENEMY_VISUALS passed artillery/armor/medic use formal visibility=true");
        }

        private static void VerifySupplyRules(GameSimulation simulation, List<string> lines)
        {
            var overlap = new GameState(HexMap.CreateRectangle(13, 3, 0));
            AddCity(overlap, 1, 1, new HexCoord(0, 1), 1);
            AddCity(overlap, 2, 2, new HexCoord(12, 1), 1);
            simulation.Supply.RecalculateCoverage(overlap);
            var middleOwners = simulation.Supply.GetCoverageNations(overlap, new HexCoord(6, 1));
            Require(middleOwners.Contains(1) && middleOwners.Contains(2) && middleOwners.Count == 2,
                "one cell must be able to record overlapping supply coverage from both nations");

            var blocked = new GameState(HexMap.CreateRectangle(12, 5, 0));
            AddCity(blocked, 1, 1, new HexCoord(0, 2), 2);
            AddUnit(blocked, 1, 2, UnitType.MainInfantry, new HexCoord(4, 0), 12, true);
            AddUnit(blocked, 2, 2, UnitType.MainInfantry, new HexCoord(4, 2), 12, true);
            AddUnit(blocked, 3, 2, UnitType.MainInfantry, new HexCoord(4, 4), 12, true);
            simulation.Supply.RecalculateCoverage(blocked);
            Require(simulation.Supply.GetStatusAt(blocked, 1, new HexCoord(2, 2)).IsCovered &&
                    !simulation.Supply.GetStatusAt(blocked, 1, new HexCoord(4, 2)).IsCovered &&
                    !simulation.Supply.GetStatusAt(blocked, 1, new HexCoord(8, 2)).IsCovered,
                "hostile bodies and garrison control must cut holes and block the rear supply region");

            var highway = new GameState(HexMap.CreateRectangle(13, 3, 0));
            AddCity(highway, 1, 2, new HexCoord(0, 1), 1);
            for (var q = 0; q < 12; q++)
            {
                var from = new HexCoord(q, 1);
                var to = new HexCoord(q + 1, 1);
                highway.Map.Get(from).RoadNeighbors.Add(to);
                highway.Map.Get(to).RoadNeighbors.Add(from);
                highway.Map.Get(from).RoadOwnerNationIds[to] = 2;
                highway.Map.Get(to).RoadOwnerNationIds[from] = 2;
            }
            simulation.Supply.RecalculateCoverage(highway);
            var highwayStatus = simulation.Supply.GetStatusAt(highway, 2, new HexCoord(12, 1));
            Require(highwayStatus.IsCovered && System.Math.Abs(highwayStatus.Cost - 6f) < 0.001f,
                "an uninterrupted twelve-edge road must consume exactly six supply range");
            Require(simulation.Visibility.CalculateVisibleCells(highway, 2).Contains(new HexCoord(12, 1)),
                "normal supply reach must provide shared vision beyond city and unit sight");

            var locked = new GameState(HexMap.CreateRectangle(16, 3, 0));
            AddCity(locked, 1, 1, new HexCoord(0, 1), 1);
            AddUnit(locked, 1, 1, UnitType.MainInfantry, new HexCoord(4, 1), 12, false);
            simulation.Supply.RecalculateCoverage(locked);
            Require(simulation.Supply.LockTurnStatus(locked, locked.Units[1]).Tier == 0,
                "a unit covered at turn start must lock normal supply");
            MoveUnit(locked, locked.Units[1], new HexCoord(13, 1));
            locked.Cities[1].IsDisabled = true;
            Require(simulation.Supply.GetStatus(locked, locked.Units[1]).Tier == 0,
                "movement and source loss during a turn must not alter locked supply");

            simulation.Supply.RecalculateCoverage(locked);
            locked.Map.Get(locked.Units[1].Position).SupplyNationIds.Add(2);
            var firstMiss = simulation.Supply.LockTurnStatus(locked, locked.Units[1]);
            Require(firstMiss.Tier == 1 && firstMiss.ConsecutiveTurnsWithoutSupply == 1 &&
                    firstMiss.AttackMultiplier == 0.5f,
                "enemy coverage must never supply a unit whose own nation is absent");
            simulation.Supply.RecalculateCoverage(locked);
            var secondMiss = simulation.Supply.LockTurnStatus(locked, locked.Units[1]);
            simulation.Supply.RecalculateCoverage(locked);
            var thirdMiss = simulation.Supply.LockTurnStatus(locked, locked.Units[1]);
            Require(secondMiss.Tier == 1 && secondMiss.ConsecutiveTurnsWithoutSupply == 2 &&
                    thirdMiss.Tier == 2 && thirdMiss.ConsecutiveTurnsWithoutSupply == 3 &&
                    thirdMiss.AttackMultiplier == 0.1f,
                "the first two missed turns must apply 50 percent and the third 90 percent penalties");
            locked.Cities[1].IsDisabled = false;
            MoveUnit(locked, locked.Units[1], new HexCoord(4, 1));
            simulation.Supply.RecalculateCoverage(locked);
            Require(simulation.Supply.LockTurnStatus(locked, locked.Units[1]).Tier == 0 &&
                    locked.Units[1].ConsecutiveTurnsWithoutSupply == 0,
                "returning to own coverage before turn start must reset the missed-turn count");

            var demo = PrototypeScenario.Create(simulation.Rules);
            foreach (var unit in demo.Units.Values)
                demo.Map.Get(unit.Position).UnitId = null;
            demo.Units.Clear();
            // Distance calibration is tested with every gate open; intact-wall
            // blocking is verified independently in VerifyEdgeWallRules.
            foreach (var wall in demo.CityWalls.Values) wall.Health = 0;
            simulation.Supply.RecalculateCoverage(demo);
            foreach (var cell in demo.Map.Cells.Values)
            {
                if (cell.Coord.Q <= 0)
                    Require(simulation.Supply.GetStatusAt(demo, 1, cell.Coord).IsCovered,
                        $"blue supply must cover its complete half; missing {cell.Coord}");
                if (cell.Coord.Q >= 0)
                    Require(simulation.Supply.GetStatusAt(demo, 2, cell.Coord).IsCovered,
                        $"red supply must cover its complete half; missing {cell.Coord}");
            }
            var blueNorth = simulation.Supply.GetStatusAt(demo, 1, demo.Cities[4].Center);
            var blueSouth = simulation.Supply.GetStatusAt(demo, 1, demo.Cities[6].Center);
            var blueRear = simulation.Supply.GetStatusAt(demo, 1, demo.Cities[2].Center);
            var redNorth = simulation.Supply.GetStatusAt(demo, 2, demo.Cities[3].Center);
            var redSouth = simulation.Supply.GetStatusAt(demo, 2, demo.Cities[5].Center);
            var redRear = simulation.Supply.GetStatusAt(demo, 2, demo.Cities[1].Center);
            Require(blueNorth.IsCovered && blueSouth.IsCovered,
                "blue supply must reach both red forward city centers without relying on units");
            Require(redNorth.IsCovered && redSouth.IsCovered,
                "red supply must reach both blue forward city centers without relying on units");
            Require(!blueRear.IsCovered && !redRear.IsCovered,
                "neither side's supply field may reach the opposing rear level-two city");
            lines.Add($"SUPPLY_DEMO front blue={blueNorth.Cost}/{blueSouth.Cost} " +
                      $"red={redNorth.Cost}/{redSouth.Cost} " +
                      $"rear blue={blueRear.IsCovered} red={redRear.IsCovered}");
            lines.Add("SUPPLY_RULES passed snapshot/overlap/blockade/locked-turn/50-90/enemy-isolation/" +
                      "complete-own-half/both-fronts-not-rear=true");
        }

        private static void VerifyUnitFramework(GameSimulation simulation, List<string> lines)
        {
            var rules = simulation.Rules;
            Require(rules.HasAbility(UnitType.MainInfantry, UnitAbility.IgnoresTerrainMovement) &&
                    rules.HasAbility(UnitType.MainInfantry, UnitAbility.FormsControlZone) &&
                    rules.HasAbility(UnitType.MainInfantry, UnitAbility.RapidOccupation),
                "main infantry must combine branch and concrete abilities");
            Require(rules.HasAbility(UnitType.LightArmor, UnitAbility.PreservesMovementAfterAttack) &&
                    !rules.HasAbility(UnitType.LightArmor, UnitAbility.SupplyDependent) &&
                    rules.Unit(UnitType.LightArmor).Vision == 3,
                "light armor must combine armor mobility with extended vision");
            Require(rules.HasAbility(UnitType.LightArtillery, UnitAbility.Suppression) &&
                    !rules.HasAbility(UnitType.LightArtillery, UnitAbility.IgnoresCityWall) &&
                    !rules.HasAbility(UnitType.LightArtillery, UnitAbility.PreventsCounterattack) &&
                    !rules.HasAbility(UnitType.LightArtillery, UnitAbility.CannotCounterattack),
                "light artillery must keep suppression as its only special ability");
            Require(rules.Unit(UnitType.MainInfantry).Movement == 3 &&
                    rules.Unit(UnitType.Medic).Movement == 3 &&
                    rules.Unit(UnitType.LightArtillery).Movement == 4 &&
                    rules.Unit(UnitType.LightArmor).Movement == 7,
                "the low-mobility roster must use 3/3/4/7 action points");

            var demo = PrototypeScenario.Create(rules);
            var blueInfantry = 0;
            var blueArtillery = 0;
            var blueArmor = 0;
            var blueMedic = 0;
            var redInfantry = 0;
            var redArtillery = 0;
            var redArmor = 0;
            var redMedic = 0;
            foreach (var unit in demo.Units.Values)
            {
                var blue = unit.NationId == 1;
                switch (unit.Type)
                {
                    case UnitType.MainInfantry: if (blue) blueInfantry++; else redInfantry++; break;
                    case UnitType.LightArtillery: if (blue) blueArtillery++; else redArtillery++; break;
                    case UnitType.LightArmor: if (blue) blueArmor++; else redArmor++; break;
                    case UnitType.Medic: if (blue) blueMedic++; else redMedic++; break;
                }
            }
            Require(demo.Map.Cells.Count == 631 && demo.Cities.Count == 6,
                "the tabletop demo must contain a radius-fourteen hexagonal map and six cities");
            var occupiedUnitCells = new HashSet<HexCoord>();
            foreach (var pair in demo.Map.Cells)
            {
                Require(new HexCoord(0, 0).DistanceTo(pair.Key) <= 14,
                    $"map cell {pair.Key} must remain inside the radius-fourteen board");
                var mirrored = new HexCoord(-pair.Key.Q, pair.Key.R + pair.Key.Q);
                Require(demo.Map.TryGet(mirrored, out var mirroredCell) &&
                        mirroredCell.Terrain == pair.Value.Terrain,
                    $"terrain at {pair.Key} must match its opposing-side mirror {mirrored}");
                foreach (var neighbor in pair.Value.RoadNeighbors)
                {
                    Require(demo.Map.TryGet(neighbor, out var neighborCell) &&
                            neighborCell.RoadNeighbors.Contains(pair.Key),
                        $"road {pair.Key}->{neighbor} must stay on the board and be symmetric");
                }
            }
            foreach (var city in demo.Cities.Values)
                Require(demo.Map.TryGet(city.Center, out var cityCell) && cityCell.CityId == city.Id,
                    $"city {city.Id} must occupy its declared board cell");
            foreach (var building in demo.Buildings.Values)
                Require(demo.Map.TryGet(building.Position, out var buildingCell) &&
                        buildingCell.BuildingId == building.Id,
                    $"building {building.Id} must occupy its declared board cell");
            foreach (var unit in demo.Units.Values)
                Require(demo.Map.TryGet(unit.Position, out var unitCell) && unitCell.UnitId == unit.Id &&
                        occupiedUnitCells.Add(unit.Position),
                    $"unit {unit.Id} must occupy one unique board cell");
            var tacticalTerrain = 0;
            foreach (var cell in demo.Map.Cells.Values)
                if (cell.Terrain != TerrainType.Plain) tacticalTerrain++;
            Require(tacticalTerrain >= 80,
                $"expanded board must contain a meaningful tactical terrain network, got {tacticalTerrain} cells");
            Require(blueInfantry == 10 && blueArtillery == 4 && blueArmor == 3 && blueMedic == 2 &&
                    redInfantry == 10 && redArtillery == 4 && redArmor == 3 && redMedic == 2,
                "each side must start with 10 infantry, 4 artillery, 3 armor and 2 medics");

            var state = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddUnit(state, 1, 1, UnitType.Medic, new HexCoord(1, 1), 80, false);
            AddUnit(state, 2, 1, UnitType.MainInfantry, new HexCoord(2, 1), 4, false);
            Require(!simulation.Control.IsControlledBy(state, new HexCoord(0, 1), 1),
                "medic must not project an adjacent control zone");
            Require(!simulation.Control.IsControlledBy(state, new HexCoord(3, 1), 1),
                "ungarrisoned main infantry must not project a control zone");
            state.Units[2].IsGarrisoned = true;
            Require(simulation.Control.IsControlledBy(state, new HexCoord(3, 1), 1) &&
                    !simulation.Control.IsControlledBy(state, new HexCoord(4, 1), 1),
                "garrisoned main infantry must project exactly one surrounding control ring");
            state.Units[2].IsGarrisoned = false;
            Require(simulation.Medical.Preview(state, state.Units[1], state.Units[2]) == 4,
                "medic must preview the configured four-point healing");
            Require(simulation.TryExecute(state, new HealCommand(1, 1, 2)) && state.Units[2].Health == 8 &&
                    state.Units[1].RemainingMovement == 0,
                "healing must restore health and consume the medic action");

            var garrison = new GameState(HexMap.CreateRectangle(4, 3, 0)) { ActiveNationId = 1 };
            AddCity(garrison, 1, 1, new HexCoord(1, 1), 1);
            AddUnit(garrison, 1, 1, UnitType.MainInfantry, new HexCoord(1, 1), 12, false);
            AddUnit(garrison, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 12, false);
            Require(simulation.TryExecute(garrison, new GarrisonCommand(1, 1)) &&
                    garrison.Units[1].IsGarrisoned && garrison.Units[1].RemainingMovement == 0 &&
                    garrison.Units[1].HasMoved && garrison.Units[1].HasAttacked,
                "garrisoning must immediately exhaust movement and attack for the current turn");
            Require(simulation.Movement.FindReachablePaths(garrison, garrison.Units[1]).Count == 0 &&
                    !simulation.TryExecute(garrison, new AttackCommand(1, 1, 2)),
                "a newly garrisoned unit must not move or attack again in the same turn");
            Require(simulation.Combat.GetGarrisonMultiplier(garrison, garrison.Units[1]) == 1.5f &&
                    simulation.Control.IsControlledBy(garrison, new HexCoord(0, 1), 1) &&
                    !simulation.Control.IsControlledBy(garrison, new HexCoord(3, 1), 1),
                "main infantry garrisoned off a wall must gain x1.5 defense and a one-cell control zone");

            var fieldPost = new GameState(HexMap.CreateRectangle(8, 5, 0)) { ActiveNationId = 1 };
            AddUnit(fieldPost, 1, 1, UnitType.LightArmor, new HexCoord(5, 3), 20, false);
            Require(simulation.TryExecute(fieldPost, new GarrisonCommand(1, 1)) &&
                    simulation.Combat.GetGarrisonMultiplier(fieldPost, fieldPost.Units[1]) == 1.5f &&
                    !simulation.Control.IsControlledBy(fieldPost, new HexCoord(6, 3), 1),
                "any unit may garrison in the field for x1.5 defense without gaining infantry control");

            var wallPost = new GameState(HexMap.CreateRectangle(8, 5, 0)) { ActiveNationId = 1 };
            AddCity(wallPost, 1, 1, new HexCoord(3, 2), 1);
            CityWallSystem.InitializeCityWalls(wallPost);
            var wallCell = new HexCoord(4, 2);
            AddUnit(wallPost, 1, 1, UnitType.MainInfantry, wallCell, 12, false);
            Require(simulation.TryExecute(wallPost, new GarrisonCommand(1, 1)) &&
                    simulation.Combat.GetGarrisonMultiplier(wallPost, wallPost.Units[1]) == 2.5f,
                "main infantry alone must receive x2.5 defense when garrisoned on a wall cell");

            var forbiddenWallPost = new GameState(HexMap.CreateRectangle(8, 5, 0)) { ActiveNationId = 1 };
            AddCity(forbiddenWallPost, 1, 1, new HexCoord(3, 2), 1);
            CityWallSystem.InitializeCityWalls(forbiddenWallPost);
            AddUnit(forbiddenWallPost, 1, 1, UnitType.Medic, wallCell, 9, false);
            Require(!simulation.TryExecute(forbiddenWallPost, new GarrisonCommand(1, 1)),
                "non-main-infantry units must not garrison on wall cells");

            var hostileCityPost = new GameState(HexMap.CreateRectangle(8, 5, 0)) { ActiveNationId = 1 };
            AddCity(hostileCityPost, 1, 2, new HexCoord(3, 2), 1);
            AddUnit(hostileCityPost, 1, 1, UnitType.MainInfantry, new HexCoord(2, 2), 12, false);
            Require(!simulation.TryExecute(hostileCityPost, new GarrisonCommand(1, 1)),
                "units must not garrison anywhere inside an enemy city zone before formal capture");
            hostileCityPost.Cities[1].NationId = 1;
            Require(simulation.TryExecute(hostileCityPost, new GarrisonCommand(1, 1)),
                "garrisoning must become legal after the city formally changes owner");
            lines.Add("UNIT_FRAMEWORK passed branches/abilities/control/healing/garrison-field-only/wall-post=true");
        }

        private static void VerifyControlPinning(GameSimulation simulation, List<string> lines)
        {
            var friendlyTransit = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddUnit(friendlyTransit, 1, 1, UnitType.MainInfantry, new HexCoord(0, 1), 12, false);
            AddUnit(friendlyTransit, 2, 1, UnitType.MainInfantry, new HexCoord(1, 1), 12, false);
            var transitPaths = simulation.Movement.FindReachablePaths(friendlyTransit, friendlyTransit.Units[1]);
            Require(!transitPaths.ContainsKey(new HexCoord(1, 1)) &&
                    transitPaths.ContainsKey(new HexCoord(2, 1)) &&
                    transitPaths[new HexCoord(2, 1)].Cells.Count == 3 &&
                    transitPaths[new HexCoord(2, 1)].Cells[1].Equals(new HexCoord(1, 1)) &&
                    !simulation.Movement.CanMove(friendlyTransit, friendlyTransit.Units[1],
                        new HexCoord(1, 1), out _),
                "friendly units must permit transit while remaining invalid movement destinations");

            var state = new GameState(HexMap.CreateRectangle(6, 3, 0)) { ActiveNationId = 1 };
            AddUnit(state, 1, 1, UnitType.LightArmor, new HexCoord(0, 1), 22, false);
            AddUnit(state, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 12, true);
            var mover = state.Units[1];
            var contact = new HexCoord(1, 1);
            var behind = new HexCoord(3, 1);
            var reachable = simulation.Movement.FindReachablePaths(state, mover);
            Require(reachable.ContainsKey(contact), "the first enemy-control cell must remain a legal endpoint");
            Require(!reachable.ContainsKey(behind) && simulation.Movement.FindPath(state, mover, behind) == null,
                "movement preview must never route through enemy control to a rear cell");
            foreach (var path in reachable.Values)
            {
                for (var i = 1; i < path.Cells.Count - 1; i++)
                    Require(!simulation.Control.HasEnemyControl(state, path.Cells[i], mover.NationId),
                        "enemy control may only appear at the final cell of a preview path");
            }

            Require(simulation.TryExecute(state, new MoveCommand(1, mover.Id, contact)) &&
                    mover.RemainingMovement == 0 && mover.IsPinnedByEnemyControl && mover.CanAttackThisTurn,
                "entering control with movement remaining must pin movement but preserve one attack");
            Require(simulation.TryExecute(state, new AttackCommand(1, mover.Id, 2)) &&
                    mover.HasAttacked && !mover.IsPinnedByEnemyControl,
                "a pinned unit must be able to spend its preserved attack normally");

            var exhausted = new GameState(HexMap.CreateRectangle(4, 3, 0)) { ActiveNationId = 1 };
            AddUnit(exhausted, 1, 1, UnitType.LightArmor, new HexCoord(0, 1), 20, false);
            AddUnit(exhausted, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 12, true);
            exhausted.Units[1].RemainingMovement = 1;
            Require(simulation.TryExecute(exhausted, new MoveCommand(1, 1, new HexCoord(1, 1))) &&
                    exhausted.Units[1].RemainingMovement == 0 &&
                    !exhausted.Units[1].HasUnspentAttackAfterControlStop &&
                    !exhausted.Units[1].CanAttackThisTurn,
                "control must never grant an attack when movement was naturally exhausted on entry");

            var release = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddUnit(release, 1, 1, UnitType.LightArmor, new HexCoord(1, 1), 22, false);
            AddUnit(release, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 1, true);
            AddUnit(release, 3, 1, UnitType.MainInfantry, new HexCoord(2, 0), 12, false);
            release.Units[3].RemainingMovement = 0;
            release.Units[3].IsPinnedByEnemyControl = true;
            release.Units[3].HasUnspentAttackAfterControlStop = true;
            Require(simulation.TryExecute(release, new AttackCommand(1, 1, 2)) &&
                    !release.Units.ContainsKey(2) && !release.Units[3].IsPinnedByEnemyControl &&
                    release.Units[3].HasUnspentAttackAfterControlStop && release.Units[3].CanAttackThisTurn &&
                    !simulation.Control.HasEnemyControl(release, release.Units[3].Position, 1),
                "destroying the last control source must release allied pins without consuming their preserved attack");
            lines.Add("CONTROL_PINNING passed friendly-transit/preview-stop/execution-stop/attack-preserved=true");
        }

        private static void VerifyPromotionRules(GameSimulation simulation, List<string> lines)
        {
            Require(System.Math.Abs(RuleMath.LevelMultiplier(1) - 1f) < 0.001f &&
                    System.Math.Abs(RuleMath.LevelMultiplier(2) - 1.35f) < 0.001f &&
                    System.Math.Abs(RuleMath.LevelMultiplier(3) - 1.80f) < 0.001f &&
                    System.Math.Abs(RuleMath.LevelMultiplier(4) - 2.50f) < 0.001f,
                "level stat multipliers must be 1.00/1.35/1.80/2.50");

            var state = new GameState(HexMap.CreateRectangle(6, 3, 0)) { ActiveNationId = 1 };
            AddUnit(state, 1, 1, UnitType.LightArmor, new HexCoord(1, 1), 20, false);
            var veteran = state.Units[1];
            for (var kill = 1; kill <= 14; kill++)
            {
                veteran.HasAttacked = false;
                veteran.RemainingMovement = simulation.Rules.Unit(veteran.Type).Movement;
                if (kill == 1 || kill == 4 || kill == 14) veteran.Health = 1;
                AddUnit(state, 100 + kill, 2, UnitType.Medic, new HexCoord(2, 1), 1, false);
                Require(simulation.TryExecute(state, new AttackCommand(1, veteran.Id, 100 + kill)),
                    $"promotion fixture kill {kill} must execute");
                if (kill == 1 || kill == 4 || kill == 14)
                {
                    var targetLevel = kill == 1 ? 2 : kill == 4 ? 3 : 4;
                    var requiredKills = kill == 1 ? 1 : kill == 4 ? 3 : 10;
                    Require(veteran.Level == targetLevel - 1 && veteran.PromotionKills == requiredKills &&
                            veteran.Health == 1 && simulation.CanPromote(veteran),
                        "reaching the kill gap must mark the unit ready without automatically promoting or healing it");
                    veteran.RemainingMovement = 0;
                    var attackedBeforePromotion = veteran.HasAttacked;
                    Require(simulation.TryExecute(state, new PromoteUnitCommand(1, veteran.Id)) &&
                            veteran.Level == targetLevel && veteran.PromotionKills == 0 &&
                            veteran.RemainingMovement == 0 && veteran.HasAttacked == attackedBeforePromotion,
                        "manual promotion must reset only promotion progress and consume no action");
                    var promotedMaximum = RuleMath.Round(simulation.Rules.Unit(veteran.Type).MaxHealth *
                                                         RuleMath.LevelMultiplier(veteran.Level));
                    Require(veteran.Health == promotedMaximum, "each promotion must fully restore health");
                }
                else
                {
                    var expectedLevel = kill >= 4 ? 3 : 2;
                    var expectedProgress = kill >= 4 ? kill - 4 : kill - 1;
                    Require(veteran.Level == expectedLevel && veteran.PromotionKills == expectedProgress &&
                            !simulation.CanPromote(veteran),
                        $"kill {kill} must leave current-level progress {expectedProgress} without promotion");
                }
            }

            AddUnit(state, 200, 2, UnitType.Medic, new HexCoord(3, 1), 9, false);
            Require(simulation.Combat.Preview(state, veteran, state.Units[200]).Damage > 0,
                "a level-four unit must attack one cell beyond its base maximum range");

            var types = new[]
            {
                UnitType.MainInfantry, UnitType.Medic, UnitType.LightArmor, UnitType.LightArtillery
            };
            foreach (var type in types)
            {
                for (var lowerLevel = 1; lowerLevel <= 3; lowerLevel++)
                {
                    var duel = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
                    AddUnit(duel, 1, 1, type, new HexCoord(1, 1), 999, false);
                    AddUnit(duel, 2, 2, type, new HexCoord(2, 1), 999, false);
                    duel.Units[1].Level = lowerLevel + 1;
                    duel.Units[2].Level = lowerLevel;
                    duel.Units[1].Health = RuleMath.Round(simulation.Rules.Unit(type).MaxHealth *
                                                         RuleMath.LevelMultiplier(lowerLevel + 1));
                    duel.Units[2].Health = RuleMath.Round(simulation.Rules.Unit(type).MaxHealth *
                                                         RuleMath.LevelMultiplier(lowerLevel));
                    var advantage = simulation.Combat.Preview(duel, duel.Units[1], duel.Units[2]);
                    Require(advantage.Damage > advantage.CounterDamage,
                        $"{type} level {lowerLevel + 1} must win its opening exchange against level {lowerLevel}");
                }

                for (var lowerLevel = 1; lowerLevel <= 2; lowerLevel++)
                {
                    var battle = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
                    AddUnit(battle, 1, 1, type, new HexCoord(1, 1), 999, false);
                    AddUnit(battle, 2, 2, type, new HexCoord(2, 1), 999, false);
                    var higherLevel = lowerLevel + 2;
                    var higherMaximum = RuleMath.Round(simulation.Rules.Unit(type).MaxHealth *
                                                       RuleMath.LevelMultiplier(higherLevel));
                    battle.Units[1].Level = higherLevel;
                    battle.Units[2].Level = lowerLevel;
                    battle.Units[1].Health = higherMaximum;
                    battle.Units[2].Health = RuleMath.Round(simulation.Rules.Unit(type).MaxHealth *
                                                           RuleMath.LevelMultiplier(lowerLevel));
                    var exchanges = 0;
                    while (battle.Units[2].Health > 0 && exchanges++ < 8)
                        simulation.Combat.Resolve(battle, battle.Units[1], battle.Units[2]);
                    var healthLost = higherMaximum - battle.Units[1].Health;
                    Require(battle.Units[2].Health == 0 && healthLost <= System.Math.Ceiling(higherMaximum * 0.30f),
                        $"{type} two levels higher must destroy its peer while losing at most 30 percent health");
                }
            }

            var aiPromotion = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 2 };
            AddUnit(aiPromotion, 1, 2, UnitType.MainInfantry, new HexCoord(2, 1), 12, false);
            aiPromotion.Units[1].PromotionKills = 1;
            var aiPlan = new AiPlanner(simulation, new StrategicEvaluator()).PlanTurnStatic(aiPromotion, 2);
            Require(aiPlan.Exists(entry => entry.Command is PromoteUnitCommand promote && promote.UnitId == 1),
                "the computer must explicitly schedule its free promotion before ordinary actions");
            lines.Add("PROMOTION_RULES passed manual/free kills=1/3/10 stats=1.00/1.35/1.80/2.50 " +
                      "one-level-positive/two-level-low-loss/full-heal/range+1/ai-command=true");
        }

        private static void VerifyCombatOutcomes(GameSimulation simulation, List<string> lines)
        {
            var infantryMirror = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddCity(infantryMirror, 1, 1, new HexCoord(0, 1), 1);
            AddCity(infantryMirror, 2, 2, new HexCoord(4, 1), 1);
            AddUnit(infantryMirror, 1, 1, UnitType.MainInfantry, new HexCoord(2, 1), 12, false);
            AddUnit(infantryMirror, 2, 2, UnitType.MainInfantry, new HexCoord(3, 1), 12, false);
            var infantryExchange = simulation.Combat.Preview(infantryMirror, infantryMirror.Units[1],
                infantryMirror.Units[2]);
            Require(infantryExchange.Damage == 4 && infantryExchange.CounterDamage == 4,
                "equal unmodified infantry must exchange four-for-four");
            infantryMirror.Units[2].IsGarrisoned = true;
            var fortifiedExchange = simulation.Combat.Preview(infantryMirror, infantryMirror.Units[1],
                infantryMirror.Units[2]);
            Require(fortifiedExchange.Damage == 3 && fortifiedExchange.CounterDamage == 6,
                "field-garrisoned infantry must reverse a melee assault to a three-for-six exchange");

            var armorMirror = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddCity(armorMirror, 1, 1, new HexCoord(0, 1), 1);
            AddCity(armorMirror, 2, 2, new HexCoord(4, 1), 1);
            AddUnit(armorMirror, 1, 1, UnitType.LightArmor, new HexCoord(2, 1), 20, false);
            AddUnit(armorMirror, 2, 2, UnitType.LightArmor, new HexCoord(3, 1), 20, false);
            var armorExchange = simulation.Combat.Preview(armorMirror, armorMirror.Units[1], armorMirror.Units[2]);
            Require(armorExchange.Damage == 5 && armorExchange.CounterDamage == 5,
                "equal unmodified armor must exchange five-for-five");

            var artilleryDuel = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddCity(artilleryDuel, 1, 1, new HexCoord(0, 1), 1);
            AddCity(artilleryDuel, 2, 2, new HexCoord(4, 1), 1);
            AddUnit(artilleryDuel, 1, 1, UnitType.LightArtillery, new HexCoord(1, 1), 10, false);
            AddUnit(artilleryDuel, 2, 2, UnitType.LightArtillery, new HexCoord(3, 1), 10, false);
            var duel = simulation.Combat.Preview(artilleryDuel, artilleryDuel.Units[1], artilleryDuel.Units[2]);
            Require(duel.Damage == 9 && artilleryDuel.Units[2].Health - duel.Damage == 1 &&
                    duel.CanCounter && duel.CounterDamage > 0 && duel.CounterHealthRatio == 1f,
                "a full-health artillery duel must naturally leave one health and allow a full-prehit counter");

            var closeAssault = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1 };
            AddCity(closeAssault, 1, 1, new HexCoord(0, 1), 1);
            AddCity(closeAssault, 2, 2, new HexCoord(4, 1), 1);
            AddUnit(closeAssault, 1, 1, UnitType.LightArmor, new HexCoord(1, 1), 20, false);
            AddUnit(closeAssault, 2, 2, UnitType.LightArtillery, new HexCoord(2, 1), 10, false);
            var assault = simulation.Combat.Preview(closeAssault, closeAssault.Units[1], closeAssault.Units[2]);
            Require(assault.Damage == 10 && !assault.CanCounter && assault.CounterDamage == 0,
                "light armor must destroy an exposed adjacent light artillery before it can counter");

            var counterTiming = new GameState(HexMap.CreateRectangle(6, 3, 0)) { ActiveNationId = 1 };
            AddCity(counterTiming, 1, 1, new HexCoord(0, 1), 1);
            AddCity(counterTiming, 2, 2, new HexCoord(5, 1), 1);
            AddUnit(counterTiming, 1, 1, UnitType.MainInfantry, new HexCoord(2, 1), 12, false);
            AddUnit(counterTiming, 2, 2, UnitType.LightArmor, new HexCoord(3, 1), 20, false);
            var preparedCounter = simulation.Combat.Preview(counterTiming, counterTiming.Units[1],
                counterTiming.Units[2]);
            var woundedTiming = GameStateCloner.Clone(counterTiming);
            woundedTiming.Units[2].Health = 10;
            var woundedCounter = simulation.Combat.Preview(woundedTiming, woundedTiming.Units[1],
                woundedTiming.Units[2]);
            Require(preparedCounter.CounterDamage > woundedCounter.CounterDamage &&
                    preparedCounter.CounterHealthRatio == 1f && woundedCounter.CounterHealthRatio == 0.5f,
                "counter strength must use health before the current attack, not post-hit health");
            var lethalTiming = GameStateCloner.Clone(counterTiming);
            lethalTiming.Units[2].Health = 1;
            var lethal = simulation.Combat.Preview(lethalTiming, lethalTiming.Units[1], lethalTiming.Units[2]);
            Require(!lethal.CanCounter && lethal.CounterDamage == 0,
                "a unit destroyed by the current attack must not counter despite prehit counter timing");

            var suppression = new GameState(HexMap.CreateRectangle(7, 3, 0)) { ActiveNationId = 1 };
            AddUnit(suppression, 1, 1, UnitType.LightArtillery, new HexCoord(2, 1), 10, false);
            AddUnit(suppression, 2, 2, UnitType.MainInfantry, new HexCoord(4, 1), 12, true);
            Require(simulation.Control.HasEnemyControl(suppression, new HexCoord(4, 0), 1),
                "a garrisoned infantry target must project control before suppression");
            simulation.Combat.Resolve(suppression, suppression.Units[1], suppression.Units[2]);
            Require(suppression.Units[2].Health > 0 && suppression.Units[2].IsSuppressed &&
                    !suppression.Units[2].IsGarrisoned &&
                    !simulation.Control.HasEnemyControl(suppression, new HexCoord(4, 0), 1),
                "artillery suppression must immediately cancel garrison and its control zone");

            var wallSuppression = new GameState(HexMap.CreateRectangle(8, 5, 0)) { ActiveNationId = 1 };
            AddCity(wallSuppression, 1, 2, new HexCoord(5, 2), 1);
            CityWallSystem.InitializeCityWalls(wallSuppression);
            var suppressedWall = simulation.Walls.FindWallAt(wallSuppression, new HexCoord(4, 2));
            AddUnit(wallSuppression, 1, 1, UnitType.LightArtillery, new HexCoord(2, 2), 10, false);
            AddUnit(wallSuppression, 2, 2, UnitType.MainInfantry, new HexCoord(4, 2), 12, true);
            Require(suppressedWall != null, "wall suppression fixture must contain the requested edge wall");
            simulation.Walls.Resolve(wallSuppression, wallSuppression.Units[1], suppressedWall);
            Require(wallSuppression.Units[2].Health > 0 && wallSuppression.Units[2].IsSuppressed &&
                    !wallSuppression.Units[2].IsGarrisoned,
                "an artillery shot shared by wall and occupant must also cancel the occupant's garrison");
            lines.Add("COMBAT_OUTCOMES passed mirrors=4/4,5/5 garrison=3/6 artillery=10H/10A " +
                      "duel=9 damage armor-close-kill=true prehit-counter=true suppression-ungarrisons=true");
        }

        private static void VerifyDefensivePlanning(GameSimulation simulation, List<string> lines)
        {
            var state = new GameState(HexMap.CreateRectangle(11, 6, 1)) { ActiveNationId = 2 };
            AddCity(state, 1, 2, new HexCoord(6, 2), 1);
            CityWallSystem.InitializeCityWalls(state);
            AddUnit(state, 1, 2, UnitType.MainInfantry, new HexCoord(5, 2), 12, false);
            AddUnit(state, 2, 1, UnitType.MainInfantry, new HexCoord(3, 1), 12, false);
            AddUnit(state, 3, 1, UnitType.MainInfantry, new HexCoord(3, 2), 12, false);
            AddUnit(state, 4, 1, UnitType.LightArmor, new HexCoord(3, 3), 22, false);

            var planner = new AiPlanner(simulation, new StrategicEvaluator());
            var plan = planner.PlanTurnStatic(state, 2);
            var defensive = plan.Find(entry => entry.Command is GarrisonCommand);
            Require(defensive != null,
                "threatened city should create a defensive objective and include a garrison in the static plan");
            Require(defensive.DecisionTrace.Contains("战略[防御城市#1") &&
                    defensive.DecisionTrace.Contains("战役[建立城墙防御支点]"),
                "defensive decision trace must expose strategic, battle and micro purpose");
            lines.Add($"DEFENSIVE_AI passed {defensive.DecisionTrace}");
        }

        private static void VerifyDevelopmentFramework(GameSimulation simulation, List<string> lines)
        {
            var state = PrototypeScenario.Create(simulation.Rules);
            var income = simulation.Economy.CalculateIncome(state, 1);
            Require(income.CityBaseEconomy == 14 && income.DomesticTradeEconomy == 6 &&
                    income.EnterpriseEconomy == 10 && income.FactoryIndustry == 18 &&
                    income.Economy == 30 && income.Industry == 18,
                $"six-city opening must yield 30 economy and 18 industry, got {income.Economy}/{income.Industry}");

            simulation.Economy.Collect(state, 1);
            simulation.Economy.Collect(state, 1);
            Require(state.Nations[1].Economy == 60 && state.Nations[1].Industry == 36,
                "two six-city-map incomes must make one main infantry affordable");
            var before = state.Units.Count;
            Require(simulation.Production.Recruit(state, 1, 1, UnitType.MainInfantry) &&
                    state.Units.Count == before + 1 && state.Nations[1].Economy == 0,
                "city recruitment must be immediate and deduct the configured infantry cost");

            var artillery = PrototypeScenario.Create(simulation.Rules);
            for (var i = 0; i < 4; i++) simulation.Economy.Collect(artillery, 1);
            Require(artillery.Nations[1].Economy == 120 && artillery.Nations[1].Industry == 72 &&
                    simulation.Production.Manufacture(artillery, 1, 2, UnitType.LightArtillery),
                "four expanded-map incomes must fund one factory-built light artillery");
            var producedArtillery = artillery.Units[39];
            Require(producedArtillery.RemainingMovement == 0 && producedArtillery.HasMoved &&
                    producedArtillery.HasAttacked && artillery.Nations[1].Economy == 0 &&
                    artillery.Nations[1].Industry == 0,
                "directly built units must enter the map exhausted and pay resources immediately");

            var armor = PrototypeScenario.Create(simulation.Rules);
            for (var i = 0; i < 7; i++) simulation.Economy.Collect(armor, 1);
            Require(armor.Nations[1].Economy == 210 && armor.Nations[1].Industry == 126 &&
                    simulation.Production.Manufacture(armor, 1, 2, UnitType.LightArmor),
                "seven opening incomes must fund exactly one factory-built light armor");

            var occupiedBuilding = PrototypeScenario.Create(simulation.Rules);
            AddUnit(occupiedBuilding, 99, 2, UnitType.MainInfantry, occupiedBuilding.Buildings[1].Position, 12,
                false);
            Require(simulation.Economy.CalculateIncome(occupiedBuilding, 1).EnterpriseEconomy == 5,
                "an enterprise occupied by an enemy unit must stop producing");

            var captured = PrototypeScenario.Create(simulation.Rules);
            AddUnit(captured, 99, 2, UnitType.MainInfantry, captured.Cities[1].Center, 12, false);
            simulation.Cities.BeginOccupationIfCenter(captured, captured.Units[99]);
            Require(simulation.Economy.CalculateIncome(captured, 1).Economy < 30,
                "a city whose center is occupied must stop its city and building income");
            Require(simulation.Cities.CompleteOccupation(captured, captured.Units[99], captured.Cities[1]) &&
                    captured.Buildings[1].NationId == 2 && captured.Buildings[2].NationId == 2 &&
                    simulation.Economy.CalculateIncome(captured, 2).Economy > 0,
                "a formally captured city and its unoccupied buildings must produce for the new owner");
            lines.Add("DEVELOPMENT_RULES passed opening=30E/18I configured-costs/capture/occupation=true");
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
        }

        private static void MoveUnit(GameState state, UnitState unit, HexCoord destination)
        {
            state.Map.Get(unit.Position).UnitId = null;
            unit.Position = destination;
            state.Map.Get(destination).UnitId = unit.Id;
        }

        private static void AddUnit(GameState state, int id, int nationId, UnitType type, HexCoord position,
            int health, bool garrisoned)
        {
            state.Units.Add(id, new UnitState
            {
                Id = id,
                NationId = nationId,
                Type = type,
                Position = position,
                Health = System.Math.Min(health, RulesCatalog.CreateDefault().Unit(type).MaxHealth),
                RemainingMovement = RulesCatalog.CreateDefault().Unit(type).Movement,
                IsGarrisoned = garrisoned
            });
            state.Map.Get(position).UnitId = id;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new System.InvalidOperationException(message);
        }

        private static int? UnitId(GameCommand command)
        {
            return command switch
            {
                MoveCommand move => move.UnitId,
                AttackCommand attack => attack.AttackerId,
                AttackWallCommand attackWall => attackWall.AttackerId,
                GarrisonCommand garrison => garrison.UnitId,
                OccupyCityCommand occupy => occupy.UnitId,
                PromoteUnitCommand promote => promote.UnitId,
                HealCommand heal => heal.HealerId,
                _ => null
            };
        }

        private static string CommandDetails(GameCommand command)
        {
            return command switch
            {
                MoveCommand move => $"command=Move unit={move.UnitId} to={move.Destination}",
                AttackCommand attack =>
                    $"command=Attack attacker={attack.AttackerId} defender={attack.DefenderId}",
                AttackWallCommand wall => $"command=AttackWall attacker={wall.AttackerId} wall={wall.WallId}",
                HealCommand heal => $"command=Heal healer={heal.HealerId} target={heal.TargetId}",
                GarrisonCommand garrison => $"command=Garrison unit={garrison.UnitId}",
                OccupyCityCommand occupy => $"command=Occupy unit={occupy.UnitId} city={occupy.CityId}",
                PromoteUnitCommand promote => $"command=Promote unit={promote.UnitId}",
                _ => $"command={command.Type}"
            };
        }
    }
}

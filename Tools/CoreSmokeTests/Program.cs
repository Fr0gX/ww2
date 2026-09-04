using System;
using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;
using WW2.Runtime;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var rules = RulesCatalog.CreateDefault();
            var simulation = new GameSimulation(rules);
            var state = CreateState();

            Assert(state.Map.Get(new HexCoord(1, 1)).Terrain == TerrainType.Plain, "map creation");

            var preview = simulation.Combat.Preview(state, state.Units[1], state.Units[2]);
            Assert(preview.Damage == 4, $"deterministic infantry damage: {preview.Damage}");
            Assert(preview.CounterDamage == 5, $"prepared defensive retaliation: {preview.CounterDamage}");
            var stats = simulation.Combat.GetEffectiveStats(state, state.Units[1]);
            Assert(stats.BaseAttack == 4 && stats.EffectiveAttack == 4 && stats.EffectiveDefense == 5,
                "effective combat stats expose corrected attack and defense");

            Assert(simulation.Supply.IsUnitSupplied(state, state.Units[1]), "friendly supply connection");
            var visible = simulation.Visibility.CalculateVisibleCells(state, 1);
            Assert(visible.Contains(new HexCoord(2, 1)), "unit visibility");

            VerifyActionPointRules(simulation);
            VerifyCombatRules(simulation);
            VerifyBranchAndMedicalRules(simulation);
            VerifyWallAndOccupationRules(simulation);
            VerifySupplyPenalties(simulation);
            VerifyIndependentCitySupply(simulation);
            VerifyPrototypeScenarioSupply(simulation);

            Console.WriteLine("Core smoke tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void VerifyActionPointRules(GameSimulation simulation)
    {
        var movementState = new GameState(HexMap.CreateRectangle(5, 5, 1)) { ActiveNationId = 1 };
        AddUnit(movementState, 10, 1, UnitType.MainInfantry, new HexCoord(2, 2), 100);
        Assert(simulation.TryExecute(movementState, new MoveCommand(1, 10, new HexCoord(3, 2))),
            "first incremental move");
        Assert(movementState.Units[10].RemainingMovement == 5, "one plain tile costs one movement point");
        Assert(simulation.TryExecute(movementState, new MoveCommand(1, 10, new HexCoord(4, 2))),
            "second incremental move");
        Assert(movementState.Units[10].RemainingMovement == 4, "movement can be split across commands");

        movementState.Map.Get(new HexCoord(4, 3)).Terrain = TerrainType.Mountain;
        Assert(simulation.TryExecute(movementState, new MoveCommand(1, 10, new HexCoord(4, 3))),
            "move into complex terrain");
        Assert(movementState.Units[10].RemainingMovement == 3, "infantry ignores mountain movement cost");

        var armorMovement = new GameState(HexMap.CreateRectangle(4, 3, 1)) { ActiveNationId = 1 };
        AddUnit(armorMovement, 11, 1, UnitType.LightArmor, new HexCoord(1, 1), 115);
        armorMovement.Map.Get(new HexCoord(2, 1)).Terrain = TerrainType.Mountain;
        Assert(simulation.TryExecute(armorMovement, new MoveCommand(1, 11, new HexCoord(2, 1))),
            "armor enters mountain");
        Assert(armorMovement.Units[11].RemainingMovement == 4, "armor pays increased mountain movement cost");

        var infantryAttack = CreateAttackState(UnitType.MainInfantry);
        Assert(simulation.TryExecute(infantryAttack, new AttackCommand(1, 1, 2)), "infantry attack");
        Assert(infantryAttack.Units[1].RemainingMovement == 0, "ordinary attack exhausts movement");

        var armorAttack = CreateAttackState(UnitType.LightArmor);
        Assert(simulation.TryExecute(armorAttack, new AttackCommand(1, 1, 2)), "armor attack");
        Assert(armorAttack.Units[1].RemainingMovement == 10, "armor attack preserves movement");
        Assert(simulation.TryExecute(armorAttack, new MoveCommand(1, 1, new HexCoord(0, 1))),
            "armor moves after attacking");
        Assert(armorAttack.Units[1].RemainingMovement == 9, "armor post-attack movement cost");
    }

    private static void VerifyCombatRules(GameSimulation simulation)
    {
        var fullStrength = CreateAttackState(UnitType.MainInfantry);
        var fullPreview = simulation.Combat.Preview(fullStrength, fullStrength.Units[1], fullStrength.Units[2]);
        var halfStrength = CreateAttackState(UnitType.MainInfantry);
        halfStrength.Units[1].Health = 6;
        var halfPreview = simulation.Combat.Preview(halfStrength, halfStrength.Units[1], halfStrength.Units[2]);
        Assert(halfPreview.Damage < fullPreview.Damage, "attack damage follows attacker health percentage");

        var artilleryAttack = new GameState(HexMap.CreateRectangle(5, 3, 1)) { ActiveNationId = 1 };
        AddUnit(artilleryAttack, 1, 1, UnitType.LightArtillery, new HexCoord(1, 1), 80);
        AddUnit(artilleryAttack, 2, 2, UnitType.MainInfantry, new HexCoord(3, 1), 100);
        var artilleryPreview = simulation.Combat.Preview(artilleryAttack, artilleryAttack.Units[1],
            artilleryAttack.Units[2]);
        Assert(artilleryPreview.Damage > 0 && artilleryPreview.CounterDamage == 0,
            "artillery attack prevents retaliation");
        Assert(artilleryPreview.AppliesSuppression && artilleryPreview.SuppressionChance == 1f,
            "artillery suppression is deterministic");

        var rangedWallAttack = new GameState(HexMap.CreateRectangle(7, 5, 0)) { ActiveNationId = 1 };
        rangedWallAttack.Cities.Add(2, new CityState
        {
            Id = 2,
            NationId = 2,
            Center = new HexCoord(4, 2),
            Level = 1,
            IsFormalOccupation = true
        });
        rangedWallAttack.Map.Get(new HexCoord(4, 2)).CityId = 2;
        CityWallSystem.InitializeCityWalls(rangedWallAttack);
        AddUnit(rangedWallAttack, 1, 1, UnitType.LightArtillery, new HexCoord(1, 2), 80);
        AddUnit(rangedWallAttack, 2, 2, UnitType.MainInfantry, new HexCoord(3, 2), 100);
        Assert(simulation.Walls.FindWallBetween(rangedWallAttack, new HexCoord(2, 2), new HexCoord(3, 2))?.Health > 0,
            "defender stands behind an intact wall");
        Assert(simulation.TryExecute(rangedWallAttack, new AttackCommand(1, 1, 2)),
            "ranged attacks may target a unit behind a wall");

        var artilleryDefends = new GameState(HexMap.CreateRectangle(4, 3, 1)) { ActiveNationId = 1 };
        AddUnit(artilleryDefends, 1, 1, UnitType.MainInfantry, new HexCoord(1, 1), 100);
        AddUnit(artilleryDefends, 2, 2, UnitType.LightArtillery, new HexCoord(2, 1), 80);
        var noArtilleryCounter = simulation.Combat.Preview(artilleryDefends, artilleryDefends.Units[1],
            artilleryDefends.Units[2]);
        Assert(noArtilleryCounter.Damage > 0 && noArtilleryCounter.CounterDamage == 0,
            "artillery cannot retaliate");

        var exhausted = CreateAttackState(UnitType.MainInfantry);
        exhausted.Units[1].RemainingMovement = 0;
        Assert(!simulation.TryExecute(exhausted, new AttackCommand(1, 1, 2)),
            "zero movement units cannot attack");
    }

    private static void VerifyBranchAndMedicalRules(GameSimulation simulation)
    {
        var rules = simulation.Rules;
        Assert(rules.Unit(UnitType.MainInfantry).Branch == UnitBranch.Infantry &&
               rules.HasAbility(UnitType.MainInfantry, UnitAbility.IgnoresTerrainMovement),
            "main infantry inherits infantry branch ability");
        Assert(rules.Unit(UnitType.Medic).Branch == UnitBranch.Infantry &&
               rules.HasAbility(UnitType.Medic, UnitAbility.Healing),
            "medic inherits infantry branch and owns healing");
        Assert(rules.HasAbility(UnitType.LightArmor, UnitAbility.PreservesMovementAfterAttack) &&
               !rules.HasAbility(UnitType.LightArmor, UnitAbility.SupplyDependent) &&
               rules.Unit(UnitType.LightArmor).Vision == 3,
            "light armor preserves movement and owns the extended vision profile");
        Assert(rules.HasAbility(UnitType.LightArtillery, UnitAbility.IgnoresCityWall) &&
               rules.HasAbility(UnitType.LightArtillery, UnitAbility.PreventsCounterattack),
            "light artillery inherits artillery branch abilities");

        var state = new GameState(HexMap.CreateRectangle(4, 3, 1)) { ActiveNationId = 1 };
        AddUnit(state, 1, 1, UnitType.Medic, new HexCoord(1, 1), 80);
        AddUnit(state, 2, 1, UnitType.MainInfantry, new HexCoord(2, 1), 4);
        Assert(simulation.Medical.Preview(state, state.Units[1], state.Units[2]) == 4,
            "medic previews configured healing amount");
        Assert(simulation.TryExecute(state, new HealCommand(1, 1, 2)) && state.Units[2].Health == 8 &&
               state.Units[1].RemainingMovement == 0,
            "medical action heals an adjacent ally and consumes the turn");
        Assert(!simulation.Control.HasEnemyControl(state, new HexCoord(0, 1), 2),
            "medic does not project a control zone");
    }

    private static void VerifyWallAndOccupationRules(GameSimulation simulation)
    {
        var state = new GameState(HexMap.CreateRectangle(7, 5, 0)) { ActiveNationId = 1 };
        state.Nations.Add(1, new NationState { Id = 1, Name = "Blue" });
        state.Nations.Add(2, new NationState { Id = 2, Name = "Red" });
        state.Cities.Add(1, new CityState
        {
            Id = 1,
            NationId = 1,
            Center = new HexCoord(0, 2),
            Level = 1,
            IsFormalOccupation = true
        });
        state.Cities.Add(2, new CityState
        {
            Id = 2,
            NationId = 2,
            Center = new HexCoord(4, 2),
            Level = 1,
            IsFormalOccupation = true
        });
        state.Map.Get(new HexCoord(0, 2)).CityId = 1;
        state.Map.Get(new HexCoord(4, 2)).CityId = 2;
        for (var q = 0; q <= 4; q++) state.Map.Get(new HexCoord(q, 2)).OwnerNationId = 1;
        CityWallSystem.InitializeCityWalls(state);
        AddUnit(state, 1, 1, UnitType.MainInfantry, new HexCoord(2, 2), 100);
        var wall = simulation.Walls.FindWallBetween(state, new HexCoord(2, 2), new HexCoord(3, 2));
        Assert(wall != null, "city boundary creates a wall unit");
        var enemyCityWallCount = 0;
        foreach (var candidate in state.CityWalls.Values)
            if (candidate.CityId == 2) enemyCityWallCount++;
        Assert(enemyCityWallCount == 6, "level-one city creates one wall state per boundary cell");
        Assert(simulation.Walls.FindWallBetween(state, new HexCoord(2, 3), new HexCoord(3, 2))?.Id == wall.Id,
            "multiple exterior faces of one boundary cell share one wall state");
        Assert(!simulation.TryExecute(state, new MoveCommand(1, 1, new HexCoord(3, 2))),
            "intact hostile wall blocks entry");

        var basePreview = simulation.Walls.Preview(state, state.Units[1], wall);
        AddUnit(state, 2, 2, UnitType.MainInfantry, new HexCoord(3, 2), 100);
        state.Units[2].IsGarrisoned = true;
        var reinforced = simulation.Walls.Preview(state, state.Units[1], wall);
        Assert(reinforced.GarrisonDefense > 0 && reinforced.SynergyMultiplier == 1f &&
               reinforced.Damage < basePreview.Damage, "garrison and wall have greater-than-additive defense");

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
        AddUnit(artillery, 1, 1, UnitType.LightArtillery, new HexCoord(1, 2), 9);
        AddUnit(artillery, 2, 2, UnitType.MainInfantry, new HexCoord(3, 2), 12);
        artillery.Units[2].IsGarrisoned = true;
        var artilleryWall = simulation.Walls.FindWallAt(artillery, new HexCoord(3, 2));
        var bombardment = simulation.Walls.Preview(artillery, artillery.Units[1], artilleryWall);
        Assert(bombardment.Damage == 6 && bombardment.GarrisonDamage == 4 &&
               bombardment.CounterDamage == 0 && bombardment.AppliesSuppression,
            "artillery damages wall and garrison together with deterministic suppression");
        state.Map.Get(new HexCoord(3, 2)).UnitId = null;
        state.Units.Remove(2);

        wall.Health = basePreview.Damage;
        Assert(simulation.TryExecute(state, new AttackWallCommand(1, 1, wall.Id)) && wall.Health == 0,
            "wall can be attacked and destroyed");
        simulation.Turns.BeginNationTurn(state, 1);
        Assert(simulation.TryExecute(state, new MoveCommand(1, 1, new HexCoord(3, 2))),
            "destroyed wall permits entry");
        simulation.Turns.BeginNationTurn(state, 1);
        Assert(simulation.TryExecute(state, new MoveCommand(1, 1, new HexCoord(4, 2))),
            "unit enters enemy city center");
        Assert(state.Cities[2].IsDisabled && state.Cities[2].OccupyingUnitId == 1,
            "city becomes uncontrolled and awaits active occupation");
        Assert(simulation.Cities.CanOccupy(state, state.Units[1], state.Cities[2], out _),
            "infantry may occupy immediately");
        Assert(simulation.TryExecute(state, new OccupyCityCommand(1, 1, 2)) && state.Cities[2].NationId == 1,
            "occupation requires an explicit command");

        var delayed = new GameState(HexMap.CreateRectangle(5, 3, 0)) { ActiveNationId = 1, Round = 1 };
        delayed.Cities.Add(1, new CityState
        {
            Id = 1,
            NationId = 2,
            Center = new HexCoord(3, 1),
            Level = 1,
            IsFormalOccupation = true
        });
        delayed.Map.Get(new HexCoord(3, 1)).CityId = 1;
        AddUnit(delayed, 1, 1, UnitType.LightArmor, new HexCoord(3, 1), 115);
        simulation.Cities.BeginOccupationIfCenter(delayed, delayed.Units[1]);
        Assert(!simulation.Cities.CanOccupy(delayed, delayed.Units[1], delayed.Cities[1], out _),
            "non-infantry waits until the next round to occupy");
        delayed.Round = 2;
        Assert(simulation.TryExecute(delayed, new OccupyCityCommand(1, 1, 1)) &&
               delayed.Units[1].RemainingMovement == 0,
            "next-round occupation needs one click and consumes all action points");
    }

    private static void VerifySupplyPenalties(GameSimulation simulation)
    {
        var state = new GameState(HexMap.CreateRectangle(4, 3, 0)) { ActiveNationId = 1 };
        AddUnit(state, 1, 1, UnitType.MainInfantry, new HexCoord(2, 1), 100);
        var status = simulation.Supply.GetStatus(state, state.Units[1]);
        Assert(status.Tier == 3 && status.AttackMultiplier == 0.40f && status.MovementPenalty == 6,
            "disconnected supply applies maximum progressive penalty");
        simulation.Turns.BeginNationTurn(state, 1);
        Assert(state.Units[1].RemainingMovement == 0, "maximum supply penalty may exhaust all action points");
    }

    private static void VerifyIndependentCitySupply(GameSimulation simulation)
    {
        var state = new GameState(HexMap.CreateRectangle(10, 3, 0)) { ActiveNationId = 1 };
        AddCity(state, 1, 1, new HexCoord(0, 1));
        AddCity(state, 2, 1, new HexCoord(7, 1));
        AddCity(state, 3, 2, new HexCoord(9, 2));
        AddUnit(state, 20, 1, UnitType.MainInfantry, new HexCoord(8, 1), 100);

        var suppliedCities = simulation.Supply.FindSuppliedCities(state, 1);
        Assert(suppliedCities.Contains(1) && suppliedCities.Contains(2),
            "every valid friendly city is an independent supply source without a capital");
        Assert(simulation.Supply.IsUnitSupplied(state, state.Units[20]),
            "unit receives supply from its nearest valid city");

        AddUnit(state, 21, 2, UnitType.MainInfantry, new HexCoord(4, 1), 100);
        suppliedCities = simulation.Supply.FindSuppliedCities(state, 1);
        Assert(suppliedCities.Contains(2), "a city does not depend on a capital connection");
    }

    private static void VerifyPrototypeScenarioSupply(GameSimulation simulation)
    {
        var state = PrototypeScenario.Create();
        Assert(state.Map.Cells.Count == 631, $"expanded prototype has 631 cells ({state.Map.Cells.Count})");
        Assert(state.Cities.Count == 6, $"prototype has exactly six cities ({state.Cities.Count})");
        foreach (var unit in state.Units.Values)
        {
            state.Map.Get(unit.Position).UnitId = null;
        }
        state.Units.Clear();
        foreach (var wall in state.CityWalls.Values) wall.Health = 0;
        simulation.Supply.RecalculateCoverage(state);

        foreach (var cell in state.Map.Cells.Values)
        {
            if (cell.Coord.Q <= 0)
                Assert(simulation.Supply.GetStatusAt(state, 1, cell.Coord).IsCovered,
                    $"blue supply covers own half at {cell.Coord}");
            if (cell.Coord.Q >= 0)
                Assert(simulation.Supply.GetStatusAt(state, 2, cell.Coord).IsCovered,
                    $"red supply covers own half at {cell.Coord}");
        }

        Assert(simulation.Supply.GetStatusAt(state, 1, state.Cities[4].Center).IsCovered &&
               simulation.Supply.GetStatusAt(state, 1, state.Cities[6].Center).IsCovered,
            "blue supply reaches both red front cities");
        Assert(simulation.Supply.GetStatusAt(state, 2, state.Cities[3].Center).IsCovered &&
               simulation.Supply.GetStatusAt(state, 2, state.Cities[5].Center).IsCovered,
            "red supply reaches both blue front cities");
        Assert(!simulation.Supply.GetStatusAt(state, 1, state.Cities[2].Center).IsCovered &&
               !simulation.Supply.GetStatusAt(state, 2, state.Cities[1].Center).IsCovered,
            "neither supply field reaches the opposing rear city");
    }

    private static GameState CreateAttackState(UnitType attackerType)
    {
        var state = new GameState(HexMap.CreateRectangle(4, 3, 1)) { ActiveNationId = 1 };
        AddUnit(state, 1, 1, attackerType, new HexCoord(1, 1),
            RulesCatalog.CreateDefault().Unit(attackerType).MaxHealth);
        AddUnit(state, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 100);
        return state;
    }

    private static GameState CreateState()
    {
        var state = new GameState(HexMap.CreateRectangle(5, 4, 1)) { ActiveNationId = 1 };
        state.Nations.Add(1, new NationState { Id = 1, Name = "Blue" });
        state.Nations.Add(2, new NationState { Id = 2, Name = "Red" });
        state.Cities.Add(1, new CityState { Id = 1, NationId = 1, Center = new HexCoord(0, 1), Level = 1 });
        state.Cities.Add(2, new CityState { Id = 2, NationId = 2, Center = new HexCoord(4, 1), Level = 1 });
        state.Map.Get(new HexCoord(0, 1)).OwnerNationId = 1;
        state.Map.Get(new HexCoord(4, 1)).OwnerNationId = 2;

        AddUnit(state, 1, 1, UnitType.MainInfantry, new HexCoord(1, 1), 100);
        AddUnit(state, 2, 2, UnitType.MainInfantry, new HexCoord(2, 1), 100);
        return state;
    }

    private static void AddUnit(GameState state, int id, int nationId, UnitType type, HexCoord position, int health)
    {
        state.Units.Add(id, new UnitState
        {
            Id = id,
            NationId = nationId,
            Type = type,
            Position = position,
            Health = System.Math.Min(health, RulesCatalog.CreateDefault().Unit(type).MaxHealth),
            RemainingMovement = RulesCatalog.CreateDefault().Unit(type).Movement
        });
        state.Map.Get(position).UnitId = id;
    }

    private static void AddCity(GameState state, int id, int nationId, HexCoord center)
    {
        state.Cities.Add(id, new CityState
        {
            Id = id,
            NationId = nationId,
            Center = center,
            Level = 1,
            IsFormalOccupation = true
        });
        state.Map.Get(center).CityId = id;
        state.Map.Get(center).OwnerNationId = nationId;
    }

    private static void AddRoad(GameState state, HexCoord start, HexCoord end)
    {
        var cursor = start;
        while (!cursor.Equals(end))
        {
            var next = cursor.Neighbor(0);
            state.Map.Get(cursor).RoadNeighbors.Add(next);
            state.Map.Get(next).RoadNeighbors.Add(cursor);
            cursor = next;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}

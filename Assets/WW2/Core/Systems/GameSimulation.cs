using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class GameSimulation
    {
        public GameSimulation(RulesCatalog rules)
        {
            Rules = rules;
            Control = new ControlSystem(rules);
            Pathfinder = new HexPathfinder();
            Supply = new SupplySystem(rules, Control, Pathfinder);
            Combat = new CombatSystem(rules, Supply);
            Medical = new MedicalSystem(rules);
            Walls = new CityWallSystem(rules, Control, Combat);
            Visibility = new VisibilitySystem(rules, Supply);
            Movement = new MovementSystem(rules, Control, Pathfinder, Walls, Visibility);
            Cities = new CitySystem(rules);
            Economy = new EconomySystem(Control);
            Production = new ProductionSystem(rules, Economy);
            Diplomacy = new DiplomacySystem();
            Technology = new TechnologySystem();
            Turns = new TurnSystem(rules, Economy, Supply, Walls);
        }

        public RulesCatalog Rules { get; }
        public ControlSystem Control { get; }
        public HexPathfinder Pathfinder { get; }
        public CombatSystem Combat { get; }
        public MedicalSystem Medical { get; }
        public CityWallSystem Walls { get; }
        public MovementSystem Movement { get; }
        public SupplySystem Supply { get; }
        public CitySystem Cities { get; }
        public VisibilitySystem Visibility { get; }
        public EconomySystem Economy { get; }
        public ProductionSystem Production { get; }
        public DiplomacySystem Diplomacy { get; }
        public TechnologySystem Technology { get; }
        public TurnSystem Turns { get; }

        public bool TryExecute(GameState state, GameCommand command)
        {
            if (command.NationId != state.ActiveNationId)
            {
                return false;
            }

            switch (command)
            {
                case MoveCommand move:
                    return TryMove(state, command.NationId, move.UnitId, move.Destination);
                case AttackCommand attack:
                    return TryAttack(state, command.NationId, attack.AttackerId, attack.DefenderId);
                case AttackWallCommand attackWall:
                    return TryAttackWall(state, command.NationId, attackWall.AttackerId, attackWall.WallId);
                case HealCommand heal:
                    return TryHeal(state, command.NationId, heal.HealerId, heal.TargetId);
                case GarrisonCommand garrison:
                    return TryGarrison(state, command.NationId, garrison.UnitId);
                case OccupyCityCommand occupy:
                    return TryOccupyCity(state, command.NationId, occupy.UnitId, occupy.CityId);
                case PromoteUnitCommand promote:
                    return TryPromoteUnit(state, command.NationId, promote.UnitId);
                case RecruitUnitCommand recruit:
                    return Production.Recruit(state, command.NationId, recruit.CityId, recruit.UnitType);
                case ManufactureUnitCommand manufacture:
                    return Production.Manufacture(state, command.NationId, manufacture.FactoryId,
                        manufacture.UnitType);
                case EndTurnCommand endTurn:
                    Turns.EndNationTurn(state, endTurn.NextNationId);
                    Turns.BeginNationTurn(state, endTurn.NextNationId);
                    return true;
                default:
                    return false;
            }
        }

        private bool TryMove(GameState state, int nationId, int unitId, HexCoord destination)
        {
            if (!state.Units.TryGetValue(unitId, out var mover) || mover.NationId != nationId)
            {
                return false;
            }

            if (!Movement.CanMove(state, mover, destination, out var path))
            {
                return false;
            }

            Cities.CancelOccupationIfLeaving(state, mover, destination);
            if (!Movement.TryMove(state, mover, destination, path))
            {
                return false;
            }

            Cities.BeginOccupationIfCenter(state, mover);
            return true;
        }

        private bool TryAttack(GameState state, int nationId, int attackerId, int defenderId)
        {
            if (!state.Units.TryGetValue(attackerId, out var attacker) ||
                !state.Units.TryGetValue(defenderId, out var defender) ||
                attacker.NationId != nationId || defender.NationId == nationId || attacker.HasAttacked ||
                !attacker.CanAttackThisTurn)
            {
                return false;
            }

            var shieldingWall = Walls.FindWallAt(state, defender.Position);
            if (shieldingWall != null && shieldingWall.Health > 0 &&
                state.Cities.TryGetValue(shieldingWall.CityId, out var wallCity) &&
                wallCity.NationId == defender.NationId && wallCity.NationId != attacker.NationId)
            {
                return false;
            }

            var preview = Combat.Preview(state, attacker, defender);
            if (preview.Damage <= 0)
            {
                return false;
            }

            Combat.Resolve(state, attacker, defender);
            if (defender.Health <= 0) RegisterKill(attacker);
            if (!HasAbility(attacker.Type, UnitAbility.PreservesMovementAfterAttack))
            {
                attacker.RemainingMovement = 0;
            }
            attacker.IsPinnedByEnemyControl = false;
            attacker.HasUnspentAttackAfterControlStop = false;
            RemoveDestroyedUnit(state, attacker);
            RemoveDestroyedUnit(state, defender);
            RefreshControlPins(state);
            return true;
        }

        private bool TryAttackWall(GameState state, int nationId, int attackerId, int wallId)
        {
            if (!state.Units.TryGetValue(attackerId, out var attacker) ||
                !state.CityWalls.TryGetValue(wallId, out var wall) || attacker.NationId != nationId ||
                attacker.HasAttacked || !attacker.CanAttackThisTurn)
            {
                return false;
            }

            var preview = Walls.Preview(state, attacker, wall);
            if (preview.Damage <= 0) return false;
            preview = Walls.Resolve(state, attacker, wall);
            if (preview.GarrisonUnitId.HasValue &&
                state.Units.TryGetValue(preview.GarrisonUnitId.Value, out var defeatedGarrison) &&
                defeatedGarrison.Health <= 0)
            {
                RegisterKill(attacker);
            }
            if (!HasAbility(attacker.Type, UnitAbility.PreservesMovementAfterAttack)) attacker.RemainingMovement = 0;
            attacker.IsPinnedByEnemyControl = false;
            attacker.HasUnspentAttackAfterControlStop = false;
            RemoveDestroyedUnit(state, attacker);
            if (preview.GarrisonUnitId.HasValue &&
                state.Units.TryGetValue(preview.GarrisonUnitId.Value, out var garrison))
            {
                RemoveDestroyedUnit(state, garrison);
            }
            RefreshControlPins(state);
            return true;
        }

        private bool TryHeal(GameState state, int nationId, int healerId, int targetId)
        {
            if (!state.Units.TryGetValue(healerId, out var healer) || healer.NationId != nationId ||
                !state.Units.TryGetValue(targetId, out var target) || target.NationId != nationId)
            {
                return false;
            }

            return Medical.Resolve(state, healer, target) > 0;
        }

        private bool TryOccupyCity(GameState state, int nationId, int unitId, int cityId)
        {
            return state.Units.TryGetValue(unitId, out var unit) && unit.NationId == nationId &&
                   state.Cities.TryGetValue(cityId, out var city) && Cities.CompleteOccupation(state, unit, city);
        }

        public bool CanPromote(UnitState unit)
        {
            return unit != null && unit.Health > 0 && unit.Level < 4 &&
                   unit.PromotionKills >= RuleMath.KillsRequiredForPromotion(unit.Level);
        }

        private bool TryPromoteUnit(GameState state, int nationId, int unitId)
        {
            if (!state.Units.TryGetValue(unitId, out var unit) || unit.NationId != nationId || !CanPromote(unit))
                return false;
            unit.Level++;
            unit.PromotionKills = 0;
            unit.Health = RuleMath.Round(Rules.Unit(unit.Type).MaxHealth * RuleMath.LevelMultiplier(unit.Level));
            return true;
        }

        private bool TryGarrison(GameState state, int nationId, int unitId)
        {
            if (!state.Units.TryGetValue(unitId, out var unit) || unit.NationId != nationId ||
                !CanGarrison(state, unit))
            {
                return false;
            }

            unit.IsGarrisoned = true;
            unit.RemainingMovement = 0;
            unit.IsPinnedByEnemyControl = false;
            unit.HasUnspentAttackAfterControlStop = false;
            unit.HasMoved = true;
            unit.HasAttacked = true;
            return true;
        }

        public bool CanGarrison(GameState state, UnitState unit)
        {
            if (unit == null || unit.Health <= 0 || unit.RemainingMovement <= 0 || unit.HasAttacked ||
                unit.IsGarrisoned) return false;
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId != unit.NationId && city.Center.DistanceTo(unit.Position) <= city.Level)
                    return false;
            }
            var wall = Walls.FindWallAt(state, unit.Position);
            return wall == null || Rules.HasAbility(unit.Type, UnitAbility.GarrisonExpert);
        }

        private bool HasAbility(UnitType type, UnitAbility ability) => Rules.HasAbility(type, ability);

        private void RegisterKill(UnitState unit)
        {
            if (unit == null || unit.Level >= 4) return;
            unit.PromotionKills = System.Math.Min(RuleMath.KillsRequiredForPromotion(unit.Level),
                unit.PromotionKills + 1);
        }

        private void RemoveDestroyedUnit(GameState state, UnitState unit)
        {
            if (unit.Health > 0)
            {
                return;
            }

            if (state.Map.TryGet(unit.Position, out var cell) && cell.UnitId == unit.Id)
            {
                cell.UnitId = null;
            }

            Cities.CancelOccupationForUnit(state, unit.Id);
            state.Units.Remove(unit.Id);
        }

        private void RefreshControlPins(GameState state)
        {
            foreach (var unit in state.Units.Values)
            {
                if (!unit.IsPinnedByEnemyControl || Control.HasEnemyControl(state, unit.Position, unit.NationId))
                    continue;
                // The movement already spent on contact stays spent, while the preserved attack remains available.
                unit.IsPinnedByEnemyControl = false;
            }
        }
    }
}

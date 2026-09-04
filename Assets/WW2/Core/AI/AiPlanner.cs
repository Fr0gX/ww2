using System;
using System.Collections.Generic;
using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;

namespace WW2.Core.AI
{
    public sealed class AiPlan
    {
        public List<GameCommand> Commands { get; } = new List<GameCommand>();
        public float Score { get; set; }
    }

    public interface IAiEvaluator
    {
        float Evaluate(GameState state, int nationId);
    }

    public sealed class StrategicEvaluator : IAiEvaluator
    {
        public float Evaluate(GameState state, int nationId)
        {
            var score = 0f;
            foreach (var city in state.Cities.Values)
            {
                score += city.NationId == nationId ? 100f : -25f;
                if (city.NationId == nationId && city.IsDisabled)
                {
                    score -= 50f;
                }
            }

            foreach (var unit in state.Units.Values)
            {
                score += unit.NationId == nationId ? unit.Health * 0.8f : -unit.Health * 0.8f;
            }

            if (state.Nations.TryGetValue(nationId, out var nation))
            {
                score += nation.Economy * 0.1f + nation.Industry * 0.1f + nation.Research * 0.05f;
            }

            return score;
        }
    }

    internal sealed class LegacyAiPlanner
    {
        private readonly GameSimulation _simulation;
        private readonly IAiEvaluator _evaluator;
        private float _lastMoveGain;
        private int _lastMoveProgress;
        private bool _lastMoveWasFallback;

        public LegacyAiPlanner(GameSimulation simulation, IAiEvaluator evaluator)
        {
            _simulation = simulation;
            _evaluator = evaluator;
        }

        public string LastDecisionTrace { get; private set; } = string.Empty;

        public AiPlan Plan(GameState state, int nationId)
        {
            var plan = new AiPlan { Score = _evaluator.Evaluate(state, nationId) };
            var command = ChooseNextCommand(state, nationId);
            if (command != null)
            {
                plan.Commands.Add(command);
            }

            return plan;
        }

        public GameCommand ChooseNextCommand(GameState state, int nationId)
        {
            if (state.ActiveNationId != nationId)
            {
                return null;
            }

            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || !_simulation.CanPromote(unit)) continue;
                LastDecisionTrace = $"晋升：{unit.Type}#{unit.Id}达到当前等级击杀要求";
                return new PromoteUnitCommand(nationId, unit.Id);
            }

            var occupation = FindOccupation(state, nationId);
            if (occupation != null)
            {
                LastDecisionTrace = $"占领优先：单位#{occupation.UnitId}已满足城市#{occupation.CityId}的占领条件";
                return occupation;
            }

            var heal = FindBestHeal(state, nationId);
            if (heal != null)
            {
                var healer = state.Units[heal.HealerId];
                var target = state.Units[heal.TargetId];
                var amount = _simulation.Medical.Preview(state, healer, target);
                LastDecisionTrace = $"医疗：{healer.Type}#{healer.Id}@{healer.Position} -> " +
                                    $"{target.Type}#{target.Id}@{target.Position}，恢复={amount}";
                return heal;
            }

            var attack = FindBestAttack(state, nationId);
            if (attack != null)
            {
                var attacker = state.Units[attack.AttackerId];
                var defender = state.Units[attack.DefenderId];
                var preview = _simulation.Combat.Preview(state, attacker, defender);
                LastDecisionTrace = $"攻击优先：{attacker.Type}#{attacker.Id}@{attacker.Position} -> " +
                                    $"{defender.Type}#{defender.Id}@{defender.Position}，预计伤害={preview.Damage}，" +
                                    $"反击={preview.CounterDamage}，击败={preview.Damage >= defender.Health}";
                return attack;
            }

            var wallAttack = FindBestWallAttack(state, nationId);
            if (wallAttack != null)
            {
                var attacker = state.Units[wallAttack.AttackerId];
                var wall = state.CityWalls[wallAttack.WallId];
                var preview = _simulation.Walls.Preview(state, attacker, wall);
                LastDecisionTrace = $"破城优先：{attacker.Type}#{attacker.Id}@{attacker.Position} -> " +
                                    $"城墙#{wall.Id}@{wall.Position}，预计伤害={preview.Damage}，" +
                                    $"驻军伤害={preview.GarrisonDamage}，反击={preview.CounterDamage}，" +
                                    $"摧毁={preview.Damage >= wall.Health}";
                return wallAttack;
            }

            var move = FindBestMove(state, nationId);
            if (move != null)
            {
                var mover = state.Units[move.UnitId];
                LastDecisionTrace = $"机动：{mover.Type}#{mover.Id}@{mover.Position} -> {move.Destination}，" +
                                    $"评分变化={_lastMoveGain:0.0}，接近敌城={_lastMoveProgress}格，" +
                                    $"模式={(_lastMoveWasFallback ? "战略推进" : "局部最优")}";
                return move;
            }

            var garrison = FindUsefulGarrison(state, nationId);
            if (garrison != null)
            {
                var unit = state.Units[garrison.UnitId];
                LastDecisionTrace = $"驻扎：{unit.Type}#{unit.Id}@{unit.Position}，当前无更高价值的攻击或机动";
                return garrison;
            }
            LastDecisionTrace = "无合法命令：没有可占领城市、有效攻击、可推进机动或有价值驻扎";
            return null;
        }

        private AttackCommand FindBestAttack(GameState state, int nationId)
        {
            AttackCommand best = null;
            var bestValue = 4f;
            var visible = _simulation.Visibility.CalculateVisibleCells(state, nationId);
            foreach (var attacker in state.Units.Values)
            {
                if (attacker.NationId != nationId || attacker.HasAttacked || attacker.Health <= 0 ||
                    attacker.RemainingMovement <= 0)
                {
                    continue;
                }

                foreach (var target in state.Units.Values)
                {
                    if (target.NationId == nationId || target.Health <= 0 || !visible.Contains(target.Position))
                    {
                        continue;
                    }

                    var wall = attacker.Position.DistanceTo(target.Position) == 1
                        ? _simulation.Walls.FindWallBetween(state, attacker.Position, target.Position)
                        : null;
                    if (!_simulation.Rules.HasAbility(attacker.Type, UnitAbility.IgnoresCityWall) &&
                        wall != null && wall.Health > 0 && state.Cities.TryGetValue(wall.CityId, out var wallCity) &&
                        wallCity.NationId == target.NationId && wallCity.NationId != nationId &&
                        attacker.Position.DistanceTo(target.Position) <= 1)
                    {
                        continue;
                    }

                    var preview = _simulation.Combat.Preview(state, attacker, target);
                    if (preview.Damage <= 0)
                    {
                        continue;
                    }

                    var value = preview.Damage * 8f - preview.CounterDamage * 10f;
                    value += (100f - Math.Min(100f, target.Health)) * 0.20f;
                    value += TargetPriority(target.Type);
                    if (preview.Damage >= target.Health) value += 125f + TargetPriority(target.Type);
                    if (preview.CounterDamage >= attacker.Health) value -= 180f;
                    if (preview.CounterDamage > attacker.Health * 0.45f) value -= 35f;
                    if (preview.AppliesSuppression && !target.IsSuppressed) value += 28f;
                    if (target.IsSuppressed && preview.Damage < target.Health) value -= 8f;
                    if (target.IsGarrisoned) value += 24f;
                    if (IsEnemyCityCenter(state, target.Position, nationId)) value += 55f;
                    value += CountAdjacentAllies(state, target, target.Position) * 10f;
                    var supply = _simulation.Supply.GetStatus(state, attacker);
                    value -= supply.Tier * 12f;
                    if (_simulation.Rules.HasAbility(attacker.Type,
                            UnitAbility.PreservesMovementAfterAttack)) value += attacker.RemainingMovement * 1.5f;

                    if (value > bestValue || (Math.Abs(value - bestValue) < 0.01f &&
                                             (best == null || attacker.Id < best.AttackerId)))
                    {
                        bestValue = value;
                        best = new AttackCommand(nationId, attacker.Id, target.Id);
                    }
                }
            }

            return best;
        }

        private AttackWallCommand FindBestWallAttack(GameState state, int nationId)
        {
            AttackWallCommand best = null;
            var bestValue = 4f;
            var visible = _simulation.Visibility.CalculateVisibleCells(state, nationId);
            foreach (var attacker in state.Units.Values)
            {
                if (attacker.NationId != nationId || attacker.Health <= 0 || attacker.HasAttacked ||
                    attacker.RemainingMovement <= 0)
                {
                    continue;
                }

                foreach (var wall in state.CityWalls.Values)
                {
                    if (wall.Health <= 0 || !visible.Contains(wall.InnerPosition) ||
                        !state.Cities.TryGetValue(wall.CityId, out var city) ||
                        city.NationId == nationId)
                    {
                        continue;
                    }

                    var preview = _simulation.Walls.Preview(state, attacker, wall);
                    if (preview.Damage <= 0) continue;
                    var ratio = wall.MaxHealth <= 0 ? 0f : wall.Health / (float)wall.MaxHealth;
                    var value = preview.Damage * 7f - preview.CounterDamage * 10f;
                    value += preview.GarrisonDamage * 10f;
                    value += (1f - ratio) * 55f;
                    value += CountFriendlyNear(state, nationId, wall.InnerPosition, 3) * 9f;
                    if (preview.Damage >= wall.Health) value += 155f;
                    if (preview.GarrisonUnitId.HasValue) value += 35f;
                    if (_simulation.Rules.Unit(attacker.Type).Branch == UnitBranch.Artillery) value += 20f;
                    if (preview.CounterDamage >= attacker.Health) value -= 180f;
                    if (value <= bestValue) continue;
                    bestValue = value;
                    best = new AttackWallCommand(nationId, attacker.Id, wall.Id);
                }
            }

            return best;
        }

        private HealCommand FindBestHeal(GameState state, int nationId)
        {
            HealCommand best = null;
            var bestValue = 8f;
            foreach (var healer in state.Units.Values)
            {
                if (healer.NationId != nationId || healer.Health <= 0 ||
                    !_simulation.Rules.HasAbility(healer.Type, UnitAbility.Healing)) continue;
                foreach (var target in state.Units.Values)
                {
                    var amount = _simulation.Medical.Preview(state, healer, target);
                    if (amount <= 0) continue;
                    var value = amount * 3f + TargetPriority(target.Type);
                    if (target.Health <= amount) value += 18f;
                    if (value <= bestValue) continue;
                    bestValue = value;
                    best = new HealCommand(nationId, healer.Id, target.Id);
                }
            }
            return best;
        }

        private OccupyCityCommand FindOccupation(GameState state, int nationId)
        {
            foreach (var city in state.Cities.Values)
            {
                if (!city.OccupyingUnitId.HasValue || !state.Units.TryGetValue(city.OccupyingUnitId.Value, out var unit) ||
                    unit.NationId != nationId || !_simulation.Cities.CanOccupy(state, unit, city, out _))
                {
                    continue;
                }
                return new OccupyCityCommand(nationId, unit.Id, city.Id);
            }
            return null;
        }

        private MoveCommand FindBestMove(GameState state, int nationId)
        {
            MoveCommand best = null;
            var bestGain = 0.5f;
            MoveCommand fallback = null;
            var bestFallback = float.NegativeInfinity;
            var bestProgress = 0;
            var fallbackProgress = 0;
            var selectedGain = 0f;
            var fallbackGain = 0f;
            var visible = _simulation.Visibility.CalculateVisibleCells(state, nationId);
            var supplied = _simulation.Supply.CalculateSupplyReach(state, nationId);
            foreach (var unit in state.Units.Values)
            {
                // One compound relocation per unit and turn. This prevents fast front-line units
                // from consuming the global action budget before the rest of the army is considered.
                if (unit.NationId != nationId || unit.RemainingMovement <= 0 || unit.Health <= 0 || unit.HasMoved)
                {
                    continue;
                }

                var currentValue = EvaluatePosition(state, unit, unit.Position, nationId, visible, supplied);
                var currentObjectiveDistance = NearestEnemyCityDistance(state, unit.Position, nationId);
                var reachable = _simulation.Movement.FindReachablePaths(state, unit);
                foreach (var pair in reachable)
                {
                    var destination = pair.Key;
                    var gain = EvaluatePosition(state, unit, destination, nationId, visible, supplied) - currentValue;
                    if (gain > bestGain || (Math.Abs(gain - bestGain) < 0.01f &&
                                           (best == null || unit.Id < best.UnitId)))
                    {
                        bestGain = gain;
                        best = new MoveCommand(nationId, unit.Id, destination);
                        selectedGain = gain;
                        bestProgress = currentObjectiveDistance - NearestEnemyCityDistance(state, destination, nationId);
                    }

                    var progress = currentObjectiveDistance - NearestEnemyCityDistance(state, destination, nationId);
                    if (progress <= 0) continue;
                    var fallbackValue = progress * 42f + gain;
                    if (!supplied.Contains(destination)) fallbackValue -= 12f;
                    if (fallbackValue <= bestFallback) continue;
                    bestFallback = fallbackValue;
                    fallback = new MoveCommand(nationId, unit.Id, destination);
                    fallbackGain = gain;
                    fallbackProgress = progress;
                }
            }

            _lastMoveWasFallback = best == null && fallback != null;
            _lastMoveGain = best != null ? selectedGain : fallbackGain;
            _lastMoveProgress = best != null ? bestProgress : fallbackProgress;
            return best ?? fallback;
        }

        private static int NearestEnemyCityDistance(GameState state, HexCoord position, int nationId)
        {
            var best = int.MaxValue;
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == nationId) continue;
                best = Math.Min(best, position.DistanceTo(city.Center));
            }
            return best == int.MaxValue ? 0 : best;
        }

        private GarrisonCommand FindUsefulGarrison(GameState state, int nationId)
        {
            GarrisonCommand best = null;
            var bestValue = 0f;
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || unit.RemainingMovement <= 0 || unit.HasAttacked || unit.IsGarrisoned ||
                    !_simulation.Control.IsInsideFriendlyCity(state, unit))
                {
                    continue;
                }

                var value = 0f;
                if (HasIntactWallInCoverage(state, unit)) value += 35f;
                value += 12f;
                foreach (var enemy in state.Units.Values)
                {
                    if (enemy.NationId == nationId || enemy.Health <= 0) continue;
                    var distance = enemy.Position.DistanceTo(unit.Position);
                    if (distance <= 5) value += 26f - distance * 3f;
                }
                if (value <= bestValue) continue;
                bestValue = value;
                best = new GarrisonCommand(nationId, unit.Id);
            }

            return best;
        }

        private float EvaluatePosition(GameState state, UnitState unit, HexCoord position, int nationId,
            HashSet<HexCoord> visible, HashSet<HexCoord> supplied)
        {
            var value = 0f;
            var cell = state.Map.Get(position);
            value += (_simulation.Rules.Terrain(cell.Terrain).DefenseMultiplier - 1f) * 30f;
            value += cell.OwnerNationId == nationId ? 3f : cell.OwnerNationId == 0 ? 0f : 8f;
            if (position.Equals(unit.Position) && unit.IsGarrisoned) value += 24f;

            if (supplied.Contains(position)) value += 18f;
            else value -= 42f;

            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == nationId)
                {
                    continue;
                }

                var distance = position.DistanceTo(city.Center);
                value += Math.Max(0f, 92f - distance * 12f);
                if (distance == 0)
                {
                    value += _simulation.Rules.HasAbility(unit.Type, UnitAbility.RapidOccupation) ? 190f : 125f;
                }
                if (city.IsDisabled && distance <= 1) value += 45f;
            }

            foreach (var enemy in state.Units.Values)
            {
                if (enemy.NationId == nationId || enemy.Health <= 0 || !visible.Contains(enemy.Position))
                {
                    continue;
                }

                var distance = position.DistanceTo(enemy.Position);
                var definition = _simulation.Rules.Unit(unit.Type);
                var canAttackNext = !unit.HasAttacked && distance >= definition.MinRange &&
                                    distance <= RuleMath.EffectiveMaxRange(definition.MaxRange, unit.Level) &&
                                    distance <= definition.Vision;
                if (canAttackNext) value += 32f + TargetPriority(enemy.Type) * 0.35f;
                if (_simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery)
                {
                    if (distance >= 2 && distance <= 3) value += 38f;
                    if (distance <= 1) value -= 70f;
                }
                else if (_simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Armor && distance == 1)
                {
                    value += 20f;
                }

                var enemyDefinition = _simulation.Rules.Unit(enemy.Type);
                if (distance >= enemyDefinition.MinRange &&
                    distance <= RuleMath.EffectiveMaxRange(enemyDefinition.MaxRange, enemy.Level))
                {
                    var danger = enemyDefinition.Attack *
                                 (enemyDefinition.Branch == UnitBranch.Artillery ? 1.25f : 0.70f);
                    if (_simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery && distance <= 1)
                        danger *= 1.5f;
                    value -= danger;
                }
            }

            foreach (var ally in state.Units.Values)
            {
                if (ally.Id != unit.Id && ally.NationId == nationId && ally.Health > 0 &&
                    ally.Position.DistanceTo(position) == 1)
                {
                    value += 11f;
                }
            }

            CityWallState destinationWall = null;
            foreach (var wall in state.CityWalls.Values)
            {
                if (wall.Health <= 0 && wall.InnerPosition.Equals(position))
                {
                    destinationWall = wall;
                    break;
                }
            }
            if (destinationWall != null && state.Cities.TryGetValue(destinationWall.CityId, out var breachedCity) &&
                breachedCity.NationId != nationId)
            {
                value += 30f;
            }
            return value;
        }

        private bool HasIntactWallInCoverage(GameState state, UnitState unit)
        {
            foreach (var wall in state.CityWalls.Values)
            {
                if (wall.Health <= 0 || !state.Cities.TryGetValue(wall.CityId, out var city) ||
                    city.NationId != unit.NationId) continue;
                if (unit.Position.DistanceTo(wall.InnerPosition) <= 2) return true;
            }
            return false;
        }

        private static float TargetPriority(UnitType type)
        {
            return type switch
            {
                UnitType.LightArtillery => 34f,
                UnitType.Medic => 30f,
                UnitType.LightArmor => 26f,
                _ => 16f
            };
        }

        private static int CountAdjacentAllies(GameState state, UnitState unit, HexCoord position)
        {
            var count = 0;
            foreach (var other in state.Units.Values)
            {
                if (other.Id == unit.Id || other.NationId != unit.NationId || other.Health <= 0) continue;
                if (other.Position.DistanceTo(position) == 1) count++;
            }
            return count;
        }

        private static int CountFriendlyNear(GameState state, int nationId, HexCoord position, int radius)
        {
            var count = 0;
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId == nationId && unit.Health > 0 && unit.Position.DistanceTo(position) <= radius) count++;
            }
            return count;
        }

        private bool HasSupplyRouteTo(GameState state, int nationId, HexCoord position)
        {
            return _simulation.Supply.GetStatusAt(state, nationId, position).IsCovered;
        }

        private static bool IsEnemyCityCenter(GameState state, HexCoord position, int nationId)
        {
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId != nationId && city.Center.Equals(position))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

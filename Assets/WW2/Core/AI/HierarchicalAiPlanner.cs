using System;
using System.Collections.Generic;
using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;

namespace WW2.Core.AI
{
    public sealed class AiTurnPlanEntry
    {
        public GameCommand Command { get; set; }
        public string DecisionTrace { get; set; } = string.Empty;
    }

    /// <summary>
    /// Active three-layer planner. Strategy chooses one persistent front, the battle layer
    /// compares complete action purposes, and the micro layer executes deterministic combos.
    /// </summary>
    public sealed class AiPlanner
    {
        private enum StrategicMode
        {
            Advance,
            Assault,
            Defend
        }

        private sealed class DecisionOption
        {
            public GameCommand Command;
            public float Score;
            public string BattleIntent = string.Empty;
            public string MicroReason = string.Empty;
            public int? FollowUpUnitId;
            public int? FollowUpTargetId;
            public int? FollowUpWallId;
            public string FollowUpIntent = string.Empty;
            public bool AdvancesProductionCycle;
        }

        private sealed class StaticSequence
        {
            public int Phase;
            public float Score;
            public int? UnitId;
            public bool RequiresFollowUp;
            public readonly List<AiTurnPlanEntry> Entries = new List<AiTurnPlanEntry>();
        }

        private readonly GameSimulation _simulation;
        private readonly IAiEvaluator _evaluator;
        private int _strategyRound = -1;
        private int _strategyNationId = -1;
        private int? _objectiveCityId;
        private HexCoord? _objectivePosition;
        private StrategicMode _strategicMode;
        private string _strategyReason = string.Empty;
        private int _pendingRound = -1;
        private int? _pendingUnitId;
        private int? _pendingTargetId;
        private int? _pendingWallId;
        private string _pendingIntent = string.Empty;
        private int _productionCycleIndex;
        private GameState _decisionState;
        private int _decisionNationId;
        private HashSet<HexCoord> _decisionVisible;
        private static readonly UnitType[] ProductionCycle =
        {
            UnitType.MainInfantry,
            UnitType.LightArtillery,
            UnitType.LightArmor,
            UnitType.Medic
        };

        public AiPlanner(GameSimulation simulation, IAiEvaluator evaluator)
        {
            _simulation = simulation;
            _evaluator = evaluator;
        }

        public string LastDecisionTrace { get; private set; } = string.Empty;

        public AiPlan Plan(GameState state, int nationId)
        {
            var plan = new AiPlan { Score = _evaluator.Evaluate(state, nationId) };
            var command = ChooseNextCommand(state, nationId);
            if (command != null) plan.Commands.Add(command);
            return plan;
        }

        public List<AiTurnPlanEntry> PlanTurnStatic(GameState state, int nationId)
        {
            var result = new List<AiTurnPlanEntry>();
            if (state.ActiveNationId != nationId) return result;
            _decisionState = state;
            _decisionNationId = nationId;
            _decisionVisible = _simulation.Visibility.CalculateVisibleCells(state, nationId);
            EnsureStrategicPlan(state, nationId);

            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || !_simulation.CanPromote(unit)) continue;
                result.Add(new AiTurnPlanEntry
                {
                    Command = new PromoteUnitCommand(nationId, unit.Id),
                    DecisionTrace = FormatTrace("兑现战场成长", $"{unit.Type}#{unit.Id}晋升至L{unit.Level + 1}", 3000f)
                });
            }

            var visible = DecisionVisible(state, nationId);
            var assigned = new HashSet<int>();
            var sequences = new List<StaticSequence>();
            foreach (var city in state.Cities.Values)
            {
                if (!city.OccupyingUnitId.HasValue ||
                    !state.Units.TryGetValue(city.OccupyingUnitId.Value, out var occupant) ||
                    occupant.NationId != nationId || !_simulation.Cities.CanOccupy(state, occupant, city, out _))
                    continue;
                var option = new DecisionOption
                {
                    Command = new OccupyCityCommand(nationId, occupant.Id, city.Id),
                    Score = 2000f,
                    BattleIntent = "兑现战役目标",
                    MicroReason = $"单位#{occupant.Id}占领城市#{city.Id}"
                };
                sequences.Add(StaticSequenceFor(option, 0, occupant.Id));
                assigned.Add(occupant.Id);
            }

            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || unit.Health <= 0 || assigned.Contains(unit.Id)) continue;
                var immediate = FindBestStaticImmediateAction(state, unit, nationId, visible);
                var move = FindBestMoveForUnit(state, unit, nationId, visible);
                var garrison = FindUsefulGarrisonForUnit(state, unit, nationId, visible);
                var option = BestStaticOption(immediate, move, garrison);
                if (option == null) continue;

                var phase = option.Command is AttackCommand || option.Command is AttackWallCommand
                    ? _simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery ? 2 : 3
                    : option.Command is HealCommand ? 1 : option.Command is MoveCommand ? 4 : 5;
                var sequence = StaticSequenceFor(option, phase, unit.Id);
                if (option.Command is MoveCommand && option.FollowUpUnitId.HasValue)
                {
                    GameCommand followUp = option.FollowUpTargetId.HasValue
                        ? new AttackCommand(nationId, unit.Id, option.FollowUpTargetId.Value)
                        : option.FollowUpWallId.HasValue
                            ? new AttackWallCommand(nationId, unit.Id, option.FollowUpWallId.Value)
                            : null;
                    if (followUp != null)
                    {
                        sequence.RequiresFollowUp = true;
                        sequence.Entries.Add(new AiTurnPlanEntry
                        {
                            Command = followUp,
                            DecisionTrace = FormatTrace("完成静态预判的接续攻击",
                                $"{unit.Type}#{unit.Id}在机动后按预案接战", option.Score)
                        });
                    }
                }
                else if (ReferenceEquals(option, immediate) && move != null && move.Score >= 35f &&
                         _simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Armor)
                {
                    // Armor may spend its attack and then execute the independently evaluated move.
                    sequence.Entries.Add(new AiTurnPlanEntry
                    {
                        Command = move.Command,
                        DecisionTrace = FormatTrace(move.BattleIntent, move.MicroReason, move.Score)
                    });
                }
                sequences.Add(sequence);
                if (!ReferenceEquals(option, garrison))
                {
                    var fallbackMove = FindBestMoveForUnit(state, unit, nationId, visible, null, false);
                    if (fallbackMove != null)
                        sequences.Add(StaticSequenceFor(fallbackMove, 4, unit.Id));
                    if (garrison != null) sequences.Add(StaticSequenceFor(garrison, 5, unit.Id));
                }
            }

            var production = FindProduction(state, nationId);
            if (production != null)
            {
                sequences.Add(StaticSequenceFor(production, 6));
                if (production.AdvancesProductionCycle)
                    _productionCycleIndex = (_productionCycleIndex + 1) % ProductionCycle.Length;
            }
            sequences.Sort((left, right) => left.Phase != right.Phase
                ? left.Phase.CompareTo(right.Phase)
                : right.Score.CompareTo(left.Score));
            ReserveStaticPlan(state, sequences, result);
            return result;
        }

        private void ReserveStaticPlan(GameState state, List<StaticSequence> sequences,
            List<AiTurnPlanEntry> result)
        {
            var destinations = new HashSet<HexCoord>();
            var positions = new Dictionary<int, HexCoord>();
            var unitHealth = new Dictionary<int, int>();
            var wallHealth = new Dictionary<int, int>();
            var committedUnits = new HashSet<int>();
            foreach (var unit in state.Units.Values)
            {
                positions[unit.Id] = unit.Position;
                unitHealth[unit.Id] = unit.Health;
            }
            foreach (var wall in state.CityWalls.Values) wallHealth[wall.Id] = wall.Health;

            foreach (var sequence in sequences)
            {
                if (sequence.UnitId.HasValue && committedUnits.Contains(sequence.UnitId.Value)) continue;
                var resultStart = result.Count;
                var acceptedFollowUp = false;
                var savedDestinations = sequence.RequiresFollowUp ? new HashSet<HexCoord>(destinations) : null;
                var savedPositions = sequence.RequiresFollowUp
                    ? new Dictionary<int, HexCoord>(positions)
                    : null;
                var savedUnitHealth = sequence.RequiresFollowUp ? new Dictionary<int, int>(unitHealth) : null;
                var savedWallHealth = sequence.RequiresFollowUp ? new Dictionary<int, int>(wallHealth) : null;
                var movementRejected = false;
                var movementReplaced = false;
                GameCommand replacementFollowUp = null;
                string replacementFollowUpTrace = null;
                foreach (var entry in sequence.Entries)
                {
                    if (entry.Command is MoveCommand move)
                    {
                        if (!unitHealth.TryGetValue(move.UnitId, out var health) || health <= 0)
                        {
                            movementRejected = true;
                            continue;
                        }
                        var moveEntry = entry;
                        if (destinations.Contains(move.Destination))
                        {
                            if (!state.Units.TryGetValue(move.UnitId, out var movingUnit))
                            {
                                movementRejected = true;
                                continue;
                            }
                            var alternative = FindBestMoveForUnit(state, movingUnit, movingUnit.NationId,
                                DecisionVisible(state, movingUnit.NationId), destinations);
                            if (!(alternative?.Command is MoveCommand alternativeMove))
                            {
                                movementRejected = true;
                                continue;
                            }
                            move = alternativeMove;
                            moveEntry = new AiTurnPlanEntry
                            {
                                Command = move,
                                DecisionTrace = FormatTrace(alternative.BattleIntent,
                                    alternative.MicroReason + "；避让已预约落点", alternative.Score)
                            };
                            movementReplaced = true;
                            replacementFollowUp = StaticFollowUpCommand(alternative, movingUnit.NationId,
                                movingUnit.Id);
                            replacementFollowUpTrace = FormatTrace("完成静态预判的接续攻击",
                                $"{movingUnit.Type}#{movingUnit.Id}在避让机动后按预案接战", alternative.Score);
                        }
                        destinations.Add(move.Destination);
                        positions[move.UnitId] = move.Destination;
                        result.Add(moveEntry);
                        continue;
                    }

                    // An attack following a rejected move was calculated from the rejected destination.
                    if (movementRejected &&
                        (entry.Command is AttackCommand || entry.Command is AttackWallCommand)) continue;
                    var reservedEntry = entry;
                    if (movementReplaced &&
                        (entry.Command is AttackCommand || entry.Command is AttackWallCommand))
                    {
                        if (replacementFollowUp == null) continue;
                        reservedEntry = new AiTurnPlanEntry
                        {
                            Command = replacementFollowUp,
                            DecisionTrace = replacementFollowUpTrace
                        };
                        movementReplaced = false;
                    }
                    if (TryReserveStaticCommand(state, reservedEntry.Command, positions, unitHealth, wallHealth))
                    {
                        result.Add(reservedEntry);
                        if (reservedEntry.Command is AttackCommand || reservedEntry.Command is AttackWallCommand)
                            acceptedFollowUp = true;
                    }
                }
                if (sequence.RequiresFollowUp && !acceptedFollowUp)
                {
                    if (result.Count > resultStart) result.RemoveRange(resultStart, result.Count - resultStart);
                    destinations.Clear();
                    foreach (var destination in savedDestinations) destinations.Add(destination);
                    positions.Clear();
                    foreach (var pair in savedPositions) positions[pair.Key] = pair.Value;
                    unitHealth.Clear();
                    foreach (var pair in savedUnitHealth) unitHealth[pair.Key] = pair.Value;
                    wallHealth.Clear();
                    foreach (var pair in savedWallHealth) wallHealth[pair.Key] = pair.Value;
                }
                else if (sequence.UnitId.HasValue && result.Count > resultStart)
                {
                    committedUnits.Add(sequence.UnitId.Value);
                }
            }
        }

        private static GameCommand StaticFollowUpCommand(DecisionOption option, int nationId, int unitId)
        {
            if (option?.FollowUpTargetId.HasValue == true)
                return new AttackCommand(nationId, unitId, option.FollowUpTargetId.Value);
            if (option?.FollowUpWallId.HasValue == true)
                return new AttackWallCommand(nationId, unitId, option.FollowUpWallId.Value);
            return null;
        }

        private bool TryReserveStaticCommand(GameState state, GameCommand command,
            Dictionary<int, HexCoord> positions, Dictionary<int, int> unitHealth,
            Dictionary<int, int> wallHealth)
        {
            switch (command)
            {
                case AttackCommand attack:
                    if (!unitHealth.TryGetValue(attack.AttackerId, out var attackerHealth) || attackerHealth <= 0 ||
                        !unitHealth.TryGetValue(attack.DefenderId, out var defenderHealth) || defenderHealth <= 0 ||
                        !state.Units.TryGetValue(attack.AttackerId, out var attacker) ||
                        !state.Units.TryGetValue(attack.DefenderId, out var defender)) return false;
                    var attackPreview = PreviewAtPredictedState(state, attacker, defender,
                        positions[attacker.Id], positions[defender.Id], attackerHealth, defenderHealth);
                    if (attackPreview.Damage <= 0) return false;
                    unitHealth[defender.Id] = Math.Max(0, defenderHealth - attackPreview.Damage);
                    unitHealth[attacker.Id] = Math.Max(0, attackerHealth - attackPreview.CounterDamage);
                    return true;

                case AttackWallCommand wallAttack:
                    if (!unitHealth.TryGetValue(wallAttack.AttackerId, out var wallAttackerHealth) ||
                        wallAttackerHealth <= 0 || !wallHealth.TryGetValue(wallAttack.WallId, out var remainingWall) ||
                        remainingWall <= 0 || !state.Units.TryGetValue(wallAttack.AttackerId, out var wallAttacker) ||
                        !state.CityWalls.TryGetValue(wallAttack.WallId, out var wall)) return false;
                    var wallPreview = PreviewWallAtPredictedState(state, wallAttacker, wall,
                        positions[wallAttacker.Id], wallAttackerHealth, remainingWall, unitHealth);
                    if (wallPreview.Damage <= 0) return false;
                    wallHealth[wall.Id] = Math.Max(0, remainingWall - wallPreview.Damage);
                    unitHealth[wallAttacker.Id] = Math.Max(0, wallAttackerHealth - wallPreview.CounterDamage);
                    if (wallPreview.GarrisonUnitId.HasValue &&
                        unitHealth.TryGetValue(wallPreview.GarrisonUnitId.Value, out var garrisonHealth))
                        unitHealth[wallPreview.GarrisonUnitId.Value] =
                            Math.Max(0, garrisonHealth - wallPreview.GarrisonDamage);
                    return true;

                case HealCommand heal:
                    if (!unitHealth.TryGetValue(heal.HealerId, out var healerHealth) || healerHealth <= 0 ||
                        !unitHealth.TryGetValue(heal.TargetId, out var healedHealth) || healedHealth <= 0 ||
                        !state.Units.TryGetValue(heal.HealerId, out var healer) ||
                        !state.Units.TryGetValue(heal.TargetId, out var healed)) return false;
                    var healing = PreviewHealAtPredictedState(state, healer, healed, positions[healer.Id],
                        positions[healed.Id], healerHealth, healedHealth);
                    if (healing <= 0) return false;
                    var maximum = RuleMath.Round(_simulation.Rules.Unit(healed.Type).MaxHealth *
                                                 RuleMath.LevelMultiplier(healed.Level));
                    unitHealth[healed.Id] = Math.Min(maximum, healedHealth + healing);
                    return true;
                case GarrisonCommand garrison:
                    return unitHealth.TryGetValue(garrison.UnitId, out var garrisoningHealth) && garrisoningHealth > 0;
                case OccupyCityCommand occupy:
                    return unitHealth.TryGetValue(occupy.UnitId, out var occupyingHealth) && occupyingHealth > 0;
                default:
                    return true;
            }
        }

        private CombatPreview PreviewAtPredictedState(GameState state, UnitState attacker, UnitState defender,
            HexCoord attackerPosition, HexCoord defenderPosition, int attackerHealth, int defenderHealth)
        {
            var oldAttackerPosition = attacker.Position;
            var oldDefenderPosition = defender.Position;
            var oldAttackerHealth = attacker.Health;
            var oldDefenderHealth = defender.Health;
            attacker.Position = attackerPosition;
            defender.Position = defenderPosition;
            attacker.Health = attackerHealth;
            defender.Health = defenderHealth;
            try
            {
                return _simulation.Combat.Preview(state, attacker, defender);
            }
            finally
            {
                attacker.Position = oldAttackerPosition;
                defender.Position = oldDefenderPosition;
                attacker.Health = oldAttackerHealth;
                defender.Health = oldDefenderHealth;
            }
        }

        private WallCombatPreview PreviewWallAtPredictedState(GameState state, UnitState attacker,
            CityWallState wall, HexCoord attackerPosition, int attackerHealth, int remainingWall,
            Dictionary<int, int> unitHealth)
        {
            var oldPosition = attacker.Position;
            var oldAttackerHealth = attacker.Health;
            var oldWallHealth = wall.Health;
            UnitState garrison = null;
            var oldGarrisonHealth = 0;
            foreach (var candidate in state.Units.Values)
            {
                if (!candidate.IsGarrisoned || candidate.Health <= 0 ||
                    !candidate.Position.Equals(wall.InnerPosition) ||
                    !state.Cities.TryGetValue(wall.CityId, out var city) || candidate.NationId != city.NationId)
                    continue;
                garrison = candidate;
                break;
            }
            if (garrison != null)
            {
                oldGarrisonHealth = garrison.Health;
                if (unitHealth.TryGetValue(garrison.Id, out var predictedHealth))
                    garrison.Health = predictedHealth;
            }
            attacker.Position = attackerPosition;
            attacker.Health = attackerHealth;
            wall.Health = remainingWall;
            try
            {
                return _simulation.Walls.Preview(state, attacker, wall);
            }
            finally
            {
                attacker.Position = oldPosition;
                attacker.Health = oldAttackerHealth;
                wall.Health = oldWallHealth;
                if (garrison != null) garrison.Health = oldGarrisonHealth;
            }
        }

        private int PreviewHealAtPredictedState(GameState state, UnitState healer, UnitState target,
            HexCoord healerPosition, HexCoord targetPosition, int healerHealth, int targetHealth)
        {
            var oldHealerPosition = healer.Position;
            var oldTargetPosition = target.Position;
            var oldHealerHealth = healer.Health;
            var oldTargetHealth = target.Health;
            healer.Position = healerPosition;
            target.Position = targetPosition;
            healer.Health = healerHealth;
            target.Health = targetHealth;
            try
            {
                return _simulation.Medical.Preview(state, healer, target);
            }
            finally
            {
                healer.Position = oldHealerPosition;
                target.Position = oldTargetPosition;
                healer.Health = oldHealerHealth;
                target.Health = oldTargetHealth;
            }
        }

        private static DecisionOption BestStaticOption(params DecisionOption[] options)
        {
            DecisionOption best = null;
            foreach (var option in options)
                if (option != null && (best == null || option.Score > best.Score)) best = option;
            return best;
        }

        private StaticSequence StaticSequenceFor(DecisionOption option, int phase, int? unitId = null)
        {
            var sequence = new StaticSequence { Phase = phase, Score = option.Score, UnitId = unitId };
            sequence.Entries.Add(new AiTurnPlanEntry
            {
                Command = option.Command,
                DecisionTrace = FormatTrace(option.BattleIntent, option.MicroReason, option.Score)
            });
            return sequence;
        }

        private string FormatTrace(string battleIntent, string microReason, float score)
        {
            return $"战略[{StrategyLabel(_decisionState)}]｜战役[{battleIntent}]｜" +
                   $"微操[{microReason}]｜价值={score:0.0}";
        }

        public GameCommand ChooseNextCommand(GameState state, int nationId)
        {
            if (state.ActiveNationId != nationId) return null;
            _decisionState = state;
            _decisionNationId = nationId;
            _decisionVisible = _simulation.Visibility.CalculateVisibleCells(state, nationId);
            EnsureStrategicPlan(state, nationId);

            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != nationId || !_simulation.CanPromote(unit)) continue;
                var promotion = new DecisionOption
                {
                    Command = new PromoteUnitCommand(nationId, unit.Id),
                    Score = 3000f,
                    BattleIntent = "兑现战场成长",
                    MicroReason = $"{unit.Type}#{unit.Id}晋升至L{unit.Level + 1}"
                };
                PublishTrace(state, promotion);
                return promotion.Command;
            }

            var pending = TryPendingFollowUp(state, nationId);
            if (pending != null)
            {
                PublishTrace(state, pending);
                return pending.Command;
            }

            var options = new List<DecisionOption>();
            AddOption(options, FindOccupation(state, nationId));
            AddOption(options, FindBestAttack(state, nationId));
            AddOption(options, FindBestWallAttack(state, nationId));
            AddOption(options, FindBestHeal(state, nationId));
            AddOption(options, FindBestMove(state, nationId));
            AddOption(options, FindUsefulGarrison(state, nationId));
            AddOption(options, FindProduction(state, nationId));

            DecisionOption best = null;
            foreach (var option in options)
            {
                if (best == null || option.Score > best.Score) best = option;
            }

            if (best == null || best.Score < 12f)
            {
                LastDecisionTrace = $"战略[{StrategyLabel(state)}]｜战役[保持态势]｜" +
                                    "微操[没有收益足以覆盖风险的合法行动]";
                return null;
            }

            ArmFollowUp(state, best);
            if (best.AdvancesProductionCycle)
                _productionCycleIndex = (_productionCycleIndex + 1) % ProductionCycle.Length;
            PublishTrace(state, best);
            return best.Command;
        }

        private static void AddOption(List<DecisionOption> options, DecisionOption option)
        {
            if (option?.Command != null) options.Add(option);
        }

        private void PublishTrace(GameState state, DecisionOption option)
        {
            LastDecisionTrace = $"战略[{StrategyLabel(state)}]｜战役[{option.BattleIntent}]｜" +
                                $"微操[{option.MicroReason}]｜价值={option.Score:0.0}";
        }

        private string StrategyLabel(GameState state)
        {
            var mode = _strategicMode == StrategicMode.Defend ? "防御" :
                _strategicMode == StrategicMode.Assault ? "攻坚" : "推进";
            if (!_objectiveCityId.HasValue || !state.Cities.TryGetValue(_objectiveCityId.Value, out var city))
                return $"{mode}；无城市目标";
            return $"{mode}城市#{city.Id}@{city.Center}；{_strategyReason}";
        }

        private void EnsureStrategicPlan(GameState state, int nationId)
        {
            var objectiveValid = _objectiveCityId.HasValue && state.Cities.TryGetValue(_objectiveCityId.Value, out var oldCity) &&
                                 (_strategicMode == StrategicMode.Defend
                                     ? oldCity.NationId == nationId
                                     : oldCity.NationId != nationId);
            if (_strategyRound == state.Round && _strategyNationId == nationId && objectiveValid) return;

            _strategyRound = state.Round;
            _strategyNationId = nationId;
            ClearPending();

            CityState threatened = null;
            var greatestThreat = 0f;
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId != nationId) continue;
                var threat = 0f;
                var defenders = 0;
                foreach (var unit in state.Units.Values)
                {
                    if (unit.Health <= 0) continue;
                    var distance = unit.Position.DistanceTo(city.Center);
                    if (unit.NationId == nationId)
                    {
                        if (distance <= 2) defenders++;
                        continue;
                    }
                    if (distance > 5) continue;
                    threat += (6 - distance) * (6f + _simulation.Rules.Unit(unit.Type).Attack);
                }
                if (city.IsDisabled) threat += 120f;
                threat -= defenders * 18f;
                if (threat <= greatestThreat) continue;
                greatestThreat = threat;
                threatened = city;
            }

            if (threatened != null && greatestThreat >= 55f)
            {
                _objectiveCityId = threatened.Id;
                _objectivePosition = threatened.Center;
                _strategicMode = StrategicMode.Defend;
                _strategyReason = $"敌军近城威胁{greatestThreat:0}，优先保住补给源";
                return;
            }

            CityState target = null;
            var bestScore = float.NegativeInfinity;
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == nationId) continue;
                var score = city.IsDisabled ? 150f : 0f;
                var nearby = 0;
                foreach (var unit in state.Units.Values)
                {
                    if (unit.NationId != nationId || unit.Health <= 0) continue;
                    var distance = unit.Position.DistanceTo(city.Center);
                    score += Math.Max(0f, 14f - distance) * 3f;
                    if (distance <= 4) nearby++;
                }
                var breached = CountBreachedWalls(state, city.Id);
                score += breached * 28f + nearby * 9f - city.Level * 5f;
                if (score <= bestScore) continue;
                bestScore = score;
                target = city;
            }

            _objectiveCityId = target?.Id;
            _objectivePosition = target?.Center;
            if (target == null)
            {
                _strategicMode = StrategicMode.Advance;
                _strategyReason = "当前没有可争夺的敌方城市";
                return;
            }

            var friendlyNear = CountFriendlyNear(state, nationId, target.Center, 4);
            var breaches = CountBreachedWalls(state, target.Id);
            _strategicMode = target.IsDisabled || breaches > 0 || friendlyNear >= 4
                ? StrategicMode.Assault
                : StrategicMode.Advance;
            _strategyReason = _strategicMode == StrategicMode.Assault
                ? $"已有{friendlyNear}支部队接敌、{breaches}处突破口"
                : $"全军选择距离与兵力最有利的单一战线";
        }

        private DecisionOption FindOccupation(GameState state, int nationId)
        {
            foreach (var city in state.Cities.Values)
            {
                if (!city.OccupyingUnitId.HasValue || !state.Units.TryGetValue(city.OccupyingUnitId.Value, out var unit) ||
                    unit.NationId != nationId || !_simulation.Cities.CanOccupy(state, unit, city, out _)) continue;
                return new DecisionOption
                {
                    Command = new OccupyCityCommand(nationId, unit.Id, city.Id),
                    Score = 2000f,
                    BattleIntent = "兑现战役目标",
                    MicroReason = $"单位#{unit.Id}立即占领城市#{city.Id}，切换城市与补给控制权"
                };
            }
            return null;
        }

        private DecisionOption FindBestAttack(GameState state, int nationId)
        {
            DecisionOption best = null;
            var visible = DecisionVisible(state, nationId);
            foreach (var attacker in state.Units.Values)
            {
                if (attacker.NationId != nationId || attacker.HasAttacked || attacker.Health <= 0 ||
                    !attacker.CanAttackThisTurn) continue;
                foreach (var target in state.Units.Values)
                {
                    if (!CanAttackTarget(state, attacker, target, nationId, visible, out var preview)) continue;
                    var score = AttackUtility(state, attacker, target, preview);
                    var remaining = Math.Max(0, target.Health - preview.Damage);
                    var followUp = remaining > 0
                        ? FindBestFollower(state, nationId, attacker.Id, target, remaining,
                            preview.AppliesSuppression, visible)
                        : null;
                    if (followUp != null)
                    {
                        score += Math.Max(0f, followUp.Score) * 0.42f;
                        if (followUp.Score >= 100f) score += 28f;
                    }
                    if (best != null && score <= best.Score) continue;

                    var intent = preview.Damage >= target.Health ? "集中补刀" :
                        preview.AppliesSuppression && followUp != null ? "炮火压制后接续突击" :
                        followUp != null ? "集中火力削减关键目标" : "有利交换";
                    best = new DecisionOption
                    {
                        Command = new AttackCommand(nationId, attacker.Id, target.Id),
                        Score = score,
                        BattleIntent = intent,
                        MicroReason = $"{attacker.Type}#{attacker.Id}攻击{target.Type}#{target.Id}，" +
                                      $"伤害{preview.Damage}/反击{preview.CounterDamage}" +
                                      (preview.AppliesSuppression ? "并施加压制" : string.Empty),
                        FollowUpUnitId = followUp?.FollowUpUnitId,
                        FollowUpTargetId = followUp?.FollowUpTargetId,
                        FollowUpIntent = followUp == null ? string.Empty : "执行已预判的接续攻击"
                    };
                }
            }
            return best;
        }

        private DecisionOption FindBestStaticImmediateAction(GameState state, UnitState unit, int nationId,
            HashSet<HexCoord> visible)
        {
            DecisionOption best = null;
            if (unit.CanAttackThisTurn)
            {
                foreach (var target in state.Units.Values)
                {
                    if (!CanAttackTarget(state, unit, target, nationId, visible, out var preview)) continue;
                    var score = AttackUtility(state, unit, target, preview);
                    var option = new DecisionOption
                    {
                        Command = new AttackCommand(nationId, unit.Id, target.Id),
                        Score = score,
                        BattleIntent = preview.Damage >= target.Health ? "集中补刀" :
                            preview.AppliesSuppression ? "炮火压制" : "有利交换",
                        MicroReason = $"{unit.Type}#{unit.Id}攻击{target.Type}#{target.Id}，" +
                                      $"伤害{preview.Damage}/反击{preview.CounterDamage}"
                    };
                    if (best == null || option.Score > best.Score) best = option;
                }

                foreach (var wall in state.CityWalls.Values)
                {
                    if (wall.Health <= 0 || !visible.Contains(wall.InnerPosition) ||
                        !state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId == nationId) continue;
                    var preview = _simulation.Walls.Preview(state, unit, wall);
                    if (preview.Damage <= 0) continue;
                    var score = preview.Damage * 9f + preview.GarrisonDamage * 12f - preview.CounterDamage * 12f;
                    if (preview.Damage >= wall.Health) score += 190f;
                    if (preview.GarrisonUnitId.HasValue) score += 38f;
                    if (_objectiveCityId == wall.CityId) score += 70f;
                    if (_simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery) score += 32f;
                    if (_simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing))
                    {
                        if (preview.Damage < wall.Health || _objectiveCityId != wall.CityId) continue;
                        score -= 185f;
                    }
                    if (preview.CounterDamage >= unit.Health) score -= 250f;
                    var option = new DecisionOption
                    {
                        Command = new AttackWallCommand(nationId, unit.Id, wall.Id),
                        Score = score,
                        BattleIntent = preview.Damage >= wall.Health ? "打开城市突破口" : "削弱目标城市边防",
                        MicroReason = $"{unit.Type}#{unit.Id}攻击墙#{wall.Id}，" +
                                      $"墙伤{preview.Damage}/驻军伤{preview.GarrisonDamage}/反击{preview.CounterDamage}"
                    };
                    if (best == null || option.Score > best.Score) best = option;
                }
            }

            if (_simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing) &&
                unit.RemainingMovement > 0 && !unit.HasAttacked)
            {
                foreach (var target in state.Units.Values)
                {
                    var amount = _simulation.Medical.Preview(state, unit, target);
                    if (amount <= 0) continue;
                    var incoming = EstimateIncomingDamage(state, target, target.Position, nationId, visible);
                    var score = amount * 12f + TargetPriority(target.Type) * 0.55f;
                    if (incoming >= target.Health) score += 45f;
                    if (_simulation.Rules.Unit(target.Type).Branch == UnitBranch.Armor) score += 18f;
                    var option = new DecisionOption
                    {
                        Command = new HealCommand(nationId, unit.Id, target.Id),
                        Score = score,
                        BattleIntent = incoming >= target.Health ? "抢救核心单位" : "恢复高价值战斗力",
                        MicroReason = $"医疗兵#{unit.Id}为{target.Type}#{target.Id}恢复{amount}"
                    };
                    if (best == null || option.Score > best.Score) best = option;
                }
            }
            return best != null && best.Score >= 12f ? best : null;
        }

        private float AttackUtility(GameState state, UnitState attacker, UnitState target, CombatPreview preview)
        {
            var score = preview.Damage * 10f - preview.CounterDamage * 12f + TargetPriority(target.Type);
            var kill = preview.Damage >= target.Health;
            if (kill) score += 145f + TargetPriority(target.Type);
            if (preview.CounterDamage >= attacker.Health) score -= 260f;
            else if (preview.CounterDamage > attacker.Health * 0.45f) score -= 55f;
            if (preview.AppliesSuppression && !target.IsSuppressed) score += 34f;
            if (target.IsSuppressed && !kill) score += 14f;
            if (target.IsGarrisoned) score += 24f;
            if (IsAtStrategicObjective(target.Position)) score += 30f;

            var branch = _simulation.Rules.Unit(attacker.Type).Branch;
            if (branch == UnitBranch.Infantry && !kill && preview.CounterDamage >= preview.Damage)
                score -= 62f;
            if (branch == UnitBranch.Armor && kill) score += attacker.RemainingMovement * 2f;
            var supply = _simulation.Supply.GetStatus(state, attacker);
            score -= supply.Tier * 18f;
            return score;
        }

        private DecisionOption FindBestFollower(GameState state, int nationId, int excludedId, UnitState target,
            int remainingHealth, bool suppressed, HashSet<HexCoord> visible)
        {
            var originalHealth = target.Health;
            var originalSuppression = target.IsSuppressed;
            var originalGarrisoned = target.IsGarrisoned;
            target.Health = remainingHealth;
            if (suppressed)
            {
                target.IsSuppressed = true;
                target.IsGarrisoned = false;
            }
            try
            {
                DecisionOption best = null;
                foreach (var ally in state.Units.Values)
                {
                    if (ally.Id == excludedId || ally.NationId != nationId || ally.HasAttacked ||
                        !ally.CanAttackThisTurn || ally.Health <= 0) continue;
                    if (!CanAttackTarget(state, ally, target, nationId, visible, out var preview)) continue;
                    var score = AttackUtility(state, ally, target, preview);
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption
                    {
                        Score = score,
                        FollowUpUnitId = ally.Id,
                        FollowUpTargetId = target.Id
                    };
                }
                return best != null && best.Score >= 18f ? best : null;
            }
            finally
            {
                target.Health = originalHealth;
                target.IsSuppressed = originalSuppression;
                target.IsGarrisoned = originalGarrisoned;
            }
        }

        private DecisionOption FindBestWallAttack(GameState state, int nationId)
        {
            DecisionOption best = null;
            var visible = DecisionVisible(state, nationId);
            foreach (var attacker in state.Units.Values)
            {
                if (attacker.NationId != nationId || attacker.Health <= 0 || attacker.HasAttacked ||
                    !attacker.CanAttackThisTurn) continue;
                foreach (var wall in state.CityWalls.Values)
                {
                    if (wall.Health <= 0 || !visible.Contains(wall.InnerPosition) ||
                        !state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId == nationId) continue;
                    var preview = _simulation.Walls.Preview(state, attacker, wall);
                    if (preview.Damage <= 0) continue;
                    var score = preview.Damage * 9f + preview.GarrisonDamage * 12f - preview.CounterDamage * 12f;
                    if (preview.Damage >= wall.Health) score += 190f;
                    if (preview.GarrisonUnitId.HasValue) score += 38f;
                    if (_objectiveCityId == wall.CityId) score += 70f;
                    var branch = _simulation.Rules.Unit(attacker.Type).Branch;
                    if (branch == UnitBranch.Artillery) score += 32f;
                    if (_simulation.Rules.HasAbility(attacker.Type, UnitAbility.Healing))
                    {
                        // A medic only joins a breach when it can finish the selected city's wall.
                        // This preserves its action for healing instead of treating it as cheap infantry.
                        if (preview.Damage < wall.Health || _objectiveCityId != wall.CityId) continue;
                        score -= 185f;
                    }
                    if (preview.CounterDamage >= attacker.Health) score -= 250f;
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption
                    {
                        Command = new AttackWallCommand(nationId, attacker.Id, wall.Id),
                        Score = score,
                        BattleIntent = preview.Damage >= wall.Health ? "打开城市突破口" : "持续削弱目标城市边防",
                        MicroReason = $"{attacker.Type}#{attacker.Id}攻击墙#{wall.Id}，" +
                                      $"墙伤{preview.Damage}/驻军伤{preview.GarrisonDamage}/反击{preview.CounterDamage}"
                    };
                }
            }
            return best;
        }

        private DecisionOption FindBestHeal(GameState state, int nationId)
        {
            DecisionOption best = null;
            var visible = DecisionVisible(state, nationId);
            foreach (var healer in state.Units.Values)
            {
                if (healer.NationId != nationId || healer.Health <= 0 || healer.HasAttacked ||
                    healer.RemainingMovement <= 0 || !_simulation.Rules.HasAbility(healer.Type, UnitAbility.Healing))
                    continue;
                foreach (var target in state.Units.Values)
                {
                    var amount = _simulation.Medical.Preview(state, healer, target);
                    if (amount <= 0) continue;
                    var incoming = EstimateIncomingDamage(state, target, target.Position, nationId, visible);
                    var score = amount * 12f + TargetPriority(target.Type) * 0.55f;
                    if (amount == 1) score -= 22f;
                    if (incoming >= target.Health) score += 45f;
                    if (_simulation.Rules.Unit(target.Type).Branch == UnitBranch.Armor) score += 18f;
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption
                    {
                        Command = new HealCommand(nationId, healer.Id, target.Id),
                        Score = score,
                        BattleIntent = incoming >= target.Health ? "抢救即将被歼灭的核心单位" : "恢复高价值战斗力",
                        MicroReason = $"医疗兵#{healer.Id}为{target.Type}#{target.Id}恢复{amount}，预计来袭{incoming}"
                    };
                }
            }
            return best;
        }

        private DecisionOption FindBestMove(GameState state, int nationId)
        {
            DecisionOption best = null;
            var visible = DecisionVisible(state, nationId);
            foreach (var unit in state.Units.Values)
            {
                var option = FindBestMoveForUnit(state, unit, nationId, visible);
                if (option != null && (best == null || option.Score > best.Score)) best = option;
            }
            return best;
        }

        private DecisionOption FindBestMoveForUnit(GameState state, UnitState unit, int nationId,
            HashSet<HexCoord> visible, HashSet<HexCoord> excludedDestinations = null, bool allowFollowUp = true)
        {
            if (unit.NationId != nationId || unit.RemainingMovement <= 0 || unit.Health <= 0 || unit.HasMoved)
                return null;
            if (unit.IsGarrisoned && _strategicMode == StrategicMode.Defend && IsInsideObjectiveCity(state, unit))
                return null;

            DecisionOption best = null;
            var currentValue = EvaluatePosition(state, unit, unit.Position, nationId, visible);
            var currentDistance = ObjectiveDistance(state, unit.Position);
            var currentSupply = _simulation.Supply.GetStatusAt(state, nationId, unit.Position);
            foreach (var pair in _simulation.Movement.FindReachablePaths(state, unit))
            {
                var destination = pair.Key;
                if (excludedDestinations != null && excludedDestinations.Contains(destination)) continue;
                var destinationValue = EvaluatePosition(state, unit, destination, nationId, visible);
                var progress = currentDistance - ObjectiveDistance(state, destination);
                var gain = destinationValue - currentValue;
                var entersEnemyControl = _simulation.Control.HasEnemyControl(state, destination, nationId);
                var remainingAfterCost = Math.Max(0, unit.RemainingMovement - pair.Value.Cost);
                var postMoveAp = entersEnemyControl ? 0 : remainingAfterCost;
                var canAttackAfterMove = remainingAfterCost > 0 && !unit.HasAttacked;
                var immediate = allowFollowUp && canAttackAfterMove
                    ? BestImmediateAttackFrom(state, unit, destination, nationId, visible)
                    : null;
                var immediateWall = allowFollowUp && canAttackAfterMove
                    ? BestImmediateWallAttackFrom(state, unit, destination, nationId, visible)
                    : null;
                var followUp = immediateWall != null && (immediate == null || immediateWall.Score > immediate.Score)
                    ? immediateWall
                    : immediate;

                var destinationSupply = _simulation.Supply.GetStatusAt(state, nationId, destination);
                var improvesSupply = destinationSupply.Tier < currentSupply.Tier;
                var purposefulRetreat = _strategicMode == StrategicMode.Defend && progress > 0;
                if (followUp == null && !improvesSupply && !purposefulRetreat)
                {
                    if (progress < 0 && gain < 48f) continue;
                    if (progress == 0 && gain < 30f) continue;
                }

                var score = 28f + gain + progress * 11f;
                if (followUp != null) score += Math.Max(0f, followUp.Score) * 0.48f;
                if (entersEnemyControl && !IsEnemyObjectiveCenter(state, destination, nationId)) score -= 88f;
                if (postMoveAp == 0 && followUp == null &&
                    _simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery) score -= 35f;
                if (followUp == null && score < 35f) continue;
                if (best != null && score <= best.Score) continue;

                var role = _simulation.Rules.Unit(unit.Type).Branch;
                var intent = followUp != null ? "机动后立即接战" :
                    _strategicMode == StrategicMode.Defend ? "向受威胁城市收缩" :
                    role == UnitBranch.Artillery ? "建立二至三格火力阵位" :
                    role == UnitBranch.Armor ? "寻找低风险突破位置" : "推进并保持战线连续";
                best = new DecisionOption
                {
                    Command = new MoveCommand(nationId, unit.Id, destination),
                    Score = score,
                    BattleIntent = intent,
                    MicroReason = $"{unit.Type}#{unit.Id}由{unit.Position}至{destination}，" +
                                  $"目标推进{progress}、位置收益{gain:0.0}、移动后AP{postMoveAp}",
                    FollowUpUnitId = followUp == null ? null : unit.Id,
                    FollowUpTargetId = followUp?.FollowUpTargetId,
                    FollowUpWallId = followUp?.FollowUpWallId,
                    FollowUpIntent = followUp == null ? string.Empty : "完成机动预设的即时攻击"
                };
            }
            return best;
        }

        private DecisionOption BestImmediateAttackFrom(GameState state, UnitState unit, HexCoord destination,
            int nationId, HashSet<HexCoord> visible)
        {
            var original = unit.Position;
            unit.Position = destination;
            try
            {
                DecisionOption best = null;
                foreach (var target in state.Units.Values)
                {
                    if (!CanAttackTarget(state, unit, target, nationId, visible, out var preview)) continue;
                    var score = AttackUtility(state, unit, target, preview);
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption { Score = score, FollowUpTargetId = target.Id };
                }
                return best != null && best.Score >= 18f ? best : null;
            }
            finally
            {
                unit.Position = original;
            }
        }

        private DecisionOption BestImmediateWallAttackFrom(GameState state, UnitState unit, HexCoord destination,
            int nationId, HashSet<HexCoord> visible)
        {
            var original = unit.Position;
            unit.Position = destination;
            try
            {
                DecisionOption best = null;
                foreach (var wall in state.CityWalls.Values)
                {
                    if (wall.Health <= 0 || !visible.Contains(wall.InnerPosition) ||
                        !state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId == nationId) continue;
                    var preview = _simulation.Walls.Preview(state, unit, wall);
                    if (preview.Damage <= 0) continue;
                    var score = preview.Damage * 9f + preview.GarrisonDamage * 12f - preview.CounterDamage * 12f;
                    if (preview.Damage >= wall.Health) score += 190f;
                    if (preview.GarrisonUnitId.HasValue) score += 38f;
                    if (_objectiveCityId == wall.CityId) score += 70f;
                    if (_simulation.Rules.Unit(unit.Type).Branch == UnitBranch.Artillery) score += 32f;
                    if (_simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing))
                    {
                        if (preview.Damage < wall.Health || _objectiveCityId != wall.CityId) continue;
                        score -= 185f;
                    }
                    if (preview.CounterDamage >= unit.Health) score -= 250f;
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption { Score = score, FollowUpWallId = wall.Id };
                }
                return best != null && best.Score >= 18f ? best : null;
            }
            finally
            {
                unit.Position = original;
            }
        }

        private float EvaluatePosition(GameState state, UnitState unit, HexCoord position, int nationId,
            HashSet<HexCoord> visible)
        {
            var value = 0f;
            var cell = state.Map.Get(position);
            value += (_simulation.Rules.Terrain(cell.Terrain).DefenseMultiplier - 1f) * 36f;
            value += cell.OwnerNationId == nationId ? 5f : cell.OwnerNationId == 0 ? 0f : 10f;

            var supply = _simulation.Supply.GetStatusAt(state, nationId, position);
            value += supply.Tier == 0 ? 28f : supply.Tier == 1 ? 5f : supply.Tier == 2 ? -42f : -105f;

            var objectiveDistance = ObjectiveDistance(state, position);
            if (_objectiveCityId.HasValue && state.Cities.TryGetValue(_objectiveCityId.Value, out var objective))
            {
                value += _strategicMode == StrategicMode.Defend
                    ? Math.Max(0f, 135f - objectiveDistance * 23f)
                    : Math.Max(0f, 118f - objectiveDistance * 14f);
                if (objectiveDistance == 0)
                {
                    value += _strategicMode == StrategicMode.Defend ? 85f :
                        _simulation.Rules.HasAbility(unit.Type, UnitAbility.RapidOccupation) ? 220f : 145f;
                }
                if (objective.IsDisabled && objectiveDistance <= 1 && objective.NationId != nationId) value += 55f;
            }

            var nearestEnemy = int.MaxValue;
            foreach (var enemy in state.Units.Values)
            {
                if (enemy.NationId == nationId || enemy.Health <= 0 || !visible.Contains(enemy.Position)) continue;
                var distance = position.DistanceTo(enemy.Position);
                nearestEnemy = Math.Min(nearestEnemy, distance);
                var definition = _simulation.Rules.Unit(unit.Type);
                if (!unit.HasAttacked && distance >= definition.MinRange &&
                    distance <= RuleMath.EffectiveMaxRange(definition.MaxRange, unit.Level) &&
                    distance <= definition.Vision) value += 28f + TargetPriority(enemy.Type) * 0.30f;
            }

            var branch = _simulation.Rules.Unit(unit.Type).Branch;
            if (branch == UnitBranch.Artillery)
            {
                if (nearestEnemy >= 2 && nearestEnemy <= 3) value += 52f;
                if (nearestEnemy <= 1) value -= 105f;
            }
            else if (branch == UnitBranch.Armor && nearestEnemy == 1)
            {
                value += 18f;
            }

            var adjacentAllies = 0;
            var injuredAllies = 0;
            foreach (var ally in state.Units.Values)
            {
                if (ally.Id == unit.Id || ally.NationId != nationId || ally.Health <= 0 ||
                    ally.Position.DistanceTo(position) != 1) continue;
                adjacentAllies++;
                var maximum = _simulation.Rules.Unit(ally.Type).MaxHealth;
                if (ally.Health < maximum) injuredAllies++;
            }
            value += adjacentAllies * 10f;
            if (_simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing)) value += injuredAllies * 18f;

            var incoming = EstimateIncomingDamage(state, unit, position, nationId, visible);
            value -= incoming * (branch == UnitBranch.Artillery ||
                                 _simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing) ? 10f : 7f);
            if (incoming >= unit.Health) value -= 165f;

            if (position.Equals(unit.Position) && unit.IsGarrisoned)
                value += _strategicMode == StrategicMode.Defend && IsInsideObjectiveCity(state, unit) ? 115f : 38f;
            foreach (var wall in state.CityWalls.Values)
            {
                if (wall.Health <= 0 && wall.InnerPosition.Equals(position) &&
                    state.Cities.TryGetValue(wall.CityId, out var city) && city.NationId != nationId)
                    value += 42f;
            }
            return value;
        }

        private int EstimateIncomingDamage(GameState state, UnitState unit, HexCoord position, int nationId,
            HashSet<HexCoord> visible)
        {
            var original = unit.Position;
            unit.Position = position;
            var first = 0;
            var second = 0;
            try
            {
                foreach (var enemy in state.Units.Values)
                {
                    if (enemy.NationId == nationId || enemy.Health <= 0 || !visible.Contains(enemy.Position)) continue;
                    var preview = _simulation.Combat.Preview(state, enemy, unit);
                    var damage = preview.Damage;
                    if (damage > first)
                    {
                        second = first;
                        first = damage;
                    }
                    else if (damage > second)
                    {
                        second = damage;
                    }
                }
            }
            finally
            {
                unit.Position = original;
            }
            return first + second;
        }

        private DecisionOption FindUsefulGarrison(GameState state, int nationId)
        {
            DecisionOption best = null;
            var visible = DecisionVisible(state, nationId);
            foreach (var unit in state.Units.Values)
            {
                var option = FindUsefulGarrisonForUnit(state, unit, nationId, visible);
                if (option != null && (best == null || option.Score > best.Score)) best = option;
            }
            return best;
        }

        private DecisionOption FindUsefulGarrisonForUnit(GameState state, UnitState unit, int nationId,
            HashSet<HexCoord> visible)
        {
            if (unit.NationId != nationId || !_simulation.CanGarrison(state, unit)) return null;
            var city = CityContaining(state, unit.Position, nationId);
            var wall = _simulation.Walls.FindWallAt(state, unit.Position);
            var nearestEnemy = int.MaxValue;
            foreach (var enemy in state.Units.Values)
            {
                if (enemy.NationId == nationId || enemy.Health <= 0) continue;
                nearestEnemy = Math.Min(nearestEnemy, enemy.Position.DistanceTo(unit.Position));
            }
            var incoming = EstimateIncomingDamage(state, unit, unit.Position, nationId, visible);
            var defendsObjective = city != null && _strategicMode == StrategicMode.Defend &&
                                   _objectiveCityId == city.Id;
            var wallPost = wall != null && wall.Health > 0 && city != null;
            if (incoming <= 0 && !defendsObjective && !wallPost) return null;
            var score = 24f + incoming * 10f;
            if (wallPost) score += 52f;
            if (defendsObjective) score += 120f;
            if (nearestEnemy <= 5) score += (6 - nearestEnemy) * 18f;
            if (wallPost && _simulation.Rules.HasAbility(unit.Type, UnitAbility.GarrisonExpert)) score += 36f;
            if (!defendsObjective && incoming < unit.Health)
            {
                var branch = _simulation.Rules.Unit(unit.Type).Branch;
                if (branch == UnitBranch.Armor) score -= 82f;
                if (branch == UnitBranch.Artillery) score -= 68f;
                if (_simulation.Rules.HasAbility(unit.Type, UnitAbility.Healing)) score -= 48f;
            }
            if (score < 12f) return null;
            return new DecisionOption
            {
                Command = new GarrisonCommand(nationId, unit.Id),
                Score = score,
                BattleIntent = wallPost ? "建立城墙防御支点" : "建立野战防御阵地",
                MicroReason = $"{unit.Type}#{unit.Id}就地驻扎，防御×" +
                              $"{(wallPost && unit.Type == UnitType.MainInfantry ? 2.5f : 1.5f):0.0}，" +
                              $"预计承伤{incoming}、最近敌军{nearestEnemy}格"
            };
        }

        private DecisionOption FindProduction(GameState state, int nationId)
        {
            var type = ProductionCycle[_productionCycleIndex];
            var branch = _simulation.Rules.Unit(type).Branch;
            DecisionOption best = null;
            if (branch == UnitBranch.Infantry)
            {
                foreach (var city in state.Cities.Values)
                {
                    if (!_simulation.Production.CanRecruit(state, nationId, city.Id, type, out var deployment,
                            out _)) continue;
                    var score = 42f - ObjectiveDistance(state, deployment) * 0.6f;
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption
                    {
                        Command = new RecruitUnitCommand(nationId, city.Id, type),
                        Score = score,
                        BattleIntent = "补充战役预备队",
                        MicroReason = $"城市#{city.Id}即时征募{type}并部署于{deployment}",
                        AdvancesProductionCycle = true
                    };
                }
            }
            else
            {
                foreach (var building in state.Buildings.Values)
                {
                    if (building.Type != BuildingType.MilitaryFactory ||
                        !_simulation.Production.CanManufacture(state, nationId, building.Id, type,
                            out var deployment, out _)) continue;
                    var score = 42f - ObjectiveDistance(state, deployment) * 0.6f;
                    if (best != null && score <= best.Score) continue;
                    best = new DecisionOption
                    {
                        Command = new ManufactureUnitCommand(nationId, building.Id, type),
                        Score = score,
                        BattleIntent = "补充战役预备队",
                        MicroReason = $"工厂#{building.Id}即时制造{type}并部署于{deployment}",
                        AdvancesProductionCycle = true
                    };
                }
            }
            return best;
        }

        private bool CanAttackTarget(GameState state, UnitState attacker, UnitState target, int nationId,
            HashSet<HexCoord> visible, out CombatPreview preview)
        {
            preview = null;
            if (target == null || target.NationId == nationId || target.Health <= 0 ||
                !visible.Contains(target.Position)) return false;
            var wall = _simulation.Walls.FindWallAt(state, target.Position);
            if (wall != null && wall.Health > 0 && state.Cities.TryGetValue(wall.CityId, out var wallCity) &&
                wallCity.NationId == target.NationId && wallCity.NationId != nationId) return false;
            preview = _simulation.Combat.Preview(state, attacker, target);
            return preview.Damage > 0;
        }

        private void ArmFollowUp(GameState state, DecisionOption option)
        {
            ClearPending();
            if (!option.FollowUpUnitId.HasValue ||
                (!option.FollowUpTargetId.HasValue && !option.FollowUpWallId.HasValue)) return;
            _pendingRound = state.Round;
            _pendingUnitId = option.FollowUpUnitId;
            _pendingTargetId = option.FollowUpTargetId;
            _pendingWallId = option.FollowUpWallId;
            _pendingIntent = option.FollowUpIntent;
        }

        private DecisionOption TryPendingFollowUp(GameState state, int nationId)
        {
            if (_pendingRound != state.Round || !_pendingUnitId.HasValue ||
                (!_pendingTargetId.HasValue && !_pendingWallId.HasValue))
            {
                ClearPending();
                return null;
            }
            var unitId = _pendingUnitId.Value;
            var targetId = _pendingTargetId;
            var wallId = _pendingWallId;
            var intent = _pendingIntent;
            ClearPending();
            if (!state.Units.TryGetValue(unitId, out var unit) ||
                unit.NationId != nationId || unit.HasAttacked || !unit.CanAttackThisTurn) return null;
            var visible = DecisionVisible(state, nationId);
            if (targetId.HasValue && state.Units.TryGetValue(targetId.Value, out var target) &&
                CanAttackTarget(state, unit, target, nationId, visible, out var preview))
            {
                var utility = AttackUtility(state, unit, target, preview);
                if (utility >= 12f)
                    return new DecisionOption
                    {
                        Command = new AttackCommand(nationId, unit.Id, target.Id),
                        Score = utility + 35f,
                        BattleIntent = string.IsNullOrEmpty(intent) ? "接续既定战术组合" : intent,
                        MicroReason = $"{unit.Type}#{unit.Id}按预案攻击{target.Type}#{target.Id}，" +
                                      $"伤害{preview.Damage}/反击{preview.CounterDamage}"
                    };
            }
            if (!wallId.HasValue || !state.CityWalls.TryGetValue(wallId.Value, out var wall) || wall.Health <= 0)
                return null;
            var wallPreview = _simulation.Walls.Preview(state, unit, wall);
            if (wallPreview.Damage <= 0) return null;
            return new DecisionOption
            {
                Command = new AttackWallCommand(nationId, unit.Id, wall.Id),
                Score = wallPreview.Damage * 9f + wallPreview.GarrisonDamage * 12f + 35f,
                BattleIntent = string.IsNullOrEmpty(intent) ? "接续既定战术组合" : intent,
                MicroReason = $"{unit.Type}#{unit.Id}按预案攻击墙#{wall.Id}，" +
                              $"墙伤{wallPreview.Damage}/驻军伤{wallPreview.GarrisonDamage}/反击{wallPreview.CounterDamage}"
            };
        }

        private void ClearPending()
        {
            _pendingRound = -1;
            _pendingUnitId = null;
            _pendingTargetId = null;
            _pendingWallId = null;
            _pendingIntent = string.Empty;
        }

        private HashSet<HexCoord> DecisionVisible(GameState state, int nationId)
        {
            if (ReferenceEquals(_decisionState, state) && _decisionNationId == nationId && _decisionVisible != null)
                return _decisionVisible;
            return _simulation.Visibility.CalculateVisibleCells(state, nationId);
        }

        private int ObjectiveDistance(GameState state, HexCoord position)
        {
            return _objectiveCityId.HasValue && state.Cities.TryGetValue(_objectiveCityId.Value, out var city)
                ? position.DistanceTo(city.Center)
                : 0;
        }

        private bool IsAtStrategicObjective(HexCoord position)
        {
            return _objectivePosition.HasValue && _objectivePosition.Value.DistanceTo(position) <= 3;
        }

        private bool IsInsideObjectiveCity(GameState state, UnitState unit)
        {
            return _objectiveCityId.HasValue && state.Cities.TryGetValue(_objectiveCityId.Value, out var city) &&
                   city.NationId == unit.NationId && city.Center.DistanceTo(unit.Position) <= city.Level;
        }

        private bool IsEnemyObjectiveCenter(GameState state, HexCoord position, int nationId)
        {
            return _objectiveCityId.HasValue && state.Cities.TryGetValue(_objectiveCityId.Value, out var city) &&
                   city.NationId != nationId && city.Center.Equals(position);
        }

        private static CityState CityContaining(GameState state, HexCoord position, int nationId)
        {
            foreach (var city in state.Cities.Values)
                if (city.NationId == nationId && city.Center.DistanceTo(position) <= city.Level) return city;
            return null;
        }

        private static int CountBreachedWalls(GameState state, int cityId)
        {
            var count = 0;
            foreach (var wall in state.CityWalls.Values)
                if (wall.CityId == cityId && wall.Health <= 0) count++;
            return count;
        }

        private static int CountFriendlyNear(GameState state, int nationId, HexCoord position, int radius)
        {
            var count = 0;
            foreach (var unit in state.Units.Values)
                if (unit.NationId == nationId && unit.Health > 0 && unit.Position.DistanceTo(position) <= radius) count++;
            return count;
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
    }
}

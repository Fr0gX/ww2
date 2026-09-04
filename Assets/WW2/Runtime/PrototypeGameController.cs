using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using WW2.Core.AI;
using WW2.Core.Commands;
using WW2.Core.Model;
using WW2.Core.Systems;

namespace WW2.Runtime
{
    public sealed class PrototypeGameController : MonoBehaviour
    {
        public const int HumanNationId = 1;
        public const int ComputerNationId = 2;

        private readonly HashSet<HexCoord> _legalMoves = new HashSet<HexCoord>();
        private readonly Dictionary<HexCoord, PathResult> _legalMovePaths = new Dictionary<HexCoord, PathResult>();
        private readonly HashSet<HexCoord> _legalTargets = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> _legalHealTargets = new HashSet<HexCoord>();
        private readonly HashSet<int> _legalWallTargetIds = new HashSet<int>();
        private readonly Dictionary<HexCoord, int> _legalWallTargetsByCell = new Dictionary<HexCoord, int>();
        private readonly HashSet<int> _selectableUnitIds = new HashSet<int>();
        private readonly HashSet<HexCoord> _supplyReach = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> _enemyControlReach = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> _visibleCells = new HashSet<HexCoord>();
        private readonly List<string> _history = new List<string>();
        private readonly List<string> _aiReplayBuffer = new List<string>();
        private GameBootstrap _game;
        private HexMapView _mapView;
        private AiPlanner _ai;
        private int? _selectedUnitId;
        private int? _inspectedUnitId;
        private int? _inspectedWallId;
        private int? _inspectedCityId;
        private int? _inspectedBuildingId;
        private IReadOnlyList<HexCoord> _supplyPath;
        private bool _computerThinking;
        private bool _cameraFocusRequested;
        private bool _presentationLocked;
        private bool _skipComputerPresentation;
        private string _aiReplayPath;
        private int? _hoveredWallId;
        private NationIncome _humanIncome = new NationIncome();

        public int? SelectedUnitId => _selectedUnitId;
        public bool ComputerThinking => _computerThinking;
        public bool SkipComputerPresentation => _skipComputerPresentation;
        public string AiReplayPath => _aiReplayPath;
        public int LegalMoveCount => _legalMoves.Count;
        public int LegalTargetCount => _legalTargets.Count;
        public int LegalHealTargetCount => _legalHealTargets.Count;
        public int LegalWallTargetCount => _legalWallTargetIds.Count;
        public int ReadyUnitCount
        {
            get
            {
                var count = 0;
                foreach (var unit in _game.State.Units.Values)
                    if (unit.NationId == HumanNationId && unit.CanActThisTurn) count++;
                return count;
            }
        }
        public int HumanUnitCount
        {
            get
            {
                var count = 0;
                foreach (var unit in _game.State.Units.Values)
                    if (unit.NationId == HumanNationId && unit.Health > 0) count++;
                return count;
            }
        }
        public int WinnerNationId { get; private set; }
        public string StatusText { get; private set; } = "左键选择或查看，右键短按攻击；右键拖动地图，滚轮缩放。";
        public HexCoord? HoveredCoord { get; private set; }
        public string HoverText { get; private set; } = string.Empty;
        public string ResultTitle { get; private set; } = string.Empty;
        public string ResultDetail { get; private set; } = string.Empty;
        public HexCoord? ResultCoord { get; private set; }
        public float ResultVisibleUntil { get; private set; }
        public CombatPreview HoveredCombatPreview { get; private set; }
        public WallCombatPreview HoveredWallCombatPreview { get; private set; }
        public UnitState HoveredCombatDefender { get; private set; }
        public SupplyStatus SelectedSupplyStatus => GetSupplyStatus(SelectedUnit);
        public IReadOnlyList<string> History => _history;
        public HashSet<HexCoord> VisibleCells => _visibleCells;
        public int? HoveredWallId => _hoveredWallId;

        public UnitState SelectedUnit
        {
            get
            {
                if (!_selectedUnitId.HasValue) return null;
                return _game.State.Units.TryGetValue(_selectedUnitId.Value, out var unit) ? unit : null;
            }
        }

        public UnitState InspectedUnit => _inspectedUnitId.HasValue && _game.State.Units.TryGetValue(_inspectedUnitId.Value, out var unit)
            ? unit : null;
        public CityWallState InspectedWall => _inspectedWallId.HasValue && _game.State.CityWalls.TryGetValue(_inspectedWallId.Value, out var wall)
            ? wall : null;
        public CityState InspectedCity => _inspectedCityId.HasValue && _game.State.Cities.TryGetValue(_inspectedCityId.Value, out var city)
            ? city : null;
        public BuildingState InspectedBuilding => _inspectedBuildingId.HasValue &&
                                                  _game.State.Buildings.TryGetValue(_inspectedBuildingId.Value,
                                                      out var building)
            ? building : null;
        public NationState HumanNation => _game.State.Nations.TryGetValue(HumanNationId, out var nation) ? nation : null;
        public NationIncome HumanIncome => _humanIncome;

        public SupplyStatus GetSupplyStatus(UnitState unit)
        {
            if (unit == null) return null;
            // Unit supply is locked at its own turn start. Mid-turn movement and
            // captures deliberately do not change this status.
            return _game.Simulation.Supply.GetStatus(_game.State, unit);
        }

        public void Initialize(GameBootstrap game, HexMapView mapView)
        {
            if (_mapView != null)
            {
                _mapView.CellClicked -= HandleCellClicked;
                _mapView.CellRightClicked -= HandleCellRightClicked;
                _mapView.CellHovered -= HandleCellHovered;
                _mapView.BackgroundClicked -= ReturnToOverview;
            }
            _game = game;
            _mapView = mapView;
            _ai = new AiPlanner(game.Simulation, new StrategicEvaluator());
            _mapView.CellClicked += HandleCellClicked;
            _mapView.CellRightClicked += HandleCellRightClicked;
            _mapView.CellHovered += HandleCellHovered;
            _mapView.BackgroundClicked += ReturnToOverview;
            ResetSession();
        }

        public void ResetSession()
        {
            StopAllCoroutines();
            _selectedUnitId = null;
            _inspectedUnitId = null;
            _inspectedWallId = null;
            _inspectedCityId = null;
            _inspectedBuildingId = null;
            _computerThinking = false;
            _presentationLocked = false;
            _skipComputerPresentation = false;
            _hoveredWallId = null;
            _cameraFocusRequested = true;
            WinnerNationId = 0;
            HoveredCoord = null;
            HoverText = string.Empty;
            ResultTitle = string.Empty;
            ResultDetail = string.Empty;
            ResultCoord = null;
            ResultVisibleUntil = 0f;
            HoveredCombatPreview = null;
            HoveredWallCombatPreview = null;
            HoveredCombatDefender = null;
            _history.Clear();
            BeginAiReplay();
            StatusText = "目标：夺取敌方市中心，或消灭敌方野战部队。";
            AddHistory("战役开始：蓝方先行。");
            Refresh();
        }

        public void EndPlayerTurn()
        {
            if (!CanHumanInput()) return;
            _selectedUnitId = null;
            AddHistory("蓝方结束回合，结算城市与补给。 ");
            _game.Simulation.TryExecute(_game.State,
                new EndTurnCommand(HumanNationId, ComputerNationId));
            if (CheckVictory())
            {
                Refresh();
                return;
            }

            _computerThinking = true;
            _skipComputerPresentation = false;
            WriteAiReplay($"TURN_START round={_game.State.Round} army={BuildComputerArmySnapshot()}");
            StatusText = "红方正在重新评价战场……";
            PublishResult("回合结算", "补给区已重算，单位补给状态与行动力已锁定至本回合结束。", null);
            Refresh();
            StartCoroutine(RunComputerTurn());
        }

        public void GarrisonSelected()
        {
            if (!CanHumanInput() || !SelectedUnitId.HasValue) return;
            var command = new GarrisonCommand(HumanNationId, SelectedUnitId.Value);
            if (_game.Simulation.TryExecute(_game.State, command))
            {
                var unit = SelectedUnit;
                var multiplier = _game.Simulation.Combat.GetGarrisonMultiplier(_game.State, unit);
                var controlRadius = _game.Simulation.Rules.HasAbility(unit.Type, UnitAbility.FormsControlZone) ? 1 : 0;
                AddHistory($"{UnitLabel(unit)}执行驻扎，建立防御阵地。 ");
                StatusText = controlRadius > 0
                    ? $"驻扎完成：防御 ×{multiplier:0.0}，对相邻 1 格形成控制区。"
                    : $"驻扎完成：防御 ×{multiplier:0.0}。";
                _selectedUnitId = null;
                Refresh();
                var detail = multiplier > 1.5f
                    ? "主战步兵占据城墙格：防御 ×2.5，并直接强化该段边防。"
                    : controlRadius > 0
                        ? "防御 ×1.5；主战步兵对相邻 1 格形成控制区。"
                        : "防御 ×1.5；单位建立就地防御阵地。";
                PublishResult("驻扎完成", detail, unit.Position);
                _mapView.PlayPulse(unit.Position, new Color(1f, 0.82f, 0.20f));
                return;
            }
            else
            {
                StatusText = "驻扎要求单位仍可行动且不在敌方城市内；城墙格仅允许主战步兵驻扎。";
            }
            Refresh();
        }

        public bool CanGarrisonSelected()
        {
            var unit = SelectedUnit;
            return CanHumanInput() && _game.Simulation.CanGarrison(_game.State, unit);
        }

        public bool CanOccupySelected(out string reason)
        {
            var unit = SelectedUnit;
            var city = unit == null ? null : _game.Simulation.Cities.CityAtCenter(_game.State, unit.Position);
            return _game.Simulation.Cities.CanOccupy(_game.State, unit, city, out reason);
        }

        public void OccupySelected()
        {
            if (!CanHumanInput() || SelectedUnit == null) return;
            var city = _game.Simulation.Cities.CityAtCenter(_game.State, SelectedUnit.Position);
            var reason = city == null ? "单位必须控制敌方市中心" : string.Empty;
            if (city == null || !CanOccupySelected(out reason))
            {
                StatusText = reason;
                return;
            }

            if (_game.Simulation.TryExecute(_game.State,
                    new OccupyCityCommand(HumanNationId, SelectedUnit.Id, city.Id)))
            {
                AddHistory($"{UnitLabel(SelectedUnit)}完成城市占领。 ");
                StatusText = "城市已正式归属蓝方。";
                Refresh();
                PublishResult("城市占领", "城市、控制区与残存城墙已经易主。", city.Center);
                _mapView.PlayPulse(city.Center, new Color(0.20f, 0.88f, 1f));
                CheckVictory();
            }
        }

        public bool CanPromoteSelected()
        {
            return CanHumanInput() && _game.Simulation.CanPromote(SelectedUnit);
        }

        public void PromoteSelected()
        {
            var unit = SelectedUnit;
            if (!CanHumanInput() || unit == null || !_game.Simulation.CanPromote(unit)) return;
            var previousLevel = unit.Level;
            if (!_game.Simulation.TryExecute(_game.State, new PromoteUnitCommand(HumanNationId, unit.Id))) return;
            AddHistory($"{UnitLabel(unit)}由L{previousLevel}晋升至L{unit.Level}。 ");
            StatusText = $"晋升完成：L{unit.Level}，生命已完全恢复" +
                         (unit.Level >= 4 ? "，最大射程 +1。" : "。 ");
            Refresh();
            _mapView.PlayPulse(unit.Position, new Color(1f, 0.78f, 0.18f));
        }

        public bool IsUnitSupplied(UnitState unit)
        {
            return unit != null && _game.Simulation.Supply.IsUnitSupplied(_game.State, unit);
        }

        public bool IsWallTarget(int wallId) => _legalWallTargetIds.Contains(wallId);

        public void Restart()
        {
            _game.Restart();
        }

        public bool CanProduce(UnitType type, out string reason)
        {
            if (!CanHumanInput())
            {
                reason = "当前不能制造单位";
                return false;
            }
            if (InspectedBuilding != null)
                return _game.Simulation.Production.CanManufacture(_game.State, HumanNationId,
                    InspectedBuilding.Id, type, out _, out reason);
            if (InspectedCity != null)
                return _game.Simulation.Production.CanRecruit(_game.State, HumanNationId,
                    InspectedCity.Id, type, out _, out reason);
            reason = "请选择城市或工厂";
            return false;
        }

        public void Produce(UnitType type)
        {
            if (!CanProduce(type, out var reason))
            {
                StatusText = reason;
                return;
            }
            GameCommand command = InspectedBuilding != null
                ? new ManufactureUnitCommand(HumanNationId, InspectedBuilding.Id, type)
                : new RecruitUnitCommand(HumanNationId, InspectedCity.Id, type);
            var previousMaximumId = 0;
            foreach (var id in _game.State.Units.Keys) previousMaximumId = Math.Max(previousMaximumId, id);
            if (!_game.Simulation.TryExecute(_game.State, command))
            {
                StatusText = "战场状态发生变化，制造未执行。";
                Refresh();
                return;
            }
            var produced = _game.State.Units[previousMaximumId + 1];
            var definition = _game.Simulation.Rules.Unit(type);
            StatusText = $"{UnitTypeLabel(type)}制造完成，下回合可以行动。";
            AddHistory($"蓝方制造{UnitTypeLabel(type)}，支付经济{definition.EconomyCost}、工业{definition.IndustryCost}。 ");
            PublishResult("制造完成",
                $"{UnitTypeLabel(type)}已部署；经济 -{definition.EconomyCost}，工业 -{definition.IndustryCost}。",
                produced.Position);
            Refresh();
            _mapView.PlayPulse(produced.Position, new Color(0.20f, 0.83f, 0.88f));
        }

        private void HandleCellClicked(HexCoord coord)
        {
            if (!CanHumanInput() || !_game.State.Map.TryGet(coord, out var cell)) return;

            if (cell.UnitId.HasValue && _game.State.Units.TryGetValue(cell.UnitId.Value, out var clickedUnit))
            {
                if (clickedUnit.NationId == HumanNationId)
                {
                    var canPromote = _game.Simulation.CanPromote(clickedUnit);
                    if (!clickedUnit.CanActThisTurn && !canPromote)
                    {
                        _selectedUnitId = null;
                        _inspectedUnitId = clickedUnit.Id;
                        _inspectedWallId = null;
                        _inspectedCityId = null;
                        _inspectedBuildingId = null;
                        _cameraFocusRequested = false;
                        StatusText = $"{UnitLabel(clickedUnit)}本回合行动完毕。";
                        Refresh();
                        return;
                    }
                    var selectionChanged = _selectedUnitId != clickedUnit.Id;
                    _cameraFocusRequested = selectionChanged;
                    _selectedUnitId = clickedUnit.Id;
                    _inspectedUnitId = clickedUnit.Id;
                    _inspectedWallId = null;
                    _inspectedCityId = null;
                    _inspectedBuildingId = null;
                    StatusText = canPromote
                        ? $"{UnitLabel(clickedUnit)}已满足晋升条件；可在左侧面板点击升级。"
                        : clickedUnit.IsPinnedByEnemyControl
                        ? $"已选择{UnitLabel(clickedUnit)}。移动已被敌方控制区截停，原有攻击尚未消耗。"
                        : clickedUnit.Type == UnitType.LightArmor
                        ? $"已选择{UnitLabel(clickedUnit)}。攻击后仍可继续移动。"
                        : clickedUnit.Type == UnitType.Medic
                            ? $"已选择{UnitLabel(clickedUnit)}。右键相邻受伤友军进行治疗。"
                            : clickedUnit.Type == UnitType.MainInfantry
                                ? $"已选择{UnitLabel(clickedUnit)}。无视地形移动加费。"
                                : $"已选择{UnitLabel(clickedUnit)}。远程压制目标；对方按射程与视野正常反击。";
                    ResultTitle = string.Empty;
                    ResultDetail = string.Empty;
                    ResultCoord = null;
                    if (selectionChanged) Refresh();
                    return;
                }
            }

            if (TryGetVisibleHostileWallAt(coord, out var inspectedWall))
            {
                _inspectedWallId = inspectedWall.Id;
                _inspectedUnitId = cell.UnitId.HasValue &&
                                   _game.State.Units.TryGetValue(cell.UnitId.Value, out var wallDefender) &&
                                   wallDefender.NationId != HumanNationId && VisibleCells.Contains(coord)
                    ? wallDefender.Id
                    : (int?)null;
                _inspectedCityId = inspectedWall.CityId;
                _inspectedBuildingId = null;
                StatusText = _inspectedUnitId.HasValue
                    ? _legalWallTargetsByCell.ContainsKey(coord)
                        ? "已同时锁定城市边防与守军；右键攻击该边界格。"
                        : "已同时查看城市边防与守军。"
                    : _legalWallTargetsByCell.ContainsKey(coord)
                        ? "已准备突破该边界格；右键内部格执行攻击。"
                        : "已查看该边界格的城市边防。";
                return;
            }

            if (cell.UnitId.HasValue && _game.State.Units.TryGetValue(cell.UnitId.Value, out clickedUnit) &&
                clickedUnit.NationId != HumanNationId && VisibleCells.Contains(coord))
            {
                _inspectedUnitId = clickedUnit.Id;
                _inspectedWallId = null;
                _inspectedCityId = null;
                _inspectedBuildingId = null;
                StatusText = _selectedUnitId.HasValue && _legalTargets.Contains(coord)
                    ? "已查看敌方单位；右键立即攻击。"
                    : "已查看敌方单位。";
                return;
            }

            if (_selectedUnitId.HasValue && _legalMoves.Contains(coord))
            {
                var unit = SelectedUnit;
                var origin = unit.Position;
                if (!_legalMovePaths.TryGetValue(coord, out var movePath))
                {
                    StatusText = "战场状态已经变化，请重新选择单位。";
                    Refresh();
                    return;
                }
                var movementCost = movePath?.Cost ?? 0;
                CityState targetCity = null;
                if (cell.CityId.HasValue) _game.State.Cities.TryGetValue(cell.CityId.Value, out targetCity);
                var enteringEnemyCenter = targetCity != null && targetCity.NationId != unit.NationId;
                if (_game.Simulation.TryExecute(_game.State,
                        new MoveCommand(HumanNationId, unit.Id, coord)))
                {
                    // Keep the player's current framing after an action. If this move
                    // exhausts the unit, Refresh clears its selection; requesting a
                    // focus here would then snap the camera back to the overview.
                    AddHistory($"{UnitLabel(unit)}从{origin}移动至{coord}。 ");
                    StatusText = enteringEnemyCenter
                        ? _game.Simulation.Rules.HasAbility(unit.Type, UnitAbility.RapidOccupation)
                            ? "步兵已控制市中心，可以立即点击“占领”。"
                            : $"市中心已失控；第 {targetCity.OccupationReadyRound} 轮可点击“占领”。"
                        : unit.IsPinnedByEnemyControl
                            ? "进入敌方控制区：剩余移动被清空，原有攻击未被消耗。"
                            : $"移动完成；{SupplyLabel(_game.Simulation.Supply.GetStatus(_game.State, unit))}。";
                    CheckVictory();
                    Refresh();
                    var supply = SupplyLabel(_game.Simulation.Supply.GetStatus(_game.State, unit));
                    if (enteringEnemyCenter)
                    {
                        PublishResult("控制市中心", $"城市失控；{supply}；等待主动占领。", coord);
                    }
                    else
                    {
                        PublishResult("移动完成",
                            unit.IsPinnedByEnemyControl
                                ? $"控制区截停移动；原有攻击未消耗；{supply}。"
                                : $"消耗 {movementCost} 行动力，剩余 {unit.RemainingMovement}；{supply}。", coord);
                    }
                    _mapView.PlayUnitMove(unit.Id, movePath?.Cells);
                    BeginPresentationLock(0.22f + (movePath?.Cells.Count ?? 2) * 0.135f);
                    return;
                }
                Refresh();
                return;
            }

            if (cell.CityId.HasValue && _game.State.Cities.TryGetValue(cell.CityId.Value, out var inspectedCity))
            {
                _inspectedCityId = inspectedCity.Id;
                _inspectedUnitId = null;
                _inspectedWallId = null;
                _inspectedBuildingId = null;
                StatusText = "已查看城市；地图标明归属、占领状态与城墙情况。";
            }
            else if (cell.BuildingId.HasValue &&
                     _game.State.Buildings.TryGetValue(cell.BuildingId.Value, out var inspectedBuilding))
            {
                _inspectedBuildingId = inspectedBuilding.Id;
                _inspectedCityId = inspectedBuilding.CityId;
                _inspectedUnitId = null;
                _inspectedWallId = null;
                StatusText = inspectedBuilding.Type == BuildingType.MilitaryFactory
                    ? "已查看工厂；可以即时制造装甲或火炮。"
                    : "已查看民营企业；未被占据时持续提供经济。";
            }
            else
            {
                ReturnToOverview();
            }
        }

        public void ReturnToOverview()
        {
            if (_game?.State == null || _computerThinking || _presentationLocked) return;
            _selectedUnitId = null;
            _inspectedUnitId = null;
            _inspectedWallId = null;
            _inspectedCityId = null;
            _inspectedBuildingId = null;
            HoveredCombatPreview = null;
            HoveredWallCombatPreview = null;
            HoveredCombatDefender = null;
            ResultTitle = string.Empty;
            ResultDetail = string.Empty;
            ResultCoord = null;
            StatusText = "全局视角：选择仍可行动的己方单位。";
            _cameraFocusRequested = true;
            Refresh();
        }

        private void HandleCellRightClicked(HexCoord coord)
        {
            if (!CanHumanInput() || SelectedUnit == null || !_game.State.Map.TryGet(coord, out var cell)) return;

            if (cell.UnitId.HasValue && _game.State.Units.TryGetValue(cell.UnitId.Value, out var friendlyUnit) &&
                friendlyUnit.NationId == HumanNationId)
            {
                if (_legalHealTargets.Contains(coord))
                {
                    ExecutePlayerHeal(friendlyUnit);
                    return;
                }
                StatusText = "该格有己方单位；请用左键选择。";
                return;
            }

            if (_legalWallTargetsByCell.TryGetValue(coord, out var wallId) &&
                _game.State.CityWalls.TryGetValue(wallId, out var wall))
            {
                ExecutePlayerWallAttack(wall);
                return;
            }

            if (cell.UnitId.HasValue && _legalTargets.Contains(coord) &&
                _game.State.Units.TryGetValue(cell.UnitId.Value, out var defender) &&
                defender.NationId != HumanNationId)
            {
                ExecutePlayerAttack(defender);
                return;
            }

            if (!_legalTargets.Contains(coord))
            {
                StatusText = "该格不是当前单位的合法攻击目标。";
            }
        }

        private void ExecutePlayerHeal(UnitState target)
        {
            var healer = SelectedUnit;
            if (healer == null || target == null) return;
            var amount = _game.Simulation.Medical.Preview(_game.State, healer, target);
            if (amount <= 0) return;
            var healerPosition = healer.Position;
            var targetPosition = target.Position;
            var healerId = healer.Id;
            var targetId = target.Id;
            if (_game.Simulation.TryExecute(_game.State,
                    new HealCommand(HumanNationId, healerId, targetId)))
            {
                AddHistory($"{UnitLabel(healer)}治疗{UnitLabel(target)}：恢复 {amount}。 ");
                StatusText = $"医疗完成：{UnitLabel(target)}恢复 {amount} 生命。";
                Refresh();
                PublishResult("医疗支援", $"恢复 {amount} 生命；医疗兵本回合行动结束。", targetPosition);
                _mapView.PlayHealing(healerPosition, targetPosition, amount);
                BeginPresentationLock(0.52f);
            }
        }

        private void ExecutePlayerAttack(UnitState defender)
        {
            var attacker = SelectedUnit;
            if (attacker == null) return;
            var preview = _game.Simulation.Combat.Preview(_game.State, attacker, defender);
            var attackerPosition = attacker.Position;
            var defenderPosition = defender.Position;
            var attackerName = UnitLabel(attacker);
            var defenderName = UnitLabel(defender);
            var attackerType = attacker.Type;
            var defenderType = defender.Type;
            var attackerId = attacker.Id;
            var defenderId = defender.Id;
            if (_game.Simulation.TryExecute(_game.State,
                    new AttackCommand(HumanNationId, attacker.Id, defender.Id)))
            {
                AddHistory($"{attackerName}攻击{defenderName}：造成{preview.Damage}，反击{preview.CounterDamage}。 ");
                StatusText = preview.AppliesSuppression
                    ? "炮击完成，目标受到压制。"
                    : _game.Simulation.Rules.HasAbility(attacker.Type,
                        UnitAbility.PreservesMovementAfterAttack)
                        ? $"装甲攻击完成；仍有 {attacker.RemainingMovement} 行动力。"
                        : "攻击完成；剩余行动力已经耗尽。";
                if (_game.Simulation.CanPromote(attacker)) StatusText += " 已满足升级条件。";
                CheckVictory();
                Refresh();
                _mapView.PlayCombatSequence(attackerId, attackerType, attackerPosition, defenderPosition,
                    preview.Damage, preview.CounterDamage, new Color(1f, 0.38f, 0.10f), defenderId,
                    defenderType, !_game.State.Units.ContainsKey(defenderId),
                    !_game.State.Units.ContainsKey(attackerId));
                BeginPresentationLock(preview.CounterDamage > 0 ? 0.72f : 0.48f);
                return;
            }
            Refresh();
        }

        private void ExecutePlayerWallAttack(CityWallState wall)
        {
            var attacker = SelectedUnit;
            if (attacker == null || wall == null) return;
            var preview = _game.Simulation.Walls.Preview(_game.State, attacker, wall);
            var attackerPosition = attacker.Position;
            var attackerId = attacker.Id;
            var attackerType = attacker.Type;
            HexCoord? garrisonPosition = null;
            int? garrisonId = null;
            UnitType? garrisonType = null;
            if (preview.GarrisonUnitId.HasValue &&
                _game.State.Units.TryGetValue(preview.GarrisonUnitId.Value, out var previewGarrison))
            {
                garrisonPosition = previewGarrison.Position;
                garrisonId = previewGarrison.Id;
                garrisonType = previewGarrison.Type;
            }
            if (_game.Simulation.TryExecute(_game.State,
                    new AttackWallCommand(HumanNationId, attacker.Id, wall.Id)))
            {
                var destroyed = wall.Health <= 0;
                var garrisonResult = preview.GarrisonDamage > 0 ? $"，驻军受创 {preview.GarrisonDamage}" : string.Empty;
                AddHistory($"{UnitLabel(attacker)}攻击城墙：墙体 {preview.Damage}{garrisonResult}，反击 {preview.CounterDamage}。 ");
                StatusText = destroyed
                    ? "该边界格的城墙已摧毁；可从任一外侧进入。"
                    : preview.GarrisonDamage > 0
                        ? $"城墙剩余 {wall.Health}/{wall.MaxHealth}；驻军同步受创并被压制。"
                        : $"城墙剩余 {wall.Health}/{wall.MaxHealth}。";
                if (_game.Simulation.CanPromote(attacker)) StatusText += " 已满足升级条件。";
                Refresh();
                _mapView.PlayCombatSequence(attackerId, attackerType, attackerPosition, wall.Position,
                    preview.Damage, preview.CounterDamage, new Color(1f, 0.58f, 0.10f), garrisonId, garrisonType,
                    garrisonId.HasValue && !_game.State.Units.ContainsKey(garrisonId.Value),
                    !_game.State.Units.ContainsKey(attackerId), destroyed, 1f,
                    HexMapView.WallWorldPosition(wall));
                if (garrisonPosition.HasValue && preview.GarrisonDamage > 0)
                {
                    _mapView.PlayDamageNumber(garrisonPosition.Value, preview.GarrisonDamage, false);
                    _mapView.PlayPulse(garrisonPosition.Value, new Color(1f, 0.30f, 0.12f));
                }
                BeginPresentationLock(preview.CounterDamage > 0 ? 0.72f : 0.50f);
            }
        }

        public bool IsUnitAnimating(int unitId) => _mapView != null && _mapView.IsUnitAnimating(unitId);

        public void SkipComputerTurnPresentation()
        {
            if (!_computerThinking) return;
            _skipComputerPresentation = true;
            _mapView.CancelActionPresentations();
            StatusText = "正在快速结算敌方剩余行动……";
            ResultTitle = string.Empty;
            ResultDetail = string.Empty;
        }

        public static string SupplyLabel(SupplyStatus status)
        {
            if (status == null || status.Tier == 0) return "补给充足";
            var name = status.Tier == 1 ? "补给异常" : "补给极度异常";
            var penalty = Mathf.RoundToInt((1f - status.AttackMultiplier) * 100f);
            return $"{name}：攻防行动 -{penalty}%（连续 {status.ConsecutiveTurnsWithoutSupply} 回合）";
        }

        private IEnumerator RunComputerTurn()
        {
            yield return new WaitForSecondsRealtime(0.08f);
            StatusText = "红方正在后台规划整个回合……";
            var planningStartedAt = Time.realtimeSinceStartup;
            var sourceState = _game.State;
            var planningSimulation = _game.Simulation;
            var planningAi = _ai;
            var planningTask = Task.Run(() => BuildComputerTurnPlan(
                GameStateCloner.Clone(sourceState), planningSimulation, planningAi));
            while (!planningTask.IsCompleted) yield return null;
            if (planningTask.IsFaulted)
            {
                var message = planningTask.Exception?.GetBaseException().Message ?? "unknown planner failure";
                Debug.LogError($"[AI] whole-turn planner failed: {message}");
                WriteAiReplay($"PLAN_FAILED round={_game.State.Round} reason={message}");
                FinishComputerTurn(0);
                yield break;
            }

            var plan = planningTask.Result;
            var planningMilliseconds = Mathf.RoundToInt((Time.realtimeSinceStartup - planningStartedAt) * 1000f);
            WriteAiReplay($"PLAN_READY round={_game.State.Round} actions={plan.Actions.Count} " +
                          $"skipped={plan.SkippedCommands.Count} planMs={planningMilliseconds} " +
                          $"terminal={plan.TerminalReason}");
            foreach (var skipped in plan.SkippedCommands)
                WriteAiReplay($"STATIC_COMMAND_SKIPPED round={_game.State.Round} {skipped}");
            StatusText = _skipComputerPresentation
                ? "敌方方案已确定，正在直接结算……"
                : $"敌方方案已确定：{plan.Actions.Count} 个行动。";

            var actions = 0;
            for (var index = 0; index < plan.Actions.Count && WinnerNationId == 0; index++)
            {
                if (_skipComputerPresentation)
                {
                    for (var remaining = index; remaining < plan.Actions.Count; remaining++)
                        RecordPlannedAction(plan.Actions[remaining], remaining + 1, false);
                    _game.ReplaceState(plan.FinalState);
                    actions = plan.Actions.Count;
                    CheckVictory();
                    break;
                }

                var planned = plan.Actions[index];
                var command = planned.Command;
                var feedback = planned.Feedback;
                // There is one player-visibility mask. Enemy markers and presentations are both
                // gated by this same composite mask (units + cities + supply coverage).
                var visibleMask = _game.Simulation.Visibility.CalculateVisibleCells(_game.State, HumanNationId);
                var hadExistingMarker = feedback.UnitId.HasValue && _mapView.HasUnitMarker(feedback.UnitId.Value);
                if (!_game.Simulation.TryExecute(_game.State, command))
                {
                    WriteAiReplay($"PLAN_DIVERGENCE round={_game.State.Round} action={index + 1} command={command.Type}");
                    Debug.LogError($"[AI] fixed turn plan diverged at action {index + 1}: {command.Type}");
                    _game.ReplaceState(plan.FinalState);
                    actions = plan.Actions.Count;
                    CheckVictory();
                    break;
                }
                var movementAction = feedback.Path != null;
                var fullyObservedMovement = movementAction &&
                                            FeedbackInsideMask(feedback, visibleMask) &&
                                            PathInsideMask(feedback.Path, visibleMask);
                feedback.Visible = movementAction ? fullyObservedMovement :
                    FeedbackInsideMask(feedback, visibleMask);
                var animateMoveFromExistingMarker = hadExistingMarker && fullyObservedMovement;
                RecordPlannedAction(planned, index + 1, true);
                actions++;
                if (CheckVictory()) break;
                if (feedback.Visible)
                {
                    if (!feedback.Attack) PublishResult(feedback.Title, feedback.Detail, feedback.To);
                    if (animateMoveFromExistingMarker)
                    {
                        // Animate the marker that was visible at the starting cell. Refreshing first
                        // would delete it, and moves leaving vision would appear to vanish mid-step.
                        _mapView.PlayUnitMove(feedback.UnitId.Value,
                            feedback.Path ?? new[] { feedback.From.Value, feedback.To.Value }, 2.15f);
                        _mapView.FocusOn(feedback.To.Value, 3);
                        while (_mapView.IsUnitAnimating(feedback.UnitId.Value) && !_skipComputerPresentation)
                            yield return null;
                        Refresh();
                    }
                    else
                    {
                        Refresh();
                        if (feedback.Attack && feedback.From.HasValue && feedback.To.HasValue)
                        {
                            _mapView.PlayCombatSequence(feedback.UnitId, feedback.AttackerType, feedback.From.Value,
                                feedback.To.Value, feedback.DamageAtTo, feedback.CounterDamageAtFrom, feedback.Color,
                                feedback.TargetUnitId, feedback.TargetType, feedback.TargetDestroyed,
                                feedback.AttackerDestroyed, feedback.WallDestroyed, 1.8f, feedback.TargetWorld);
                            if (feedback.SecondaryTo.HasValue && feedback.SecondaryDamageAtTo > 0)
                            {
                                _mapView.PlayDamageNumber(feedback.SecondaryTo.Value, feedback.SecondaryDamageAtTo, false);
                                _mapView.PlayPulse(feedback.SecondaryTo.Value, new Color(1f, 0.30f, 0.12f));
                            }
                        }
                        else if (feedback.Support && feedback.From.HasValue && feedback.To.HasValue)
                        {
                            _mapView.PlayHealing(feedback.From.Value, feedback.To.Value, feedback.HealingAtTo);
                        }
                        else if (feedback.To.HasValue)
                        {
                            _mapView.PlayPulse(feedback.To.Value, feedback.Color);
                        }
                        if (feedback.To.HasValue) _mapView.FocusOn(feedback.To.Value, 3);
                        if (feedback.Attack)
                        {
                            while (_mapView.IsCombatPresentationActive && !_skipComputerPresentation)
                                yield return null;
                        }
                        else
                        {
                            yield return new WaitForSecondsRealtime(0.24f);
                        }
                    }
                }
                else
                {
                    // Crossing the formal visibility boundary updates the marker silently. The
                    // camera, path and action effect remain hidden because part of the action was unseen.
                    Refresh();
                    yield return null;
                }
            }

            FinishComputerTurn(actions);
        }

        private ComputerTurnPlan BuildComputerTurnPlan(GameState planningState, GameSimulation simulation,
            AiPlanner planner)
        {
            var plan = new ComputerTurnPlan { FinalState = planningState };
            List<AiTurnPlanEntry> staticEntries;
            simulation.Supply.BeginFastEvaluation(planningState);
            try
            {
                // This is the only decision pass of the turn. The resulting command list is immutable;
                // committing it below may discard invalidated commands but never asks the AI to reconsider.
                staticEntries = planner.PlanTurnStatic(planningState, ComputerNationId);
            }
            finally
            {
                simulation.Supply.EndFastEvaluation();
            }

            for (var index = 0; index < staticEntries.Count && DetermineWinner(planningState) == 0; index++)
            {
                var entry = staticEntries[index];
                var command = entry.Command;
                if (!CanCaptureComputerFeedback(planningState, command))
                {
                    plan.SkippedCommands.Add($"index={index + 1} command={command.Type} reason=missing-reference " +
                                             $"decision={entry.DecisionTrace}");
                    continue;
                }

                var feedback = CaptureComputerFeedback(planningState, command, simulation);
                if (!simulation.TryExecute(planningState, command))
                {
                    plan.SkippedCommands.Add($"index={index + 1} command={command.Type} " +
                                             $"reason=invalidated-by-plan decision={entry.DecisionTrace}");
                    continue;
                }
                FinalizeComputerFeedback(planningState, feedback, simulation);
                plan.Actions.Add(new PlannedComputerAction
                {
                    Command = command,
                    DecisionTrace = entry.DecisionTrace,
                    Feedback = feedback
                });
            }
            plan.TerminalReason = DetermineWinner(planningState) != 0
                ? "victory"
                : $"static-plan-complete; proposed={staticEntries.Count}; skipped={plan.SkippedCommands.Count}";
            return plan;
        }

        private static bool CanCaptureComputerFeedback(GameState state, GameCommand command)
        {
            switch (command)
            {
                case AttackCommand attack:
                    return state.Units.ContainsKey(attack.AttackerId) && state.Units.ContainsKey(attack.DefenderId);
                case HealCommand heal:
                    return state.Units.ContainsKey(heal.HealerId) && state.Units.ContainsKey(heal.TargetId);
                case AttackWallCommand attackWall:
                    return state.Units.ContainsKey(attackWall.AttackerId) &&
                           state.CityWalls.ContainsKey(attackWall.WallId);
                case MoveCommand move:
                    return state.Units.ContainsKey(move.UnitId);
                case GarrisonCommand garrison:
                    return state.Units.ContainsKey(garrison.UnitId);
                case OccupyCityCommand occupy:
                    return state.Units.ContainsKey(occupy.UnitId) && state.Cities.ContainsKey(occupy.CityId);
                case PromoteUnitCommand promote:
                    return state.Units.ContainsKey(promote.UnitId);
                default:
                    return true;
            }
        }

        private static bool FeedbackInsideMask(ActionFeedback feedback, HashSet<HexCoord> visibleMask)
        {
            if (feedback == null || visibleMask == null) return false;
            var hasPosition = false;
            if (feedback.From.HasValue)
            {
                hasPosition = true;
                if (!visibleMask.Contains(feedback.From.Value)) return false;
            }
            if (feedback.To.HasValue)
            {
                hasPosition = true;
                if (!visibleMask.Contains(feedback.To.Value)) return false;
            }
            if (feedback.SecondaryTo.HasValue)
            {
                hasPosition = true;
                if (!visibleMask.Contains(feedback.SecondaryTo.Value)) return false;
            }
            return hasPosition;
        }

        private static bool PathInsideMask(IReadOnlyList<HexCoord> path, HashSet<HexCoord> visibleMask)
        {
            if (path == null || path.Count == 0 || visibleMask == null) return false;
            foreach (var coord in path)
            {
                if (visibleMask.Contains(coord)) continue;
                return false;
            }
            return true;
        }

        private void RecordPlannedAction(PlannedComputerAction planned, int actionNumber, bool includeHistory)
        {
            WriteAiReplay($"DECISION round={_game.State.Round} action={actionNumber} {planned.DecisionTrace}");
            WriteAiReplay($"RESULT round={_game.State.Round} action={actionNumber} {planned.Feedback.History}");
            WriteAiReplay($"PRESENTATION round={_game.State.Round} action={actionNumber} " +
                          $"shown={planned.Feedback.Visible} from={planned.Feedback.From} to={planned.Feedback.To}");
            Debug.Log($"[AI] round={_game.State.Round} action={actionNumber} {planned.Feedback.History}");
            if (includeHistory && planned.Feedback.Visible) AddHistory(planned.Feedback.History);
        }

        private void FinishComputerTurn(int actions)
        {
            if (WinnerNationId == 0)
            {
                Debug.Log($"[AI] round={_game.State.Round} turn-end actions={actions}; {BuildComputerIdleReport()}");
                WriteAiReplay($"TURN_END round={_game.State.Round} actions={actions}; {BuildComputerIdleReport()} " +
                              $"army={BuildComputerArmySnapshot()}");
                FlushAiReplay();
                _game.Simulation.TryExecute(_game.State,
                    new EndTurnCommand(ComputerNationId, HumanNationId));
                CheckVictory();
                SelectReadyOccupation();
            }

            _computerThinking = false;
            _skipComputerPresentation = false;
            FlushAiReplay();
            StatusText = WinnerNationId == 0
                ? "蓝方回合：观察补给、支援与夹击位置后行动。"
                : StatusText;
            Refresh();
        }

        private static int DetermineWinner(GameState state)
        {
            var blueUnits = 0;
            var redUnits = 0;
            var blueCities = 0;
            var redCities = 0;
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId == HumanNationId) blueUnits++;
                if (unit.NationId == ComputerNationId) redUnits++;
            }
            foreach (var city in state.Cities.Values)
            {
                if (city.NationId == HumanNationId) blueCities++;
                if (city.NationId == ComputerNationId) redCities++;
            }
            if (redUnits == 0 || redCities == 0) return HumanNationId;
            if (blueUnits == 0 || blueCities == 0) return ComputerNationId;
            return 0;
        }

        private string BuildComputerIdleReport()
        {
            var ready = new List<string>();
            foreach (var unit in _game.State.Units.Values)
            {
                if (unit.NationId != ComputerNationId || !unit.CanActThisTurn) continue;
                var reachable = _game.Simulation.Movement.FindReachablePaths(_game.State, unit).Count;
                ready.Add($"{UnitLabel(unit)}(AP={unit.RemainingMovement},pinned={unit.IsPinnedByEnemyControl}," +
                          $"moved={unit.HasMoved},targets={reachable})");
            }
            return ready.Count == 0 ? "no idle ready units" : "idle=" + string.Join(",", ready);
        }

        private string BuildComputerArmySnapshot()
        {
            var units = new List<string>();
            foreach (var unit in _game.State.Units.Values)
            {
                if (unit.NationId != ComputerNationId || unit.Health <= 0) continue;
                var supply = _game.Simulation.Supply.GetStatus(_game.State, unit);
                units.Add($"{UnitTypeLabel(unit.Type)}#{unit.Id}@{unit.Position}[HP={unit.Health},AP={unit.RemainingMovement}," +
                          $"SUP={supply.Tier},missed={supply.ConsecutiveTurnsWithoutSupply}," +
                          $"source={supply.SourceCityId},cost={supply.Cost}," +
                          $"moved={unit.HasMoved},attacked={unit.HasAttacked},garrison={unit.IsGarrisoned}]");
            }
            return string.Join("|", units);
        }

        private void BeginAiReplay()
        {
            try
            {
                FlushAiReplay();
                _aiReplayBuffer.Clear();
                var directory = Path.Combine(Application.persistentDataPath, "AI-Replays");
                Directory.CreateDirectory(directory);
                _aiReplayPath = Path.Combine(directory, $"ai-replay-{DateTime.Now:yyyyMMdd-HHmmss}.log");
                File.WriteAllText(_aiReplayPath,
                    $"WW2 AI REPLAY\nstarted={DateTime.Now:O}\nformat=DECISION/RESULT/TURN_END\n\n");
                Debug.Log($"[AI] replay log: {_aiReplayPath}");
            }
            catch (Exception exception)
            {
                _aiReplayPath = string.Empty;
                Debug.LogWarning($"Unable to create AI replay log: {exception.Message}");
            }
        }

        private void WriteAiReplay(string line)
        {
            if (string.IsNullOrEmpty(_aiReplayPath)) return;
            _aiReplayBuffer.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}\n");
        }

        private void FlushAiReplay()
        {
            if (string.IsNullOrEmpty(_aiReplayPath) || _aiReplayBuffer.Count == 0) return;
            try
            {
                File.AppendAllText(_aiReplayPath, string.Concat(_aiReplayBuffer));
                _aiReplayBuffer.Clear();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Unable to append AI replay log: {exception.Message}");
            }
        }

        private void SelectReadyOccupation()
        {
            foreach (var city in _game.State.Cities.Values)
            {
                if (!city.OccupyingUnitId.HasValue ||
                    !_game.State.Units.TryGetValue(city.OccupyingUnitId.Value, out var unit) ||
                    unit.NationId != HumanNationId ||
                    !_game.Simulation.Cities.CanOccupy(_game.State, unit, city, out _)) continue;
                _selectedUnitId = unit.Id;
                _inspectedUnitId = unit.Id;
                _inspectedWallId = null;
                _inspectedCityId = city.Id;
                _inspectedBuildingId = null;
                _cameraFocusRequested = true;
                StatusText = "占领条件已经满足：点击“占领城市”即可完成易主，并耗尽该单位行动力。";
                return;
            }
        }

        private ActionFeedback CaptureComputerFeedback(GameState state, GameCommand command,
            GameSimulation simulation)
        {
            var visible = simulation.Visibility.CalculateVisibleCells(state, HumanNationId);
            switch (command)
            {
                case AttackCommand attack:
                    var attacker = state.Units[attack.AttackerId];
                    var defender = state.Units[attack.DefenderId];
                    var preview = simulation.Combat.Preview(state, attacker, defender);
                    return new ActionFeedback
                    {
                        UnitId = attacker.Id,
                        AttackerType = attacker.Type,
                        TargetUnitId = defender.Id,
                        TargetType = defender.Type,
                        From = attacker.Position,
                        To = defender.Position,
                        Attack = true,
                        Visible = visible.Contains(attacker.Position) || visible.Contains(defender.Position),
                        Color = new Color(1f, 0.32f, 0.18f),
                        Title = "敌军攻击",
                        Detail = preview.CanCounter
                            ? $"受到 {preview.Damage} 伤害；敌军承受 {preview.CounterDamage} 反击。"
                            : $"受到 {preview.Damage} 伤害；无反击（{preview.CounterBlockedReason}）。",
                        History = $"红方{UnitLabel(attacker)}攻击{UnitLabel(defender)}：{preview.Damage}/{preview.CounterDamage}。",
                        DamageAtTo = preview.Damage,
                        CounterDamageAtFrom = preview.CounterDamage
                    };
                case HealCommand heal:
                    var healer = state.Units[heal.HealerId];
                    var healed = state.Units[heal.TargetId];
                    var healing = simulation.Medical.Preview(state, healer, healed);
                    return new ActionFeedback
                    {
                        UnitId = healer.Id,
                        From = healer.Position,
                        To = healed.Position,
                        Support = true,
                        Visible = visible.Contains(healer.Position) || visible.Contains(healed.Position),
                        Color = new Color(0.22f, 0.88f, 0.56f),
                        Title = "敌军医疗支援",
                        Detail = $"{UnitLabel(healed)}恢复 {healing} 生命。",
                        History = $"红方{UnitLabel(healer)}治疗{UnitLabel(healed)}：+{healing}。",
                        HealingAtTo = healing
                    };
                case AttackWallCommand wallAttack:
                    var wallAttacker = state.Units[wallAttack.AttackerId];
                    var wall = state.CityWalls[wallAttack.WallId];
                    var wallPreview = simulation.Walls.Preview(state, wallAttacker, wall);
                    HexCoord? garrisonPosition = null;
                    if (wallPreview.GarrisonUnitId.HasValue &&
                        state.Units.TryGetValue(wallPreview.GarrisonUnitId.Value, out var wallGarrison))
                    {
                        garrisonPosition = wallGarrison.Position;
                    }
                    return new ActionFeedback
                    {
                        UnitId = wallAttacker.Id,
                        AttackerType = wallAttacker.Type,
                        TargetUnitId = wallPreview.GarrisonUnitId,
                        TargetType = wallPreview.GarrisonUnitId.HasValue &&
                                     state.Units.TryGetValue(wallPreview.GarrisonUnitId.Value, out var targetGarrison)
                            ? targetGarrison.Type
                            : (UnitType?)null,
                        WallId = wall.Id,
                        From = wallAttacker.Position,
                        To = wall.Position,
                        Attack = true,
                        Visible = visible.Contains(wallAttacker.Position) || visible.Contains(wall.Position),
                        Color = new Color(1f, 0.62f, 0.12f),
                        Title = "敌军攻击城墙",
                        Detail = wallPreview.GarrisonDamage > 0
                            ? $"城墙受到 {wallPreview.Damage}，驻军受到 {wallPreview.GarrisonDamage} 并被压制；反击 {wallPreview.CounterDamage}。"
                            : $"城墙受到 {wallPreview.Damage} 伤害；敌军承受 {wallPreview.CounterDamage} 反击。",
                        History = $"红方{UnitLabel(wallAttacker)}攻击城墙：墙{wallPreview.Damage}/驻军{wallPreview.GarrisonDamage}/反击{wallPreview.CounterDamage}。",
                        DamageAtTo = wallPreview.Damage,
                        CounterDamageAtFrom = wallPreview.CounterDamage,
                        SecondaryTo = garrisonPosition,
                        SecondaryDamageAtTo = wallPreview.GarrisonDamage
                    };
                case MoveCommand move:
                    var mover = state.Units[move.UnitId];
                    simulation.Movement.CanMove(state, mover, move.Destination, out var plannedPath);
                    return new ActionFeedback
                    {
                        UnitId = mover.Id,
                        AttackerType = mover.Type,
                        From = mover.Position,
                        To = move.Destination,
                        Path = plannedPath?.Cells,
                        Visible = visible.Contains(mover.Position),
                        Color = new Color(1f, 0.42f, 0.24f),
                        Title = "发现敌军机动",
                        Detail = $"{UnitLabel(mover)}移动至{move.Destination}。",
                        History = $"红方{UnitLabel(mover)}向{move.Destination}机动。"
                    };
                case GarrisonCommand garrison:
                    var unit = state.Units[garrison.UnitId];
                    return new ActionFeedback
                    {
                        To = unit.Position,
                        Visible = visible.Contains(unit.Position),
                        Color = new Color(1f, 0.68f, 0.18f),
                        Title = "敌军驻扎",
                        Detail = "该单位提高防御并扩大城市控制范围。",
                        History = $"红方{UnitLabel(unit)}执行驻扎。"
                    };
                case OccupyCityCommand occupy:
                    var city = state.Cities[occupy.CityId];
                    return new ActionFeedback
                    {
                        To = city.Center,
                        Visible = visible.Contains(city.Center),
                        Color = new Color(1f, 0.34f, 0.20f),
                        Title = "敌军占领城市",
                        Detail = "城市、控制区与残存城墙已经易主。",
                        History = "红方完成城市占领。"
                    };
                case PromoteUnitCommand promote:
                    var promoted = state.Units[promote.UnitId];
                    return new ActionFeedback
                    {
                        UnitId = promoted.Id,
                        To = promoted.Position,
                        Visible = visible.Contains(promoted.Position),
                        Color = new Color(1f, 0.78f, 0.18f),
                        Title = "敌军晋升",
                        Detail = $"{UnitLabel(promoted)}晋升至 L{promoted.Level + 1}。",
                        History = $"红方{UnitLabel(promoted)}晋升至L{promoted.Level + 1}。"
                    };
                default:
                    return new ActionFeedback { History = "红方完成行动。" };
            }
        }

        private void FinalizeComputerFeedback(GameState state, ActionFeedback feedback, GameSimulation simulation)
        {
            if (feedback.UnitId.HasValue)
                feedback.AttackerDestroyed = !state.Units.ContainsKey(feedback.UnitId.Value);
            if (feedback.TargetUnitId.HasValue)
                feedback.TargetDestroyed = !state.Units.ContainsKey(feedback.TargetUnitId.Value);
            if (feedback.WallId.HasValue && state.CityWalls.TryGetValue(feedback.WallId.Value, out var actedWall))
                feedback.WallDestroyed = actedWall.Health <= 0;
            if (!feedback.To.HasValue) return;
            var visibleAfter = simulation.Visibility.CalculateVisibleCells(state, HumanNationId);
            feedback.Visible = feedback.Visible || visibleAfter.Contains(feedback.To.Value);
        }

        private void Refresh()
        {
            _humanIncome = _game.Simulation.Economy.CalculateIncome(_game.State, HumanNationId);
            HoveredCombatPreview = null;
            HoveredWallCombatPreview = null;
            HoveredCombatDefender = null;
            _legalMoves.Clear();
            _legalMovePaths.Clear();
            _legalTargets.Clear();
            _legalHealTargets.Clear();
            _legalWallTargetIds.Clear();
            _legalWallTargetsByCell.Clear();
            _selectableUnitIds.Clear();
            _supplyReach.Clear();
            _enemyControlReach.Clear();
            _visibleCells.Clear();
            _supplyPath = null;
            foreach (var coord in _game.Simulation.Visibility.CalculateVisibleCells(_game.State, HumanNationId))
                _visibleCells.Add(coord);
            var selected = SelectedUnit;
            if (selected == null || selected.NationId != HumanNationId || WinnerNationId != 0)
            {
                _selectedUnitId = null;
                selected = null;
            }
            else if (!selected.CanActThisTurn && !_game.Simulation.CanPromote(selected))
            {
                _selectedUnitId = null;
                selected = null;
            }

            if (selected != null && CanHumanAct())
            {
                if (selected.RemainingMovement > 0)
                {
                    foreach (var pair in _game.Simulation.Movement.FindReachablePaths(_game.State, selected))
                    {
                        _legalMoves.Add(pair.Key);
                        _legalMovePaths[pair.Key] = pair.Value;
                    }
                }

                if (selected.CanAttackThisTurn)
                {
                    var visible = VisibleCells;
                    foreach (var enemy in _game.State.Units.Values)
                    {
                        var wall = _game.Simulation.Walls.FindWallAt(_game.State, enemy.Position);
                        var shielded = wall != null && wall.Health > 0 &&
                                       _game.State.Cities.TryGetValue(wall.CityId, out var wallCity) &&
                                       wallCity.NationId == enemy.NationId;
                        if (enemy.NationId != HumanNationId && visible.Contains(enemy.Position) && !shielded &&
                            _game.Simulation.Combat.Preview(_game.State, selected, enemy).Damage > 0)
                        {
                            _legalTargets.Add(enemy.Position);
                        }
                    }

                    if (_game.Simulation.Rules.HasAbility(selected.Type, UnitAbility.Healing))
                    {
                        foreach (var ally in _game.State.Units.Values)
                        {
                            if (_game.Simulation.Medical.CanHeal(_game.State, selected, ally))
                                _legalHealTargets.Add(ally.Position);
                        }
                    }

                    foreach (var wall in _game.State.CityWalls.Values)
                    {
                        var occupiedByFriendly = _game.State.Map.TryGet(wall.InnerPosition, out var wallCell) &&
                                                 wallCell.UnitId.HasValue &&
                                                 _game.State.Units.TryGetValue(wallCell.UnitId.Value, out var occupant) &&
                                                 occupant.NationId == HumanNationId;
                        if (wall.Health > 0 && visible.Contains(wall.InnerPosition) &&
                            !occupiedByFriendly &&
                            _game.State.Cities.TryGetValue(wall.CityId, out var city) && city.NationId != HumanNationId &&
                            _game.Simulation.Walls.Preview(_game.State, selected, wall).Damage > 0)
                        {
                            _legalWallTargetIds.Add(wall.Id);
                            _legalWallTargetsByCell[wall.InnerPosition] = wall.Id;
                        }
                    }
                }

                _supplyPath = null;
                if (selected.RemainingMovement > 0)
                {
                    foreach (var reachable in _game.Simulation.Supply.CalculateSupplyReach(_game.State, HumanNationId))
                        _supplyReach.Add(reachable);
                    foreach (var visibleCoord in _visibleCells)
                    {
                        if (_game.Simulation.Control.HasEnemyControl(_game.State, visibleCoord, HumanNationId))
                            _enemyControlReach.Add(visibleCoord);
                    }
                }
            }

            if (CanHumanAct())
            {
                foreach (var unit in _game.State.Units.Values)
                {
                    if (unit.NationId == HumanNationId && unit.Health > 0 &&
                        unit.CanActThisTurn)
                    {
                        _selectableUnitIds.Add(unit.Id);
                    }
                }
            }

            _mapView.Build(_game.State, HumanNationId, _visibleCells, selected?.Position, selected?.Id,
                _selectableUnitIds,
                _legalMoves, _legalTargets, _legalHealTargets, _legalWallTargetIds, _supplyReach,
                _enemyControlReach, _supplyPath,
                selected == null ? 0 : GetSupplyStatus(selected).Tier);
            if (_cameraFocusRequested && selected != null && CanHumanAct())
            {
                var radius = 1;
                foreach (var coord in _legalMoves) radius = System.Math.Max(radius, selected.Position.DistanceTo(coord));
                foreach (var coord in _legalTargets) radius = System.Math.Max(radius, selected.Position.DistanceTo(coord));
                foreach (var coord in _legalHealTargets) radius = System.Math.Max(radius, selected.Position.DistanceTo(coord));
                foreach (var wallId in _legalWallTargetIds)
                {
                    var wall = _game.State.CityWalls[wallId];
                    radius = System.Math.Max(radius, selected.Position.DistanceTo(wall.InnerPosition));
                }
                _mapView.FocusOn(selected.Position, radius);
                _cameraFocusRequested = false;
            }
            else if (_cameraFocusRequested)
            {
                _mapView.FocusOverview();
                _cameraFocusRequested = false;
            }
        }

        private void HandleCellHovered(HexCoord? coord)
        {
            _hoveredWallId = null;
            HoveredCoord = coord;
            HoveredCombatPreview = null;
            HoveredWallCombatPreview = null;
            HoveredCombatDefender = null;
            var hasFriendlyUnit = coord.HasValue && _game.State.Map.TryGet(coord.Value, out var hoveredCell) &&
                                  hoveredCell.UnitId.HasValue &&
                                  _game.State.Units.TryGetValue(hoveredCell.UnitId.Value, out var hoveredUnit) &&
                                  hoveredUnit.NationId == HumanNationId;
            if (coord.HasValue && !hasFriendlyUnit && TryGetVisibleHostileWallAt(coord.Value, out var wall))
            {
                _hoveredWallId = wall.Id;
                if (SelectedUnit != null && _legalWallTargetsByCell.ContainsKey(coord.Value))
                    HoveredWallCombatPreview = _game.Simulation.Walls.Preview(_game.State, SelectedUnit, wall);
                HoverText = DescribeHover(coord.Value);
                return;
            }
            if (coord.HasValue && SelectedUnit != null && _legalTargets.Contains(coord.Value) &&
                _game.State.Map.TryGet(coord.Value, out var targetCell) && targetCell.UnitId.HasValue &&
                _game.State.Units.TryGetValue(targetCell.UnitId.Value, out var defender))
            {
                HoveredCombatDefender = defender;
                HoveredCombatPreview = _game.Simulation.Combat.Preview(_game.State, SelectedUnit, defender);
            }
            HoverText = coord.HasValue ? DescribeHover(coord.Value) : string.Empty;
        }

        private string DescribeHover(HexCoord coord)
        {
            if (!_game.State.Map.TryGet(coord, out var cell)) return string.Empty;
            var containsFriendlyUnit = cell.UnitId.HasValue &&
                                       _game.State.Units.TryGetValue(cell.UnitId.Value, out var occupant) &&
                                       occupant.NationId == HumanNationId;
            if (!containsFriendlyUnit && TryGetVisibleHostileWallAt(coord, out var wall))
            {
                return _legalWallTargetsByCell.ContainsKey(coord) && HoveredWallCombatPreview != null
                    ? HoveredWallCombatPreview.GarrisonDamage > 0
                        ? $"炮击预览：城墙 {HoveredWallCombatPreview.Damage}，驻军 {HoveredWallCombatPreview.GarrisonDamage}并压制，反击 {HoveredWallCombatPreview.CounterDamage}；右键攻击"
                        : $"突破预览：伤害 {HoveredWallCombatPreview.Damage}，反击 {HoveredWallCombatPreview.CounterDamage}；右键该格攻击"
                    : $"城市边防　耐久 {wall.Health}/{wall.MaxHealth}";
            }
            if (cell.UnitId.HasValue && _game.State.Units.TryGetValue(cell.UnitId.Value, out var unit))
            {
                if (unit.NationId == HumanNationId)
                {
                    if (SelectedUnit != null && _legalHealTargets.Contains(coord))
                    {
                        var amount = _game.Simulation.Medical.Preview(_game.State, SelectedUnit, unit);
                        return $"医疗目标：预计恢复 {amount} 生命；右键治疗";
                    }
                    var action = unit.IsPinnedByEnemyControl ? "移动被截停，攻击未消耗" :
                        !unit.CanActThisTurn ? "本回合行动完毕" : "左键选择";
                    return $"{UnitLabel(unit)}　生命 {unit.Health}　行动力 {unit.RemainingMovement}　{SupplyLabel(_game.Simulation.Supply.GetStatus(_game.State, unit))}　{action}";
                }

                if (VisibleCells.Contains(coord))
                {
                    if (SelectedUnit != null && _legalTargets.Contains(coord))
                    {
                        var preview = _game.Simulation.Combat.Preview(_game.State, SelectedUnit, unit);
                        var counter = preview.CanCounter ? $"反击 {preview.CounterDamage}" : $"无反击：{preview.CounterBlockedReason}";
                        return $"攻击预览：伤害 {preview.Damage}，{counter}；右键攻击";
                    }
                    return $"敌方{UnitLabel(unit)}　生命 {unit.Health}";
                }
            }

            if (SelectedUnit != null && _legalMovePaths.TryGetValue(coord, out var path))
            {
                return $"可移动：消耗 {path.Cost}　{TerrainLabel(cell.Terrain)}";
            }

            if (cell.CityId.HasValue && _game.State.Cities.TryGetValue(cell.CityId.Value, out var hoveredCity))
            {
                var owner = hoveredCity.NationId == HumanNationId ? "己方" : "敌方";
                return $"{owner} {hoveredCity.Level} 级市中心" + (hoveredCity.IsDisabled ? "（失控，等待占领结算）" : string.Empty);
            }

            if (cell.BuildingId.HasValue &&
                _game.State.Buildings.TryGetValue(cell.BuildingId.Value, out var hoveredBuilding))
            {
                var owner = hoveredBuilding.NationId == HumanNationId ? "己方" : "敌方";
                var type = hoveredBuilding.Type == BuildingType.MilitaryFactory ? "工厂" :
                    hoveredBuilding.Type == BuildingType.CivilEnterprise ? "民营企业" : "研究院";
                var operational = _game.Simulation.Economy.IsBuildingOperational(_game.State, hoveredBuilding,
                    hoveredBuilding.NationId);
                return $"{owner}{type} L{hoveredBuilding.Level}　" + (operational ? "正常运转" : "已经停产");
            }

            return $"{TerrainLabel(cell.Terrain)}　归属：{OwnerLabel(cell.OwnerNationId)}";
        }

        private bool TryGetVisibleHostileWallAt(HexCoord coord, out CityWallState wall)
        {
            wall = _game.Simulation.Walls.FindWallAt(_game.State, coord);
            var occupiedByFriendly = _game.State.Map.TryGet(coord, out var cell) && cell.UnitId.HasValue &&
                                     _game.State.Units.TryGetValue(cell.UnitId.Value, out var unit) &&
                                     unit.NationId == HumanNationId;
            return !occupiedByFriendly && wall != null && wall.Health > 0 && VisibleCells.Contains(coord) &&
                   _game.State.Cities.TryGetValue(wall.CityId, out var city) && city.NationId != HumanNationId;
        }

        private void PublishResult(string title, string detail, HexCoord? coord)
        {
            ResultTitle = title;
            ResultDetail = detail;
            ResultCoord = coord;
            ResultVisibleUntil = Time.unscaledTime + 3.2f;
        }

        private bool CheckVictory()
        {
            var blueUnits = 0;
            var redUnits = 0;
            var blueCities = 0;
            var redCities = 0;
            foreach (var unit in _game.State.Units.Values)
            {
                if (unit.NationId == HumanNationId) blueUnits++;
                if (unit.NationId == ComputerNationId) redUnits++;
            }
            foreach (var city in _game.State.Cities.Values)
            {
                if (city.NationId == HumanNationId) blueCities++;
                if (city.NationId == ComputerNationId) redCities++;
            }

            if (redUnits == 0 || redCities == 0) WinnerNationId = HumanNationId;
            if (blueUnits == 0 || blueCities == 0) WinnerNationId = ComputerNationId;
            if (WinnerNationId != 0)
            {
                StatusText = WinnerNationId == HumanNationId ? "蓝方胜利。" : "红方胜利。";
                AddHistory(StatusText);
                return true;
            }

            return false;
        }

        private bool CanHumanAct()
        {
            return !_computerThinking && WinnerNationId == 0 && _game.State.ActiveNationId == HumanNationId;
        }

        private bool CanHumanInput()
        {
            return CanHumanAct() && !_presentationLocked;
        }

        private void BeginPresentationLock(float duration)
        {
            _presentationLocked = true;
            StartCoroutine(ReleasePresentationLock(duration));
        }

        private IEnumerator ReleasePresentationLock(float duration)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0.12f, duration));
            _presentationLocked = false;
        }

        private void AddHistory(string text)
        {
            _history.Insert(0, text);
            if (_history.Count > 6) _history.RemoveAt(_history.Count - 1);
        }

        public static string UnitLabel(UnitState unit)
        {
            if (unit == null) return "单位";
            return $"{UnitTypeLabel(unit.Type)}#{unit.Id}";
        }

        public static string UnitTypeLabel(UnitType type)
        {
            return type switch
            {
                UnitType.MainInfantry => "主战步兵",
                UnitType.Medic => "医疗兵",
                UnitType.LightArmor => "轻装甲",
                UnitType.LightArtillery => "轻火炮",
                _ => type.ToString()
            };
        }

        public static string TerrainLabel(TerrainType terrain)
        {
            return terrain switch
            {
                TerrainType.Plain => "平原",
                TerrainType.Forest => "森林",
                TerrainType.Hill => "丘陵",
                TerrainType.Mountain => "山地",
                TerrainType.Marsh => "沼泽",
                _ => terrain.ToString()
            };
        }

        private static string OwnerLabel(int nationId)
        {
            if (nationId == HumanNationId) return "蓝方";
            if (nationId == ComputerNationId) return "红方";
            return "中立";
        }

        private sealed class ActionFeedback
        {
            public int? UnitId { get; set; }
            public UnitType AttackerType { get; set; }
            public int? TargetUnitId { get; set; }
            public UnitType? TargetType { get; set; }
            public int? WallId { get; set; }
            public HexCoord? From { get; set; }
            public HexCoord? To { get; set; }
            public IReadOnlyList<HexCoord> Path { get; set; }
            public Vector3? TargetWorld { get; set; }
            public bool Attack { get; set; }
            public bool Support { get; set; }
            public bool Visible { get; set; }
            public Color Color { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
            public string History { get; set; } = string.Empty;
            public int DamageAtTo { get; set; }
            public int CounterDamageAtFrom { get; set; }
            public HexCoord? SecondaryTo { get; set; }
            public int SecondaryDamageAtTo { get; set; }
            public int HealingAtTo { get; set; }
            public bool TargetDestroyed { get; set; }
            public bool AttackerDestroyed { get; set; }
            public bool WallDestroyed { get; set; }
        }

        private sealed class PlannedComputerAction
        {
            public GameCommand Command { get; set; }
            public string DecisionTrace { get; set; } = string.Empty;
            public ActionFeedback Feedback { get; set; }
        }

        private sealed class ComputerTurnPlan
        {
            public List<PlannedComputerAction> Actions { get; } = new List<PlannedComputerAction>();
            public List<string> SkippedCommands { get; } = new List<string>();
            public GameState FinalState { get; set; }
            public string TerminalReason { get; set; } = string.Empty;
        }

        private void OnDestroy()
        {
            FlushAiReplay();
            if (_mapView != null)
            {
                _mapView.CellClicked -= HandleCellClicked;
                _mapView.CellRightClicked -= HandleCellRightClicked;
                _mapView.CellHovered -= HandleCellHovered;
                _mapView.BackgroundClicked -= ReturnToOverview;
            }
        }
    }
}

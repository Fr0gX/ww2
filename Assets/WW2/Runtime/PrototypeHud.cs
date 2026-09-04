using UnityEngine;
using WW2.Core.Model;
using WW2.Core.Rules;
using WW2.Core.Systems;

namespace WW2.Runtime
{
    public sealed class PrototypeHud : MonoBehaviour
    {
        private static readonly Color Panel = new Color(0.030f, 0.048f, 0.058f, 0.965f);
        private static readonly Color PanelRaised = new Color(0.047f, 0.070f, 0.082f, 0.98f);
        private static readonly Color Line = new Color(0.26f, 0.34f, 0.36f, 0.62f);
        private static readonly Color Ink = new Color(0.94f, 0.95f, 0.91f);
        private static readonly Color Muted = new Color(0.57f, 0.64f, 0.63f);
        private static readonly Color Cyan = new Color(0.18f, 0.72f, 0.76f);
        private static readonly Color Gold = new Color(0.88f, 0.62f, 0.22f);
        private static readonly Color Blue = new Color(0.12f, 0.38f, 0.62f);
        private static readonly Color Red = new Color(0.66f, 0.20f, 0.15f);

        private GameBootstrap _game;
        private PrototypeGameController _controller;
        private GUIStyle _displayStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _smallStyle;
        private GUIStyle _microStyle;
        private GUIStyle _unitStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _strongBadgeStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _secondaryButtonStyle;
        private GUIStyle _panelStyle;
        private Font _uiFont;
        private Texture2D _chipTexture;

        public void Initialize(GameBootstrap game, PrototypeGameController controller)
        {
            _game = game;
            _controller = controller;
        }

        private void OnGUI()
        {
            if (_game?.State == null || _controller == null) return;
            EnsureStyles();
            if (_uiFont != null) GUI.skin.font = _uiFont;
            DrawMapLabels();
            DrawTopBar();
            DrawContextCard();
            DrawCombatPreview();
            DrawResultToast();
            DrawHoverRibbon();
            DrawVictory();
        }

        private void DrawTopBar()
        {
            var width = Mathf.Min(760f, Screen.width - 40f);
            var rect = new Rect(20f, 18f, width, 64f);
            DrawPanel(rect);
            DrawSolid(new Rect(rect.x, rect.y + 10f, 4f, rect.height - 20f), Blue);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 7f, 190f, 20f), "战区指挥部", _microStyle);
            GUI.Label(new Rect(rect.x + 20f, rect.y + 28f, 180f, 27f), $"第 {_game.State.Round} 轮", _titleStyle);

            var humanTurn = _game.State.ActiveNationId == PrototypeGameController.HumanNationId &&
                            !_controller.ComputerThinking;
            var readyText = humanTurn
                ? $"{_controller.ReadyUnitCount} / {_controller.HumanUnitCount}  可行动"
                : "敌军行动中";
            DrawChip(new Rect(rect.x + 152f, rect.y + 18f, 148f, 30f), readyText,
                humanTurn && _controller.ReadyUnitCount > 0 ? Cyan : Muted, true);

            var nation = _controller.HumanNation;
            var income = _controller.HumanIncome;
            DrawChip(new Rect(rect.x + 308f, rect.y + 18f, 108f, 30f),
                $"经济 {nation?.Economy ?? 0}  +{income.Economy}", new Color(0.16f, 0.55f, 0.38f), false);
            DrawChip(new Rect(rect.x + 424f, rect.y + 18f, 108f, 30f),
                $"工业 {nation?.Industry ?? 0}  +{income.Industry}", new Color(0.53f, 0.42f, 0.20f), false);

            var buttonX = rect.xMax - 210f;
            GUI.enabled = _controller.WinnerNationId == 0 &&
                          (humanTurn || _controller.ComputerThinking && !_controller.SkipComputerPresentation);
            var actionLabel = _controller.ComputerThinking
                ? _controller.SkipComputerPresentation ? "结算中" : "跳过演出"
                : "结束回合";
            if (GUI.Button(new Rect(buttonX, rect.y + 14f, 118f, 36f), actionLabel, _buttonStyle))
            {
                if (_controller.ComputerThinking) _controller.SkipComputerTurnPresentation();
                else _controller.EndPlayerTurn();
            }
            GUI.enabled = true;
            if (GUI.Button(new Rect(buttonX + 126f, rect.y + 14f, 76f, 36f), "重开", _secondaryButtonStyle))
                _controller.Restart();
        }

        private void DrawContextCard()
        {
            var selected = _controller.SelectedUnit;
            if (_controller.InspectedWall != null && _controller.InspectedUnit != null)
            {
                DrawWallDefenderCard(_controller.InspectedWall, _controller.InspectedUnit);
                return;
            }
            var displayed = _controller.InspectedUnit ?? selected;
            if (displayed != null)
            {
                DrawUnitCard(displayed, selected != null && selected.Id == displayed.Id);
                return;
            }
            if (_controller.InspectedWall != null)
            {
                DrawWallCard(_controller.InspectedWall);
                return;
            }
            if (_controller.InspectedBuilding != null)
            {
                DrawBuildingCard(_controller.InspectedBuilding);
                return;
            }
            if (_controller.InspectedCity != null) DrawCityCard(_controller.InspectedCity);
        }

        private void DrawWallDefenderCard(CityWallState wall, UnitState defender)
        {
            var rect = new Rect(20f, 96f, 338f, 238f);
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), Gold);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 230f, 25f), "城市边防 + 守军", _titleStyle);
            DrawChip(new Rect(rect.xMax - 82f, rect.y + 10f, 66f, 24f), "联合目标", Red, false);

            GUI.Label(new Rect(rect.x + 18f, rect.y + 47f, 54f, 18f), "边防", _microStyle);
            DrawHealthBar(new Rect(rect.x + 70f, rect.y + 47f, 248f, 16f), wall.Health, wall.MaxHealth, true);
            var definition = _game.Simulation.Rules.Unit(defender.Type);
            var maximum = RuleMath.Round(definition.MaxHealth * RuleMath.LevelMultiplier(defender.Level));
            GUI.Label(new Rect(rect.x + 18f, rect.y + 75f, 130f, 18f),
                PrototypeGameController.UnitTypeLabel(defender.Type), _microStyle);
            DrawHealthBar(new Rect(rect.x + 150f, rect.y + 75f, 168f, 16f), defender.Health, maximum, true);

            var selected = _controller.SelectedUnit;
            var preview = selected == null ? null : _game.Simulation.Walls.Preview(_game.State, selected, wall);
            if (preview != null && preview.Damage > 0)
            {
                DrawChip(new Rect(rect.x + 18f, rect.y + 108f, 92f, 28f), $"墙 -{preview.Damage}", Red, false);
                DrawChip(new Rect(rect.x + 118f, rect.y + 108f, 92f, 28f),
                    preview.GarrisonDamage > 0 ? $"军 -{preview.GarrisonDamage}" : "守军 0", Red, false);
                DrawChip(new Rect(rect.x + 218f, rect.y + 108f, 100f, 28f),
                    $"反击 {preview.CounterDamage}", Gold, false);
                GUI.Label(new Rect(rect.x + 18f, rect.y + 146f, 300f, 20f),
                    $"城墙反击 {preview.BaseCounterDamage} + 守军反击 {preview.GarrisonCounterDamage}", _smallStyle);
                GUI.Label(new Rect(rect.x + 18f, rect.y + 170f, 300f, 34f),
                    _game.Simulation.Rules.HasAbility(selected.Type, UnitAbility.Suppression)
                        ? "火炮同时压制边防与守军；双方按射程与视野正常反击。"
                        : "普通单位攻击边防；城墙与守军分别反击后合计伤害。", _smallStyle);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 18f, rect.y + 118f, 300f, 42f),
                    "该格同时包含边防与守军；选择可攻击单位后查看联合战果。", _smallStyle);
            }
        }

        private void DrawUnitCard(UnitState unit, bool controlled)
        {
            var friendly = unit.NationId == PrototypeGameController.HumanNationId;
            var hasPanelAction = false;
            if (controlled)
            {
                var canOccupy = _controller.CanOccupySelected(out _);
                hasPanelAction = _controller.CanPromoteSelected() || canOccupy || _controller.CanGarrisonSelected();
            }
            var rect = new Rect(20f, 96f, 338f, controlled ? hasPanelAction ? 326f : 270f : 218f);
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), friendly ? Blue : Red);
            var promotable = _game.Simulation.CanPromote(unit);
            var promotion = unit.Level >= 4
                ? "满级"
                : $"{unit.PromotionKills}/{RuleMath.KillsRequiredForPromotion(unit.Level)}杀";
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 225f, 25f),
                $"{PrototypeGameController.UnitTypeLabel(unit.Type)} L{unit.Level}  #{unit.Id} · {promotion}", _titleStyle);
            DrawChip(new Rect(rect.xMax - 94f, rect.y + 10f, 78f, 24f),
                controlled ? promotable ? "可以升级" : "正在行动" : !friendly ? "敌方目标" : promotable ? "可以升级" :
                    unit.IsPinnedByEnemyControl ? "可攻击" :
                    unit.CanActThisTurn ? "可行动" : "行动完毕",
                promotable ? Gold : controlled ? Gold : unit.CanActThisTurn ? Cyan : Muted, false);

            var definition = _game.Simulation.Rules.Unit(unit.Type);
            var maxHealth = RuleMath.Round(definition.MaxHealth * RuleMath.LevelMultiplier(unit.Level));
            GUI.Label(new Rect(rect.x + 18f, rect.y + 45f, 60f, 18f), "兵力", _microStyle);
            DrawHealthBar(new Rect(rect.x + 66f, rect.y + 46f, 252f, 16f), unit.Health, maxHealth, true);
            var supply = _controller.GetSupplyStatus(unit);
            var effectiveMaximum = Mathf.Max(1,
                RuleMath.Round(definition.Movement * supply.MovementMultiplier));
            effectiveMaximum = Mathf.Max(effectiveMaximum, unit.RemainingMovement);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 75f, 60f, 18f), "行动", _microStyle);
            DrawActionPointBar(new Rect(rect.x + 66f, rect.y + 72f, 252f, 22f),
                unit.RemainingMovement, effectiveMaximum);

            var stats = _game.Simulation.Combat.GetEffectiveStats(_game.State, unit, supply);
            DrawStatBlock(new Rect(rect.x + 18f, rect.y + 108f, 146f, 48f), "攻击力",
                stats.EffectiveAttack, stats.BaseAttack, new Color(0.84f, 0.26f, 0.13f));
            DrawStatBlock(new Rect(rect.x + 172f, rect.y + 108f, 146f, 48f), "防御力",
                stats.EffectiveDefense, stats.BaseDefense, new Color(0.12f, 0.46f, 0.67f));
            DrawSupplyBadge(new Rect(rect.x + 18f, rect.y + 169f, 146f, 24f), supply);
            DrawChip(new Rect(rect.x + 172f, rect.y + 169f, 146f, 24f),
                unit.CanAttackThisTurn ? unit.IsPinnedByEnemyControl ? "受牵制 · 可攻击" : "攻击可用" : "攻击已使用",
                unit.CanAttackThisTurn ? new Color(0.94f, 0.40f, 0.16f) : Muted, false);
            if (!controlled) return;

            var support = _game.Simulation.Combat.CountSupport(_game.State, unit);
            var garrison = stats.GarrisonMultiplier;
            var maximumRange = RuleMath.EffectiveMaxRange(definition.MaxRange, unit.Level);
            var modifierText = $"射程 {definition.MinRange}–{maximumRange}  ·  " +
                               (support > 0 ? $"支援防御 +{Mathf.Min(2, support) * 25}%" : "无邻接支援");
            if (unit.IsGarrisoned) modifierText += $"  ·  驻扎 ×{garrison:0.0}";
            if (unit.IsSuppressed) modifierText += "  ·  已受压制";
            var correction = $"兵力 ×{stats.HealthMultiplier * 100f:0}%  ·  补给 ×{stats.SupplyMultiplier * 100f:0}%  ·  地形 ×{stats.TerrainDefenseMultiplier:0.00}";
            if (stats.SuppressionMultiplier < 1f) correction += $"  ·  压制 ×{stats.SuppressionMultiplier:0.00}";
            GUI.Label(new Rect(rect.x + 18f, rect.y + 204f, 302f, 18f), correction, _smallStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 224f, 302f, 18f), modifierText, _smallStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 248f, 302f, 18f),
                $"移动 {_controller.LegalMoveCount} 格   攻击 {_controller.LegalTargetCount + _controller.LegalWallTargetCount}   治疗 {_controller.LegalHealTargetCount}",
                _smallStyle);
            DrawSelectedUnitActions(new Rect(rect.x + 18f, rect.y + 280f, 302f, 32f));
        }

        private void DrawSelectedUnitActions(Rect rect)
        {
            if (_controller.SelectedUnit == null || _controller.ComputerThinking || _controller.WinnerNationId != 0)
                return;
            var canPromote = _controller.CanPromoteSelected();
            var canOccupy = _controller.CanOccupySelected(out _);
            var canGarrison = !canOccupy && _controller.CanGarrisonSelected();
            var hasTacticalAction = canOccupy || canGarrison;
            if (!canPromote && !hasTacticalAction) return;
            var gap = canPromote && hasTacticalAction ? 8f : 0f;
            var width = canPromote && hasTacticalAction ? (rect.width - gap) * 0.5f : rect.width;
            var x = rect.x;
            if (canPromote)
            {
                if (GUI.Button(new Rect(x, rect.y, width, rect.height),
                        $"升级至 L{_controller.SelectedUnit.Level + 1}", _buttonStyle))
                    _controller.PromoteSelected();
                x += width + gap;
            }
            if (!hasTacticalAction) return;
            if (GUI.Button(new Rect(x, rect.y, width, rect.height), canOccupy ? "占领城市" : "驻扎",
                    canOccupy ? _buttonStyle : _secondaryButtonStyle))
            {
                if (canOccupy) _controller.OccupySelected();
                else _controller.GarrisonSelected();
            }
        }

        private void DrawWallCard(CityWallState wall)
        {
            var rect = new Rect(20f, 96f, 318f, 150f);
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), Gold);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 210f, 25f), "城市边防", _titleStyle);
            DrawHealthBar(new Rect(rect.x + 18f, rect.y + 46f, 280f, 17f), wall.Health, wall.MaxHealth, true);
            var selected = _controller.SelectedUnit;
            var preview = selected == null ? null : _game.Simulation.Walls.Preview(_game.State, selected, wall);
            if (preview != null && preview.Damage > 0)
            {
                if (preview.GarrisonDamage > 0)
                {
                    DrawChip(new Rect(rect.x + 18f, rect.y + 78f, 86f, 24f), $"墙 -{preview.Damage}", Red, false);
                    DrawChip(new Rect(rect.x + 110f, rect.y + 78f, 86f, 24f), $"军 -{preview.GarrisonDamage}", Red, false);
                    DrawChip(new Rect(rect.x + 202f, rect.y + 78f, 96f, 24f), $"反击 {preview.CounterDamage}", Gold, false);
                }
                else
                {
                    DrawChip(new Rect(rect.x + 18f, rect.y + 78f, 128f, 24f), $"预计伤害 {preview.Damage}", Red, false);
                    DrawChip(new Rect(rect.x + 154f, rect.y + 78f, 144f, 24f), $"反击 {preview.CounterDamage}", Gold, false);
                }
                GUI.Label(new Rect(rect.x + 18f, rect.y + 112f, 280f, 18f),
                    $"基础防御 {preview.BaseDefense}  ·  驻军贡献 {preview.GarrisonDefense}  ·  联合防御", _smallStyle);
            }
            else
            {
                GUI.Label(new Rect(rect.x + 18f, rect.y + 80f, 280f, 38f),
                    "该边界格只有一份边防耐久；摧毁后可从任一外侧进入。", _smallStyle);
            }
        }

        private void DrawCityCard(CityState city)
        {
            var friendly = city.NationId == PrototypeGameController.HumanNationId;
            var rect = new Rect(20f, 96f, 338f, friendly ? 220f : 142f);
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), city.NationId == 1 ? Blue : Red);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 190f, 25f), $"{city.Level} 级城市", _titleStyle);
            DrawChip(new Rect(rect.xMax - 82f, rect.y + 10f, 66f, 24f), city.NationId == 1 ? "蓝方" : "红方",
                city.NationId == 1 ? Blue : Red, false);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 48f, 300f, 30f),
                city.IsDisabled ? "市中心已失控，等待占领结算" : "控制区、生产与边防运转正常", _smallStyle);
            var baseIncome = _game.Simulation.Economy.GetCityBaseEconomy(city);
            var tradeIncome = _game.Simulation.Economy.GetCityTradeEconomy(_game.State, city);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 78f, 300f, 22f),
                $"经济产出  基础 +{(city.IsDisabled ? 0 : baseIncome)}  ·  国内贸易 +{tradeIncome}", _smallStyle);
            if (!friendly) return;
            GUI.Label(new Rect(rect.x + 18f, rect.y + 108f, 300f, 18f), "城市征募 · 单位当回合不可行动", _microStyle);
            DrawProductionButton(new Rect(rect.x + 18f, rect.y + 132f, 145f, 62f), UnitType.MainInfantry);
            DrawProductionButton(new Rect(rect.x + 173f, rect.y + 132f, 145f, 62f), UnitType.Medic);
        }

        private void DrawBuildingCard(BuildingState building)
        {
            var friendly = building.NationId == PrototypeGameController.HumanNationId;
            var factory = building.Type == BuildingType.MilitaryFactory;
            var rect = new Rect(20f, 96f, 338f, friendly && factory ? 220f : 150f);
            DrawPanel(rect, PanelRaised);
            var accent = factory ? Gold : new Color(0.18f, 0.65f, 0.52f);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 210f, 25f),
                factory ? $"{building.Level} 级工厂" : $"{building.Level} 级民营企业", _titleStyle);
            DrawChip(new Rect(rect.xMax - 82f, rect.y + 10f, 66f, 24f), friendly ? "蓝方" : "红方",
                friendly ? Blue : Red, false);
            var operational = _game.Simulation.Economy.IsBuildingOperational(_game.State, building, building.NationId);
            var output = operational ? _game.Simulation.Economy.GetBuildingOutput(building) : 0;
            GUI.Label(new Rect(rect.x + 18f, rect.y + 48f, 300f, 26f),
                operational ? factory ? $"工业产出 +{output} / 回合" : $"经济产出 +{output} / 回合"
                    : "建筑已停产：城市失控或建筑被敌军占据", _smallStyle);
            GUI.Label(new Rect(rect.x + 18f, rect.y + 78f, 300f, 20f),
                factory ? "工厂负责即时制造装甲与火炮。" : "企业直接增加国家经济收入。", _smallStyle);
            if (!friendly || !factory) return;
            GUI.Label(new Rect(rect.x + 18f, rect.y + 108f, 300f, 18f), "工厂制造 · 单位当回合不可行动", _microStyle);
            DrawProductionButton(new Rect(rect.x + 18f, rect.y + 132f, 145f, 62f), UnitType.LightArtillery);
            DrawProductionButton(new Rect(rect.x + 173f, rect.y + 132f, 145f, 62f), UnitType.LightArmor);
        }

        private void DrawProductionButton(Rect rect, UnitType type)
        {
            var definition = _game.Simulation.Rules.Unit(type);
            var canProduce = _controller.CanProduce(type, out _);
            GUI.enabled = canProduce;
            if (GUI.Button(new Rect(rect.x, rect.y, rect.width, 34f),
                    $"制造 {PrototypeGameController.UnitTypeLabel(type)}", _buttonStyle))
                _controller.Produce(type);
            GUI.enabled = true;
            GUI.Label(new Rect(rect.x, rect.y + 38f, rect.width, 20f),
                definition.IndustryCost > 0
                    ? $"经济 {definition.EconomyCost}  ·  工业 {definition.IndustryCost}"
                    : $"经济 {definition.EconomyCost}", _microStyle);
        }

        private void DrawCombatPreview()
        {
            var wallPreview = _controller.HoveredWallCombatPreview;
            var preview = _controller.HoveredCombatPreview;
            if (wallPreview == null && preview == null) return;
            const float width = 456f;
            var x = Mathf.Max(356f, (Screen.width - width) * 0.5f);
            var rect = new Rect(x, 96f, width, 92f);
            DrawPanel(rect, PanelRaised);
            var damage = wallPreview?.Damage ?? preview.Damage;
            var counter = wallPreview?.CounterDamage ?? preview.CounterDamage;
            GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, 126f, 20f), "攻击结果预览", _microStyle);
            if (wallPreview != null && wallPreview.GarrisonDamage > 0)
            {
                DrawChip(new Rect(rect.x + 18f, rect.y + 34f, 126f, 36f), $"墙体  -{damage}", Red, true);
                DrawChip(new Rect(rect.x + 152f, rect.y + 34f, 126f, 36f), $"驻军  -{wallPreview.GarrisonDamage}", Red, true);
                DrawChip(new Rect(rect.x + 286f, rect.y + 34f, 152f, 36f), $"反击  {counter}  ·  压制", Gold, true);
                return;
            }
            DrawChip(new Rect(rect.x + 18f, rect.y + 34f, 126f, 36f), $"造成  {damage}", Red, true);
            DrawChip(new Rect(rect.x + 152f, rect.y + 34f, 126f, 36f), $"反击  {counter}", Gold, true);
            var detail = wallPreview != null
                ? $"边防 {wallPreview.BaseDefense}\n驻军贡献 {wallPreview.GarrisonDefense}"
                : preview.SuppressionChance > 0f
                    ? $"夹击 +{(preview.FlankingMultiplier - 1f) * 100f:0}%\n确定施加压制"
                    : $"夹击 +{(preview.FlankingMultiplier - 1f) * 100f:0}%\n敌方支援 +{(preview.SupportMultiplier - 1f) * 100f:0}%";
            GUI.Label(new Rect(rect.x + 294f, rect.y + 32f, 144f, 42f), detail, _smallStyle);
        }

        private void DrawResultToast()
        {
            if (string.IsNullOrEmpty(_controller.ResultTitle) || Time.unscaledTime > _controller.ResultVisibleUntil) return;
            var width = 330f;
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 94f, width, 42f);
            var remaining = _controller.ResultVisibleUntil - Time.unscaledTime;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(remaining * 2f));
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 5f, rect.height), Cyan);
            GUI.Label(new Rect(rect.x + 16f, rect.y + 4f, 96f, 18f), _controller.ResultTitle, _labelStyle);
            GUI.Label(new Rect(rect.x + 112f, rect.y + 5f, 204f, 30f), _controller.ResultDetail, _smallStyle);
            GUI.color = old;
        }

        private void DrawHoverRibbon()
        {
            if (string.IsNullOrEmpty(_controller.HoverText)) return;
            var width = Mathf.Min(620f, Screen.width - 380f);
            if (width < 260f) return;
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 42f, width, 28f);
            DrawPanel(rect, new Color(0.045f, 0.062f, 0.072f, 0.94f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 3f, rect.width - 24f, 22f), _controller.HoverText, _smallStyle);
        }

        private void DrawMapLabels()
        {
            var camera = Camera.main;
            if (camera == null) return;
            var visible = _controller.VisibleCells;
            foreach (var unit in _game.State.Units.Values)
            {
                if (unit.NationId != PrototypeGameController.HumanNationId && !visible.Contains(unit.Position)) continue;
                if (_controller.IsUnitAnimating(unit.Id)) continue;
                var point = camera.WorldToScreenPoint(HexMapView.ToWorld(unit.Position) + Vector3.up * 1.12f);
                if (point.z <= 0f) continue;
                var friendly = unit.NationId == PrototypeGameController.HumanNationId;
                var emphasized = _controller.SelectedUnitId == unit.Id ||
                                 (_controller.HoveredCoord.HasValue && _controller.HoveredCoord.Value.Equals(unit.Position));
                var type = ShortUnitType(unit.Type);
                if (emphasized)
                {
                    var definition = _game.Simulation.Rules.Unit(unit.Type);
                    var maxHealth = RuleMath.Round(definition.MaxHealth * RuleMath.LevelMultiplier(unit.Level));
                    var rect = new Rect(point.x - 50f, Screen.height - point.y - 16f, 100f, friendly ? 47f : 34f);
                    DrawPanel(rect, new Color(0.045f, 0.063f, 0.074f, 0.96f));
                    DrawSolid(new Rect(rect.x, rect.y, 4f, rect.height), friendly ? Blue : Red);
                    GUI.Label(new Rect(rect.x + 5f, rect.y + 2f, 48f, 18f), $"{type} #{unit.Id}", _unitStyle);
                    DrawHealthBar(new Rect(rect.x + 52f, rect.y + 7f, 41f, 7f), unit.Health, maxHealth, false);
                    if (friendly)
                    {
                        DrawChip(new Rect(rect.x + 7f, rect.y + 24f, 86f, 17f),
                            unit.IsPinnedByEnemyControl ? "移动截停 · 攻击保留" :
                                unit.HasUnspentAttackAfterControlStop ? "攻击未消耗" :
                                unit.RemainingMovement > 0 ? $"AP  {unit.RemainingMovement}" : "行动完毕",
                            unit.CanActThisTurn ? Cyan : Muted, false);
                    }
                }
                else
                {
                    var ready = friendly && unit.CanActThisTurn;
                    var rect = new Rect(point.x - 27f, Screen.height - point.y - 8f, 54f, 20f);
                    DrawRounded(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height),
                        new Color(0f, 0f, 0f, 0.36f));
                    DrawRounded(rect, friendly ? ready ? new Color(0.06f, 0.30f, 0.40f, 0.96f) :
                        new Color(0.16f, 0.18f, 0.19f, 0.92f) : new Color(0.43f, 0.10f, 0.08f, 0.94f));
                    DrawSolid(new Rect(rect.x + 2f, rect.y + 5f, 2f, rect.height - 10f),
                        ready ? Cyan : friendly ? Muted : Red);
                    GUI.Label(rect, friendly
                        ? unit.IsPinnedByEnemyControl ? "攻击可用" : $"AP  {unit.RemainingMovement}"
                        : $"HP  {unit.Health}", _unitStyle);
                }
            }

            foreach (var city in _game.State.Cities.Values)
            {
                // Place the badge below the footprint so the large L2/L3 city
                // remains a single, uninterrupted silhouette.
                var point = camera.WorldToScreenPoint(HexMapView.ToWorld(city.Center) + Vector3.up * 0.40f);
                if (point.z <= 0f) continue;
                var occupiedCenter = _game.State.Map.TryGet(city.Center, out var centerCell) && centerCell.UnitId.HasValue;
                var rect = new Rect(point.x - 34f + (occupiedCenter ? 54f : 0f),
                    Screen.height - point.y + 8f, 68f, 18f);
                var color = city.IsDisabled ? Gold : city.NationId == 1 ? Blue : Red;
                DrawRounded(new Rect(rect.x + 2f, rect.y + 3f, rect.width, rect.height),
                    new Color(0f, 0f, 0f, 0.36f));
                DrawRounded(rect, color);
                GUI.Label(rect, city.IsDisabled ? "城市失控" : $"{(city.NationId == 1 ? "蓝" : "红")}城  L{city.Level}", _badgeStyle);
            }

            foreach (var wall in _game.State.CityWalls.Values)
            {
                var focused = _controller.InspectedWall?.Id == wall.Id ||
                              _controller.HoveredWallId == wall.Id;
                if (!focused) continue;
                var point = camera.WorldToScreenPoint(HexMapView.WallWorldPosition(wall) + Vector3.up * 0.48f);
                if (point.z <= 0f) continue;
                var rect = new Rect(point.x - 26f, Screen.height - point.y - 7f, 52f, 15f);
                DrawRounded(rect, wall.Health <= 0 ? new Color(0.18f, 0.19f, 0.19f, 0.92f) :
                    wall.Health < wall.MaxHealth ? Gold : Red);
                GUI.Label(rect, wall.Health <= 0 ? "缺口" : $"边防 {wall.Health}", _badgeStyle);
            }
        }

        private void DrawVictory()
        {
            if (_controller.WinnerNationId == 0) return;
            var rect = new Rect((Screen.width - 390f) * 0.5f, (Screen.height - 150f) * 0.5f, 390f, 150f);
            DrawPanel(rect, PanelRaised);
            DrawSolid(new Rect(rect.x, rect.y, 6f, rect.height), Gold);
            GUI.Label(new Rect(rect.x + 32f, rect.y + 24f, 326f, 38f),
                _controller.WinnerNationId == 1 ? "蓝方胜利" : "红方胜利", _displayStyle);
            GUI.Label(new Rect(rect.x + 32f, rect.y + 66f, 326f, 24f), "战役目标已经达成", _labelStyle);
            if (GUI.Button(new Rect(rect.x + 128f, rect.y + 104f, 134f, 32f), "重新开始", _buttonStyle))
                _controller.Restart();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null) return;
            _uiFont = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI", "Arial" }, 16);
            if (_uiFont != null) GUI.skin.font = _uiFont;
            var panelTexture = MakeRoundedTexture(64, 64, 13f, Color.white, new Color(1f, 1f, 1f, 0.88f), 2f);
            var primary = MakeRoundedTexture(64, 40, 11f,
                new Color(0.075f, 0.36f, 0.43f), new Color(0.24f, 0.69f, 0.72f), 2f);
            var primaryHover = MakeRoundedTexture(64, 40, 11f,
                new Color(0.10f, 0.45f, 0.52f), new Color(0.35f, 0.81f, 0.82f), 2f);
            var secondary = MakeRoundedTexture(64, 40, 11f,
                new Color(0.12f, 0.16f, 0.17f), new Color(0.30f, 0.37f, 0.37f), 2f);
            var secondaryHover = MakeRoundedTexture(64, 40, 11f,
                new Color(0.17f, 0.22f, 0.23f), new Color(0.42f, 0.49f, 0.48f), 2f);
            _chipTexture = MakeRoundedTexture(64, 32, 10f, Color.white,
                new Color(1f, 1f, 1f, 0.82f), 2f);
            _displayStyle = CreateStyle(26, FontStyle.Bold, TextAnchor.MiddleCenter, Ink);
            _titleStyle = CreateStyle(18, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            _labelStyle = CreateStyle(14, FontStyle.Bold, TextAnchor.MiddleLeft, Ink);
            _smallStyle = CreateStyle(12, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.78f, 0.82f, 0.80f));
            _smallStyle.wordWrap = true;
            _microStyle = CreateStyle(11, FontStyle.Bold, TextAnchor.MiddleLeft, Muted);
            _unitStyle = CreateStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _badgeStyle = CreateStyle(11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _strongBadgeStyle = CreateStyle(14, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            _buttonStyle = CreateButtonStyle(primary, primaryHover);
            _secondaryButtonStyle = CreateButtonStyle(secondary, secondaryHover);
            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelTexture },
                border = new RectOffset(16, 16, 16, 16),
                padding = new RectOffset(16, 16, 14, 14)
            };
        }

        private static GUIStyle CreateStyle(int size, FontStyle style, TextAnchor alignment, Color color)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = style,
                alignment = alignment,
                normal = { textColor = color }
            };
        }

        private static GUIStyle CreateButtonStyle(Texture2D normalTexture, Texture2D hoverTexture)
        {
            return new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(14, 14, 14, 14),
                padding = new RectOffset(12, 12, 7, 7),
                normal = { textColor = Color.white, background = normalTexture },
                hover = { textColor = Color.white, background = hoverTexture },
                active = { textColor = Color.white, background = hoverTexture },
                focused = { textColor = Color.white, background = normalTexture },
                onNormal = { textColor = Color.white, background = normalTexture },
                onHover = { textColor = Color.white, background = hoverTexture }
            };
        }

        private void DrawActionPointBar(Rect rect, int current, int maximum)
        {
            maximum = Mathf.Clamp(maximum, 1, 12);
            current = Mathf.Clamp(current, 0, maximum);
            DrawRounded(rect, new Color(0.020f, 0.030f, 0.034f, 0.94f));
            const float gap = 2f;
            var width = (rect.width - gap * (maximum + 1)) / maximum;
            for (var i = 0; i < maximum; i++)
            {
                DrawSolid(new Rect(rect.x + gap + i * (width + gap), rect.y + 3f, width, rect.height - 6f),
                    i < current ? Cyan : new Color(0.17f, 0.21f, 0.22f));
            }
            GUI.Label(rect, $"AP  {current} / {maximum}", _badgeStyle);
        }

        private void DrawSupplyBadge(Rect rect, SupplyStatus status)
        {
            if (status == null) return;
            var color = status.Tier == 0 ? new Color(0.12f, 0.55f, 0.33f) : status.Tier == 1
                ? new Color(0.82f, 0.46f, 0.08f) : new Color(0.66f, 0.10f, 0.12f);
            var text = status.Tier == 0 ? "补给正常" : status.Tier == 1
                ? $"补给异常 · {status.ConsecutiveTurnsWithoutSupply}/3"
                : "补给极度异常";
            DrawChip(rect, text, color, false);
        }

        private void DrawStatBlock(Rect rect, string label, int effective, int baseValue, Color color)
        {
            DrawRounded(rect, new Color(0.022f, 0.036f, 0.041f, 0.96f));
            DrawSolid(new Rect(rect.x + 1f, rect.y + 8f, 3f, rect.height - 16f), color);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 4f, 56f, 16f), label, _microStyle);
            GUI.Label(new Rect(rect.x + 66f, rect.y + 3f, 62f, 28f), effective.ToString(), _strongBadgeStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 27f, 118f, 16f), $"基础 {baseValue}  →  当前 {effective}", _microStyle);
        }

        private void DrawHealthBar(Rect rect, int health, int maxHealth, bool showText)
        {
            var ratio = maxHealth <= 0 ? 0f : Mathf.Clamp01(health / (float)maxHealth);
            DrawRounded(rect, new Color(0.020f, 0.030f, 0.034f, 0.94f));
            var color = ratio > 0.60f ? new Color(0.20f, 0.72f, 0.38f) : ratio > 0.30f ? Gold : Red;
            if (ratio > 0f)
                DrawRounded(new Rect(rect.x + 2f, rect.y + 2f, (rect.width - 4f) * ratio, rect.height - 4f), color);
            if (!showText) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            GUI.Label(rect, $"{health} / {maxHealth}", style);
        }

        private void DrawChip(Rect rect, string text, Color color, bool strong)
        {
            var old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, strong ? 0.96f : 0.82f);
            GUI.DrawTexture(rect, _chipTexture, ScaleMode.StretchToFill, true);
            GUI.color = old;
            GUI.Label(rect, text, strong ? _strongBadgeStyle : _badgeStyle);
        }

        private void DrawPanel(Rect rect, Color? color = null)
        {
            var tint = color ?? Panel;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.30f);
            GUI.Box(new Rect(rect.x + 5f, rect.y + 7f, rect.width, rect.height), GUIContent.none, _panelStyle);
            GUI.color = tint;
            GUI.Box(rect, GUIContent.none, _panelStyle);
            GUI.color = old;
            DrawSolid(new Rect(rect.x + 14f, rect.y + 1f, rect.width - 28f, 1f),
                new Color(0.72f, 0.80f, 0.78f, 0.18f));
        }

        private void DrawRounded(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _chipTexture, ScaleMode.StretchToFill, true);
            GUI.color = old;
        }

        private static void DrawSolid(Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static Texture2D MakeRoundedTexture(int width, int height, float radius, Color fill,
            Color border, float borderWidth)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Runtime matte rounded UI",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[width * height];
            var halfWidth = width * 0.5f;
            var halfHeight = height * 0.5f;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var qx = Mathf.Abs(x + 0.5f - halfWidth) - (halfWidth - radius);
                var qy = Mathf.Abs(y + 0.5f - halfHeight) - (halfHeight - radius);
                var outsideX = Mathf.Max(qx, 0f);
                var outsideY = Mathf.Max(qy, 0f);
                var distance = Mathf.Min(Mathf.Max(qx, qy), 0f) +
                               Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY) - radius;
                var coverage = Mathf.Clamp01(0.75f - distance);
                if (coverage <= 0f)
                {
                    pixels[y * width + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                var borderBlend = Mathf.Clamp01(-distance / Mathf.Max(0.01f, borderWidth));
                var vertical = y / Mathf.Max(1f, height - 1f);
                var shadedFill = Color.Lerp(fill * 0.94f, fill, vertical);
                shadedFill.a = fill.a;
                var pixel = Color.Lerp(border, shadedFill, borderBlend);
                pixel.a *= coverage;
                pixels[y * width + x] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static string ShortUnitType(UnitType type)
        {
            return type switch
            {
                UnitType.MainInfantry => "步",
                UnitType.Medic => "医",
                UnitType.LightArmor => "甲",
                UnitType.LightArtillery => "炮",
                _ => "兵"
            };
        }
    }
}

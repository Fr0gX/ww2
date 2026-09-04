using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class CombatPreview
    {
        public int Damage { get; set; }
        public int CounterDamage { get; set; }
        public bool AppliesSuppression { get; set; }
        public float SuppressionChance { get; set; }
        public bool CanCounter { get; set; }
        public string CounterBlockedReason { get; set; } = string.Empty;
        public int FlankingCount { get; set; }
        public int SupportCount { get; set; }
        public float FlankingMultiplier { get; set; } = 1f;
        public float SupportMultiplier { get; set; } = 1f;
        public float AttackerHealthRatio { get; set; }
        public float CounterHealthRatio { get; set; }
    }

    public sealed class EffectiveCombatStats
    {
        public int BaseAttack { get; set; }
        public int BaseDefense { get; set; }
        public int EffectiveAttack { get; set; }
        public int EffectiveDefense { get; set; }
        public float HealthMultiplier { get; set; }
        public float SupplyMultiplier { get; set; }
        public float TerrainDefenseMultiplier { get; set; }
        public float SupportMultiplier { get; set; }
        public float GarrisonMultiplier { get; set; }
        public float SuppressionMultiplier { get; set; }
    }

    public sealed class CombatSystem
    {
        // Attack is the damage scale; defense reduces it with diminishing returns.
        // A defense of 10 halves incoming damage, without imposing a global damage cap.
        public const float DefenseScale = 10f;
        private readonly RulesCatalog _rules;
        private readonly SupplySystem _supply;

        public CombatSystem(RulesCatalog rules, SupplySystem supply)
        {
            _rules = rules;
            _supply = supply;
        }

        public CombatPreview Preview(GameState state, UnitState attacker, UnitState defender)
        {
            var distance = attacker.Position.DistanceTo(defender.Position);
            var attackDefinition = _rules.Unit(attacker.Type);
            var defenseDefinition = _rules.Unit(defender.Type);
            if (distance < attackDefinition.MinRange ||
                distance > RuleMath.EffectiveMaxRange(attackDefinition.MaxRange, attacker.Level))
            {
                return new CombatPreview();
            }

            var flankingCount = FlankingCount(state, attacker, defender);
            var supportCount = SupportCount(state, defender);
            var damage = CalculateAttackDamage(state, attacker, defender);
            var remainingDefenderHealth = System.Math.Max(0, defender.Health - damage);
            var suppressionChance = _rules.HasAbility(attacker.Type, UnitAbility.Suppression) ? 1f : 0f;
            var appliesSuppression = remainingDefenderHealth > 0 && suppressionChance > 0f;
            var counterReason = CounterBlockedReason(state, attacker, defender, defenseDefinition, distance,
                remainingDefenderHealth);
            var canCounter = string.IsNullOrEmpty(counterReason);
            // Both sides use their state at the start of the engagement. A lethal hit
            // cancels retaliation, while a survivor retaliates with its prepared defense.
            var counter = canCounter ? CalculateCounterDamage(state, defender, attacker) : 0;

            return new CombatPreview
            {
                Damage = damage,
                CounterDamage = counter,
                AppliesSuppression = appliesSuppression,
                SuppressionChance = suppressionChance,
                CanCounter = canCounter,
                CounterBlockedReason = counterReason,
                FlankingCount = flankingCount,
                SupportCount = supportCount,
                FlankingMultiplier = 1f + System.Math.Min(2, flankingCount) * 0.25f,
                SupportMultiplier = 1f + System.Math.Min(2, supportCount) * 0.25f,
                AttackerHealthRatio = HealthRatio(attacker, attacker.Health),
                CounterHealthRatio = HealthRatio(defender, defender.Health)
            };
        }

        public CombatPreview Resolve(GameState state, UnitState attacker, UnitState defender)
        {
            var preview = Preview(state, attacker, defender);
            defender.Health = System.Math.Max(0, defender.Health - preview.Damage);
            if (defender.Health > 0)
            {
                attacker.Health = System.Math.Max(0, attacker.Health - preview.CounterDamage);
            }

            if (preview.AppliesSuppression && defender.Health > 0)
            {
                defender.IsSuppressed = true;
                defender.IsGarrisoned = false;
            }

            attacker.HasAttacked = true;
            attacker.IsPinnedByEnemyControl = false;
            attacker.HasUnspentAttackAfterControlStop = false;
            return preview;
        }

        public int CountSupport(GameState state, UnitState unit)
        {
            return SupportCount(state, unit);
        }

        public EffectiveCombatStats GetEffectiveStats(GameState state, UnitState unit, SupplyStatus knownSupply = null)
        {
            var definition = _rules.Unit(unit.Type);
            var level = RuleMath.LevelMultiplier(unit.Level);
            var health = HealthRatio(unit, unit.Health);
            var status = knownSupply ?? _supply.GetStatus(state, unit);
            var terrain = TerrainDefenseMultiplier(unit.Type,
                _rules.Terrain(state.Map.Get(unit.Position).Terrain).DefenseMultiplier);
            var support = SupportMultiplier(state, unit);
            var garrison = GetGarrisonMultiplier(state, unit);
            var suppression = unit.IsSuppressed ? 0.60f : 1f;
            return new EffectiveCombatStats
            {
                BaseAttack = RuleMath.Round(definition.Attack * level),
                BaseDefense = RuleMath.Round(definition.Defense * level),
                EffectiveAttack = RuleMath.Round(definition.Attack * level * health * status.AttackMultiplier),
                EffectiveDefense = RuleMath.Round(definition.Defense * level * health * status.DefenseMultiplier *
                                                  terrain * support * garrison * suppression),
                HealthMultiplier = health,
                SupplyMultiplier = status.AttackMultiplier,
                TerrainDefenseMultiplier = terrain,
                SupportMultiplier = support,
                GarrisonMultiplier = garrison,
                SuppressionMultiplier = suppression
            };
        }

        private int CalculateAttackDamage(GameState state, UnitState attacker, UnitState defender)
        {
            var attackerDefinition = _rules.Unit(attacker.Type);
            var effectiveAttack = attackerDefinition.Attack
                                  * RuleMath.LevelMultiplier(attacker.Level)
                                  * HealthRatio(attacker, attacker.Health)
                                  * _supply.GetStatus(state, attacker).AttackMultiplier
                                  * FlankingMultiplier(state, attacker, defender);
            var effectiveDefense = EffectiveDefenseForce(state, defender);
            return CalculateRatioDamage(effectiveAttack, effectiveDefense, defender.Health);
        }

        private int CalculateCounterDamage(GameState state, UnitState defender, UnitState attacker)
        {
            var counterForce = EffectiveDefenseForce(state, defender);
            var attackerDefense = EffectiveDefenseForce(state, attacker);
            return CalculateRatioDamage(counterForce, attackerDefense, attacker.Health);
        }

        private float EffectiveDefenseForce(GameState state, UnitState unit)
        {
            var definition = _rules.Unit(unit.Type);
            var terrain = TerrainDefenseMultiplier(unit.Type,
                _rules.Terrain(state.Map.Get(unit.Position).Terrain).DefenseMultiplier);
            return definition.Defense
                   * RuleMath.LevelMultiplier(unit.Level)
                   * HealthRatio(unit, unit.Health)
                   * _supply.GetStatus(state, unit).DefenseMultiplier
                   * terrain
                   * SupportMultiplier(state, unit)
                   * GetGarrisonMultiplier(state, unit)
                   * (unit.IsSuppressed ? 0.60f : 1f);
        }

        public static int CalculateRatioDamage(float attackForce, float defenseForce, int targetHealth)
        {
            if (attackForce <= 0f || targetHealth <= 0) return 0;
            var rounded = System.Math.Max(1,
                RuleMath.Round(attackForce * DefenseScale / (DefenseScale + System.Math.Max(0f, defenseForce))));
            return System.Math.Min(targetHealth, rounded);
        }

        private string CounterBlockedReason(GameState state, UnitState attacker, UnitState defender,
            UnitDefinition defenderDefinition, int distance, int remainingDefenderHealth)
        {
            if (remainingDefenderHealth <= 0) return "目标被消灭";
            if (_rules.HasAbility(attacker.Type, UnitAbility.PreventsCounterattack)) return "该攻击不触发反击";
            if (_rules.HasAbility(defender.Type, UnitAbility.CannotCounterattack)) return "该单位无法反击";
            if (IsAttackingOutFromFriendlyWall(state, attacker, defender)) return "攻击者受到己方城墙掩护";
            if (distance < defenderDefinition.MinRange ||
                distance > RuleMath.EffectiveMaxRange(defenderDefinition.MaxRange, defender.Level))
                return "超出反击射程";
            if (distance > defenderDefinition.Vision) return "攻击者不在反击视野";
            return string.Empty;
        }

        private static bool IsAttackingOutFromFriendlyWall(GameState state, UnitState attacker, UnitState defender)
        {
            if (attacker.Position.DistanceTo(defender.Position) != 1) return false;
            foreach (var wall in state.CityWalls.Values)
            {
                if (wall.Health <= 0 || !wall.InnerPosition.Equals(attacker.Position) ||
                    !state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId != attacker.NationId)
                    continue;
                if (city.Center.DistanceTo(defender.Position) > city.Level) return true;
            }
            return false;
        }

        public float GetGarrisonMultiplier(GameState state, UnitState unit)
        {
            if (!unit.IsGarrisoned) return 1f;
            if (_rules.HasAbility(unit.Type, UnitAbility.GarrisonExpert))
            {
                foreach (var wall in state.CityWalls.Values)
                    if (wall.InnerPosition.Equals(unit.Position)) return 2.5f;
            }
            return 1.5f;
        }

        private float HealthRatio(UnitState unit, int health)
        {
            var definition = _rules.Unit(unit.Type);
            var maxHealth = RuleMath.Round(definition.MaxHealth * RuleMath.LevelMultiplier(unit.Level));
            return maxHealth <= 0 ? 0f : System.Math.Max(0f, System.Math.Min(1f, health / (float)maxHealth));
        }

        private static float SupportMultiplier(GameState state, UnitState defender)
        {
            var count = SupportCount(state, defender);
            return 1f + System.Math.Min(2, count) * 0.25f;
        }

        private static int SupportCount(GameState state, UnitState defender)
        {
            var count = 0;
            foreach (var unit in state.Units.Values)
            {
                if (unit.Id != defender.Id && unit.NationId == defender.NationId && unit.Health > 0 &&
                    !unit.IsSuppressed && unit.Position.DistanceTo(defender.Position) == 1)
                {
                    count++;
                }
            }

            return count;
        }

        private static float FlankingMultiplier(GameState state, UnitState attacker, UnitState defender)
        {
            var count = FlankingCount(state, attacker, defender);
            return 1f + System.Math.Min(2, count) * 0.25f;
        }

        private static int FlankingCount(GameState state, UnitState attacker, UnitState defender)
        {
            var count = 0;
            foreach (var unit in state.Units.Values)
            {
                if (unit.Id != attacker.Id && unit.NationId == attacker.NationId && unit.Health > 0 &&
                    unit.Position.DistanceTo(defender.Position) == 1)
                {
                    count++;
                }
            }

            return count;
        }

        private float TerrainDefenseMultiplier(UnitType type, float multiplier)
        {
            return _rules.Unit(type).Branch == UnitBranch.Infantry
                ? System.Math.Max(1f, multiplier)
                : multiplier;
        }
    }
}

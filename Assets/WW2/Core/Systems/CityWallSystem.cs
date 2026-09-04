using System.Collections.Generic;
using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class WallCombatPreview
    {
        public int Damage { get; set; }
        public int GarrisonDamage { get; set; }
        public int CounterDamage { get; set; }
        public int BaseDefense { get; set; }
        public int GarrisonDefense { get; set; }
        public int BaseCounterAttack { get; set; }
        public int GarrisonCounterAttack { get; set; }
        public int BaseCounterDamage { get; set; }
        public int GarrisonCounterDamage { get; set; }
        public int? GarrisonUnitId { get; set; }
        public bool AppliesSuppression { get; set; }
        public float SynergyMultiplier { get; set; } = 1f;
        public string CounterBlockedReason { get; set; } = string.Empty;
    }

    public sealed class CityWallSystem
    {
        private readonly RulesCatalog _rules;
        private readonly ControlSystem _control;
        private readonly CombatSystem _combat;

        public CityWallSystem(RulesCatalog rules, ControlSystem control, CombatSystem combat)
        {
            _rules = rules;
            _control = control;
            _combat = combat;
        }

        public CityWallState FindBlockingWall(GameState state, int nationId, IReadOnlyList<HexCoord> path)
        {
            for (var i = 1; i < path.Count; i++)
            {
                var wall = FindIntactBlockingEntry(state, nationId, path[i - 1], path[i]);
                if (wall != null) return wall;
            }

            return null;
        }

        /// <summary>
        /// Shared edge rule for movement and supply: a surviving hostile wall blocks
        /// entry from outside into its boundary cell. Friendly walls never block.
        /// </summary>
        public static CityWallState FindIntactBlockingEntry(GameState state, int nationId, HexCoord from,
            HexCoord to)
        {
            if (from.DistanceTo(to) != 1) return null;
            foreach (var wall in state.CityWalls.Values)
            {
                if (wall.Health <= 0 || !wall.InnerPosition.Equals(to) ||
                    !state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId == nationId ||
                    !IsOutside(city, from))
                {
                    continue;
                }
                return wall;
            }
            return null;
        }

        public CityWallState FindWallBetween(GameState state, HexCoord first, HexCoord second)
        {
            if (first.DistanceTo(second) != 1) return null;
            foreach (var wall in state.CityWalls.Values)
            {
                if (!state.Cities.TryGetValue(wall.CityId, out var city)) continue;
                if (wall.InnerPosition.Equals(first) && IsOutside(city, second) ||
                    wall.InnerPosition.Equals(second) && IsOutside(city, first)) return wall;
            }

            return null;
        }

        public CityWallState FindWallAt(GameState state, HexCoord innerPosition)
        {
            foreach (var wall in state.CityWalls.Values)
                if (wall.InnerPosition.Equals(innerPosition)) return wall;
            return null;
        }

        public bool IsEntryAcrossWall(GameState state, CityWallState wall, HexCoord from, HexCoord to)
        {
            return wall != null && from.DistanceTo(to) == 1 && to.Equals(wall.InnerPosition) &&
                   state.Cities.TryGetValue(wall.CityId, out var city) && IsOutside(city, from);
        }

        public IEnumerable<HexCoord> ExteriorNeighbors(GameState state, CityWallState wall)
        {
            if (wall == null || !state.Cities.TryGetValue(wall.CityId, out var city)) yield break;
            for (var direction = 0; direction < 6; direction++)
            {
                var neighbor = wall.InnerPosition.Neighbor(direction);
                if (state.Map.TryGet(neighbor, out _) && IsOutside(city, neighbor)) yield return neighbor;
            }
        }

        public WallCombatPreview Preview(GameState state, UnitState attacker, CityWallState wall)
        {
            if (!state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId == attacker.NationId ||
                wall.Health <= 0)
            {
                return new WallCombatPreview();
            }

            var attackerDefinition = _rules.Unit(attacker.Type);
            var distance = attacker.Position.DistanceTo(wall.InnerPosition);
            if (distance < attackerDefinition.MinRange ||
                distance > RuleMath.EffectiveMaxRange(attackerDefinition.MaxRange, attacker.Level))
            {
                return new WallCombatPreview();
            }

            var garrison = DefenderAt(state, city, wall);
            var baseDefense = 8 + city.Level * 4;
            var wallHealthRatio = wall.MaxHealth <= 0 ? 0f : wall.Health / (float)wall.MaxHealth;
            var wallDefense = baseDefense * wallHealthRatio;
            var garrisonDefense = 0;
            var garrisonCounter = 0;
            if (garrison != null)
            {
                garrisonDefense = _combat.GetEffectiveStats(state, garrison).EffectiveDefense;
                var definition = _rules.Unit(garrison.Type);
                var counterDistance = garrison.Position.DistanceTo(attacker.Position);
                if (!_rules.HasAbility(garrison.Type, UnitAbility.CannotCounterattack) &&
                    counterDistance >= definition.MinRange &&
                    counterDistance <= RuleMath.EffectiveMaxRange(definition.MaxRange, garrison.Level) &&
                    counterDistance <= definition.Vision)
                {
                    garrisonCounter = garrisonDefense;
                }
            }

            var effectiveAttack = _combat.GetEffectiveStats(state, attacker).EffectiveAttack;
            // Suppressive artillery applies the same shot to the wall and its occupant.
            // It receives every legal retaliation; suppression is not counter immunity.
            var artillery = _rules.HasAbility(attacker.Type, UnitAbility.Suppression);
            var effectiveDefense = artillery ? wallDefense : wallDefense + garrisonDefense;
            var damage = CombatSystem.CalculateRatioDamage(effectiveAttack, effectiveDefense, wall.Health);
            var garrisonDamage = artillery && garrison != null
                ? CombatSystem.CalculateRatioDamage(effectiveAttack, garrisonDefense, garrison.Health)
                : 0;
            var remainingGarrisonHealth = garrison == null ? 0 : garrison.Health - garrisonDamage;
            var appliesSuppression = artillery && garrison != null && remainingGarrisonHealth > 0;

            var baseCounter = 3 + city.Level;
            var counterDamage = 0;
            var baseCounterDamage = 0;
            var garrisonCounterDamage = 0;
            var blocked = string.Empty;
            var legalBaseCounter = distance == 1 ? baseCounter : 0;
            var wallCounter = damage >= wall.Health ? 0f : legalBaseCounter * wallHealthRatio;
            var survivingGarrisonCounter = remainingGarrisonHealth > 0 ? garrisonCounter : 0f;
            if (distance > 1 && survivingGarrisonCounter <= 0f) blocked = "墙体反击射程仅为 1 格";
            var attackerDefense = _combat.GetEffectiveStats(state, attacker).EffectiveDefense;
            baseCounterDamage = CombatSystem.CalculateRatioDamage(wallCounter, attackerDefense, attacker.Health);
            garrisonCounterDamage = CombatSystem.CalculateRatioDamage(survivingGarrisonCounter, attackerDefense,
                attacker.Health);
            counterDamage = System.Math.Min(attacker.Health, baseCounterDamage + garrisonCounterDamage);

            return new WallCombatPreview
            {
                Damage = damage,
                GarrisonDamage = garrisonDamage,
                CounterDamage = counterDamage,
                BaseDefense = baseDefense,
                GarrisonDefense = garrisonDefense,
                BaseCounterAttack = legalBaseCounter,
                GarrisonCounterAttack = garrisonCounter,
                BaseCounterDamage = baseCounterDamage,
                GarrisonCounterDamage = garrisonCounterDamage,
                GarrisonUnitId = garrison?.Id,
                AppliesSuppression = appliesSuppression,
                SynergyMultiplier = 1f,
                CounterBlockedReason = blocked
            };
        }

        public WallCombatPreview Resolve(GameState state, UnitState attacker, CityWallState wall)
        {
            var preview = Preview(state, attacker, wall);
            wall.Health = System.Math.Max(0, wall.Health - preview.Damage);
            if (preview.GarrisonUnitId.HasValue &&
                state.Units.TryGetValue(preview.GarrisonUnitId.Value, out var garrison))
            {
                garrison.Health = System.Math.Max(0, garrison.Health - preview.GarrisonDamage);
                if (garrison.Health > 0 && preview.AppliesSuppression)
                {
                    garrison.IsSuppressed = true;
                    garrison.IsGarrisoned = false;
                }
            }
            attacker.Health = System.Math.Max(0, attacker.Health - preview.CounterDamage);
            attacker.HasAttacked = true;
            attacker.IsPinnedByEnemyControl = false;
            attacker.HasUnspentAttackAfterControlStop = false;
            return preview;
        }

        public void Recover(GameState state, int nationId)
        {
            foreach (var wall in state.CityWalls.Values)
            {
                if (!state.Cities.TryGetValue(wall.CityId, out var city) || city.NationId != nationId ||
                    city.IsDisabled || wall.Health >= wall.MaxHealth ||
                    _control.HasEnemyControl(state, wall.InnerPosition, nationId))
                {
                    continue;
                }

                wall.Health = System.Math.Min(wall.MaxHealth, wall.Health + 2 + city.Level);
            }
        }

        public static void InitializeCityWalls(GameState state)
        {
            state.CityWalls.Clear();
            var id = 1;
            foreach (var city in state.Cities.Values)
            {
                var maxHealth = 14 + city.Level * 6;
                foreach (var pair in state.Map.Cells)
                {
                    if (city.Center.DistanceTo(pair.Key) != city.Level) continue;
                    var hasExteriorNeighbor = false;
                    for (var direction = 0; direction < 6; direction++)
                    {
                        var outside = pair.Key.Neighbor(direction);
                        if (state.Map.TryGet(outside, out _) && IsOutside(city, outside))
                        {
                            hasExteriorNeighbor = true;
                            break;
                        }
                    }
                    if (!hasExteriorNeighbor) continue;
                    state.CityWalls.Add(id, new CityWallState
                    {
                        Id = id,
                        CityId = city.Id,
                        InnerPosition = pair.Key,
                        Health = maxHealth,
                        MaxHealth = maxHealth
                    });
                    id++;
                }
            }
        }

        private static bool IsOutside(CityState city, HexCoord position)
        {
            return city.Center.DistanceTo(position) > city.Level;
        }

        private UnitState DefenderAt(GameState state, CityState city, CityWallState wall)
        {
            UnitState best = null;
            var bestValue = -1f;
            foreach (var unit in state.Units.Values)
            {
                if (unit.NationId != city.NationId || unit.Health <= 0 ||
                    !unit.Position.Equals(wall.InnerPosition)) continue;
                var value = _combat.GetEffectiveStats(state, unit).EffectiveDefense;
                if (value <= bestValue) continue;
                bestValue = value;
                best = unit;
            }
            return best;
        }
    }
}

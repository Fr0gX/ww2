using WW2.Core.Model;
using WW2.Core.Rules;

namespace WW2.Core.Systems
{
    public sealed class MedicalSystem
    {
        private readonly RulesCatalog _rules;

        public MedicalSystem(RulesCatalog rules)
        {
            _rules = rules;
        }

        public int Preview(GameState state, UnitState healer, UnitState target)
        {
            if (!CanHeal(state, healer, target)) return 0;
            var maximum = RuleMath.Round(_rules.Unit(target.Type).MaxHealth *
                                         RuleMath.LevelMultiplier(target.Level));
            return System.Math.Min(_rules.Unit(healer.Type).HealingAmount, maximum - target.Health);
        }

        public bool CanHeal(GameState state, UnitState healer, UnitState target)
        {
            if (healer == null || target == null || healer.Id == target.Id || healer.Health <= 0 ||
                target.Health <= 0 || healer.NationId != target.NationId || healer.HasAttacked ||
                healer.RemainingMovement <= 0 || healer.Position.DistanceTo(target.Position) != 1 ||
                !_rules.HasAbility(healer.Type, UnitAbility.Healing))
            {
                return false;
            }

            var maximum = RuleMath.Round(_rules.Unit(target.Type).MaxHealth *
                                         RuleMath.LevelMultiplier(target.Level));
            return target.Health < maximum;
        }

        public int Resolve(GameState state, UnitState healer, UnitState target)
        {
            var amount = Preview(state, healer, target);
            if (amount <= 0) return 0;
            target.Health += amount;
            healer.RemainingMovement = 0;
            healer.HasAttacked = true;
            healer.HasMoved = true;
            healer.IsPinnedByEnemyControl = false;
            healer.HasUnspentAttackAfterControlStop = false;
            return amount;
        }
    }
}

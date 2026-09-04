using System;

namespace WW2.Core.Model
{
    [Serializable]
    public sealed class UnitBranchDefinition
    {
        public UnitBranchDefinition(UnitBranch branch, UnitAbility abilities)
        {
            Branch = branch;
            Abilities = abilities;
        }

        public UnitBranch Branch { get; }
        public UnitAbility Abilities { get; }
    }

    [Serializable]
    public sealed class UnitDefinition
    {
        public UnitDefinition(UnitType type, UnitBranch branch, UnitAbility abilities, int maxHealth, int attack,
            int defense, int minRange, int maxRange, int movement, int vision, int economyCost, int industryCost,
            int productionTurns, int healingAmount = 0, float suppressionChance = 0f)
        {
            Type = type;
            Branch = branch;
            Abilities = abilities;
            MaxHealth = maxHealth;
            Attack = attack;
            Defense = defense;
            MinRange = minRange;
            MaxRange = maxRange;
            Movement = movement;
            Vision = vision;
            EconomyCost = economyCost;
            IndustryCost = industryCost;
            ProductionTurns = productionTurns;
            HealingAmount = healingAmount;
            SuppressionChance = suppressionChance;
        }

        public UnitType Type { get; }
        public UnitBranch Branch { get; }
        public UnitAbility Abilities { get; }
        public int MaxHealth { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MinRange { get; }
        public int MaxRange { get; }
        public int Movement { get; }
        public int Vision { get; }
        public int EconomyCost { get; }
        public int IndustryCost { get; }
        public int ProductionTurns { get; }
        public int HealingAmount { get; }
        public float SuppressionChance { get; }
    }

    [Serializable]
    public sealed class TerrainDefinition
    {
        public TerrainDefinition(TerrainType type, int footCost, int mechanicalCost, int supplyCost,
            float defenseMultiplier, float armorAttackMultiplier)
        {
            Type = type;
            FootCost = footCost;
            MechanicalCost = mechanicalCost;
            SupplyCost = supplyCost;
            DefenseMultiplier = defenseMultiplier;
            ArmorAttackMultiplier = armorAttackMultiplier;
        }

        public TerrainType Type { get; }
        public int FootCost { get; }
        public int MechanicalCost { get; }
        public int SupplyCost { get; }
        public float DefenseMultiplier { get; }
        public float ArmorAttackMultiplier { get; }
    }
}

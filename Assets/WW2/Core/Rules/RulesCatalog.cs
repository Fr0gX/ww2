using System.Collections.Generic;
using WW2.Core.Model;

namespace WW2.Core.Rules
{
    public sealed class RulesCatalog
    {
        private readonly Dictionary<UnitType, UnitDefinition> _units = new Dictionary<UnitType, UnitDefinition>();
        private readonly Dictionary<UnitBranch, UnitBranchDefinition> _branches =
            new Dictionary<UnitBranch, UnitBranchDefinition>();
        private readonly Dictionary<TerrainType, TerrainDefinition> _terrains = new Dictionary<TerrainType, TerrainDefinition>();

        public UnitDefinition Unit(UnitType type) => _units[type];
        public UnitBranchDefinition Branch(UnitBranch branch) => _branches[branch];
        public bool HasAbility(UnitType type, UnitAbility ability)
        {
            var unit = Unit(type);
            return ((unit.Abilities | Branch(unit.Branch).Abilities) & ability) != 0;
        }
        public TerrainDefinition Terrain(TerrainType type) => _terrains[type];
        // Unit roles come from their statistics and action rules. There is deliberately
        // no hidden rock-paper-scissors matchup table.
        public float Matchup(UnitType attacker, UnitType defender) => 1f;

        public static RulesCatalog CreateDefault()
        {
            var rules = new RulesCatalog();
            rules.AddBranch(new UnitBranchDefinition(UnitBranch.Infantry,
                UnitAbility.IgnoresTerrainMovement));
            rules.AddBranch(new UnitBranchDefinition(UnitBranch.Armor,
                UnitAbility.PreservesMovementAfterAttack));
            rules.AddBranch(new UnitBranchDefinition(UnitBranch.Artillery, UnitAbility.None));

            // First playable roster. Branch abilities define the shared combat language;
            // unit abilities define only the role unique to this concrete unit.
            rules.AddUnit(new UnitDefinition(UnitType.MainInfantry, UnitBranch.Infantry,
                UnitAbility.FormsControlZone | UnitAbility.RapidOccupation | UnitAbility.GarrisonExpert,
                12, 6, 6, 1, 1, 3, 2, 60, 0, 0));
            rules.AddUnit(new UnitDefinition(UnitType.Medic, UnitBranch.Infantry,
                UnitAbility.Healing,
                9, 3, 3, 1, 1, 3, 2, 80, 0, 0, healingAmount: 4));
            rules.AddUnit(new UnitDefinition(UnitType.LightArmor, UnitBranch.Armor,
                UnitAbility.None,
                20, 11, 11, 1, 1, 7, 3, 210, 126, 0));
            rules.AddUnit(new UnitDefinition(UnitType.LightArtillery, UnitBranch.Artillery,
                UnitAbility.Suppression,
                10, 10, 1, 1, 3, 4, 2, 120, 72, 0, suppressionChance: 1f));

            rules.AddTerrain(new TerrainDefinition(TerrainType.Plain, 1, 1, 1, 1f, 1f));
            rules.AddTerrain(new TerrainDefinition(TerrainType.Forest, 1, 3, 2, 1.20f, 1f));
            rules.AddTerrain(new TerrainDefinition(TerrainType.Hill, 1, 3, 2, 1.25f, 1f));
            rules.AddTerrain(new TerrainDefinition(TerrainType.Mountain, 1, 6, 3, 1.40f, 1f));
            rules.AddTerrain(new TerrainDefinition(TerrainType.Marsh, 1, 4, 3, 0.90f, 1f));

            return rules;
        }

        private void AddUnit(UnitDefinition definition) => _units.Add(definition.Type, definition);
        private void AddBranch(UnitBranchDefinition definition) => _branches.Add(definition.Branch, definition);
        private void AddTerrain(TerrainDefinition definition) => _terrains.Add(definition.Type, definition);
    }
}

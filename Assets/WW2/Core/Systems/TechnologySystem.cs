using System.Collections.Generic;
using WW2.Core.Model;

namespace WW2.Core.Systems
{
    public sealed class TechnologyDefinition
    {
        public TechnologyDefinition(string id, string prerequisiteId, int researchCost, int economyCost)
        {
            Id = id;
            PrerequisiteId = prerequisiteId;
            ResearchCost = researchCost;
            EconomyCost = economyCost;
        }

        public string Id { get; }
        public string PrerequisiteId { get; }
        public int ResearchCost { get; }
        public int EconomyCost { get; }
    }

    public sealed class TechnologySystem
    {
        private readonly Dictionary<string, TechnologyDefinition> _definitions = new Dictionary<string, TechnologyDefinition>();

        public TechnologySystem()
        {
            AddBranch("equipment");
            AddBranch("doctrine");
            AddBranch("logistics");
            AddBranch("industry_information");
        }

        public IReadOnlyDictionary<string, TechnologyDefinition> Definitions => _definitions;

        public bool TryComplete(NationState nation, string technologyId)
        {
            if (!_definitions.TryGetValue(technologyId, out var technology) || nation.Technologies.Contains(technologyId) ||
                nation.Research < technology.ResearchCost || nation.Economy < technology.EconomyCost ||
                (!string.IsNullOrEmpty(technology.PrerequisiteId) && !nation.Technologies.Contains(technology.PrerequisiteId)))
            {
                return false;
            }

            nation.Research -= technology.ResearchCost;
            nation.Economy -= technology.EconomyCost;
            nation.Technologies.Add(technologyId);
            return true;
        }

        private void AddBranch(string branch)
        {
            var first = $"{branch}.1";
            var second = $"{branch}.2";
            var third = $"{branch}.3";
            _definitions.Add(first, new TechnologyDefinition(first, null, 50, 100));
            _definitions.Add(second, new TechnologyDefinition(second, first, 100, 180));
            _definitions.Add(third, new TechnologyDefinition(third, second, 160, 300));
        }
    }
}


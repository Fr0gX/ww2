using WW2.Core.Model;

namespace WW2.Core.Systems
{
    public sealed class DiplomacySystem
    {
        public bool CanTrade(NationState nation, int otherNationId) => Relation(nation, otherNationId) >= 10;
        public bool CanRequestAccess(NationState nation, int otherNationId) => Relation(nation, otherNationId) >= 30;
        public bool CanCooperateOnTechnology(NationState nation, int otherNationId) => Relation(nation, otherNationId) >= 30;
        public bool CanFormAlliance(NationState nation, int otherNationId) => Relation(nation, otherNationId) >= 60;

        public void DeclareWar(NationState first, NationState second)
        {
            SetMutualRelation(first, second, -100);
            SetMutualState(first, second, DiplomaticState.War);
        }

        public void MakePeace(NationState first, NationState second)
        {
            SetMutualRelation(first, second, -20);
            SetMutualState(first, second, DiplomaticState.Peace);
        }

        public void SetMutualState(NationState first, NationState second, DiplomaticState state)
        {
            first.Diplomacy[second.Id] = state;
            second.Diplomacy[first.Id] = state;
        }

        public void SetMutualRelation(NationState first, NationState second, int relation)
        {
            relation = System.Math.Max(-100, System.Math.Min(100, relation));
            first.Relations[second.Id] = relation;
            second.Relations[first.Id] = relation;
        }

        private static int Relation(NationState nation, int otherNationId)
        {
            return nation.Relations.TryGetValue(otherNationId, out var relation) ? relation : 0;
        }
    }
}


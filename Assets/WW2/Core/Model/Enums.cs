namespace WW2.Core.Model
{
    public enum TerrainType
    {
        Plain,
        Forest,
        Hill,
        Mountain,
        Marsh
    }

    public enum UnitBranch
    {
        Infantry,
        Armor,
        Artillery
    }

    [System.Flags]
    public enum UnitAbility
    {
        None = 0,
        IgnoresTerrainMovement = 1 << 0,
        PreservesMovementAfterAttack = 1 << 1,
        IgnoresCityWall = 1 << 2,
        PreventsCounterattack = 1 << 3,
        FormsControlZone = 1 << 4,
        RapidOccupation = 1 << 5,
        GarrisonExpert = 1 << 6,
        Healing = 1 << 7,
        Suppression = 1 << 8,
        CannotCounterattack = 1 << 9,
        SupplyDependent = 1 << 10
    }

    public enum UnitType
    {
        MainInfantry,
        Medic,
        LightArmor,
        LightArtillery
    }

    public enum BuildingType
    {
        MilitaryFactory,
        CivilEnterprise,
        ResearchInstitute
    }

    public enum CitySpecialization
    {
        None,
        Industrial,
        Economic,
        Research,
        Fortress
    }

    public enum DiplomaticState
    {
        War,
        Truce,
        Peace,
        Access,
        Alliance
    }

    public enum CommandType
    {
        Move,
        Attack,
        AttackWall,
        Heal,
        Garrison,
        OccupyCity,
        PromoteUnit,
        RecruitUnit,
        ManufactureUnit,
        EndTurn
    }
}

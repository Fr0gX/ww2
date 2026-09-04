using System;

namespace WW2.Core.Rules
{
    public static class RuleMath
    {
        public static float LevelMultiplier(int level)
        {
            switch (level)
            {
                case 1: return 1f;
                case 2: return 1.35f;
                case 3: return 1.80f;
                default: return 2.50f;
            }
        }

        public static int KillsRequiredForPromotion(int level)
        {
            switch (level)
            {
                case 1: return 1;
                case 2: return 3;
                case 3: return 10;
                default: return 0;
            }
        }

        public static int EffectiveMaxRange(int baseMaxRange, int level)
        {
            return baseMaxRange + (level >= 4 ? 1 : 0);
        }

        public static int Round(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

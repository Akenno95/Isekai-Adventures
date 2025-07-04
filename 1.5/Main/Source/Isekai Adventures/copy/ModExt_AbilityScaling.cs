using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public class ModExt_AbilityScaling : DefModExtension
    {
        public StatDef burstShotCountStat;
        public float burstShotMultiplier = 1f;

        public StatDef rangeStat;
        public float rangeMultiplier = 1f;

        public StatDef cooldownStat;
        public float cooldownMultiplier = 1f;
    }

}

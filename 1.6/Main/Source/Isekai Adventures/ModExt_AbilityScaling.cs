using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public class ModExt_AbilityScaling : DefModExtension
    {
        public StatDef burstShotCountStat;       // Stat, die die Anzahl skaliert
        public float burstShotMultiplier = 1f; // Multiplikator auf den Stat

        public StatDef ticksBetweenStat;         // optional: Stat, die Delay skaliert
        public float ticksBetweenMult = 0f;    // z.B. -0.2f => 20% schneller
    }

}

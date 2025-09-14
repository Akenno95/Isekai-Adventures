using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public class ModExt_ProjectileScaling : DefModExtension
    {
        // Flat-Bonus: + (flatBonusStat * flatBonusFactor)
        public StatDef flatBonusStat;
        public float flatBonusFactor = 1f;

        // Multiplier für den Gesamtschaden (Base + Flat)
        public StatDef damageMultStat;
        public float damageMultScale = 1f;

        // Armor Penetration (optional, wie gehabt)
        public StatDef armorPenetrationStat;
        public float armorPenetrationFactor = 1f;
    }
}

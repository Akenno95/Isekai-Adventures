using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public class ModExt_ProjectileScaling : DefModExtension
    {
        public StatDef damageStat;
        public float damageMultiplier = 1f;

        public StatDef armorPenetrationStat;
        public float armorPenetrationMultiplier = 1f;
    }
}

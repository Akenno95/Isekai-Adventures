using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using Verse.Sound;

namespace IsekaiAdventures
{
    public class CompProperties_AbilityLaunchProjectileBurst : CompProperties_AbilityEffect
    {
        public ThingDef projectileDef;

        public int burstCount = 1;

        public int ticksBetweenShots = 5;

        public SoundDef firingSound;

        public CompProperties_AbilityLaunchProjectileBurst()
        {
            compClass = typeof(CompAbilityEffect_LaunchProjectileBurst);
        }
    }
}
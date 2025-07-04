using RimWorld;
using Verse;
using UnityEngine;

namespace IsekaiAdventures
{
    public class Verb_AbilityShoot_Scaling : Verb_AbilityShoot
    {
        private ModExt_AbilityScaling Ext => Ability?.def?.GetModExtension<ModExt_AbilityScaling>();
        private Pawn PawnCaster => Caster as Pawn;

        protected override int ShotsPerBurst
        {
            get
            {
                if (PawnCaster == null || Ext?.burstShotCountStat == null)
                    return verbProps.burstShotCount;

                float stat = PawnCaster.GetStatValue(Ext.burstShotCountStat, true);
                return Mathf.Max(1, Mathf.RoundToInt(verbProps.burstShotCount + stat * Ext.burstShotMultiplier));
            }
        }

    }
}

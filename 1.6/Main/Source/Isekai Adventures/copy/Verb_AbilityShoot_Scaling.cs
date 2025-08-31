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

        public override float EffectiveRange
        {
            get
            {
                if (PawnCaster == null || Ext?.rangeStat == null)
                    return verbProps.range;

                float stat = PawnCaster.GetStatValue(Ext.rangeStat, true);
                return Mathf.Clamp(verbProps.range + stat * Ext.rangeMultiplier, 1f, 100f);
            }
        }


        public override void WarmupComplete()
        {
            base.WarmupComplete();
            TryApplyCooldown();
        }

        private void TryApplyCooldown()
        {
            if (PawnCaster == null || Ability == null || Ext?.cooldownStat == null)
                return;

            float stat = PawnCaster.GetStatValue(Ext.cooldownStat, true);
            int baseTicks = Ability.def.cooldownTicksRange.RandomInRange;

            float factor = Mathf.Max(stat * Ext.cooldownMultiplier, 0.01f);
            int scaledTicks = Mathf.RoundToInt(baseTicks / factor);

            Ability.StartCooldown(scaledTicks);
        }

    }
}

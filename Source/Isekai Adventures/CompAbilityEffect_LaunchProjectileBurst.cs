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
    public class CompAbilityEffect_LaunchProjectileBurst : CompAbilityEffect
    {
        public int ticksToShot = -1;

        public int shotsLeft = 0;

        public LocalTargetInfo curTarget;

        private new CompProperties_AbilityLaunchProjectileBurst Props => props as CompProperties_AbilityLaunchProjectileBurst;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            shotsLeft = Props.burstCount;
            curTarget = target;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref ticksToShot, "ticksToShot", 0);
            Scribe_Values.Look(ref shotsLeft, "shotsLeft", 0);
            Scribe_Deep.Look(ref curTarget, "curTarget");
        }

        public override void CompTick()
        {
            base.CompTick();
            if (shotsLeft == 0)
            {
                return;
            }
            if (ticksToShot > 0)
            {
                ticksToShot--;
                return;
            }
            if (curTarget == null || !curTarget.IsValid || !curTarget.HasThing || !curTarget.Thing.Spawned || curTarget.Thing.Destroyed)
            {
                shotsLeft = 0;
                curTarget = null;
                return;
            }
            shotsLeft--;
            ticksToShot = Props.ticksBetweenShots;
            LaunchProjectile(curTarget);
            if (shotsLeft == 0)
            {
                curTarget = null;
            }
        }

        public virtual void LaunchProjectile(LocalTargetInfo target)
        {
            //IL_004f: Unknown result type (might be due to invalid IL or missing references)
            Projectile proj = GenSpawn.Spawn(Props.projectileDef, parent.pawn.Position, parent.pawn.Map) as Projectile;
            proj.Launch(parent.pawn, parent.pawn.DrawPos, target, target, ProjectileHitFlags.IntendedTarget);
            if (Props.firingSound != null)
            {
                Props.firingSound.PlayOneShot(new TargetInfo(parent.pawn.Position, parent.pawn.Map));
            }
        }

        public override bool AICanTargetNow(LocalTargetInfo target)
        {
            return target.Pawn != null;
        }
    }
}
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class HediffCompProperties_DamagePulse : HediffCompProperties
    {
        public DamageDef damageDef;
        public float damageAmount = 1f;
        public float damagePenetration = -1f;
        public int intervalTicks = 60;

        // Applied after each damage proc.
        // Negative values reduce severity: -0.2f
        // Positive values increase severity: 0.2f
        public float severityOffset = -0.1f;

        // If true and the Hediff is attached to a body part,
        // the damage will be applied to that part.
        public bool damageAttachedPartOnly = false;

        public bool debug = false;

        public HediffCompProperties_DamagePulse()
        {
            compClass = typeof(HediffComp_DamagePulse);
        }
    }

    public class HediffComp_DamagePulse : HediffComp
    {
        private int ticksUntilNextProc;
        private bool initialized;

        public HediffCompProperties_DamagePulse Props =>
            (HediffCompProperties_DamagePulse)props;

        private Pawn PawnOwner => parent?.pawn;

        public override void CompPostMake()
        {
            base.CompPostMake();
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            // First proc happens only after the full interval has passed.
            // Example: intervalTicks = 60 -> first damage proc after 60 ticks.
            ticksUntilNextProc = Props.intervalTicks;
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Initialize();

            Pawn pawn = PawnOwner;

            if (pawn == null || pawn.Dead)
                return;

            if (Props.damageDef == null)
            {
                if (Props.debug)
                {
                    Log.Warning($"[Isekai] HediffComp_DamagePulse on {parent.def.defName} has no damageDef.");
                }

                return;
            }

            ticksUntilNextProc--;

            if (ticksUntilNextProc > 0)
                return;

            // Order is intentional:
            // 1. Trigger damageDef.
            // 2. Then modify severity.
            // This ensures even low-severity Hediffs still proc correctly once their interval is reached.
            DoDamageProc(pawn);

            if (ApplySeverityOffsetAndShouldRemove())
            {
                RemoveSelf(pawn);
                return;
            }

            ticksUntilNextProc = Props.intervalTicks;
        }

        private void DoDamageProc(Pawn pawn)
        {
            if (pawn == null || pawn.Dead)
                return;

            BodyPartRecord hitPart = null;

            if (Props.damageAttachedPartOnly)
            {
                hitPart = parent.Part;
            }

            DamageInfo dinfo = new DamageInfo(
                Props.damageDef,
                Props.damageAmount,
                Props.damagePenetration,
                -1f,
                null,
                hitPart
            );

            pawn.TakeDamage(dinfo);
        }

        private bool ApplySeverityOffsetAndShouldRemove()
        {
            if (Props.severityOffset == 0f)
                return false;

            float newSeverity = parent.Severity + Props.severityOffset;

            // Float safety:
            // Values like 0.00000003 may display as 0.000 but still allow another proc.
            if (Props.severityOffset < 0f && newSeverity <= 0.0001f)
            {
                parent.Severity = 0f;
                return true;
            }

            parent.Severity = newSeverity;
            return false;
        }

        private void RemoveSelf(Pawn pawn)
        {
            if (pawn?.health?.hediffSet?.hediffs?.Contains(parent) == true)
            {
                pawn.health.RemoveHediff(parent);
            }
        }

        public void ResetInterval()
        {
            ticksUntilNextProc = Props.intervalTicks;
        }

        public override void CompExposeData()
        {
            base.CompExposeData();

            Scribe_Values.Look(ref ticksUntilNextProc, "ticksUntilNextProc", 0);
            Scribe_Values.Look(ref initialized, "initialized", false);
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (Props.severityOffset < 0f && parent?.Severity > 0f)
                {
                    float remainingSeverity = Mathf.Max(0f, parent.Severity - 0.0001f);
                    int remainingProcs = Mathf.CeilToInt(remainingSeverity / -Props.severityOffset);

                    if (remainingProcs > 0)
                        return $"{remainingProcs}x";
                }

                return null;
            }
        }
    }
}
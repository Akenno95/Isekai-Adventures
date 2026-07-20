// /Source/IsekaiAdventures/Hediff/HealPulse.cs
// RimWorld 1.6 - IsekaiAdventures
// HealPulse built in the same style as DamagePulse.
//
// Design:
// - DamagePulse has one timer: ticksUntilNextProc.
// - HealPulse has one timer per channel: injury/tend/scar/hediffSeverity/regrowth.
// - Each successful pulse may apply severityOffset to the source Hediff.
// - Negative severityOffset consumes the Hediff over time.
// - Positive severityOffset can make the Hediff grow stronger over time.
// - Speed reduces effective remaining ticks by subtracting speed each tick.
//
// Important:
// - Custom injury healing does NOT call Hediff_Injury.Heal(), because the vanilla
//   TotalHealFactor patch also intercepts that method. Calling it here would apply
//   TotalHealFactor twice. Instead, this comp directly reduces injury severity.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class HediffCompProperties_HealPulse : HediffCompProperties
    {
        // ------------------------------------------------------------
        // Shared DamagePulse-style behavior
        // ------------------------------------------------------------

        // Applied after each successful heal pulse.
        // Negative values reduce severity: -0.1f
        // Positive values increase severity: 0.1f
        public float severityOffset = 0f;

        // If true, severityOffset is only applied if a pulse actually did something.
        // Recommended true for healing, so the buff does not consume itself while the pawn is healthy.
        public bool severityOffsetOnlyOnSuccess = true;

        public bool debug = false;

        // If true, all enabled pulse channels can fire immediately after the Hediff is added.
        // If false, the first pulse happens after the configured interval, like DamagePulse.
        public bool pulseImmediately = false;

        // Regrowth is often long-running. By default, progress does not consume the source Hediff.
        // Completion can still consume it, so a regrowth spell can spend one charge when a part finishes.
        public bool severityOffsetOnRegrowthProgress = false;
        public bool severityOffsetOnRegrowthComplete = true;

        // Filters.
        public bool naturalPartsOnly = false;
        public List<BodyPartDef> includeParts;
        public List<string> includePartTags;

        // ------------------------------------------------------------
        // Injury healing defaults
        // ------------------------------------------------------------
        public bool healInjuries = true;
        public int injuryHealIntervalTicks = 60;
        public float injuryHealAmount = 0.05f;
        public float injuryHealPercentOfMissing = 0f;
        public float injuryHealPercentOfMax = 0f;
        public int injuryHealSlots = 1;
        public float injuryHealSpeed = 1f;

        // ------------------------------------------------------------
        // Auto tend defaults
        // ------------------------------------------------------------
        public bool tendWounds = false;
        public int tendIntervalTicks = 180;
        public float tendQuality = 0.35f;
        public int tendSlots = 1;
        public float tendSpeed = 1f;
        public bool tendBleedingOnly = true;
        public bool tendFreshMissingParts = true;

        // ------------------------------------------------------------
        // Scar healing defaults
        // ------------------------------------------------------------
        public bool healScars = false;
        public int scarHealIntervalTicks = 600;
        public float scarHealAmount = 0.01f;
        public int scarHealSlots = 1;
        public float scarHealSpeed = 1f;

        // ------------------------------------------------------------
        // Non-injury hediff severity healing defaults
        // ------------------------------------------------------------
        public bool healNonInjuryHediffs = false;
        public List<HediffDef> hediffsToReduce;
        public int hediffSeverityHealIntervalTicks = 600;
        public float hediffSeverityHealAmount = 0.01f;
        public int hediffSeverityHealSlots = 1;
        public float hediffSeverityHealSpeed = 1f;

        // ------------------------------------------------------------
        // Regrowth defaults
        // ------------------------------------------------------------
        public bool regrowParts = false;
        public int regrowthIntervalTicks = 60;
        public float regrowthWorkRequiredTicks = 60000f;
        public int regrowthSlots = 1;
        public float regrowthSpeed = 1f;
        public float regrowthWorkFactor = 1f;

        // Optional size scaling. Disabled by default.
        public bool scaleRegrowthWorkByPartSize = false;
        public float regrowthPartSizeFactor = 1f;
        public float minRegrowthSizeMultiplier = 0.5f;
        public float maxRegrowthSizeMultiplier = 5f;

        // Regrowth visual indicator.
        public HediffDef regrowthIndicatorHediff;
        public bool removeIndicatorOnComplete = true;

        public HediffCompProperties_HealPulse()
        {
            compClass = typeof(HediffComp_HealPulse);
        }
    }

    public class HealPulseRegrowthJob : IExposable
    {
        public BodyPartRecord part;
        public float progressTicks;
        public float workRequiredTicks;

        private Pawn pawn;

        public HealPulseRegrowthJob()
        {
        }

        public HealPulseRegrowthJob(Pawn pawn)
        {
            this.pawn = pawn;
        }

        public HealPulseRegrowthJob(Pawn pawn, BodyPartRecord part, float workRequiredTicks)
        {
            this.pawn = pawn;
            this.part = part;
            this.workRequiredTicks = workRequiredTicks;
            progressTicks = 0f;
        }

        public void ExposeData()
        {
            BodyPartRecord corePart = pawn?.RaceProps?.body?.corePart;
            Scribe_BodyParts.Look(ref part, "part", corePart);
            Scribe_Values.Look(ref progressTicks, "progressTicks", 0f);
            Scribe_Values.Look(ref workRequiredTicks, "workRequiredTicks", 0f);
        }
    }

    public class HediffComp_HealPulse : HediffComp
    {
        private float injuryTicksUntilNextPulse;
        private float tendTicksUntilNextPulse;
        private float scarTicksUntilNextPulse;
        private float hediffSeverityTicksUntilNextPulse;
        private float regrowthTicksUntilNextPulse;

        private bool initialized;
        private List<HealPulseRegrowthJob> activeRegrowthJobs;

        public HediffCompProperties_HealPulse Props =>
            (HediffCompProperties_HealPulse)props;

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
            activeRegrowthJobs ??= new List<HealPulseRegrowthJob>();

            // DamagePulse-style default: first pulse happens only after the full interval has passed.
            // Test-friendly option: pulseImmediately makes the first pulse happen on the next tick.
            injuryTicksUntilNextPulse = Props.pulseImmediately ? 0f : Mathf.Max(1, Props.injuryHealIntervalTicks);
            tendTicksUntilNextPulse = Props.pulseImmediately ? 0f : Mathf.Max(1, Props.tendIntervalTicks);
            scarTicksUntilNextPulse = Props.pulseImmediately ? 0f : Mathf.Max(1, Props.scarHealIntervalTicks);
            hediffSeverityTicksUntilNextPulse = Props.pulseImmediately ? 0f : Mathf.Max(1, Props.hediffSeverityHealIntervalTicks);
            regrowthTicksUntilNextPulse = Props.pulseImmediately ? 0f : Mathf.Max(1, Props.regrowthIntervalTicks);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Initialize();

            Pawn pawn = PawnOwner;
            if (pawn == null || pawn.Dead || pawn.health?.hediffSet == null)
                return;

            bool anySuccessfulPulse = false;

            if (Props.healInjuries)
                anySuccessfulPulse |= TickInjuryHealing(pawn);

            if (Props.tendWounds)
                anySuccessfulPulse |= TickAutoTend(pawn);

            if (Props.healScars)
                anySuccessfulPulse |= TickScarHealing(pawn);

            if (Props.healNonInjuryHediffs)
                anySuccessfulPulse |= TickHediffSeverityHealing(pawn);

            if (Props.regrowParts)
                anySuccessfulPulse |= TickRegrowth(pawn);

            bool shouldApplySeverity = Props.severityOffset != 0f && (!Props.severityOffsetOnlyOnSuccess || anySuccessfulPulse);
            if (shouldApplySeverity && ApplySeverityOffsetAndShouldRemove())
            {
                RemoveSelf(pawn);
            }
        }

        // ============================================================
        // DAMAGEPULSE-STYLE TIMER HELPERS
        // ============================================================

        private bool AdvanceTimer(ref float ticksUntilNextPulse, int baseIntervalTicks, float speed)
        {
            if (speed <= 0f)
                return false;

            ticksUntilNextPulse -= speed;

            if (ticksUntilNextPulse > 0f)
                return false;

            ticksUntilNextPulse += Mathf.Max(1, baseIntervalTicks);
            return true;
        }

        private bool ApplySeverityOffsetAndShouldRemove()
        {
            if (Props.severityOffset == 0f)
                return false;

            float newSeverity = parent.Severity + Props.severityOffset;

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

        public void ResetIntervals()
        {
            injuryTicksUntilNextPulse = Mathf.Max(1, Props.injuryHealIntervalTicks);
            tendTicksUntilNextPulse = Mathf.Max(1, Props.tendIntervalTicks);
            scarTicksUntilNextPulse = Mathf.Max(1, Props.scarHealIntervalTicks);
            hediffSeverityTicksUntilNextPulse = Mathf.Max(1, Props.hediffSeverityHealIntervalTicks);
            regrowthTicksUntilNextPulse = Mathf.Max(1, Props.regrowthIntervalTicks);
        }

        // ============================================================
        // INJURY HEALING
        // ============================================================

        private bool TickInjuryHealing(Pawn pawn)
        {
            HealContext speedContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Injury, parent);
            float speed = HealResolver.ResolveSpeed(speedContext, HealModifierProperty.InjuryHealSpeed, Props.injuryHealSpeed);

            if (!AdvanceTimer(ref injuryTicksUntilNextPulse, Props.injuryHealIntervalTicks, speed))
                return false;

            return DoInjuryHeal(pawn);
        }

        private bool DoInjuryHeal(Pawn pawn)
        {
            List<Hediff_Injury> injuries = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(i => i != null
                    && i.Severity > 0f
                    && !i.IsPermanent()
                    && i.Visible
                    && PartAllowed(pawn, i.Part)
                    && i.CanHealNaturally())
                .OrderByDescending(i => i.Bleeding)
                .ThenByDescending(i => i.Severity)
                .ToList();

            if (injuries.Count == 0)
                return false;

            HealContext slotContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Injury, parent);
            int slots = HealResolver.ResolveSlots(slotContext, HealModifierProperty.InjuryHealSlots, Props.injuryHealSlots);
            if (slots <= 0)
                return false;

            bool didHeal = false;

            foreach (Hediff_Injury injury in injuries.Take(slots).ToList())
            {
                if (injury == null || injury.Severity <= 0f)
                    continue;

                float missingSeverity = injury.Severity;
                float maxPartHealth = EstimateMaxPartHealth(pawn, injury.Part, missingSeverity);

                HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Injury, parent);
                context.targetHediff = injury;
                context.targetPart = injury.Part;
                context.baseAmount = Props.injuryHealAmount;
                context.missingSeverity = missingSeverity;
                context.maxPartHealth = maxPartHealth;

                float amount = ResolveFullInjuryAmount(context, missingSeverity, maxPartHealth);
                if (amount <= 0f)
                    continue;

                ReduceInjurySeverityDirect(injury, amount);
                didHeal = true;
            }

            return didHeal;
        }

        private float ResolveFullInjuryAmount(HealContext context, float missingSeverity, float maxPartHealth)
        {
            float amount = HealResolver.ResolveValue(context, HealModifierProperty.InjuryHealAmount, Props.injuryHealAmount);

            float percentMissing = Props.injuryHealPercentOfMissing
                + HealResolver.ResolveValue(context, HealModifierProperty.InjuryHealPercentOfMissing, 0f);

            float percentMax = Props.injuryHealPercentOfMax
                + HealResolver.ResolveValue(context, HealModifierProperty.InjuryHealPercentOfMax, 0f);

            float factor = HealResolver.ResolveAdditiveFactor(context, HealModifierProperty.InjuryHealFactor, 1f);
            float totalHealFactor = HealResolver.ResolveTotalHealFactor(context.patient ?? context.healer, context.healDef);

            float final = (amount + missingSeverity * Mathf.Max(0f, percentMissing) + maxPartHealth * Mathf.Max(0f, percentMax)) * factor * totalHealFactor;

            if (Props.debug)
            {
                Log.Message($"[IsekaiAdventures] HealPulse Injury: pawn={context.patient?.LabelShort ?? context.healer?.LabelShort}, " +
                            $"baseAmount={Props.injuryHealAmount}, resolvedAmount={amount}, missing={missingSeverity}, maxPart={maxPartHealth}, " +
                            $"percentMissing={percentMissing}, percentMax={percentMax}, factor={factor}, totalHealFactor={totalHealFactor}, final={final}");
            }

            return Mathf.Max(0f, final);
        }

        private void ReduceInjurySeverityDirect(Hediff_Injury injury, float amount)
        {
            if (injury == null || amount <= 0f)
                return;

            injury.Severity = Mathf.Max(0f, injury.Severity - amount);

            if (injury.Severity <= 0.001f)
            {
                try
                {
                    HealthUtility.Cure(injury);
                }
                catch
                {
                    injury.Severity = 0f;
                }
            }
        }

        // ============================================================
        // AUTO TEND
        // ============================================================

        private bool TickAutoTend(Pawn pawn)
        {
            HealContext speedContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Tend, parent);
            float speed = HealResolver.ResolveSpeed(speedContext, HealModifierProperty.TendSpeed, Props.tendSpeed);

            if (!AdvanceTimer(ref tendTicksUntilNextPulse, Props.tendIntervalTicks, speed))
                return false;

            return DoAutoTend(pawn);
        }

        private bool DoAutoTend(Pawn pawn)
        {
            HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Tend, parent);
            int slots = HealResolver.ResolveSlots(context, HealModifierProperty.TendSlots, Props.tendSlots);
            if (slots <= 0)
                return false;

            float totalHealFactor = HealResolver.ResolveTotalHealFactor(pawn, IsekaiHealDefOf.Isekai_Heal_Tend);
            if (totalHealFactor <= 0f)
                return false;

            float resolvedQualityBeforeFactor = HealResolver.ResolveTendQuality(context, Props.tendQuality);
            float quality = resolvedQualityBeforeFactor * totalHealFactor;
            quality = Mathf.Clamp01(quality);

            if (Props.debug)
            {
                Log.Message($"[IsekaiAdventures] HealPulse Tend: pawn={pawn.LabelShort}, baseQuality={Props.tendQuality}, resolvedQualityBeforeFactor={resolvedQualityBeforeFactor}, totalHealFactor={totalHealFactor}, finalQuality={quality}");
            }
            if (quality <= 0f)
                return false;

            IEnumerable<Hediff> candidates = pawn.health.hediffSet.hediffs
                .Where(h => h != null
                    && PartAllowed(pawn, h.Part)
                    && h.TendableNow()
                    && (!Props.tendBleedingOnly || h.Bleeding));

            if (!Props.tendFreshMissingParts)
                candidates = candidates.Where(h => !(h is Hediff_MissingPart));

            List<Hediff> targets = candidates
                .OrderByDescending(h => h.Bleeding)
                .ThenByDescending(h => h.Severity)
                .Take(slots)
                .ToList();

            if (targets.Count == 0)
                return false;

            bool didTend = false;
            foreach (Hediff hediff in targets)
            {
                if (TryTend(hediff, quality))
                    didTend = true;
            }

            return didTend;
        }

        private bool TryTend(Hediff hediff, float quality)
        {
            if (hediff == null || quality <= 0f)
                return false;

            try
            {
                hediff.Tended(Mathf.Clamp01(quality), 1f, 1);
                return true;
            }
            catch (Exception ex)
            {
                if (Props.debug)
                    Log.Warning($"[IsekaiAdventures] HealPulse auto tend failed for {hediff.def?.defName}: {ex.Message}");

                return false;
            }
        }

        // ============================================================
        // SCAR HEALING
        // ============================================================

        private bool TickScarHealing(Pawn pawn)
        {
            HealContext speedContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Scar, parent);
            float speed = HealResolver.ResolveSpeed(speedContext, HealModifierProperty.ScarHealSpeed, Props.scarHealSpeed);

            if (!AdvanceTimer(ref scarTicksUntilNextPulse, Props.scarHealIntervalTicks, speed))
                return false;

            return DoScarHeal(pawn);
        }

        private bool DoScarHeal(Pawn pawn)
        {
            List<Hediff_Injury> scars = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(i => i != null && i.IsPermanent() && i.Visible && i.Severity > 0f && PartAllowed(pawn, i.Part))
                .OrderByDescending(i => i.Severity)
                .ToList();

            if (scars.Count == 0)
                return false;

            HealContext slotContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Scar, parent);
            int slots = HealResolver.ResolveSlots(slotContext, HealModifierProperty.ScarHealSlots, Props.scarHealSlots);
            if (slots <= 0)
                return false;

            bool didHeal = false;

            foreach (Hediff_Injury scar in scars.Take(slots).ToList())
            {
                HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Scar, parent);
                context.targetHediff = scar;
                context.targetPart = scar.Part;
                context.baseAmount = Props.scarHealAmount;

                float amount = HealResolver.ResolveScarHealAmount(context, Props.scarHealAmount);
                if (Props.debug)
                {
                    float totalHealFactor = HealResolver.ResolveTotalHealFactor(pawn, IsekaiHealDefOf.Isekai_Heal_Scar);
                    Log.Message($"[IsekaiAdventures] HealPulse Scar: pawn={pawn.LabelShort}, baseAmount={Props.scarHealAmount}, resolvedAmount={amount}, totalHealFactor={totalHealFactor}, target={scar.def?.defName}, severityBefore={scar.Severity}");
                }

                if (amount <= 0f)
                    continue;

                scar.Severity = Mathf.Max(0f, scar.Severity - amount);
                didHeal = true;

                if (scar.Severity <= 0.001f)
                {
                    try { HealthUtility.Cure(scar); }
                    catch { scar.Severity = 0f; }
                }
            }

            return didHeal;
        }

        // ============================================================
        // NON-INJURY HEDIFF SEVERITY HEALING
        // ============================================================

        private bool TickHediffSeverityHealing(Pawn pawn)
        {
            if (Props.hediffsToReduce == null || Props.hediffsToReduce.Count == 0)
                return false;

            HealContext speedContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_HediffSeverity, parent);
            float speed = HealResolver.ResolveSpeed(speedContext, HealModifierProperty.HediffSeverityHealSpeed, Props.hediffSeverityHealSpeed);

            if (!AdvanceTimer(ref hediffSeverityTicksUntilNextPulse, Props.hediffSeverityHealIntervalTicks, speed))
                return false;

            return DoHediffSeverityHeal(pawn);
        }

        private bool DoHediffSeverityHeal(Pawn pawn)
        {
            List<Hediff> targets = pawn.health.hediffSet.hediffs
                .Where(h => h != null
                    && !(h is Hediff_Injury)
                    && h.Severity > 0f
                    && !h.IsPermanent()
                    && Props.hediffsToReduce.Contains(h.def)
                    && PartAllowed(pawn, h.Part))
                .OrderByDescending(h => h.Severity)
                .ToList();

            if (targets.Count == 0)
                return false;

            HealContext slotContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_HediffSeverity, parent);
            int slots = HealResolver.ResolveSlots(slotContext, HealModifierProperty.HediffSeverityHealSlots, Props.hediffSeverityHealSlots);
            if (slots <= 0)
                return false;

            bool didHeal = false;

            foreach (Hediff hediff in targets.Take(slots).ToList())
            {
                HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_HediffSeverity, parent);
                context.targetHediff = hediff;
                context.targetPart = hediff.Part;
                context.baseAmount = Props.hediffSeverityHealAmount;

                float amount = HealResolver.ResolveHediffSeverityHealAmount(context, Props.hediffSeverityHealAmount);
                if (amount <= 0f)
                    continue;

                hediff.Severity = Mathf.Max(0f, hediff.Severity - amount);
                didHeal = true;
            }

            return didHeal;
        }

        // ============================================================
        // REGROWTH
        // ============================================================

        private bool TickRegrowth(Pawn pawn)
        {
            HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Regrowth, parent);
            float speed = HealResolver.ResolveSpeed(context, HealModifierProperty.RegrowthSpeed, Props.regrowthSpeed);

            if (!AdvanceTimer(ref regrowthTicksUntilNextPulse, Props.regrowthIntervalTicks, speed))
                return false;

            float totalHealFactor = HealResolver.ResolveTotalHealFactor(pawn, IsekaiHealDefOf.Isekai_Heal_Regrowth);
            if (Props.debug)
            {
                Log.Message($"[IsekaiAdventures] HealPulse Regrowth Tick: pawn={pawn.LabelShort}, speed={speed}, totalHealFactor={totalHealFactor}, ticksUntilNext={regrowthTicksUntilNextPulse}");
            }

            if (totalHealFactor <= 0f)
                return false;

            RegrowthPulseResult result = DoRegrowthStep(pawn, totalHealFactor);

            if (result.completedAnyPart && Props.severityOffsetOnRegrowthComplete)
                return true;

            if (result.progressedAnyPart && Props.severityOffsetOnRegrowthProgress)
                return true;

            return false;
        }

        private RegrowthPulseResult DoRegrowthStep(Pawn pawn, float totalHealFactor)
        {
            activeRegrowthJobs ??= new List<HealPulseRegrowthJob>();
            activeRegrowthJobs.RemoveAll(job => job == null || job.part == null || !IsPartStillMissingAndValid(pawn, job.part));

            HealContext slotContext = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Regrowth, parent);
            int slots = HealResolver.ResolveSlots(slotContext, HealModifierProperty.RegrowthSlots, Props.regrowthSlots);
            if (slots <= 0)
                return RegrowthPulseResult.None;

            FillRegrowthJobs(pawn, slots);

            if (activeRegrowthJobs.Count == 0)
                return RegrowthPulseResult.None;

            bool didProgress = false;
            bool completedAny = false;

            foreach (HealPulseRegrowthJob job in activeRegrowthJobs.ToList())
            {
                if (job?.part == null)
                    continue;

                job.progressTicks += Props.regrowthIntervalTicks * totalHealFactor;
                didProgress = true;

                UpdateRegrowIndicatorProgress(pawn, job);

                if (job.progressTicks >= Mathf.Max(1f, job.workRequiredTicks))
                {
                    RestoreRegrowthJob(pawn, job);
                    activeRegrowthJobs.Remove(job);
                    completedAny = true;
                }
            }

            return new RegrowthPulseResult(didProgress, completedAny);
        }

        private void FillRegrowthJobs(Pawn pawn, int slots)
        {
            int openSlots = slots - activeRegrowthJobs.Count;
            if (openSlots <= 0)
                return;

            List<Hediff_MissingPart> candidates = GetRegrowthCandidates(pawn)
                .Where(mp => activeRegrowthJobs.All(job => job.part != mp.Part))
                .ToList();

            foreach (Hediff_MissingPart missing in candidates.Take(openSlots))
            {
                if (missing?.Part == null)
                    continue;

                HealContext context = HealContext.ForSelf(pawn, IsekaiHealDefOf.Isekai_Heal_Regrowth, parent);
                context.targetPart = missing.Part;

                float baseWork = CalculateBaseRegrowthWork(missing.Part);
                float workRequired = HealResolver.ResolveRegrowthWorkRequired(context, baseWork);

                float clampedWork = Mathf.Max(1f, workRequired);
                float existingProgress = GetExistingRegrowthIndicatorProgress(pawn, missing.Part) * clampedWork;

                HealPulseRegrowthJob job = new HealPulseRegrowthJob(pawn, missing.Part, clampedWork);
                job.progressTicks = Mathf.Clamp(existingProgress, 0f, clampedWork - 0.001f);

                activeRegrowthJobs.Add(job);
                TryAddRegrowIndicator(pawn, missing.Part);
            }
        }

        private List<Hediff_MissingPart> GetRegrowthCandidates(Pawn pawn)
        {
            List<Hediff_MissingPart> allMissing = pawn.health.hediffSet?.GetMissingPartsCommonAncestors();
            if (allMissing == null || allMissing.Count == 0)
                return new List<Hediff_MissingPart>();

            return allMissing
                .Where(mp => mp?.Part != null
                    && PartAllowed(pawn, mp.Part)
                    && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(mp.Part))
                .OrderByDescending(mp => RegrowthPriorityScore(mp.Part))
                .ThenByDescending(mp => mp.Part.coverageAbsWithChildren)
                .ToList();
        }

        private int RegrowthPriorityScore(BodyPartRecord part)
        {
            if (part?.def == null)
                return 0;

            int score = 0;
            List<BodyPartTagDef> tags = part.def.tags;

            if (tags != null)
            {
                foreach (BodyPartTagDef tag in tags)
                {
                    if (tag == null)
                        continue;

                    string name = tag.defName;
                    if (name == "MovingLimbCore") score += 1000;
                    if (name == "ManipulationLimbCore") score += 900;
                    if (name == "SightSource") score += 800;
                    if (name == "EatingSource") score += 700;
                    if (name == "TalkingSource") score += 600;
                }
            }

            score += Mathf.RoundToInt(part.coverageAbsWithChildren * 1000f);
            return score;
        }

        private float CalculateBaseRegrowthWork(BodyPartRecord part)
        {
            float work = Mathf.Max(1f, Props.regrowthWorkRequiredTicks);

            if (!Props.scaleRegrowthWorkByPartSize || part == null)
                return work;

            float raw = part.coverageAbsWithChildren * 10f;
            float sizeMultiplier = Mathf.Clamp(raw, Props.minRegrowthSizeMultiplier, Props.maxRegrowthSizeMultiplier);
            float t = Mathf.Clamp01(Props.regrowthPartSizeFactor);
            return work * Mathf.Lerp(1f, sizeMultiplier, t);
        }

        private bool IsPartStillMissingAndValid(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.health?.hediffSet == null || part == null)
                return false;

            if (!PartAllowed(pawn, part))
                return false;

            return pawn.health.hediffSet.PartIsMissing(part)
                && !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(part);
        }

        private void RestoreRegrowthJob(Pawn pawn, HealPulseRegrowthJob job)
        {
            if (pawn?.health == null || job?.part == null)
                return;

            try
            {
                pawn.health.RestorePart(job.part);
            }
            catch (Exception ex)
            {
                if (Props.debug)
                    Log.Warning($"[IsekaiAdventures] HealPulse failed to restore part '{job.part?.def?.defName}': {ex.Message}");
            }

            if (Props.removeIndicatorOnComplete)
                RemoveRegrowIndicator(pawn, job.part);
        }

        private struct RegrowthPulseResult
        {
            public readonly bool progressedAnyPart;
            public readonly bool completedAnyPart;

            public static readonly RegrowthPulseResult None = new RegrowthPulseResult(false, false);

            public RegrowthPulseResult(bool progressedAnyPart, bool completedAnyPart)
            {
                this.progressedAnyPart = progressedAnyPart;
                this.completedAnyPart = completedAnyPart;
            }
        }

        // ============================================================
        // SHARED HELPERS
        // ============================================================

        private float EstimateMaxPartHealth(Pawn pawn, BodyPartRecord part, float missingSeverity)
        {
            if (pawn?.health?.hediffSet == null || part == null)
                return missingSeverity;

            float current = pawn.health.hediffSet.GetPartHealth(part);
            return Mathf.Max(0f, current + missingSeverity);
        }

        private bool PartAllowed(Pawn pawn, BodyPartRecord part)
        {
            if (part == null)
                return true;

            if (Props.naturalPartsOnly)
            {
                bool hasAddedPart = pawn.health.hediffSet.hediffs.Any(h => h != null && h.Part == part && h is Hediff_AddedPart);
                if (hasAddedPart)
                    return false;
            }

            if (Props.includeParts != null && Props.includeParts.Count > 0 && !Props.includeParts.Contains(part.def))
                return false;

            if (Props.includePartTags != null && Props.includePartTags.Count > 0)
            {
                List<BodyPartTagDef> tags = part.def.tags;
                if (tags == null || tags.Count == 0)
                    return false;

                bool match = Props.includePartTags.Any(tagName => tags.Any(tag => tag?.defName == tagName));
                if (!match)
                    return false;
            }

            return true;
        }

        // ============================================================
        // REGROWTH INDICATOR
        // ============================================================

        private float GetExistingRegrowthIndicatorProgress(Pawn pawn, BodyPartRecord missingPart)
        {
            if (Props.regrowthIndicatorHediff == null || pawn?.health?.hediffSet == null || missingPart == null)
                return 0f;

            BodyPartRecord host = FindIndicatorHostPart(pawn, missingPart);
            Hediff indicator = pawn.health.hediffSet.hediffs.FirstOrDefault(h =>
                h != null
                && h.def == Props.regrowthIndicatorHediff
                && h.Part == host);

            if (indicator == null)
                return 0f;

            return Mathf.Clamp01(indicator.Severity);
        }

        private void TryAddRegrowIndicator(Pawn pawn, BodyPartRecord missingPart)
        {
            if (Props.regrowthIndicatorHediff == null)
                return;

            if (pawn?.health?.hediffSet == null || missingPart == null)
                return;

            BodyPartRecord host = FindIndicatorHostPart(pawn, missingPart);

            bool already = pawn.health.hediffSet.hediffs.Any(h =>
                h != null
                && h.def == Props.regrowthIndicatorHediff
                && h.Part == host);

            if (already)
                return;

            try
            {
                Hediff indicator = pawn.health.AddHediff(Props.regrowthIndicatorHediff, host);
                if (indicator != null)
                    indicator.Severity = 0.01f;
            }
            catch (Exception ex)
            {
                if (Props.debug)
                    Log.Warning($"[IsekaiAdventures] HealPulse failed to add regrowth indicator: {ex.Message}");
            }
        }

        private BodyPartRecord FindIndicatorHostPart(Pawn pawn, BodyPartRecord missingPart)
        {
            if (pawn?.health?.hediffSet == null || missingPart == null)
                return null;

            BodyPartRecord current = missingPart.parent;
            while (current != null)
            {
                if (!pawn.health.hediffSet.PartIsMissing(current))
                    return current;

                current = current.parent;
            }

            return null;
        }

        private void UpdateRegrowIndicatorProgress(Pawn pawn, HealPulseRegrowthJob job)
        {
            if (Props.regrowthIndicatorHediff == null || pawn?.health?.hediffSet == null || job?.part == null)
                return;

            BodyPartRecord host = FindIndicatorHostPart(pawn, job.part);
            float progress = Mathf.Clamp(job.progressTicks / Mathf.Max(1f, job.workRequiredTicks), 0.01f, 0.99f);

            foreach (Hediff indicator in pawn.health.hediffSet.hediffs.ToList())
            {
                if (indicator != null && indicator.def == Props.regrowthIndicatorHediff && indicator.Part == host)
                    indicator.Severity = progress;
            }
        }

        private void RemoveRegrowIndicator(Pawn pawn, BodyPartRecord missingPart)
        {
            if (Props.regrowthIndicatorHediff == null || pawn?.health?.hediffSet == null)
                return;

            BodyPartRecord host = FindIndicatorHostPart(pawn, missingPart);

            List<Hediff> indicators = pawn.health.hediffSet.hediffs
                .Where(h => h != null && h.def == Props.regrowthIndicatorHediff && h.Part == host)
                .ToList();

            foreach (Hediff indicator in indicators)
            {
                try { pawn.health.RemoveHediff(indicator); }
                catch { }
            }
        }

        // ============================================================
        // SAVE / LOAD AND LABEL
        // ============================================================

        public override void CompExposeData()
        {
            base.CompExposeData();

            Scribe_Values.Look(ref injuryTicksUntilNextPulse, "injuryTicksUntilNextPulse", 0f);
            Scribe_Values.Look(ref tendTicksUntilNextPulse, "tendTicksUntilNextPulse", 0f);
            Scribe_Values.Look(ref scarTicksUntilNextPulse, "scarTicksUntilNextPulse", 0f);
            Scribe_Values.Look(ref hediffSeverityTicksUntilNextPulse, "hediffSeverityTicksUntilNextPulse", 0f);
            Scribe_Values.Look(ref regrowthTicksUntilNextPulse, "regrowthTicksUntilNextPulse", 0f);
            Scribe_Values.Look(ref initialized, "initialized", false);

            Scribe_Collections.Look(ref activeRegrowthJobs, "activeRegrowthJobs", LookMode.Deep, parent?.pawn);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && activeRegrowthJobs == null)
                activeRegrowthJobs = new List<HealPulseRegrowthJob>();
        }

        public override string CompLabelInBracketsExtra
        {
            get
            {
                if (Props.severityOffset < 0f && parent?.Severity > 0f)
                {
                    float remainingSeverity = Mathf.Max(0f, parent.Severity - 0.0001f);
                    int remainingPulses = Mathf.CeilToInt(remainingSeverity / -Props.severityOffset);

                    if (remainingPulses > 0)
                        return $"{remainingPulses}x";
                }

                return null;
            }
        }
    }
}

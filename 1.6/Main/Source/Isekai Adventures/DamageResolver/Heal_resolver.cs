// /Source/IsekaiAdventures/Healing/HealPipeline.cs
// RimWorld 1.6 - IsekaiAdventures
// Heal Pipeline Core - Phase 1 Revised
//
// Major design decisions:
// - No generic <values> list.
// - Fixed XML modifiers are direct fields on HealModifierDef.
// - statScales stay as the stat-based scaling system.
// - totalHealFactor is multiplicative and explicit.
// - totalHealFactor default is 1.0.
// - If any active modifier has totalHealFactor = 0, healing becomes 0.
// - Vanilla healing only uses TotalHealFactor, not custom Amount/Speed/Slots scaling.

using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    // ============================================================
    // 1) HEAL IDENTIFICATION
    // ============================================================

    public class HealDef : Def
    {
    }

    public class HealTypeSetDef : Def
    {
        public List<HealDef> healDefs;
        public List<HealTypeSetDef> healTypeSets;

        public bool Contains(HealDef healDef)
        {
            HashSet<HealTypeSetDef> visited = new HashSet<HealTypeSetDef>();
            return ContainsInternal(healDef, visited);
        }

        private bool ContainsInternal(HealDef healDef, HashSet<HealTypeSetDef> visited)
        {
            if (healDef == null)
                return false;

            if (visited == null)
                visited = new HashSet<HealTypeSetDef>();

            if (!visited.Add(this))
                return false;

            if (healDefs != null && healDefs.Contains(healDef))
                return true;

            if (healTypeSets != null)
            {
                foreach (HealTypeSetDef set in healTypeSets)
                {
                    if (set != null && set.ContainsInternal(healDef, visited))
                        return true;
                }
            }

            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            bool hasHealDefs = healDefs != null && healDefs.Count > 0;
            bool hasNestedSets = healTypeSets != null && healTypeSets.Count > 0;

            if (!hasHealDefs && !hasNestedSets)
                yield return $"{defName}: HealTypeSetDef has no healDefs and no healTypeSets.";

            if (healTypeSets != null)
            {
                foreach (HealTypeSetDef set in healTypeSets)
                {
                    if (set == null)
                        yield return $"{defName}: healTypeSets contains a null entry.";
                    else if (set == this)
                        yield return $"{defName}: healTypeSets contains itself.";
                }
            }
        }
    }

    // ============================================================
    // 2) MODIFIER PROPERTIES
    // ============================================================

    public enum HealModifierProperty
    {
        TotalHealFactor,

        InjuryHealAmount,
        InjuryHealPercentOfMissing,
        InjuryHealPercentOfMax,
        InjuryHealFactor,
        InjuryHealSlots,
        InjuryHealSpeed,

        ScarHealAmount,
        ScarHealFactor,
        ScarHealSlots,
        ScarHealSpeed,

        HediffSeverityHealAmount,
        HediffSeverityHealFactor,
        HediffSeverityHealSlots,
        HediffSeverityHealSpeed,

        TendQuality,
        TendSlots,
        TendSpeed,

        RegrowthSlots,
        RegrowthSpeed,
        RegrowthWorkFactor
    }

    // ============================================================
    // 3) STAT SCALING
    // ============================================================

    public class HealPropertyScale
    {
        public HealModifierProperty property;
        public StatDef stat;
        public float factor = 1f;
        public float offset = 0f;

        public float Evaluate(Pawn pawn)
        {
            if (pawn == null || stat == null)
                return 0f;

            try
            {
                return offset + pawn.GetStatValue(stat) * factor;
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Failed to evaluate HealPropertyScale. Stat='{stat?.defName}', property='{property}', error='{ex.Message}'");
                return 0f;
            }
        }
    }

    // ============================================================
    // 4) HEAL MODIFIER DEF
    // ============================================================

    public class HealModifierDef : Def
    {
        public bool global = false;

        public List<HealDef> healDefs;
        public List<HealTypeSetDef> healTypeSets;

        // Multiplicative final healing factor.
        // Neutral = 1.
        // 0 = completely blocks healing.
        // Multiple modifiers multiply together.
        public float totalHealFactor = 1f;

        // Additive custom heal modifiers.
        public float injuryHealAmount = 0f;
        public float injuryHealPercentOfMissing = 0f;
        public float injuryHealPercentOfMax = 0f;
        public float injuryHealFactor = 0f;
        public float injuryHealSlots = 0f;
        public float injuryHealSpeed = 0f;

        public float scarHealAmount = 0f;
        public float scarHealFactor = 0f;
        public float scarHealSlots = 0f;
        public float scarHealSpeed = 0f;

        public float hediffSeverityHealAmount = 0f;
        public float hediffSeverityHealFactor = 0f;
        public float hediffSeverityHealSlots = 0f;
        public float hediffSeverityHealSpeed = 0f;

        public float tendQuality = 0f;
        public float tendSlots = 0f;
        public float tendSpeed = 0f;

        public float regrowthSlots = 0f;
        public float regrowthSpeed = 0f;
        public float regrowthWorkFactor = 0f;

        public List<HealPropertyScale> statScales;

        public bool AppliesTo(HealDef healDef)
        {
            if (healDef == null)
                return false;

            bool hasHealDefs = healDefs != null && healDefs.Count > 0;
            bool hasSets = healTypeSets != null && healTypeSets.Count > 0;

            if (!hasHealDefs && !hasSets)
                return true;

            if (hasHealDefs && healDefs.Contains(healDef))
                return true;

            if (hasSets)
            {
                foreach (HealTypeSetDef set in healTypeSets)
                {
                    if (set != null && set.Contains(healDef))
                        return true;
                }
            }

            return false;
        }

        public float GetAdditiveValue(HealModifierProperty property)
        {
            switch (property)
            {
                case HealModifierProperty.InjuryHealAmount: return injuryHealAmount;
                case HealModifierProperty.InjuryHealPercentOfMissing: return injuryHealPercentOfMissing;
                case HealModifierProperty.InjuryHealPercentOfMax: return injuryHealPercentOfMax;
                case HealModifierProperty.InjuryHealFactor: return injuryHealFactor;
                case HealModifierProperty.InjuryHealSlots: return injuryHealSlots;
                case HealModifierProperty.InjuryHealSpeed: return injuryHealSpeed;

                case HealModifierProperty.ScarHealAmount: return scarHealAmount;
                case HealModifierProperty.ScarHealFactor: return scarHealFactor;
                case HealModifierProperty.ScarHealSlots: return scarHealSlots;
                case HealModifierProperty.ScarHealSpeed: return scarHealSpeed;

                case HealModifierProperty.HediffSeverityHealAmount: return hediffSeverityHealAmount;
                case HealModifierProperty.HediffSeverityHealFactor: return hediffSeverityHealFactor;
                case HealModifierProperty.HediffSeverityHealSlots: return hediffSeverityHealSlots;
                case HealModifierProperty.HediffSeverityHealSpeed: return hediffSeverityHealSpeed;

                case HealModifierProperty.TendQuality: return tendQuality;
                case HealModifierProperty.TendSlots: return tendSlots;
                case HealModifierProperty.TendSpeed: return tendSpeed;

                case HealModifierProperty.RegrowthSlots: return regrowthSlots;
                case HealModifierProperty.RegrowthSpeed: return regrowthSpeed;
                case HealModifierProperty.RegrowthWorkFactor: return regrowthWorkFactor;

                case HealModifierProperty.TotalHealFactor:
                default:
                    return 0f;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (totalHealFactor < 0f)
                yield return $"{defName}: totalHealFactor should be >= 0. Use 0 to block healing completely.";

            if (statScales != null)
            {
                foreach (HealPropertyScale scale in statScales)
                {
                    if (scale == null)
                    {
                        yield return $"{defName}: statScales contains a null entry.";
                        continue;
                    }

                    if (scale.stat == null)
                        yield return $"{defName}: statScale for property {scale.property} has no stat.";
                }
            }
        }
    }

    // ============================================================
    // 5) CONTEXT AND DEBUG VALUE
    // ============================================================

    public class HealContext
    {
        public Pawn healer;
        public Pawn patient;

        public HealDef healDef;

        public Hediff sourceHediff;
        public Hediff targetHediff;
        public BodyPartRecord targetPart;

        public float baseAmount;
        public float missingSeverity;
        public float maxPartHealth;

        public int tickInterval;

        public Pawn StatPawn => healer ?? patient;

        public static HealContext ForSelf(Pawn pawn, HealDef healDef, Hediff sourceHediff = null)
        {
            return new HealContext
            {
                healer = pawn,
                patient = pawn,
                healDef = healDef,
                sourceHediff = sourceHediff
            };
        }
    }

    public class HealResolvedValue
    {
        public HealModifierProperty property;
        public float baseValue;
        public float fixedModifierValue;
        public float statScaleValue;
        public float finalValue;

        public override string ToString()
        {
            return $"{property}: base={baseValue}, fixed={fixedModifierValue}, stat={statScaleValue}, final={finalValue}";
        }
    }

    // ============================================================
    // 6) MODIFIER SOURCES
    // ============================================================

    public interface IHealModifierProvider
    {
        IEnumerable<HealModifierDef> GetHealModifiers();
    }

    public class HediffCompProperties_HealModifiers : HediffCompProperties
    {
        public List<HealModifierDef> modifiers;
        public float minSeverity = 0f;

        public HediffCompProperties_HealModifiers()
        {
            compClass = typeof(HediffComp_HealModifiers);
        }
    }

    public class HediffComp_HealModifiers : HediffComp, IHealModifierProvider
    {
        public HediffCompProperties_HealModifiers Props =>
            (HediffCompProperties_HealModifiers)props;

        public IEnumerable<HealModifierDef> GetHealModifiers()
        {
            if (Props?.modifiers == null)
                yield break;

            if (parent != null && parent.Severity < Props.minSeverity)
                yield break;

            foreach (HealModifierDef modifier in Props.modifiers)
            {
                if (modifier != null)
                    yield return modifier;
            }
        }
    }

    public class CompProperties_HealModifiers : CompProperties
    {
        public List<HealModifierDef> modifiers;

        public CompProperties_HealModifiers()
        {
            compClass = typeof(CompHealModifiers);
        }
    }

    public class CompHealModifiers : ThingComp, IHealModifierProvider
    {
        public CompProperties_HealModifiers Props =>
            (CompProperties_HealModifiers)props;

        public IEnumerable<HealModifierDef> GetHealModifiers()
        {
            if (Props?.modifiers == null)
                yield break;

            foreach (HealModifierDef modifier in Props.modifiers)
            {
                if (modifier != null)
                    yield return modifier;
            }
        }
    }

    public class GeneHealModifiersExtension : DefModExtension, IHealModifierProvider
    {
        public List<HealModifierDef> modifiers;

        public IEnumerable<HealModifierDef> GetHealModifiers()
        {
            if (modifiers == null)
                yield break;

            foreach (HealModifierDef modifier in modifiers)
            {
                if (modifier != null)
                    yield return modifier;
            }
        }
    }

    // ============================================================
    // 7) COLLECTOR
    // ============================================================

    public static class HealModifierCollector
    {
        public static IEnumerable<HealModifierDef> CollectFor(Pawn pawn, HealDef healDef)
        {
            if (healDef == null)
                yield break;

            HashSet<HealModifierDef> yielded = new HashSet<HealModifierDef>();

            foreach (HealModifierDef modifier in DefDatabase<HealModifierDef>.AllDefsListForReading)
            {
                if (modifier == null || !modifier.global)
                    continue;

                if (!modifier.AppliesTo(healDef))
                    continue;

                if (yielded.Add(modifier))
                    yield return modifier;
            }

            if (pawn == null)
                yield break;

            List<Hediff> hediffs = pawn.health?.hediffSet?.hediffs;
            if (hediffs != null)
            {
                foreach (Hediff hediff in hediffs)
                {
                    HediffWithComps hediffWithComps = hediff as HediffWithComps;
                    if (hediffWithComps?.comps == null)
                        continue;

                    foreach (HediffComp comp in hediffWithComps.comps)
                    {
                        if (comp is IHealModifierProvider provider)
                        {
                            foreach (HealModifierDef modifier in provider.GetHealModifiers())
                            {
                                if (modifier != null && modifier.AppliesTo(healDef) && yielded.Add(modifier))
                                    yield return modifier;
                            }
                        }
                    }
                }
            }

            if (pawn.genes?.GenesListForReading != null)
            {
                foreach (Gene gene in pawn.genes.GenesListForReading)
                {
                    GeneHealModifiersExtension extension = gene?.def?.GetModExtension<GeneHealModifiersExtension>();
                    if (extension == null)
                        continue;

                    foreach (HealModifierDef modifier in extension.GetHealModifiers())
                    {
                        if (modifier != null && modifier.AppliesTo(healDef) && yielded.Add(modifier))
                            yield return modifier;
                    }
                }
            }

            if (pawn.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    if (apparel == null)
                        continue;

                    CompHealModifiers comp = apparel.GetComp<CompHealModifiers>();
                    if (comp == null)
                        continue;

                    foreach (HealModifierDef modifier in comp.GetHealModifiers())
                    {
                        if (modifier != null && modifier.AppliesTo(healDef) && yielded.Add(modifier))
                            yield return modifier;
                    }
                }
            }

            ThingWithComps weapon = pawn.equipment?.Primary;
            if (weapon != null)
            {
                CompHealModifiers comp = weapon.GetComp<CompHealModifiers>();
                if (comp != null)
                {
                    foreach (HealModifierDef modifier in comp.GetHealModifiers())
                    {
                        if (modifier != null && modifier.AppliesTo(healDef) && yielded.Add(modifier))
                            yield return modifier;
                    }
                }
            }
        }
    }

    // ============================================================
    // 8) RESOLVER
    // ============================================================

    public static class HealResolver
    {
        public static float ResolveValue(HealContext context, HealModifierProperty property, float baseValue = 0f)
        {
            return ResolveDetailed(context, property, baseValue).finalValue;
        }

        public static HealResolvedValue ResolveDetailed(HealContext context, HealModifierProperty property, float baseValue = 0f)
        {
            HealResolvedValue result = new HealResolvedValue
            {
                property = property,
                baseValue = baseValue,
                fixedModifierValue = 0f,
                statScaleValue = 0f,
                finalValue = baseValue
            };

            if (context == null || context.healDef == null)
            {
                result.finalValue = ClampProperty(property, baseValue);
                return result;
            }

            Pawn pawnForStats = context.StatPawn;
            Pawn pawnForCollection = context.patient ?? context.healer;

            foreach (HealModifierDef modifier in HealModifierCollector.CollectFor(pawnForCollection, context.healDef))
            {
                if (modifier == null)
                    continue;

                result.fixedModifierValue += modifier.GetAdditiveValue(property);

                if (modifier.statScales != null)
                {
                    foreach (HealPropertyScale scale in modifier.statScales)
                    {
                        if (scale != null && scale.property == property)
                            result.statScaleValue += scale.Evaluate(pawnForStats);
                    }
                }
            }

            result.finalValue = ClampProperty(property, baseValue + result.fixedModifierValue + result.statScaleValue);
            return result;
        }

        public static int ResolveSlots(HealContext context, HealModifierProperty property, float baseValue = 0f)
        {
            float value = ResolveValue(context, property, baseValue);
            return Mathf.Max(0, Mathf.FloorToInt(value + 0.5f));
        }

        public static float ResolveSpeed(HealContext context, HealModifierProperty property, float baseSpeed = 1f)
        {
            return Mathf.Max(0f, ResolveValue(context, property, baseSpeed));
        }

        public static float ResolveAdditiveFactor(HealContext context, HealModifierProperty property, float baseFactor = 1f)
        {
            return Mathf.Max(0f, ResolveValue(context, property, baseFactor));
        }

        public static float ResolveTotalHealFactor(Pawn pawn, HealDef healDef)
        {
            if (healDef == null)
                return 1f;

            float factor = 1f;

            foreach (HealModifierDef modifier in HealModifierCollector.CollectFor(pawn, healDef))
            {
                if (modifier == null)
                    continue;

                factor *= Mathf.Max(0f, modifier.totalHealFactor);

                if (factor <= 0f)
                    return 0f;
            }

            return Mathf.Max(0f, factor);
        }

        public static float ResolveInjuryHealAmount(HealContext context)
        {
            if (context == null)
                return 0f;

            float amount = ResolveValue(context, HealModifierProperty.InjuryHealAmount, context.baseAmount);
            float percentMissing = ResolveValue(context, HealModifierProperty.InjuryHealPercentOfMissing, 0f);
            float percentMax = ResolveValue(context, HealModifierProperty.InjuryHealPercentOfMax, 0f);
            float factor = ResolveAdditiveFactor(context, HealModifierProperty.InjuryHealFactor, 1f);
            float totalHealFactor = ResolveTotalHealFactor(context.patient ?? context.healer, context.healDef);

            float final = (amount + context.missingSeverity * percentMissing + context.maxPartHealth * percentMax) * factor * totalHealFactor;
            return Mathf.Max(0f, final);
        }

        public static float ResolveScarHealAmount(HealContext context, float baseAmount)
        {
            if (context == null)
                return 0f;

            float amount = ResolveValue(context, HealModifierProperty.ScarHealAmount, baseAmount);
            float factor = ResolveAdditiveFactor(context, HealModifierProperty.ScarHealFactor, 1f);
            float totalHealFactor = ResolveTotalHealFactor(context.patient ?? context.healer, context.healDef);

            return Mathf.Max(0f, amount * factor * totalHealFactor);
        }

        public static float ResolveHediffSeverityHealAmount(HealContext context, float baseAmount)
        {
            if (context == null)
                return 0f;

            float amount = ResolveValue(context, HealModifierProperty.HediffSeverityHealAmount, baseAmount);
            float factor = ResolveAdditiveFactor(context, HealModifierProperty.HediffSeverityHealFactor, 1f);
            float totalHealFactor = ResolveTotalHealFactor(context.patient ?? context.healer, context.healDef);

            return Mathf.Max(0f, amount * factor * totalHealFactor);
        }

        public static float ResolveTendQuality(HealContext context, float baseQuality)
        {
            return Mathf.Clamp01(ResolveValue(context, HealModifierProperty.TendQuality, baseQuality));
        }

        public static float ResolveRegrowthWorkRequired(HealContext context, float baseWorkRequired)
        {
            if (context == null)
                return Mathf.Max(0f, baseWorkRequired);

            float workFactor = ResolveAdditiveFactor(context, HealModifierProperty.RegrowthWorkFactor, 1f);
            return Mathf.Max(0f, baseWorkRequired * workFactor);
        }

        private static float ClampProperty(HealModifierProperty property, float value)
        {
            switch (property)
            {
                case HealModifierProperty.TendQuality:
                    return Mathf.Clamp01(value);

                case HealModifierProperty.TotalHealFactor:
                case HealModifierProperty.InjuryHealAmount:
                case HealModifierProperty.InjuryHealPercentOfMissing:
                case HealModifierProperty.InjuryHealPercentOfMax:
                case HealModifierProperty.InjuryHealFactor:
                case HealModifierProperty.InjuryHealSlots:
                case HealModifierProperty.InjuryHealSpeed:
                case HealModifierProperty.ScarHealAmount:
                case HealModifierProperty.ScarHealFactor:
                case HealModifierProperty.ScarHealSlots:
                case HealModifierProperty.ScarHealSpeed:
                case HealModifierProperty.HediffSeverityHealAmount:
                case HealModifierProperty.HediffSeverityHealFactor:
                case HealModifierProperty.HediffSeverityHealSlots:
                case HealModifierProperty.HediffSeverityHealSpeed:
                case HealModifierProperty.TendSlots:
                case HealModifierProperty.TendSpeed:
                case HealModifierProperty.RegrowthSlots:
                case HealModifierProperty.RegrowthSpeed:
                case HealModifierProperty.RegrowthWorkFactor:
                    return Mathf.Max(0f, value);

                default:
                    return value;
            }
        }
    }

    // ============================================================
    // 9) DEFOF
    // ============================================================

    [DefOf]
    public static class IsekaiHealDefOf
    {
        public static HealDef Isekai_Heal_Injury;
        public static HealDef Isekai_Heal_Scar;
        public static HealDef Isekai_Heal_Tend;
        public static HealDef Isekai_Heal_Regrowth;
        public static HealDef Isekai_Heal_HediffSeverity;
        public static HealDef Isekai_Heal_Vanilla;

        static IsekaiHealDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(IsekaiHealDefOf));
        }
    }

    // ============================================================
    // 10) VANILLA HEAL PATCH
    // ============================================================

    [HarmonyPatch(typeof(Hediff_Injury), nameof(Hediff_Injury.Heal))]
    public static class Patch_HediffInjury_Heal_TotalHealFactor
    {
        public static bool Prefix(Hediff_Injury __instance, ref float amount)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn == null)
                return true;

            HealDef vanillaHealDef = IsekaiHealDefOf.Isekai_Heal_Vanilla;
            if (vanillaHealDef == null)
                return true;

            float factor = HealResolver.ResolveTotalHealFactor(pawn, vanillaHealDef);

            if (factor <= 0f)
                return false;

            amount *= factor;
            return amount > 0f;
        }
    }
}

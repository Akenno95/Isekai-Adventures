// /Source/IsekaiAdventures/Combat/OnHit/IsekaiAdventures_OnHitResolver.cs
// RimWorld 1.6 - IsekaiAdventures
// OnHit Modifier System - Phase 1 + chanceFromStats
//
// Contains:
// - OnHitModifierDef
// - Hediff / Thing / GeneDefExtension OnHit modifier sources
// - OnHitModifierCollector
// - OnHitResolver
//
// Requires:
// - Isekai_DamageResolverCore.cs
//   specifically DamageResult, DamageContext, DamageTypeSetDef, StatScale
//
// Notes:
// - OnHit means: the attack hit the target.
// - By default, OnHit only triggers if finalDamage > 0.
// - Set <triggerIfNoDamage>true</triggerIfNoDamage> to allow triggering even if damage was negated.
// - <applyHediff> applies to the hit target.
// - <applyHediffToSelf> applies to the attacker.
// - <chanceFromStats> scales trigger chance from attacker stats.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    // ============================================================
    // 1) ON HIT MODIFIER DEF
    // ============================================================

    /// <summary>
    /// XML-defined OnHit rule.
    /// This is processed after vanilla TakeDamage has run.
    /// </summary>
    public class OnHitModifierDef : Def
    {
        // Optional exact filter. Null = no exact DamageDef filter.
        public DamageDef damageDef;

        // Optional group filter. Null = no group filter.
        public DamageTypeSetDef damageTypeSet;

        // Chance that the entire OnHit modifier triggers.
        // 1.0 = always, 0.25 = 25%.
        public float chance = 1f;

        // Optional stat scaling for chance.
        // finalChance = chance + Sum(stat * factor), then clamped between 0 and 1.
        // Evaluated from the attacker/caster.
        public List<StatScale> chanceFromStats;

        // Default false:
        // If final damage was negated to 0, this modifier will not trigger.
        // If true, it may trigger even when finalDamage == 0.
        public bool triggerIfNoDamage = false;

        // Applies to the hit target.
        public HediffDef applyHediff;
        public float severity = 0.1f;

        // Applies to the attacker / caster / instigator.
        public HediffDef applyHediffToSelf;
        public float severityToSelf = 0.1f;

        public bool AppliesTo(DamageDef incomingDamageDef)
        {
            if (incomingDamageDef == null)
                return false;

            // No filter = applies to all DamageDefs, but only when provided by an active source.
            if (damageDef == null && damageTypeSet == null)
                return true;

            if (damageDef != null && damageDef == incomingDamageDef)
                return true;

            if (damageTypeSet != null && damageTypeSet.Contains(incomingDamageDef))
                return true;

            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (chance < 0f || chance > 1f)
                yield return $"{defName}: chance should be between 0 and 1.";

            if (chanceFromStats != null)
            {
                foreach (StatScale scale in chanceFromStats)
                {
                    if (scale == null || scale.stat == null)
                        yield return $"{defName}: chanceFromStats contains an entry without stat.";
                }
            }

            if (applyHediff == null && applyHediffToSelf == null)
                yield return $"{defName}: OnHitModifierDef has no applyHediff and no applyHediffToSelf.";

            if (severity < 0f)
                yield return $"{defName}: severity should not be negative.";

            if (severityToSelf < 0f)
                yield return $"{defName}: severityToSelf should not be negative.";

            if (damageTypeSet != null && (damageTypeSet.damageDefs == null || damageTypeSet.damageDefs.Count == 0))
                yield return $"{defName}: damageTypeSet is empty.";
        }
    }

    /// <summary>
    /// DefModExtension for GeneDef or other Def-based OnHit modifier sources.
    /// </summary>
    public class OnHitModifiersExtension : DefModExtension
    {
        public List<OnHitModifierDef> modifiers;

        public IEnumerable<OnHitModifierDef> GetOnHitModifiers()
        {
            return modifiers ?? Enumerable.Empty<OnHitModifierDef>();
        }
    }


    // ============================================================
    // 2) MODIFIER SOURCE COMPS
    // ============================================================

    public interface IOnHitModifierProvider
    {
        IEnumerable<OnHitModifierDef> GetOnHitModifiers();
    }

    // ---------------- Hediffs ----------------

    public class HediffCompProperties_OnHitModifiers : HediffCompProperties
    {
        public List<OnHitModifierDef> modifiers;

        // OnHitModifiers from this Hediff only become active
        // once parent.Severity reaches this value.
        // If omitted in XML, default is 0 and the modifier is active immediately.
        public float minSeverity = 0f;

        public HediffCompProperties_OnHitModifiers()
        {
            compClass = typeof(HediffComp_OnHitModifiers);
        }
    }

    public class HediffComp_OnHitModifiers : HediffComp, IOnHitModifierProvider
    {
        public HediffCompProperties_OnHitModifiers Props => (HediffCompProperties_OnHitModifiers)props;

        public IEnumerable<OnHitModifierDef> GetOnHitModifiers()
        {
            if (Props == null)
                return Enumerable.Empty<OnHitModifierDef>();

            if (parent != null && parent.Severity < Props.minSeverity)
                return Enumerable.Empty<OnHitModifierDef>();

            return Props.modifiers ?? Enumerable.Empty<OnHitModifierDef>();
        }
    }

    // ---------------- Things / Apparel / Equipment ----------------

    public class CompProperties_OnHitModifiers : CompProperties
    {
        public List<OnHitModifierDef> modifiers;

        public CompProperties_OnHitModifiers()
        {
            compClass = typeof(CompOnHitModifiers);
        }
    }

    public class CompOnHitModifiers : ThingComp, IOnHitModifierProvider
    {
        public CompProperties_OnHitModifiers Props => (CompProperties_OnHitModifiers)props;

        public IEnumerable<OnHitModifierDef> GetOnHitModifiers()
        {
            return Props?.modifiers ?? Enumerable.Empty<OnHitModifierDef>();
        }
    }


    // ============================================================
    // 3) ON HIT MODIFIER COLLECTOR
    // ============================================================

    public static class OnHitModifierCollector
    {
        /// <summary>
        /// OnHit modifiers are collected from the attacker.
        /// </summary>
        public static IEnumerable<OnHitModifierDef> CollectFor(Pawn attacker, DamageDef damageDef)
        {
            if (attacker == null)
                yield break;

            HashSet<OnHitModifierDef> yielded = new HashSet<OnHitModifierDef>();

            // Hediffs: buffs, class states, temporary enchantments.
            if (attacker.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in attacker.health.hediffSet.hediffs)
                {
                    if (hediff == null)
                        continue;

                    HediffComp_OnHitModifiers comp = hediff.TryGetComp<HediffComp_OnHitModifiers>();
                    if (comp == null)
                        continue;

                    foreach (OnHitModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Genes: innate bloodline/species modifiers via DefModExtension.
            if (attacker.genes?.GenesListForReading != null)
            {
                foreach (Gene gene in attacker.genes.GenesListForReading)
                {
                    if (gene?.def == null)
                        continue;

                    OnHitModifiersExtension extension = gene.def.GetModExtension<OnHitModifiersExtension>();
                    if (extension == null)
                        continue;

                    foreach (OnHitModifierDef mod in extension.GetOnHitModifiers())
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Apparel: enchanted clothing / armor that grants on-hit effects.
            if (attacker.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in attacker.apparel.WornApparel)
                {
                    CompOnHitModifiers comp = apparel?.GetComp<CompOnHitModifiers>();
                    if (comp == null)
                        continue;

                    foreach (OnHitModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Equipment: weapons, staffs, relics.
            ThingWithComps primary = attacker.equipment?.Primary;
            if (primary != null)
            {
                CompOnHitModifiers comp = primary.GetComp<CompOnHitModifiers>();
                if (comp != null)
                {
                    foreach (OnHitModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }
        }

        private static IEnumerable<OnHitModifierDef> SafeMods(IOnHitModifierProvider provider)
        {
            IEnumerable<OnHitModifierDef> mods;

            try
            {
                mods = provider.GetOnHitModifiers();
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] OnHit modifier provider failed: {ex.Message}");
                yield break;
            }

            if (mods == null)
                yield break;

            foreach (OnHitModifierDef mod in mods)
            {
                if (mod != null)
                    yield return mod;
            }
        }
    }


    // ============================================================
    // 4) ON HIT RESOLVER
    // ============================================================

    public static class OnHitResolver
    {
        public static void Resolve(DamageResult result)
        {
            try
            {
                if (result == null)
                    return;

                DamageContext context = result.context;
                if (context == null)
                    return;

                if (!context.canTriggerOnHit)
                    return;

                Pawn attacker = context.attacker;
                Pawn target = context.TargetPawn;

                if (attacker == null || target == null)
                    return;

                DamageDef damageDef = context.damageDef;
                if (damageDef == null)
                    return;

                // Snapshot the modifiers before applying effects.
                // Applying a Hediff to the attacker or target can modify Hediff lists.
                List<OnHitModifierDef> modifiers = OnHitModifierCollector.CollectFor(attacker, damageDef).ToList();

                foreach (OnHitModifierDef mod in modifiers)
                {
                    TryApplyModifier(mod, result, attacker, target);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] OnHitResolver failed: {ex}");
            }
        }

        private static void TryApplyModifier(OnHitModifierDef mod, DamageResult result, Pawn attacker, Pawn target)
        {
            if (mod == null || result == null || attacker == null || target == null)
                return;

            // Default behavior: OnHit requires actual damage.
            // If damage was negated to 0, only modifiers with triggerIfNoDamage may trigger.
            if (result.WasNegated && !mod.triggerIfNoDamage)
                return;

            float chance = EvaluateChance(mod, attacker);
            if (chance < 1f && Rand.Value > chance)
                return;

            bool appliedSomething = false;

            if (mod.applyHediff != null)
            {
                ApplyHediff(target, mod.applyHediff, mod.severity);
                appliedSomething = true;
            }

            if (mod.applyHediffToSelf != null)
            {
                ApplyHediff(attacker, mod.applyHediffToSelf, mod.severityToSelf);
                appliedSomething = true;
            }

            if (Prefs.DevMode && appliedSomething)
            {
                string attackerLabel = attacker.LabelShortCap ?? "unknown attacker";
                string targetLabel = target.LabelShortCap ?? "unknown target";
                string damageText = result.WasNegated ? "negated hit" : $"{result.finalDamage:0.##} damage";
                Log.Message($"[IsekaiAdventures] OnHit {mod.defName}: {attackerLabel} -> {targetLabel}, {result.context.damageDef.defName}, {damageText}");
            }
        }

        private static float EvaluateChance(OnHitModifierDef mod, Pawn attacker)
        {
            if (mod == null)
                return 0f;

            float finalChance = mod.chance;

            if (mod.chanceFromStats != null)
            {
                foreach (StatScale scale in mod.chanceFromStats)
                {
                    if (scale != null)
                        finalChance += scale.Evaluate(attacker);
                }
            }

            return Mathf.Clamp01(finalChance);
        }

        private static void ApplyHediff(Pawn pawn, HediffDef hediffDef, float severity)
        {
            if (pawn == null || hediffDef == null)
                return;

            float amount = Mathf.Max(0f, severity);
            if (amount <= 0f)
                return;

            try
            {
                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
                if (existing != null)
                {
                    existing.Severity += amount;
                    return;
                }

                Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                hediff.Severity = amount;
                pawn.health.AddHediff(hediff);
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Failed to apply OnHit hediff '{hediffDef.defName}' to '{pawn.LabelShortCap}': {ex.Message}");
            }
        }
    }
}

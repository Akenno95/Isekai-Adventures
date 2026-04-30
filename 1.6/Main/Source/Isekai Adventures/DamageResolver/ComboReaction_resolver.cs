// /Source/IsekaiAdventures/Combat/ComboReaction/IsekaiAdventures_ComboReactionResolver.cs
// RimWorld 1.6 - IsekaiAdventures
// ComboReaction Modifier System - Phase 1
//
// Contains:
// - HediffSetDef
// - ComboTriggerSetDef
// - ComboTriggerMode
// - HediffRequirement
// - HediffConsumption
// - ComboReactionModifierDef
// - Hediff / Thing / GeneDefExtension ComboReaction modifier sources
// - Global ComboReaction modifiers through <global>true</global>
// - ComboReactionModifierCollector
// - ComboReactionResolver
// - HediffAdded patch for Hediff-triggered combos
//
// Requires:
// - Isekai_DamageResolverCore.cs
//   specifically DamageResult, DamageContext, DamageTypeSetDef
//
// Design:
// - ComboReaction is deterministic: no chance.
// - ComboReaction applies Hediffs only to the target pawn.
// - No direct damage in Phase 1.
// - Damage-triggered combos are resolved from DamageResult after TakeDamage.
// - Hediff-triggered combos are resolved when a Hediff is added to a pawn.
//
// XML behavior:
// - triggerMode omitted = Any
// - Any: Damage trigger OR Hediff trigger may activate this combo.
// - Damage: only DamageDef/DamageTypeSet triggers are considered.
// - Hediff: only Hediff/HediffSet triggers are considered.
// - requiredHediffs severity omitted/0 = Hediff must only be present.
// - requiredHediffs severity > 0 = Hediff must be present with at least this severity.
// - consume omitted = true. By default, matched requiredHediffs are completely consumed.
// - <consume>false</consume> keeps requiredHediffs unless consumeHediffs is used.
// - consumeHediffs allows special consume rules.

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    // ============================================================
    // 1) SET DEFS
    // ============================================================

    public class HediffSetDef : Def
    {
        public List<HediffDef> hediffs;

        public bool Contains(HediffDef hediffDef)
        {
            return hediffDef != null && hediffs != null && hediffs.Contains(hediffDef);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (hediffs == null || hediffs.Count == 0)
                yield return $"{defName}: HediffSetDef has no hediffs.";
        }
    }

    public class ComboTriggerSetDef : Def
    {
        public List<DamageDef> damageDefs;
        public List<DamageTypeSetDef> damageTypeSets;
        public List<HediffDef> hediffDefs;
        public List<HediffSetDef> hediffSets;

        public bool MatchesDamage(DamageDef damageDef)
        {
            if (damageDef == null)
                return false;

            if (damageDefs != null && damageDefs.Contains(damageDef))
                return true;

            if (damageTypeSets != null)
            {
                foreach (DamageTypeSetDef set in damageTypeSets)
                {
                    if (set != null && set.Contains(damageDef))
                        return true;
                }
            }

            return false;
        }

        public bool MatchesHediff(HediffDef hediffDef)
        {
            if (hediffDef == null)
                return false;

            if (hediffDefs != null && hediffDefs.Contains(hediffDef))
                return true;

            if (hediffSets != null)
            {
                foreach (HediffSetDef set in hediffSets)
                {
                    if (set != null && set.Contains(hediffDef))
                        return true;
                }
            }

            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            bool hasDamageDefs = damageDefs != null && damageDefs.Count > 0;
            bool hasDamageTypeSets = damageTypeSets != null && damageTypeSets.Count > 0;
            bool hasHediffDefs = hediffDefs != null && hediffDefs.Count > 0;
            bool hasHediffSets = hediffSets != null && hediffSets.Count > 0;

            if (!hasDamageDefs && !hasDamageTypeSets && !hasHediffDefs && !hasHediffSets)
                yield return $"{defName}: ComboTriggerSetDef has no triggers.";
        }
    }


    // ============================================================
    // 2) COMBO DATA STRUCTURES
    // ============================================================

    public enum ComboTriggerMode
    {
        Any,
        Damage,
        Hediff
    }

    public class HediffRequirement
    {
        public HediffDef hediffDef;
        public HediffSetDef hediffSet;

        // 0 means: only presence is required.
        // >0 means: matching Hediff must have at least this severity.
        public float severity = 0f;

        public bool Matches(Hediff hediff)
        {
            if (hediff?.def == null)
                return false;

            if (hediffDef != null && hediff.def == hediffDef)
                return true;

            if (hediffSet != null && hediffSet.Contains(hediff.def))
                return true;

            return false;
        }
    }

    public class HediffConsumption
    {
        public HediffDef hediffDef;
        public HediffSetDef hediffSet;
        public float severity = 0f;
        public bool consumeAll = false;

        public bool Matches(Hediff hediff)
        {
            if (hediff?.def == null)
                return false;

            if (hediffDef != null && hediff.def == hediffDef)
                return true;

            if (hediffSet != null && hediffSet.Contains(hediff.def))
                return true;

            return false;
        }
    }


    // ============================================================
    // 3) COMBO REACTION MODIFIER DEF
    // ============================================================

    public class ComboReactionModifierDef : Def
    {
        public bool global = false;

        // Default Any.
        public ComboTriggerMode triggerMode = ComboTriggerMode.Any;

        // Single damage trigger.
        public DamageDef triggerDamageDef;

        // Damage set trigger.
        public DamageTypeSetDef triggerDamageTypeSet;

        // Single Hediff trigger.
        public HediffDef triggerHediff;

        // Hediff set trigger.
        public HediffSetDef triggerHediffSet;

        // Combined trigger set.
        public ComboTriggerSetDef triggerSet;

        // Required Hediffs on the target pawn.
        // All entries must be satisfied.
        public List<HediffRequirement> requiredHediffs;

        // Default true:
        // matched requiredHediffs are fully consumed.
        // Set <consume>false</consume> if required Hediffs should remain,
        // or when using consumeHediffs for partial/special consumption.
        public bool consume = true;

        // Special consume rules.
        public List<HediffConsumption> consumeHediffs;

        // Hediff applied to the target pawn when the combo fires.
        public HediffDef applyHediff;
        public float severity = 0.1f;

        public bool MatchesDamageTrigger(DamageDef damageDef)
        {
            if (damageDef == null)
                return false;

            if (triggerMode == ComboTriggerMode.Hediff)
                return false;

            if (triggerDamageDef != null && triggerDamageDef == damageDef)
                return true;

            if (triggerDamageTypeSet != null && triggerDamageTypeSet.Contains(damageDef))
                return true;

            if (triggerSet != null && triggerSet.MatchesDamage(damageDef))
                return true;

            return false;
        }

        public bool MatchesHediffTrigger(HediffDef hediffDef)
        {
            if (hediffDef == null)
                return false;

            if (triggerMode == ComboTriggerMode.Damage)
                return false;

            if (triggerHediff != null && triggerHediff == hediffDef)
                return true;

            if (triggerHediffSet != null && triggerHediffSet.Contains(hediffDef))
                return true;

            if (triggerSet != null && triggerSet.MatchesHediff(hediffDef))
                return true;

            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            bool triggerSetHasDamage = triggerSet != null &&
                ((triggerSet.damageDefs != null && triggerSet.damageDefs.Count > 0) ||
                 (triggerSet.damageTypeSets != null && triggerSet.damageTypeSets.Count > 0));

            bool triggerSetHasHediff = triggerSet != null &&
                ((triggerSet.hediffDefs != null && triggerSet.hediffDefs.Count > 0) ||
                 (triggerSet.hediffSets != null && triggerSet.hediffSets.Count > 0));

            bool hasDamageTrigger = triggerDamageDef != null || triggerDamageTypeSet != null || triggerSetHasDamage;
            bool hasHediffTrigger = triggerHediff != null || triggerHediffSet != null || triggerSetHasHediff;

            if (!hasDamageTrigger && !hasHediffTrigger)
                yield return $"{defName}: ComboReactionModifierDef has no trigger.";

            if (requiredHediffs == null || requiredHediffs.Count == 0)
                yield return $"{defName}: ComboReactionModifierDef has no requiredHediffs. Combos without requirements are too broad.";

            if (requiredHediffs != null)
            {
                foreach (HediffRequirement req in requiredHediffs)
                {
                    if (req == null)
                    {
                        yield return $"{defName}: requiredHediffs contains a null entry.";
                        continue;
                    }

                    if (req.hediffDef == null && req.hediffSet == null)
                        yield return $"{defName}: requiredHediffs contains an entry without hediffDef or hediffSet.";

                    if (req.hediffDef != null && req.hediffSet != null)
                        yield return $"{defName}: requiredHediffs entry should not use both hediffDef and hediffSet.";

                    if (req.severity < 0f)
                        yield return $"{defName}: requiredHediffs severity should not be negative.";
                }
            }

            if (consume && consumeHediffs != null && consumeHediffs.Count > 0)
                yield return $"{defName}: consume is true by default. Set <consume>false</consume> when using consumeHediffs.";

            if (consumeHediffs != null)
            {
                foreach (HediffConsumption consumeRule in consumeHediffs)
                {
                    if (consumeRule == null)
                    {
                        yield return $"{defName}: consumeHediffs contains a null entry.";
                        continue;
                    }

                    if (consumeRule.hediffDef == null && consumeRule.hediffSet == null)
                        yield return $"{defName}: consumeHediffs contains an entry without hediffDef or hediffSet.";

                    if (consumeRule.hediffDef != null && consumeRule.hediffSet != null)
                        yield return $"{defName}: consumeHediffs entry should not use both hediffDef and hediffSet.";

                    if (consumeRule.severity < 0f)
                        yield return $"{defName}: consumeHediffs severity should not be negative.";

                    if (!consumeRule.consumeAll && consumeRule.severity <= 0f)
                        yield return $"{defName}: consumeHediffs entry should use consumeAll=true or severity > 0.";
                }
            }

            if (applyHediff == null)
                yield return $"{defName}: ComboReactionModifierDef has no applyHediff.";

            if (severity < 0f)
                yield return $"{defName}: severity should not be negative.";
        }
    }

    public class ComboReactionModifiersExtension : DefModExtension
    {
        public List<ComboReactionModifierDef> modifiers;

        public IEnumerable<ComboReactionModifierDef> GetComboReactionModifiers()
        {
            return modifiers ?? Enumerable.Empty<ComboReactionModifierDef>();
        }
    }


    // ============================================================
    // 4) MODIFIER SOURCE COMPS
    // ============================================================

    public interface IComboReactionModifierProvider
    {
        IEnumerable<ComboReactionModifierDef> GetComboReactionModifiers();
    }

    public class HediffCompProperties_ComboReactionModifiers : HediffCompProperties
    {
        public List<ComboReactionModifierDef> modifiers;
        public float minSeverity = 0f;

        public HediffCompProperties_ComboReactionModifiers()
        {
            compClass = typeof(HediffComp_ComboReactionModifiers);
        }
    }

    public class HediffComp_ComboReactionModifiers : HediffComp, IComboReactionModifierProvider
    {
        public HediffCompProperties_ComboReactionModifiers Props => (HediffCompProperties_ComboReactionModifiers)props;

        public IEnumerable<ComboReactionModifierDef> GetComboReactionModifiers()
        {
            if (Props == null)
                return Enumerable.Empty<ComboReactionModifierDef>();

            if (parent != null && parent.Severity < Props.minSeverity)
                return Enumerable.Empty<ComboReactionModifierDef>();

            return Props.modifiers ?? Enumerable.Empty<ComboReactionModifierDef>();
        }
    }

    public class CompProperties_ComboReactionModifiers : CompProperties
    {
        public List<ComboReactionModifierDef> modifiers;

        public CompProperties_ComboReactionModifiers()
        {
            compClass = typeof(CompComboReactionModifiers);
        }
    }

    public class CompComboReactionModifiers : ThingComp, IComboReactionModifierProvider
    {
        public CompProperties_ComboReactionModifiers Props => (CompProperties_ComboReactionModifiers)props;

        public IEnumerable<ComboReactionModifierDef> GetComboReactionModifiers()
        {
            return Props?.modifiers ?? Enumerable.Empty<ComboReactionModifierDef>();
        }
    }


    // ============================================================
    // 5) COLLECTOR
    // ============================================================

    public static class ComboReactionModifierCollector
    {
        public static IEnumerable<ComboReactionModifierDef> CollectFor(Pawn pawn)
        {
            if (pawn == null)
                yield break;

            HashSet<ComboReactionModifierDef> yielded = new HashSet<ComboReactionModifierDef>();

            foreach (ComboReactionModifierDef mod in DefDatabase<ComboReactionModifierDef>.AllDefsListForReading)
            {
                if (mod != null && mod.global && yielded.Add(mod))
                    yield return mod;
            }

            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff == null)
                        continue;

                    HediffComp_ComboReactionModifiers comp = hediff.TryGetComp<HediffComp_ComboReactionModifiers>();
                    if (comp == null)
                        continue;

                    foreach (ComboReactionModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            if (pawn.genes?.GenesListForReading != null)
            {
                foreach (Gene gene in pawn.genes.GenesListForReading)
                {
                    if (gene?.def == null)
                        continue;

                    ComboReactionModifiersExtension extension = gene.def.GetModExtension<ComboReactionModifiersExtension>();
                    if (extension == null)
                        continue;

                    foreach (ComboReactionModifierDef mod in extension.GetComboReactionModifiers())
                    {
                        if (mod != null && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            if (pawn.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    CompComboReactionModifiers comp = apparel?.GetComp<CompComboReactionModifiers>();
                    if (comp == null)
                        continue;

                    foreach (ComboReactionModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            ThingWithComps primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                CompComboReactionModifiers comp = primary.GetComp<CompComboReactionModifiers>();
                if (comp != null)
                {
                    foreach (ComboReactionModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }
        }

        private static IEnumerable<ComboReactionModifierDef> SafeMods(IComboReactionModifierProvider provider)
        {
            IEnumerable<ComboReactionModifierDef> mods;

            try
            {
                mods = provider.GetComboReactionModifiers();
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] ComboReaction modifier provider failed: {ex.Message}");
                yield break;
            }

            if (mods == null)
                yield break;

            foreach (ComboReactionModifierDef mod in mods)
            {
                if (mod != null)
                    yield return mod;
            }
        }
    }


    // ============================================================
    // 6) RESOLVER
    // ============================================================

    public static class ComboReactionResolver
    {
        private static bool resolvingCombo;

        public static void Resolve(DamageResult result)
        {
            try
            {
                if (result?.context == null)
                    return;

                if (!result.context.canTriggerComboReaction)
                    return;

                Pawn target = result.context.TargetPawn;
                DamageDef damageDef = result.context.damageDef;
                if (target == null || damageDef == null)
                    return;

                List<ComboReactionModifierDef> modifiers = ComboReactionModifierCollector.CollectFor(target).ToList();

                foreach (ComboReactionModifierDef mod in modifiers)
                {
                    if (mod == null || !mod.MatchesDamageTrigger(damageDef))
                        continue;

                    TryApplyCombo(mod, target, $"damage {damageDef.defName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] ComboReactionResolver damage-trigger failed: {ex}");
            }
        }

        public static void ResolveHediffAdded(Pawn target, Hediff addedHediff)
        {
            try
            {
                if (resolvingCombo)
                    return;

                if (target == null || addedHediff?.def == null)
                    return;

                List<ComboReactionModifierDef> modifiers = ComboReactionModifierCollector.CollectFor(target).ToList();

                foreach (ComboReactionModifierDef mod in modifiers)
                {
                    if (mod == null || !mod.MatchesHediffTrigger(addedHediff.def))
                        continue;

                    TryApplyCombo(mod, target, $"hediff {addedHediff.def.defName}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] ComboReactionResolver hediff-trigger failed: {ex}");
            }
        }

        private static void TryApplyCombo(ComboReactionModifierDef mod, Pawn target, string triggerLabel)
        {
            if (mod == null || target == null)
                return;

            if (!RequirementsMet(target, mod, out List<Hediff> matchedRequiredHediffs))
                return;

            resolvingCombo = true;
            try
            {
                if (mod.consume)
                {
                    ConsumeRequired(target, matchedRequiredHediffs);
                }
                else if (mod.consumeHediffs != null && mod.consumeHediffs.Count > 0)
                {
                    ConsumeSpecial(target, mod.consumeHediffs);
                }

                bool applied = ApplyHediff(target, mod.applyHediff, mod.severity);

                if (Prefs.DevMode && applied)
                {
                    Log.Message($"[IsekaiAdventures] ComboReaction {mod.defName}: target={target.LabelShortCap}, trigger={triggerLabel}, applied={mod.applyHediff.defName}, severity={mod.severity:0.##}");
                }
            }
            finally
            {
                resolvingCombo = false;
            }
        }

        private static bool RequirementsMet(Pawn pawn, ComboReactionModifierDef mod, out List<Hediff> matchedRequiredHediffs)
        {
            matchedRequiredHediffs = new List<Hediff>();

            if (pawn == null || mod?.requiredHediffs == null || mod.requiredHediffs.Count == 0)
                return false;

            foreach (HediffRequirement req in mod.requiredHediffs)
            {
                Hediff matched = FindMatchingHediff(pawn, req);
                if (matched == null)
                    return false;

                matchedRequiredHediffs.Add(matched);
            }

            return true;
        }

        private static Hediff FindMatchingHediff(Pawn pawn, HediffRequirement req)
        {
            if (pawn?.health?.hediffSet?.hediffs == null || req == null)
                return null;

            foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff == null || !req.Matches(hediff))
                    continue;

                if (req.severity > 0f && hediff.Severity < req.severity)
                    continue;

                return hediff;
            }

            return null;
        }

        private static void ConsumeRequired(Pawn pawn, List<Hediff> matchedRequiredHediffs)
        {
            if (pawn == null || matchedRequiredHediffs == null)
                return;

            foreach (Hediff hediff in matchedRequiredHediffs.Where(h => h != null).Distinct().ToList())
            {
                RemoveHediff(pawn, hediff);
            }
        }

        private static void ConsumeSpecial(Pawn pawn, List<HediffConsumption> consumeRules)
        {
            if (pawn?.health?.hediffSet?.hediffs == null || consumeRules == null)
                return;

            foreach (HediffConsumption rule in consumeRules)
            {
                if (rule == null)
                    continue;

                List<Hediff> matches = pawn.health.hediffSet.hediffs
                    .Where(h => h != null && rule.Matches(h))
                    .ToList();

                foreach (Hediff hediff in matches)
                {
                    if (rule.consumeAll)
                    {
                        RemoveHediff(pawn, hediff);
                    }
                    else if (rule.severity > 0f)
                    {
                        hediff.Severity -= rule.severity;
                        if (hediff.Severity <= 0.0001f)
                            RemoveHediff(pawn, hediff);
                    }
                }
            }
        }

        private static bool ApplyHediff(Pawn pawn, HediffDef hediffDef, float severity)
        {
            if (pawn == null || hediffDef == null)
                return false;

            float amount = Mathf.Max(0f, severity);
            if (amount <= 0f)
                return false;

            try
            {
                Hediff existing = pawn.health?.hediffSet?.GetFirstHediffOfDef(hediffDef);
                if (existing != null)
                {
                    existing.Severity += amount;
                    return true;
                }

                Hediff hediff = HediffMaker.MakeHediff(hediffDef, pawn);
                hediff.Severity = amount;
                pawn.health.AddHediff(hediff);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Failed to apply ComboReaction hediff '{hediffDef.defName}' to '{pawn.LabelShortCap}': {ex.Message}");
                return false;
            }
        }

        private static void RemoveHediff(Pawn pawn, Hediff hediff)
        {
            if (pawn?.health == null || hediff == null)
                return;

            try
            {
                pawn.health.RemoveHediff(hediff);
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Failed to remove ComboReaction hediff '{hediff.def?.defName}' from '{pawn.LabelShortCap}': {ex.Message}");
            }
        }
    }


    // ============================================================
    // 7) HEDIFF ADDED PATCH
    // ============================================================

    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff), new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_Pawn_HealthTracker_AddHediff_ComboReaction
    {
        public static void Postfix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            try
            {
                if (__instance == null || hediff == null)
                    return;

                Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawn == null)
                    return;

                ComboReactionResolver.ResolveHediffAdded(pawn, hediff);
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] ComboReaction AddHediff postfix failed: {ex}");
            }
        }
    }
}

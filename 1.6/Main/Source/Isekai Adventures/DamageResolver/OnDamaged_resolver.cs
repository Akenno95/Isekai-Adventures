// /Source/IsekaiAdventures/Combat/OnDamaged/IsekaiAdventures_OnDamagedResolver.cs
// RimWorld 1.6 - IsekaiAdventures
// OnDamaged Modifier System - Phase 1
//
// Contains:
// - OnDamagedModifierDef
// - Hediff / Thing / GeneDefExtension OnDamaged modifier sources
// - Global OnDamaged modifiers through <global>true</global>
// - OnDamagedModifierCollector
// - OnDamagedResolver
//
// Requires:
// - Isekai_DamageResolverCore.cs
//   specifically DamageResult, DamageContext, DamageTypeSetDef
//
// Meaning:
// - OnHitModifier = attacker reacts when attacker hits something.
// - OnDamagedModifier = defender reacts when defender is hit.
//
// XML direction:
// - <applyHediff> applies to the attacker.
// - <applyHediffToSelf> applies to the defender / damaged pawn.
//
// Trigger rules:
// - By default, OnDamaged only triggers if finalDamage > 0.
// - <triggerIfNoDamage>true</triggerIfNoDamage> allows triggering on any 0-damage hit.
// - <triggerIfBlocked>true</triggerIfBlocked> allows triggering specifically when the hit was blocked.

using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    // ============================================================
    // 1) ON DAMAGED MODIFIER DEF
    // ============================================================

    /// <summary>
    /// XML-defined OnDamaged rule.
    /// This is processed after vanilla TakeDamage has run.
    /// </summary>
    public class OnDamagedModifierDef : Def
    {
        // If true, this modifier is always considered by the OnDamagedResolver,
        // even if no Hediff, Gene, Apparel or Equipment provides it.
        // Default false: modifier only works when provided by an active source.
        public bool global = false;

        // Optional exact filter. Null = no exact DamageDef filter.
        public DamageDef damageDef;

        // Optional group filter. Null = no group filter.
        public DamageTypeSetDef damageTypeSet;

        // Chance that the entire OnDamaged modifier triggers.
        // 1.0 = always, 0.25 = 25%.
        public float chance = 1f;

        // Optional stat scaling for chance.
        // finalChance = chance + Sum(stat * factor), then clamped between 0 and 1.
        public List<StatScale> chanceFromStats;

        // Default false:
        // If final damage was negated to 0, this modifier will not trigger.
        // If true, it may trigger even when finalDamage == 0.
        public bool triggerIfNoDamage = false;

        // Default false:
        // If the hit was blocked, this modifier may trigger even when finalDamage == 0.
        // This is useful for shield charge, guard break, stamina drain, boss shield mechanics, etc.
        public bool triggerIfBlocked = false;

        // Applies to the attacker / instigator.
        public HediffDef applyHediff;
        public float severity = 0.1f;

        // Applies to the defender / damaged pawn.
        public HediffDef applyHediffToSelf;
        public float severityToSelf = 0.1f;

        public bool AppliesTo(DamageDef incomingDamageDef)
        {
            if (incomingDamageDef == null)
                return false;

            // No filter = applies to all DamageDefs, but only when provided by an active source or global=true.
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
                yield return $"{defName}: OnDamagedModifierDef has no applyHediff and no applyHediffToSelf.";

            if (severity < 0f)
                yield return $"{defName}: severity should not be negative.";

            if (severityToSelf < 0f)
                yield return $"{defName}: severityToSelf should not be negative.";

            if (damageTypeSet != null && (damageTypeSet.damageDefs == null || damageTypeSet.damageDefs.Count == 0))
                yield return $"{defName}: damageTypeSet is empty.";
        }
    }

    /// <summary>
    /// DefModExtension for GeneDef or other Def-based OnDamaged modifier sources.
    /// </summary>
    public class OnDamagedModifiersExtension : DefModExtension
    {
        public List<OnDamagedModifierDef> modifiers;

        public IEnumerable<OnDamagedModifierDef> GetOnDamagedModifiers()
        {
            return modifiers ?? Enumerable.Empty<OnDamagedModifierDef>();
        }
    }


    // ============================================================
    // 2) MODIFIER SOURCE COMPS
    // ============================================================

    public interface IOnDamagedModifierProvider
    {
        IEnumerable<OnDamagedModifierDef> GetOnDamagedModifiers();
    }

    // ---------------- Hediffs ----------------

    public class HediffCompProperties_OnDamagedModifiers : HediffCompProperties
    {
        public List<OnDamagedModifierDef> modifiers;

        // OnDamagedModifiers from this Hediff only become active
        // once parent.Severity reaches this value.
        // If omitted in XML, default is 0 and the modifier is active immediately.
        public float minSeverity = 0f;

        public HediffCompProperties_OnDamagedModifiers()
        {
            compClass = typeof(HediffComp_OnDamagedModifiers);
        }
    }

    public class HediffComp_OnDamagedModifiers : HediffComp, IOnDamagedModifierProvider
    {
        public HediffCompProperties_OnDamagedModifiers Props => (HediffCompProperties_OnDamagedModifiers)props;

        public IEnumerable<OnDamagedModifierDef> GetOnDamagedModifiers()
        {
            if (Props == null)
                return Enumerable.Empty<OnDamagedModifierDef>();

            if (parent != null && parent.Severity < Props.minSeverity)
                return Enumerable.Empty<OnDamagedModifierDef>();

            return Props.modifiers ?? Enumerable.Empty<OnDamagedModifierDef>();
        }
    }

    // ---------------- Things / Apparel / Equipment ----------------

    public class CompProperties_OnDamagedModifiers : CompProperties
    {
        public List<OnDamagedModifierDef> modifiers;

        public CompProperties_OnDamagedModifiers()
        {
            compClass = typeof(CompOnDamagedModifiers);
        }
    }

    public class CompOnDamagedModifiers : ThingComp, IOnDamagedModifierProvider
    {
        public CompProperties_OnDamagedModifiers Props => (CompProperties_OnDamagedModifiers)props;

        public IEnumerable<OnDamagedModifierDef> GetOnDamagedModifiers()
        {
            return Props?.modifiers ?? Enumerable.Empty<OnDamagedModifierDef>();
        }
    }


    // ============================================================
    // 3) ON DAMAGED MODIFIER COLLECTOR
    // ============================================================

    public static class OnDamagedModifierCollector
    {
        /// <summary>
        /// OnDamaged modifiers are collected from the defender / damaged pawn.
        /// </summary>
        public static IEnumerable<OnDamagedModifierDef> CollectFor(Pawn defender, DamageDef damageDef)
        {
            if (defender == null)
                yield break;

            HashSet<OnDamagedModifierDef> yielded = new HashSet<OnDamagedModifierDef>();

            // Global modifiers: always considered for Pawns,
            // even without Hediff/Gene/Apparel/Equipment.
            foreach (OnDamagedModifierDef mod in DefDatabase<OnDamagedModifierDef>.AllDefsListForReading)
            {
                if (mod != null && mod.global && mod.AppliesTo(damageDef) && yielded.Add(mod))
                    yield return mod;
            }

            // Hediffs: defensive states, thorns, shield stances, boss shields, curses.
            if (defender.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in defender.health.hediffSet.hediffs)
                {
                    if (hediff == null)
                        continue;

                    HediffComp_OnDamagedModifiers comp = hediff.TryGetComp<HediffComp_OnDamagedModifiers>();
                    if (comp == null)
                        continue;

                    foreach (OnDamagedModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Genes: innate reactive defenses via DefModExtension.
            if (defender.genes?.GenesListForReading != null)
            {
                foreach (Gene gene in defender.genes.GenesListForReading)
                {
                    if (gene?.def == null)
                        continue;

                    OnDamagedModifiersExtension extension = gene.def.GetModExtension<OnDamagedModifiersExtension>();
                    if (extension == null)
                        continue;

                    foreach (OnDamagedModifierDef mod in extension.GetOnDamagedModifiers())
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Apparel: armor, shields, cloaks, enchanted equipment worn by defender.
            if (defender.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in defender.apparel.WornApparel)
                {
                    CompOnDamagedModifiers comp = apparel?.GetComp<CompOnDamagedModifiers>();
                    if (comp == null)
                        continue;

                    foreach (OnDamagedModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Equipment: defensive weapon reactions, parry stance, shield weapon, relics.
            ThingWithComps primary = defender.equipment?.Primary;
            if (primary != null)
            {
                CompOnDamagedModifiers comp = primary.GetComp<CompOnDamagedModifiers>();
                if (comp != null)
                {
                    foreach (OnDamagedModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }
        }

        private static IEnumerable<OnDamagedModifierDef> SafeMods(IOnDamagedModifierProvider provider)
        {
            IEnumerable<OnDamagedModifierDef> mods;

            try
            {
                mods = provider.GetOnDamagedModifiers();
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] OnDamaged modifier provider failed: {ex.Message}");
                yield break;
            }

            if (mods == null)
                yield break;

            foreach (OnDamagedModifierDef mod in mods)
            {
                if (mod != null)
                    yield return mod;
            }
        }
    }


    // ============================================================
    // 4) ON DAMAGED RESOLVER
    // ============================================================

    public static class OnDamagedResolver
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

                if (!context.canTriggerOnDamaged)
                    return;

                Pawn defender = context.TargetPawn;
                if (defender == null)
                    return;

                Pawn attacker = context.attacker;

                DamageDef damageDef = context.damageDef;
                if (damageDef == null)
                    return;

                // Snapshot the modifiers before applying effects.
                // Applying Hediffs can modify Hediff lists and break enumeration.
                List<OnDamagedModifierDef> modifiers = OnDamagedModifierCollector.CollectFor(defender, damageDef).ToList();

                foreach (OnDamagedModifierDef mod in modifiers)
                {
                    TryApplyModifier(mod, result, attacker, defender);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] OnDamagedResolver failed: {ex}");
            }
        }

        private static void TryApplyModifier(OnDamagedModifierDef mod, DamageResult result, Pawn attacker, Pawn defender)
        {
            if (mod == null || result == null || defender == null)
                return;

            bool noDamage = result.WasNegated;
            bool blocked = result.WasBlocked;

            // Default behavior: OnDamaged requires actual damage.
            // If damage was negated to 0, this modifier needs triggerIfNoDamage.
            // If damage was blocked, triggerIfBlocked is enough.
            if (noDamage)
            {
                bool allowedByNoDamage = mod.triggerIfNoDamage;
                bool allowedByBlock = blocked && mod.triggerIfBlocked;

                if (!allowedByNoDamage && !allowedByBlock)
                    return;
            }

            float chance = EvaluateChance(mod, defender);
            if (chance < 1f && Rand.Value > chance)
                return;

            bool appliedSomething = false;

            // OnDamaged applyHediff goes to attacker.
            if (mod.applyHediff != null)
            {
                if (attacker != null)
                {
                    if (ApplyHediff(attacker, mod.applyHediff, mod.severity))
                    {
                        appliedSomething = true;

                        if (Prefs.DevMode)
                            Log.Message($"[IsekaiAdventures] OnDamaged {mod.defName}: applied {mod.applyHediff.defName} to attacker {attacker.LabelShortCap}");
                    }
                }
                else if (Prefs.DevMode)
                {
                    Log.Message($"[IsekaiAdventures] OnDamaged {mod.defName}: skipped applyHediff {mod.applyHediff.defName} because attacker is null");
                }
            }

            // OnDamaged applyHediffToSelf goes to defender.
            if (mod.applyHediffToSelf != null)
            {
                if (ApplyHediff(defender, mod.applyHediffToSelf, mod.severityToSelf))
                {
                    appliedSomething = true;

                    if (Prefs.DevMode)
                        Log.Message($"[IsekaiAdventures] OnDamaged {mod.defName}: applied {mod.applyHediffToSelf.defName} to defender {defender.LabelShortCap}");
                }
            }

            if (Prefs.DevMode && appliedSomething)
            {
                string attackerLabel = attacker != null ? attacker.LabelShortCap : "unknown attacker";
                string defenderLabel = defender.LabelShortCap ?? "unknown defender";
                string damageText;

                if (result.WasBlocked)
                    damageText = "blocked hit";
                else if (result.WasNegated)
                    damageText = "negated hit";
                else
                    damageText = $"{result.finalDamage:0.##} damage";

                Log.Message($"[IsekaiAdventures] OnDamaged {mod.defName}: {attackerLabel} -> {defenderLabel}, {result.context.damageDef.defName}, {damageText}");
            }
        }

        private static float EvaluateChance(OnDamagedModifierDef mod, Pawn defender)
        {
            if (mod == null)
                return 0f;

            float finalChance = mod.chance;

            if (mod.chanceFromStats != null)
            {
                foreach (StatScale scale in mod.chanceFromStats)
                {
                    if (scale != null)
                        finalChance += scale.Evaluate(defender);
                }
            }

            return Mathf.Clamp01(finalChance);
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
                Log.Warning($"[IsekaiAdventures] Failed to apply OnDamaged hediff '{hediffDef.defName}' to '{pawn.LabelShortCap}': {ex.Message}");
                return false;
            }
        }
    }
}

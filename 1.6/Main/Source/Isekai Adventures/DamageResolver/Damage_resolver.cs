// /Source/IsekaiAdventures/Combat/Isekai_DamageResolverCore.cs
// RimWorld 1.6 - IsekaiAdventures
// Damage Resolver Core - Phase 2
//
// Contains:
// - DamageModifierDef
// - DamageTypeSetDef
// - StatScale
// - DamageContext
// - DamageResult
// - Hediff / Thing / GeneDefExtension modifier sources
// - DamageResolver
// - BlockChance / IgnoreBlockChance
// - Harmony hook into Thing.TakeDamage
//
// Notes:
// - This file does NOT contain Harmony.PatchAll().
// - Your central IsekaiAdventures_Startup should call Harmony.PatchAll() once.
// - Genes use DefModExtension here, not GeneComp.
// - Hediffs and Things/Apparel/Equipment use normal comps.
// - New XML should prefer percentBonusFromStats / percentReductionFromStats.
// - Singular percentBonusFromStat / percentReductionFromStat remain supported for compatibility.

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
    // 1) XML DATA STRUCTURES
    // ============================================================

    /// <summary>
    /// A named group of DamageDefs.
    /// Example: Magic = Isekai_MagicSlashDamage, Isekai_FireMagicDamage, Isekai_HolyDamage.
    /// DamageModifierDef can reference this through <damageTypeSet>...</damageTypeSet>.
    /// </summary>
    public class DamageTypeSetDef : Def
    {
        public List<DamageDef> damageDefs;

        public bool Contains(DamageDef damageDef)
        {
            return damageDef != null && damageDefs != null && damageDefs.Contains(damageDef);
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (damageDefs == null || damageDefs.Count == 0)
                yield return $"{defName}: DamageTypeSetDef has no damageDefs.";
        }
    }

    /// <summary>
    /// One stat scaling entry.
    /// Example XML:
    /// <li>
    ///   <stat>Isekai_TestPower</stat>
    ///   <factor>0.02</factor>
    /// </li>
    /// </summary>
    public class StatScale
    {
        public StatDef stat;
        public float factor = 1f;

        public float Evaluate(Pawn pawn)
        {
            if (pawn == null || stat == null)
                return 0f;

            try
            {
                return pawn.GetStatValue(stat) * factor;
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Failed to read stat '{stat?.defName}' for damage scaling: {ex.Message}");
                return 0f;
            }
        }
    }

    /// <summary>
    /// XML-defined damage modifier rule.
    /// This is not damage by itself. It only contributes numbers to the DamageResolver.
    /// </summary>
    public class DamageModifierDef : Def
    {
        // If true, this modifier is always considered by the DamageResolver,
        // even if no Hediff, Gene, Apparel or Equipment provides it.
        // Default false: modifier only works when provided by an active source.
        public bool global = false;

        // Optional exact filter. Null = no exact DamageDef filter.
        public DamageDef damageDef;

        // Optional group filter. Null = no group filter.
        public DamageTypeSetDef damageTypeSet;

        // Offensive contributions, usually collected from attacker.
        public float flatBonusDamage = 0f;
        public float percentBonusDamage = 0f;

        // Legacy/single-entry field. Prefer percentBonusFromStats for new XML.
        public StatScale percentBonusFromStat;
        public List<StatScale> percentBonusFromStats;

        // Offensive anti-block contributions.
        // ignoreBlockChance = chance to bypass defensive blockChance.
        public float ignoreBlockChance = 0f;
        public List<StatScale> ignoreBlockChanceFromStats;

        // Defensive contributions, usually collected from target.
        public float flatReduction = 0f;
        public float percentReduction = 0f;

        // Legacy/single-entry field. Prefer percentReductionFromStats for new XML.
        public StatScale percentReductionFromStat;
        public List<StatScale> percentReductionFromStats;

        // Defensive block contributions.
        // blockChance = chance to fully block/negate incoming damage before normal damage calculation.
        public float blockChance = 0f;
        public List<StatScale> blockChanceFromStats;

        public bool AppliesTo(DamageDef incomingDamageDef)
        {
            if (incomingDamageDef == null)
                return false;

            // No filter = applies to all DamageDefs, but only when provided by an active source.
            if (damageDef == null && damageTypeSet == null)
                return true;

            // Exact DamageDef filter.
            if (damageDef != null && damageDef == incomingDamageDef)
                return true;

            // Group filter.
            if (damageTypeSet != null && damageTypeSet.Contains(incomingDamageDef))
                return true;

            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string err in base.ConfigErrors())
                yield return err;

            if (percentBonusDamage < -0.99f)
                yield return $"{defName}: percentBonusDamage below -99% can create strange results.";

            // Negative percentReduction is allowed intentionally.
            // Example: <percentReduction>-0.20</percentReduction> means target takes 20% more damage.

            if (percentBonusFromStat != null && percentBonusFromStat.stat == null)
                yield return $"{defName}: percentBonusFromStat has no stat.";

            if (percentReductionFromStat != null && percentReductionFromStat.stat == null)
                yield return $"{defName}: percentReductionFromStat has no stat.";

            if (damageTypeSet != null && (damageTypeSet.damageDefs == null || damageTypeSet.damageDefs.Count == 0))
                yield return $"{defName}: damageTypeSet is empty.";

            if (percentBonusFromStats != null)
            {
                foreach (StatScale scale in percentBonusFromStats)
                {
                    if (scale == null || scale.stat == null)
                        yield return $"{defName}: percentBonusFromStats contains an entry without stat.";
                }
            }

            if (percentReductionFromStats != null)
            {
                foreach (StatScale scale in percentReductionFromStats)
                {
                    if (scale == null || scale.stat == null)
                        yield return $"{defName}: percentReductionFromStats contains an entry without stat.";
                }
            }

            if (blockChanceFromStats != null)
            {
                foreach (StatScale scale in blockChanceFromStats)
                {
                    if (scale == null || scale.stat == null)
                        yield return $"{defName}: blockChanceFromStats contains an entry without stat.";
                }
            }

            if (ignoreBlockChanceFromStats != null)
            {
                foreach (StatScale scale in ignoreBlockChanceFromStats)
                {
                    if (scale == null || scale.stat == null)
                        yield return $"{defName}: ignoreBlockChanceFromStats contains an entry without stat.";
                }
            }
        }
    }

    /// <summary>
    /// DefModExtension for GeneDef or other Def-based modifier sources.
    /// </summary>
    public class DamageModifiersExtension : DefModExtension
    {
        public List<DamageModifierDef> modifiers;

        public IEnumerable<DamageModifierDef> GetDamageModifiers()
        {
            return modifiers ?? Enumerable.Empty<DamageModifierDef>();
        }
    }


    // ============================================================
    // 2) CONTEXT AND RESULT
    // ============================================================

    public enum DamageOrigin
    {
        Primary,
        OnHit,
        Secondary,
        DoT
    }

    /// <summary>
    /// Describes where damage came from.
    /// Later this will help prevent recursion between ComboReaction, OnHit and OnDamaged reactions.
    /// </summary>
    public class DamageContext
    {
        public Pawn attacker;
        public Thing target;
        public DamageDef damageDef;
        public float baseDamage;

        public DamageOrigin origin = DamageOrigin.Primary;
        public bool canTriggerOnHit = true;
        public bool canTriggerOnDamaged = true;
        public bool canTriggerComboReaction = true;
        public int chainDepth = 0;

        public Pawn TargetPawn => target as Pawn;

        public DamageContext(
            Pawn attacker,
            Thing target,
            DamageDef damageDef,
            float baseDamage,
            DamageOrigin origin = DamageOrigin.Primary,
            bool canTriggerOnHit = true,
            bool canTriggerOnDamaged = true,
            bool canTriggerComboReaction = true,
            int chainDepth = 0)
        {
            this.attacker = attacker;
            this.target = target;
            this.damageDef = damageDef;
            this.baseDamage = baseDamage;
            this.origin = origin;
            this.canTriggerOnHit = canTriggerOnHit;
            this.canTriggerOnDamaged = canTriggerOnDamaged;
            this.canTriggerComboReaction = canTriggerComboReaction;
            this.chainDepth = chainDepth;
        }
    }

    /// <summary>
    /// Result object for the damage calculation.
    /// This will be used later by OnHitModifier / OnDamagedModifier / ComboReactionModifier.
    /// </summary>
    public class DamageResult
    {
        public DamageContext context;
        public float baseDamage;
        public float finalDamage;
        public bool hadContributions;
        public bool wasBlocked;
        public bool ignoreBlockSucceeded;

        public bool DamageChanged => Mathf.Abs(finalDamage - baseDamage) > 0.001f;
        public bool WasNegated => baseDamage > 0f && finalDamage <= 0.001f;
        public bool WasBlocked => wasBlocked;

        public DamageResult(DamageContext context, float baseDamage, float finalDamage, bool hadContributions, bool wasBlocked = false, bool ignoreBlockSucceeded = false)
        {
            this.context = context;
            this.baseDamage = baseDamage;
            this.finalDamage = finalDamage;
            this.hadContributions = hadContributions;
            this.wasBlocked = wasBlocked;
            this.ignoreBlockSucceeded = ignoreBlockSucceeded;
        }
    }

    /// <summary>
    /// Internal math bucket.
    /// Modifiers add to this. Only DamageResolver calculates final damage.
    /// </summary>
    public class DamageCalculationContext
    {
        public float flatBonus;
        public float percentBonus;
        public float flatReduction;
        public float percentReduction;

        public float blockChance;
        public float ignoreBlockChance;

        public bool hasContributions;
        public readonly List<string> debugLines = new List<string>();

        public void AddDebug(string line)
        {
            if (Prefs.DevMode && !line.NullOrEmpty())
                debugLines.Add(line);
        }

        public void MarkContribution(string debugLine)
        {
            hasContributions = true;
            AddDebug(debugLine);
        }
    }


    // ============================================================
    // 3) MODIFIER SOURCE COMPS
    // ============================================================

    public interface IDamageModifierProvider
    {
        IEnumerable<DamageModifierDef> GetDamageModifiers();
    }

    // ---------------- Hediffs ----------------

    public class HediffCompProperties_DamageModifiers : HediffCompProperties
    {
        public List<DamageModifierDef> modifiers;

        // DamageModifiers from this Hediff only become active once parent.Severity reaches this value.
        // If omitted in XML, default is 0 and the modifier is active immediately.
        public float minSeverity = 0f;

        public HediffCompProperties_DamageModifiers()
        {
            compClass = typeof(HediffComp_DamageModifiers);
        }
    }

    public class HediffComp_DamageModifiers : HediffComp, IDamageModifierProvider
    {
        public HediffCompProperties_DamageModifiers Props => (HediffCompProperties_DamageModifiers)props;

        public IEnumerable<DamageModifierDef> GetDamageModifiers()
        {
            if (Props == null)
                return Enumerable.Empty<DamageModifierDef>();

            if (parent != null && parent.Severity < Props.minSeverity)
                return Enumerable.Empty<DamageModifierDef>();

            return Props.modifiers ?? Enumerable.Empty<DamageModifierDef>();
        }
    }

    // ---------------- Things / Apparel / Equipment ----------------

    public class CompProperties_DamageModifiers : CompProperties
    {
        public List<DamageModifierDef> modifiers;

        public CompProperties_DamageModifiers()
        {
            compClass = typeof(CompDamageModifiers);
        }
    }

    public class CompDamageModifiers : ThingComp, IDamageModifierProvider
    {
        public CompProperties_DamageModifiers Props => (CompProperties_DamageModifiers)props;

        public IEnumerable<DamageModifierDef> GetDamageModifiers()
        {
            return Props?.modifiers ?? Enumerable.Empty<DamageModifierDef>();
        }
    }


    // ============================================================
    // 4) MODIFIER COLLECTOR
    // ============================================================

    public enum ModifierSide
    {
        Offensive,
        Defensive
    }

    public static class DamageModifierCollector
    {
        public static IEnumerable<DamageModifierDef> CollectFor(Pawn pawn, DamageDef damageDef, ModifierSide side)
        {
            if (pawn == null)
                yield break;

            HashSet<DamageModifierDef> yielded = new HashSet<DamageModifierDef>();

            // Global modifiers: always considered for Pawns,
            // even without Hediff/Gene/Apparel/Equipment.
            // We intentionally require pawn != null so global combat rules
            // do not accidentally affect apparel, weapons, buildings or plants.
            foreach (DamageModifierDef mod in DefDatabase<DamageModifierDef>.AllDefsListForReading)
            {
                if (mod != null && mod.global && mod.AppliesTo(damageDef) && yielded.Add(mod))
                    yield return mod;
            }

            // Hediffs: buffs, curses, temporary states.
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff == null)
                        continue;

                    HediffComp_DamageModifiers comp = hediff.TryGetComp<HediffComp_DamageModifiers>();
                    if (comp == null)
                        continue;

                    foreach (DamageModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Genes: innate bloodline/species modifiers via DefModExtension.
            if (pawn.genes?.GenesListForReading != null)
            {
                foreach (Gene gene in pawn.genes.GenesListForReading)
                {
                    if (gene?.def == null)
                        continue;

                    DamageModifiersExtension extension = gene.def.GetModExtension<DamageModifiersExtension>();
                    if (extension == null)
                        continue;

                    foreach (DamageModifierDef mod in extension.GetDamageModifiers())
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Apparel: mostly defensive, but allowed offensively too for enchanted gear.
            if (pawn.apparel?.WornApparel != null)
            {
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    CompDamageModifiers comp = apparel?.GetComp<CompDamageModifiers>();
                    if (comp == null)
                        continue;

                    foreach (DamageModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }

            // Equipment: weapons, staffs, relics.
            ThingWithComps primary = pawn.equipment?.Primary;
            if (primary != null)
            {
                CompDamageModifiers comp = primary.GetComp<CompDamageModifiers>();
                if (comp != null)
                {
                    foreach (DamageModifierDef mod in SafeMods(comp))
                    {
                        if (mod != null && mod.AppliesTo(damageDef) && yielded.Add(mod))
                            yield return mod;
                    }
                }
            }
        }

        private static IEnumerable<DamageModifierDef> SafeMods(IDamageModifierProvider provider)
        {
            IEnumerable<DamageModifierDef> mods;

            try
            {
                mods = provider.GetDamageModifiers();
            }
            catch (Exception ex)
            {
                Log.Warning($"[IsekaiAdventures] Damage modifier provider failed: {ex.Message}");
                yield break;
            }

            if (mods == null)
                yield break;

            foreach (DamageModifierDef mod in mods)
            {
                if (mod != null)
                    yield return mod;
            }
        }
    }


    // ============================================================
    // 5) DAMAGE RESOLVER
    // ============================================================

    public static class DamageResolver
    {
        // Hard safety caps. Keep in code, not XML.
        public const float MaxPercentBonus = 3.00f;          // +300%
        public const float MaxPercentReduction = 1.00f;      // 100% - allows full damage negation
        public const float MaxBlockChance = 1.00f;           // 100%
        public const float MaxIgnoreBlockChance = 1.00f;     // 100%
        public const float MinFinalDamage = 0f;

        public static float Resolve(DamageContext context)
        {
            return ResolveDetailed(context).finalDamage;
        }

        public static float Resolve(DamageContext context, out bool hadContributions)
        {
            DamageResult result = ResolveDetailed(context);
            hadContributions = result.hadContributions;
            return result.finalDamage;
        }

        public static DamageResult ResolveDetailed(DamageContext context)
        {
            if (context == null)
                return new DamageResult(null, 0f, 0f, false);

            if (context.damageDef == null)
            {
                float safeDamage = Mathf.Max(MinFinalDamage, context.baseDamage);
                return new DamageResult(context, context.baseDamage, safeDamage, false);
            }

            DamageCalculationContext calc = new DamageCalculationContext();

            // 1) Offensive modifiers from attacker.
            foreach (DamageModifierDef mod in DamageModifierCollector.CollectFor(context.attacker, context.damageDef, ModifierSide.Offensive))
            {
                ApplyOffensiveModifier(calc, mod, context.attacker);
            }

            // 2) Defensive modifiers from target pawn.
            Pawn targetPawn = context.TargetPawn;
            foreach (DamageModifierDef mod in DamageModifierCollector.CollectFor(targetPawn, context.damageDef, ModifierSide.Defensive))
            {
                ApplyDefensiveModifier(calc, mod, targetPawn);
            }

            bool hadContributions = calc.hasContributions;

            // 3) Clamp accumulated percentages and chances.
            calc.percentBonus = Mathf.Clamp(calc.percentBonus, -0.99f, MaxPercentBonus);
            calc.percentReduction = Mathf.Min(calc.percentReduction, MaxPercentReduction);

            float rawIgnoreBlockChance = calc.ignoreBlockChance;
            float rawBlockChance = calc.blockChance;
            calc.ignoreBlockChance = Mathf.Clamp(calc.ignoreBlockChance, 0f, MaxIgnoreBlockChance);
            calc.blockChance = Mathf.Clamp(calc.blockChance, 0f, MaxBlockChance);

            // 4) Block / Anti-Block phase.
            // First the attacker may bypass block. If this fails, the defender may block.
            bool ignoreBlockSucceeded = false;

            if (calc.ignoreBlockChance > 0f && Rand.Value < calc.ignoreBlockChance)
            {
                ignoreBlockSucceeded = true;
                if (Prefs.DevMode)
                    calc.AddDebug($"IGNORE_BLOCK success (raw {rawIgnoreBlockChance:P0}, final {calc.ignoreBlockChance:P0})");
            }
            else if (calc.ignoreBlockChance > 0f && Prefs.DevMode)
            {
                calc.AddDebug($"IGNORE_BLOCK failed (raw {rawIgnoreBlockChance:P0}, final {calc.ignoreBlockChance:P0})");
            }

            if (!ignoreBlockSucceeded && calc.blockChance > 0f && Rand.Value < calc.blockChance)
            {
                float blockedDamage = 0f;
                DamageResult blockedResult = new DamageResult(context, context.baseDamage, blockedDamage, true, wasBlocked: true, ignoreBlockSucceeded: false);

                if (Prefs.DevMode)
                {
                    calc.AddDebug($"BLOCKED (raw {rawBlockChance:P0}, final {calc.blockChance:P0})");
                    calc.AddDebug($"Base={context.baseDamage:0.##}, Final=0, BLOCKED, NEGATED");
                    Log.Message($"[IsekaiAdventures] DamageResolver {context.damageDef.defName}: {string.Join(" | ", calc.debugLines)}");
                }

                return blockedResult;
            }
            else if (!ignoreBlockSucceeded && calc.blockChance > 0f && Prefs.DevMode)
            {
                calc.AddDebug($"BLOCK failed (raw {rawBlockChance:P0}, final {calc.blockChance:P0})");
            }

            // 5) Fixed deterministic damage order.
            float finalDamage = context.baseDamage;
            finalDamage += calc.flatBonus;
            finalDamage *= 1f + calc.percentBonus;
            finalDamage -= calc.flatReduction;
            finalDamage *= 1f - calc.percentReduction;
            finalDamage = Mathf.Max(MinFinalDamage, finalDamage);

            DamageResult result = new DamageResult(context, context.baseDamage, finalDamage, hadContributions, wasBlocked: false, ignoreBlockSucceeded: ignoreBlockSucceeded);

            if (Prefs.DevMode && hadContributions)
            {
                string status = string.Empty;
                if (result.WasNegated)
                    status += ", NEGATED";
                if (ignoreBlockSucceeded)
                    status += ", IGNORE_BLOCK";

                calc.AddDebug($"Base={context.baseDamage:0.##}, FlatBonus={calc.flatBonus:0.##}, PercentBonus={calc.percentBonus:P0}, FlatReduction={calc.flatReduction:0.##}, PercentReduction={calc.percentReduction:P0}, Final={finalDamage:0.##}{status}");
                Log.Message($"[IsekaiAdventures] DamageResolver {context.damageDef.defName}: {string.Join(" | ", calc.debugLines)}");
            }

            return result;
        }

        private static void ApplyOffensiveModifier(DamageCalculationContext calc, DamageModifierDef mod, Pawn attacker)
        {
            if (mod == null)
                return;

            float beforeFlatBonus = calc.flatBonus;
            float beforePercentBonus = calc.percentBonus;
            float beforeIgnoreBlockChance = calc.ignoreBlockChance;

            calc.flatBonus += mod.flatBonusDamage;
            calc.percentBonus += mod.percentBonusDamage;
            calc.ignoreBlockChance += mod.ignoreBlockChance;

            if (mod.percentBonusFromStat != null)
                calc.percentBonus += mod.percentBonusFromStat.Evaluate(attacker);

            if (mod.percentBonusFromStats != null)
            {
                foreach (StatScale scale in mod.percentBonusFromStats)
                {
                    if (scale != null)
                        calc.percentBonus += scale.Evaluate(attacker);
                }
            }

            if (mod.ignoreBlockChanceFromStats != null)
            {
                foreach (StatScale scale in mod.ignoreBlockChanceFromStats)
                {
                    if (scale != null)
                        calc.ignoreBlockChance += scale.Evaluate(attacker);
                }
            }

            float addedFlat = calc.flatBonus - beforeFlatBonus;
            float addedPercent = calc.percentBonus - beforePercentBonus;
            float addedIgnoreBlock = calc.ignoreBlockChance - beforeIgnoreBlockChance;

            if (Mathf.Abs(addedFlat) > 0.0001f || Mathf.Abs(addedPercent) > 0.0001f || Mathf.Abs(addedIgnoreBlock) > 0.0001f)
            {
                calc.MarkContribution($"OFF {mod.defName} (+Flat {addedFlat:0.##}, +Pct {addedPercent:P0}, +IgnoreBlock {addedIgnoreBlock:P0})");
            }
        }

        private static void ApplyDefensiveModifier(DamageCalculationContext calc, DamageModifierDef mod, Pawn target)
        {
            if (mod == null)
                return;

            float beforeFlatReduction = calc.flatReduction;
            float beforePercentReduction = calc.percentReduction;
            float beforeBlockChance = calc.blockChance;

            calc.flatReduction += mod.flatReduction;
            calc.percentReduction += mod.percentReduction;
            calc.blockChance += mod.blockChance;

            if (mod.percentReductionFromStat != null)
                calc.percentReduction += mod.percentReductionFromStat.Evaluate(target);

            if (mod.percentReductionFromStats != null)
            {
                foreach (StatScale scale in mod.percentReductionFromStats)
                {
                    if (scale != null)
                        calc.percentReduction += scale.Evaluate(target);
                }
            }

            if (mod.blockChanceFromStats != null)
            {
                foreach (StatScale scale in mod.blockChanceFromStats)
                {
                    if (scale != null)
                        calc.blockChance += scale.Evaluate(target);
                }
            }

            float addedFlatReduction = calc.flatReduction - beforeFlatReduction;
            float addedPercentReduction = calc.percentReduction - beforePercentReduction;
            float addedBlockChance = calc.blockChance - beforeBlockChance;

            if (Mathf.Abs(addedFlatReduction) > 0.0001f || Mathf.Abs(addedPercentReduction) > 0.0001f || Mathf.Abs(addedBlockChance) > 0.0001f)
            {
                calc.MarkContribution($"DEF {mod.defName} (-Flat {addedFlatReduction:0.##}, -Pct {addedPercentReduction:P0}, +Block {addedBlockChance:P0})");
            }
        }
    }


    // ============================================================
    // 6) HARMONY PATCH: THING.TAKEDAMAGE
    // ============================================================

    /// <summary>
    /// Bridge into vanilla RimWorld damage.
    /// It changes only the DamageInfo amount before vanilla applies injuries.
    /// </summary>
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage_DamageResolver
    {
        /// <summary>
        /// Prefix phase:
        /// - Calculate final damage.
        /// - Store the DamageResult in __state for the postfix.
        /// - Replace DamageInfo amount before vanilla applies injury logic.
        /// </summary>
        public static void Prefix(Thing __instance, ref DamageInfo dinfo, ref DamageResult __state)
        {
            __state = null;

            try
            {
                if (__instance == null)
                    return;

                DamageDef damageDef = dinfo.Def;
                if (damageDef == null)
                    return;

                float baseDamage = dinfo.Amount;
                if (baseDamage <= 0f)
                    return;

                Pawn attacker = dinfo.Instigator as Pawn;

                DamageContext context = new DamageContext(
                    attacker: attacker,
                    target: __instance,
                    damageDef: damageDef,
                    baseDamage: baseDamage,
                    origin: DamageOrigin.Primary,
                    canTriggerOnHit: true,
                    canTriggerOnDamaged: true,
                    canTriggerComboReaction: true,
                    chainDepth: 0
                );

                DamageResult result = DamageResolver.ResolveDetailed(context);

                // Store the result even when DamageResolver did not modify damage.
                // OnHitModifier may still need to trigger after a normal vanilla hit.
                __state = result;

                if (!result.hadContributions)
                    return;

                if (result.DamageChanged)
                {
                    dinfo.SetAmount(result.finalDamage);

                    if (Prefs.DevMode)
                    {
                        string attackerLabel = attacker != null ? attacker.LabelShortCap : "unknown attacker";
                        string targetLabel = __instance.LabelShortCap ?? __instance.def?.defName ?? "unknown target";

                        string resultText;
                        if (result.WasBlocked)
                            resultText = " (blocked)";
                        else if (result.WasNegated)
                            resultText = " (negated)";
                        else if (result.ignoreBlockSucceeded)
                            resultText = " (ignore block)";
                        else
                            resultText = string.Empty;

                        Log.Message($"[IsekaiAdventures] TakeDamage patched: {attackerLabel} -> {targetLabel}, {damageDef.defName}, {baseDamage:0.##} => {result.finalDamage:0.##}{resultText}");
                    }
                }
            }
            catch (Exception ex)
            {
                __state = null;
                Log.Error($"[IsekaiAdventures] DamageResolver TakeDamage prefix failed. Vanilla damage will continue. Error: {ex}");
            }
        }

        /// <summary>
        /// Postfix phase:
        /// - Vanilla damage has now been applied.
        /// - OnHitModifier is resolved after vanilla damage.
        /// </summary>
        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageResult __state)
        {
            try
            {
                if (__state == null)
                    return;

                OnHitResolver.Resolve(__state);
                OnDamagedResolver.Resolve(__state);
                ComboReactionResolver.Resolve(__state);
            }
            catch (Exception ex)
            {
                Log.Error($"[IsekaiAdventures] DamageResolver postfix failed while resolving OnHit modifiers: {ex}");
            }
        }
    }
}

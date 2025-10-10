using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;

namespace IsekaiAdventures
{
    [StaticConstructorOnStartup]
    public static class IA_MaxHP_PatchInit
    {
        static IA_MaxHP_PatchInit()
        {
            var h = new Harmony("IsekaiAdventures.MaxHPBonus");
            int ok = 0;

            // --- Helpers ------------------------------------------------------
            void PatchWith(Harmony h2, MethodInfo mi, string label, string postfixName)
            {
                if (mi == null) return;
                try
                {
                    var hm = new HarmonyMethod(typeof(IA_MaxHP_Postfixes), postfixName)
                    {
                        priority = Priority.Last
                    };
                    h2.Patch(mi, postfix: hm);
                    ok++;
                }
                catch (Exception e)
                {
                    Log.Warning($"[IA.MaxHP] Patch fehlgeschlagen: {label}: {e}");
                }
            }

            void PatchEBF(string typeName, string methodName, Type[] sig, string postfixName)
            {
                var t = AccessTools.TypeByName(typeName);
                if (t == null) return;

                var mi = AccessTools.Method(t, methodName, sig);
                if (mi == null) return;

                PatchWith(h, mi, $"{typeName}.{methodName}", postfixName);
            }
            // ------------------------------------------------------------------

            // EBF vorhanden?
            bool ebfPresent = AccessTools.TypeByName("EBF.EBFEndpoints") != null
                           || AccessTools.TypeByName("EBF.VanillaExtender") != null;

            if (ebfPresent)
            {
                // ----- Nur EBF patchen (kein Vanilla, verhindert Doppel-Boni) -----
                PatchEBF("EBF.VanillaExtender", "GetRawMaxHealth",
                    new[] { typeof(BodyPartDef), typeof(Pawn) },
                    nameof(IA_MaxHP_Postfixes.PostfixEBF_Def));

                PatchEBF("EBF.EBFEndpoints", "GetMaxHealthUnmodified",
                    new[] { typeof(BodyPartDef), typeof(Pawn) },
                    nameof(IA_MaxHP_Postfixes.PostfixEBF_Def));

                PatchEBF("EBF.EBFEndpoints", "GetMaxHealthWithEBF",
                    new[] { typeof(BodyPartRecord), typeof(Pawn), typeof(bool) },
                    nameof(IA_MaxHP_Postfixes.PostfixEBF));

                PatchEBF("EBF.VanillaExtender", "GetRawMaxHealth_Cached",
                    new[] { typeof(BodyPartDef), typeof(Pawn), typeof(BodyPartRecord) },
                    nameof(IA_MaxHP_Postfixes.PostfixEBF_Def));
            }
            else
            {
                // ----- Nur Vanilla patchen -----
                var huCandidates = AccessTools.GetDeclaredMethods(typeof(HealthUtility))
                    .Where(m =>
                        m.ReturnType == typeof(float) &&
                        m.GetParameters().Length >= 2 &&
                        m.GetParameters()[0].ParameterType == typeof(Pawn) &&
                        typeof(BodyPartRecord).IsAssignableFrom(m.GetParameters()[1].ParameterType) &&
                        m.Name.IndexOf("Max", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        m.Name.IndexOf("Part", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        m.Name.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct()
                    .ToList();

                foreach (var mi in huCandidates)
                    PatchWith(h, mi, $"HealthUtility.{mi.Name}", nameof(IA_MaxHP_Postfixes.PostfixHU));

                PatchWith(h, AccessTools.Method(typeof(BodyPartRecord), "GetMaxHealth", new[] { typeof(Pawn) }),
                    "BodyPartRecord.GetMaxHealth", nameof(IA_MaxHP_Postfixes.PostfixBPR));

                PatchWith(h, AccessTools.Method(typeof(BodyPartDef), "GetMaxHealth", new[] { typeof(Pawn) }),
                    "BodyPartDef.GetMaxHealth", nameof(IA_MaxHP_Postfixes.PostfixBPD));
            }

            if (ok == 0)
                Log.Error("[IA.MaxHP] Keiner der erwarteten MaxHP-Hooks wurde gefunden! Bitte RW-/Mod-Version prüfen.");
        }
    }

    // Reentrancy-Guard: verhindert Doppelanwendung im selben Call-Stack
    static class IA_MaxHP_Reentry
    {
        [ThreadStatic] public static int Depth;
    }

    public static class IA_MaxHP_Postfixes
    {
        // ------- Vanilla -------
        [HarmonyPriority(Priority.Last)]
        public static void PostfixHU(Pawn pawn, BodyPartRecord part, ref float __result)
            => ApplyBonuses(pawn, part, ref __result);

        [HarmonyPriority(Priority.Last)]
        public static void PostfixBPR(BodyPartRecord __instance, Pawn pawn, ref float __result)
            => ApplyBonuses(pawn, __instance, ref __result);

        [HarmonyPriority(Priority.Last)]
        public static void PostfixBPD(BodyPartDef __instance, Pawn pawn, ref float __result)
        {
            var part = pawn?.health?.hediffSet?.GetNotMissingParts()?.FirstOrDefault(p => p.def == __instance);
            if (part == null) return;
            ApplyBonuses(pawn, part, ref __result);
        }

        // ------- EBF (Positions-Argumente) -------
        [HarmonyPriority(Priority.Last)]
        public static void PostfixEBF(BodyPartRecord __0, Pawn __1, ref float __result)
            => ApplyBonuses(__1, __0, ref __result);

        [HarmonyPriority(Priority.Last)]
        public static void PostfixEBF_Def(BodyPartDef __0, Pawn __1, ref float __result)
        {
            var part = __1?.health?.hediffSet?.GetNotMissingParts()?.FirstOrDefault(p => p.def == __0);
            if (part == null) return;
            ApplyBonuses(__1, part, ref __result);
        }

        // ------- Gemeinsame Logik -------
        private static void ApplyBonuses(Pawn pawn, BodyPartRecord part, ref float result)
        {
            if (IA_MaxHP_Reentry.Depth > 0) return; // 1x pro Stack
            IA_MaxHP_Reentry.Depth++;
            try
            {
                if (pawn?.health?.hediffSet == null || part == null) return;

                var buffs = pawn.health.hediffSet.hediffs.OfType<IsekaiAdventures.Hediff_MaxHPBonus>().ToList();
                if (buffs.Count == 0) return;

                float addFlat = 0f;
                float addPct = 0f;
                int stacksUsed = 0;

                foreach (var h in buffs)
                {
                    var ext = h.Ext;
                    if (ext == null) continue;

                    bool appliesToThisPart = (h.Part == null) || (h.Part == part);
                    if (!appliesToThisPart) continue;

                    if (ext.onlyNatural && pawn.health.hediffSet.hediffs.Any(x => x is Hediff_AddedPart && x.Part == part))
                        continue;

                    if (IsExcluded(part.def, ext)) continue;
                    if (h.Part != null && !ExtAllowsPart(part.def, ext)) continue;

                    float mult = Mathf.Max(1f, h.Severity);

                    addFlat += ext.flatBonus * mult;
                    addPct += ext.percentBonus * mult;
                    stacksUsed++;

                    if (!ext.stackMultiples && stacksUsed > 0) break;
                    if (ext.maxStacks > 0 && stacksUsed >= ext.maxStacks) break;
                }

                if (addFlat == 0f && Mathf.Abs(addPct) < 0.0001f) return;

                float baseVal = result;
                float after = Mathf.Max(1f, baseVal * (1f + addPct) + addFlat);
                result = after;
            }
            finally
            {
                IA_MaxHP_Reentry.Depth--;
            }
        }

        private static bool IsExcluded(BodyPartDef target, MaxHPBonusExtension ext)
        {
            if (ext == null) return false;
            if (ext.excludeParts != null && ext.excludeParts.Contains(target)) return true;
            if (ext.excludePartTags != null && HasAnyTag(target, ext.excludePartTags)) return true;
            return false;
        }

        private static bool ExtAllowsPart(BodyPartDef target, MaxHPBonusExtension ext)
        {
            if (ext == null) return false;
            if (ext.applyToAll) return true;
            if (ext.includeParts != null && ext.includeParts.Contains(target)) return true;
            if (ext.includePartTags != null && HasAnyTag(target, ext.includePartTags)) return true;
            return false;
        }

        private static bool HasAnyTag(BodyPartDef def, List<string> tagNames)
        {
            if (def == null || tagNames == null || tagNames.Count == 0) return false;
            var tags = def.tags;
            if (tags == null || tags.Count == 0) return false;
            return tagNames.Any(want => tags.Any(td => td?.defName == want));
        }
    }
}

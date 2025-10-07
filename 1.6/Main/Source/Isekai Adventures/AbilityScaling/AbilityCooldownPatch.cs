// /Source/IsekaiAdventures/Harmony/AbilityCooldownPatch.cs
using HarmonyLib;
using Verse;
using RimWorld;
using UnityEngine;
using System;

namespace IsekaiAdventures
{

    // Trifft genau: void Ability.StartCooldown(int ticks)
    [HarmonyPatch(typeof(Ability), nameof(Ability.StartCooldown), new Type[] { typeof(int) })]
    public static class Patch_Ability_StartCooldown
    {
        static void Prefix(Ability __instance, ref int ticks)
        {
            var pawn = __instance?.pawn;
            var ext = __instance?.def?.GetModExtension<IsekaiAdventures.MDO_BurstScaling>();
            if (pawn == null || ext == null) return;

            float factor = Mathf.Max(0.05f, pawn.GetStatOr(ext.cooldownFactorStat, ext.fallbackCooldownFactor));
            ticks = Mathf.Max(0, Mathf.RoundToInt(ticks * factor)); // direkt den Parameter skalieren
        }
    }


}

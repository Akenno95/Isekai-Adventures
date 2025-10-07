// /Source/IsekaiAdventures/Harmony/VerbBurstPostfix.cs
using HarmonyLib;
using Verse;
using RimWorld;
using System.Linq;
using UnityEngine;

namespace IsekaiAdventures
{
    [HarmonyPatch(typeof(Verb), "TryCastNextBurstShot")]
    public static class Patch_Verb_TryCastNextBurstShot
    {
        static void Postfix(Verb __instance)
        {
            // Wir targeten nur unsere Ability-Verb-Instanzen:
            if (__instance is not Verb_AbilityBurstScaling v) return;

            // Ext & Pawn ermitteln
            var ext = v.Ability?.def?.GetModExtension<MDO_BurstScaling>();
            if (ext == null) return;

            // CasterPawn über Property (direkt zugreifbar, wir sind in Subklasse-Kontext)
            Pawn pawn = v.CasterPawn;

            // 1) Ticks zwischen Bursts auf unseren Wert setzen
            int baseTicks = v.verbProps.ticksBetweenBurstShots;
            int ticks = BurstCalc.ComputeTicksBetween(pawn, baseTicks, ext);
            var ticksField = AccessTools.Field(typeof(Verb), "ticksToNextBurstShot");
            if (ticksField != null) ticksField.SetValue(v, ticks);

           
        }
    }
}

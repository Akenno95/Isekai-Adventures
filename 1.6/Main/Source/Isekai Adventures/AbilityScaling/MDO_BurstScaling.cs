// /Source/IsekaiAdventures/DefModExtension/MDO_BurstScaling.cs
using Verse;
using RimWorld;
using System.Collections.Generic;
using UnityEngine;

namespace IsekaiAdventures
{
    public enum BurstCountCombineMode { BasePlusAddThenMultiply, ReplaceThenMultiply }
    public enum TicksBetweenMode { BasePlusAdd, ReplaceIfPositive }

    public class MDO_BurstScaling : DefModExtension
    {
        public StatDef burstCountStat;
        public StatDef burstMultiplierStat;
        public StatDef ticksBetweenStat;
        public StatDef cooldownFactorStat;
        public StatDef damageFactorStat; // optional

        public BurstCountCombineMode burstCountMode = BurstCountCombineMode.BasePlusAddThenMultiply;
        public TicksBetweenMode ticksBetweenMode = TicksBetweenMode.BasePlusAdd;

        public int minShots = 1, maxShots = 50;
        public int minTicksBetween = 0, maxTicksBetween = 120;

        public float fallbackBurstAdd = 0f;
        public float fallbackBurstMul = 1f;
        public int fallbackTicksBetweenAdd = 0;
        public float fallbackCooldownFactor = 1f;
        public float fallbackDamageFactor = 1f;

        public override IEnumerable<string> ConfigErrors()
        {
            if (maxShots < minShots) yield return "maxShots < minShots";
            if (maxTicksBetween < minTicksBetween) yield return "maxTicksBetween < minTicksBetween";
        }
    }

    internal static class StatUtil
    {
        public static float GetStatOr(this Pawn pawn, StatDef stat, float fallback)
        {
            if (pawn == null || stat == null) return fallback;
            try { return pawn.GetStatValue(stat); } catch { return fallback; }
        }
    }
}

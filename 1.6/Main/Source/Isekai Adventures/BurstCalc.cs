// /Source/IsekaiAdventures/Util/BurstCalc.cs
using Verse;
using UnityEngine;

namespace IsekaiAdventures
{
    internal static class BurstCalc
    {
        public static int ComputeShotsPerBurst(Pawn pawn, int baseBurst, MDO_BurstScaling ext)
        {
            if (pawn == null || ext == null) return Mathf.Max(1, baseBurst);
            var add = pawn.GetStatOr(ext.burstCountStat, ext.fallbackBurstAdd);
            var mul = pawn.GetStatOr(ext.burstMultiplierStat, ext.fallbackBurstMul);

            float raw = ext.burstCountMode == BurstCountCombineMode.ReplaceThenMultiply
                ? ((add <= 0f) ? baseBurst : add) * Mathf.Max(0.01f, mul)
                : (baseBurst + add) * Mathf.Max(0.01f, mul);

            int clamped = Mathf.Clamp(Mathf.RoundToInt(raw), ext.minShots, ext.maxShots);
            return Mathf.Max(1, clamped);
        }

        public static int ComputeTicksBetween(Pawn pawn, int baseTicksBetween, MDO_BurstScaling ext)
        {
            if (pawn == null || ext == null) return Mathf.Max(0, baseTicksBetween);

            int add = Mathf.RoundToInt(pawn.GetStatOr(ext.ticksBetweenStat, ext.fallbackTicksBetweenAdd));
            int result = ext.ticksBetweenMode == TicksBetweenMode.ReplaceIfPositive
                ? (add > 0 ? add : baseTicksBetween)
                : baseTicksBetween + add;

            return Mathf.Clamp(result, ext.minTicksBetween, ext.maxTicksBetween);
        }
    }
}

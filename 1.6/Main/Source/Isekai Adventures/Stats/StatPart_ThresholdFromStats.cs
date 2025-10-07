// /Source/IsekaiAdventures/Stats/StatPart_Threshold.cs
using RimWorld;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    // mode: Positive = unter 0 ignorieren; Negative = negatives Delta zulassen (Debuff)
    public enum ThresholdMode { Positive, Negative }

    public class StatPart_Threshold : StatPart
    {
        public class Source
        {
            public StatDef stat;
            public float weight = 1f;
            public string labelOverride;
        }

        // ---- Inputs (immer Summe: Σ weight * stat) ----
        public List<Source> sources = new List<Source>();

        // ---- Steuerung ----
        public float threshold = 0f;        // T
        public float multiplier = 1f;       // skaliert (Summe - T)
        public ThresholdMode mode = ThresholdMode.Positive;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is not Pawn pawn || sources == null || sources.Count == 0)
                return;

            // 1) Summe bilden
            float sum = 0f;
            foreach (var s in sources)
            {
                if (s?.stat == null) continue;
                float v = 0f;
                try { v = pawn.GetStatValue(s.stat); } catch { }
                sum += s.weight * v;
            }

            // 2) Delta relativ zu Threshold
            float delta = sum - threshold;

            // 3) Mode: Positive = unter 0 ignorieren; Negative = auch <0 anwenden
            if (mode == ThresholdMode.Positive && delta < 0f)
                return; // kein Effekt

            // 4) Ergebnis skalieren
            float result = delta * multiplier;

            // 5) Final = defaultBaseValue ± result
            float baseVal = parentStat != null ? parentStat.defaultBaseValue : 0f;
            val = baseVal + result;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (req.Thing is not Pawn pawn || sources == null || sources.Count == 0) return null;

            var sb = new StringBuilder();
            float sum = 0f;

            foreach (var s in sources)
            {
                if (s?.stat == null) continue;
                float v = 0f; try { v = pawn.GetStatValue(s.stat); } catch { }
                float c = s.weight * v;
                sum += c;
                string name = s.labelOverride ?? s.stat.label;
                sb.AppendLine($"  {name}: {v:0.##} × {s.weight:0.###} = {c:0.##}");
            }

            float delta = sum - threshold;
            sb.AppendLine($"  → Summe: {sum:0.##}");
            sb.AppendLine($"  → Threshold: {threshold:0.##} ⇒ Δ = {delta:+0.##;-0.##}");

            if (mode == ThresholdMode.Positive && delta < 0f)
            {
                sb.AppendLine("  → mode=Positive: under Threshold no effect");
                return sb.ToString().TrimEnd();
            }

            float result = delta * multiplier;
            float baseVal = parentStat != null ? parentStat.defaultBaseValue : 0f;
            float final = baseVal + result;

            sb.AppendLine($"  → multiplier: {multiplier:0.###} ⇒ Result = {result:+0.##;-0.##}");
            sb.AppendLine($"  → defaultBaseValue: {baseVal:0.##}");
            sb.AppendLine($"  ⇒ Final: {final:0.##}");
            return sb.ToString().TrimEnd();
        }
    }
}

using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class HediffCompProperties_Regenerate : HediffCompProperties
    {
        public int tickInterval = 60;            // alle 60 Ticks (~1 Sek.)
        public float healFlat = 0.5f;              // fester Heal pro Tick
        public float healPercentOfMissing = 0f;    // Anteil des fehlenden HP

        public bool naturalPartsOnly = false;      // Prothesen ignorieren?

        public List<BodyPartDef> includeParts;     // optional: nur diese Parts
        public List<string> includePartTags;  // optional: nach Tags (defName)

        public HediffCompProperties_Regenerate()
        {
            compClass = typeof(HediffComp_Regenerate);
        }
        public float healPercentOfMax = 0f;          // Anteil vom (geschätzten) Max pro Tick

        // Optionale Heilung/Nachbehandlung von „Zuständen“
        public bool healNonInjuryHediffs = false;    // z. B. toxische Belastung, Blessuren etc.
        public List<HediffDef> hediffsToReduce;      // explizite Liste von Hediffs, deren Severity reduziert werden soll
        public float hediffSeverityReducePerTick = 0f;

    }

    public class HediffComp_Regenerate : HediffComp
    {
        private HediffCompProperties_Regenerate Props => (HediffCompProperties_Regenerate)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (parent.pawn.IsHashIntervalTick(Props.tickInterval))
                HealTick();
        }

        private void HealTick()
        {
            var pawn = parent.pawn;
            if (pawn?.health?.hediffSet == null) return;

            // 1) Verletzungen nach BodyPart gruppieren
            var injuriesByPart = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(i => i.Severity > 0f && i.Visible && PartAllowed(i.Part) && i.CanHealNaturally())
                .GroupBy(i => i.Part);

            foreach (var group in injuriesByPart)
            {
                var part = group.Key;
                var partInjuries = group.ToList();
                if (partInjuries.Count == 0) continue;

                // Missing = Summe der Verletzungsschwere
                float totalMissing = 0f;
                foreach (var inj in partInjuries)
                    totalMissing += inj.Severity;

                if (totalMissing <= 0f) continue;

                // EBF-safe „Max“ schätzen: aktueller Health + Missing
                float cur = pawn.health.hediffSet.GetPartHealth(part);
                float estimatedMax = cur + totalMissing;

                // Gesamt-Heilmenge für diesen Part
                float totalHeal = Props.healFlat;
                if (Props.healPercentOfMissing > 0f)
                    totalHeal += totalMissing * Props.healPercentOfMissing;
                if (Props.healPercentOfMax > 0f && estimatedMax > 0f)
                    totalHeal += estimatedMax * Props.healPercentOfMax;

                if (totalHeal <= 0f) continue;

                // proportional verteilen (größte Verletzung zuerst)
                float remaining = totalHeal;
                foreach (var inj in partInjuries.OrderByDescending(i => i.Severity))
                {
                    if (remaining <= 0f) break;
                    float share = (inj.Severity / totalMissing) * totalHeal;
                    float delta = Mathf.Min(share, inj.Severity, remaining);
                    if (delta > 0f)
                    {
                        inj.Heal(delta);
                        remaining -= delta;
                    }
                }
            }

            // 2) Optionale Heilung anderer Hediffs (explicit list)
            if (Props.healNonInjuryHediffs && Props.hediffsToReduce != null && Props.hediffsToReduce.Count > 0 && Props.hediffSeverityReducePerTick > 0f)
            {
                foreach (var h in pawn.health.hediffSet.hediffs)
                {
                    if (h is Hediff_Injury) continue;
                    if (!Props.hediffsToReduce.Contains(h.def)) continue;
                    // Nur heilbar/nicht permanent
                    if (h.IsPermanent()) continue;

                    float newSev = Mathf.Max(0f, h.Severity - Props.hediffSeverityReducePerTick);
                    h.Severity = newSev;
                }
            }
        }

        private bool PartAllowed(BodyPartRecord part)
        {
            if (part == null) return true;

            // Keine Heilung für Prothesen, falls aktiviert
            if (Props.naturalPartsOnly)
            {
                if (parent.pawn.health.hediffSet.hediffs
                    .Any(h => h.Part == part && h is Hediff_AddedPart))
                    return false;
            }

            // Nur bestimmte Parts erlaubt?
            if (Props.includeParts != null && Props.includeParts.Count > 0)
                if (!Props.includeParts.Contains(part.def))
                    return false;

            // Nach Tags (BodyPartTagDef.defName)
            if (Props.includePartTags != null && Props.includePartTags.Count > 0)
            {
                var tags = part.def.tags; // List<BodyPartTagDef>
                if (tags == null || tags.Count == 0) return false;
                bool match = Props.includePartTags
                    .Any(t => tags.Any(td => td?.defName == t));
                if (!match) return false;
            }

            return true;
        }

    }
}

using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class HediffCompProperties_Regenerate : HediffCompProperties
    {
        public int tickInterval = 60;                 // ~1 s

        // --- Verletzungen (nicht-permanent) ---
        public float healFlat = 0.5f;                 // Basis-Heal
        public float healPercentOfMissing = 0f;       // Anteil fehlender HP
        public float healPercentOfMax = 0f;           // Anteil geschätztem Max
        public StatDef flatHealBonusStat;             // nur für Injuries (additiv zur Basis)

        // --- Globaler Faktor (wirkt auf ALLES) ---
        public StatDef totalHealFactorStat;           // 1.0 = neutral, 0 = alles aus, 2.0 = doppelt

        // Filter
        public bool naturalPartsOnly = false;         // Prothesen ignorieren?
        public List<BodyPartDef> includeParts;        // nur diese Parts
        public List<string> includePartTags;          // nach BodyPartTagDef.defName

        // --- Scars (separat, NICHT aus totalHeal) ---
        public bool canHealScars = false;
        public float scarSeverityReducePerTick = 0f;  // pro Tick, vor globalem Faktor

        // --- Limb-Regeneration (separat, NICHT aus totalHeal) ---
        public bool canRegenLimbs = false;
        public int ticksPerLimb = 60_000;             // 1 Ingame-Tag = 60k

        // Optionaler visueller Indikator
        public HediffDef regrowthIndicatorHediff;     // am Host-Part (Vorfahre) oder null
        public bool removeIndicatorOnComplete = true; // bei RestorePart entfernen

        // --- Non-Injury-Reduktion (separat, NICHT aus totalHeal) ---
        public bool healNonInjuryHediffs = false;
        public List<HediffDef> hediffsToReduce;
        public float hediffSeverityReducePerTick = 0f;

        // --- TEND-LOGIK (NEU) ---
        // 1) Injuries: erst tend, dann heilen – Tend kostet Budget
        public bool tendBleeding = true;              // blutende Injuries zuerst tenden?
        public bool allowPartialTend = true;          // wenn Budget knapp: niedrigere Qualität statt gar kein Tend
        public float tendTargetQuality = 0.6f;        // gewünschte Qualität (0..1)
        public float tendCostPerQuality = 1.0f;       // Kosten in "Heal-Budget" pro 1.0 Qualität (Balance-Hebel)
        public float minTendQuality = 0.1f;           // Mindestqualität, wenn partial

        // 2) Fresh MissingParts brauchen eigenen Pool (sie haben kein Injury-Budget)
        public bool tendFreshMissingParts = true;     // fresh MissingPart sofort verbinden?
        public float tendGlobalPoolPerTick = 0.0f;    // zusätzlicher globaler Tend-Pool pro Tick (vor Faktor)

        public HediffCompProperties_Regenerate()
        {
            compClass = typeof(HediffComp_Regenerate);
        }
    }

    public class HediffComp_Regenerate : HediffComp
    {
        private HediffCompProperties_Regenerate Props => (HediffCompProperties_Regenerate)props;

        // Persistenz für Limb-Regen
        private BodyPartDef targetRegenPartDef;
        private int regenProgressTicks;

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Defs.Look(ref targetRegenPartDef, "targetRegenPartDef");
            Scribe_Values.Look(ref regenProgressTicks, "regenProgressTicks", 0);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            if (!parent.pawn.IsHashIntervalTick(Props.tickInterval)) return;

            HealTick();
            LimbRegenTick();
        }

        // ------------------------------------------------------------
        // Kern: Heil-Tick (Tend -> Blutung zuerst -> Rest)
        // ------------------------------------------------------------
        private void HealTick()
        {
            var pawn = parent.pawn;
            if (pawn?.health?.hediffSet == null) return;

            float global = GetGlobalHealFactor(pawn);

            // --- 0) Fresh MissingParts: aus globalem Tend-Pool bedienen ---
            if (Props.tendFreshMissingParts && Props.tendGlobalPoolPerTick > 0f && global > 0f)
            {
                float pool = Props.tendGlobalPoolPerTick * global;
                if (pool > 0f)
                {
                    var freshMP = pawn.health.hediffSet.hediffs
                        .OfType<Hediff_MissingPart>()
                        .Where(mp => mp.Part != null && PartAllowed(mp.Part) && mp.IsFresh && mp.Bleeding)
                        .OrderByDescending(mp => mp.Part.coverageAbsWithChildren)
                        .ToList();

                    foreach (var mp in freshMP)
                    {
                        if (pool <= 0f) break;
                        float usedQ = TryTendFromBudget(mp, ref pool, Props.tendTargetQuality, Props.tendCostPerQuality, Props.allowPartialTend, Props.minTendQuality);
                        // Hinweis: selbst kleine Qualität stoppt Blutung; Qualität beeinflusst Dauer/Wirksamkeit
                    }
                }
            }

            // --------- A) Normale Verletzungen (nicht-permanent) ----------
            var injuriesByPart = pawn.health.hediffSet.hediffs
                .OfType<Hediff_Injury>()
                .Where(i => i.Severity > 0f && !i.IsPermanent() && i.Visible && PartAllowed(i.Part) && i.CanHealNaturally())
                .GroupBy(i => i.Part);

            foreach (var group in injuriesByPart)
            {
                var part = group.Key;
                var list = group.ToList();
                if (list.Count == 0) continue;

                float totalMissing = 0f;
                foreach (var inj in list) totalMissing += inj.Severity;
                if (totalMissing <= 0f) continue;

                float cur = pawn.health.hediffSet.GetPartHealth(part);
                float estimatedMax = cur + totalMissing;

                // --- Basis-Budget NUR für Injuries ---
                float totalHeal =
                      Props.healFlat
                    + (Props.flatHealBonusStat != null ? pawn.GetStatValue(Props.flatHealBonusStat) : 0f)
                    + (Props.healPercentOfMissing > 0f ? Props.healPercentOfMissing * totalMissing : 0f)
                    + (Props.healPercentOfMax > 0f && estimatedMax > 0f ? Props.healPercentOfMax * estimatedMax : 0f);

                totalHeal *= global;
                if (totalHeal <= 0f) continue;

                float remaining = totalHeal;

                // --------- Phase 0.5: Tend zuerst (kostet Budget) ----------
                if (Props.tendBleeding)
                {
                    var bleeding = list.Where(i => i.Bleeding).OrderByDescending(i => i.Severity).ToList();
                    foreach (var inj in bleeding)
                    {
                        if (remaining <= 0f) break;
                        // Tend aus Remaining bezahlen
                        float usedQ = TryTendFromBudget(inj, ref remaining, Props.tendTargetQuality, Props.tendCostPerQuality, Props.allowPartialTend, Props.minTendQuality);
                        // egal ob partial: Tend stoppt Blutung sofort (Vanilla)
                    }
                }

                // --------- Phase 1: Blutungen „überheilen“, falls noch blutend (rare edge) ----------
                // (Normalerweise ist nach Tend nichts mehr blutend. Diese Phase ist fallback-sicher.)
                var stillBleeding = list.Where(i => i.Bleeding).OrderByDescending(i => i.Severity).ToList();
                foreach (var inj in stillBleeding)
                {
                    if (remaining <= 0f) break;
                    float need = inj.Severity;
                    float delta = Mathf.Min(need, remaining);
                    if (delta > 0f)
                    {
                        inj.Heal(delta);
                        remaining -= delta;
                    }
                }

                // --------- Phase 2: Rest proportional auf alle verbleibenden Injuries ----------
                if (remaining > 0f)
                {
                    float totalMissing2 = 0f;
                    foreach (var inj in list) totalMissing2 += inj.Severity;
                    if (totalMissing2 > 0f)
                    {
                        foreach (var inj in list.OrderByDescending(i => i.Severity))
                        {
                            if (remaining <= 0f) break;
                            float share = (inj.Severity / totalMissing2) * remaining;
                            float delta = Mathf.Min(share, inj.Severity, remaining);
                            if (delta <= 0f) continue;

                            inj.Heal(delta);
                            remaining -= delta;
                        }
                    }
                }
            }

            // --------- B) Scars separat (NICHT aus totalHeal) ----------
            if (Props.canHealScars && Props.scarSeverityReducePerTick > 0f && global > 0f)
            {
                var scars = pawn.health.hediffSet.hediffs
                    .OfType<Hediff_Injury>()
                    .Where(i => i.IsPermanent() && i.Visible && PartAllowed(i.Part))
                    .ToList();

                if (scars.Count > 0)
                {
                    float budget = Props.scarSeverityReducePerTick * global;
                    float totalScarSev = 0f; foreach (var s in scars) totalScarSev += Mathf.Max(0.0001f, s.Severity);
                    float remaining = budget;

                    foreach (var scar in scars.OrderByDescending(s => s.Severity))
                    {
                        if (remaining <= 0f) break;
                        float share = (scar.Severity / totalScarSev) * budget;
                        float delta = Mathf.Min(share, scar.Severity, remaining);
                        if (delta <= 0f) continue;

                        scar.Severity = Mathf.Max(0f, scar.Severity - delta);
                        if (scar.Severity <= 0.001f) HealthUtility.Cure(scar);
                        remaining -= delta;
                    }
                }
            }

            // --------- C) Non-Injury separat (NICHT aus totalHeal) ----------
            if (Props.healNonInjuryHediffs && Props.hediffsToReduce != null && Props.hediffsToReduce.Count > 0 && Props.hediffSeverityReducePerTick > 0f && global > 0f)
            {
                float eff = Props.hediffSeverityReducePerTick * global;
                foreach (var h in pawn.health.hediffSet.hediffs)
                {
                    if (h is Hediff_Injury) continue;
                    if (!Props.hediffsToReduce.Contains(h.def)) continue;
                    if (h.IsPermanent()) continue;

                    h.Severity = Mathf.Max(0f, h.Severity - eff);
                }
            }
        }

        // ------------------------------------------------------------
        // Limb-Regeneration (separat, mit robustem Indikator)
        // ------------------------------------------------------------
        private void LimbRegenTick()
        {
            if (!Props.canRegenLimbs) return;

            var pawn = parent.pawn;
            if (pawn?.health?.hediffSet == null) return;

            float global = GetGlobalHealFactor(pawn);
            if (global <= 0f) return; // globaler Stopp

            // aktuelles Ziel validieren
            var currentMissing = GetMissingPartForDef(pawn, targetRegenPartDef);
            if (currentMissing == null)
            {
                if (Props.regrowthIndicatorHediff != null && targetRegenPartDef != null && Props.removeIndicatorOnComplete)
                    RemoveRegrowIndicatorAll(pawn);

                targetRegenPartDef = null;
                regenProgressTicks = 0;
            }

            // neues Ziel wählen
            if (targetRegenPartDef == null)
            {
                var pick = SelectMissingPart(pawn);
                if (pick == null) return;

                targetRegenPartDef = pick.Part.def;
                regenProgressTicks = 0;

                // Indikator setzen (robust; wenn kein Host -> skip)
                TryAddRegrowIndicator(pawn, pick.Part);
            }

            // Fortschritt skaliert mit globalem Faktor
            int inc = Mathf.Max(0, Mathf.RoundToInt(Props.tickInterval * global));
            regenProgressTicks += inc;

            // Fortschrittsanzeige
            UpdateRegrowIndicatorProgress(pawn);

            if (regenProgressTicks >= Mathf.Max(1, Props.ticksPerLimb))
            {
                var mp = GetMissingPartForDef(pawn, targetRegenPartDef);
                if (mp != null)
                    pawn.health.RestorePart(mp.Part);

                if (Props.regrowthIndicatorHediff != null && targetRegenPartDef != null && Props.removeIndicatorOnComplete)
                    RemoveRegrowIndicatorAll(pawn);

                targetRegenPartDef = null;
                regenProgressTicks = 0;
            }
        }

        // ------------------------------------------------------------
        // Hilfsfunktionen
        // ------------------------------------------------------------
        private float GetGlobalHealFactor(Pawn pawn)
        {
            float mult = 1f;
            if (Props.totalHealFactorStat != null)
                mult = pawn.GetStatValue(Props.totalHealFactorStat);
            return Mathf.Max(0f, mult); // clamp: nie negativ
        }

        /// <summary>
        /// Versucht, ein Hediff aus einem Budget zu tenden.
        /// Zieht die Kosten (quality * costPerQuality) vom Budget ab und gibt die tatsächliche Qualität zurück (0..1).
        /// Bei allowPartial=true wird mit geringerer Qualität getended, wenn Budget nicht reicht.
        /// </summary>
        private float TryTendFromBudget(Hediff hediff, ref float budget, float targetQuality, float costPerQuality, bool allowPartial, float minQuality)
        {
            try
            {
                float qTarget = Mathf.Clamp01(targetQuality);
                float costPerQ = Mathf.Max(0.0001f, costPerQuality);

                float maxQAffordable = Mathf.Clamp01(budget / costPerQ);
                float q;
                if (maxQAffordable >= qTarget)
                {
                    q = qTarget;
                }
                else if (allowPartial && maxQAffordable >= minQuality)
                {
                    q = maxQAffordable;
                }
                else
                {
                    return 0f; // kein Tend (Budget zu klein)
                }

                // verbrauche Budget
                float cost = q * costPerQ;
                budget = Mathf.Max(0f, budget - cost);

                hediff.Tended(q, 1, 1); // Tend stoppt Blutung sofort (Vanilla)
                return q;
            }
            catch
            {
                return 0f;
            }
        }

        private void TryTend(Hediff hediff, float quality)
        {
            try
            {
                hediff.Tended(Mathf.Clamp01(quality), 1, 1);
            }
            catch { }
        }

        private Hediff_MissingPart SelectMissingPart(Pawn pawn)
        {
            var allMissing = pawn.health.hediffSet?.GetMissingPartsCommonAncestors();
            if (allMissing == null || allMissing.Count == 0) return null;

            var candidates = allMissing
                .Where(mp =>
                    PartAllowed(mp.Part) &&
                    !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(mp.Part))
                .ToList();
            if (candidates.Count == 0) return null;

            return candidates
                .OrderByDescending(mp => mp.Part.coverageAbsWithChildren)
                .FirstOrDefault();
        }

        private Hediff_MissingPart GetMissingPartForDef(Pawn pawn, BodyPartDef def)
        {
            if (def == null) return null;
            var allMissing = pawn.health.hediffSet?.GetMissingPartsCommonAncestors();
            if (allMissing == null) return null;

            return allMissing.FirstOrDefault(mp =>
                mp.Part?.def == def &&
                PartAllowed(mp.Part) &&
                !pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(mp.Part));
        }

        private bool PartAllowed(BodyPartRecord part)
        {
            if (part == null) return true;

            if (Props.naturalPartsOnly)
            {
                if (parent.pawn.health.hediffSet.hediffs
                    .Any(h => h.Part == part && h is Hediff_AddedPart))
                    return false;
            }

            if (Props.includeParts != null && Props.includeParts.Count > 0)
                if (!Props.includeParts.Contains(part.def))
                    return false;

            if (Props.includePartTags != null && Props.includePartTags.Count > 0)
            {
                var tags = part.def.tags;
                if (tags == null || tags.Count == 0) return false;
                bool match = Props.includePartTags.Any(t => tags.Any(td => td?.defName == t));
                if (!match) return false;
            }

            return true;
        }

        // ========= Regrow-Indicator: robust =========

        private void TryAddRegrowIndicator(Pawn pawn, BodyPartRecord missingPart)
        {
            if (Props.regrowthIndicatorHediff == null) return;
            if (pawn?.health?.hediffSet == null) return;

            BodyPartRecord host = FindIndicatorHostPart(pawn, missingPart);
            bool already = pawn.health.hediffSet.hediffs.Any(h =>
                h.def == Props.regrowthIndicatorHediff &&
                ((host == null && h.Part == null) || (host != null && h.Part == host)));
            if (already) return;

            if (host == missingPart) return;

            try { pawn.health.AddHediff(Props.regrowthIndicatorHediff, host); } catch { }
        }

        private BodyPartRecord FindIndicatorHostPart(Pawn pawn, BodyPartRecord missingPart)
        {
            if (pawn?.health?.hediffSet == null || missingPart == null) return null;
            var set = pawn.health.hediffSet;
            BodyPartRecord cur = missingPart.parent;
            while (cur != null)
            {
                if (!set.PartIsMissing(cur)) return cur;
                cur = cur.parent;
            }
            return null; // whole body
        }

        private void UpdateRegrowIndicatorProgress(Pawn pawn)
        {
            if (Props.regrowthIndicatorHediff == null) return;
            if (pawn?.health?.hediffSet == null) return;

            float denom = Mathf.Max(1, Props.ticksPerLimb);
            float progress = Mathf.Clamp01(regenProgressTicks / denom); // 0..1
            var indicators = pawn.health.hediffSet.hediffs
                .Where(h => h.def == Props.regrowthIndicatorHediff)
                .ToList();
            foreach (var h in indicators) h.Severity = progress;
        }

        private void RemoveRegrowIndicatorAll(Pawn pawn)
        {
            if (Props.regrowthIndicatorHediff == null) return;
            if (pawn?.health?.hediffSet == null) return;

            var toRemove = pawn.health.hediffSet.hediffs
                .Where(h => h.def == Props.regrowthIndicatorHediff)
                .ToList();
            foreach (var h in toRemove) { try { pawn.health.RemoveHediff(h); } catch { } }
        }
    }
}

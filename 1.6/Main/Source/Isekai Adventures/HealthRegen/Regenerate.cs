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

        // --- Non-Injury-Reduktion (separat, NICHT aus totalHeal) ---
        public bool healNonInjuryHediffs = false;
        public List<HediffDef> hediffsToReduce;
        public float hediffSeverityReducePerTick = 0f;

        // --- Optional: Sichtbarer Indikator, dass ein Part nachwächst ---
        public HediffDef regrowthIndicatorHediff;      // wird am Ziel-Part gesetzt/entfernt
        public bool removeIndicatorOnComplete = true;  // bei RestorePart wieder löschen


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
        // Kern: Heil-Tick
        // ------------------------------------------------------------
        private void HealTick()
        {
            var pawn = parent.pawn;
            if (pawn?.health?.hediffSet == null) return;

            float global = GetGlobalHealFactor(pawn);

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

                // --- Basis-Heilbudget NUR für Injuries ---
                float totalHeal =
                      Props.healFlat
                    + (Props.flatHealBonusStat != null ? pawn.GetStatValue(Props.flatHealBonusStat) : 0f)
                    + (Props.healPercentOfMissing > 0f ? Props.healPercentOfMissing * totalMissing : 0f)
                    + (Props.healPercentOfMax > 0f && estimatedMax > 0f ? Props.healPercentOfMax * estimatedMax : 0f);

                totalHeal *= global;
                if (totalHeal <= 0f) continue;

                float remaining = totalHeal;

                // --------- Phase 1: Blutungen stoppen (hart priorisiert) ----------
                // Heile zuerst alle blutenden Verletzungen – größte Blutung/Schwere zuerst
                var bleeding = list.Where(i => i.Bleeding).OrderByDescending(i => i.Severity).ToList();
                foreach (var inj in bleeding)
                {
                    if (remaining <= 0f) break;

                    // Wir versuchen, so viel zu heilen, bis die Blutung aufhört (Annäherung: volle Schwere ist Obergrenze).
                    // RimWorld stoppt Blutung, wenn Schwere/State unter bestimmte Schwellen fällt oder getended wird.
                    // Praktisch reicht meist "ein gutes Stück heilen"; wir heilen maximal was im Budget liegt.
                    float delta = Mathf.Min(inj.Severity, remaining);
                    if (delta > 0f)
                    {
                        inj.Heal(delta);
                        remaining -= delta;

                        // Falls immer noch Blutung: nächster Tick/Budget nimmt sie wieder zuerst dran.
                        if (!inj.Bleeding) { /* schön, Blutung gestoppt */ }
                    }
                }

                // --------- Phase 2: Rest proportional auf alle verbleibenden Injuries ----------
                if (remaining > 0f)
                {
                    // Recompute totalMissing (kann sich nach Phase 1 geändert haben)
                    float totalMissing2 = 0f;
                    foreach (var inj in list) totalMissing2 += inj.Severity;
                    if (totalMissing2 > 0f)
                    {
                        foreach (var inj in list.OrderByDescending(i => i.Severity))
                        {
                            if (remaining <= 0f) break;
                            float share = (inj.Severity / totalMissing2) * remaining;   // proportional am Restbudget
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
        // Limb-Regeneration (separat)
        // ------------------------------------------------------------
        private void LimbRegenTick()
        {
            if (!Props.canRegenLimbs) return;

            var pawn = parent.pawn;
            if (pawn?.health?.hediffSet == null) return;

            float global = GetGlobalHealFactor(pawn);
            if (global <= 0f) return; // globaler Stopp

            // Ziel validieren
            var currentMissing = GetMissingPartForDef(pawn, targetRegenPartDef);
            if (currentMissing == null)
            {
                // Falls Indikator lief, entfernen
                if (Props.regrowthIndicatorHediff != null && targetRegenPartDef != null && Props.removeIndicatorOnComplete)
                    RemoveRegrowIndicator(pawn, targetRegenPartDef);

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

                // Optionaler visueller Indikator am Ziel-Part
                if (Props.regrowthIndicatorHediff != null)
                {
                    // nur setzen, wenn noch nicht vorhanden
                    if (!pawn.health.hediffSet.hediffs.Any(h => h.def == Props.regrowthIndicatorHediff && h.Part?.def == targetRegenPartDef))
                    {
                        pawn.health.AddHediff(Props.regrowthIndicatorHediff, pick.Part);
                    }
                }
            }

            // Fortschritt skaliert mit globalem Faktor
            int inc = Mathf.Max(0, Mathf.RoundToInt(Props.tickInterval * global));
            regenProgressTicks += inc;

            if (regenProgressTicks >= Mathf.Max(1, Props.ticksPerLimb))
            {
                var mp = GetMissingPartForDef(pawn, targetRegenPartDef);
                if (mp != null)
                    pawn.health.RestorePart(mp.Part);

                // Indikator aufräumen
                if (Props.regrowthIndicatorHediff != null && targetRegenPartDef != null && Props.removeIndicatorOnComplete)
                    RemoveRegrowIndicator(pawn, targetRegenPartDef);

                targetRegenPartDef = null;
                regenProgressTicks = 0;
            }
        }

        // Entfernt den optionalen Indikator am gegebenen BodyPartDef
        private void RemoveRegrowIndicator(Pawn pawn, BodyPartDef partDef)
        {
            var toRemove = pawn.health.hediffSet.hediffs
                .Where(h => h.def == Props.regrowthIndicatorHediff && h.Part?.def == partDef)
                .ToList();
            foreach (var h in toRemove)
                pawn.health.RemoveHediff(h);
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

            // größtes Coverage zuerst (z. B. Arm vor Finger)
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

            // Prothesen ausschließen, wenn gewünscht
            if (Props.naturalPartsOnly)
            {
                if (parent.pawn.health.hediffSet.hediffs
                    .Any(h => h.Part == part && h is Hediff_AddedPart))
                    return false;
            }

            // nur bestimmte Parts
            if (Props.includeParts != null && Props.includeParts.Count > 0)
                if (!Props.includeParts.Contains(part.def))
                    return false;

            // nach Tags
            if (Props.includePartTags != null && Props.includePartTags.Count > 0)
            {
                var tags = part.def.tags; // List<BodyPartTagDef>
                if (tags == null || tags.Count == 0) return false;
                bool match = Props.includePartTags.Any(t => tags.Any(td => td?.defName == t));
                if (!match) return false;
            }

            return true;
        }
    }
}

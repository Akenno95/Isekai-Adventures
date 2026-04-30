using System.Collections.Generic;
using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public enum ZoneHostilityMode { Any, FriendlyOnly, HostileOnly }
    public enum ZoneTargetKind { AnyPawn, HumanlikeOnly, AnimalsOnly, MechsOnly }
    public enum ZoneVisualMode { None, Ring }
    public enum ZoneVisualColor { Blue, Yellow }

    public enum ZoneEffectTrigger { OnEnter, WhileInside, OnExit }

    public class ZoneMagicEffectDef : IExposable
    {
        public ZoneEffectTrigger trigger = ZoneEffectTrigger.WhileInside;

        // Optional: nur anwenden wenn Pawn diesen Hediff hat (z.B. Undead)
        public HediffDef requiredHediff;

        // Genau eins davon setzen:
        public HediffDef hediffDef;
        public MentalStateDef mentalStateDef;

        // Hediff tuning
        public float initialSeverity = 0.1f;
        public float addSeverity = 0.1f;

        public float applyChance = 1f;

        public bool IsValid => hediffDef != null || mentalStateDef != null;
        public bool IsHediff => hediffDef != null;
        public bool IsMental => mentalStateDef != null;

        public void ExposeData()
        {
            Scribe_Values.Look(ref trigger, "trigger", ZoneEffectTrigger.WhileInside);

            Scribe_Defs.Look(ref requiredHediff, "requiredHediff");

            Scribe_Defs.Look(ref hediffDef, "hediffDef");
            Scribe_Defs.Look(ref mentalStateDef, "mentalStateDef");

            Scribe_Values.Look(ref initialSeverity, "initialSeverity", 0.1f);
            Scribe_Values.Look(ref addSeverity, "addSeverity", 0.1f);
            Scribe_Values.Look(ref applyChance, "applyChance", 1f);
        }
    }
}
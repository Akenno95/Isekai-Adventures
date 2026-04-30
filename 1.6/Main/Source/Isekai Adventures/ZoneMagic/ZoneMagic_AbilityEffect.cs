using System.Collections.Generic;
using Verse;
using RimWorld;

namespace IsekaiAdventures
{
    public class CompProperties_AbilityEffect_ZoneMagicPlace : CompProperties_AbilityEffect
    {
        public ThingDef zoneAreaThingDef;

        public float radius = 4.9f;
        public int tickInterval = 60;
        public int durationTicks = 600;

        public ZoneHostilityMode hostility = ZoneHostilityMode.Any;
        public ZoneTargetKind targetKind = ZoneTargetKind.AnyPawn;

        public ZoneVisualMode visualMode = ZoneVisualMode.Ring;
        public ZoneVisualColor visualColor = ZoneVisualColor.Blue;

        public List<ZoneMagicEffectDef> effects = new();

        public CompProperties_AbilityEffect_ZoneMagicPlace()
        {
            compClass = typeof(CompAbilityEffect_ZoneMagicPlace);
        }
    }

    public class CompAbilityEffect_ZoneMagicPlace : CompAbilityEffect
    {
        public new CompProperties_AbilityEffect_ZoneMagicPlace Props =>
            (CompProperties_AbilityEffect_ZoneMagicPlace)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            if (Props.zoneAreaThingDef == null) return;
            if (!target.IsValid) return;

            var pawn = parent?.pawn;
            var map = pawn?.Map;
            if (map == null) return;

            var thing = ThingMaker.MakeThing(Props.zoneAreaThingDef);
            var area = thing as ZoneMagicArea;
            if (area == null)
            {
                Log.Error($"ZoneMagicPlace: zoneAreaThingDef {Props.zoneAreaThingDef.defName} ist keine ZoneMagicArea.");
                thing.Destroy();
                return;
            }

            area.InitFromAbility(pawn, Props);
            GenSpawn.Spawn(area, target.Cell, map);
        }
    }
}
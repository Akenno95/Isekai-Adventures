using System.Collections.Generic;
using Verse;

namespace IsekaiAdventures
{
    public class CompProperties_ZoneMagicEmitter : CompProperties
    {
        public ThingDef zoneAreaThingDef;

        public float radius = 4.9f;
        public int tickInterval = 60;

        public ZoneHostilityMode hostility = ZoneHostilityMode.Any;
        public ZoneTargetKind targetKind = ZoneTargetKind.AnyPawn;

        public ZoneVisualMode visualMode = ZoneVisualMode.None;
        public ZoneVisualColor visualColor = ZoneVisualColor.Blue;

        public List<ZoneMagicEffectDef> effects = new();

        public CompProperties_ZoneMagicEmitter()
        {
            compClass = typeof(CompZoneMagicEmitter);
        }
    }

    public class CompZoneMagicEmitter : ThingComp
    {
        private ZoneMagicArea cachedArea;
        public CompProperties_ZoneMagicEmitter Props => (CompProperties_ZoneMagicEmitter)props;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            EnsureAreaExists();
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            EnsureAreaExists();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);

            if (cachedArea != null && !cachedArea.Destroyed)
                cachedArea.Destroy();
            cachedArea = null;
        }

        private void EnsureAreaExists()
        {
            if (parent.Map == null) return;
            if (Props.zoneAreaThingDef == null) return;

            if (cachedArea != null && !cachedArea.Destroyed && cachedArea.Spawned)
            {
                cachedArea.SetForcedPosition(parent.Position);
                return;
            }

            var thing = ThingMaker.MakeThing(Props.zoneAreaThingDef);
            cachedArea = thing as ZoneMagicArea;
            if (cachedArea == null)
            {
                Log.Error($"ZoneMagicEmitter: zoneAreaThingDef {Props.zoneAreaThingDef.defName} ist keine ZoneMagicArea.");
                thing.Destroy();
                return;
            }

            // Building: unendlich
            cachedArea.InitFromEmitter(parent, Props, durationOverrideTicks: -1);
            GenSpawn.Spawn(cachedArea, parent.Position, parent.Map);
        }
    }
}
// /Source/IsekaiAdventures/Verbs/Verb_AbilityBurstScaling.cs
using Verse;
using RimWorld;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace IsekaiAdventures
{
    public class Verb_AbilityBurstScaling : Verb_AbilityShoot
    {
        MDO_BurstScaling Ext => this.Ability?.def?.GetModExtension<MDO_BurstScaling>();
        Pawn CasterPawnSafe => CasterPawn;

        int BaseBurst => verbProps.burstShotCount;
        int BaseTicksBetween => verbProps.ticksBetweenBurstShots;

        // FIX #4: protected -> protected (unverändert korrekt), aber hier behalten wir protected bei
        protected override int ShotsPerBurst =>
            BurstCalc.ComputeShotsPerBurst(CasterPawnSafe, BaseBurst, Ext);

        // FIX #1: public (nicht protected!) und danach Ticks setzen
        public override void WarmupComplete()
        {
            base.WarmupComplete();

            // Erste Pause zwischen Folgeschüssen auf unseren Wert setzen
            var ext = Ext;
            if (ext != null)
            {
                int ticks = BurstCalc.ComputeTicksBetween(CasterPawnSafe, BaseTicksBetween, ext);
                var field = AccessTools.Field(typeof(Verb), "ticksToNextBurstShot");
                if (field != null) field.SetValue(this, ticks);
            }
        }
    }
}

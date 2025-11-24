
using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace IsekaiAdventures
{
    public class HediffCompProperties_ComboReactor : HediffCompProperties
    {
        public List<ComboReaction> damageReactions;
        public List<ComboReaction> hediffReactions;

        public HediffCompProperties_ComboReactor()
        {
            compClass = typeof(HediffComp_ComboReactor);
        }
    }

    public class ComboReaction
    {
        public List<DamageDef> reactionDamageType;
        public List<HediffDef> reactionHediff;
        public ReactionProperties reactionProperties;
        public bool removeOnReact = false;
		public bool requireAll = false;
    }

	public class ReactionProperties
    {
        public DamageDef damageDef;
        public FloatRange damageRange;
        public FloatRange armourPenetrationRange;
        public bool isAOE = false;
        public float radius = 1f;
        public int stunTicks = 0;
        public HediffDef hediffToAdd;
    }

    public class HediffComp_ComboReactor : HediffComp
    {
        public HediffCompProperties_ComboReactor Props => (HediffCompProperties_ComboReactor)props;

        public override void CompPostPostAdd(DamageInfo? dinfo)
        {
            base.CompPostPostAdd(dinfo);

            if (Props.hediffReactions == null || Props.hediffReactions.Count == 0)
                return;

            bool shouldRemove = false;

			foreach (var reaction in Props.hediffReactions)
			{
				if (reaction.reactionHediff == null || reaction.reactionHediff.Count == 0) continue;

				bool shouldTrigger;

				if (reaction.requireAll)
				{
					shouldTrigger = reaction.reactionHediff
						.All(h => Pawn.health.hediffSet.HasHediff(h));
				}
				else
				{
					shouldTrigger = reaction.reactionHediff
						.Any(h => Pawn.health.hediffSet.HasHediff(h));
				}

				if (shouldTrigger)
				{
					ExecuteReaction(reaction);
					if (reaction.removeOnReact)
						shouldRemove = true;
				}
			}


			if (shouldRemove)
            {
                Pawn.health.RemoveHediff(this.parent);
            }
        }

        public override void Notify_PawnPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(dinfo, totalDamageDealt);

            if (Props.damageReactions == null || dinfo.Def == null)
                return;

            var reactions = Props.damageReactions
                .Where(r => r.reactionDamageType != null && r.reactionDamageType.Contains(dinfo.Def))
                .ToList();

            if (reactions.NullOrEmpty())
                return;

            bool shouldRemove = false;

            foreach (var reaction in reactions)
            {
                ExecuteReaction(reaction);
                if (reaction.removeOnReact)
                    shouldRemove = true;
            }

            if (shouldRemove)
            {
                Pawn.health.RemoveHediff(this.parent);
            }
        }

        private void ExecuteReaction(ComboReaction reaction)
        {
            var props = reaction.reactionProperties;
            if (props == null) return;

            if (props.isAOE)
            {
                var targets = GenRadial.RadialDistinctThingsAround(Pawn.Position, Pawn.Map, props.radius, true)
                    .OfType<Pawn>()
                    .Where(p => p != null && p.Spawned && !p.Dead)
                    .ToList();

                foreach (var target in targets)
                {
                    ApplyEffects(target, props);
                }
            }
            else
            {
                ApplyEffects(Pawn, props);
            }
        }

        private void ApplyEffects(Pawn target, ReactionProperties props)
        {
            if (props.damageDef != null)
            {
                float dmg = props.damageRange.RandomInRange;
                float ap = props.armourPenetrationRange.RandomInRange;
                var dinfo = new DamageInfo(props.damageDef, dmg, ap, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown);
                target.TakeDamage(dinfo);
            }

            if (props.stunTicks > 0 && target.stances?.stunner != null)
            {
                target.stances.stunner.StunFor(props.stunTicks, null);
            }

            if (props.hediffToAdd != null)
            {
                var newHediff = HediffMaker.MakeHediff(props.hediffToAdd, target);
                target.health.AddHediff(newHediff);
            }
        }
    }
}

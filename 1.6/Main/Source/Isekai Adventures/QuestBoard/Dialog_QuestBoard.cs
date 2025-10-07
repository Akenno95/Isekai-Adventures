using RimWorld;
using RimWorld.QuestGen;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class Dialog_QuestBoard : Window
    {
        public override Vector2 InitialSize => new Vector2(500f, 600f);

        public Dialog_QuestBoard()
        {
            this.forcePause = true;
            this.closeOnAccept = false;
            this.absorbInputAroundWindow = true;
            this.doCloseX = true; 
            this.closeOnCancel = true; 
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "Isekai Quest Board");

            Text.Font = GameFont.Small;

            Rect listRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 50f);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(listRect);

            foreach (var cat in QuestBoardSettings.AllCategories)
            {
                // Kategorie
                listing.Label(cat.category);

                foreach (var questDefName in cat.quests)
                {
                    var questDef = DefDatabase<QuestScriptDef>.GetNamedSilentFail(questDefName);
                    string label = questDef?.label.NullOrEmpty() != false ? questDefName : questDef.label;

                    if (questDef == null)
                    {
                        listing.Label($"  {label} (Missing Def)");
                        continue;
                    }

                    if (QuestCooldownTracker.Instance.IsOnCooldown(questDefName))
                    {
                        int ticksLeft = QuestCooldownTracker.Instance.TicksRemaining(questDefName);
                        string timeLeft = ticksLeft.ToStringTicksToPeriod();
                        listing.Label($"  {label} (On Cooldown: {timeLeft})");
                    }
                    else if (listing.ButtonText($"  {label}"))
                    {
                        var slate = new Slate();
                        QuestUtility.GenerateQuestAndMakeAvailable(questDef, slate);

                        // HIER:
                        int cooldown = QuestBoardSettings.GetCooldownForQuest(questDefName);
                        QuestCooldownTracker.Instance.StartCooldown(questDefName, cooldown);

                        Messages.Message("Quest started: " + label, MessageTypeDefOf.PositiveEvent);
                    }

                }

                listing.GapLine();
            }

            listing.End();
        }
    }
}

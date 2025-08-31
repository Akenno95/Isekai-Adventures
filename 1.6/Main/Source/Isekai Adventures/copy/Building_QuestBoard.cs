using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace IsekaiAdventures
{
    public class Building_QuestBoard : Building
    {
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "Open Quest Board",
                defaultDesc = "View available quests.",
                icon = ContentFinder<Texture2D>.Get("UI/Icons/QuestGiver"), // optional
                action = () => Find.WindowStack.Add(new Dialog_QuestBoard())
            };
        }
    }

}

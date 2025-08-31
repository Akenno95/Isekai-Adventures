using System.Collections.Generic;
using Verse;

namespace IsekaiAdventures
{
    public class QuestCategoryEntry
    {
        public string category;
        public List<string> quests;
    }

    public class QuestBoardSettingsDef : Def
    {
        public List<QuestCategoryEntry> questCategories = new List<QuestCategoryEntry>();
    }
}

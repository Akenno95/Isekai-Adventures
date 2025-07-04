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

        // Optionaler Cooldown pro Quest (defName → Ticks)
        public Dictionary<string, int> questCooldowns = new Dictionary<string, int>();

        // Standard-Cooldown (z. B. 60000 Ticks = 1 Tag)
        public int defaultCooldownTicks = 60000;
    }
}

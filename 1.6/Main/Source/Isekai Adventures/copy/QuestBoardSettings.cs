using System.Collections.Generic;
using System.Linq;
using Verse;

namespace IsekaiAdventures
{
    [StaticConstructorOnStartup]
    public static class QuestBoardSettings
    {
        public static List<QuestCategoryEntry> AllCategories { get; private set; }

        static QuestBoardSettings()
        {
            var settings = DefDatabase<QuestBoardSettingsDef>.AllDefsListForReading.FirstOrDefault();
            AllCategories = settings?.questCategories ?? new List<QuestCategoryEntry>();
        }
    }
}

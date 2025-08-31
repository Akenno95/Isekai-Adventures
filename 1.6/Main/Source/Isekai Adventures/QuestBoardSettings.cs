using System.Collections.Generic;
using System.Linq;
using Verse;

namespace IsekaiAdventures
{
    [StaticConstructorOnStartup]
    public static class QuestBoardSettings
    {
        public static List<QuestCategoryEntry> AllCategories { get; private set; } = new();
        public static Dictionary<string, int> Cooldowns { get; private set; } = new();
        public static int DefaultCooldownTicks { get; private set; } = 60000;

        static QuestBoardSettings()
        {
            var settings = DefDatabase<QuestBoardSettingsDef>.AllDefsListForReading.FirstOrDefault();
            if (settings != null)
            {
                AllCategories = settings.questCategories ?? new List<QuestCategoryEntry>();
                Cooldowns = settings.questCooldowns ?? new Dictionary<string, int>();
                DefaultCooldownTicks = settings.defaultCooldownTicks;
            }
        }

        public static int GetCooldownForQuest(string defName)
        {
            if (Cooldowns.TryGetValue(defName, out var ticks))
                return ticks;

            return DefaultCooldownTicks;
        }
    }
}

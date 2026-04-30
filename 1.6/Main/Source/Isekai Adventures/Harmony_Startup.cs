using HarmonyLib;
using Verse;

namespace IsekaiAdventures
{
    [StaticConstructorOnStartup]
    public static class IsekaiAdventures_Startup
    {
        static IsekaiAdventures_Startup()
        {
            var harmony = new Harmony("IsekaiAdventures.Main");
            harmony.PatchAll();
            Log.Message("[IsekaiAdventures] Harmony patches applied.");
        }
    }
}
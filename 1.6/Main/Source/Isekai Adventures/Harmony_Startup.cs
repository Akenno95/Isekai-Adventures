using HarmonyLib;
using Verse;

namespace IsekaiAdventures
{
    [StaticConstructorOnStartup] // sorgt dafür, dass dieser Code sofort beim Laden der Mod ausgeführt wird
    public static class IsekaiAdventures_Startup
    {
        static IsekaiAdventures_Startup()
        {
            // "IsekaiAdventures.AbilityPatches" ist nur ein eindeutiger Bezeichner für deine Mod
            var harmony = new Harmony("IsekaiAdventures.AbilityPatches");
            harmony.PatchAll();
        }
    }
}

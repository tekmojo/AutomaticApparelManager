using HarmonyLib;
using Verse;

namespace AutomaticOutfitManager.Core
{
    public sealed class AutomaticOutfitManagerMod : Mod
    {
        public const string HarmonyId = "tekmojo.automaticoutfitmanager";

        public AutomaticOutfitManagerMod(ModContentPack content) : base(content)
        {
            new Harmony(HarmonyId).PatchAll();
            Log.Message("[AutomaticOutfitManager] Phase 2 snapshot support loaded.");
        }
    }
}

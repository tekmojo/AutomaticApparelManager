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
            string version = typeof(AutomaticOutfitManagerMod)
                .Assembly.GetName().Version?.ToString(3) ?? "unknown";
            Log.Message($"[AutomaticOutfitManager] {version} loaded (Phase 2 complete).");
        }
    }
}

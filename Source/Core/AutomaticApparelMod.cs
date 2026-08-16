using HarmonyLib;
using Verse;

namespace AutomaticApparel.Core
{
    public sealed class AutomaticApparelMod : Mod
    {
        public const string HarmonyId = "tekmojo.automaticapparelmanager";

        public AutomaticApparelMod(ModContentPack content) : base(content)
        {
            new Harmony(HarmonyId).PatchAll();
            Log.Message("[Automatic Apparel] Phase 1 loaded.");
        }
    }
}

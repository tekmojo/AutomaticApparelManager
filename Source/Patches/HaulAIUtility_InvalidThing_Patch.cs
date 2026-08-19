using HarmonyLib;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
{
    /// <summary>
    /// Prevents malformed spawned things from crashing RimWorld's opportunistic
    /// hauling scan. Some content mods can briefly leave a zero-stack thing in
    /// the haulables list; vanilla then dereferences it while checking fog.
    /// </summary>
    [HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.PawnCanAutomaticallyHaulFast_NewTemp))]
    internal static class HaulAIUtility_InvalidThing_Patch
    {
        private static bool Prefix(Thing t, ref bool __result)
        {
            if (t != null && !t.Destroyed && t.stackCount > 0)
                return true;

            __result = false;
            return false;
        }
    }
}

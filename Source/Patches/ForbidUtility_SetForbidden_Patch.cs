using AutomaticApparel.Core;
using AutomaticApparel.Storage;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutomaticApparel.Patches
{
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.SetForbidden),
        typeof(Thing), typeof(bool), typeof(bool))]
    public static class ForbidUtility_SetForbidden_Patch
    {
        public static void Postfix(Thing t, bool value)
        {
            if (!value || !(t is Apparel apparel))
                return;

            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            if (component?.IsTrackedApparel(apparel) != true &&
                !AutomaticApparelClassifier.Matches(apparel))
                return;

            if (apparel.Spawned && apparel.IsForbidden(Faction.OfPlayer))
                apparel.SetForbidden(false, false);
        }
    }

    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden),
        typeof(Thing), typeof(Faction))]
    [HarmonyPriority(Priority.Last)]
    public static class ForbidUtility_IsForbidden_Patch
    {
        public static void Postfix(Thing t, Faction faction, ref bool __result)
        {
            if (!__result || faction != Faction.OfPlayer || !(t is Apparel apparel))
                return;

            if (AutomaticApparelClassifier.Matches(apparel))
                __result = false;
        }
    }
}

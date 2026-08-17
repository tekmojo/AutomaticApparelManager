using AutomaticApparel.Storage;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutomaticApparel.Patches
{
    [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.SetAllow), typeof(SpecialThingFilterDef), typeof(bool))]
    public static class ThingFilter_SetAllow_Patch
    {
        public static void Postfix(ThingFilter __instance, SpecialThingFilterDef sfDef, bool allow)
        {
            if (!allow || sfDef?.defName != "AutomaticApparel_AllowAutomatic")
                return;

            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (AutomaticApparelClassifier.Matches(def))
                    __instance.SetAllow(def, true);
            }
        }
    }

    [HarmonyPatch(typeof(ThingFilter), nameof(ThingFilter.Allows), typeof(Thing))]
    [HarmonyPriority(Priority.Last)]
    public static class ThingFilter_EnforceAutomaticApparel_Patch
    {
        public static void Postfix(ThingFilter __instance, Thing t, ref bool __result)
        {
            if (!__result || t?.def?.apparel == null)
                return;

            bool automatic = AutomaticApparelClassifier.Matches(t);
            string filterName = automatic
                ? "AutomaticApparel_AllowAutomatic"
                : "AutomaticApparel_AllowNonAutomatic";
            SpecialThingFilterDef filterDef =
                DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(filterName);

            if (filterDef != null && !__instance.Allows(filterDef))
                __result = false;
        }
    }

    // Storage frameworks may call StorageSettings directly or replace the
    // ordinary ThingFilter evaluation. Enforce the special selection at the
    // shared storage-settings boundary as well.
    [HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.AllowedToAccept), typeof(Thing))]
    [HarmonyPriority(Priority.Last)]
    public static class StorageSettings_EnforceAutomaticApparel_Patch
    {
        public static void Postfix(StorageSettings __instance, Thing t, ref bool __result)
        {
            if (!__result || t?.def?.apparel == null)
                return;

            ThingFilter filter = Traverse.Create(__instance).Field("filter").GetValue<ThingFilter>();
            if (filter == null)
                return;

            bool automatic = AutomaticApparelClassifier.Matches(t);
            string filterName = automatic
                ? "AutomaticApparel_AllowAutomatic"
                : "AutomaticApparel_AllowNonAutomatic";
            SpecialThingFilterDef filterDef =
                DefDatabase<SpecialThingFilterDef>.GetNamedSilentFail(filterName);

            if (filterDef != null && !filter.Allows(filterDef))
                __result = false;
        }
    }

}

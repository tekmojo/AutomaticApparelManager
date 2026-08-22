using System.Linq;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Storage;
using HarmonyLib;
using RimWorld;

namespace AutomaticOutfitManager.Patches
{
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.GetInspectString))]
    public static class Apparel_GetInspectString_Patch
    {
        public static void Postfix(Apparel __instance, ref string __result)
        {
            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            if (__instance == null || component == null)
                return;

            string managedLabel = null;
            if (ManagedApparelClassifier.Matches(__instance.def))
            {
                var matchingRules = component.Rules
                    .Where(rule => rule != null &&
                                   rule.Enabled &&
                                   rule.RequiredApparel != null &&
                                   rule.RequiredApparel.Contains(__instance.def))
                    .ToList();
                var workAreas = matchingRules
                    .Where(rule => rule.Area != null)
                    .Select(rule => rule.Area.Label)
                    .Distinct()
                    .ToList();
                var lockerAreas = matchingRules
                    .Where(rule => rule.ChangingArea != null)
                    .Select(rule => rule.ChangingArea.Label)
                    .Distinct()
                    .ToList();

                managedLabel = "Automatic Outfit Manager: Required work gear";
                if (workAreas.Count > 0)
                    managedLabel += $"\nRequired in: {string.Join(", ", workAreas)}";
                if (lockerAreas.Count > 0)
                    managedLabel += $"\nLocker room: {string.Join(", ", lockerAreas)}";
            }
            else if (component.IsManagedApparel(__instance))
            {
                string owner = component.SavedOwnerFor(__instance);
                managedLabel = string.IsNullOrEmpty(owner)
                    ? "Automatic Outfit Manager: Saved personal gear"
                    : $"Automatic Outfit Manager: Saved personal gear — {owner}";
            }

            if (managedLabel == null)
                return;

            __result = string.IsNullOrEmpty(__result)
                ? managedLabel
                : __result + "\n" + managedLabel;
        }
    }
}

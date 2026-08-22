using System.Collections.Generic;
using System.Reflection;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Storage;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutomaticOutfitManager.Patches
{
    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "ApparelScoreGain")]
    [HarmonyPriority(Priority.Last)]
    public static class JobGiverOptimizeApparel_ScoreGain_SavedOwner_Patch
    {
        public static void Postfix(Pawn __0, Apparel __1, ref float __result)
        {
            Pawn pawn = __0;
            Apparel apparel = __1;
            if (apparel == null || pawn == null)
                return;

            // Rule apparel belongs to the shared locker pool. It must only be
            // worn by an AutomaticOutfitManager transition (or an explicit player
            // order), never selected as a pawn's ordinary optimized outfit.
            if (ManagedApparelClassifier.Matches(apparel.def))
            {
                __result = float.MinValue;
                return;
            }

            if (AutomaticOutfitManagerGameComponent.Current?.IsSavedForOtherPawn(apparel, pawn) == true)
                __result = float.MinValue;
        }
    }

    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "ApparelScoreRaw")]
    [HarmonyPriority(Priority.Last)]
    public static class JobGiverOptimizeApparel_ScoreRaw_SavedOwner_Patch
    {
        public static void Postfix(Pawn __0, Apparel __1, ref float __result)
        {
            Pawn pawn = __0;
            Apparel apparel = __1;
            if (apparel == null || pawn == null)
                return;

            if (ManagedApparelClassifier.Matches(apparel.def))
            {
                __result = float.MinValue;
                return;
            }

            if (AutomaticOutfitManagerGameComponent.Current?.IsSavedForOtherPawn(apparel, pawn) == true)
                __result = float.MinValue;
        }
    }

    [HarmonyPatch]
    public static class EquipmentUtility_CanEquip_SavedApparel_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(EquipmentUtility)))
            {
                if (method.Name == nameof(EquipmentUtility.CanEquip))
                    yield return method;
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Thing __0, Pawn __1, ref bool __result)
        {
            if (!__result || !(__0 is Apparel apparel) || __1 == null)
                return;

            // Required work gear is shared. Ownership only protects saved
            // personal apparel that is not itself assigned to a rule.
            if (ManagedApparelClassifier.Matches(apparel.def))
                return;

            if (AutomaticOutfitManagerGameComponent.Current?.IsSavedForOtherPawn(apparel, __1) == true)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Wear),
        typeof(Apparel), typeof(bool), typeof(bool))]
    [HarmonyPriority(Priority.First)]
    public static class PawnApparelTracker_Wear_SavedApparel_Patch
    {
        public static bool Prefix(Pawn_ApparelTracker __instance, Apparel newApparel)
        {
            if (newApparel == null || ManagedApparelClassifier.Matches(newApparel.def))
                return true;

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            return AutomaticOutfitManagerGameComponent.Current?.IsSavedForOtherPawn(newApparel, pawn) != true;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    public static class Apparel_GetGizmos_SavedOwner_Patch
    {
        public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, ThingWithComps __instance)
        {
            foreach (Gizmo gizmo in __result)
                yield return gizmo;

            Apparel apparel = __instance as Apparel;
            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            if (apparel == null || component == null ||
                ManagedApparelClassifier.Matches(apparel.def))
            {
                yield break;
            }

            Pawn owner = component.SavedPawnFor(apparel);
            if (owner?.Spawned != true)
                yield break;

            yield return new Command_Action
            {
                defaultLabel = $"Jump to {owner.LabelShortCap}",
                defaultDesc = $"Select and center the camera on the pawn whose saved personal gear this is: {owner.LabelShortCap}.",
                icon = TexButton.ShowImportantLocations,
                action = () => CameraJumper.TryJumpAndSelect(owner)
            };

            yield return new Command_Action
            {
                defaultLabel = "Clear saved owner",
                defaultDesc = $"Remove {owner.LabelShortCap}'s saved-gear claim from this item. It becomes ordinary apparel and may be worn by another pawn.",
                icon = TexCommand.ClearPrioritizedWork,
                action = () => component.ClearSavedOwner(apparel)
            };
        }
    }
}

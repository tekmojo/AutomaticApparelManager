using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.State;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Patches
{
    [HarmonyPatch]
    public static class ReservationUtility_SavedApparel_Patch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(ReservationUtility)))
            {
                if (method.Name == nameof(ReservationUtility.CanReserve) ||
                    method.Name == nameof(ReservationUtility.CanReserveAndReach))
                {
                    yield return method;
                }
            }
        }

        [HarmonyPriority(Priority.Last)]
        public static void Postfix(object[] __args, ref bool __result)
        {
            if (!__result || __args == null)
                return;

            Pawn pawn = __args.OfType<Pawn>().FirstOrDefault();
            Apparel apparel = ApparelTarget(__args);
            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            if (pawn == null || apparel == null || component == null ||
                !component.IsManagedApparel(apparel))
            {
                return;
            }

            // Guests and other non-colony pawns should never select jobs that
            // reserve managed apparel. The StartJob guard remains a fallback
            // for modded jobs that skip RimWorld's reservation checks.
            if (pawn.Faction != Faction.OfPlayer &&
                !IsAssignedToPawn(component.StateFor(pawn), apparel))
            {
                __result = false;
                return;
            }

            // Apparel required by an enabled rule is shared work gear. A
            // particular item can still carry an older saved-owner record
            // after being removed from a pawn, but that record must not stop
            // colony haulers from returning the loose item to locker storage.
            if (AutomaticOutfitManager.Storage.ManagedApparelClassifier.Matches(apparel.def))
            {
                if (component.IsManagedApparelAssignedToOtherPawn(apparel, pawn))
                    __result = false;
                return;
            }

            Pawn owner = component.SavedPawnFor(apparel);
            if (owner == null || owner == pawn)
                return;

            PawnApparelState ownerState = component.StateFor(owner);
            if (ownerState != null &&
                (ownerState.Transition == ApparelTransition.ReturningToChangingArea ||
                 ownerState.Transition == ApparelTransition.Restoring))
            {
                __result = false;
            }
        }

        private static bool IsAssignedToPawn(PawnApparelState state, Apparel apparel)
        {
            if (state == null || apparel == null)
                return false;

            if (state.Transition == ApparelTransition.Preparing ||
                state.Transition == ApparelTransition.Active)
                return state.ManagedApparel?.Contains(apparel) == true;

            if (state.Transition == ApparelTransition.Restoring)
                return state.ManagedApparel?.Contains(apparel) == true ||
                       state.OriginalApparel?.Contains(apparel) == true;

            return false;
        }

        private static Apparel ApparelTarget(IEnumerable<object> arguments)
        {
            foreach (object argument in arguments)
            {
                if (argument is LocalTargetInfo target && target.Thing is Apparel targetApparel)
                    return targetApparel;

                if (argument is Apparel apparel)
                    return apparel;
            }

            return null;
        }
    }
}

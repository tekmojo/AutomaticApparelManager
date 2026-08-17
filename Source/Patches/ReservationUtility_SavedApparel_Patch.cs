using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutomaticApparel.Core;
using AutomaticApparel.State;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
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
            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            if (pawn == null || apparel == null || component == null ||
                !component.IsManagedApparel(apparel))
            {
                return;
            }

            // Guests and other non-colony pawns should never select jobs that
            // reserve managed apparel. The StartJob guard remains a fallback
            // for modded jobs that skip RimWorld's reservation checks.
            if (pawn.Faction != Faction.OfPlayer)
            {
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

using System;
using System.Reflection;
using AutomaticApparel.Core;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutomaticApparel.Patches
{
    [HarmonyPatch]
    public static class PawnApparelTracker_TryDrop_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_ApparelTracker),
                nameof(Pawn_ApparelTracker.TryDrop),
                new[]
                {
                    typeof(Apparel),
                    typeof(Apparel).MakeByRefType(),
                    typeof(IntVec3),
                    typeof(bool)
                });
        }

        public static void Prefix(Pawn_ApparelTracker __instance, Apparel ap, ref bool forbid)
        {
            if (!forbid || ap == null)
                return;

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            if (component?.StateFor(pawn) != null)
                forbid = false;
        }

        public static void Postfix(Pawn_ApparelTracker __instance, Apparel ap, Apparel resultingAp)
        {
            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (AutomaticApparelGameComponent.Current?.StateFor(pawn) == null)
                return;

            Apparel dropped = resultingAp ?? ap;
            if (dropped?.Spawned == true && dropped.IsForbidden(Faction.OfPlayer))
                dropped.SetForbidden(false, false);
        }
    }
}

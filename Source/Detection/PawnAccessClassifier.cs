using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace AutomaticOutfitManager.Detection
{
    public static class PawnAccessClassifier
    {
        public static bool IsHostedGuest(Pawn pawn)
        {
            if (pawn?.guest == null || pawn.guest.IsPrisoner || pawn.IsSlave)
                return false;

            // Hospitality and similar guest systems may temporarily expose a
            // hosted worker as player-faction. HostFaction is the stable signal;
            // retain the friendly foreign-faction fallback for vanilla guests.
            return IsArrivedHospitalityGuest(pawn) ||
                   pawn.HostFaction == Faction.OfPlayer ||
                   (pawn.Faction != null && pawn.Faction != Faction.OfPlayer &&
                    !pawn.Faction.HostileTo(Faction.OfPlayer));
        }

        private static bool IsArrivedHospitalityGuest(Pawn pawn)
        {
            Type compType = AccessTools.TypeByName("Hospitality.CompGuest");
            if (compType == null || pawn?.AllComps == null)
                return false;

            ThingComp comp = pawn.AllComps.FirstOrDefault(candidate =>
                candidate != null && compType.IsInstanceOfType(candidate));
            if (comp == null)
                return false;

            FieldInfo arrivedField = AccessTools.Field(compType, "arrived");
            FieldInfo sentAwayField = AccessTools.Field(compType, "sentAway");
            bool arrived = arrivedField?.GetValue(comp) is bool value && value;
            bool sentAway = sentAwayField?.GetValue(comp) is bool sent && sent;
            return arrived && !sentAway;
        }

        public static bool IsColonyPrisoner(Pawn pawn) =>
            pawn?.guest?.IsPrisoner == true && pawn.IsPrisonerOfColony;

        public static bool IsApparelEligibleHuman(Pawn pawn) =>
            pawn?.RaceProps?.Humanlike == true && pawn.apparel != null &&
            (pawn.IsColonist || pawn.IsSlave || IsHostedGuest(pawn) ||
             IsColonyPrisoner(pawn));
    }
}

using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Detection
{
    public static class ApparelFinder
    {
        public static Apparel FindBest(Pawn pawn, ThingDef def, Area changingArea = null)
        {
            if (pawn?.Map == null || def == null)
                return null;

            Apparel preferred = FindClosest(pawn, def, changingArea);
            return preferred ?? FindClosest(pawn, def, null);
        }

        private static Apparel FindClosest(Pawn pawn, ThingDef def, Area area)
        {
            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(def),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                thing => thing is Apparel apparel &&
                         apparel.Spawned &&
                         (area == null ||
                          (area.Map == apparel.Map && area[apparel.Position])) &&
                         !apparel.IsForbidden(pawn) &&
                         EquipmentUtility.CanEquip(apparel, pawn) &&
                         pawn.CanReserve(apparel)) as Apparel;
        }
    }
}

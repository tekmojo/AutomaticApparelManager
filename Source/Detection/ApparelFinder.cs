using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Detection
{
    public static class ApparelFinder
    {
        public static Apparel FindBest(Pawn pawn, ThingDef def)
        {
            if (pawn?.Map == null || def == null)
                return null;

            return GenClosest.ClosestThingReachable(
                pawn.Position,
                pawn.Map,
                ThingRequest.ForDef(def),
                PathEndMode.ClosestTouch,
                TraverseParms.For(pawn),
                9999f,
                thing => thing is Apparel apparel &&
                         apparel.Spawned &&
                         !apparel.IsForbidden(pawn) &&
                         pawn.CanReserve(apparel)) as Apparel;
        }
    }
}

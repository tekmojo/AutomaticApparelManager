using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Detection
{
    /// <summary>
    /// Holds a short-lived claim on the work target that caused a pawn to begin
    /// changing outfits. RimWorld does not reserve that target while Wear jobs
    /// run, so without this guard every eligible pawn can select the same Flick,
    /// frame, or bill and start its own apparel transition.
    /// </summary>
    public static class ManagedWorkClaimRegistry
    {
        private sealed class Claim
        {
            public Pawn Owner;
            public Map Map;
            public Thing Thing;
            public IntVec3 Cell;
            public int UntilTick;
        }

        private static readonly List<Claim> Claims = new List<Claim>();

        public static bool TryClaim(Pawn pawn, Job job, int ticks = 15000)
        {
            if (!TryGetTarget(pawn, job, out Map map, out Thing thing, out IntVec3 cell))
                return true;

            Cleanup();
            Claim existing = Claims.FirstOrDefault(claim =>
                Matches(claim, map, thing, cell));
            if (existing != null && existing.Owner != pawn)
                return false;

            Claims.RemoveAll(claim => claim.Owner == pawn && claim != existing);
            if (existing == null)
            {
                Claims.Add(new Claim
                {
                    Owner = pawn,
                    Map = map,
                    Thing = thing,
                    Cell = cell,
                    UntilTick = CurrentTick + ticks
                });
            }
            else
            {
                existing.UntilTick = CurrentTick + ticks;
            }

            return true;
        }

        public static bool IsClaimedByOther(Pawn pawn, Job job)
        {
            if (!TryGetTarget(pawn, job, out Map map, out Thing thing, out IntVec3 cell))
                return false;
            return IsClaimedByOther(pawn, map, thing, cell);
        }

        public static bool IsClaimedByOther(
            Pawn pawn, Map map, Thing thing, IntVec3 cell)
        {
            if (pawn == null || map == null)
                return false;

            Cleanup();
            return Claims.Any(claim =>
                claim.Owner != pawn && Matches(claim, map, thing, cell));
        }

        public static void Release(Pawn pawn, Job job)
        {
            if (pawn == null)
                return;
            if (!TryGetTarget(pawn, job, out Map map, out Thing thing, out IntVec3 cell))
                return;

            Claims.RemoveAll(claim =>
                claim.Owner == pawn && Matches(claim, map, thing, cell));
        }

        public static void ReleaseAll(Pawn pawn)
        {
            if (pawn != null)
                Claims.RemoveAll(claim => claim.Owner == pawn);
        }

        public static bool HasActiveClaim(Pawn pawn)
        {
            if (pawn == null)
                return false;

            Cleanup();
            return Claims.Any(claim => claim.Owner == pawn);
        }

        public static string DescribeActiveClaim(Pawn pawn)
        {
            if (pawn == null)
                return "none";

            Cleanup();
            Claim claim = Claims.FirstOrDefault(candidate => candidate.Owner == pawn);
            if (claim == null)
                return "none";
            return claim.Thing != null
                ? $"{claim.Thing.LabelCap} at {claim.Cell}"
                : $"cell {claim.Cell}";
        }

        private static bool TryGetTarget(
            Pawn pawn, Job job, out Map map, out Thing thing, out IntVec3 cell)
        {
            map = pawn?.Map;
            thing = null;
            cell = IntVec3.Invalid;
            if (map == null || job == null)
                return false;

            LocalTargetInfo target = job.targetA.IsValid
                ? job.targetA
                : job.targetB.IsValid
                    ? job.targetB
                    : job.targetC;
            if (!target.IsValid)
                return false;

            if (target.HasThing)
            {
                thing = target.Thing;
                map = thing?.MapHeld ?? map;
                cell = thing?.PositionHeld ?? IntVec3.Invalid;
            }
            else
            {
                cell = target.Cell;
            }

            return map != null && cell.IsValid && cell.InBounds(map);
        }

        private static bool Matches(
            Claim claim, Map map, Thing thing, IntVec3 cell)
        {
            if (claim?.Map != map)
                return false;
            if (claim.Thing != null || thing != null)
                return claim.Thing == thing;
            return claim.Cell == cell;
        }

        private static void Cleanup()
        {
            int now = CurrentTick;
            Claims.RemoveAll(claim =>
                claim == null || claim.UntilTick <= now ||
                claim.Owner?.Spawned != true || claim.Owner.Map != claim.Map ||
                (claim.Thing != null && claim.Thing.Destroyed));
        }

        private static int CurrentTick => Find.TickManager?.TicksGame ?? 0;
    }
}

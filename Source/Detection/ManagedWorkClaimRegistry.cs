using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Detection
{
    /// <summary>
    /// Holds short-lived claims on every concrete thing used by the work job that
    /// caused a pawn to begin changing outfits. RimWorld does not keep the job's
    /// reservations while Wear jobs run, so another pawn could otherwise take a
    /// bill ingredient or queued target even when the primary workstation/frame
    /// remains claimed by the outfitting pawn.
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

        private sealed class WorkTarget
        {
            public Map Map;
            public Thing Thing;
            public IntVec3 Cell;
        }

        private static readonly List<Claim> Claims = new List<Claim>();

        public static void ResetForLoadedGame()
        {
            // Claims are intentionally runtime-only. Loading another save in the
            // same RimWorld process must discard owners and maps from the prior
            // game before persisted pending jobs rebuild their protection.
            Claims.Clear();
        }

        public static bool TryClaim(Pawn pawn, Job job, int ticks = 15000)
        {
            List<WorkTarget> targets = TargetsFor(pawn, job);
            if (targets.Count == 0)
                return true;

            Cleanup();
            if (targets.Any(target => Claims.Any(claim =>
                    claim.Owner != pawn && Matches(claim, target))))
            {
                return false;
            }

            // Claim the complete target set atomically. Only replace the pawn's
            // previous claims after every new target has passed the contention
            // check, so a failed handoff cannot leave a partial claim behind.
            Claims.RemoveAll(claim => claim.Owner == pawn);
            int untilTick = CurrentTick + ticks;
            foreach (WorkTarget target in targets)
            {
                Claims.Add(new Claim
                {
                    Owner = pawn,
                    Map = target.Map,
                    Thing = target.Thing,
                    Cell = target.Cell,
                    UntilTick = untilTick
                });
            }

            return true;
        }

        public static bool IsClaimedByOther(Pawn pawn, Job job)
        {
            List<WorkTarget> targets = TargetsFor(pawn, job);
            if (targets.Count == 0)
                return false;

            Cleanup();
            return targets.Any(target => Claims.Any(claim =>
                claim.Owner != pawn && Matches(claim, target)));
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
            List<WorkTarget> targets = TargetsFor(pawn, job);
            if (targets.Count == 0)
                return;

            Claims.RemoveAll(claim =>
                claim.Owner == pawn && targets.Any(target => Matches(claim, target)));
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
            string primary = claim.Thing != null
                ? $"{claim.Thing.LabelCap} at {claim.Cell}"
                : $"cell {claim.Cell}";
            int relatedCount = Claims.Count(candidate =>
                candidate.Owner == pawn && candidate != claim);
            return relatedCount > 0
                ? $"{primary} (+{relatedCount} related target{(relatedCount == 1 ? "" : "s")})"
                : primary;
        }

        private static List<WorkTarget> TargetsFor(Pawn pawn, Job job)
        {
            var targets = new List<WorkTarget>();
            if (pawn?.Map == null || job == null)
                return targets;

            foreach (LocalTargetInfo target in EnumerateTargets(job))
            {
                if (!target.IsValid || !target.HasThing || target.Thing == null)
                    continue;

                Thing thing = target.Thing;
                Map map = thing.MapHeld ?? pawn.Map;
                IntVec3 cell = thing.PositionHeld;
                if (map == null || !cell.IsValid || !cell.InBounds(map) ||
                    targets.Any(existing => Matches(existing, map, thing, cell)))
                {
                    continue;
                }

                targets.Add(new WorkTarget { Map = map, Thing = thing, Cell = cell });
            }

            // Thing targets are the reservation-sensitive part of bills, hauls,
            // construction, training, and similar work. Jobs without any Thing
            // target retain the former first-cell claim behavior.
            if (targets.Count == 0)
            {
                LocalTargetInfo cellTarget = EnumerateTargets(job)
                    .FirstOrDefault(target => target.IsValid && !target.HasThing);
                if (cellTarget.IsValid && cellTarget.Cell.IsValid &&
                    cellTarget.Cell.InBounds(pawn.Map))
                {
                    targets.Add(new WorkTarget
                    {
                        Map = pawn.Map,
                        Thing = null,
                        Cell = cellTarget.Cell
                    });
                }
            }

            return targets;
        }

        private static IEnumerable<LocalTargetInfo> EnumerateTargets(Job job)
        {
            if (job == null)
                yield break;

            yield return job.targetA;
            yield return job.targetB;
            yield return job.targetC;

            if (job.targetQueueA != null)
            {
                foreach (LocalTargetInfo target in job.targetQueueA)
                    yield return target;
            }

            if (job.targetQueueB != null)
            {
                foreach (LocalTargetInfo target in job.targetQueueB)
                    yield return target;
            }
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

        private static bool Matches(Claim claim, WorkTarget target) =>
            target != null && Matches(claim, target.Map, target.Thing, target.Cell);

        private static bool Matches(
            WorkTarget target, Map map, Thing thing, IntVec3 cell)
        {
            if (target?.Map != map)
                return false;
            if (target.Thing != null || thing != null)
                return target.Thing == thing;
            return target.Cell == cell;
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

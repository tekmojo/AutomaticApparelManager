using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Core;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Storage
{
    /// <summary>
    /// Low-priority, ordinary hauling work that returns loose required gear to
    /// valid storage in a paused rule's locker room. Because this is a work
    /// giver, schedules, needs, drafting and forced orders retain priority.
    /// </summary>
    public sealed class WorkGiver_LockerRestock : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest =>
            ThingRequest.ForGroup(ThingRequestGroup.Apparel);

        public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            TryMakeJob(pawn, t as Apparel, out _);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            TryMakeJob(pawn, t as Apparel, out Job job) ? job : null;

        private static bool TryMakeJob(Pawn pawn, Apparel apparel, out Job job)
        {
            job = null;
            if (apparel?.Spawned != true || pawn?.Map == null ||
                apparel.Map != pawn.Map || apparel.IsForbidden(pawn) ||
                !pawn.CanReserve(apparel) ||
                !pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Some))
            {
                return false;
            }

            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            var rules = component?.Rules?
                .Where(rule => rule != null && rule.Enabled && rule.WorkAreaPaused &&
                               rule.ChangingArea?.Map == pawn.Map &&
                               rule.RequiredApparel?.Contains(apparel.def) == true)
                .ToList();
            if (rules == null || rules.Count == 0)
                return false;

            // Once the item is accepted by an enabled haul destination inside
            // any matching locker, restocking is complete. Without this check,
            // treating every scan as StoragePriority.Unstored lets the bot find
            // another nominally "better" locker cell forever.
            IHaulDestination currentDestination = StoreUtility.CurrentHaulDestinationOf(apparel);
            if (currentDestination != null && currentDestination.HaulDestinationEnabled &&
                currentDestination.Accepts(apparel) &&
                rules.Any(rule => rule.ChangingArea[apparel.Position]))
            {
                return false;
            }

            foreach (var rule in rules)
            {
                IEnumerable<ISlotGroup> lockerStorage = rule.ChangingArea.ActiveCells
                    .Select(cell => cell.GetSlotGroup(pawn.Map))
                    .Where(group => group != null)
                    .Distinct();

                foreach (ISlotGroup slotGroup in lockerStorage)
                {
                    if (!StoreUtility.TryFindBestBetterStoreCellForIn(
                            apparel, pawn, pawn.Map, StoragePriority.Unstored,
                            pawn.Faction, slotGroup, out IntVec3 destination))
                    {
                        continue;
                    }

                    job = JobMaker.MakeJob(JobDefOf.HaulToCell, apparel, destination);
                    job.count = 1;
                    job.haulOpportunisticDuplicates = false;
                    return true;
                }
            }

            return false;
        }
    }
}

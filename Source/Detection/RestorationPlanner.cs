using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Rules;
using AutomaticOutfitManager.State;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Detection
{
    public static class RestorationPlanner
    {
        public static bool TryMakeHeldOriginalsAccessible(
            Pawn pawn, PawnApparelState state)
        {
            if (pawn?.Map == null || state?.OriginalApparel == null)
                return false;

            bool droppedAny = false;
            foreach (Apparel item in state.OriginalApparel.Where(item =>
                         item != null && !item.Destroyed && !item.Spawned &&
                         pawn.apparel?.WornApparel.Contains(item) != true).ToList())
            {
                IThingHolder holder = item.ParentHolder;
                ThingOwner owner = holder?.GetDirectlyHeldThings();
                if (owner == null || !owner.Contains(item) ||
                    item.MapHeld != pawn.Map || !item.PositionHeld.IsValid)
                {
                    continue;
                }

                // Never pull a saved item out of somebody else's inventory.
                // Ownership enforcement will make that pawn release it through
                // its normal safe path instead.
                IThingHolder ancestor = holder;
                bool heldByOtherPawn = false;
                while (ancestor != null)
                {
                    if (ancestor is Pawn holdingPawn && holdingPawn != pawn)
                    {
                        heldByOtherPawn = true;
                        break;
                    }
                    ancestor = ancestor.ParentHolder;
                }
                if (heldByOtherPawn)
                    continue;

                IntVec3 dropCell = item.PositionHeld;
                try
                {
                    if (owner.TryDrop(
                            item, dropCell, pawn.Map, ThingPlaceMode.Near,
                            out Thing dropped) && dropped is Apparel droppedApparel)
                    {
                        if (droppedApparel.IsForbidden(pawn))
                            droppedApparel.SetForbidden(false, false);
                        droppedAny = true;
                        if (Prefs.DevMode)
                            Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: recovered saved apparel {droppedApparel.LabelCap} from an inventory or container.");
                        // Release one exact item at a time. Its Wear job can
                        // reserve it immediately, avoiding a pile of saved gear
                        // that storage or inventory mods may collect again while
                        // the pawn is still restoring earlier layers.
                        break;
                    }
                }
                catch (System.Exception exception)
                {
                    if (Prefs.DevMode)
                        Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: could not release saved apparel {item.LabelCap} from its holder. {exception.GetType().Name}: {exception.Message}");
                }
            }

            return droppedAny;
        }

        public static List<Job> BuildJobs(
            Pawn pawn,
            PawnApparelState state,
            ApparelRule activeRule,
            out bool hasUnavailableOriginal)
        {
            var jobs = new List<Job>();
            hasUnavailableOriginal = false;
            if (pawn?.apparel == null || state == null)
                return jobs;

            var original = new HashSet<Apparel>(state.OriginalApparel.Where(item => item != null));
            var automatic = new HashSet<Apparel>(state.ManagedApparel.Where(item => item != null));

            // Backward-compatible fallback for snapshots saved before automatic item
            // references were recorded explicitly.
            if (automatic.Count == 0 && activeRule?.RequiredApparel != null)
            {
                foreach (Apparel worn in pawn.apparel.WornApparel)
                {
                    if (!original.Contains(worn) && activeRule.RequiredApparel.Contains(worn.def))
                        automatic.Add(worn);
                }
            }

            // Only apparel explicitly assigned by the intervention is removed.
            // Pawns can legitimately equip utility belts, weapons-as-apparel,
            // ideology items, or other non-work gear while a session is active.
            // Treating every post-snapshot item as automatic stripped those
            // unrelated slots during restoration.

            foreach (Apparel item in pawn.apparel.WornApparel.Where(automatic.Contains).ToList())
            {
                jobs.Add(JobMaker.MakeJob(JobDefOf.RemoveApparel, item));
            }

            foreach (Apparel item in state.OriginalApparel)
            {
                if (item == null || item.Destroyed || pawn.apparel.WornApparel.Contains(item))
                    continue;

                if (item.Spawned && item.IsForbidden(pawn))
                    item.SetForbidden(false, false);

                if (!item.Spawned || item.Map != pawn.Map || item.IsForbidden(pawn) ||
                    !pawn.CanReserve(item) || !pawn.CanReach(item, PathEndMode.ClosestTouch, Danger.Deadly))
                {
                    hasUnavailableOriginal = true;
                    continue;
                }

                Job wearJob = JobMaker.MakeJob(JobDefOf.Wear, item);
                // Restoring the exact captured outfit is an AutomaticOutfitManager
                // transition, not ordinary outfit optimization. Mark it forced
                // for the same reason as required work gear: apparel policies
                // and compatibility patches must not repeatedly reject an item
                // the pawn was already wearing when the snapshot was taken.
                wearJob.playerForced = true;
                jobs.Add(wearJob);
            }

            return jobs;
        }
    }
}

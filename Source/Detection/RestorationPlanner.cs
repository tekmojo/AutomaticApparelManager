using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Rules;
using AutomaticApparel.State;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Detection
{
    public static class RestorationPlanner
    {
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
            var automatic = new HashSet<Apparel>(state.AutomaticApparel.Where(item => item != null));

            // Backward-compatible fallback for snapshots saved before automatic item
            // references were recorded explicitly.
            if (activeRule?.RequiredApparel != null)
            {
                foreach (Apparel worn in pawn.apparel.WornApparel)
                {
                    if (!original.Contains(worn) && activeRule.RequiredApparel.Contains(worn.def))
                        automatic.Add(worn);
                }
            }

            // Anything worn after the snapshot is replacement apparel, even when
            // an older save did not record its exact item reference.
            foreach (Apparel worn in pawn.apparel.WornApparel)
            {
                if (!original.Contains(worn))
                    automatic.Add(worn);
            }

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

                jobs.Add(JobMaker.MakeJob(JobDefOf.Wear, item));
            }

            return jobs;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.State;
using AutomaticApparel.Storage;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class PawnJobTracker_StartJob_Patch
    {
        public static void Prefix(
            Pawn_JobTracker __instance,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag,
            bool fromQueue)
        {
            if (newJob == null)
                return;

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null)
                return;

            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            PawnApparelState state = component?.StateFor(pawn);

            // Keep guests and other non-colony pawns from reserving, hauling,
            // repairing, processing, or wearing managed apparel. Checking the
            // common job boundary makes this work for native and modded jobs,
            // including bills that place their ingredient in a target queue.
            if (pawn.Faction != Faction.OfPlayer && JobTargetsManagedApparel(newJob))
            {
                Job waitJob = JobMaker.MakeJob(JobDefOf.Wait);
                waitJob.expiryInterval = 30;
                newJob = waitJob;
                jobGiver = null;
                tag = null;
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel wearTarget &&
                !AutomaticApparelClassifier.Matches(wearTarget.def) &&
                component?.IsSavedForOtherPawn(wearTarget, pawn) == true)
            {
                ReplaceWithBriefWait(ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel transitionWearTarget &&
                state != null &&
                !IsAllowedTransitionWear(state, transitionWearTarget))
            {
                ReplaceWithBriefWait(ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear || newJob.def == JobDefOf.RemoveApparel)
                return;

            if (state != null)
            {
                var activeRule = component.RuleById(state.ActiveRuleId);
                if (state.Transition == ApparelTransition.ReturningToChangingArea &&
                    activeRule?.ChangingArea != null &&
                    JobTargetsArea(newJob, activeRule.ChangingArea))
                {
                    return;
                }

                if (state.Transition == ApparelTransition.Restoring &&
                    newJob.def == JobDefOf.HaulToCell &&
                    newJob.targetA.Thing is Apparel returnItem &&
                    (state.AutomaticApparel?.Contains(returnItem) ?? false))
                {
                    return;
                }

                bool matchesActiveRule = RuleEvaluator.MatchesRule(pawn, newJob, activeRule);
                if (!matchesActiveRule && state.Transition == ApparelTransition.Preparing)
                    return;

                if (!matchesActiveRule && state.Transition != ApparelTransition.Preparing)
                {
                    if (activeRule?.ChangingArea != null &&
                        !PawnInsideArea(pawn, activeRule.ChangingArea) &&
                        TryFindChangingCell(pawn, activeRule.ChangingArea, out IntVec3 changingCell))
                    {
                        state.Transition = ApparelTransition.ReturningToChangingArea;
                        var returnJobs = new List<Job>
                        {
                            JobMaker.MakeJob(JobDefOf.Goto, changingCell)
                        };
                        QueueBeforeCurrent(__instance, ref newJob, ref jobGiver, ref tag, returnJobs);
                        return;
                    }

                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    if (state.Transition == ApparelTransition.Restoring &&
                        state.LastRestorationAttemptTick >= 0 &&
                        currentTick - state.LastRestorationAttemptTick < 60)
                    {
                        __instance.ClearQueuedJobs(false);
                        ReplaceWithBriefWait(ref newJob, ref jobGiver, ref tag);
                        return;
                    }

                    List<Job> restorationJobs = RestorationPlanner.BuildJobs(
                        pawn, state, activeRule, out bool hasUnavailableOriginal);
                    if (restorationJobs.Count > 0)
                    {
                        state.Transition = ApparelTransition.Restoring;
                        state.LastRestorationAttemptTick = currentTick;
                        state.UnavailableRestorationAttempts = hasUnavailableOriginal
                            ? state.UnavailableRestorationAttempts + 1
                            : 0;
                        QueueRestorationJobs(__instance, ref newJob, ref jobGiver, ref tag, restorationJobs);

                        if (Prefs.DevMode)
                            Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: restoring apparel with {restorationJobs.Count} job(s) before {__instance.curJob?.def?.defName ?? "next job"}.");
                        return;
                    }

                    if (hasUnavailableOriginal)
                    {
                        state.Transition = ApparelTransition.Restoring;
                        state.LastRestorationAttemptTick = currentTick;
                        state.UnavailableRestorationAttempts++;
                        __instance.ClearQueuedJobs(false);
                        ReplaceWithBriefWait(ref newJob, ref jobGiver, ref tag);
                        return;
                    }

                    __instance.ClearQueuedJobs(false);
                    component.EndIntervention(pawn);
                }
            }

            if (newJob.def == JobDefOf.HaulToCell &&
                AutomaticApparelClassifier.Matches(newJob.targetA.Thing))
            {
                return;
            }

            var rule = RuleEvaluator.MatchingRule(pawn, newJob);
            if (rule == null)
                return;

            List<ThingDef> missing = RuleEvaluator.MissingRequiredApparel(pawn, rule);
            if (missing.Count == 0)
            {
                PawnApparelState activeState = component?.StateFor(pawn);
                if (activeState != null && activeState.ActiveRuleId == rule.Id &&
                    activeState.Transition == ApparelTransition.Preparing)
                {
                    activeState.Transition = ApparelTransition.Active;
                    if (Prefs.DevMode)
                        Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: preparation complete; rule '{rule.Name}' is active.");
                }
                return;
            }

            var wearJobs = new List<Job>();
            foreach (ThingDef def in missing)
            {
                Apparel apparel = ApparelFinder.FindBest(pawn, def, rule.ChangingArea);
                if (apparel == null)
                {
                    Log.Warning($"[Automatic Apparel] {pawn.LabelShortCap}: no reachable {def.LabelCap} found for rule '{rule.Name}'.");
                    return;
                }

                Job wearJob = JobMaker.MakeJob(JobDefOf.Wear, apparel);
                // Rule-required safety apparel must be wearable even when the
                // pawn's ordinary outfit policy does not include it.
                wearJob.playerForced = true;
                wearJobs.Add(wearJob);
            }

            if (wearJobs.Count == 0)
                return;

            component?.BeginIntervention(pawn, rule, wearJobs.Select(job => job.targetA.Thing as Apparel));

            if (Prefs.DevMode)
                Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: intercepted {newJob.def.defName}; preparing {wearJobs.Count} apparel item(s) for '{rule.Name}'.");

            QueueBeforeCurrent(__instance, ref newJob, ref jobGiver, ref tag, wearJobs);
        }

        private static bool PawnInsideArea(Pawn pawn, Area area) =>
            pawn?.Map != null && area?.Map == pawn.Map &&
            pawn.Position.IsValid && pawn.Position.InBounds(pawn.Map) && area[pawn.Position];

        private static bool TryFindChangingCell(Pawn pawn, Area area, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (pawn?.Map == null || area?.Map != pawn.Map)
                return false;

            cell = area.ActiveCells
                .Where(candidate => candidate.Standable(pawn.Map) &&
                                    pawn.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(candidate => candidate.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
            return cell.IsValid;
        }

        private static bool JobTargetsArea(Job job, Area area)
        {
            if (job == null || area == null)
                return false;

            LocalTargetInfo target = job.targetA;
            IntVec3 cell = target.HasThing ? target.Thing?.PositionHeld ?? IntVec3.Invalid : target.Cell;
            return cell.IsValid && cell.InBounds(area.Map) && area[cell];
        }

        private static bool JobTargetsManagedApparel(Job job)
        {
            if (job == null)
                return false;

            if (IsManagedApparel(job.targetA) ||
                IsManagedApparel(job.targetB) ||
                IsManagedApparel(job.targetC))
            {
                return true;
            }

            return (job.targetQueueA?.Any(IsManagedApparel) ?? false) ||
                   (job.targetQueueB?.Any(IsManagedApparel) ?? false);
        }

        private static bool IsManagedApparel(LocalTargetInfo target)
        {
            Apparel apparel = target.Thing as Apparel;
            return apparel != null &&
                   AutomaticApparelGameComponent.Current?.IsManagedApparel(apparel) == true;
        }

        private static bool IsAllowedTransitionWear(PawnApparelState state, Apparel apparel)
        {
            if (state == null || apparel == null)
                return false;

            switch (state.Transition)
            {
                case ApparelTransition.Preparing:
                case ApparelTransition.Active:
                    return state.AutomaticApparel?.Contains(apparel) == true;
                case ApparelTransition.Restoring:
                    return state.OriginalApparel?.Contains(apparel) == true;
                case ApparelTransition.ReturningToChangingArea:
                default:
                    return false;
            }
        }

        private static void ReplaceWithBriefWait(
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag)
        {
            Job waitJob = JobMaker.MakeJob(JobDefOf.Wait);
            waitJob.expiryInterval = 30;
            newJob = waitJob;
            jobGiver = null;
            tag = null;
        }

        private static void QueueBeforeCurrent(
            Pawn_JobTracker tracker,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag,
            List<Job> jobs)
        {
            Job interruptedJob = newJob;
            tracker.jobQueue.EnqueueFirst(interruptedJob, tag);
            for (int i = jobs.Count - 1; i >= 1; i--)
                tracker.jobQueue.EnqueueFirst(jobs[i]);

            newJob = jobs[0];
            jobGiver = null;
            tag = null;
        }

        private static void QueueRestorationJobs(
            Pawn_JobTracker tracker,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag,
            List<Job> jobs)
        {
            // Restoration does not need to preserve the interrupted job: normal
            // AI will reconsider it after the saved outfit is complete. Clearing
            // the queue also repairs saves affected by the former retry loop,
            // which could accumulate hundreds of duplicate Wear jobs.
            tracker.ClearQueuedJobs(false);
            for (int i = jobs.Count - 1; i >= 1; i--)
                tracker.jobQueue.EnqueueFirst(jobs[i]);

            newJob = jobs[0];
            jobGiver = null;
            tag = null;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System;
using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.Rules;
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

            // Some modded robot thinkers start their wander job without passing
            // through the vanilla ThinkNode_JobGiver result patch. At this
            // boundary the originating giver is still available, so redirect
            // the job before it can enter the restricted area. The helper
            // converts it to a safe GotoWander destination and avoids the
            // cancel/reselect loop seen at doorways.
            if (PausedAreaWorkFilter.TryRedirectWanderingJob(pawn, newJob, jobGiver))
            {
                jobGiver = null;
                tag = null;
            }

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

            // Access controls also apply to animals, mechs, and modded robots,
            // but apparel intervention does not. Clear any legacy state created
            // for a non-humanlike unit and leave its real job/status untouched.
            if (pawn.RaceProps?.Humanlike != true || pawn.apparel == null)
            {
                if (state != null)
                    component.EndIntervention(pawn);
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel wearTarget &&
                !AutomaticApparelClassifier.Matches(wearTarget.def) &&
                component?.IsSavedForOtherPawn(wearTarget, pawn) == true)
            {
                ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel transitionWearTarget &&
                state != null &&
                !IsAllowedTransitionWear(state, transitionWearTarget))
            {
                ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
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

                if (state.Transition == ApparelTransition.Restoring)
                {
                    // A work candidate can be reconsidered while saved apparel
                    // is still being restored. Never let that stale candidate
                    // fall through to the normal missing-work-gear path or the
                    // pawn will alternate forever between the two outfits.
                    int restorationTick = Find.TickManager?.TicksGame ?? 0;
                    if (IsRecoveryWaitJob(newJob))
                        return;

                    if (state.LastRestorationAttemptTick >= 0 &&
                        restorationTick - state.LastRestorationAttemptTick < 600)
                    {
                        // A failed Wear causes RimWorld to start an error-recovery
                        // job immediately. Replanning here used to replace that
                        // recovery with the same Wear hundreds of times in one
                        // tick. Drop stale continuations and yield until the
                        // normal restoration retry window has elapsed.
                        __instance.ClearQueuedJobs(false);
                        ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                        return;
                    }

                    List<Job> pendingRestorationJobs = RestorationPlanner.BuildJobs(
                        pawn, state, activeRule, out bool hasUnavailableSavedApparel);
                    if (pendingRestorationJobs.Count > 0)
                    {
                        state.LastRestorationAttemptTick = restorationTick;
                        state.UnavailableRestorationAttempts = hasUnavailableSavedApparel
                            ? state.UnavailableRestorationAttempts + 1
                            : 0;
                        QueueRestorationJobs(
                            __instance, ref newJob, ref jobGiver, ref tag, pendingRestorationJobs);
                        return;
                    }

                    if (hasUnavailableSavedApparel)
                    {
                        state.LastRestorationAttemptTick = restorationTick;
                        state.UnavailableRestorationAttempts++;
                        __instance.ClearQueuedJobs(false);
                        ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                        return;
                    }

                    __instance.ClearQueuedJobs(false);
                    component.EndIntervention(pawn);
                    ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                    return;
                }

                bool targetsActiveWorkArea = RuleEvaluator.MatchesRule(pawn, newJob, activeRule);
                // Wait/Goto and similar connective jobs can inherit a cell inside
                // the work area after the real task finishes. They still need to
                // be permitted while a pawn is moving through the transition,
                // but they are not fresh work and must not reset or indefinitely
                // hold open the task buffer.
                bool startsMeaningfulWorkInArea = targetsActiveWorkArea &&
                    IsBufferableJob(newJob) && newJob.workGiverDef != null;
                bool matchesActiveRule = startsMeaningfulWorkInArea ||
                    PausedAreaWorkFilter.MatchesPermittedHaulingRule(pawn, newJob, activeRule) ||
                    PausedAreaWorkFilter.MatchesProtectedTransitRule(pawn, newJob, activeRule);
                // Only actual work targeting the configured area starts a fresh
                // work session. A connective route that merely crosses the area
                // still requires PPE, but must not erase already-used buffer
                // tasks or a pawn can retain work gear indefinitely.
                if (startsMeaningfulWorkInArea)
                {
                    if (Prefs.DevMode && state.BufferedTasksCompleted > 0)
                        Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: task buffer reset by {newJob.def.defName} in '{activeRule?.Name}'.");
                    state.BufferedTasksCompleted = 0;
                    state.LastBufferedJobLoadId = -1;
                }
                bool shouldLeaveRule = state.RecallRequested || !matchesActiveRule;
                if (shouldLeaveRule && state.Transition == ApparelTransition.Preparing &&
                    !state.RecallRequested)
                    return;

                if (shouldLeaveRule && !state.RecallRequested &&
                    state.Transition == ApparelTransition.Active &&
                    activeRule != null && activeRule.Enabled && !activeRule.WorkAreaPaused &&
                    activeRule.ReturnTaskBuffer > state.BufferedTasksCompleted &&
                    !RequiresImmediateRestoration(newJob) &&
                    (newJob.workGiverDef == null ||
                     RuleEvaluator.MatchingRule(pawn, newJob) == null))
                {
                    // Movement and brief wait jobs are connective AI steps, not
                    // meaningful tasks. Let them pass without consuming the
                    // buffer or causing an outfit swap before the real job starts.
                    if (IsBufferableJob(newJob) &&
                        newJob.loadID != state.LastBufferedJobLoadId)
                    {
                        state.BufferedTasksCompleted++;
                        state.LastBufferedJobLoadId = newJob.loadID;
                        if (Prefs.DevMode)
                            Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: task buffer {state.BufferedTasksCompleted}/{activeRule.ReturnTaskBuffer} used by {newJob.def.defName}.");
                    }
                    return;
                }

                if (shouldLeaveRule)
                {
                    if (activeRule?.ChangingArea != null &&
                        !PawnInsideArea(pawn, activeRule.ChangingArea) &&
                        TryFindChangingCell(pawn, activeRule.ChangingArea, out IntVec3 changingCell))
                    {
                        int returnTick = Find.TickManager?.TicksGame ?? 0;
                        if (state.LastChangingAreaReturnAttemptTick >= 0 &&
                            returnTick - state.LastChangingAreaReturnAttemptTick < 30)
                        {
                            // A failed or instantly completed Goto can cause the
                            // interrupted candidate to be reconsidered repeatedly
                            // in one tick. Yield briefly instead of recreating the
                            // same locker-room return until RimWorld's safety cap.
                            __instance.ClearQueuedJobs(false);
                            ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                            return;
                        }

                        state.Transition = ApparelTransition.ReturningToChangingArea;
                        state.LastChangingAreaReturnAttemptTick = returnTick;

                        // Recall invalidates the job chosen before the request.
                        // Do not preserve it behind the Goto: if the Goto ends
                        // immediately, that stale job can restart and recursively
                        // create hundreds of identical return jobs in one tick.
                        __instance.ClearQueuedJobs(false);
                        newJob = JobMaker.MakeJob(JobDefOf.Goto, changingCell);
                        jobGiver = null;
                        tag = null;
                        return;
                    }

                    int currentTick = Find.TickManager?.TicksGame ?? 0;
                    if (state.Transition == ApparelTransition.Restoring &&
                        state.LastRestorationAttemptTick >= 0 &&
                        currentTick - state.LastRestorationAttemptTick < 600)
                    {
                        // A restoration job can fail transiently because an item
                        // is reserved, inside storage, or its path is changing.
                        // Do not convert every newly selected job into Wait while
                        // cooling down; let the pawn perform safe unrelated work
                        // and retry restoration after ten in-game seconds.
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
                        ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                        return;
                    }

                    __instance.ClearQueuedJobs(false);
                    component.EndIntervention(pawn);

                    // The candidate job was selected before recall/restoration.
                    // Recheck the paused area after clearing the apparel state;
                    // otherwise that stale job can start in the same StartJob
                    // call and bypass the normal work-giver pause filters.
                    if (PausedAreaWorkFilter.ShouldRejectPausedAreaJob(pawn, newJob))
                    {
                        ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                        return;
                    }
                }
            }

            if (newJob.def == JobDefOf.HaulToCell &&
                AutomaticApparelClassifier.Matches(newJob.targetA.Thing))
            {
                return;
            }

            var rule = RuleEvaluator.MatchingRule(pawn, newJob) ??
                PausedAreaWorkFilter.MatchingPermittedHaulingRule(pawn, newJob) ??
                PausedAreaWorkFilter.MatchingProtectedTransitRule(pawn, newJob);
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

        private static bool IsFriendlyGuest(Pawn pawn)
        {
            return pawn?.guest != null &&
                   !pawn.guest.IsPrisoner &&
                   pawn.Faction != null &&
                   pawn.Faction != Faction.OfPlayer &&
                   !pawn.Faction.HostileTo(Faction.OfPlayer);
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

        private static bool IsBufferableJob(Job job)
        {
            if (job?.def == null)
                return false;

            string defName = job.def.defName ?? string.Empty;
            return !defName.StartsWith("Wait", StringComparison.OrdinalIgnoreCase) &&
                   !defName.StartsWith("Goto", StringComparison.OrdinalIgnoreCase) &&
                   !string.Equals(defName, "TakeInventory", StringComparison.OrdinalIgnoreCase) &&
                   job.def != JobDefOf.Wait &&
                   job.def != JobDefOf.Goto &&
                   job.def != JobDefOf.Wear &&
                   job.def != JobDefOf.RemoveApparel;
        }

        private static bool IsRecoveryWaitJob(Job job)
        {
            string defName = job?.def?.defName ?? string.Empty;
            return job?.def == JobDefOf.Wait ||
                   job?.def == JobDefOf.Wait_Wander ||
                   defName.StartsWith("Wait", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresImmediateRestoration(Job job)
        {
            if (job?.def == null)
                return false;

            // Sleeping is a long-lived state rather than a short task between
            // work-area jobs. Never spend a task-buffer slot on it: restore the
            // pawn's saved clothing before they settle into bed.
            string defName = job.def.defName ?? string.Empty;
            return job.def == JobDefOf.LayDown ||
                   string.Equals(defName, "LayDown", StringComparison.OrdinalIgnoreCase) ||
                   defName.IndexOf("GotoBed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ReplaceWithBriefWait(
            Pawn pawn,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag)
        {
            // Some StartJob callers immediately inspect targetA while deciding
            // whether to append an opportunistic haul. A targetless replacement
            // can send null into Fogged(Thing); targeting the pawn makes this a
            // complete, harmless wait job for both vanilla and modded callers.
            Job waitJob = pawn != null
                ? JobMaker.MakeJob(JobDefOf.Wait, pawn)
                : JobMaker.MakeJob(JobDefOf.Wait);
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

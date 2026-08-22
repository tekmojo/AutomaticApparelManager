using System.Collections.Generic;
using System.Linq;
using System;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Detection;
using AutomaticOutfitManager.Rules;
using AutomaticOutfitManager.State;
using AutomaticOutfitManager.Storage;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class PawnJobTracker_StartJob_Patch
    {
        private static readonly Dictionary<int, int> LastUnavailableNestedGearWarningTick =
            new Dictionary<int, int>();

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

            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            PawnApparelState state = component?.StateFor(pawn);

            // A work candidate can be selected just before another pawn claims
            // the same target for an outfit transition. Recheck at the common
            // job boundary so that candidate cannot start a second transition
            // in the small window between scanner and StartJob.
            if (ManagedWorkClaimRegistry.IsClaimedByOther(pawn, newJob))
            {
                __instance.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 60, ref newJob, ref jobGiver, ref tag);
                return;
            }

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

            ApparelRule deniedWorkRule =
                PausedAreaWorkFilter.DeniedOrdinaryWorkRule(pawn, newJob);
            if (deniedWorkRule != null)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                int pawnId = pawn.thingIDNumber;
                if (!LastUnavailableNestedGearWarningTick.TryGetValue(pawnId, out int lastTick) ||
                    tick - lastTick >= 600)
                {
                    LastUnavailableNestedGearWarningTick[pawnId] = tick;
                    string category = PawnAccessClassifier.IsHostedGuest(pawn) ? "guest work" : "work";
                    Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: blocked from '{deniedWorkRule.Name}'; {category} is disabled.");
                }
                if (state != null && PawnAccessClassifier.IsHostedGuest(pawn))
                    component.RequestRecall(state);
                __instance.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 180, ref newJob, ref jobGiver, ref tag);
                return;
            }

            ApparelRule deniedHaulingRule =
                PausedAreaWorkFilter.DeniedHaulingRule(pawn, newJob);
            if (deniedHaulingRule != null)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                int pawnId = pawn.thingIDNumber;
                if (!LastUnavailableNestedGearWarningTick.TryGetValue(pawnId, out int lastTick) ||
                    tick - lastTick >= 600)
                {
                    LastUnavailableNestedGearWarningTick[pawnId] = tick;
                    string category = PawnAccessClassifier.IsHostedGuest(pawn)
                        ? "guest hauling"
                        : "hauling";
                    Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: blocked from '{deniedHaulingRule.Name}'; {category} is disabled.");
                }
                if (state != null && PawnAccessClassifier.IsHostedGuest(pawn))
                    component.RequestRecall(state);
                __instance.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 180, ref newJob, ref jobGiver, ref tag);
                return;
            }

            // Keep guests and other non-colony pawns from reserving, hauling,
            // repairing, processing, or wearing managed apparel. Checking the
            // common job boundary makes this work for native and modded jobs,
            // including bills that place their ingredient in a target queue.
            if (pawn.Faction != Faction.OfPlayer && JobTargetsManagedApparel(newJob) &&
                !IsAssignedTransitionApparelJob(state, newJob))
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
                !ManagedApparelClassifier.Matches(wearTarget.def) &&
                component?.IsSavedForOtherPawn(wearTarget, pawn) == true)
            {
                ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel managedWearTarget &&
                ManagedApparelClassifier.Matches(managedWearTarget.def) &&
                !newJob.playerForced &&
                !IsAssignedTransitionApparelJob(state, newJob))
            {
                ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel assignedWearTarget &&
                component?.IsManagedApparelAssignedToOtherPawn(assignedWearTarget, pawn) == true)
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

            if (state?.RecallRequested == true &&
                state.Transition == ApparelTransition.Preparing &&
                newJob.def == JobDefOf.Wear &&
                newJob.targetA.Thing is Apparel queuedAutomaticOutfitManager &&
                state.ManagedApparel?.Contains(queuedAutomaticOutfitManager) == true)
            {
                // The current assigned apparel step was allowed to finish so
                // RimWorld could leave its layers in a consistent state. Drop
                // the rest of the preparation queue now; the brief trigger job
                // will enter the ordinary recall/restoration path on the next
                // selection without ever starting the intercepted work.
                __instance.ClearQueuedJobs(false);
                state.PendingWorkJob = null;
                ManagedWorkClaimRegistry.ReleaseAll(pawn);
                ReplaceWithBriefWait(pawn, ref newJob, ref jobGiver, ref tag);
                return;
            }

            if (newJob.def == JobDefOf.Wear || newJob.def == JobDefOf.RemoveApparel)
                return;

            // Apparel jobs temporarily displace the work that requested them.
            // Resume the exact intercepted job rather than hoping the think tree
            // can reconstruct a bill, construction, or hauling job from only
            // its target. This also avoids clearing the queued continuation in
            // a sequence of handoff Wait jobs.
            if (state?.Transition == ApparelTransition.Preparing &&
                HasCompletedPreparation(pawn, component, state) &&
                state.PendingWorkJob != null &&
                !SameJob(newJob, state.PendingWorkJob))
            {
                if (state.RecallRequested || RequiresImmediateRestoration(newJob) ||
                    !PendingWorkJobIsViable(pawn, state.PendingWorkJob) ||
                    !ManagedWorkClaimRegistry.TryClaim(pawn, state.PendingWorkJob))
                {
                    if (Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: pending work continuation was cancelled; returning to normal transition logic.");
                    state.PendingWorkJob = null;
                    ManagedWorkClaimRegistry.ReleaseAll(pawn);
                }
                else
                {
                    Job resumedJob = state.PendingWorkJob;
                    __instance.ClearQueuedJobs(false);
                    newJob = resumedJob;
                    jobGiver = null;
                    tag = null;
                    if (Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: resuming exact prepared job {newJob.def.defName} for '{ManagedWorkClaimRegistry.DescribeActiveClaim(pawn)}'.");
                }
            }

            // Work in overlapping areas must satisfy the combined equipment
            // requirements before it begins. Previously the active outer rule
            // accepted the job and the path-cell safety check discovered the
            // missing nested gear at the doorway, causing an immediate
            // stop/reselect loop.
            List<ApparelRule> matchingWorkRules = newJob.workGiverDef != null
                ? RuleEvaluator.MatchingRules(pawn, newJob)
                : new List<ApparelRule>();
            bool canPrepareForMatchingWork = state == null ||
                state.Transition == ApparelTransition.Preparing ||
                state.Transition == ApparelTransition.Active;
            if (canPrepareForMatchingWork && state != null && newJob.workGiverDef != null &&
                (matchingWorkRules.Count > 0 ||
                 state.Transition != ApparelTransition.Preparing))
            {
                state.CurrentRuleIds = matchingWorkRules
                    .Where(rule => rule != null)
                    .Select(rule => rule.Id)
                    .Distinct()
                    .ToList();
            }
            if (canPrepareForMatchingWork && state != null && matchingWorkRules.Count > 0)
            {
                // Do not create a nested buffer merely because a candidate was
                // intercepted. Contested hauling/construction candidates can
                // disappear while the pawn changes. Record entry only when the
                // combined outfit is complete and the prepared work can start.
                if (HasCompletedPreparation(pawn, component, state))
                    TrackNestedRuleEntries(state, matchingWorkRules);
            }
            // A nested buffer remains active after its work area stops matching.
            // Give it the same semantics as the outer buffer: the next meaningful
            // jobs consume its slots wherever RimWorld sends the pawn. Restricting
            // this call to matching outer-area work made the nested session vanish
            // without ever recording work outside that area.
            if (canPrepareForMatchingWork && state?.NestedRuleBuffers?.Count > 0 &&
                HandleNestedRuleBuffers(
                    __instance, pawn, component, state, matchingWorkRules,
                    ref newJob, ref jobGiver, ref tag))
            {
                return;
            }
            if (canPrepareForMatchingWork && matchingWorkRules.Count > 0 &&
                TryPrepareForMatchingRules(
                    __instance, pawn, component, matchingWorkRules,
                    ref newJob, ref jobGiver, ref tag))
            {
                return;
            }
            if (canPrepareForMatchingWork && matchingWorkRules.Count > 0)
            {
                ManagedWorkClaimRegistry.Release(pawn, newJob);
                if (state != null && SameJob(newJob, state.PendingWorkJob))
                    state.PendingWorkJob = null;
            }

            if (state != null)
            {
                // The work target that caused preparation can disappear while
                // the pawn is wearing several apparel items (another pawn may
                // finish it, reserve it, or consume its inputs). Preserve the
                // prepared rule set above and promote the session once every
                // required item is worn. Remaining in Preparing made unrelated
                // thinker jobs bypass both the task buffer and idle recovery,
                // leaving fully equipped pawns standing or returning gear only
                // after another matching job happened to be selected.
                if (state.Transition == ApparelTransition.Preparing &&
                    HasCompletedPreparation(pawn, component, state))
                {
                    state.Transition = ApparelTransition.Active;
                    state.ActiveIdleTicks = 0;
                    if (Prefs.DevMode)
                    {
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: preparation complete; equipped rule set is active.");
                    }
                }

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
                    (state.ManagedApparel?.Contains(returnItem) ?? false))
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

                    RestorationPlanner.TryMakeHeldOriginalsAccessible(pawn, state);
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
                bool holdsPendingNestedBuffer =
                    state.NestedRuleBuffers?.Any(progress =>
                        progress != null && !progress.Finished) == true &&
                    !state.RecallRequested &&
                    newJob.workGiverDef == null &&
                    !RequiresImmediateRestoration(newJob) &&
                    (JobTargetsArea(newJob, activeRule?.Area) ||
                     (IsRecoveryWaitJob(newJob) &&
                      PawnInsideArea(pawn, activeRule?.Area)));

                // The thinker commonly inserts a brief Wait/Goto between the
                // completed nested job and the next meaningful task. Keep the
                // nested outfit through that connective step so the configured
                // buffer can be consumed. The game component still applies its
                // bounded idle timeout; a buffer permits follow-up work but must
                // never make a pawn stand indefinitely waiting for work.
                if (holdsPendingNestedBuffer)
                    return;
                // Only actual work targeting the configured area starts a fresh
                // work session. A connective route that merely crosses the area
                // still requires PPE, but must not erase already-used buffer
                // tasks or a pawn can retain work gear indefinitely.
                if (startsMeaningfulWorkInArea)
                {
                    if (Prefs.DevMode && state.BufferedTasksCompleted > 0)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: task buffer reset by {newJob.def.defName} in '{activeRule?.Name}'.");
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
                            Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: task buffer {state.BufferedTasksCompleted}/{activeRule.ReturnTaskBuffer} used by {newJob.def.defName}.");
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

                    RestorationPlanner.TryMakeHeldOriginalsAccessible(pawn, state);
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
                            Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: restoring apparel with {restorationJobs.Count} job(s) before {__instance.curJob?.def?.defName ?? "next job"}.");
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
                ManagedApparelClassifier.Matches(newJob.targetA.Thing))
            {
                return;
            }

            List<ApparelRule> applicableRules = RuleEvaluator.MatchingRules(pawn, newJob);
            ApparelRule haulingRule =
                PausedAreaWorkFilter.MatchingPermittedHaulingRule(pawn, newJob);
            if (haulingRule != null)
                applicableRules.Add(haulingRule);
            applicableRules.AddRange(
                PausedAreaWorkFilter.MatchingProtectedTransitRules(pawn, newJob));
            applicableRules = applicableRules
                .Where(candidate => candidate != null)
                .GroupBy(candidate => candidate.Id)
                .Select(group => group.First())
                .ToList();
            if (applicableRules.Count == 0)
                return;

            ApparelRule unwearableRule = applicableRules.FirstOrDefault(candidate =>
                !RuleEvaluator.RuleCanApplyToPawn(pawn, candidate));
            ApparelConflict transitConflict = ApparelCompatibility.FindConflict(
                applicableRules, pawn.RaceProps?.body);
            if (unwearableRule != null || transitConflict != null)
            {
                foreach (ApparelRule blockedRule in applicableRules)
                    UnavailableWorkRegistry.Block(pawn, blockedRule);
                string reason = unwearableRule != null
                    ? $"required gear for '{unwearableRule.Name}' cannot be worn"
                    : $"required gear is incompatible: {transitConflict.Label}";
                Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: delaying {newJob.def.defName}; {reason}.");
                __instance.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 300, ref newJob, ref jobGiver, ref tag);
                return;
            }

            var requiredByDef = new Dictionary<ThingDef, ApparelRule>();
            foreach (ApparelRule applicableRule in applicableRules)
            {
                foreach (ThingDef def in applicableRule.RequiredApparel ??
                         Enumerable.Empty<ThingDef>())
                {
                    if (def != null && !requiredByDef.ContainsKey(def))
                        requiredByDef.Add(def, applicableRule);
                }
            }
            List<ThingDef> missing = requiredByDef.Keys
                .Where(def => !pawn.apparel.WornApparel.Any(item => item?.def == def))
                .ToList();
            if (missing.Count == 0)
            {
                PawnApparelState activeState = component?.StateFor(pawn);
                if (activeState != null &&
                    applicableRules.Any(candidate => candidate.Id == activeState.ActiveRuleId) &&
                    activeState.Transition == ApparelTransition.Preparing &&
                    HasCompletedPreparation(pawn, component, activeState))
                {
                    activeState.Transition = ApparelTransition.Active;
                    if (Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: preparation complete; equipped rule set is active.");
                }
                return;
            }

            var wearJobs = new List<Job>();
            foreach (ThingDef def in missing)
            {
                ApparelRule sourceRule = requiredByDef[def];
                Apparel apparel = ApparelFinder.FindBest(pawn, def, sourceRule.ChangingArea);
                if (apparel == null)
                {
                    UnavailableWorkRegistry.Block(pawn, sourceRule);
                    Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: delaying {newJob.def.defName}; no reachable {def.LabelCap} is available for '{sourceRule.Name}'.");
                    __instance.ClearQueuedJobs(false);
                    ReplaceWithWait(pawn, 300, ref newJob, ref jobGiver, ref tag);
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

            ApparelRule primaryRule = component?.StateFor(pawn) is PawnApparelState existingState
                ? component.RuleById(existingState.ActiveRuleId) ?? applicableRules[0]
                : applicableRules[0];
            PawnApparelState preparedState = component?.BeginIntervention(
                pawn, primaryRule, wearJobs.Select(job => job.targetA.Thing as Apparel));
            if (preparedState != null)
            {
                preparedState.CurrentRuleIds = applicableRules
                    .Select(candidate => candidate.Id)
                    .Distinct()
                    .ToList();
            }

            if (Prefs.DevMode)
            {
                string ruleNames = string.Join(", ",
                    applicableRules.Select(candidate => $"'{candidate.Name}'"));
                Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: intercepted {newJob.def.defName}; preparing {wearJobs.Count} apparel item(s) for {ruleNames}.");
            }

            QueueBeforeCurrent(__instance, ref newJob, ref jobGiver, ref tag, wearJobs);
        }

        private static bool PawnInsideArea(Pawn pawn, Area area) =>
            pawn?.Map != null && area?.Map == pawn.Map &&
            pawn.Position.IsValid && pawn.Position.InBounds(pawn.Map) && area[pawn.Position];

        private static bool HasCompletedPreparation(
            Pawn pawn,
            AutomaticOutfitManagerGameComponent component,
            PawnApparelState state)
        {
            if (pawn?.apparel == null || component == null || state == null)
                return false;

            List<ApparelRule> preparedRules = (state.CurrentRuleIds ?? new List<string>())
                .Select(component.RuleById)
                .Where(rule => rule?.Enabled == true)
                .ToList();
            if (preparedRules.Count == 0)
            {
                ApparelRule activeRule = component.RuleById(state.ActiveRuleId);
                if (activeRule?.Enabled == true)
                    preparedRules.Add(activeRule);
            }

            return preparedRules.Count > 0 &&
                   preparedRules.All(rule => !RuleEvaluator.HasMissingRequiredApparel(pawn, rule));
        }

        private static void TrackNestedRuleEntries(
            PawnApparelState state, List<ApparelRule> matchingRules)
        {
            foreach (ApparelRule rule in matchingRules.Where(rule =>
                         rule != null && rule.Id != state.ActiveRuleId))
            {
                NestedRuleBufferState progress = state.NestedRuleBuffers
                    .FirstOrDefault(item => item.RuleId == rule.Id);
                if (progress == null)
                {
                    state.NestedRuleBuffers.Add(new NestedRuleBufferState { RuleId = rule.Id });
                    state.LastNestedBufferStatus =
                        $"{rule.Name}: entered nested work; 0 of {rule.ReturnTaskBuffer} outer tasks used.";
                    if (Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {state.Pawn?.LabelShortCap}: nested task buffer started for '{rule.Name}' (0/{rule.ReturnTaskBuffer}).");
                }
                else
                {
                    progress.Completed = 0;
                    progress.Finished = false;
                    progress.LastJobLoadId = -1;
                    progress.LastJobLabel = null;
                    state.LastNestedBufferStatus =
                        $"{rule.Name}: nested work restarted; 0 of {rule.ReturnTaskBuffer} outer tasks used.";
                }
            }
        }

        private static bool HandleNestedRuleBuffers(
            Pawn_JobTracker tracker,
            Pawn pawn,
            AutomaticOutfitManagerGameComponent component,
            PawnApparelState state,
            List<ApparelRule> matchingRules,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag)
        {
            // Mirror the outer task-buffer contract: connective movement and
            // waiting do not count, while the next meaningful jobs do. A nested
            // buffer must not depend on continuing to match the outer work area;
            // otherwise a pawn sent elsewhere loses the nested session before
            // any configured follow-up task can be observed or completed.
            if (!IsBufferableJob(newJob) || RequiresImmediateRestoration(newJob))
                return false;

            var matchingIds = new HashSet<string>(matchingRules.Select(rule => rule.Id));
            foreach (NestedRuleBufferState progress in state.NestedRuleBuffers.ToList())
            {
                if (matchingIds.Contains(progress.RuleId))
                    continue;

                // Keep completed nested sessions for worker-list and tooltip
                // visibility until the pawn's saved outfit is fully restored.
                // Re-entering the nested area resets this flag above.
                if (progress.Finished)
                    continue;

                ApparelRule nestedRule = component.RuleById(progress.RuleId);
                if (nestedRule == null)
                {
                    state.NestedRuleBuffers.Remove(progress);
                    continue;
                }

                if (newJob.loadID == progress.LastJobLoadId)
                    continue;

                if (progress.Completed < nestedRule.ReturnTaskBuffer)
                {
                    progress.Completed++;
                    progress.LastJobLoadId = newJob.loadID;
                    progress.LastJobLabel = newJob.GetReport(pawn);
                    state.LastNestedBufferStatus =
                        $"{nestedRule.Name}: {progress.Completed} of {nestedRule.ReturnTaskBuffer} outer tasks used" +
                        (string.IsNullOrEmpty(progress.LastJobLabel)
                            ? "."
                            : $"; last: {progress.LastJobLabel}.");
                    if (Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: nested task buffer {progress.Completed}/{nestedRule.ReturnTaskBuffer} used by {newJob.def.defName} after leaving '{nestedRule.Name}'.");
                    continue;
                }

                // The outer outfit remains part of the session even when the
                // buffered follow-up job is outside every managed area. Preserve
                // its shared requirements while removing only nested-only gear.
                ApparelRule activeRule = component.RuleById(state.ActiveRuleId);
                IEnumerable<ApparelRule> retainedRules = matchingRules
                    .Concat(activeRule == null
                        ? Enumerable.Empty<ApparelRule>()
                        : new[] { activeRule })
                    .Where(rule => rule != null && rule.Id != nestedRule.Id)
                    .Distinct();
                var retainedDefs = new HashSet<ThingDef>(retainedRules
                    .SelectMany(rule => rule.RequiredApparel ?? new List<ThingDef>())
                    .Where(def => def != null));
                var nestedOnlyDefs = new HashSet<ThingDef>(
                    (nestedRule.RequiredApparel ?? new List<ThingDef>())
                    .Where(def => def != null && !retainedDefs.Contains(def)));
                List<Job> removalJobs = pawn.apparel.WornApparel
                    .Where(item => item != null && nestedOnlyDefs.Contains(item.def) &&
                                   state.ManagedApparel.Contains(item))
                    .Select(item => JobMaker.MakeJob(JobDefOf.RemoveApparel, item))
                    .ToList();
                state.LastNestedBufferStatus =
                    $"{nestedRule.Name}: buffer complete; removing nested-only gear before {newJob.GetReport(pawn)}.";
                progress.Completed = nestedRule.ReturnTaskBuffer;
                progress.Finished = true;
                progress.LastJobLoadId = newJob.loadID;
                progress.LastJobLabel = newJob.GetReport(pawn);

                if (removalJobs.Count == 0)
                    continue;

                if (nestedRule.ChangingArea != null &&
                    !PawnInsideArea(pawn, nestedRule.ChangingArea) &&
                    TryFindChangingCell(pawn, nestedRule.ChangingArea, out IntVec3 changingCell))
                {
                    removalJobs.Insert(0, JobMaker.MakeJob(JobDefOf.Goto, changingCell));
                }

                state.Transition = ApparelTransition.Preparing;
                QueueBeforeCurrent(tracker, ref newJob, ref jobGiver, ref tag, removalJobs);
                if (Prefs.DevMode)
                    Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: nested task buffer complete for '{nestedRule.Name}'; removing {removalJobs.Count} nested transition job(s).");
                return true;
            }

            return false;
        }

        private static bool TryPrepareForMatchingRules(
            Pawn_JobTracker tracker,
            Pawn pawn,
            AutomaticOutfitManagerGameComponent component,
            List<ApparelRule> rules,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag)
        {
            ApparelRule unwearableRule = rules.FirstOrDefault(rule =>
                !RuleEvaluator.RuleCanApplyToPawn(pawn, rule));
            if (unwearableRule != null)
            {
                UnavailableWorkRegistry.Block(pawn, unwearableRule);
                int tick = Find.TickManager?.TicksGame ?? 0;
                int pawnId = pawn.thingIDNumber;
                if (!LastUnavailableNestedGearWarningTick.TryGetValue(pawnId, out int lastTick) ||
                    tick - lastTick >= 600)
                {
                    LastUnavailableNestedGearWarningTick[pawnId] = tick;
                    Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: blocked from '{unwearableRule.Name}'; its required gear cannot be worn by this pawn.");
                }
                tracker.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 300, ref newJob, ref jobGiver, ref tag);
                return true;
            }

            ApparelConflict conflict = ApparelCompatibility.FindConflict(
                rules, pawn.RaceProps?.body);
            if (conflict != null)
            {
                foreach (ApparelRule rule in rules)
                    UnavailableWorkRegistry.Block(pawn, rule);
                int tick = Find.TickManager?.TicksGame ?? 0;
                int pawnId = pawn.thingIDNumber;
                if (!LastUnavailableNestedGearWarningTick.TryGetValue(pawnId, out int lastTick) ||
                    tick - lastTick >= 600)
                {
                    LastUnavailableNestedGearWarningTick[pawnId] = tick;
                    Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: delaying {newJob.def.defName}; incompatible required gear: {conflict.Label}.");
                }

                tracker.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 300, ref newJob, ref jobGiver, ref tag);
                return true;
            }

            var requiredByDef = new Dictionary<ThingDef, ApparelRule>();
            foreach (ApparelRule rule in rules)
            {
                foreach (ThingDef def in rule.RequiredApparel ?? Enumerable.Empty<ThingDef>())
                {
                    if (def != null && !requiredByDef.ContainsKey(def))
                        requiredByDef.Add(def, rule);
                }
            }

            var missing = requiredByDef.Keys
                .Where(def => !pawn.apparel.WornApparel.Any(item => item?.def == def))
                .ToList();
            if (missing.Count == 0)
                return false;

            var wearJobs = new List<Job>();
            foreach (ThingDef def in missing)
            {
                ApparelRule sourceRule = requiredByDef[def];
                Apparel apparel = ApparelFinder.FindBest(pawn, def, sourceRule.ChangingArea);
                if (apparel == null)
                {
                    UnavailableWorkRegistry.Block(pawn, sourceRule);
                    int tick = Find.TickManager?.TicksGame ?? 0;
                    int pawnId = pawn.thingIDNumber;
                    if (!LastUnavailableNestedGearWarningTick.TryGetValue(pawnId, out int lastTick) ||
                        tick - lastTick >= 600)
                    {
                        LastUnavailableNestedGearWarningTick[pawnId] = tick;
                        Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: delaying {newJob.def.defName}; no reachable {def.LabelCap} is available for '{sourceRule.Name}'.");
                    }

                    // Discard the stale work candidate and give the normal think
                    // tree time to select other work. It will reconsider after
                    // gear is produced, hauled, or becomes unreserved.
                    tracker.ClearQueuedJobs(false);
                    ReplaceWithWait(pawn, 300, ref newJob, ref jobGiver, ref tag);
                    return true;
                }

                Job wearJob = JobMaker.MakeJob(JobDefOf.Wear, apparel);
                wearJob.playerForced = true;
                wearJobs.Add(wearJob);
            }

            if (!ManagedWorkClaimRegistry.TryClaim(pawn, newJob))
            {
                tracker.ClearQueuedJobs(false);
                ReplaceWithWait(pawn, 60, ref newJob, ref jobGiver, ref tag);
                return true;
            }

            ApparelRule primaryRule = component?.StateFor(pawn) is PawnApparelState existing
                ? component.RuleById(existing.ActiveRuleId) ?? rules[0]
                : rules[0];
            UnavailableWorkRegistry.Clear(pawn, rules);
            PawnApparelState interventionState = component?.BeginIntervention(
                pawn, primaryRule, wearJobs.Select(job => job.targetA.Thing as Apparel));
            if (interventionState != null)
            {
                interventionState.PendingWorkJob = newJob;
                interventionState.CurrentRuleIds = rules
                    .Where(rule => rule != null)
                    .Select(rule => rule.Id)
                    .Distinct()
                    .ToList();
            }

            if (Prefs.DevMode)
            {
                string ruleNames = string.Join(", ", rules.Select(rule => $"'{rule.Name}'"));
                Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: intercepted {newJob.def.defName}; preparing {wearJobs.Count} apparel item(s) for overlapping rules {ruleNames}.");
            }

            QueueBeforeCurrent(tracker, ref newJob, ref jobGiver, ref tag, wearJobs);
            return true;
        }

        private static bool SameJob(Job left, Job right) =>
            left != null && right != null &&
            (ReferenceEquals(left, right) || left.loadID == right.loadID);

        private static bool PendingWorkJobIsViable(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job?.def == null || job.workGiverDef == null)
                return false;

            LocalTargetInfo[] targets = { job.targetA, job.targetB, job.targetC };
            foreach (LocalTargetInfo target in targets)
            {
                if (target.IsValid && target.HasThing && target.Thing.Destroyed)
                    return false;
                if (target.IsValid && !target.HasThing &&
                    (!target.Cell.IsValid || !target.Cell.InBounds(pawn.Map)))
                    return false;
            }

            return RuleEvaluator.MatchingRules(pawn, job).Count > 0;
        }

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
                   AutomaticOutfitManagerGameComponent.Current?.IsManagedApparel(apparel) == true;
        }

        private static bool IsFriendlyGuest(Pawn pawn)
            => PawnAccessClassifier.IsHostedGuest(pawn);

        private static bool IsAllowedTransitionWear(PawnApparelState state, Apparel apparel)
        {
            if (state == null || apparel == null)
                return false;

            switch (state.Transition)
            {
                case ApparelTransition.Preparing:
                case ApparelTransition.Active:
                    return state.ManagedApparel?.Contains(apparel) == true;
                case ApparelTransition.Restoring:
                    return state.OriginalApparel?.Contains(apparel) == true;
                case ApparelTransition.ReturningToChangingArea:
                default:
                    return false;
            }
        }

        private static bool IsAssignedTransitionApparelJob(PawnApparelState state, Job job)
        {
            if (state == null || job?.targetA.Thing is not Apparel apparel)
                return false;

            if (job.def == JobDefOf.Wear)
                return IsAllowedTransitionWear(state, apparel);

            if (job.def == JobDefOf.RemoveApparel)
                return state.ManagedApparel?.Contains(apparel) == true;

            // A transition may hand its just-removed automatic item to a haul
            // job. Only the exact items recorded for this pawn are exempted.
            return (job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer) &&
                   state.ManagedApparel?.Contains(apparel) == true;
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
                   defName.StartsWith("Wait", StringComparison.OrdinalIgnoreCase) ||
                   defName.IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0;
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
            ReplaceWithWait(pawn, 30, ref newJob, ref jobGiver, ref tag);
        }

        private static void ReplaceWithWait(
            Pawn pawn,
            int expiryInterval,
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
            waitJob.expiryInterval = expiryInterval;
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

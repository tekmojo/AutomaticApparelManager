using System.Collections.Generic;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Detection;
using AutomaticOutfitManager.Rules;
using AutomaticOutfitManager.State;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Patches
{
    /// <summary>
    /// Rechecks the cell RimWorld is actually about to enter. A job's route can
    /// change after StartJob (doors, reservations, congestion, or a modded
    /// pathfinder), so the initial protected-transit prediction is not enough
    /// to guarantee that an unequipped pawn never crosses a managed work area.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    public static class PawnPathFollower_ProtectedArea_Patch
    {
        private static readonly Dictionary<int, int> LastBlockedLogTick =
            new Dictionary<int, int>();
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, IntVec3> NextCellField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, IntVec3>("nextCell");

        public static bool Prefix(Pawn_PathFollower __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (pawn?.Map == null || pawn.Drafted ||
                !PawnAccessClassifier.IsApparelEligibleHuman(pawn) ||
                pawn.jobs?.curJob == null)
            {
                return true;
            }

            Job currentJob = pawn.jobs.curJob;
            PawnApparelState state = AutomaticOutfitManagerGameComponent.Current?.StateFor(pawn);

            // A pause recall can legitimately route a pawn through the protected
            // work area to reach its configured changing area and exact saved
            // apparel. The StartJob patch has already assigned these transition
            // jobs, so allow only the recorded Goto/apparel operations rather
            // than broadly exempting every pawn with RecallRequested set.
            if (IsManagedRecallTransition(pawn, currentJob, state))
                return true;

            IntVec3 nextCell = NextCellField(__instance);
            if (!nextCell.IsValid || !nextCell.InBounds(pawn.Map))
                return true;

            ApparelRule rule = null;
            var rules = AutomaticOutfitManagerGameComponent.Current?.Rules;
            if (rules != null)
            {
                foreach (ApparelRule candidate in rules)
                {
                    if (candidate == null || !candidate.Enabled ||
                        candidate.Area?.Map != pawn.Map || !candidate.Area[nextCell])
                        continue;

                    bool blocked = candidate.WorkAreaPaused
                        ? !PausedAreaWorkFilter.JobMayEnterPausedRule(pawn, currentJob, candidate)
                        : RuleEvaluator.HasMissingRequiredApparel(pawn, candidate);
                    if (blocked)
                    {
                        rule = candidate;
                        break;
                    }
                }
            }
            if (rule == null)
                return true;

            if (Prefs.DevMode)
            {
                int tick = Find.TickManager?.TicksGame ?? 0;
                if (!LastBlockedLogTick.TryGetValue(pawn.thingIDNumber, out int lastTick) ||
                    tick - lastTick >= 600)
                {
                    LastBlockedLogTick[pawn.thingIDNumber] = tick;
                    string reason = rule.WorkAreaPaused
                        ? "while work is paused"
                        : "without its required work gear";
                    Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: stopped before entering '{rule.Name}' {reason}; reconsidering {currentJob.def.defName}.");
                }
            }

            // End the candidate before it enters the protected cell. RimWorld's
            // normal think tree will select it (or another useful job) again;
            // StartJob then queues the required apparel using the now-current
            // route. Avoid substituting Wait here, which can strand pawns at a
            // doorway when their original task remains the best available job.
            // Paused work must not retain a queued continuation. Otherwise
            // RimWorld can immediately restart the same blocked Goto and invoke
            // this path guard again indefinitely. Unpaused missing-gear jobs keep
            // their queue so the apparel intervention can resume the real task.
            if (rule.WorkAreaPaused)
                pawn.jobs.ClearQueuedJobs(false);

            // Do not synchronously select another job from inside the path-cell
            // callback. If the thinker returns the same candidate, recursive
            // EndCurrentJob calls can produce hundreds of retries in one tick.
            // Leaving selection to the next job-tracker tick gives StartJob a
            // clean opportunity to prepare every rule crossed by the new path.
            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, false, true);
            return false;
        }

        private static bool IsManagedRecallTransition(
            Pawn pawn, Job currentJob, PawnApparelState state)
        {
            if (pawn?.Map == null || currentJob?.def == null ||
                state?.RecallRequested != true)
            {
                return false;
            }

            if (state.Transition == ApparelTransition.ReturningToChangingArea &&
                currentJob.def == JobDefOf.Goto)
            {
                var activeRule = AutomaticOutfitManagerGameComponent.Current?
                    .RuleById(state.ActiveRuleId);
                return activeRule?.Enabled == true &&
                       activeRule.ChangingArea?.Map == pawn.Map &&
                       RuleEvaluator.JobTargetsArea(currentJob, activeRule.ChangingArea);
            }

            if (state.Transition != ApparelTransition.Restoring ||
                currentJob.targetA.Thing is not Apparel apparel)
            {
                return false;
            }

            if (currentJob.def == JobDefOf.Wear)
                return state.OriginalApparel?.Contains(apparel) == true;

            if (currentJob.def == JobDefOf.RemoveApparel)
                return state.ManagedApparel?.Contains(apparel) == true;

            return (currentJob.def == JobDefOf.HaulToCell ||
                    currentJob.def == JobDefOf.HaulToContainer) &&
                   state.ManagedApparel?.Contains(apparel) == true;
        }
    }
}

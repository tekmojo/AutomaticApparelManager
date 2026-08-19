using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.Rules;
using AutomaticApparel.State;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
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
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> PawnField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");
        private static readonly AccessTools.FieldRef<Pawn_PathFollower, IntVec3> NextCellField =
            AccessTools.FieldRefAccess<Pawn_PathFollower, IntVec3>("nextCell");

        public static bool Prefix(Pawn_PathFollower __instance)
        {
            Pawn pawn = PawnField(__instance);
            if (pawn?.Map == null || pawn.Drafted || pawn.Faction != Faction.OfPlayer ||
                pawn.RaceProps?.Humanlike != true || pawn.jobs?.curJob == null)
            {
                return true;
            }

            Job currentJob = pawn.jobs.curJob;
            if (currentJob.def == JobDefOf.Wear || currentJob.def == JobDefOf.RemoveApparel)
                return true;

            PawnApparelState state = AutomaticApparelGameComponent.Current?.StateFor(pawn);
            if (state?.Transition == ApparelTransition.Preparing)
                return true;

            // A pause recall can legitimately route a pawn through the protected
            // work area to reach its configured changing area. The StartJob patch
            // has already replaced the interrupted work with this exact Goto, so
            // allow only that narrowly identified transition rather than broadly
            // exempting every pawn with RecallRequested set.
            if (IsRecallGotoToChangingArea(pawn, currentJob, state))
                return true;

            IntVec3 nextCell = NextCellField(__instance);
            if (!nextCell.IsValid || !nextCell.InBounds(pawn.Map))
                return true;

            ApparelRule rule = null;
            var rules = AutomaticApparelGameComponent.Current?.Rules;
            if (rules != null)
            {
                foreach (ApparelRule candidate in rules)
                {
                    if (candidate == null || !candidate.Enabled ||
                        candidate.Area?.Map != pawn.Map || !candidate.Area[nextCell])
                        continue;

                    bool blocked = candidate.WorkAreaPaused
                        ? !PausedAreaWorkFilter.JobMayEnterPausedRule(pawn, currentJob, candidate)
                        : RuleEvaluator.RuleCanApplyToPawn(pawn, candidate) &&
                          RuleEvaluator.HasMissingRequiredApparel(pawn, candidate);
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
                string reason = rule.WorkAreaPaused
                    ? "while work is paused"
                    : "without its required work gear";
                Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: stopped before entering '{rule.Name}' {reason}; reconsidering {currentJob.def.defName}.");
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

            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true, true);
            return false;
        }

        private static bool IsRecallGotoToChangingArea(
            Pawn pawn, Job currentJob, PawnApparelState state)
        {
            if (pawn?.Map == null || currentJob?.def != JobDefOf.Goto ||
                state?.RecallRequested != true ||
                state.Transition != ApparelTransition.ReturningToChangingArea)
            {
                return false;
            }

            var activeRule = AutomaticApparelGameComponent.Current?
                .RuleById(state.ActiveRuleId);
            return activeRule?.Enabled == true &&
                   activeRule.ChangingArea?.Map == pawn.Map &&
                   RuleEvaluator.JobTargetsArea(currentJob, activeRule.ChangingArea);
        }
    }
}

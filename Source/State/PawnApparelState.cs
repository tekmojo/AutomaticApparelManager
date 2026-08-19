using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Rules;
using RimWorld;
using Verse;

namespace AutomaticApparel.State
{
    public enum ApparelTransition
    {
        Preparing,
        Active,
        ReturningToChangingArea,
        Restoring
    }

    public sealed class PawnApparelState : IExposable
    {
        public Pawn Pawn;
        public string ActiveRuleId;
        public List<Apparel> OriginalApparel = new List<Apparel>();
        public List<Apparel> AutomaticApparel = new List<Apparel>();
        public ApparelTransition Transition = ApparelTransition.Preparing;
        public int StartedTick;
        public int LastRestorationAttemptTick = -1;
        public int UnavailableRestorationAttempts;
        public bool RecallRequested;
        public bool RecallAllRequested;
        public bool RecallInterruptPending;
        public int LastRecallInterruptAttemptTick = -1;
        public int BufferedTasksCompleted;
        public int LastBufferedJobLoadId = -1;
        public int LastChangingAreaReturnAttemptTick = -1;
        public int ActiveIdleTicks;

        public static PawnApparelState Capture(Pawn pawn, ApparelRule rule)
        {
            return new PawnApparelState
            {
                Pawn = pawn,
                ActiveRuleId = rule?.Id,
                OriginalApparel = pawn?.apparel?.WornApparel
                    .Where(apparel => apparel != null)
                    .ToList() ?? new List<Apparel>(),
                AutomaticApparel = new List<Apparel>(),
                Transition = ApparelTransition.Preparing,
                StartedTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref Pawn, "pawn");
            Scribe_Values.Look(ref ActiveRuleId, "activeRuleId");
            Scribe_Collections.Look(ref OriginalApparel, "originalApparel", LookMode.Reference);
            Scribe_Collections.Look(ref AutomaticApparel, "automaticApparel", LookMode.Reference);
            Scribe_Values.Look(ref Transition, "transition", ApparelTransition.Preparing);
            Scribe_Values.Look(ref StartedTick, "startedTick");
            Scribe_Values.Look(ref LastRestorationAttemptTick, "lastRestorationAttemptTick", -1);
            Scribe_Values.Look(ref UnavailableRestorationAttempts, "unavailableRestorationAttempts");
            Scribe_Values.Look(ref RecallRequested, "recallRequested", false);
            Scribe_Values.Look(ref RecallAllRequested, "recallAllRequested", false);
            Scribe_Values.Look(ref RecallInterruptPending, "recallInterruptPending", false);
            Scribe_Values.Look(ref LastRecallInterruptAttemptTick, "lastRecallInterruptAttemptTick", -1);
            Scribe_Values.Look(ref BufferedTasksCompleted, "bufferedTasksCompleted", 0);
            Scribe_Values.Look(ref LastBufferedJobLoadId, "lastBufferedJobLoadId", -1);
            Scribe_Values.Look(ref LastChangingAreaReturnAttemptTick, "lastChangingAreaReturnAttemptTick", -1);
            Scribe_Values.Look(ref ActiveIdleTicks, "activeIdleTicks", 0);
            OriginalApparel ??= new List<Apparel>();
            AutomaticApparel ??= new List<Apparel>();
        }

        public void AddAutomaticApparel(IEnumerable<Apparel> apparel)
        {
            if (apparel == null)
                return;

            foreach (Apparel item in apparel.Where(item => item != null))
            {
                if (!AutomaticApparel.Contains(item))
                    AutomaticApparel.Add(item);
            }
        }
    }
}

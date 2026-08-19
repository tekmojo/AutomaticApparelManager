using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.Rules;
using AutomaticApparel.State;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutomaticApparel.UI
{
    public static class PawnAutomaticApparelStatus
    {
        private const float CacheSeconds = 0.5f;
        private static readonly Dictionary<Pawn, CachedStatus> StatusCache =
            new Dictionary<Pawn, CachedStatus>();

        private sealed class CachedStatus
        {
            public float CreatedAt;
            public ApparelTransition Transition;
            public string RuleId;
            public int OriginalCount;
            public int AutomaticCount;
            public int WornCount;
            public int BufferedTasksCompleted;
            public int ReturnTaskBuffer;
            public int CurrentJobLoadId;
            public bool RecallInterruptPending;
            public string Text;
        }

        public static string Build(Pawn pawn)
        {
            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            PawnApparelState state = component?.StateFor(pawn);
            if (state == null)
            {
                if (pawn != null)
                    StatusCache.Remove(pawn);
                return null;
            }

            ApparelRule rule = component.RuleById(state.ActiveRuleId);
            int originalCount = state.OriginalApparel?.Count ?? 0;
            int automaticCount = state.AutomaticApparel?.Count ?? 0;
            int wornCount = pawn.apparel?.WornApparelCount ?? 0;
            int returnTaskBuffer = rule?.ReturnTaskBuffer ?? 0;
            int currentJobLoadId = pawn.CurJob?.loadID ?? -1;
            if (StatusCache.TryGetValue(pawn, out CachedStatus cached) &&
                Time.realtimeSinceStartup - cached.CreatedAt < CacheSeconds &&
                cached.Transition == state.Transition &&
                cached.RuleId == state.ActiveRuleId &&
                cached.OriginalCount == originalCount &&
                cached.AutomaticCount == automaticCount &&
                cached.WornCount == wornCount &&
                cached.BufferedTasksCompleted == state.BufferedTasksCompleted &&
                cached.ReturnTaskBuffer == returnTaskBuffer &&
                cached.CurrentJobLoadId == currentJobLoadId &&
                cached.RecallInterruptPending == state.RecallInterruptPending)
            {
                return cached.Text;
            }

            string transition = state.RecallInterruptPending
                ? "Recall pending"
                : TransitionLabel(pawn, state, returnTaskBuffer);
            string text = $"Automatic Apparel: {transition}";
            if (rule != null)
                text += $"\nRule: {rule.Name}";

            text += $"\n{TaskBufferStatus(state.BufferedTasksCompleted, returnTaskBuffer)}";

            string detail = DetailFor(pawn, state, rule);
            if (!string.IsNullOrEmpty(detail))
                text += $"\n{detail}";

            StatusCache[pawn] = new CachedStatus
            {
                CreatedAt = Time.realtimeSinceStartup,
                Transition = state.Transition,
                RuleId = state.ActiveRuleId,
                OriginalCount = originalCount,
                AutomaticCount = automaticCount,
                WornCount = wornCount,
                BufferedTasksCompleted = state.BufferedTasksCompleted,
                ReturnTaskBuffer = returnTaskBuffer,
                CurrentJobLoadId = currentJobLoadId,
                RecallInterruptPending = state.RecallInterruptPending,
                Text = text
            };
            return text;
        }

        private static string TransitionLabel(
            Pawn pawn, PawnApparelState state, int returnTaskBuffer)
        {
            ApparelTransition transition = state.Transition;
            if (transition == ApparelTransition.Restoring)
            {
                if (pawn?.Drafted == true)
                    return "Restoration paused — drafted";

                Job currentJob = pawn?.CurJob;
                if (currentJob?.def == JobDefOf.LayDown)
                    return "Restoration paused — sleeping or resting";

                if (currentJob?.playerForced == true &&
                    currentJob.def != JobDefOf.Wear &&
                    currentJob.def != JobDefOf.RemoveApparel)
                {
                    return "Restoration paused — forced order";
                }
            }

            Job bufferedJob = pawn?.CurJob;
            if (transition == ApparelTransition.Active &&
                returnTaskBuffer > 0 &&
                state.BufferedTasksCompleted > 0 &&
                bufferedJob != null &&
                bufferedJob.loadID == state.LastBufferedJobLoadId)
            {
                int completed = System.Math.Min(
                    state.BufferedTasksCompleted, returnTaskBuffer);
                string activity = bufferedJob.GetReport(pawn);
                if (string.IsNullOrEmpty(activity))
                    activity = bufferedJob.def?.label ?? "Task";
                return $"Buffered task {completed} of {returnTaskBuffer}: {activity.CapitalizeFirst()}";
            }

            switch (transition)
            {
                case ApparelTransition.Preparing:
                    return "Outfitting work gear";
                case ApparelTransition.Active:
                    return "Work outfit equipped";
                case ApparelTransition.ReturningToChangingArea:
                    return "Returning to locker room";
                case ApparelTransition.Restoring:
                    return "Outfitting saved gear";
                default:
                    return transition.ToString();
            }
        }

        private static string DetailFor(Pawn pawn, PawnApparelState state, ApparelRule rule)
        {
            if (state.Transition == ApparelTransition.Preparing)
            {
                List<ThingDef> missing = RuleEvaluator.MissingRequiredApparel(pawn, rule);
                return missing.Count == 0
                    ? "Waiting for the work job to resume."
                    : $"Still needed: {string.Join(", ", missing.Select(def => def.LabelCap.ToString()))}";
            }

            if (state.Transition == ApparelTransition.ReturningToChangingArea)
                return rule?.ChangingArea == null ? null : $"Destination: {rule.ChangingArea.Label}";

            if (state.Transition != ApparelTransition.Restoring)
                return null;

            Apparel missingItem = state.OriginalApparel.FirstOrDefault(item =>
                item != null && !item.Destroyed &&
                pawn.apparel?.WornApparel.Contains(item) != true);
            if (missingItem == null)
                return "Finishing the outfit change.";

            return $"Waiting for saved apparel: {missingItem.LabelCap} — {UnavailableReason(pawn, missingItem)}";
        }

        private static string TaskBufferStatus(int completed, int maximum)
        {
            maximum = System.Math.Max(0, maximum);
            completed = System.Math.Max(0, System.Math.Min(completed, maximum));
            return $"Task buffer: {completed} of {maximum} completed.";
        }

        private static string UnavailableReason(Pawn pawn, Apparel apparel)
        {
            Pawn wearer = Find.Maps
                .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                .FirstOrDefault(candidate => candidate != pawn &&
                    candidate.apparel?.WornApparel.Contains(apparel) == true);
            if (wearer != null)
                return $"currently worn by {wearer.LabelShortCap}";

            if (!apparel.Spawned)
                return "inside an inventory or container";
            if (apparel.Map != pawn.Map)
                return "on another map";
            if (apparel.IsForbidden(pawn))
                return "forbidden";
            if (!pawn.CanReserve(apparel))
                return "reserved by another task";
            if (!pawn.CanReach(apparel, PathEndMode.ClosestTouch, Danger.Deadly))
                return "unreachable";
            return "ready to retrieve";
        }
    }
}

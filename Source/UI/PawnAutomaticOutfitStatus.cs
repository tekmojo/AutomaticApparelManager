using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Detection;
using AutomaticOutfitManager.Rules;
using AutomaticOutfitManager.State;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.UI
{
    public static class PawnAutomaticOutfitStatus
    {
        private const float CacheSeconds = 0.5f;
        private static readonly Dictionary<Pawn, CachedStatus> StatusCache =
            new Dictionary<Pawn, CachedStatus>();

        private sealed class CachedStatus
        {
            public float CreatedAt;
            public ApparelTransition Transition;
            public string RuleId;
            public string CurrentRuleSignature;
            public string NestedBufferSignature;
            public int OriginalCount;
            public int AutomaticCount;
            public int WornCount;
            public int BufferedTasksCompleted;
            public int ReturnTaskBuffer;
            public int CurrentJobLoadId;
            public bool RecallInterruptPending;
            public bool Drafted;
            public string WornSignature;
            public string Text;
        }

        public static string Build(Pawn pawn)
        {
            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            PawnApparelState state = component?.StateFor(pawn);
            if (state == null)
            {
                if (pawn != null)
                    StatusCache.Remove(pawn);
                return null;
            }

            ApparelRule rule = component.RuleById(state.ActiveRuleId);
            int originalCount = state.OriginalApparel?.Count ?? 0;
            int automaticCount = state.ManagedApparel?.Count ?? 0;
            int wornCount = pawn.apparel?.WornApparelCount ?? 0;
            int returnTaskBuffer = rule?.ReturnTaskBuffer ?? 0;
            int currentJobLoadId = pawn.CurJob?.loadID ?? -1;
            string wornSignature = string.Join(",", (pawn.apparel?.WornApparel ??
                    new List<Apparel>())
                .Where(item => item != null)
                .Select(item => item.GetUniqueLoadID())
                .OrderBy(id => id));
            string currentRuleSignature = string.Join(",", state.CurrentRuleIds ?? new List<string>());
            string nestedBufferSignature = string.Join(",", (state.NestedRuleBuffers ??
                new List<NestedRuleBufferState>()).Select(item =>
                    $"{item.RuleId}:{item.Completed}:{item.Finished}:{item.LastJobLoadId}"));
            if (StatusCache.TryGetValue(pawn, out CachedStatus cached) &&
                Time.realtimeSinceStartup - cached.CreatedAt < CacheSeconds &&
                cached.Transition == state.Transition &&
                cached.RuleId == state.ActiveRuleId &&
                cached.CurrentRuleSignature == currentRuleSignature &&
                cached.NestedBufferSignature == nestedBufferSignature &&
                cached.OriginalCount == originalCount &&
                cached.AutomaticCount == automaticCount &&
                cached.WornCount == wornCount &&
                cached.BufferedTasksCompleted == state.BufferedTasksCompleted &&
                cached.ReturnTaskBuffer == returnTaskBuffer &&
                cached.CurrentJobLoadId == currentJobLoadId &&
                cached.RecallInterruptPending == state.RecallInterruptPending &&
                cached.Drafted == pawn.Drafted &&
                cached.WornSignature == wornSignature)
            {
                return cached.Text;
            }

            List<ApparelRule> currentRules = (state.CurrentRuleIds ?? new List<string>())
                .Select(component.RuleById)
                .Where(current => current != null)
                .ToList();
            List<ApparelRule> requiredSessionRules = currentRules
                .Concat(rule == null
                    ? Enumerable.Empty<ApparelRule>()
                    : new[] { rule })
                .Concat((state.NestedRuleBuffers ?? new List<NestedRuleBufferState>())
                    .Where(progress => progress != null && !progress.Finished)
                    .Select(progress => component.RuleById(progress.RuleId))
                    .Where(candidate => candidate != null))
                .GroupBy(candidate => candidate.Id)
                .Select(group => group.First())
                .ToList();
            string transition = state.RecallInterruptPending
                ? "Return pending"
                : TransitionLabel(
                    pawn, state, requiredSessionRules, returnTaskBuffer);
            string text = $"AutomaticOutfitManager: {transition}";
            if (currentRules.Count > 1)
                text += $"\nRules: {string.Join(" → ", currentRules.Select(current => current.Name))}";
            else if (currentRules.Count == 1)
                text += $"\nRule: {currentRules[0].Name}";
            else if (rule != null)
                text += $"\nRule: {rule.Name}";

            // Show nested buffers as soon as an overlapping rule is selected,
            // including while the pawn is still outfitting. The persisted
            // buffer state is intentionally created only when prepared work can
            // really start, but the hover should still expose the configured
            // nested buffer at 0/N instead of appearing to omit it.
            var nestedProgressByRule = (state.NestedRuleBuffers ??
                    new List<NestedRuleBufferState>())
                .Where(nested => nested != null && !string.IsNullOrEmpty(nested.RuleId))
                .GroupBy(nested => nested.RuleId)
                .ToDictionary(group => group.Key, group => group.First());
            List<string> nestedRuleIds = currentRules
                .Where(current => current.Id != state.ActiveRuleId)
                .Select(current => current.Id)
                .Concat(nestedProgressByRule.Keys)
                .Distinct()
                .ToList();
            var bufferStatuses = new List<string>();
            if (rule != null)
            {
                bufferStatuses.Add(BufferStatus(
                    rule.Name, state.BufferedTasksCompleted,
                    returnTaskBuffer, false));
            }
            else
            {
                bufferStatuses.Add(BufferStatus(
                    null, state.BufferedTasksCompleted,
                    returnTaskBuffer, false));
            }
            foreach (string nestedRuleId in nestedRuleIds)
            {
                ApparelRule nestedRule = component.RuleById(nestedRuleId);
                if (nestedRule != null)
                {
                    bool hasProgress = nestedProgressByRule.TryGetValue(
                        nestedRuleId, out NestedRuleBufferState nested);
                    bufferStatuses.Add(BufferStatus(
                        nestedRule.Name,
                        hasProgress ? nested.Completed : 0,
                        nestedRule.ReturnTaskBuffer,
                        hasProgress && nested.Finished));
                }
            }
            text += $"\nBuffers: {string.Join(" · ", bufferStatuses)}";

            string detail = DetailFor(
                pawn, state, rule, requiredSessionRules);
            if (!string.IsNullOrEmpty(detail))
                text += $"\n{detail}";

            StatusCache[pawn] = new CachedStatus
            {
                CreatedAt = Time.realtimeSinceStartup,
                Transition = state.Transition,
                RuleId = state.ActiveRuleId,
                CurrentRuleSignature = currentRuleSignature,
                NestedBufferSignature = nestedBufferSignature,
                OriginalCount = originalCount,
                AutomaticCount = automaticCount,
                WornCount = wornCount,
                BufferedTasksCompleted = state.BufferedTasksCompleted,
                ReturnTaskBuffer = returnTaskBuffer,
                CurrentJobLoadId = currentJobLoadId,
                RecallInterruptPending = state.RecallInterruptPending,
                Drafted = pawn.Drafted,
                WornSignature = wornSignature,
                Text = text
            };
            return text;
        }

        private static string TransitionLabel(
            Pawn pawn,
            PawnApparelState state,
            List<ApparelRule> requiredSessionRules,
            int returnTaskBuffer)
        {
            ApparelTransition transition = state.Transition;
            Job currentJob = pawn?.CurJob;
            if (transition == ApparelTransition.Restoring)
            {
                if (pawn?.Drafted == true)
                    return "Restoration paused — drafted";

                if (currentJob?.def == JobDefOf.LayDown)
                    return "Restoration paused — sleeping or resting";

                if (currentJob?.playerForced == true &&
                    currentJob.def != JobDefOf.Wear &&
                    currentJob.def != JobDefOf.RemoveApparel)
                {
                    return "Restoration paused — forced order";
                }
            }

            if (transition == ApparelTransition.Active &&
                returnTaskBuffer > 0 &&
                state.BufferedTasksCompleted > 0 &&
                currentJob != null &&
                currentJob.loadID == state.LastBufferedJobLoadId)
            {
                int completed = System.Math.Min(
                    state.BufferedTasksCompleted, returnTaskBuffer);
                return $"Buffered task {completed} of {returnTaskBuffer}: " +
                       JobActivity(pawn, currentJob);
            }

            switch (transition)
            {
                case ApparelTransition.Preparing:
                    if (state.NestedRuleBuffers?.Any(progress =>
                            progress?.Finished == true) == true &&
                        (currentJob?.def == JobDefOf.RemoveApparel ||
                         currentJob?.def == JobDefOf.Goto))
                    {
                        return "Returning nested work gear";
                    }
                    return currentJob?.def == JobDefOf.Wear
                        ? $"Outfitting work gear: {JobActivity(pawn, currentJob)}"
                        : "Outfitting work gear";
                case ApparelTransition.Active:
                    if (requiredSessionRules.Any(candidate =>
                            RuleEvaluator.HasMissingRequiredApparel(pawn, candidate)))
                    {
                        return "Work gear incomplete";
                    }
                    if (IsIdleJob(pawn, currentJob))
                        return "Waiting with work outfit";
                    NestedRuleBufferState currentNestedBuffer = currentJob == null
                        ? null
                        : state.NestedRuleBuffers?.FirstOrDefault(progress =>
                            progress != null && !progress.Finished &&
                            progress.LastJobLoadId == currentJob.loadID);
                    if (currentNestedBuffer != null)
                    {
                        ApparelRule nestedRule = AutomaticOutfitManagerGameComponent.Current?
                            .RuleById(currentNestedBuffer.RuleId);
                        int maximum = System.Math.Max(
                            0, nestedRule?.ReturnTaskBuffer ?? 0);
                        int completed = System.Math.Max(
                            0, System.Math.Min(currentNestedBuffer.Completed, maximum));
                        return $"Buffered task {completed} of {maximum}: " +
                               JobActivity(pawn, currentJob);
                    }
                    if (currentJob?.workGiverDef != null &&
                        RuleEvaluator.MatchingRules(pawn, currentJob).Count > 0)
                    {
                        return $"Working: {JobActivity(pawn, currentJob)}";
                    }
                    if (currentJob?.def == JobDefOf.Goto ||
                        pawn?.pather?.Moving == true)
                    {
                        return $"Traveling: {JobActivity(pawn, currentJob)}";
                    }
                    if (currentJob != null && IsMeaningfulActivity(currentJob))
                        return $"Active: {JobActivity(pawn, currentJob)}";
                    return "Work outfit equipped";
                case ApparelTransition.ReturningToChangingArea:
                    return "Returning to locker room";
                case ApparelTransition.Restoring:
                    if (currentJob?.def == JobDefOf.RemoveApparel)
                        return "Returning work gear";
                    if (IsIdleJob(pawn, currentJob))
                        return "Waiting for saved gear";
                    return currentJob?.def == JobDefOf.Wear
                        ? $"Outfitting saved gear: {JobActivity(pawn, currentJob)}"
                        : "Outfitting saved gear";
                default:
                    return transition.ToString();
            }
        }

        private static string DetailFor(
            Pawn pawn,
            PawnApparelState state,
            ApparelRule rule,
            List<ApparelRule> requiredSessionRules)
        {
            if (state.Transition == ApparelTransition.Preparing)
            {
                if (state.NestedRuleBuffers?.Any(progress =>
                        progress?.Finished == true) == true &&
                    (pawn?.CurJob?.def == JobDefOf.RemoveApparel ||
                     pawn?.CurJob?.def == JobDefOf.Goto))
                {
                    // The transition title and compact buffer summary already
                    // describe this state; repeating it made the hover grow by
                    // another wrapped line without adding actionable detail.
                    return null;
                }

                List<ThingDef> missing = requiredSessionRules
                    .SelectMany(candidate => RuleEvaluator.MissingRequiredApparel(pawn, candidate))
                    .Where(def => def != null)
                    .Distinct()
                    .ToList();
                return missing.Count == 0
                    ? "Waiting for the work job to resume."
                    : $"Still needed: {string.Join(", ", missing.Select(def => def.LabelCap.ToString()))}";
            }

            if (state.Transition == ApparelTransition.ReturningToChangingArea)
                return rule?.ChangingArea == null ? null : $"Destination: {rule.ChangingArea.Label}";

            if (state.Transition == ApparelTransition.Active)
            {
                List<ThingDef> missing = requiredSessionRules
                    .SelectMany(candidate => RuleEvaluator.MissingRequiredApparel(pawn, candidate))
                    .Where(def => def != null)
                    .Distinct()
                    .ToList();
                return missing.Count == 0
                    ? null
                    : $"Still needed: {string.Join(", ", missing.Select(def => def.LabelCap.ToString()))}";
            }

            if (state.Transition != ApparelTransition.Restoring)
                return null;

            Apparel missingItem = state.OriginalApparel.FirstOrDefault(item =>
                item != null && !item.Destroyed &&
                pawn.apparel?.WornApparel.Contains(item) != true);
            if (missingItem == null)
                return "Finishing the outfit change.";

            return $"Waiting for saved apparel: {missingItem.LabelCap} — {UnavailableReason(pawn, missingItem)}";
        }

        private static string JobActivity(Pawn pawn, Job job)
        {
            string activity = job?.GetReport(pawn);
            if (string.IsNullOrEmpty(activity))
                activity = job?.def?.label ?? "Task";
            return activity.CapitalizeFirst();
        }

        private static bool IsIdleJob(Pawn pawn, Job job)
        {
            if (job == null)
                return true;

            string defName = job.def?.defName ?? string.Empty;
            bool waitFamily = job.def == JobDefOf.Wait ||
                              job.def == JobDefOf.Wait_Wander ||
                              defName.StartsWith("Wait", System.StringComparison.OrdinalIgnoreCase) ||
                              defName.IndexOf("Standing", System.StringComparison.OrdinalIgnoreCase) >= 0;
            return waitFamily &&
                   pawn?.pather?.Moving != true &&
                   pawn?.carryTracker?.CarriedThing == null;
        }

        private static bool IsMeaningfulActivity(Job job)
        {
            if (job?.def == null)
                return false;

            string defName = job.def.defName ?? string.Empty;
            return job.def != JobDefOf.Goto &&
                   job.def != JobDefOf.Wait &&
                   job.def != JobDefOf.Wait_Wander &&
                   !defName.StartsWith("Goto", System.StringComparison.OrdinalIgnoreCase) &&
                   !defName.StartsWith("Wait", System.StringComparison.OrdinalIgnoreCase) &&
                   defName.IndexOf("Standing", System.StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string BufferStatus(
            string name, int completed, int maximum, bool finished)
        {
            maximum = System.Math.Max(0, maximum);
            completed = System.Math.Max(0, System.Math.Min(completed, maximum));
            string status = $"{completed}/{maximum}";
            if (finished)
                status += " complete";
            return string.IsNullOrEmpty(name) ? status : $"{name} {status}";
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

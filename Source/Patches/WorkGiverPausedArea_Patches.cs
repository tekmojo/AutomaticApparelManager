using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.Rules;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
{
    /// <summary>
    /// Rejects paused-area work while RimWorld is still scanning work givers.
    /// This is deliberately earlier than Pawn_JobTracker.StartJob: replacing an
    /// already-selected job with Wait makes the thinker select the same job over
    /// and over, leaving the pawn standing indefinitely.
    /// </summary>
    internal static class PausedAreaWorkFilter
    {
        public static IEnumerable<MethodBase> ScannerMethods(Type returnType)
        {
            string[] names = returnType == typeof(bool)
                ? new[] { "HasJobOnThing", "HasJobOnCell" }
                : new[] { "JobOnThing", "JobOnCell" };

            return GenTypes.AllTypes
                .Where(type => type != null &&
                               !type.IsAbstract &&
                               typeof(WorkGiver_Scanner).IsAssignableFrom(type))
                .SelectMany(type => AccessTools.GetDeclaredMethods(type))
                .Where(method => names.Contains(method.Name) &&
                                 method.ReturnType == returnType &&
                                 !method.IsAbstract &&
                                 !method.ContainsGenericParameters)
                .Cast<MethodBase>()
                .Distinct();
        }

        public static bool ShouldReject(object[] arguments)
        {
            Pawn pawn = null;
            Thing thing = null;
            IntVec3 cellArgument = IntVec3.Invalid;
            if (arguments != null)
            {
                foreach (object argument in arguments)
                {
                    if (argument is Pawn pawnArgument)
                    {
                        if (pawn == null)
                            pawn = pawnArgument;
                        else if (thing == null)
                            thing = pawnArgument;
                    }
                    else if (thing == null && argument is Thing thingArgument)
                    {
                        thing = thingArgument;
                    }
                    if (!cellArgument.IsValid && argument is IntVec3 cell)
                        cellArgument = cell;
                }
            }

            if (pawn?.Map == null || pawn.Faction != Faction.OfPlayer || pawn.Drafted)
                return false;

            // Recall/restoration jobs must remain available so a paused rule can
            // first return the pawn's normal clothing and release intervention.
            if (AutomaticApparelGameComponent.Current?.StateFor(pawn)?.RecallRequested == true)
                return false;

            IntVec3 targetCell = thing?.PositionHeld ?? cellArgument;
            Map targetMap = thing?.MapHeld ?? pawn.Map;
            if (!targetCell.IsValid || targetMap == null)
                return false;

            return AutomaticApparelGameComponent.Current?.Rules?.Any(rule =>
                rule != null &&
                rule.Enabled &&
                rule.WorkAreaPaused &&
                rule.Area?.Map == targetMap &&
                targetCell.InBounds(targetMap) &&
                rule.Area[targetCell]) == true;
        }

        public static bool ShouldRejectJob(Job job, object[] arguments)
        {
            Pawn pawn = null;
            if (arguments != null)
            {
                foreach (object argument in arguments)
                {
                    if (argument is Pawn pawnArgument)
                    {
                        pawn = pawnArgument;
                        break;
                    }
                }
            }
            if (job == null || pawn?.Map == null)
                return false;

            if (ShouldReject(arguments))
                return true;

            return ShouldRejectPausedAreaJob(pawn, job) ||
                   ShouldRejectHaulingJob(pawn, job);
        }

        public static bool ShouldRejectHaulingJob(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || !IsHaulingJob(job) ||
                !IsManagedPawn(pawn))
            {
                return false;
            }

            return AutomaticApparelGameComponent.Current?.Rules?.Any(rule =>
                rule != null &&
                rule.Enabled &&
                rule.Area?.Map == pawn.Map &&
                !HaulingAllowedFor(rule, pawn) &&
                (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                 HaulingPathCrossesArea(pawn, job, rule.Area))) == true;
        }

        private static bool HaulingPathCrossesArea(Pawn pawn, Job job, Area area)
        {
            if (pawn?.Map == null || area?.Map != pawn.Map)
                return false;

            LocalTargetInfo pickup = job.targetA;
            LocalTargetInfo destination = job.targetB.IsValid
                ? job.targetB
                : job.targetC;

            if (PathCrossesArea(pawn, pawn.Position, pickup, area))
                return true;

            IntVec3 pickupCell = pickup.IsValid ? pickup.Cell : IntVec3.Invalid;
            return pickupCell.IsValid && destination.IsValid &&
                   PathCrossesArea(pawn, pickupCell, destination, area);
        }

        private static bool PathCrossesArea(
            Pawn pawn, IntVec3 start, LocalTargetInfo destination, Area area)
        {
            if (!start.IsValid || !start.InBounds(pawn.Map) || !destination.IsValid)
                return false;

            PathEndMode endMode = destination.HasThing
                ? PathEndMode.Touch
                : PathEndMode.OnCell;
            PawnPath path = null;
            try
            {
                path = pawn.Map.pathFinder.FindPathNow(
                    start, destination, pawn, null, endMode);
                return path != null && path.Found &&
                       path.NodesReversed.Any(cell =>
                           cell.IsValid && cell.InBounds(pawn.Map) && area[cell]);
            }
            finally
            {
                path?.ReleaseToPool();
            }
        }

        public static bool ShouldRejectProtectedAreaJob(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || pawn.Drafted ||
                pawn.DevelopmentalStage == DevelopmentalStage.Adult)
                return false;

            bool isWorkWatching = IsWorkWatchingJob(job);
            return AutomaticApparelGameComponent.Current?.Rules?.Any(rule =>
                rule != null &&
                rule.Enabled &&
                rule.Area?.Map == pawn.Map &&
                ProtectedJobTargetsArea(pawn, job, rule.Area, isWorkWatching) &&
                !(isWorkWatching && rule.AllowChildWorkWatching)) == true;
        }

        private static bool ProtectedJobTargetsArea(
            Pawn child, Job job, Area area, bool isWorkWatching)
        {
            if (RuleEvaluator.JobTargetsArea(job, area) ||
                (child.Position.IsValid && child.Position.InBounds(child.Map) &&
                 area[child.Position]))
            {
                return true;
            }

            if (!isWorkWatching)
                return JobPathCrossesArea(child, job, area);

            // Work watching follows an adult dynamically. The child's job can
            // therefore target only the adult, while the adult's workstation
            // or interaction cell is the protected location that matters.
            Pawn watchedPawn = new[] { job.targetA, job.targetB, job.targetC }
                .Where(target => target.IsValid && target.HasThing)
                .Select(target => target.Thing as Pawn)
                .FirstOrDefault(target => target != null);

            return watchedPawn?.Map == area.Map &&
                   ((watchedPawn.Position.IsValid &&
                     watchedPawn.Position.InBounds(watchedPawn.Map) &&
                     area[watchedPawn.Position]) ||
                    RuleEvaluator.JobTargetsArea(watchedPawn.CurJob, area));
        }

        private static bool IsWorkWatchingJob(Job job) =>
            string.Equals(job?.def?.defName, "Workwatching", StringComparison.OrdinalIgnoreCase);

        public static bool TryRedirectProtectedChildJob(Pawn pawn, Job rejectedJob)
        {
            if (!ShouldRejectProtectedAreaJob(pawn, rejectedJob) || pawn?.Map == null)
                return false;

            List<ApparelRule> restrictedRules = ProtectedChildRules(pawn, rejectedJob);
            if (restrictedRules.Count == 0 ||
                !TryFindSafeWanderingCell(pawn, restrictedRules, out IntVec3 safeCell))
            {
                return false;
            }

            rejectedJob.def = JobDefOf.GotoWander;
            rejectedJob.targetA = safeCell;
            rejectedJob.targetB = LocalTargetInfo.Invalid;
            rejectedJob.targetC = LocalTargetInfo.Invalid;
            rejectedJob.expiryInterval = 300;
            rejectedJob.locomotionUrgency = LocomotionUrgency.Jog;
            return true;
        }

        public static bool TryMakeProtectedChildExitJob(
            Pawn pawn, Job rejectedJob, out Job exitJob)
        {
            exitJob = null;
            if (!ShouldRejectProtectedAreaJob(pawn, rejectedJob) || pawn?.Map == null)
                return false;

            List<ApparelRule> restrictedRules = ProtectedChildRules(pawn, rejectedJob);
            if (restrictedRules.Count == 0 ||
                !TryFindSafeWanderingCell(pawn, restrictedRules, out IntVec3 safeCell))
            {
                return false;
            }

            exitJob = JobMaker.MakeJob(JobDefOf.Goto, safeCell);
            exitJob.expiryInterval = 300;
            exitJob.locomotionUrgency = LocomotionUrgency.Jog;
            return true;
        }

        private static List<ApparelRule> ProtectedChildRules(Pawn pawn, Job job)
        {
            bool isWorkWatching = IsWorkWatchingJob(job);
            return AutomaticApparelGameComponent.Current?.Rules?
                .Where(rule => rule != null &&
                               rule.Enabled &&
                               rule.Area?.Map == pawn.Map &&
                               ProtectedJobTargetsArea(pawn, job, rule.Area, isWorkWatching) &&
                               !(isWorkWatching && rule.AllowChildWorkWatching))
                .ToList() ?? new List<ApparelRule>();
        }

        public static bool ShouldRejectPausedAreaJob(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || pawn.Drafted ||
                !IsManagedPawn(pawn) ||
                AutomaticApparelGameComponent.Current?.StateFor(pawn)?.RecallRequested == true)
                return false;

            // A paused rule stops ordinary work, but its hauling toggle may
            // explicitly permit hauling or wandering. Humanlike pawns still
            // pass through the normal apparel intervention where applicable.
            if (MatchingPermittedHaulingRule(pawn, job) != null ||
                MatchingPermittedWanderingRule(pawn, job) != null)
                return false;

            return AutomaticApparelGameComponent.Current?.Rules?.Any(rule =>
                rule != null &&
                rule.Enabled &&
                rule.WorkAreaPaused &&
                rule.Area?.Map == pawn.Map &&
                (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                 JobPathCrossesArea(pawn, job, rule.Area))) == true;
        }

        public static ApparelRule MatchingPermittedHaulingRule(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || !IsManagedPawn(pawn) ||
                pawn.Drafted || !IsHaulingJob(job))
            {
                return null;
            }

            return AutomaticApparelGameComponent.Current?.Rules?.FirstOrDefault(rule =>
                MatchesPermittedHaulingRule(pawn, job, rule));
        }

        public static bool MatchesPermittedHaulingRule(
            Pawn pawn, Job job, ApparelRule rule)
        {
            if (pawn?.Map == null || job == null || rule == null ||
                !IsManagedPawn(pawn) || pawn.Drafted || !IsHaulingJob(job) ||
                !rule.Enabled || !rule.WorkAreaPaused ||
                rule.Area?.Map != pawn.Map || !HaulingAllowedFor(rule, pawn))
            {
                return false;
            }

            return (!pawn.RaceProps.Humanlike || RuleEvaluator.RuleCanApplyToPawn(pawn, rule)) &&
                   (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                    HaulingPathCrossesArea(pawn, job, rule.Area));
        }

        private static ApparelRule MatchingPermittedWanderingRule(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || !IsManagedPawn(pawn) ||
                pawn.Drafted || !IsRestrictedRoamingJob(pawn, job, job.jobGiver))
            {
                return null;
            }

            return AutomaticApparelGameComponent.Current?.Rules?.FirstOrDefault(rule =>
                rule != null && rule.Enabled && rule.WorkAreaPaused &&
                rule.Area?.Map == pawn.Map && WanderingAllowedFor(rule, pawn) &&
                (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                 WanderingPathCrossesArea(pawn, job, rule.Area)));
        }

        public static bool JobMayEnterPausedRule(Pawn pawn, Job job, ApparelRule rule)
        {
            return MatchesPermittedHaulingRule(pawn, job, rule) ||
                   MatchesPermittedWanderingRule(pawn, job, rule);
        }

        public static bool IsHaulingOrWanderingActivityForRule(
            Pawn pawn, Job job, ApparelRule rule)
        {
            if (pawn?.Map == null || job == null || rule?.Area?.Map != pawn.Map ||
                !IsManagedPawn(pawn) || pawn.Drafted)
            {
                return false;
            }

            bool relevantJob = IsHaulingJob(job) ||
                               IsRestrictedRoamingJob(pawn, job, job.jobGiver);
            return relevantJob &&
                   (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                    JobPathCrossesArea(pawn, job, rule.Area));
        }

        public static bool IsHaulingActivityForRule(
            Pawn pawn, Job job, ApparelRule rule)
        {
            return IsActivityForRule(pawn, job, rule, true);
        }

        public static bool IsWanderingActivityForRule(
            Pawn pawn, Job job, ApparelRule rule)
        {
            return IsActivityForRule(pawn, job, rule, false);
        }

        private static bool IsActivityForRule(
            Pawn pawn, Job job, ApparelRule rule, bool hauling)
        {
            if (pawn?.Map == null || job == null || rule?.Area?.Map != pawn.Map ||
                !IsManagedPawn(pawn) || pawn.Drafted)
            {
                return false;
            }

            bool relevantJob = hauling
                ? IsHaulingJob(job)
                : IsRestrictedRoamingJob(pawn, job, job.jobGiver);
            return relevantJob &&
                   (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                    JobPathCrossesArea(pawn, job, rule.Area));
        }

        private static bool MatchesPermittedWanderingRule(Pawn pawn, Job job, ApparelRule rule)
        {
            return pawn?.Map != null && job != null && rule != null &&
                   IsManagedPawn(pawn) && !pawn.Drafted &&
                   IsRestrictedRoamingJob(pawn, job, job.jobGiver) &&
                   rule.Enabled && rule.WorkAreaPaused &&
                   rule.Area?.Map == pawn.Map && WanderingAllowedFor(rule, pawn) &&
                   (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                    WanderingPathCrossesArea(pawn, job, rule.Area));
        }

        public static ApparelRule MatchingProtectedTransitRule(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null || !pawn.IsColonist || pawn.Drafted)
                return null;

            return AutomaticApparelGameComponent.Current?.Rules?.FirstOrDefault(rule =>
                MatchesProtectedTransitRule(pawn, job, rule));
        }

        public static bool MatchesProtectedTransitRule(
            Pawn pawn, Job job, ApparelRule rule)
        {
            if (pawn?.Map == null || job == null || rule == null ||
                !pawn.IsColonist || pawn.Drafted || !rule.Enabled ||
                rule.WorkAreaPaused || rule.Area?.Map != pawn.Map ||
                !RuleEvaluator.RuleCanApplyToPawn(pawn, rule))
            {
                return false;
            }

            // Direct targets are handled by RuleEvaluator.MatchesRule. This
            // branch covers unrelated destinations whose actual route crosses
            // the protected area, so PPE is required even for pass-through.
            return !RuleEvaluator.JobTargetsArea(job, rule.Area) &&
                   JobPathCrossesArea(pawn, job, rule.Area);
        }

        private static bool JobPathCrossesArea(Pawn pawn, Job job, Area area)
        {
            LocalTargetInfo destination = job.targetA.IsValid
                ? job.targetA
                : job.targetB.IsValid ? job.targetB : job.targetC;
            return destination.IsValid &&
                   PathCrossesArea(pawn, pawn.Position, destination, area);
        }

        public static bool ShouldRejectWanderingJob(Pawn pawn, Job job, ThinkNode jobGiver = null)
        {
            if (pawn?.Map == null || job == null || pawn.Drafted ||
                !IsRestrictedRoamingJob(pawn, job, jobGiver) ||
                !IsManagedPawn(pawn))
                return false;

            return AutomaticApparelGameComponent.Current?.Rules?.Any(rule =>
                rule != null &&
                rule.Enabled &&
                !WanderingAllowedFor(rule, pawn) &&
                rule.Area?.Map == pawn.Map &&
                (RuleEvaluator.JobTargetsArea(job, rule.Area) ||
                 WanderingPathCrossesArea(pawn, job, rule.Area) ||
                 (job.def == JobDefOf.Wait_Wander &&
                 pawn.Position.IsValid && pawn.Position.InBounds(pawn.Map) &&
                 rule.Area[pawn.Position]))) == true;
        }

        private static bool WanderingPathCrossesArea(Pawn pawn, Job job, Area area)
        {
            LocalTargetInfo destination = job.targetA.IsValid
                ? job.targetA
                : job.targetB.IsValid ? job.targetB : job.targetC;
            return destination.IsValid &&
                   PathCrossesArea(pawn, pawn.Position, destination, area);
        }

        public static bool TryRedirectWanderingJob(Pawn pawn, Job job, ThinkNode jobGiver = null)
        {
            if (!ShouldRejectWanderingJob(pawn, job, jobGiver) || pawn?.Map == null)
                return false;

            List<ApparelRule> restrictedRules = AutomaticApparelGameComponent.Current?.Rules?
                .Where(rule => rule != null &&
                               rule.Enabled &&
                               !WanderingAllowedFor(rule, pawn) &&
                               rule.Area?.Map == pawn.Map)
                .ToList();
            if (restrictedRules == null || restrictedRules.Count == 0)
                return false;

            bool startsInside = restrictedRules.Any(rule => rule.Area[pawn.Position]);
            if (!startsInside)
            {
                // The pawn is already safe, but its thinker selected a wander
                // route through the restricted area. Sending it to another
                // GotoWander cell makes autonomous robot thinkers immediately
                // request yet another wander job on arrival, producing a
                // visible wander/no-job loop. Yield briefly in place instead;
                // normal AI can reconsider useful work after the cooldown.
                job.def = JobDefOf.Wait;
                job.targetA = pawn;
                job.targetB = LocalTargetInfo.Invalid;
                job.targetC = LocalTargetInfo.Invalid;
                job.expiryInterval = 180;
                return true;
            }

            if (!TryFindSafeWanderingCell(pawn, restrictedRules, out IntVec3 safeCell))
                return false;

            // An occupant of the restricted area still needs one direct exit
            // movement before the outside cooldown behavior can take over.
            job.def = JobDefOf.GotoWander;
            job.targetA = safeCell;
            job.targetB = LocalTargetInfo.Invalid;
            job.targetC = LocalTargetInfo.Invalid;
            return true;
        }

        public static bool TryMakeWanderingExitJob(Pawn pawn, out Job exitJob)
        {
            exitJob = null;
            if (pawn?.Map == null)
                return false;

            List<ApparelRule> restrictedRules = AutomaticApparelGameComponent.Current?.Rules?
                .Where(rule => rule != null &&
                               rule.Enabled &&
                               !WanderingAllowedFor(rule, pawn) &&
                               rule.Area?.Map == pawn.Map)
                .ToList();
            if (restrictedRules == null ||
                !restrictedRules.Any(rule => rule.Area[pawn.Position]) ||
                !TryFindSafeWanderingCell(pawn, restrictedRules, out IntVec3 safeCell))
            {
                return false;
            }

            exitJob = JobMaker.MakeJob(JobDefOf.Goto, safeCell);
            exitJob.expiryInterval = 300;
            exitJob.locomotionUrgency = LocomotionUrgency.Jog;
            return true;
        }

        private static bool TryFindSafeWanderingCell(
            Pawn pawn, List<ApparelRule> restrictedRules, out IntVec3 safeCell)
        {
            safeCell = IntVec3.Invalid;
            if (pawn?.Map == null || restrictedRules == null || restrictedRules.Count == 0)
                return false;

            bool startsInside = restrictedRules.Any(rule => rule.Area[pawn.Position]);
            IEnumerable<IntVec3> candidates = GenRadial.RadialCellsAround(
                    pawn.Position, 24f, true)
                .Where(cell => cell.IsValid &&
                               cell.InBounds(pawn.Map) &&
                               cell != pawn.Position &&
                               cell.Standable(pawn.Map) &&
                               restrictedRules.All(rule => !rule.Area[cell]) &&
                               HasRestrictedAreaClearance(
                                   pawn.Map, cell, restrictedRules, 3.9f) &&
                               pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some));

            // An occupant must cross its current restricted area to leave, so
            // choose the nearest valid outside cell. Pawns already outside keep
            // the stricter rule that their entire route must avoid the area.
            if (startsInside)
                candidates = candidates.OrderBy(cell => cell.DistanceToSquared(pawn.Position));
            else
                candidates = candidates.InRandomOrder();

            foreach (IntVec3 candidate in candidates)
            {
                if (startsInside || restrictedRules.All(rule =>
                    !PathCrossesArea(pawn, pawn.Position, candidate, rule.Area)))
                {
                    safeCell = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool HasRestrictedAreaClearance(
            Map map, IntVec3 cell, List<ApparelRule> restrictedRules, float radius)
        {
            foreach (IntVec3 nearby in GenRadial.RadialCellsAround(cell, radius, true))
            {
                if (!nearby.IsValid || !nearby.InBounds(map))
                    continue;

                if (restrictedRules.Any(rule => rule.Area[nearby]))
                    return false;
            }

            return true;
        }

        private static bool IsWanderingJob(Job job, ThinkNode jobGiver)
        {
            string defName = job?.def?.defName;
            string giverName = (jobGiver ?? job?.jobGiver)?.GetType().Name;
            return job?.def == JobDefOf.GotoWander ||
                   job?.def == JobDefOf.Wait_Wander ||
                   (!string.IsNullOrEmpty(defName) &&
                    defName.IndexOf("Wander", StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (!string.IsNullOrEmpty(giverName) &&
                    giverName.IndexOf("Wander", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsRestrictedRoamingJob(Pawn pawn, Job job, ThinkNode jobGiver)
        {
            if (IsWanderingJob(job, jobGiver))
                return true;

            // Autonomous cleaning bots use ordinary Clean jobs rather than a
            // wander-family job. They still roam the map opportunistically and
            // cannot outfit protective apparel, so the Robots/mechs wandering
            // toggle must govern this movement as well. Limiting the extension
            // to robot/mech pawns keeps colonist cleaning classified as work.
            if (!IsRobotOrMechanoid(pawn))
                return false;

            string defName = job?.def?.defName;
            string giverName = (jobGiver ?? job?.jobGiver)?.GetType().Name;
            string driverName = job?.def?.driverClass?.Name;
            return ContainsIgnoreCase(defName, "Clean") ||
                   ContainsIgnoreCase(giverName, "Clean") ||
                   ContainsIgnoreCase(driverName, "Clean");
        }

        private static bool ContainsIgnoreCase(string value, string fragment) =>
            !string.IsNullOrEmpty(value) &&
            value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsHaulingJob(Job job)
        {
            if (job?.def == null)
                return false;

            if (job.def == JobDefOf.HaulToCell)
                return true;

            Type driverClass = job.def.driverClass;
            return driverClass != null &&
                   driverClass.Name.IndexOf("Haul", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool HaulingAllowedFor(ApparelRule rule, Pawn pawn)
        {
            if (pawn?.RaceProps == null)
                return rule.AllowColonistHauling;

            if (IsPrisoner(pawn))
                return rule.AllowPrisonerHauling;
            if (IsFriendlyGuest(pawn))
                return rule.AllowGuestHauling;
            if (pawn.IsSlave)
                return rule.AllowSlaveHauling;
            if (IsRobotOrMechanoid(pawn))
                return rule.AllowRobotHauling;
            if (pawn.RaceProps.Animal)
                return rule.AllowAnimalHauling;
            return rule.AllowColonistHauling;
        }

        private static bool WanderingAllowedFor(ApparelRule rule, Pawn pawn)
        {
            if (pawn?.RaceProps == null)
                return rule.AllowColonistWandering;

            if (IsPrisoner(pawn))
                return rule.AllowPrisonerWandering;
            if (IsFriendlyGuest(pawn))
                return rule.AllowGuestWandering;
            if (pawn.IsSlave)
                return rule.AllowSlaveWandering;
            if (IsRobotOrMechanoid(pawn))
                return rule.AllowRobotWandering;
            if (pawn.RaceProps.Animal)
                return rule.AllowAnimalWandering;
            return rule.AllowColonistWandering;
        }

        private static bool IsManagedPawn(Pawn pawn) =>
            pawn?.Faction == Faction.OfPlayer || IsFriendlyGuest(pawn) || IsPrisoner(pawn);

        private static bool IsPrisoner(Pawn pawn) =>
            pawn?.guest?.IsPrisoner == true && pawn.IsPrisonerOfColony;

        private static bool IsFriendlyGuest(Pawn pawn)
        {
            return pawn?.guest != null &&
                   !pawn.guest.IsPrisoner &&
                   pawn.Faction != null &&
                   pawn.Faction != Faction.OfPlayer &&
                   !pawn.Faction.HostileTo(Faction.OfPlayer);
        }

        private static bool IsRobotOrMechanoid(Pawn pawn)
        {
            RaceProperties race = pawn?.RaceProps;
            if (race == null)
                return false;

            if (race.IsMechanoid || race.FleshType == FleshTypeDefOf.Mechanoid)
                return true;

            // Several robot mods implement utility bots as animal-like pawn
            // races so they can reuse training, zoning, and work AI. They do
            // not report the vanilla mechanoid flesh type and previously fell
            // through to the Animals column. Stable def identifiers let those
            // pawns honor the Mechs permission without targeting any one mod.
            return LooksMechanical(pawn.def?.defName) ||
                   LooksMechanical(pawn.kindDef?.defName) ||
                   LooksMechanical(race.FleshType?.defName);
        }

        private static bool LooksMechanical(string identifier)
        {
            if (string.IsNullOrEmpty(identifier))
                return false;

            return identifier.IndexOf("robot", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("mech", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("android", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.IndexOf("droid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   identifier.EndsWith("Bot", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch]
    internal static class WorkGiverPausedArea_HasJob_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            PausedAreaWorkFilter.ScannerMethods(typeof(bool));

        private static void Postfix(ref bool __result, object[] __args)
        {
            if (__result && PausedAreaWorkFilter.ShouldReject(__args))
                __result = false;
        }
    }

    [HarmonyPatch]
    internal static class WorkGiverPausedArea_JobOn_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            PausedAreaWorkFilter.ScannerMethods(typeof(Job));

        private static void Postfix(ref Job __result, object[] __args)
        {
            if (__result != null && PausedAreaWorkFilter.ShouldRejectJob(__result, __args))
                __result = null;
        }
    }

    /// <summary>
    /// Child learning and observation jobs come from general think nodes rather
    /// than WorkGiver_Scanner. Rejecting the result here lets the priority tree
    /// continue to another safe activity instead of sending a child into an
    /// active managed work area or replacing the job with an idle wait.
    /// </summary>
    [HarmonyPatch(typeof(ThinkNode_JobGiver), "TryIssueJobPackage")]
    internal static class ThinkNodeJobGiver_ProtectedArea_Patch
    {
        private static void Postfix(ThinkNode_JobGiver __instance, Pawn pawn, ref ThinkResult __result)
        {
            if (!__result.IsValid)
                return;

            if (PausedAreaWorkFilter.TryRedirectWanderingJob(pawn, __result.Job, __instance))
                return;

            if (PausedAreaWorkFilter.TryRedirectProtectedChildJob(pawn, __result.Job))
                return;

            if (PausedAreaWorkFilter.ShouldRejectPausedAreaJob(pawn, __result.Job) ||
                PausedAreaWorkFilter.ShouldRejectHaulingJob(pawn, __result.Job) ||
                PausedAreaWorkFilter.ShouldRejectProtectedAreaJob(pawn, __result.Job) ||
                PausedAreaWorkFilter.ShouldRejectWanderingJob(pawn, __result.Job, __instance))
            {
                __result = default;
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Rules;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Detection
{
    public static class RuleEvaluator
    {
        public static ApparelRule MatchingRule(Pawn pawn, Job job)
        {
            return MatchingRules(pawn, job).FirstOrDefault();
        }

        public static List<ApparelRule> MatchingRules(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Map == null ||
                !PawnAccessClassifier.IsApparelEligibleHuman(pawn) || pawn.Drafted)
                return new List<ApparelRule>();

            var component = AutomaticOutfitManagerGameComponent.Current;
            if (component?.Rules == null)
                return new List<ApparelRule>();

            // A target may be covered by an outer work area and one or more
            // nested areas. All of their safety requirements apply. Preserve
            // rule order so existing saves remain deterministic.
            return component.Rules
                .Where(rule => MatchesRule(pawn, job, rule))
                .ToList();
        }

        public static bool MatchesRule(Pawn pawn, Job job, ApparelRule rule)
        {
            return pawn != null &&
                   job != null &&
                   pawn.Map != null &&
                   PawnAccessClassifier.IsApparelEligibleHuman(pawn) &&
                   !pawn.Drafted &&
                   rule != null &&
                   rule.Enabled &&
                   !rule.WorkAreaPaused &&
                   rule.Area != null &&
                   rule.Area.Map == pawn.Map &&
                   JobTargetsArea(job, rule.Area);
        }

        public static List<ThingDef> MissingRequiredApparel(Pawn pawn, ApparelRule rule)
        {
            if (pawn?.apparel == null || rule?.RequiredApparel == null)
                return new List<ThingDef>();

            return rule.RequiredApparel
                .Where(def => def != null && !pawn.apparel.WornApparel.Any(a => a.def == def))
                .Distinct()
                .ToList();
        }

        public static bool HasMissingRequiredApparel(Pawn pawn, ApparelRule rule)
        {
            if (pawn?.apparel == null || rule?.RequiredApparel == null)
                return false;

            foreach (ThingDef required in rule.RequiredApparel)
            {
                if (required == null)
                    continue;

                bool worn = false;
                foreach (Apparel apparel in pawn.apparel.WornApparel)
                {
                    if (apparel?.def == required)
                    {
                        worn = true;
                        break;
                    }
                }

                if (!worn)
                    return true;
            }

            return false;
        }

        public static bool RuleCanApplyToPawn(Pawn pawn, ApparelRule rule)
        {
            if (pawn == null || pawn.RaceProps?.Humanlike != true || pawn.apparel == null ||
                rule?.RequiredApparel == null)
                return false;

            foreach (ThingDef def in rule.RequiredApparel)
            {
                if (def?.apparel == null)
                    continue;
                if (!ApparelUtility.HasPartsToWear(pawn, def) ||
                    (def.apparel.developmentalStageFilter & pawn.DevelopmentalStage) == 0)
                    return false;
            }

            return true;
        }

        public static bool JobTargetsArea(Job job, Area area)
        {
            if (job == null)
                return false;

            return TargetInside(job.targetA, area) ||
                   TargetInside(job.targetB, area) ||
                   TargetInside(job.targetC, area) ||
                   TargetsInside(job.targetQueueA, area) ||
                   TargetsInside(job.targetQueueB, area);
        }

        private static bool TargetsInside(
            IEnumerable<LocalTargetInfo> targets, Area area) =>
            targets != null && targets.Any(target => TargetInside(target, area));

        private static bool TargetInside(LocalTargetInfo target, Area area)
        {
            if (!target.IsValid || area == null)
                return false;

            if (target.HasThing)
            {
                var thing = target.Thing;
                if (thing == null || thing.MapHeld != area.Map)
                    return false;

                // Work-area membership follows the target and its occupied
                // footprint. Do not classify a job by every possible adjacent
                // interaction cell: a frame just outside a nested area can have
                // one candidate standing cell inside it even when RimWorld
                // chooses another, producing false nested-rule status and
                // unnecessary outfitting. Actual entry and transit remain
                // protected by the path-cell safety patch.
                if (CellInside(thing.PositionHeld, area))
                    return true;

                // Held or carried targets can have MapHeld/PositionHeld through
                // their holder while their direct Map is null. RimWorld's
                // InteractionCells calculation requires a spawned thing/map.
                if (!thing.Spawned || thing.Map != area.Map)
                    return false;

                return GenAdj.CellsOccupiedBy(thing)
                    .Any(cell => CellInside(cell, area));
            }

            return CellInside(target.Cell, area);
        }

        private static bool CellInside(IntVec3 cell, Area area) =>
            area?.Map != null && cell.IsValid && cell.InBounds(area.Map) && area[cell];
    }
}

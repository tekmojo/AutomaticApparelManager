using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Core;
using AutomaticApparel.Rules;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Detection
{
    public static class RuleEvaluator
    {
        public static ApparelRule MatchingRule(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Map == null || !pawn.IsColonist ||
                pawn.RaceProps?.Humanlike != true || pawn.apparel == null || pawn.Drafted)
                return null;

            var component = AutomaticApparelGameComponent.Current;
            if (component?.Rules == null)
                return null;

            return component.Rules.FirstOrDefault(rule =>
                RuleCanApplyToPawn(pawn, rule) && MatchesRule(pawn, job, rule));
        }

        public static bool MatchesRule(Pawn pawn, Job job, ApparelRule rule)
        {
            return pawn != null &&
                   job != null &&
                   pawn.Map != null &&
                   pawn.IsColonist &&
                   pawn.RaceProps?.Humanlike == true &&
                   pawn.apparel != null &&
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

                // Work jobs commonly target a building's anchor cell while the
                // pawn performs the job from an adjacent interaction cell. Check
                // both, plus the full footprint for multi-cell workstations.
                if (CellInside(thing.PositionHeld, area))
                    return true;

                // Held or carried targets can have MapHeld/PositionHeld through
                // their holder while their direct Map is null. RimWorld's
                // InteractionCells calculation requires a spawned thing/map.
                if (!thing.Spawned || thing.Map != area.Map)
                    return false;

                if (GenAdj.CellsOccupiedBy(thing).Any(cell => CellInside(cell, area)))
                    return true;

                List<IntVec3> interactionCells = thing.InteractionCells;
                return interactionCells != null &&
                       interactionCells.Any(cell => CellInside(cell, area));
            }

            return CellInside(target.Cell, area);
        }

        private static bool CellInside(IntVec3 cell, Area area) =>
            area?.Map != null && cell.IsValid && cell.InBounds(area.Map) && area[cell];
    }
}

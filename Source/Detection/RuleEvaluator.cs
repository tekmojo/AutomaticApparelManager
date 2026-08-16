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
            if (pawn == null || job == null || pawn.Map == null || !pawn.IsColonist || pawn.Drafted)
                return null;

            var component = AutomaticApparelGameComponent.Current;
            if (component?.Rules == null)
                return null;

            return component.Rules.FirstOrDefault(rule =>
                rule != null &&
                rule.Enabled &&
                rule.Area != null &&
                rule.Area.Map == pawn.Map &&
                JobTargetsArea(job, rule.Area));
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

        private static bool JobTargetsArea(Job job, Area area)
        {
            return TargetInside(job.targetA, area) ||
                   TargetInside(job.targetB, area) ||
                   TargetInside(job.targetC, area);
        }

        private static bool TargetInside(LocalTargetInfo target, Area area)
        {
            if (!target.IsValid || area == null)
                return false;

            IntVec3 cell;
            if (target.HasThing)
            {
                var thing = target.Thing;
                if (thing == null || thing.MapHeld != area.Map)
                    return false;
                cell = thing.PositionHeld;
            }
            else
            {
                cell = target.Cell;
            }

            return cell.IsValid && cell.InBounds(area.Map) && area[cell];
        }
    }
}

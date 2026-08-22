using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Rules;
using RimWorld;
using Verse;

namespace AutomaticOutfitManager.Detection
{
    public sealed class ApparelConflict
    {
        public ThingDef First;
        public ApparelRule FirstRule;
        public ThingDef Second;
        public ApparelRule SecondRule;

        public string Label =>
            $"{First.LabelCap} ({FirstRule.Name}) conflicts with {Second.LabelCap} ({SecondRule.Name})";
    }

    public static class ApparelCompatibility
    {
        public static List<ApparelRule> OverlappingRules(ApparelRule rule)
        {
            var rules = AutomaticOutfitManagerGameComponent.Current?.Rules;
            if (rule?.Area == null || rules == null)
                return new List<ApparelRule> { rule }.Where(item => item != null).ToList();

            return rules.Where(candidate => candidate?.Enabled == true &&
                    candidate.Area?.Map == rule.Area.Map && AreasOverlap(rule.Area, candidate.Area))
                .ToList();
        }

        public static List<ApparelRule> PausedOverlappingRules(ApparelRule rule)
        {
            if (rule == null)
                return new List<ApparelRule>();

            return OverlappingRules(rule)
                .Where(candidate => candidate != null &&
                    candidate.Id != rule.Id && candidate.WorkAreaPaused)
                .ToList();
        }

        public static ApparelConflict FindConflict(
            IEnumerable<ApparelRule> rules, BodyDef body = null)
        {
            body ??= BodyDefOf.Human;
            var requirements = (rules ?? Enumerable.Empty<ApparelRule>())
                .Where(rule => rule != null)
                .SelectMany(rule => (rule.RequiredApparel ?? new List<ThingDef>())
                    .Where(def => def?.apparel != null)
                    .Select(def => (Def: def, Rule: rule)))
                .GroupBy(item => item.Def)
                .Select(group => group.First())
                .ToList();

            for (int i = 0; i < requirements.Count; i++)
            {
                for (int j = i + 1; j < requirements.Count; j++)
                {
                    if (!ApparelUtility.CanWearTogether(
                            requirements[i].Def, requirements[j].Def, body))
                    {
                        return new ApparelConflict
                        {
                            First = requirements[i].Def,
                            FirstRule = requirements[i].Rule,
                            Second = requirements[j].Def,
                            SecondRule = requirements[j].Rule
                        };
                    }
                }
            }

            return null;
        }

        public static ApparelConflict FindConflictIfAdded(ApparelRule rule, ThingDef candidate)
        {
            return FindConflictIfAdded(rule, candidate, OverlappingRules(rule));
        }

        public static ApparelConflict FindConflictIfAdded(
            ApparelRule rule, ThingDef candidate, IEnumerable<ApparelRule> overlappingRules)
        {
            if (rule == null || candidate == null)
                return null;

            bool added = !rule.RequiredApparel.Contains(candidate);
            if (added)
                rule.RequiredApparel.Add(candidate);
            try
            {
                return FindConflict(overlappingRules);
            }
            finally
            {
                if (added)
                    rule.RequiredApparel.Remove(candidate);
            }
        }

        private static bool AreasOverlap(Area first, Area second)
        {
            if (first == null || second == null || first.Map != second.Map)
                return false;
            if (ReferenceEquals(first, second))
                return true;

            bool firstIsSmaller = first.ActiveCells.Count() <= second.ActiveCells.Count();
            IEnumerable<IntVec3> smaller = firstIsSmaller ? first.ActiveCells : second.ActiveCells;
            Area other = firstIsSmaller ? second : first;
            return smaller.Any(cell => other[cell]);
        }
    }
}

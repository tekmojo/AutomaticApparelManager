using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Core;
using AutomaticOutfitManager.Rules;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Detection
{
    public static class UnavailableWorkRegistry
    {
        private sealed class Entry
        {
            public string RuleId;
            public int UntilTick;
        }

        private static readonly Dictionary<int, List<Entry>> Entries =
            new Dictionary<int, List<Entry>>();

        public static void Block(Pawn pawn, ApparelRule rule, int ticks = 1200)
        {
            if (pawn == null || rule == null)
                return;

            int now = Find.TickManager?.TicksGame ?? 0;
            if (!Entries.TryGetValue(pawn.thingIDNumber, out List<Entry> pawnEntries))
            {
                pawnEntries = new List<Entry>();
                Entries[pawn.thingIDNumber] = pawnEntries;
            }

            Entry entry = pawnEntries.FirstOrDefault(item => item.RuleId == rule.Id);
            if (entry == null)
                pawnEntries.Add(new Entry { RuleId = rule.Id, UntilTick = now + ticks });
            else
                entry.UntilTick = now + ticks;
        }

        public static void Clear(Pawn pawn, IEnumerable<ApparelRule> rules)
        {
            if (pawn == null || rules == null ||
                !Entries.TryGetValue(pawn.thingIDNumber, out List<Entry> pawnEntries))
                return;

            var ids = new HashSet<string>(rules.Where(rule => rule != null).Select(rule => rule.Id));
            pawnEntries.RemoveAll(entry => ids.Contains(entry.RuleId));
            if (pawnEntries.Count == 0)
                Entries.Remove(pawn.thingIDNumber);
        }

        public static bool ShouldReject(Pawn pawn, Job job)
        {
            if (pawn?.Map == null || job == null ||
                !Entries.TryGetValue(pawn.thingIDNumber, out List<Entry> pawnEntries))
                return false;

            int now = Find.TickManager?.TicksGame ?? 0;
            pawnEntries.RemoveAll(entry => entry.UntilTick <= now);
            if (pawnEntries.Count == 0)
            {
                Entries.Remove(pawn.thingIDNumber);
                return false;
            }

            AutomaticOutfitManagerGameComponent component = AutomaticOutfitManagerGameComponent.Current;
            return pawnEntries.Any(entry =>
            {
                ApparelRule rule = component?.RuleById(entry.RuleId);
                return rule?.Enabled == true && rule.Area?.Map == pawn.Map &&
                       RuleEvaluator.JobTargetsArea(job, rule.Area);
            });
        }
    }
}

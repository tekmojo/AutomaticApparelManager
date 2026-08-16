using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutomaticApparel.Rules
{
    public sealed class ApparelRule : IExposable
    {
        public string Name = "New Apparel Rule";
        public bool Enabled = true;
        public Area Area;
        public List<ThingDef> RequiredApparel = new List<ThingDef>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Name, "name", "New Apparel Rule");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_References.Look(ref Area, "area");
            Scribe_Collections.Look(ref RequiredApparel, "requiredApparel", LookMode.Def);
            RequiredApparel ??= new List<ThingDef>();
        }

        public string ApparelSummary => RequiredApparel.Count == 0
            ? "None"
            : string.Join(", ", RequiredApparel.Where(d => d != null).Select(d => d.LabelCap.ToString()));
    }
}

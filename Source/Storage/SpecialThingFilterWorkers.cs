using Verse;

namespace AutomaticApparel.Storage
{
    public sealed class SpecialThingFilterWorker_AutomaticApparel : SpecialThingFilterWorker
    {
        public override bool Matches(Thing thing) => AutomaticApparelClassifier.Matches(thing);

        public override bool CanEverMatch(ThingDef def) => def?.apparel != null;
    }

    public sealed class SpecialThingFilterWorker_NonAutomaticApparel : SpecialThingFilterWorker
    {
        public override bool Matches(Thing thing) =>
            thing?.def?.apparel != null && !AutomaticApparelClassifier.Matches(thing);

        public override bool CanEverMatch(ThingDef def) => def?.apparel != null;
    }
}

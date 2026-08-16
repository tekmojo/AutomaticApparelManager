using System.Collections.Generic;
using AutomaticApparel.Rules;
using Verse;

namespace AutomaticApparel.Core
{
    public sealed class AutomaticApparelGameComponent : GameComponent
    {
        public List<ApparelRule> Rules = new List<ApparelRule>();

        public AutomaticApparelGameComponent(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Rules, "automaticApparelRules", LookMode.Deep);
            Rules ??= new List<ApparelRule>();
        }

        public static AutomaticApparelGameComponent Current =>
            Verse.Current.Game?.GetComponent<AutomaticApparelGameComponent>();
    }
}

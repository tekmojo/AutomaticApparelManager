using System.Linq;
using AutomaticApparel.Core;
using RimWorld;
using Verse;

namespace AutomaticApparel.Storage
{
    public static class AutomaticApparelClassifier
    {
        public static bool Matches(ThingDef def)
        {
            if (def?.apparel == null)
                return false;

            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            return component != null && component.Rules.Any(rule =>
                rule != null &&
                rule.Enabled &&
                rule.RequiredApparel != null &&
                rule.RequiredApparel.Contains(def));
        }

        public static bool Matches(Thing thing)
        {
            if (!(thing is Apparel apparel))
                return false;

            AutomaticApparelGameComponent component = AutomaticApparelGameComponent.Current;
            if (component == null)
                return false;

            if (Matches(apparel.def))
                return true;

            if (component.IsManagedApparel(apparel))
                return true;

            return component.PawnStates.Any(state =>
                state != null &&
                ((state.OriginalApparel?.Contains(apparel) ?? false) ||
                 (state.AutomaticApparel?.Contains(apparel) ?? false)));
        }
    }
}

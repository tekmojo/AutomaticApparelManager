using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AutomaticOutfitManager.Rules
{
    public sealed class ApparelRule : IExposable
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "New Apparel Rule";
        public bool Enabled = true;
        public bool UiCollapsed;
        public bool WorkAreaPaused;
        public bool AllowColonistWork = true;
        public bool AllowRobotWork = true;
        public bool AllowAnimalWork = true;
        public bool AllowGuestWork = true;
        public bool AllowSlaveWork = true;
        public bool AllowPrisonerWork;
        public bool AllowColonistHauling = true;
        public bool AllowRobotHauling = true;
        public bool AllowAnimalHauling = true;
        public bool AllowGuestHauling = true;
        public bool AllowSlaveHauling = true;
        public bool AllowPrisonerHauling;
        public bool AllowColonistWandering = true;
        public bool AllowRobotWandering = true;
        public bool AllowAnimalWandering = true;
        public bool AllowGuestWandering = true;
        public bool AllowSlaveWandering = true;
        public bool AllowPrisonerWandering;
        public int ReturnTaskBuffer;
        public bool AllowChildWorkWatching;
        public Area Area;
        public Area ChangingArea;
        public List<ThingDef> RequiredApparel = new List<ThingDef>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name", "New Apparel Rule");
            Scribe_Values.Look(ref Enabled, "enabled", true);
            Scribe_Values.Look(ref UiCollapsed, "uiCollapsed", false);
            Scribe_Values.Look(ref WorkAreaPaused, "workAreaPaused", false);
            Scribe_Values.Look(ref AllowColonistWork, "allowColonistWork", true);
            Scribe_Values.Look(ref AllowRobotWork, "allowRobotWork", true);
            Scribe_Values.Look(ref AllowAnimalWork, "allowAnimalWork", true);
            Scribe_Values.Look(ref AllowGuestWork, "allowGuestWork", true);
            Scribe_Values.Look(ref AllowSlaveWork, "allowSlaveWork", true);
            Scribe_Values.Look(ref AllowPrisonerWork, "allowPrisonerWork", false);
            Scribe_Values.Look(ref AllowColonistHauling, "allowColonistHauling", true);
            Scribe_Values.Look(ref AllowRobotHauling, "allowRobotHauling", true);
            Scribe_Values.Look(ref AllowAnimalHauling, "allowAnimalHauling", true);
            Scribe_Values.Look(ref AllowGuestHauling, "allowGuestHauling", true);
            Scribe_Values.Look(ref AllowSlaveHauling, "allowSlaveHauling", true);
            Scribe_Values.Look(ref AllowPrisonerHauling, "allowPrisonerHauling", false);
            Scribe_Values.Look(ref AllowColonistWandering, "allowColonistWandering", true);
            Scribe_Values.Look(ref AllowRobotWandering, "allowRobotWandering", true);
            Scribe_Values.Look(ref AllowAnimalWandering, "allowAnimalWandering", true);
            Scribe_Values.Look(ref AllowGuestWandering, "allowGuestWandering", true);
            Scribe_Values.Look(ref AllowSlaveWandering, "allowSlaveWandering", true);
            Scribe_Values.Look(ref AllowPrisonerWandering, "allowPrisonerWandering", false);
            Scribe_Values.Look(ref ReturnTaskBuffer, "returnTaskBuffer", 0);
            Scribe_Values.Look(ref AllowChildWorkWatching, "allowChildWorkWatching", false);
            Scribe_References.Look(ref Area, "area");
            Scribe_References.Look(ref ChangingArea, "changingArea");
            Scribe_Collections.Look(ref RequiredApparel, "requiredApparel", LookMode.Def);
            RequiredApparel ??= new List<ThingDef>();
            ReturnTaskBuffer = System.Math.Max(0, System.Math.Min(20, ReturnTaskBuffer));

            if (string.IsNullOrEmpty(Id))
                Id = Guid.NewGuid().ToString("N");
        }

        public string ApparelSummary => RequiredApparel.Count == 0
            ? "None"
            : string.Join(", ", RequiredApparel.Where(d => d != null).Select(d => d.LabelCap.ToString()));
    }
}

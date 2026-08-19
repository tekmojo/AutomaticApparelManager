using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Core;
using AutomaticApparel.Detection;
using AutomaticApparel.Rules;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutomaticApparel.UI
{
    public sealed class MainRulesWindow : MainTabWindow
    {
        private const float ReadinessCacheSeconds = 1f;
        private const float ActivityCacheSeconds = 0.5f;
        private Vector2 scrollPosition;
        private readonly Dictionary<string, CachedRuleReadiness> readinessCache =
            new Dictionary<string, CachedRuleReadiness>();
        private readonly Dictionary<string, CachedRuleActivity> activityCache =
            new Dictionary<string, CachedRuleActivity>();

        private sealed class CachedRuleReadiness
        {
            public float CreatedAt;
            public string Signature;
            public string Text;
            public string GearSummary;
            public Color Color;
        }

        private sealed class CachedActivityEntry
        {
            public Pawn Pawn;
            public string Report;
        }

        private sealed class CachedRuleActivity
        {
            public float CreatedAt;
            public Map Map;
            public readonly List<CachedActivityEntry> Haulers =
                new List<CachedActivityEntry>();
            public readonly List<CachedActivityEntry> Wanderers =
                new List<CachedActivityEntry>();
        }

        public override Vector2 RequestedTabSize => new Vector2(760f, 620f);

        public override void DoWindowContents(Rect inRect)
        {
            var component = AutomaticApparelGameComponent.Current;
            if (component == null)
            {
                Widgets.Label(inRect, "No active game.");
                return;
            }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), "Automatic Apparel Manager");
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "Choose where an outfit or personal protective equipment (PPE) is needed and where pawns should change.");
            y += 32f;

            Rect newRuleRect = new Rect(inRect.x, y, 130f, 30f);
            if (Widgets.ButtonText(newRuleRect, "Add rule"))
                component.Rules.Add(new ApparelRule());
            TooltipHandler.TipRegion(newRuleRect, "Create a new automatic apparel rule for this save. Examples: radiation work, freezer clothing, firefighting gear, cleanroom apparel, or uniforms.");
            Rect manageAreasRect = new Rect(inRect.x + 140f, y, 150f, 30f);
            if (Widgets.ButtonText(manageAreasRect, "Edit map areas"))
                ShowManageAreas();
            TooltipHandler.TipRegion(manageAreasRect, "Create, rename, or edit the areas used by Work area and Locker room. Examples: Reactor Room, Freezer, Hospital Cleanroom, or North Locker Room.");
            y += 40f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y + inRect.y);
            float viewHeight = Mathf.Max(outRect.height,
                component.Rules.Sum(rule => RuleHeight(rule, component) + 10f) + 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < component.Rules.Count; i++)
            {
                float ruleHeight = RuleHeight(component.Rules[i], component);
                DrawRule(component.Rules[i], i, new Rect(0f, rowY, viewRect.width, ruleHeight), component);
                rowY += ruleHeight + 10f;
            }
            Widgets.EndScrollView();
        }

        private void DrawRule(ApparelRule rule, int index, Rect rect, AutomaticApparelGameComponent component)
        {
            Widgets.DrawMenuSection(rect);
            float x = rect.x + 10f;
            float y = rect.y + 8f;
            float width = rect.width - 20f;

            Rect enabledRect = new Rect(x, y, 90f, 24f);
            Widgets.CheckboxLabeled(enabledRect, "Active", ref rule.Enabled);
            TooltipHandler.TipRegion(enabledRect, "Turn this rule on or off without deleting its settings. Recall commands do not change this setting. Example: disable a seasonal winter-clothing rule during summer.");
            Rect ruleNameRect = new Rect(x + 100f, y, width - 272f, 26f);
            rule.Name = Widgets.TextField(ruleNameRect, rule.Name ?? "");
            TooltipHandler.TipRegion(ruleNameRect, "Give this rule a recognizable name. Examples: Radiation Lab, Freezer Gear, Fire Crew, Cleanroom, or Guard Uniform.");
            Rect collapseRect = new Rect(rect.xMax - 164f, y, 76f, 26f);
            bool collapseChanged = false;
            if (Widgets.ButtonText(collapseRect, rule.UiCollapsed ? "Expand" : "Collapse"))
            {
                rule.UiCollapsed = !rule.UiCollapsed;
                collapseChanged = true;
            }
            TooltipHandler.TipRegion(collapseRect,
                rule.UiCollapsed
                    ? "Expand this rule to show and edit all settings and activity."
                    : "Collapse this rule to a compact summary. The rule remains active and its settings are preserved.");
            Rect deleteRect = new Rect(rect.xMax - 82f, y, 72f, 26f);
            if (Widgets.ButtonText(deleteRect, "Delete"))
            {
                component.Rules.RemoveAt(index);
                return;
            }
            TooltipHandler.TipRegion(deleteRect, "Permanently remove this rule from the current save.");

            if (collapseChanged)
                return;

            if (rule.UiCollapsed)
            {
                CachedRuleReadiness compactReadiness = RuleReadiness(rule, component);
                y += 34f;
                string area = rule.Area?.Label ?? "No work area";
                Widgets.Label(new Rect(x, y, 100f, 22f), "Summary:");
                Widgets.Label(new Rect(x + 100f, y, width - 430f, 22f), area);
                Color compactPreviousColor = GUI.color;
                GUI.color = compactReadiness.Color;
                Widgets.Label(new Rect(rect.xMax - 320f, y, 190f, 22f), compactReadiness.Text);
                GUI.color = compactPreviousColor;
                TooltipHandler.TipRegion(new Rect(x, y, width, 22f),
                    $"Work area: {area}\n{compactReadiness.GearSummary}\nReadiness: {compactReadiness.Text}");

                Rect compactRecallRect = new Rect(rect.xMax - 120f, y - 1f, 110f, 24f);
                bool compactPreviousEnabled = GUI.enabled;
                GUI.enabled = rule.Area != null;
                if (Widgets.ButtonText(compactRecallRect,
                        rule.WorkAreaPaused ? "Resume work" : "Recall all"))
                {
                    RecallAllOrResume(rule, component);
                }
                GUI.enabled = compactPreviousEnabled;
                TooltipHandler.TipRegion(compactRecallRect,
                    rule.WorkAreaPaused
                        ? "Resume ordinary work in this area."
                        : "Pause ordinary work and recall all current workers to the locker room.");
                return;
            }

            y += 34f;
            Rect workLabelRect = new Rect(x, y + 4f, 100f, 24f);
            Widgets.Label(workLabelRect, "Work area:");
            TooltipHandler.TipRegion(workLabelRect, "Jobs located in this area require the configured outfit or personal protective equipment (PPE). Examples: reactor rooms, freezers, hospitals, workshops, or defensive positions.");
            string areaLabel = rule.Area?.Label ?? "Choose work area...";
            Rect workButtonRect = new Rect(x + 100f, y, 300f, 28f);
            if (Widgets.ButtonText(workButtonRect, areaLabel))
                ShowAreaMenu(rule);
            if (rule.Area != null && Mouse.IsOver(workButtonRect))
                rule.Area.MarkForDraw();
            TooltipHandler.TipRegion(workButtonRect, "Select the map area where this outfit or personal protective equipment (PPE) should be worn. For example, select a Freezer area for parkas and warm hats.");

            y += 34f;
            const float permissionLabelWidth = 96f;
            const float permissionColumnWidth = 83f;
            string[] permissionHeaders =
                { "All", "Colonists", "Mechs", "Animals", "Guests", "Slaves", "Prisoners" };
            string[] permissionHeaderTips =
            {
                "Bulk control for every pawn group in this row. It is checked only when every individual group is allowed.",
                "Player colonists, including children. Child work watching is controlled separately below.",
                "Player-controlled mechanoids and compatible robot pawns.",
                "Tamed or player-owned animals.",
                "Friendly visiting pawns who are not members of the colony.",
                "Player-owned slaves.",
                "Prisoners, including compatible prison-labor systems."
            };
            TextAnchor previousAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int column = 0; column < permissionHeaders.Length; column++)
            {
                Rect headerRect = new Rect(
                    x + permissionLabelWidth + column * permissionColumnWidth,
                    y, permissionColumnWidth, 22f);
                Widgets.Label(headerRect, permissionHeaders[column]);
                TooltipHandler.TipRegion(headerRect, permissionHeaderTips[column]);
            }
            Text.Anchor = previousAnchor;

            y += 22f;
            Rect haulingLabelRect = new Rect(x, y + 2f, permissionLabelWidth, 24f);
            Widgets.Label(haulingLabelRect, "Hauling:");
            bool allHauling = AllHaulingAllowed(rule);
            bool previousAllHauling = allHauling;
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 0, ref allHauling,
                "Allow or block hauling for every listed group. Enabling this enables every group; disabling it disables every group.");
            if (allHauling != previousAllHauling)
                SetAllHauling(rule, allHauling);
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 1, ref rule.AllowColonistHauling,
                "Allow colonists, including children, to haul into, out of, or through this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 2, ref rule.AllowRobotHauling,
                "Allow player-controlled mechanoids and compatible robots to haul into, out of, or through this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 3, ref rule.AllowAnimalHauling,
                "Allow trained or tamed animals to haul into, out of, or through this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 4, ref rule.AllowGuestHauling,
                "Allow friendly guests to haul into, out of, or through this work area when their guest system permits hauling.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 5, ref rule.AllowSlaveHauling,
                "Allow player-owned slaves to haul into, out of, or through this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 6, ref rule.AllowPrisonerHauling,
                "Allow prisoners to haul into, out of, or through this work area when vanilla or modded prison labor permits hauling.");
            TooltipHandler.TipRegion(haulingLabelRect,
                "Choose which groups may haul into or out of this work area. Humanlike pawns must still be able to wear configured work gear. Prisoners are supported when vanilla or modded prison labor assigns hauling. Hostiles are never managed.");

            y += 28f;
            Rect wanderingLabelRect = new Rect(x, y + 2f, permissionLabelWidth, 24f);
            Widgets.Label(wanderingLabelRect, "Wandering:");
            bool allWandering = AllWanderingAllowed(rule);
            bool previousAllWandering = allWandering;
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 0, ref allWandering,
                "Allow or block autonomous wandering for every listed group. Enabling this enables every group; disabling it disables every group.");
            if (allWandering != previousAllWandering)
                SetAllWandering(rule, allWandering);
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 1, ref rule.AllowColonistWandering,
                "Allow colonists, including children, to choose autonomous wandering destinations in this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 2, ref rule.AllowRobotWandering,
                "Allow player-controlled mechanoids and compatible robots to choose autonomous wandering destinations in this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 3, ref rule.AllowAnimalWandering,
                "Allow tamed or player-owned animals to wander through or choose destinations in this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 4, ref rule.AllowGuestWandering,
                "Allow friendly guests to choose autonomous wandering destinations in this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 5, ref rule.AllowSlaveWandering,
                "Allow player-owned slaves to choose autonomous wandering destinations in this work area.");
            DrawPermissionCheckbox(x, y, permissionLabelWidth, permissionColumnWidth, 6, ref rule.AllowPrisonerWandering,
                "Allow prisoners to choose autonomous wandering destinations in this work area.");
            TooltipHandler.TipRegion(wanderingLabelRect,
                "Choose which groups may select autonomous wandering destinations in this area. This does not authorize assigned work, hauling, drafted movement, or direct player orders.");

            y += 28f;
            Rect childWatchingLabelRect = new Rect(x, y + 2f, 100f, 24f);
            Widgets.Label(childWatchingLabelRect, "Children:");
            Rect childWatchingRect = new Rect(x + 100f, y, 230f, 24f);
            DrawLeadingCheckbox(childWatchingRect, "Allow work watching", ref rule.AllowChildWorkWatching);
            TooltipHandler.TipRegion(new Rect(x, y, 330f, 26f),
                "Permit children to enter specifically to watch an adult work for learning. Leave disabled for hazardous areas; enable only for safe workshops or similar spaces. For hauling and wandering, children follow the Colonists column above.");

            y += 30f;
            Rect lockerLabelRect = new Rect(x, y + 4f, 100f, 24f);
            Widgets.Label(lockerLabelRect, "Locker room:");
            TooltipHandler.TipRegion(lockerLabelRect, "Optional staging area where pawns change before and after managed work. Examples: a locker room, airlock, changing bay, or equipment closet.");
            string changingAreaLabel = rule.ChangingArea?.Label ?? "No locker room";
            Rect lockerButtonRect = new Rect(x + 100f, y, 300f, 28f);
            if (Widgets.ButtonText(lockerButtonRect, changingAreaLabel))
                ShowChangingAreaMenu(rule);
            if (rule.ChangingArea != null && Mouse.IsOver(lockerButtonRect))
                rule.ChangingArea.MarkForDraw();
            TooltipHandler.TipRegion(lockerButtonRect, "Outfit items or personal protective equipment (PPE) stored here are preferred, but pawns can use matching apparel found elsewhere. After work, pawns return assigned gear only to storage inside this area and restore their saved clothes here. For example, place radiation suits in lockers inside a reactor airlock.");

            y += 34f;
            Rect bufferLabelRect = new Rect(x, y + 4f, 100f, 24f);
            Widgets.Label(bufferLabelRect, "Task buffer:");
            Rect bufferMinusRect = new Rect(x + 100f, y, 32f, 28f);
            if (Widgets.ButtonText(bufferMinusRect, "−"))
                rule.ReturnTaskBuffer = Mathf.Max(0, rule.ReturnTaskBuffer - 1);
            Rect bufferValueRect = new Rect(x + 138f, y + 4f, 110f, 24f);
            Widgets.Label(bufferValueRect, rule.ReturnTaskBuffer == 0
                ? "Immediate"
                : $"{rule.ReturnTaskBuffer} task{(rule.ReturnTaskBuffer == 1 ? "" : "s")}");
            Rect bufferPlusRect = new Rect(x + 254f, y, 32f, 28f);
            bool previousBufferEnabled = GUI.enabled;
            GUI.enabled = rule.ReturnTaskBuffer < 20;
            if (Widgets.ButtonText(bufferPlusRect, "+"))
                rule.ReturnTaskBuffer++;
            GUI.enabled = previousBufferEnabled;
            TooltipHandler.TipRegion(new Rect(bufferLabelRect.x, y, 286f, 28f),
                "Choose how many ordinary tasks a pawn may start after leaving this work area before returning to the locker room and outfitting saved gear. Immediate returns after the first managed job ends. Recall all returns workers immediately. Work requiring a different Automatic Apparel rule also bypasses this buffer.");

            y += 34f;
            Rect gearLabelRect = new Rect(x, y + 4f, 100f, 24f);
            Widgets.Label(gearLabelRect, "Required gear:");
            TooltipHandler.TipRegion(gearLabelRect, "Every outfit item or piece of personal protective equipment (PPE) a pawn should wear before starting work in the Work area. Examples: radiation suit and mask, parka and tuque, firefighter suit, armor, or a uniform.");
            Rect addGearRect = new Rect(x + 100f, y, 160f, 28f);
            if (Widgets.ButtonText(addGearRect, "Choose gear"))
                ShowApparelMenu(rule);
            TooltipHandler.TipRegion(addGearRect, "Search all loaded vanilla and modded apparel and add the items required by this rule. For example, search for radiation, parka, helmet, or uniform.");
            Rect clearGearRect = new Rect(x + 268f, y, 110f, 28f);
            if (Widgets.ButtonText(clearGearRect, "Clear gear"))
                rule.RequiredApparel.Clear();
            TooltipHandler.TipRegion(clearGearRect, "Remove all required gear from this rule.");

            y += 34f;
            CachedRuleReadiness readiness = RuleReadiness(rule, component);
            Rect gearSummaryRect = new Rect(x, y, width, 30f);
            bool previousWordWrap = Text.WordWrap;
            Text.WordWrap = false;
            Widgets.Label(gearSummaryRect, readiness.GearSummary);
            Text.WordWrap = previousWordWrap;
            TooltipHandler.TipRegion(gearSummaryRect,
                $"Unworn work gear currently spawned on this map. Apparel being worn or saved for a specific pawn is not counted. Availability does not guarantee that every item is reachable or currently unreserved.\n\n{readiness.GearSummary}");

            List<State.PawnApparelState> workers = component.PawnStates
                .Where(state => state?.Pawn?.RaceProps?.Humanlike == true &&
                                state.Pawn.apparel != null && state.ActiveRuleId == rule.Id)
                .ToList();
            CachedRuleActivity activity = RuleActivity(rule);
            y += 28f;
            Widgets.Label(new Rect(x, y, 100f, 22f), "Readiness:");
            Color previousColor = GUI.color;
            GUI.color = readiness.Color;
            Widgets.Label(new Rect(x + 100f, y, width - 220f, 22f), readiness.Text);
            GUI.color = previousColor;
            TooltipHandler.TipRegion(new Rect(x, y, width, 22f),
                "Checks whether this rule is enabled, has a work area and required gear, has storage inside its optional locker room, and has each required apparel type available or already in use. Recall all temporarily shows Paused until every recalled worker finishes changing. Active means the basic checks passed; pawns still obey normal reachability and reservation rules.");
            Rect recallRect = new Rect(rect.xMax - 120f, y - 1f, 110f, 24f);
            bool previousEnabled = GUI.enabled;
            GUI.enabled = rule.Area != null;
            if (Widgets.ButtonText(recallRect, rule.WorkAreaPaused ? "Resume work" : "Recall all"))
                RecallAllOrResume(rule, component);
            GUI.enabled = previousEnabled;
            TooltipHandler.TipRegion(recallRect,
                rule.WorkAreaPaused
                    ? "Resume ordinary work in this area. The rule remains active and pawns will outfit work gear again when qualifying jobs are assigned."
                    : "Pause ordinary work in this area and send all current workers back to the locker room to return managed gear and outfit their saved gear. The rule itself remains active. Drafted orders and paths crossing the area remain under normal RimWorld control.");

            y += 28f;
            Widgets.Label(new Rect(x, y, 100f, 22f), "Workers:");
            if (workers.Count == 0)
            {
                Widgets.Label(new Rect(x + 100f, y, width - 100f, 22f), "No active workers in this area");
            }
            else
            {
                for (int workerIndex = 0; workerIndex < workers.Count; workerIndex++)
                {
                    State.PawnApparelState state = workers[workerIndex];
                    string fullStatus = PawnAutomaticApparelStatus.Build(state.Pawn) ?? "Automatic Apparel: Active";
                    string shortStatus = fullStatus.Split('\n')[0].Replace("Automatic Apparel: ", "");
                    float workerY = y + workerIndex * 22f;
                    Rect workerRect = new Rect(x + 100f, workerY, width - 170f, 22f);
                    Widgets.DrawHighlightIfMouseover(workerRect);
                    Widgets.Label(workerRect, $"{state.Pawn.LabelShortCap} — {shortStatus}");
                    if (Widgets.ButtonInvisible(workerRect))
                        CameraJumper.TryJumpAndSelect(state.Pawn);
                    TooltipHandler.TipRegion(workerRect, $"{fullStatus}\n\nClick to select and jump to {state.Pawn.LabelShortCap}.");

                    Rect recallWorkerRect = new Rect(rect.xMax - 70f, workerY, 60f, 22f);
                    if (Widgets.ButtonText(recallWorkerRect, "Recall"))
                        RecallWorker(state);
                    TooltipHandler.TipRegion(recallWorkerRect,
                        $"Send only {state.Pawn.LabelShortCap} back to the locker room to return managed gear and restore saved clothing. This rule remains active for other workers.");
                }
            }

            y += Mathf.Max(1, workers.Count) * 22f + 4f;
            DrawActivityRow("Haulers:", activity.Haulers, x, y, width,
                "Actors currently hauling through or into this work area. This is controlled by the Hauling access row.");
            y += Mathf.Max(1, activity.Haulers.Count) * 22f + 4f;
            DrawActivityRow("Wanderers:", activity.Wanderers, x, y, width,
                "Actors currently wandering in or through this work area. This is controlled by the Wandering access row.");
        }

        private static void DrawActivityRow(
            string label, List<CachedActivityEntry> actors, float x, float y, float width, string tooltip)
        {
            Widgets.Label(new Rect(x, y, 100f, 22f), label);
            if (actors.Count == 0)
            {
                Widgets.Label(new Rect(x + 100f, y, width - 100f, 22f), "None");
                TooltipHandler.TipRegion(new Rect(x, y, width, 22f), tooltip);
                return;
            }

            for (int index = 0; index < actors.Count; index++)
            {
                CachedActivityEntry entry = actors[index];
                Pawn actor = entry.Pawn;
                if (actor == null || actor.Destroyed)
                    continue;
                float actorY = y + index * 22f;
                Rect actorRect = new Rect(x + 100f, actorY, width - 100f, 22f);
                Widgets.DrawHighlightIfMouseover(actorRect);
                Widgets.Label(actorRect,
                    $"{actor.LabelShortCap} — {entry.Report}");
                if (Widgets.ButtonInvisible(actorRect))
                    CameraJumper.TryJumpAndSelect(actor);
                TooltipHandler.TipRegion(actorRect,
                    $"{tooltip} The resolved job report identifies the actual item and destination when RimWorld provides them.\n\nClick to select and jump to {actor.LabelShortCap}.");
            }
        }

        private float RuleHeight(
            ApparelRule rule,
            AutomaticApparelGameComponent component)
        {
            if (rule?.UiCollapsed == true)
                return 70f;

            int workerCount = component.PawnStates.Count(state =>
                state?.Pawn?.RaceProps?.Humanlike == true && state.Pawn.apparel != null &&
                state.ActiveRuleId == rule.Id);
            CachedRuleActivity activity = RuleActivity(rule);
            int haulerCount = activity.Haulers.Count;
            int wandererCount = activity.Wanderers.Count;
            float activityHeight = 8f + Mathf.Max(1, haulerCount) * 22f +
                                   Mathf.Max(1, wandererCount) * 22f;
            return Mathf.Max(368f, 346f + Mathf.Max(1, workerCount) * 22f +
                activityHeight);
        }

        private CachedRuleActivity RuleActivity(ApparelRule rule)
        {
            string key = rule?.Id ?? string.Empty;
            Map map = rule?.Area?.Map;
            float now = Time.realtimeSinceStartup;
            if (activityCache.TryGetValue(key, out CachedRuleActivity cached) &&
                cached.Map == map && now - cached.CreatedAt < ActivityCacheSeconds)
            {
                return cached;
            }

            cached = new CachedRuleActivity
            {
                CreatedAt = now,
                Map = map
            };

            IReadOnlyList<Pawn> pawns = map?.mapPawns?.AllPawnsSpawned;
            if (pawns != null)
            {
                foreach (Pawn pawn in pawns)
                {
                    var job = pawn?.CurJob;
                    if (job == null)
                        continue;

                    bool hauling = Patches.PausedAreaWorkFilter
                        .IsHaulingActivityForRule(pawn, job, rule);
                    bool wandering = !hauling && Patches.PausedAreaWorkFilter
                        .IsWanderingActivityForRule(pawn, job, rule);
                    if (!hauling && !wandering)
                        continue;

                    string report = job.GetReport(pawn);
                    if (string.IsNullOrEmpty(report))
                        report = job.def?.label ?? "Idle";
                    var entry = new CachedActivityEntry
                    {
                        Pawn = pawn,
                        Report = report.CapitalizeFirst()
                    };
                    (hauling ? cached.Haulers : cached.Wanderers).Add(entry);
                }
            }

            activityCache[key] = cached;
            return cached;
        }

        private static bool IsAutomatedUnit(Pawn pawn)
        {
            if (pawn?.RaceProps == null || pawn.RaceProps.Humanlike)
                return false;

            if (pawn.RaceProps.IsMechanoid)
                return true;

            string defName = pawn.def?.defName ?? string.Empty;
            return defName.IndexOf("bot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   defName.IndexOf("robot", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void DrawLeadingCheckbox(Rect rect, string label, ref bool value)
        {
            Widgets.Checkbox(rect.x, rect.y, ref value, 24f);
            Widgets.Label(new Rect(rect.x + 28f, rect.y, rect.width - 28f, rect.height), label);
        }

        private static void DrawPermissionCheckbox(
            float x, float y, float labelWidth, float columnWidth, int column, ref bool value,
            string tooltip)
        {
            Rect cellRect = new Rect(
                x + labelWidth + column * columnWidth, y, columnWidth, 26f);
            float checkboxX = x + labelWidth + column * columnWidth +
                              (columnWidth - 24f) / 2f;
            Widgets.Checkbox(checkboxX, y, ref value, 24f);
            TooltipHandler.TipRegion(cellRect, tooltip);
        }

        private static bool AllHaulingAllowed(ApparelRule rule) =>
            rule.AllowColonistHauling && rule.AllowRobotHauling &&
            rule.AllowAnimalHauling && rule.AllowGuestHauling &&
            rule.AllowSlaveHauling && rule.AllowPrisonerHauling;

        private static void SetAllHauling(ApparelRule rule, bool value)
        {
            rule.AllowColonistHauling = value;
            rule.AllowRobotHauling = value;
            rule.AllowAnimalHauling = value;
            rule.AllowGuestHauling = value;
            rule.AllowSlaveHauling = value;
            rule.AllowPrisonerHauling = value;
        }

        private static bool AllWanderingAllowed(ApparelRule rule) =>
            rule.AllowColonistWandering && rule.AllowRobotWandering &&
            rule.AllowAnimalWandering && rule.AllowGuestWandering &&
            rule.AllowSlaveWandering && rule.AllowPrisonerWandering;

        private static void SetAllWandering(ApparelRule rule, bool value)
        {
            rule.AllowColonistWandering = value;
            rule.AllowRobotWandering = value;
            rule.AllowAnimalWandering = value;
            rule.AllowGuestWandering = value;
            rule.AllowSlaveWandering = value;
            rule.AllowPrisonerWandering = value;
        }

        private static void ToggleWorkPause(
            ApparelRule rule,
            AutomaticApparelGameComponent component)
        {
            rule.WorkAreaPaused = !rule.WorkAreaPaused;
            if (!rule.WorkAreaPaused || rule.Area?.Map == null)
                return;

            List<State.PawnApparelState> areaWorkers = component.PawnStates
                .Where(state => state?.Pawn != null && state.ActiveRuleId == rule.Id)
                .ToList();
            foreach (State.PawnApparelState state in areaWorkers)
                RecallWorker(state);

            // Existing untracked work is enforced by the game component on its
            // next scheduled tick. Do not mutate pawn job trackers from OnGUI:
            // an exception in another mod's job can otherwise abort this window
            // and leave the pause operation only partially applied.
        }

        private static void RecallAllOrResume(
            ApparelRule rule,
            AutomaticApparelGameComponent component)
        {
            if (!rule.WorkAreaPaused)
            {
                foreach (State.PawnApparelState state in component.PawnStates.Where(state =>
                    state?.ActiveRuleId == rule.Id))
                {
                    state.RecallAllRequested = true;
                }
            }

            ToggleWorkPause(rule, component);
        }

        private CachedRuleReadiness RuleReadiness(
            ApparelRule rule,
            AutomaticApparelGameComponent component)
        {
            Map map = Find.CurrentMap;
            bool recallAllInProgress = component.PawnStates.Any(state =>
                state?.ActiveRuleId == rule.Id && state.RecallAllRequested);
            string signature = $"{rule.Enabled}|{rule.WorkAreaPaused}|{recallAllInProgress}|{rule.Area?.GetUniqueLoadID()}|" +
                $"{rule.ChangingArea?.GetUniqueLoadID()}|" +
                string.Join(",", rule.RequiredApparel.Where(def => def != null).Select(def => def.defName));
            if (readinessCache.TryGetValue(rule.Id, out CachedRuleReadiness cached) &&
                cached.Signature == signature &&
                Time.realtimeSinceStartup - cached.CreatedAt < ReadinessCacheSeconds)
            {
                return cached;
            }

            string text;
            Color color;
            Dictionary<ThingDef, int> availableCounts = rule.RequiredApparel
                .Where(def => def != null)
                .Distinct()
                .ToDictionary(def => def, def => AvailableGearCount(def, map, component));
            string gearSummary = availableCounts.Count == 0
                ? "None"
                : string.Join(", ", availableCounts.Select(pair =>
                    $"{pair.Key.LabelCap}: {pair.Value} available"));
            if (!rule.Enabled)
            {
                text = "Paused";
                color = Color.yellow;
            }
            else if (recallAllInProgress)
            {
                text = "Paused";
                color = Color.yellow;
            }
            else if (rule.WorkAreaPaused)
            {
                text = "Paused";
                color = Color.yellow;
            }
            else if (rule.Area == null)
            {
                text = "Missing work area";
                color = Color.yellow;
            }
            else if (rule.RequiredApparel == null || rule.RequiredApparel.Count == 0)
            {
                text = "No required gear selected";
                color = Color.yellow;
            }
            else if (rule.ChangingArea != null && map != null &&
                     rule.ChangingArea.Map == map &&
                     !rule.ChangingArea.ActiveCells.Any(cell => cell.GetSlotGroup(map) != null))
            {
                text = "Locker room has no storage";
                color = Color.yellow;
            }
            else
            {
                List<ThingDef> unavailable = rule.RequiredApparel
                    .Where(def => def != null && availableCounts[def] == 0 &&
                        !RequiredGearInUse(def, map, component))
                    .ToList();
                if (unavailable.Count > 0)
                {
                    text = $"Required gear unavailable: {string.Join(", ", unavailable.Select(def => def.LabelCap.ToString()))}";
                    color = Color.yellow;
                }
                else
                {
                    text = "Active";
                    color = Color.green;
                }
            }

            cached = new CachedRuleReadiness
            {
                CreatedAt = Time.realtimeSinceStartup,
                Signature = signature,
                Text = text,
                GearSummary = gearSummary,
                Color = color
            };
            readinessCache[rule.Id] = cached;
            return cached;
        }

        private static int AvailableGearCount(
            ThingDef def,
            Map map,
            AutomaticApparelGameComponent component)
        {
            if (map?.listerThings == null)
                return 0;

            return map.listerThings.ThingsOfDef(def).Count(thing =>
                thing is Apparel apparel &&
                apparel.Spawned &&
                !apparel.Destroyed &&
                component.SavedPawnFor(apparel) == null);
        }

        private static bool RequiredGearInUse(
            ThingDef def,
            Map map,
            AutomaticApparelGameComponent component)
        {
            return component.PawnStates.Any(state => state?.Pawn?.Map == map &&
                state.Pawn.apparel?.WornApparel.Any(apparel => apparel?.def == def) == true);
        }

        private static void RecallWorker(State.PawnApparelState state)
        {
            if (state?.Pawn == null)
                return;

            AutomaticApparelGameComponent.Current?.RequestRecall(state);
        }

        private static void ShowAreaMenu(ApparelRule rule)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            var options = new List<FloatMenuOption>();
            foreach (Area area in map.areaManager.AllAreas.Where(a => a != null))
            {
                Area captured = area;
                options.Add(new FloatMenuOption(captured.Label, () => rule.Area = captured));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowChangingAreaMenu(ApparelRule rule)
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("No locker room (change wherever needed)", () => rule.ChangingArea = null)
            };
            foreach (Area area in map.areaManager.AllAreas.Where(a => a != null))
            {
                Area captured = area;
                options.Add(new FloatMenuOption(captured.Label, () => rule.ChangingArea = captured));
            }

            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static void ShowManageAreas()
        {
            Map map = Find.CurrentMap;
            if (map != null)
                Find.WindowStack.Add(new Dialog_ManageAreas(map));
        }

        private static void ShowApparelMenu(ApparelRule rule)
        {
            Find.WindowStack.Add(new ApparelSelectionWindow(rule));
        }
    }
}

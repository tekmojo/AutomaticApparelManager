using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Core;
using AutomaticApparel.Rules;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutomaticApparel.UI
{
    public sealed class MainRulesWindow : MainTabWindow
    {
        private Vector2 scrollPosition;

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
            float viewHeight = Mathf.Max(outRect.height, component.Rules.Count * 184f + 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < component.Rules.Count; i++)
            {
                DrawRule(component.Rules[i], i, new Rect(0f, rowY, viewRect.width, 174f), component);
                rowY += 184f;
            }
            Widgets.EndScrollView();
        }

        private static void DrawRule(ApparelRule rule, int index, Rect rect, AutomaticApparelGameComponent component)
        {
            Widgets.DrawMenuSection(rect);
            float x = rect.x + 10f;
            float y = rect.y + 8f;
            float width = rect.width - 20f;

            Rect enabledRect = new Rect(x, y, 90f, 24f);
            Widgets.CheckboxLabeled(enabledRect, "Active", ref rule.Enabled);
            TooltipHandler.TipRegion(enabledRect, "Turn this rule on or off without deleting its settings. For example, disable a seasonal winter-clothing rule during summer.");
            Rect ruleNameRect = new Rect(x + 100f, y, width - 190f, 26f);
            rule.Name = Widgets.TextField(ruleNameRect, rule.Name ?? "");
            TooltipHandler.TipRegion(ruleNameRect, "Give this rule a recognizable name. Examples: Radiation Lab, Freezer Gear, Fire Crew, Cleanroom, or Guard Uniform.");
            Rect deleteRect = new Rect(rect.xMax - 82f, y, 72f, 26f);
            if (Widgets.ButtonText(deleteRect, "Delete"))
            {
                component.Rules.RemoveAt(index);
                return;
            }
            TooltipHandler.TipRegion(deleteRect, "Permanently remove this rule from the current save.");

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
            Rect lockerLabelRect = new Rect(x, y + 4f, 100f, 24f);
            Widgets.Label(lockerLabelRect, "Locker room:");
            TooltipHandler.TipRegion(lockerLabelRect, "Optional staging area where pawns change before and after assigned work. Examples: a locker room, airlock, changing bay, or equipment closet.");
            string changingAreaLabel = rule.ChangingArea?.Label ?? "No locker room";
            Rect lockerButtonRect = new Rect(x + 100f, y, 300f, 28f);
            if (Widgets.ButtonText(lockerButtonRect, changingAreaLabel))
                ShowChangingAreaMenu(rule);
            if (rule.ChangingArea != null && Mouse.IsOver(lockerButtonRect))
                rule.ChangingArea.MarkForDraw();
            TooltipHandler.TipRegion(lockerButtonRect, "Outfit items or personal protective equipment (PPE) stored here are preferred, but pawns can use matching apparel found elsewhere. After work, pawns return assigned gear only to storage inside this area and restore their saved clothes here. For example, place radiation suits in lockers inside a reactor airlock.");

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
            Widgets.Label(new Rect(x, y, width, 30f), rule.ApparelSummary);
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

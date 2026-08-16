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
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f), "Automatic Apparel — Phase 1");
            Text.Font = GameFont.Small;

            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "Area/job-destination rules. Restoration and strict blocking are Phase 2+ features.");
            y += 32f;

            if (Widgets.ButtonText(new Rect(inRect.x, y, 130f, 30f), "New Rule"))
                component.Rules.Add(new ApparelRule());
            y += 40f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - y + inRect.y);
            float viewHeight = Mathf.Max(outRect.height, component.Rules.Count * 150f + 10f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            float rowY = 0f;
            for (int i = 0; i < component.Rules.Count; i++)
            {
                DrawRule(component.Rules[i], i, new Rect(0f, rowY, viewRect.width, 140f), component);
                rowY += 150f;
            }
            Widgets.EndScrollView();
        }

        private static void DrawRule(ApparelRule rule, int index, Rect rect, AutomaticApparelGameComponent component)
        {
            Widgets.DrawMenuSection(rect);
            float x = rect.x + 10f;
            float y = rect.y + 8f;
            float width = rect.width - 20f;

            Widgets.CheckboxLabeled(new Rect(x, y, 90f, 24f), "Enabled", ref rule.Enabled);
            rule.Name = Widgets.TextField(new Rect(x + 100f, y, width - 190f, 26f), rule.Name ?? "");
            if (Widgets.ButtonText(new Rect(rect.xMax - 82f, y, 72f, 26f), "Delete"))
            {
                component.Rules.RemoveAt(index);
                return;
            }

            y += 34f;
            Widgets.Label(new Rect(x, y + 4f, 80f, 24f), "Area:");
            string areaLabel = rule.Area?.Label ?? "Select area...";
            if (Widgets.ButtonText(new Rect(x + 80f, y, 240f, 28f), areaLabel))
                ShowAreaMenu(rule);

            y += 34f;
            Widgets.Label(new Rect(x, y + 4f, 80f, 24f), "Apparel:");
            if (Widgets.ButtonText(new Rect(x + 80f, y, 160f, 28f), "Add apparel"))
                ShowApparelMenu(rule);
            if (Widgets.ButtonText(new Rect(x + 248f, y, 110f, 28f), "Clear"))
                rule.RequiredApparel.Clear();

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

        private static void ShowApparelMenu(ApparelRule rule)
        {
            Find.WindowStack.Add(new ApparelSelectionWindow(rule));
        }
    }
}

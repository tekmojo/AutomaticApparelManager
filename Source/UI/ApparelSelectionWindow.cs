using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Rules;
using UnityEngine;
using Verse;

namespace AutomaticApparel.UI
{
    public sealed class ApparelSelectionWindow : Window
    {
        private const float RowHeight = 32f;

        private readonly ApparelRule rule;
        private readonly List<ThingDef> apparelDefs;
        private Vector2 scrollPosition;
        private string searchText = "";

        public ApparelSelectionWindow(ApparelRule rule)
        {
            this.rule = rule;
            apparelDefs = DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def?.apparel != null)
                .OrderBy(def => def.LabelCap.ToString())
                .ToList();

            doCloseX = true;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize => new Vector2(640f, 700f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Add apparel");
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, 30f);
            searchText = Widgets.TextField(searchRect, searchText ?? "");

            List<ThingDef> filtered = FilteredDefs();
            Rect countRect = new Rect(inRect.x, searchRect.yMax + 6f, inRect.width, 24f);
            Widgets.Label(countRect, $"{filtered.Count} apparel item(s) — search by name or def name");

            Rect outRect = new Rect(inRect.x, countRect.yMax + 4f, inRect.width, inRect.yMax - countRect.yMax - 4f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, filtered.Count * RowHeight);

            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (int i = 0; i < filtered.Count; i++)
                DrawApparelRow(filtered[i], new Rect(0f, i * RowHeight, viewRect.width, RowHeight));
            Widgets.EndScrollView();
        }

        private List<ThingDef> FilteredDefs()
        {
            string query = (searchText ?? "").Trim();
            if (query.Length == 0)
                return apparelDefs;

            return apparelDefs.Where(def =>
                    def.LabelCap.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    def.defName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private void DrawApparelRow(ThingDef def, Rect rect)
        {
            if (Mouse.IsOver(rect))
                Widgets.DrawHighlight(rect);

            bool selected = rule.RequiredApparel.Contains(def);
            string label = $"{def.LabelCap} [{def.defName}]";
            Widgets.Label(new Rect(rect.x + 4f, rect.y + 5f, rect.width - 100f, 24f), label);

            Rect buttonRect = new Rect(rect.xMax - 88f, rect.y + 2f, 84f, 27f);
            if (selected)
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(buttonRect, "Added", active: false);
                GUI.color = Color.white;
            }
            else if (Widgets.ButtonText(buttonRect, "Add"))
            {
                rule.RequiredApparel.Add(def);
            }
        }
    }
}

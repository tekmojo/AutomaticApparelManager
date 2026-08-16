using System.Collections.Generic;
using AutomaticApparel.Detection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticApparel.Patches
{
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class PawnJobTracker_StartJob_Patch
    {
        public static void Prefix(
            Pawn_JobTracker __instance,
            ref Job newJob,
            ref ThinkNode jobGiver,
            ref JobTag? tag,
            bool fromQueue)
        {
            if (newJob == null || newJob.def == JobDefOf.Wear)
                return;

            Pawn pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null)
                return;

            var rule = RuleEvaluator.MatchingRule(pawn, newJob);
            if (rule == null)
                return;

            List<ThingDef> missing = RuleEvaluator.MissingRequiredApparel(pawn, rule);
            if (missing.Count == 0)
                return;

            var wearJobs = new List<Job>();
            foreach (ThingDef def in missing)
            {
                Apparel apparel = ApparelFinder.FindBest(pawn, def);
                if (apparel == null)
                {
                    Log.Warning($"[Automatic Apparel] {pawn.LabelShortCap}: no reachable {def.LabelCap} found for rule '{rule.Name}'.");
                    return;
                }

                Job wearJob = JobMaker.MakeJob(JobDefOf.Wear, apparel);
                wearJob.playerForced = newJob.playerForced;
                wearJobs.Add(wearJob);
            }

            if (wearJobs.Count == 0)
                return;

            __instance.jobQueue.EnqueueFirst(newJob, tag);
            for (int i = wearJobs.Count - 1; i >= 1; i--)
                __instance.jobQueue.EnqueueFirst(wearJobs[i]);

            if (Prefs.DevMode)
                Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: intercepted {newJob.def.defName}; preparing {wearJobs.Count} apparel item(s) for '{rule.Name}'.");

            newJob = wearJobs[0];
            jobGiver = null;
            tag = null;
        }
    }
}

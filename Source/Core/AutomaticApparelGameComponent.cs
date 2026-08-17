using System.Collections.Generic;
using System.Linq;
using AutomaticApparel.Rules;
using AutomaticApparel.State;
using RimWorld;
using Verse;

namespace AutomaticApparel.Core
{
    public sealed class AutomaticApparelGameComponent : GameComponent
    {
        public List<ApparelRule> Rules = new List<ApparelRule>();
        public List<PawnApparelState> PawnStates = new List<PawnApparelState>();
        public List<string> ManagedApparelIds = new List<string>();
        public Dictionary<string, string> ManagedApparelOwners = new Dictionary<string, string>();
        public Dictionary<string, string> ManagedApparelOwnerIds = new Dictionary<string, string>();

        public AutomaticApparelGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (ManagedApparelOwnerIds.Count == 0 || currentTick % 30 != 0)
                return;

            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn?.apparel == null)
                        continue;

                    foreach (RimWorld.Apparel apparel in pawn.apparel.WornApparel.ToList())
                    {
                        if (AutomaticApparel.Storage.AutomaticApparelClassifier.Matches(apparel.def) ||
                            !IsSavedForOtherPawn(apparel, pawn))
                        {
                            continue;
                        }

                        if (pawn.apparel.TryDrop(apparel, out RimWorld.Apparel dropped, pawn.Position, false) &&
                            dropped?.Spawned == true && dropped.IsForbidden(Faction.OfPlayer))
                        {
                            dropped.SetForbidden(false, false);
                        }
                    }
                }
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Rules, "automaticApparelRules", LookMode.Deep);
            Scribe_Collections.Look(ref PawnStates, "automaticApparelPawnStates", LookMode.Deep);
            Scribe_Collections.Look(ref ManagedApparelIds, "automaticApparelManagedIds", LookMode.Value);
            Scribe_Collections.Look(
                ref ManagedApparelOwners,
                "automaticApparelManagedOwners",
                LookMode.Value,
                LookMode.Value);
            Scribe_Collections.Look(
                ref ManagedApparelOwnerIds,
                "automaticApparelManagedOwnerIds",
                LookMode.Value,
                LookMode.Value);
            Rules ??= new List<ApparelRule>();
            PawnStates ??= new List<PawnApparelState>();
            ManagedApparelIds ??= new List<string>();
            ManagedApparelOwners ??= new Dictionary<string, string>();
            ManagedApparelOwnerIds ??= new Dictionary<string, string>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                PawnStates.RemoveAll(state => state?.Pawn == null);

                if (Prefs.DevMode && PawnStates.Count > 0)
                    Log.Message($"[Automatic Apparel] Loaded {PawnStates.Count} pawn apparel snapshot(s).");
            }
        }

        public PawnApparelState StateFor(Pawn pawn) =>
            PawnStates.FirstOrDefault(state => state?.Pawn == pawn);

        public bool IsTrackedApparel(Pawn pawn, RimWorld.Apparel apparel)
        {
            PawnApparelState state = StateFor(pawn);
            return state != null && apparel != null &&
                   ((state.OriginalApparel?.Contains(apparel) ?? false) ||
                    (state.AutomaticApparel?.Contains(apparel) ?? false));
        }

        public bool IsTrackedApparel(RimWorld.Apparel apparel)
        {
            return apparel != null && PawnStates.Any(state => state != null &&
                ((state.OriginalApparel?.Contains(apparel) ?? false) ||
                 (state.AutomaticApparel?.Contains(apparel) ?? false)));
        }

        public bool IsManagedApparel(RimWorld.Apparel apparel) =>
            apparel != null && ManagedApparelIds.Contains(apparel.GetUniqueLoadID());

        public string SavedOwnerFor(RimWorld.Apparel apparel)
        {
            if (apparel == null)
                return null;

            ManagedApparelOwners.TryGetValue(apparel.GetUniqueLoadID(), out string owner);
            return owner;
        }

        public Pawn SavedPawnFor(RimWorld.Apparel apparel)
        {
            if (apparel == null)
                return null;

            string apparelId = apparel.GetUniqueLoadID();
            ManagedApparelOwnerIds.TryGetValue(apparelId, out string pawnId);

            Pawn owner = Find.Maps
                .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                .FirstOrDefault(pawn => pawnId != null && pawn.GetUniqueLoadID() == pawnId);
            if (owner != null)
                return owner;

            if (!ManagedApparelOwners.TryGetValue(apparelId, out string ownerName))
                return null;

            owner = Find.Maps
                .SelectMany(map => map.mapPawns.AllPawnsSpawned)
                .FirstOrDefault(pawn => DisplayNameFor(pawn) == ownerName);
            if (owner != null)
                ManagedApparelOwnerIds[apparelId] = owner.GetUniqueLoadID();
            return owner;
        }

        public bool IsSavedForOtherPawn(RimWorld.Apparel apparel, Pawn pawn)
        {
            if (apparel == null || pawn == null)
                return false;

            string apparelId = apparel.GetUniqueLoadID();
            if (ManagedApparelOwnerIds.TryGetValue(apparelId, out string ownerId))
                return ownerId != pawn.GetUniqueLoadID();

            // Compatibility for items saved by builds that recorded the owner
            // name before stable pawn IDs were introduced.
            if (!ManagedApparelOwners.TryGetValue(apparelId, out string ownerName))
                return false;

            if (DisplayNameFor(pawn) == ownerName)
            {
                ManagedApparelOwnerIds[apparelId] = pawn.GetUniqueLoadID();
                return false;
            }

            Pawn resolvedOwner = SavedPawnFor(apparel);
            return resolvedOwner != null || !string.IsNullOrEmpty(ownerName);
        }

        public void ClearSavedOwner(RimWorld.Apparel apparel)
        {
            if (apparel == null)
                return;

            string apparelId = apparel.GetUniqueLoadID();
            ManagedApparelOwners.Remove(apparelId);
            ManagedApparelOwnerIds.Remove(apparelId);

            foreach (PawnApparelState state in PawnStates.Where(state => state != null))
                state.OriginalApparel?.Remove(apparel);

            if (!IsTrackedApparel(apparel) &&
                !AutomaticApparel.Storage.AutomaticApparelClassifier.Matches(apparel.def))
            {
                ManagedApparelIds.Remove(apparelId);
            }
        }

        private static string DisplayNameFor(Pawn pawn) =>
            pawn?.Name?.ToStringShort ?? pawn?.LabelShort;

        public ApparelRule RuleById(string ruleId) =>
            Rules.FirstOrDefault(rule => rule != null && rule.Id == ruleId);

        public PawnApparelState BeginIntervention(Pawn pawn, ApparelRule rule, IEnumerable<RimWorld.Apparel> automaticApparel)
        {
            PawnApparelState state = StateFor(pawn);
            if (state != null)
            {
                state.AddAutomaticApparel(automaticApparel);
                RegisterManagedApparel(automaticApparel);
                return state;
            }

            state = PawnApparelState.Capture(pawn, rule);
            state.AddAutomaticApparel(automaticApparel);
            RegisterManagedApparel(state.OriginalApparel, pawn);
            RegisterManagedApparel(state.AutomaticApparel);
            PawnStates.Add(state);

            if (Prefs.DevMode)
            {
                string apparel = state.OriginalApparel.Count == 0
                    ? "none"
                    : string.Join(", ", state.OriginalApparel
                        .Where(item => item != null)
                        .Select(item => item.LabelCap.ToString()));
                Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: captured apparel snapshot for '{rule.Name}': {apparel}.");
            }

            return state;
        }

        private void RegisterManagedApparel(IEnumerable<RimWorld.Apparel> apparel, Pawn savedOwner = null)
        {
            if (apparel == null)
                return;

            foreach (RimWorld.Apparel item in apparel.Where(item => item != null))
            {
                string id = item.GetUniqueLoadID();
                if (!ManagedApparelIds.Contains(id))
                    ManagedApparelIds.Add(id);

                if (savedOwner != null)
                {
                    string savedOwnerName = DisplayNameFor(savedOwner);
                    if (!ManagedApparelOwners.TryGetValue(id, out string existingOwnerName))
                    {
                        ManagedApparelOwners[id] = savedOwnerName;
                        ManagedApparelOwnerIds[id] = savedOwner.GetUniqueLoadID();
                    }
                    else if (existingOwnerName == savedOwnerName &&
                             !ManagedApparelOwnerIds.ContainsKey(id))
                    {
                        ManagedApparelOwnerIds[id] = savedOwner.GetUniqueLoadID();
                    }
                }
            }
        }

        public void EndIntervention(Pawn pawn)
        {
            PawnApparelState state = StateFor(pawn);
            if (state == null)
                return;

            PawnStates.Remove(state);
            if (Prefs.DevMode)
                Log.Message($"[Automatic Apparel] {pawn.LabelShortCap}: apparel restoration complete; snapshot cleared.");
        }

        public static AutomaticApparelGameComponent Current =>
            Verse.Current.Game?.GetComponent<AutomaticApparelGameComponent>();
    }
}

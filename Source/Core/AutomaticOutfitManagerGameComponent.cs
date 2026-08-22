using System.Collections.Generic;
using System.Linq;
using AutomaticOutfitManager.Detection;
using AutomaticOutfitManager.Rules;
using AutomaticOutfitManager.State;
using RimWorld;
using Verse;
using Verse.AI;

namespace AutomaticOutfitManager.Core
{
    public sealed class AutomaticOutfitManagerGameComponent : GameComponent
    {
        public List<ApparelRule> Rules = new List<ApparelRule>();
        public List<PawnApparelState> PawnStates = new List<PawnApparelState>();
        public List<string> ManagedApparelIds = new List<string>();
        public Dictionary<string, string> ManagedApparelOwners = new Dictionary<string, string>();
        public Dictionary<string, string> ManagedApparelOwnerIds = new Dictionary<string, string>();

        private readonly Dictionary<Pawn, PawnApparelState> pawnStateIndex = new Dictionary<Pawn, PawnApparelState>();
        private readonly HashSet<string> managedApparelIdIndex = new HashSet<string>();
        private readonly Dictionary<string, Pawn> spawnedPawnIdIndex = new Dictionary<string, Pawn>();
        private readonly Dictionary<Pawn, int> jobTransitionFailureTicks = new Dictionary<Pawn, int>();
        private int indexedPawnStateCount = -1;
        private int indexedManagedApparelCount = -1;
        private int spawnedPawnIndexTick = -1;

        public AutomaticOutfitManagerGameComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (currentTick % 30 != 0)
                return;

            ProcessPendingRecallInterrupts(currentTick);
            EnforceRuntimePawnRules(currentTick);
            RecoverIdleApparelWorkers(currentTick);
        }

        public void RequestRecall(PawnApparelState state)
        {
            if (state?.Pawn == null)
                return;

            state.RecallRequested = true;

            // Returning/restoring is already the recall operation. Re-arming
            // the forced interrupt here can cancel the exact Goto or Wear job
            // that must finish the recall, leaving the pawn in an idle rebuild
            // loop when overlapping rules are paused in quick succession.
            if (state.Transition == ApparelTransition.ReturningToChangingArea ||
                state.Transition == ApparelTransition.Restoring)
            {
                state.RecallInterruptPending = false;
                return;
            }

            state.RecallInterruptPending = true;
        }

        private void ProcessPendingRecallInterrupts(int currentTick)
        {
            foreach (PawnApparelState state in PawnStates.ToList())
            {
                Pawn pawn = state?.Pawn;
                if (state?.RecallInterruptPending != true || pawn?.Spawned != true ||
                    pawn.Drafted || pawn.jobs == null)
                {
                    continue;
                }

                if (state.Transition == ApparelTransition.ReturningToChangingArea ||
                    state.Transition == ApparelTransition.Restoring)
                {
                    state.RecallInterruptPending = false;
                    continue;
                }

                // Do not force-interrupt a wear/remove toil after it has begun.
                // Apparel drivers can temporarily remove conflicting layers
                // before the replacement is committed. Let this exact assigned
                // step finish; the next StartJob call cancels any remaining
                // preparation and enters the normal recall path.
                if (IsAssignedApparelTransitionJob(state, pawn.jobs.curJob))
                    continue;

                // A broken third-party job can throw while RimWorld selects the
                // replacement job. Keep that failure out of the UI and avoid a
                // retry every tick; a later attempt can recover after the stale
                // target or reservation has been cleared.
                if (state.LastRecallInterruptAttemptTick >= 0 &&
                    currentTick - state.LastRecallInterruptAttemptTick < 300)
                {
                    continue;
                }

                state.LastRecallInterruptAttemptTick = currentTick;
                if (pawn.jobs.curJob == null || TryJobTransition(pawn, currentTick, "return request", () =>
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true)))
                {
                    state.RecallInterruptPending = false;
                }
            }
        }

        private static bool IsAssignedApparelTransitionJob(
            PawnApparelState state, Job job)
        {
            if (state == null || job?.targetA.Thing is not RimWorld.Apparel apparel)
                return false;

            if (job.def == JobDefOf.Wear)
            {
                return state.Transition == ApparelTransition.Restoring
                    ? state.OriginalApparel?.Contains(apparel) == true
                    : state.ManagedApparel?.Contains(apparel) == true;
            }

            if (job.def == JobDefOf.RemoveApparel)
                return state.ManagedApparel?.Contains(apparel) == true;

            return state.Transition == ApparelTransition.Restoring &&
                   (job.def == JobDefOf.HaulToCell ||
                    job.def == JobDefOf.HaulToContainer) &&
                   state.ManagedApparel?.Contains(apparel) == true;
        }

        private void EnforceRuntimePawnRules(int currentTick)
        {
            List<ApparelRule> pausedRules = Rules.Where(rule =>
                rule?.Enabled == true && rule.WorkAreaPaused && rule.Area?.Map != null).ToList();
            bool enforceOwnership = ManagedApparelOwnerIds.Count > 0;

            foreach (Map map in Find.Maps)
            {
                List<ApparelRule> mapPausedRules = pausedRules.Where(rule => rule.Area.Map == map).ToList();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (enforceOwnership)
                        EnforceSavedApparelOwnership(pawn);

                    Job job = pawn?.jobs?.curJob;
                    if (job == null)
                        continue;

                    bool handled = false;
                    if (pawn.Faction == Faction.OfPlayer && !pawn.Drafted)
                    {
                        PawnApparelState state = StateFor(pawn);
                        foreach (ApparelRule rule in mapPausedRules)
                        {
                            if (state?.ActiveRuleId == rule.Id && !state.RecallRequested &&
                                !Patches.PausedAreaWorkFilter.MatchesPermittedHaulingRule(pawn, job, rule))
                            {
                                RequestRecall(state);
                                handled = true;
                                break;
                            }

                            if (state?.RecallRequested != true && job.workGiverDef != null &&
                                RuleEvaluator.JobTargetsArea(job, rule.Area))
                            {
                                handled = TryJobTransition(pawn, currentTick, "paused-area work", () =>
                                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true));
                                break;
                            }
                        }
                    }

                    if (handled)
                        continue;

                    if (Patches.PausedAreaWorkFilter.ShouldRejectWanderingJob(pawn, job))
                    {
                        if (Patches.PausedAreaWorkFilter.TryMakeWanderingExitJob(pawn, out Job exitJob))
                            TryJobTransition(pawn, currentTick, "wandering exit", () =>
                                pawn.jobs.StartJob(exitJob, JobCondition.InterruptForced));
                        else
                            TryJobTransition(pawn, currentTick, "wandering restriction", () =>
                                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true));
                        continue;
                    }

                    if (Patches.PausedAreaWorkFilter.ShouldRejectProtectedAreaJob(pawn, job))
                    {
                        if (Patches.PausedAreaWorkFilter.TryMakeProtectedChildExitJob(pawn, job, out Job exitJob))
                            TryJobTransition(pawn, currentTick, "protected-child exit", () =>
                                pawn.jobs.StartJob(exitJob, JobCondition.InterruptForced));
                        else
                            TryJobTransition(pawn, currentTick, "protected-child restriction", () =>
                                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true));
                        continue;
                    }

                    if (Patches.PausedAreaWorkFilter.ShouldRejectHaulingJob(pawn, job))
                        TryJobTransition(pawn, currentTick, "hauling restriction", () =>
                            pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true));
                }
            }
        }

        private void EnforceSavedApparelOwnership(Pawn pawn)
        {
            if (pawn?.apparel == null)
                return;

            foreach (RimWorld.Apparel apparel in pawn.apparel.WornApparel.ToList())
            {
                if (AutomaticOutfitManager.Storage.ManagedApparelClassifier.Matches(apparel.def) ||
                    !IsSavedForOtherPawn(apparel, pawn))
                    continue;

                if (pawn.apparel.TryDrop(apparel, out RimWorld.Apparel dropped, pawn.Position, false) &&
                    dropped?.Spawned == true && dropped.IsForbidden(Faction.OfPlayer))
                    dropped.SetForbidden(false, false);
            }
        }

        private bool TryJobTransition(Pawn pawn, int currentTick, string context, System.Action transition)
        {
            if (pawn == null || transition == null)
                return false;
            if (jobTransitionFailureTicks.TryGetValue(pawn, out int failedTick) &&
                currentTick - failedTick < 300)
                return false;

            try
            {
                transition();
                jobTransitionFailureTicks.Remove(pawn);
                return true;
            }
            catch (System.Exception exception)
            {
                jobTransitionFailureTicks[pawn] = currentTick;
                Log.Warning($"[AutomaticOutfitManager] {pawn.LabelShortCap}: {context} job transition failed; retrying later. {exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        private void RecoverIdleApparelWorkers(int currentTick)
        {
            foreach (PawnApparelState state in PawnStates.ToList())
            {
                Pawn pawn = state?.Pawn;
                ApparelRule rule = RuleById(state?.ActiveRuleId);
                if (pawn?.Spawned == true && !pawn.Drafted &&
                    state.Transition == ApparelTransition.Restoring)
                {
                    Job restorationJob = pawn.jobs?.curJob;
                    if (!IsIdleRecoveryJob(pawn, restorationJob))
                    {
                        state.ActiveIdleTicks = 0;
                        continue;
                    }

                    RestorationPlanner.TryMakeHeldOriginalsAccessible(pawn, state);
                    List<Job> remainingJobs = RestorationPlanner.BuildJobs(
                        pawn, state, rule, out bool hasUnavailableSavedApparel);
                    if (remainingJobs.Count == 0 && !hasUnavailableSavedApparel)
                    {
                        state.ActiveIdleTicks = 0;
                        EndIntervention(pawn);
                        continue;
                    }

                    state.ActiveIdleTicks += 30;
                    int idleGrace = hasUnavailableSavedApparel ? 240 : 120;
                    int retryCooldown = hasUnavailableSavedApparel ? 600 : 120;
                    bool retryReady = state.LastRestorationAttemptTick < 0 ||
                        currentTick - state.LastRestorationAttemptTick >= retryCooldown;
                    if (state.ActiveIdleTicks < idleGrace || !retryReady)
                        continue;

                    state.ActiveIdleTicks = 0;
                    bool recoveryStarted = TryJobTransition(
                        pawn, currentTick, "idle restoration recovery", () =>
                    {
                        // Ending Standing normally selects another Wait-family
                        // job, which restoration intentionally ignores to avoid
                        // retry loops. A same-cell Goto is a harmless non-Wait
                        // trigger that StartJob must replace with the rebuilt
                        // restoration queue before it can execute.
                        // The previous restoration attempt has already passed
                        // the retry window checked above. Clear its timestamp
                        // so the StartJob patch does not mistake this deliberate
                        // recovery trigger for a same-tick failed-Wear retry and
                        // turn it straight back into another Wait job.
                        state.LastRestorationAttemptTick = -1;
                        Job recoveryTrigger = JobMaker.MakeJob(JobDefOf.Goto, pawn.Position);
                        recoveryTrigger.expiryInterval = 30;
                        pawn.jobs.StartJob(
                            recoveryTrigger, JobCondition.InterruptForced, null, false, true);
                    });
                    if (recoveryStarted && StateFor(pawn) != null && Prefs.DevMode)
                        Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: restoration became idle; rebuilding saved-apparel jobs.");
                    continue;
                }

                if (pawn?.Spawned != true || pawn.Drafted ||
                    state.Transition != ApparelTransition.Active ||
                    rule?.Enabled != true || rule.WorkAreaPaused ||
                    state.RecallRequested)
                {
                    if (state != null)
                        state.ActiveIdleTicks = 0;
                    continue;
                }

                Job job = pawn.jobs?.curJob;
                bool idle = IsIdleRecoveryJob(pawn, job) ||
                    ((job.def == JobDefOf.HaulToCell || job.def == JobDefOf.HaulToContainer) &&
                     pawn.carryTracker?.CarriedThing == null &&
                     pawn.pather?.Moving != true);

                if (!idle)
                {
                    state.ActiveIdleTicks = 0;
                    continue;
                }

                state.ActiveIdleTicks += 30;
                if (state.ActiveIdleTicks < 240)
                    continue;

                // Some haul drivers can finish their final toil without promptly
                // yielding a new StartJob call. That leaves the apparel state
                // active and the pawn visibly standing forever. After a short
                // grace period, request the normal locker-room restoration path.
                state.ActiveIdleTicks = 0;
                RequestRecall(state);
                if (Prefs.DevMode)
                    Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: finished work and became idle; returning to locker room.");
            }
        }

        private static bool IsIdleRecoveryJob(Pawn pawn, Job job)
        {
            if (job == null)
                return true;

            // RimWorld and several AI mods use specialized Wait jobs for the
            // visible "Standing" activity. Checking only JobDefOf.Wait misses
            // those variants and can leave a finished worker active forever.
            // The movement/carry guards keep connective waits during hauling or
            // travel from being mistaken for completed work.
            string defName = job.def?.defName ?? string.Empty;
            bool waitFamily = job.def == JobDefOf.Wait ||
                job.def == JobDefOf.Wait_Wander ||
                defName.StartsWith("Wait", System.StringComparison.OrdinalIgnoreCase) ||
                defName.IndexOf("Standing", System.StringComparison.OrdinalIgnoreCase) >= 0;

            return waitFamily &&
                   pawn?.pather?.Moving != true &&
                   pawn?.carryTracker?.CarriedThing == null;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref Rules, "automaticOutfitManagerRules", LookMode.Deep);
            Scribe_Collections.Look(ref PawnStates, "automaticOutfitManagerPawnStates", LookMode.Deep);
            Scribe_Collections.Look(ref ManagedApparelIds, "automaticOutfitManagerManagedIds", LookMode.Value);
            Scribe_Collections.Look(
                ref ManagedApparelOwners,
                "automaticOutfitManagerManagedOwners",
                LookMode.Value,
                LookMode.Value);
            Scribe_Collections.Look(
                ref ManagedApparelOwnerIds,
                "automaticOutfitManagerManagedOwnerIds",
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
                RebuildRuntimeIndexes();
                ManagedWorkClaimRegistry.ResetForLoadedGame();

                if (Prefs.DevMode && PawnStates.Count > 0)
                    Log.Message($"[AutomaticOutfitManager] Loaded {PawnStates.Count} pawn apparel snapshot(s).");
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            RebuildRuntimeIndexes();
            int restoredClaims = RebuildPendingWorkClaims();
            if (Prefs.DevMode && restoredClaims > 0)
            {
                Log.Message($"[AutomaticOutfitManager] Restored {restoredClaims} pending work claim(s) after load.");
            }
        }

        private int RebuildPendingWorkClaims()
        {
            ManagedWorkClaimRegistry.ResetForLoadedGame();
            int restoredClaims = 0;

            // Re-establish saved claims before any pawn thinker can select the
            // same bill, frame, ingredient, or queued target after loading. The
            // oldest intervention wins deterministically if an old save somehow
            // contains conflicting pending jobs; the normal continuation path
            // safely cancels any later state that could not reclaim its target.
            foreach (PawnApparelState state in PawnStates
                         .Where(state =>
                             state?.Pawn?.Spawned == true &&
                             state.PendingWorkJob != null &&
                             !state.RecallRequested &&
                             (state.Transition == ApparelTransition.Preparing ||
                              state.Transition == ApparelTransition.Active))
                         .OrderBy(state => state.StartedTick)
                         .ThenBy(state => state.Pawn.thingIDNumber))
            {
                if (ManagedWorkClaimRegistry.TryClaim(
                        state.Pawn, state.PendingWorkJob) &&
                    ManagedWorkClaimRegistry.HasActiveClaim(state.Pawn))
                {
                    restoredClaims++;
                }
            }

            return restoredClaims;
        }

        public PawnApparelState StateFor(Pawn pawn)
        {
            EnsureStateIndex();
            return pawn != null && pawnStateIndex.TryGetValue(pawn, out PawnApparelState state)
                ? state
                : null;
        }

        public bool IsTrackedApparel(Pawn pawn, RimWorld.Apparel apparel)
        {
            PawnApparelState state = StateFor(pawn);
            return state != null && apparel != null &&
                   ((state.OriginalApparel?.Contains(apparel) ?? false) ||
                    (state.ManagedApparel?.Contains(apparel) ?? false));
        }

        public bool IsTrackedApparel(RimWorld.Apparel apparel)
        {
            return apparel != null && PawnStates.Any(state => state != null &&
                ((state.OriginalApparel?.Contains(apparel) ?? false) ||
                 (state.ManagedApparel?.Contains(apparel) ?? false)));
        }

        public bool IsManagedApparelAssignedToOtherPawn(
            RimWorld.Apparel apparel, Pawn pawn)
        {
            if (apparel == null || pawn == null)
                return false;

            return PawnStates.Any(state =>
                state?.Pawn != null && state.Pawn != pawn &&
                (state.ManagedApparel?.Contains(apparel) ?? false) &&
                (state.Transition == ApparelTransition.Preparing ||
                 state.Pawn.apparel?.WornApparel.Contains(apparel) == true));
        }

        public bool IsManagedApparel(RimWorld.Apparel apparel)
        {
            EnsureManagedApparelIndex();
            return apparel != null && managedApparelIdIndex.Contains(apparel.GetUniqueLoadID());
        }

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

            Pawn owner = SpawnedPawnById(pawnId);
            if (owner != null)
                return owner;

            if (!ManagedApparelOwners.TryGetValue(apparelId, out string ownerName))
                return null;

            owner = AllSpawnedPawns().FirstOrDefault(pawn => DisplayNameFor(pawn) == ownerName);
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
                !AutomaticOutfitManager.Storage.ManagedApparelClassifier.Matches(apparel.def))
            {
                ManagedApparelIds.Remove(apparelId);
                managedApparelIdIndex.Remove(apparelId);
                indexedManagedApparelCount = ManagedApparelIds.Count;
            }
        }

        private static string DisplayNameFor(Pawn pawn) =>
            pawn?.Name?.ToStringShort ?? pawn?.LabelShort;

        public ApparelRule RuleById(string ruleId) =>
            Rules.FirstOrDefault(rule => rule != null && rule.Id == ruleId);

        public PawnApparelState BeginIntervention(Pawn pawn, ApparelRule rule, IEnumerable<RimWorld.Apparel> managedApparel)
        {
            // Access restrictions also cover animals, mechs, and modded robots,
            // but only humanlike pawns with an apparel tracker can participate
            // in outfit transitions. Keep this invariant here as well as at the
            // job boundary so another caller cannot create an empty, looping
            // apparel snapshot for an automated unit.
            if (pawn?.RaceProps?.Humanlike != true || pawn.apparel == null)
            {
                EndIntervention(pawn);
                return null;
            }

            PawnApparelState state = StateFor(pawn);
            if (state != null)
            {
                state.AddManagedApparel(managedApparel);
                RegisterManagedApparel(managedApparel);
                state.Transition = ApparelTransition.Preparing;
                state.ActiveIdleTicks = 0;
                return state;
            }

            state = PawnApparelState.Capture(pawn, rule);
            state.AddManagedApparel(managedApparel);
            RegisterManagedApparel(state.OriginalApparel, pawn);
            RegisterManagedApparel(state.ManagedApparel);
            PawnStates.Add(state);
            pawnStateIndex[pawn] = state;
            indexedPawnStateCount = PawnStates.Count;

            if (Prefs.DevMode)
            {
                string apparel = state.OriginalApparel.Count == 0
                    ? "none"
                    : string.Join(", ", state.OriginalApparel
                        .Where(item => item != null)
                        .Select(item => item.LabelCap.ToString()));
                Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: captured apparel snapshot for '{rule.Name}': {apparel}.");
            }

            return state;
        }

        private void RegisterManagedApparel(IEnumerable<RimWorld.Apparel> apparel, Pawn savedOwner = null)
        {
            if (apparel == null)
                return;

            EnsureManagedApparelIndex();

            foreach (RimWorld.Apparel item in apparel.Where(item => item != null))
            {
                string id = item.GetUniqueLoadID();
                if (!ManagedApparelIds.Contains(id))
                    ManagedApparelIds.Add(id);
                managedApparelIdIndex.Add(id);
                indexedManagedApparelCount = ManagedApparelIds.Count;

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
            ManagedWorkClaimRegistry.ReleaseAll(pawn);
            PawnApparelState state = StateFor(pawn);
            if (state == null)
                return;

            PawnStates.Remove(state);
            pawnStateIndex.Remove(pawn);
            indexedPawnStateCount = PawnStates.Count;
            if (Prefs.DevMode)
                Log.Message($"[AutomaticOutfitManager] {pawn.LabelShortCap}: apparel restoration complete; snapshot cleared.");
        }

        public static AutomaticOutfitManagerGameComponent Current =>
            Verse.Current.Game?.GetComponent<AutomaticOutfitManagerGameComponent>();

        private void EnsureStateIndex()
        {
            if (indexedPawnStateCount == PawnStates.Count)
                return;

            pawnStateIndex.Clear();
            foreach (PawnApparelState state in PawnStates.Where(state => state?.Pawn != null))
                pawnStateIndex[state.Pawn] = state;
            indexedPawnStateCount = PawnStates.Count;
        }

        private void EnsureManagedApparelIndex()
        {
            if (indexedManagedApparelCount == ManagedApparelIds.Count)
                return;

            managedApparelIdIndex.Clear();
            foreach (string id in ManagedApparelIds.Where(id => !string.IsNullOrEmpty(id)))
                managedApparelIdIndex.Add(id);
            indexedManagedApparelCount = ManagedApparelIds.Count;
        }

        private void RebuildRuntimeIndexes()
        {
            indexedPawnStateCount = -1;
            indexedManagedApparelCount = -1;
            EnsureStateIndex();
            EnsureManagedApparelIndex();
            RebuildSpawnedPawnIndex();
        }

        private Pawn SpawnedPawnById(string pawnId)
        {
            if (string.IsNullOrEmpty(pawnId))
                return null;

            int currentTick = Find.TickManager?.TicksGame ?? 0;
            if (spawnedPawnIndexTick < 0 || currentTick - spawnedPawnIndexTick >= 300)
                RebuildSpawnedPawnIndex();
            if (spawnedPawnIdIndex.TryGetValue(pawnId, out Pawn pawn))
                return pawn;

            RebuildSpawnedPawnIndex();
            spawnedPawnIdIndex.TryGetValue(pawnId, out pawn);
            return pawn;
        }

        private void RebuildSpawnedPawnIndex()
        {
            spawnedPawnIdIndex.Clear();
            foreach (Pawn pawn in AllSpawnedPawns().Where(pawn => pawn != null))
                spawnedPawnIdIndex[pawn.GetUniqueLoadID()] = pawn;
            spawnedPawnIndexTick = Find.TickManager?.TicksGame ?? 0;
        }

        private static IEnumerable<Pawn> AllSpawnedPawns() =>
            Find.Maps.SelectMany(map => map.mapPawns.AllPawnsSpawned);
    }
}

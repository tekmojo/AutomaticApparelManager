# Automatic Outfit Manager — Project Design

## Goal

Automatic Outfit Manager provides context-aware RimWorld outfits without hard-coded integrations. A player-defined area and selected apparel can represent radiation PPE, freezer clothing, firefighting equipment, clean-room garments, industrial safety gear, combat armor, or role-play uniforms.

The mod does not detect hazards. It reacts to jobs, routes, areas, apparel, and player configuration.

## Design principles

1. **Generic rules.** Operate on RimWorld concepts rather than named content mods.
2. **Vanilla jobs where practical.** Use normal wear, remove, haul, path, reservation, and storage behavior.
3. **Save-specific configuration.** Rules and active pawn transitions persist with the game.
4. **Compatibility first.** Intercept narrow boundaries and recover safely when another mod changes a job.
5. **Player control wins.** Drafting, forced orders, schedules, and explicit pause/return commands are not aggressively overridden.
6. **Bounded recovery.** Failed transitions use cooldowns instead of immediate retry loops.

## Product identity and compatibility

The player-facing product name is **Automatic Outfit Manager**. The broader “outfit” name leaves room for future managed equipment such as optional melee or ranged weapon requirements while keeping apparel and PPE as the current implemented scope.

The product is branded **Automatic Outfit Manager** in human-readable text, while the repository and technical identity use `AutomaticOutfitManager`. The package ID is `tekmojo.automaticoutfitmanager`; the assembly, namespace, Harmony ID, DefNames, serialized keys, source names, and asset paths use the compact technical identity. The rebrand intentionally establishes a clean identity rather than retaining compatibility with saves created under the former product name.

## Phase 1 — Area-triggered outfitting (implemented)

An enabled rule references a RimWorld work area and one or more apparel definitions. An eligible undrafted humanlike colonist, slave, prisoner, or hosted guest qualifies when a job target, interaction position, or protected transit route enters the area and its category is permitted.

If required gear is missing:

```text
qualifying job
  → capture exact worn apparel
  → find reachable and reservable required gear
  → queue normal Wear jobs
  → resume qualifying work
```

The searchable apparel selector enumerates loaded wearable definitions, so vanilla and modded apparel work without direct integration.

## Phase 2 — State, restoration, access, and monitoring (complete; playtested)

### Persistent pawn state

Each intervention records:

- Pawn and active rule
- Exact original apparel references
- Exact automatic/work apparel references
- Preparing, active, returning, or restoring transition
- Return-request state and safe-interrupt cooldown
- Task-buffer usage and current buffered job
- Locker-return and restoration retry timing
- The exact intercepted work job, whether it carries managed-work context, and the last untagged work definition used for consistent buffer/UI classification

Runtime indexes accelerate frequent pawn-state and managed-item lookups without changing save data.

### Exact work continuation and claims

The interrupted `Job` is deep-saved only in pawn apparel state while preparation runs; it is not duplicated in RimWorld’s job queue. Direct player assignments and modded jobs without `workGiverDef` retain managed-work context explicitly so they reset the rule buffer and display as work rather than as follow-up activity.

Every concrete Thing target in A/B/C and both target queues receives one atomic, short-lived claim. Jobs without Thing targets fall back to a cell claim. Pending claims are rebuilt deterministically after a save finishes loading, before pawn AI can take the preserved targets. After preparation, target existence, map membership, real RimWorld reservations, rule applicability, recall/urgency, and claim contention are revalidated. A valid job replaces the next thinker candidate exactly. An invalid or contested continuation is released safely and logs its bounded cancellation reason in developer mode.

### Restoration

After managed work and the configured task buffer finish, the pawn returns to the optional locker room, removes managed outfit gear, and restores the exact saved items.

Destroyed references are skipped. Temporarily unavailable items report their status and retry after a cooldown. Recovery/wait jobs pass through so a failed wear operation cannot create a same-tick retry storm. A player can deliberately release a claim with **Clear saved owner**.

### Task buffer

Each rule allows 0–20 ordinary follow-up jobs before restoration. A slot is reserved when a new bufferable job starts. Renewed qualifying work resets usage, including player-assigned and modded work that lacks a normal work-giver tag, while sleeping begins restoration immediately. The worker UI names the active buffered job and count and uses the same retained work context as the transition logic.

Future work may track successful job completion separately from job start and roll back interrupted slots.

### Locker rooms and storage

Rules may reference a separate changing area:

- Acquisition prefers required gear inside the locker.
- Restoration returns the pawn there first.
- Automatic/non-automatic special storage filters separate managed gear from ordinary apparel.
- Dropped managed gear remains unforbidden.
- A low-priority hauling work giver restocks locker storage whenever its rule is enabled, including during normal active operation and while work is paused.

### Saved ownership

- Required work gear is shared.
- Displaced personal apparel is claimed for its original pawn.
- Outfit optimization, wear, reservation, repair, processing, and hauling guards protect claimed gear.
- Non-colony pawns cannot target managed apparel.
- A periodic invariant check removes wrongly worn claimed apparel if another mod bypasses normal validation.
- Inspection text and apparel gizmos expose role, owner, areas, jump-to-owner, and clear-owner actions.

### Pause and resume work

**Pause work** closes one rule to ordinary work, interrupts active work safely, and restores current workers. **Resume work** reopens it. The control remains available in collapsed view.

Work-giver result patches reject paused-area jobs early. A periodic consolidated scan catches jobs injected by other mods. Job transitions share rate-limited exception handling.

Pauses use deterministic, safety-first overlap precedence: if any enabled overlapping rule is paused, ordinary work is blocked in the shared cells. Readiness distinguishes a partially restricted rule that remains active elsewhere from a rule whose entire work area is covered by paused overlaps. Return travel and exact restoration jobs are narrowly exempt so a worker cannot be stranded while complying with the pause.

### Access controls

Hauling and wandering permissions are independently configurable for colonists, mechs/robots, animals, guests, slaves, and prisoners. Child work watching has its own toggle.

Restrictions evaluate targets and relevant routes. Units inside receive safe exits; outside wandering attempts use a short wait rather than repeated redirection. Non-humanlike units obey access rules but never enter the apparel intervention system.

### Path safety

Incoming jobs are evaluated before start, and actual next path cells are checked for eligible humanlike pawns. This catches route changes caused by doors, congestion, reservations, or modded pathing. Essential personal jobs restore saved clothing first, then use a safe detour around protected areas when one exists; an unavailable detour yields through a bounded retry rather than crossing without required gear.

Hot-path checks use cached field access, non-allocating missing-gear tests, indexed state, and a single periodic pawn traversal.

### User interface

The **Automatic Outfit Manager** main tab provides:

- Named enabled/disabled rules
- Work-area and locker-area selection with native hover overlays
- Searchable apparel selection
- Hauling, wandering, and child permissions
- 0–20 task buffer
- Readiness and gear availability
- Worker, hauler, and wanderer activity
- Detailed hover status and click-to-jump
- Per-worker return and area-wide pause/resume
- Persistent collapse/expand state
- Rule deletion and RimWorld area management

## Phase 2 behavior boundaries

- Drafted and forced behavior takes priority where practical.
- Sleeping is restored around rather than treated as an ordinary buffered task.
- A missing item can delay restoration but cannot cause unbounded retries.
- A lost, destroyed, recalled, urgent, reserved, or contested continuation falls back to normal job selection with a specific developer-mode reason.
- Multiple enabled rules can coexist, but overlapping conflicting rules have no configurable priority yet.
- Debug logging is tied to RimWorld developer mode. Repeated guest diagnostics use a one-day per-pawn/category interval; colony diagnostics retain the shorter stabilization interval.

## Phase 3 — Rule engine (planned)

- Deterministic rule priority and conflict resolution
- Per-pawn assignment and filters
- JobDef and WorkTypeDef triggers
- Current-area and destination-area combinations
- Drafted, temperature, environment, hediff, or generic hazard triggers where appropriate
- Strict, warning, and best-effort behavior modes
- Apparel quality, condition, and material filters
- Optional melee, ranged, or either-weapon requirements for armories, guard posts, and similar work areas
- Weapon handling that respects drafted equipment, sidearm/weapon-management mods, ideology constraints, and explicit player orders

## Phase 4 — User experience (planned)

- Copy/duplicate and reorder rules
- Presets or import/export if useful
- Localization
- Dedicated diagnostic/logging option
- Clearer visual severity for blocked transitions
- Optional successful-completion accounting for task buffers

## Compatibility strategy

Harmony is the only dependency. Rimatomics and other content mods are examples, not integrations. Optional compatibility code should be introduced only when a mod requires information unavailable through generic RimWorld systems.

Harmony patches should remain narrow, avoid destructive replacement of core systems, and preserve the game’s normal recovery jobs.

## Repository structure

```text
AutomaticOutfitManager/
├── About/
│   ├── About.xml
│   └── ModIcon.png
├── Defs/
│   ├── MainButtonDefs/
│   ├── SpecialThingFilterDefs/
│   └── WorkGiverDefs/
├── 1.6/
│   └── Assemblies/
├── Source/
│   ├── Core/
│   ├── Detection/
│   ├── Patches/
│   ├── Rules/
│   ├── State/
│   ├── Storage/
│   └── UI/
├── Textures/
│   └── UI/Buttons/MainButtons/
├── build.ps1
├── PROJECT-DESIGN.md
└── README.md
```

The project references local RimWorld and Harmony assemblies; copyrighted game assemblies and third-party binaries are not committed.

## Development workflow

`main` represents the stable development baseline. Significant changes should be validated in a live modded colony, checked for log loops and exceptions, built successfully, and reviewed through a branch or pull request before merging.

The next milestone after Phase 2 stabilization is deterministic overlapping-rule behavior and configurable pawn eligibility.

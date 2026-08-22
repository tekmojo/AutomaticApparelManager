# Automatic Outfit Manager

Automatic Outfit Manager is a RimWorld 1.6 mod for area-based work outfits and personal protective equipment (PPE).

Repository: [github.com/tekmojo/AutomaticOutfitManager](https://github.com/tekmojo/AutomaticOutfitManager)

Create a rule, select a work area, choose the required apparel, and optionally assign a locker room. Eligible humanlike pawns equip the gear before qualifying work, keep it for a configurable number of follow-up tasks, then return to the locker room and restore the exact clothing they wore beforehand.

The mod uses ordinary RimWorld areas, jobs, apparel, reservations, and storage. It supports vanilla and modded apparel without hard-coded hazard or content-mod integrations.

The compact technical identity is `AutomaticOutfitManager`, including the repository, package, assembly, namespaces, DefNames, serialized keys, filenames, and asset paths. This breaking rebrand does not preserve saves created under the former product identity.

## Requirements

- RimWorld 1.6
- Harmony

Dubs Rimatomics inspired the original radiation-PPE scenario, but it is not a dependency. Automatic Outfit Manager does not detect radiation automatically.

## Quick start

1. Create a RimWorld area covering the workspace that needs special clothing.
2. Optionally create a second area around the changing and storage space.
3. Open the **Automatic Outfit Manager** main tab.
4. Select **Add rule**, name it, and choose the **Work area**.
5. Select an optional **Locker room**.
6. Use **Choose gear** to add every required apparel item.
7. Configure the task buffer and access permissions.
8. Put reachable copies of the required gear on the map.
9. For dedicated storage, enable **Allow managed outfit gear** in its storage settings.

## How each feature works

### Work area and entry protection

The work area is an existing RimWorld area. A job qualifies when its relevant target or interaction location is inside that area, or when a protected route must cross it.

The mod checks actual movement as well as the initial job. If a route changes, an eligible unequipped pawn is stopped before entering an active protected area and allowed to reconsider after obtaining the required gear.

The exact job that triggered outfitting is preserved while the pawn changes, including direct player assignments and compatible modded work that does not provide RimWorld’s usual work-giver tag. Its concrete targets are temporarily claimed so another outfitting pawn cannot take the same frame, bill ingredients, haul targets, or similar work. Before resuming, the mod confirms that the job and its reservations are still valid; invalid or contested work is released safely for normal reconsideration.

Sleeping and other essential personal jobs do not keep the work outfit through the task buffer. The pawn restores saved clothing first and, when necessary, routes around active protected areas. If no safe route exists, the personal job waits instead of sending an unequipped pawn through the area.

### Required gear

The searchable selector includes loaded vanilla and modded apparel. Before qualifying work starts, an eligible humanlike pawn:

1. Saves every personal apparel item currently worn.
2. Finds reachable, reservable copies of missing required gear.
3. Uses normal RimWorld `Wear` jobs to equip them.
4. Resumes the exact original job after preparation succeeds and its targets remain valid.

Children and pawns unable to wear the selected apparel are skipped safely. Non-humanlike pawns use access controls but do not participate in outfit changes.

### Locker room

The optional locker room controls where restoration occurs and where shared gear should be stored.

- Required gear in the locker is preferred; suitable map-wide gear is a fallback.
- After the task buffer is exhausted, the pawn returns there before restoring saved clothing.
- A low-priority hauling work giver returns unworn managed gear to valid locker storage whenever its rule is enabled, including during normal active operation and while work is paused.
- Rules without a locker still change outfits without a dedicated return trip.

### Task buffer

The task buffer controls how many ordinary jobs a pawn may start after leaving qualifying work while still wearing the work outfit. It ranges from 0 to 20.

- `0 tasks`: restore saved clothing as soon as managed work ends.
- `1 task`: allow one follow-up job, such as eating or hauling.
- Higher values reduce repeated changes around busy work areas.
- Renewed qualifying work inside the area resets the counter, including direct orders and compatible modded work without a normal work-giver tag.
- Sleeping bypasses the buffer and begins restoration.
- Pausing work, drafting, forced orders, and item availability can alter when restoration completes.

The worker row identifies the activity, for example:

`Foto — Buffered task 1 of 3: Consuming fine meal`

The tooltip also shows `Task buffer: 1 of 3 completed.` A slot is reserved when its job starts so the same job cannot consume it repeatedly. Interrupted jobs are not currently rolled back.

### Saved clothing and ownership

The mod restores the exact apparel instances captured at the start, not merely another item of the same type.

- Saved personal gear remains claimed for its original pawn.
- Other pawns cannot optimize into, reserve, process, haul away, or wear claimed gear while it is needed.
- Shared rule-required work gear is not permanently assigned.
- A safety check removes saved gear if another mod bypasses normal wear validation.
- Destroyed items are skipped; temporarily unavailable items retry with a cooldown.

Select saved apparel to use **Jump to owner** or **Clear saved owner**. Clearing the owner releases an abandoned or permanently misplaced item for normal use.

### Pause and resume work

**Pause work** closes the rule’s area to ordinary work and returns active workers to the locker. The rule remains configured and enabled.

While work is paused, existing ordinary work is interrupted safely, new ordinary work is rejected before assignment, and workers finish restoration. Permitted hauling and wandering remain independently controlled. The button changes to **Resume work**, including in collapsed view. Readiness changes immediately with the rule; worker rows independently show anyone still returning or restoring gear.

When enabled work areas overlap, the most restrictive pause wins in their shared cells. A larger or partially overlapping rule reports **Active — shared cells paused: Rule name** and continues working elsewhere. A rule whose every work cell is covered reports **Blocked — work area covered by paused: Rule name**. Return travel and exact saved-clothing restoration remain allowed so workers can leave safely and finish changing.

### Hauling and wandering access

Each rule has separate **Hauling** and **Wandering** permissions for colonists, mechs/robots, animals, guests, slaves, and prisoners.

These permissions govern travel into or through the area; they do not outfit non-humanlike units. Disallowed units inside receive an exit job. Units already outside briefly wait instead of repeatedly selecting a route through the restriction. The **Haulers** and **Wanderers** rows show current relevant activity.

### Children and work watching

**Allow work watching** controls whether children may enter the active work area for learning and observation. When disabled, unsafe learning jobs are redirected or rejected before entry.

### Readiness and worker status

Readiness reports whether a rule currently accepts work, is configured, and has the required gear/storage available. **Work paused** means the rule was paused and remains closed until resumed. **Active — shared cells paused** means only the overlap is closed; **Blocked — work area covered** means paused overlaps cover the entire rule. Returning workers do not make an active rule appear paused. Availability is a map-level summary; an item can still become reserved, unreachable, worn, or moved.

Worker rows and hover tooltips expose the current transition:

- **Outfitting work gear** — collecting or wearing required apparel.
- **Work outfit equipped** — prepared for managed work.
- **Buffered task X of Y: activity** — performing the named follow-up task.
- **Returning to locker room** — traveling to the changing area.
- **Outfitting saved gear** — removing work gear and restoring personal clothing.
- **Restoration paused — sleeping or resting/drafted/forced order** — higher-priority behavior currently wins.
- **Return pending** — waiting for a safe job transition.

Hovering also shows the rule, buffer count, missing gear, destination, or why saved apparel is unavailable. Clicking a worker selects and jumps to that pawn.

### Rule management

Rules are named, enabled/disabled, and saved with the game. **Collapse** creates a compact summary for multi-rule management. **Delete** removes a rule. **Edit map areas** opens RimWorld’s normal area interface, and assigned areas use the native hover overlay.

## Storage filters

The mod adds two special apparel filters:

- **Allow managed outfit gear** — accepts rule-required or currently managed apparel.
- **Allow non-managed apparel** — accepts ordinary apparel not managed by the mod.

For a dedicated locker, enable managed outfit gear and disable non-managed apparel. Dropped managed gear is kept unforbidden so normal hauling can move it. Filters are enforced at both thing-filter and storage-acceptance boundaries for compatibility with alternate storage systems.

## Example scenarios

### Radiation or hazardous laboratory

- Work area: reactor, laboratory, or contaminated room
- Gear: radiation suit and mask
- Locker: storage immediately outside the hazard
- Buffer: 0 for immediate restoration, or 1–2 for a meal or nearby haul
- Disable wandering for animals and robots that should never roam through it

### Freezer clothing

- Work area: freezer
- Gear: parka and cold-weather headwear
- Locker: entrance storage
- Buffer: 1–3 to avoid changing after every short hauling trip

### Fire response

- Work area: a player-created emergency zone
- Gear: fire-resistant apparel from any loaded mod
- Locker: emergency-equipment storage
- Use **Pause work** when the emergency ends, then resume or disable the rule

### Clean room or hospital

- Work area: laboratory, sterile kitchen, or hospital
- Gear: clean-room suit, mask, medical uniform, or role-play apparel
- Disable unrelated wandering and child work watching to limit traffic

### Restricted workshop

- Work area: fabrication or industrial room
- Gear: apron, helmet, respirator, or specialist uniform
- Buffer: several tasks when workers alternate between bills and nearby hauling
- Collapse rules to monitor several specialist workshops cleanly

## Compatibility and boundaries

- Harmony is the only dependency.
- No hazard, apparel, robot, storage, or race mod is hard-coded.
- Modded apparel appears automatically when it is a normal wearable `ThingDef`.
- Modded robots use native mechanoid properties and common mechanical identifiers for access controls.
- Drafted and player-forced behavior is respected rather than aggressively overridden.
- Cooldowns and recovery guards prevent broken jobs or unavailable apparel from causing retry storms.
- Overlapping rules do not yet have configurable priority. Avoid conflicting outfits on the same cells.

## Troubleshooting

### Waiting for saved apparel

Hover the worker. The tooltip reports whether the item is worn by another pawn, in a container, on another map, forbidden, reserved, unreachable, or ready to retrieve. Fix the condition or use **Clear saved owner** if the item should be released.

### Gear is not being stored in the locker

Confirm storage inside the locker accepts the item and has **Allow managed outfit gear** enabled. An eligible hauler must be permitted and able to reach both item and storage.

### A pawn does not change clothing

Confirm the rule is enabled and resumed, the job targets/interacts with the area, the pawn is an eligible undrafted humanlike colonist, slave, prisoner, or hosted guest, its category is permitted, and reachable copies of every required type exist.

### A mech, robot, or animal enters

Check its Hauling and Wandering columns. These units obey access permissions but do not equip apparel.

### Developer logs

With developer mode enabled, successful interceptions, exact-job resumptions, buffer changes, restoration, and safety redirects are logged. A cancelled continuation includes its reason, such as an invalid target, reservation conflict, pause request, or urgent personal job. Identical guest-access diagnostics are limited to once per pawn per in-game day. Repeated task transitions or `10 jobs in one tick` warnings still indicate a bug worth reporting with the current log and a short video.

## Current limitations

- No configurable priority/conflict resolution for overlapping rules.
- No per-pawn assignment filters.
- No direct JobDef, WorkTypeDef, temperature, hediff, or hazard triggers.
- No strict/warning/best-effort modes.
- English-only interface strings.
- Buffer slots are reserved when jobs start, not after successful completion.

## Build

Install RimWorld 1.6 and Harmony, then run:

```powershell
.\build.ps1
```

For a non-default RimWorld location:

```powershell
.\build.ps1 -RimWorldDir "D:\SteamLibrary\steamapps\common\RimWorld"
```

The DLL is written to `1.6\Assemblies\AutomaticOutfitManager.dll`.

## Install for local testing

Copy the repository folder into RimWorld’s `Mods` directory, enable **Harmony** first, then enable **Automatic Outfit Manager**.

See [`PROJECT-DESIGN.md`](PROJECT-DESIGN.md) for implementation scope and future phases.

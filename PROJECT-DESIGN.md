# Automatic Apparel Manager — Project Design

## Project goal

Build a general-purpose RimWorld mod that automatically manages pawn apparel according to context rather than requiring the player to manually switch outfits.

The initial motivating use case is protective equipment for Dubs Rimatomics: when a pawn receives work inside a nuclear/radiation area, the pawn should equip configured radiation PPE before entering and later return to normal clothing. The architecture must remain generic and must not require Rimatomics.

## Core design principles

1. **Generic rules, not hard-coded mod integrations.** Rules operate on RimWorld concepts such as areas, jobs, apparel defs and pawns.
2. **Use vanilla behavior where practical.** The manager uses RimWorld jobs, pathing, reservations, areas and storage settings, adding guards only where ownership and transition ordering require them.
3. **Save-specific configuration.** Player-created rules belong to the current game/save.
4. **Compatibility first.** Avoid replacing core apparel systems when interception and normal jobs can accomplish the behavior.
5. **Incremental milestones.** Establish reliable detection/equipping before implementing restoration, conflicts and more trigger types.

## Phase 1 — Proof of concept

### Trigger

An enabled rule references a RimWorld `Area`. A pawn's incoming job matches when target A, B or C resolves to a cell inside that area.

### Requirements

A rule contains one or more apparel `ThingDef`s. The system checks the pawn's currently worn apparel and determines which configured defs are missing.

### Acquisition

For each missing apparel def, find a spawned copy on the pawn's map that is reachable, not forbidden and reservable.

### Job interception

Harmony patches `Pawn_JobTracker.StartJob`. When an eligible colonist receives a matching job:

1. Preserve the original job.
2. Build vanilla `Wear` jobs for required missing apparel.
3. Put the original job back into the pawn's job queue.
4. Queue any remaining wear jobs ahead of it.
5. Replace the incoming job with the first wear job.

Conceptually:

```text
matching work job
      ↓
required apparel missing?
      ↓ yes
find apparel on map
      ↓
Wear item 1
      ↓
Wear item 2
      ↓
original work job
```

### Pawn eligibility

Phase 1 only acts on player colonists that are not drafted.

### UI

A dedicated **Auto Apparel** main tab provides:

- New rule
- Enable/disable rule
- Rule name
- Existing map area selection
- Arbitrary loaded apparel selection
- Clear apparel
- Delete rule

## Phase 1 baseline limitations

The original Phase 1 baseline did not restore displaced apparel. Phase 2 now addresses this with persistent snapshots, locker-room transitions and ownership protection.

Phase 1 also does not yet provide:

- Rule priority
- Rule conflict resolution
- Grace periods
- Strict entry blocking
- Warnings/best-effort policy modes
- Per-pawn filters
- Work type triggers
- JobDef triggers
- Environmental triggers
- Apparel condition/quality filters
- Storage assignment

## Phase 2 — Apparel state and restoration (implemented and under playtesting)

Pawn-specific, save-persistent runtime state contains:

- Active automatic-apparel rule(s)
- Apparel worn before automatic intervention
- Apparel displaced by PPE
- Transition state
- Original/resumable work context where necessary

When a pawn no longer needs the configured outfit, the manager returns the pawn to the locker room when one is assigned, removes managed work gear, and restores the exact saved personal items. Ordinary work remains blocked while a temporarily unavailable saved item is retried.

Restoration accounts for apparel that is hauled, reserved, temporarily inaccessible or forbidden. Destroyed references are skipped. A player may use **Clear saved owner** to deliberately release an item or resolve a permanently invalid claim.

A future grace period may reduce rapid transitions when a pawn repeatedly crosses a rule boundary.

### Changing areas

Each rule may reference a separate locker-room area. Required apparel stored there is preferred, with suitable gear elsewhere on the map used as a fallback. When triggering work ends, pawns return to the locker room before removing automatic apparel and restoring their saved outfit. Dropped gear remains allowed and RimWorld's normal hauling system places it according to storage priority and the Automatic Apparel special filters. Rules without a locker room retain direct changing behavior.

### Managed apparel and ownership

- Rule-required work gear is shared among eligible colony pawns.
- Displaced personal gear is persistently associated with its original pawn.
- Outfit optimization, direct wear calls and incoming wear jobs reject personal gear saved for another pawn.
- During return/restoration, the owner receives reservation priority over hauling, repair and processing jobs.
- A periodic invariant check removes wrongly worn saved gear if another mod bypasses normal wear validation.
- Non-colony pawns cannot target managed gear, including through modded repair or processing jobs.
- Inspection text exposes item role, saved owner, required work areas and locker areas.

### Storage integration

Two special apparel filters integrate with stockpiles and storage buildings:

- **Allow automatic apparel** accepts rule-required or currently managed items.
- **Allow non-automatic apparel** accepts ordinary apparel.

Filter enforcement occurs at both thing-filter and storage-acceptance boundaries to remain compatible with alternate hauling systems.

## Phase 3 — Rule engine

Expand the trigger model beyond a single area:

- Destination area
- Pawn current area
- JobDef
- WorkTypeDef
- Drafted state
- Temperature/environment
- Hediff or hazard state where appropriate

Introduce rule priority and deterministic conflict resolution.

Potential behavior modes:

- **Strict** — do not proceed into the context without required apparel.
- **Warning** — allow work but surface missing PPE.
- **Best Effort** — equip what can be found and continue.

## Phase 4 — User experience

Improve rule management for larger colonies:

- Searchable apparel selector
- Rule reordering/priority
- Pawn assignment/filtering
- Copy/duplicate rules
- Better rule summaries
- Status/debug inspection for selected pawns
- Messages for unavailable apparel
- Import/export or presets if useful

## Compatibility strategy

The core mod should not reference Rimatomics assemblies. Rimatomics apparel should appear naturally because the UI enumerates loaded apparel defs.

Optional compatibility modules may be introduced later only when a mod requires specialized hazard detection unavailable through generic RimWorld concepts.

Harmony patches should remain narrow and avoid destructive prefixes whenever possible.

## Repository/build structure

```text
AutomaticApparelManager/
├── About/
│   └── About.xml
├── Defs/
│   └── MainButtonDefs/
│       └── MainButtons.xml
├── 1.6/
│   └── Assemblies/
├── Source/
│   ├── Core/
│   ├── Detection/
│   ├── Patches/
│   ├── Rules/
│   ├── UI/
│   └── AutomaticApparel.csproj
├── .gitignore
├── build.ps1
├── PROJECT-DESIGN.md
└── README.md
```

The C# project references RimWorld's local managed assemblies and Harmony rather than committing copyrighted game assemblies or third-party binaries to this repository.

## Development workflow

`main` should represent the current stable development baseline. Substantial features should be developed on branches and reviewed through pull requests before merging.

Recommended next milestone after Phase 2 stabilization: **rule priority/conflict handling, configurable pawn eligibility, and clearer status diagnostics**.

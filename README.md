# Automatic Apparel Manager — Phase 1

Automatic Apparel Manager is a RimWorld 1.6 mod project for context-aware apparel automation.

The long-term goal is to let players define rules that automatically equip appropriate clothing or PPE based on a pawn's context, such as job destination, area, work type, environmental conditions, or other triggers.

The initial proof of concept uses Dubs Rimatomics radiation PPE as the primary real-world test case, but Rimatomics is not a hard dependency.

## Implemented in this milestone

- RimWorld 1.6 mod metadata and Harmony dependency.
- Save-specific `ApparelRule` storage via `GameComponent`.
- Dedicated **Auto Apparel** main tab.
- Create/delete/enable rules.
- Select an existing map area.
- Select arbitrary vanilla or modded apparel `ThingDef`s.
- Harmony interception of `Pawn_JobTracker.StartJob`.
- Detect jobs whose A/B/C target lies inside the configured area.
- Find reachable, reservable copies of missing apparel.
- Queue vanilla `Wear` jobs before the original work job.
- Resume the original job from RimWorld's normal job queue.

## Deliberately not implemented yet

- Restoration of previous clothing.
- Grace period.
- Multiple-rule priority/conflict resolution.
- Strict / Warning / Best Effort behavior.
- Pawn filters beyond player colonists.
- JobDef / WorkType triggers.
- Temperature/environment triggers.

## Build

1. Install RimWorld 1.6 and Harmony.
2. Open PowerShell in this repository.
3. Run:

```powershell
.\build.ps1
```

If RimWorld is installed elsewhere:

```powershell
.\build.ps1 -RimWorldDir "D:\SteamLibrary\steamapps\common\RimWorld"
```

The DLL is written to `1.6\Assemblies\AutomaticApparel.dll`.

## Install for local testing

Copy the repository folder into your RimWorld `Mods` directory, enable **Harmony** first, then enable **Automatic Apparel Manager**.

## First Rimatomics test

1. Load a colony with Dubs Rimatomics.
2. Create or use a RimWorld allowed area covering the reactor room, for example `Nuclear`.
3. Open **Auto Apparel**.
4. Create a rule named `Nuclear PPE`.
5. Select the `Nuclear` area.
6. Add the Rimatomics radiation suit and radiation mask from the apparel menu.
7. Make sure reachable copies are stored on the map and are not forbidden.
8. Allow a colonist to receive a job whose target is inside that area.

Expected prototype behavior:

`reactor job -> Wear PPE item 1 -> Wear PPE item 2 -> original reactor job`

Enable RimWorld dev mode to see successful interception messages in the log.

## Important Phase 1 limitation

The prototype intentionally does **not** restore prior clothing yet. Vanilla `Wear` may drop conflicting apparel while equipping PPE; that clothing remains on the map. Restoration and apparel snapshots are the next milestone.

See [`PROJECT-DESIGN.md`](PROJECT-DESIGN.md) for the full project scope and roadmap.

# Automatic Apparel Manager

Automatic Apparel Manager is a RimWorld 1.6 mod for context-aware outfit and personal protective equipment (PPE) changes.

Players define rules that equip specified apparel before a pawn performs work in a map area. An optional locker-room area keeps outfit changes controlled: the pawn returns there, removes the work gear, and restores the exact personal apparel saved at the start of the intervention.

The initial proof of concept uses Dubs Rimatomics radiation PPE as the primary real-world test case, but Rimatomics is not a hard dependency.

## Current features

- Save-specific, named rules with enable/disable controls.
- Work-area and optional locker-room selection using existing RimWorld areas.
- Native area highlighting when an assigned area is hovered.
- Searchable selection of arbitrary vanilla or modded apparel.
- Automatic collection and wearing of missing rule-required gear before work begins.
- Locker-room preference with map-wide fallback when locating required gear.
- Exact snapshots of displaced personal clothing and mandatory restoration before ordinary work resumes.
- Persistent saved-owner protection so another pawn cannot wear claimed personal apparel.
- Owner-priority reservations while restoring, preventing hauling or repair work from delaying retrieval.
- Inspection details showing whether an item is required gear or saved personal gear, its owner, and associated areas.
- **Jump to owner** and **Clear saved owner** actions on saved apparel.
- **Allow automatic apparel** and **Allow non-automatic apparel** storage filters.
- Dropped managed apparel is kept allowed so normal hauling can move it to suitable storage.
- Non-colony pawns, including guests, cannot reserve, haul, repair, process, or wear managed apparel.
- Player-controlled pawns and robots may still haul or repair managed gear; only the saved owner may wear personal gear.
- Children and other pawns that cannot equip the selected apparel are skipped safely.

## Current limitations and planned expansion

- Grace period between rapid area transitions.
- Multiple-rule priority/conflict resolution.
- Strict / Warning / Best Effort behavior.
- Configurable pawn assignment and filters.
- JobDef / WorkType triggers.
- Temperature/environment triggers.
- Localization beyond the current English interface.

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

## Example: radiation PPE with Dubs Rimatomics

1. Load a colony with Dubs Rimatomics.
2. Create or use a RimWorld allowed area covering the reactor room, for example `Nuclear`.
3. Open **Auto Apparel**.
4. Create a rule named `Nuclear PPE`.
5. Select the `Nuclear` area.
6. Add the Rimatomics radiation suit and radiation mask from the apparel menu.
7. Create a separate locker-room area around dedicated apparel storage and assign it to the rule.
8. In the locker storage settings, enable **Allow automatic apparel**. Disable it on storage that should reject managed gear.
9. Make sure reachable copies are stored on the map and are not forbidden.
10. Allow a colonist to receive a job whose target is inside the work area.

Expected behavior:

`reactor job → obtain and wear PPE → perform work → return to locker room → remove PPE → restore saved clothes → resume ordinary work`

Saved personal gear is reserved logically for its original pawn. If an item is permanently lost or you want another pawn to use it, select the item and choose **Clear saved owner**.

Enable RimWorld dev mode to see successful interception messages in the log.

See [`PROJECT-DESIGN.md`](PROJECT-DESIGN.md) for the full project scope and roadmap.

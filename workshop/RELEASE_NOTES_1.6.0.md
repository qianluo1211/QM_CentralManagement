# Central Material Management 1.6.0

Shuttle restock manifests, mission-briefing access, one-click repair, tech-level
sorting. Verified against game **Patch 1.0.2**.

**GOG / non-Steam users:** download `QM_CentralManagement_v1.6.0.zip` below and
follow the **Manual install** section of [the README](../../blob/main/README.md).
Extract it so the `.dll` sits directly inside a folder named
`QM_CentralManagement`, then move that folder into
`%USERPROFILE%\AppData\LocalLow\Magnum Scriptum Ltd\Quasimorph\LocalUserPresets\`.

---

## New: shuttle restock manifests

The game empties the cargo shuttle's hold after every raid, so it is guaranteed
empty before every deployment — keeping a standing emergency kit aboard meant
re-packing it by hand every time.

- Save the hold as a named list and refill it in one click.
- Two places to do it: the cargo shuttle tab on the pre-departure equipment
  screen, or the new **Shuttle** entry in the central panel's header dropdown.
- **Automatic stocking** (on by default): the hold is topped up when the
  pre-departure screen opens and again as the raid launches, so you never have
  to open the equipment screen at all.
- **Top-up only.** A manifest asking for 20 rounds with 5 aboard moves 15. It
  never removes anything you put there yourself.

## New: central management from the mission briefing

- An entry on the briefing screen, on the same row as *Start operation*, with
  the same `C` shortcut.
- It opens on the operator the briefing **already selected**, with that
  operator's gear expanded. Close it and you are back on the briefing, ready to
  launch — no leaving mission preparation and walking back to the ship.
- Switch it off with `raidPrepCentral=false`.

## New: one-click repair for the selected agent

- Mends all of the selected agent's damaged gear at once, paid out of ship
  storage. Repair kits hold five charges each, so a worn set of armour is dozens
  of drag operations by hand.
- Scope is that agent's equipment slots plus backpack and vest contents. Kits
  the agent is **carrying** are left alone — those were packed for the raid —
  and the recycling bay is never touched.
- **Kits are spent before scrap parts.** Kits give some permanent max durability
  back; rags, springs and plates trade permanent capacity for condition.
- **Nothing is ever repaired to destruction.** Vanilla applies the durability
  change first and only then notices the penalty hit 100%, turning the item into
  scrap. A batch action must not make that call, so the application that would
  finish an item off is skipped.
- Confirms first, then shows a receipt of what was spent — kits counted in
  **uses**, single-use parts in **items**.

## New: sort by tech level

Available in **every** category, alongside name and quantity.

## Also in 1.6.0

- Loadout preset strip on the after-raid screen — applying a preset there is a
  selective unload.
- The central panel's header control is a dropdown (agent gear / shuttle /
  augments / hidden) instead of one button that meant four different things.
- Barter payouts render as an item list with vanilla icons instead of a wall of
  text.
- New **ORDNANCE** category (grenades, mines, turrets, placeable cover). AMMO
  now means ammunition only, and the central and trade panels agree on
  categories.
- Fixed: clicking a mod popup's own dead space no longer dismisses it.
- Fixed: the page counter read one page short on the last page (three pages of
  results showed "2 / 3" on the third screen). Paging moves a whole screen, but
  the final stop is bottom-aligned rather than page-aligned.

## New config options

```
raidPrepCentral=true
shuttleManifests=true
shuttleAutoRestock=true
```

## Upgrade notes

- The preset file format goes 3 → 4 (it gains manifest records). Existing
  loadout presets load unchanged. Rolling back to an older build loses saved
  manifests, not presets.
- Do **not** run the Workshop version and a manual copy at the same time — the
  game scans both locations and would load the mod twice.

## Credits

Shuttle manifests were suggested by **Ashnal**. The mission-briefing entry,
one-click repair (**Grokov**) and tech-level sorting (**Vatick**) all came from
the Workshop comments. Thanks to **Joe** for asking for a non-Steam download —
that is why this repo now carries releases.

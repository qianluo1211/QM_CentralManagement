# QM Central Management (1.6.0)

QM Central Management turns the Magnum's separate storage bays into one searchable logistics terminal. It adds a major Supply technology, a dedicated spaceship action, direct agent loadout management, vanilla augmentation installation, contextual sorting, precise stack handling, protected batch recycling — and since 1.5.0, a central-management-style station trade panel plus optional in-game settings via Crynano's Mod Configuration Menu.

## Station Trade Panel (1.5.0)

- Replaces the vanilla station trade screen (requires the Central Logistics Matrix technology; the game's "Fast trade" option must be on).
- Buy and sell in one panel: the buy page lists the station's entire stock (consumables included, so sold items can be bought back); the sell page lists only items the station accepts and aggregates all your storages. Search, category filters, and sorting by name / price / quantity.
- Column headers explain every number; the buy footer shows the vanilla discount / markup bar; prices follow the vanilla formulas exactly.
- One shared cart with a single Trade action that sells first, then buys; the footer links income, spending, and the resulting balance. `tradeConfirm` adds a confirmation dialog.
- The mouse wheel only pages. Hold the left button and sweep across rows (− subtracts, + adds, MAX fills; Shift/Ctrl/Ctrl+Shift ×10/×100/×1000).
- Shortcuts: B switches panes, [ ] pages, D trades, Delete clears the cart, A selects all visible.
- Barter exchanges are visible: delivering quest items (e.g. the AnCom data chip) shows a dialog listing exactly what the station handed back.
- The panel rebuilds when the game language changes.

## Mod Configuration Menu (1.5.0)

- Optional integration with Crynano's Mod Configuration Menu: when subscribed, `raidPrepCentral`, `shuttleManifests`, `shuttleAutoRestock`, `stationTrade`, `tradeConfirm`, `autoUnlockTech`, and `debugTradeLayout` can be changed from the in-game mod settings page. Changes apply immediately and are persisted to the mod's own `mcm_settings.txt` (works even without MCM next time).
- MCM entries follow the game language.

## Features

- Adds named loadout presets above the expanded agent inventory. Presets can be saved/overwritten, selected from a dropdown, deleted, and applied in one click.
- A preset captures equipped weapons and gear, mechanical replacement parts, the real implant-socket capacity of those parts, installed implants, and every item/quantity in the backpack and vest quick slots.
- Before changing anything, the mod checks all central storage, augmentation facilities and research, body compatibility, implant capacity, locked equipment, and available equipment slots. If only items are missing, the player may force a partial application: missing ordinary gear slots stay empty, missing carried items are skipped, and an incomplete body preset leaves the current body intact.
- Presets written by older versions remain compatible and do not clear backpack contents that the old format never recorded.
- Custom controls use the vanilla button click sound, while successful preset equipment and carried-item transfers use the original equip/take sounds.
- Matching mechanical parts are kept in place. When several matching items exist, the preset system prefers a usable high-condition instance that has enough real implant sockets.
- Indexes storage bays 1-7, built cold storage, and available recycling storage.
- Searches by localized item name or internal ID and filters by category and subtype.
- Uses a responsive full-screen catalog or a side-by-side layout with the selected agent's equipment and backpack.
- Opens with the most recently deployed available agent and provides a direct agent dropdown.
- Supports normal drag-and-drop in both directions. Vanilla right-click stack splitting immediately puts the chosen quantity on the cursor.
- Sorts by tech level in every category, reading the item's own `TechLevel` (highest first), alongside name and quantity.
- Adds a REPAIR action to the footer that repairs all of the **selected agent's** damaged gear in one click, through the game's own repair logic, so compatibility rules, durability multipliers and consumption match repairing by hand exactly.
  - Scope is the agent's `Inventory.AllContainers`: every equipment slot plus the backpack and vest contents, i.e. exactly what the panel's agent pane shows. The button is disabled with no agent selected.
  - Supplies come from ship storage only (bays 1-7 and the cold storage). The recycling bay is left alone (its contents are queued for destruction), and so are kits the agent is carrying, since those were packed to be taken on the raid; drag those on by hand if you want them spent.
  - Spends repair kits before scrap parts: kits carry a positive `MaxCapacity` and give some permanent max durability back, while rags, springs and plates carry a negative one and trade permanent capacity for condition. Taking whichever matched first would quietly grind good gear down.
  - Never repairs anything to destruction. Vanilla applies the durability change first and only then notices the max-durability penalty hit 100%, replacing the item with scrap; a batch action must not make that call, so the application that would finish an item off is skipped (the penalty is projected through the agent's durability multipliers, exactly as the game scales it).
  - Equivalent to repairing by hand: every successful application raises the same repair perk progress, and the agent's resists are recomputed afterwards because worn armor changed.
  - Asks for confirmation first with the game's own dialog, the same as batch recycling, and shows a receipt afterwards listing every consumable actually spent.
- Sorts ordinary items by name or quantity; weapons and ammunition by damage or damage type; equipment by total or typed resistance; official armor sets by the game's own ArmorSets data; and augments by real installation slot.
- Opens the game's original Augmentation screen inside the central workflow, preserving vanilla installation rules and effects.
- Supports fast whole-type selection, exact unit counts, select/clear current filter, confirmation, and safety exclusions for batch recycling.
- Mouse wheel changes pages and adjusts the vanilla split slider.
- Adds the configurable `C` shortcut on the spaceship screen and on the mission briefing.
- Adds a central management button to the mission briefing, on the same row as "Start operation". It opens the panel on the operator the briefing has already selected, with that operator's gear expanded beside the catalogue, and closing it returns to the briefing (whose equipment-weight readout refreshes). It appears under the same conditions as the vanilla "select equipment" button -- technology unlocked and an operator chosen -- and can be switched off with `raidPrepCentral`.

## Research

Research the major **Central Logistics Matrix** technology below Recycling in the Supply technology tree.

Cost:

- Communication Relay x1
- Automap Device x1
- Electrical Parts Container x1
- AI Module x1

## Compatibility

- Quasimorph 1.0 and 1.0.1
- Loads through the official Mod/Hook system and uses the Harmony library bundled with the game.
- Standalone, with no required Workshop dependencies.
- Optional integration with Crynano's Mod Configuration Menu.

## Configuration

Edit `config.txt`:

- `debugLogging=false`: detailed messages in `Player.log`.
- `recycleConfirmSeconds=4`: obsolete (batch recycling now uses the vanilla confirm dialog); kept only for old config files.
- `shortcutKey=C`: Unity `KeyCode` for the spaceship and mission-briefing shortcut; use `None` to disable it.
- `raidPrepCentral=true`: central management button on the mission briefing screen.
- `stationTrade=true`: replace the vanilla station trade screen with this mod's panel (gate = this switch + the Central Logistics Matrix technology unlocked).
- `autoUnlockTech=false`: unlock the technology on every save, including new games.
- `tradeConfirm=false`: confirm before completing the whole deal.
- `debugTradeLayout=false`: dump the vanilla trade screen hierarchy into `Player.log` (diagnostics).
- `quantityShiftStep=10` / `quantityCtrlStep=100` / `quantityCtrlShiftStep=1000`: Shift / Ctrl / Ctrl+Shift steps for clicks and sweeping.
- `shortcutTogglePane=B`, `shortcutPrevPage=LeftBracket`, `shortcutNextPage=RightBracket`, `shortcutTrade=D`, `shortcutClearCart=Delete`, `shortcutSelectAll=A`: trade panel shortcuts (`None` disables one).

With Crynano's Mod Configuration Menu subscribed, `raidPrepCentral`, `shuttleManifests`, `shuttleAutoRestock`, `stationTrade`, `tradeConfirm`, `autoUnlockTech`, and `debugTradeLayout` can also be changed from the in-game mod settings page; changes apply immediately and are written to `mcm_settings.txt`.

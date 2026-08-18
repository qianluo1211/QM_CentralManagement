using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// A saved stocking list for the cargo shuttle's in-mission storage
    /// (<c>ShuttleCargoDepartment.ShuttleCargo</c>, the "cargoshuttle"
    /// technology).
    ///
    /// Item ids and unit counts only, deliberately: durability and expiry are
    /// properties of the individual item, and the restock takes whatever the
    /// hold actually has. Same shape as <see cref="LoadoutCarriedEntry"/>.
    /// </summary>
    [Serializable]
    public sealed class ShuttleManifest
    {
        public string Id;
        public string Name;
        public long UpdatedUtcTicks;
        public List<ShuttleManifestEntry> Items =
            new List<ShuttleManifestEntry>();
    }

    [Serializable]
    public sealed class ShuttleManifestEntry
    {
        public string ItemId;
        public int Units;
    }

    /// <summary>
    /// Manifest storage. A thin facade over
    /// <see cref="LoadoutPresetRepository"/>: the manifests share that file
    /// and therefore its atomic, round-trip-verified write.
    ///
    /// Manifests are deliberately NOT part of a loadout preset. `ShuttleCargo`
    /// hangs off `MagnumProgression`, so it is one storage per SHIP, while a
    /// loadout preset is per mercenary — attaching one to the other would mean
    /// switching the agent you are equipping silently re-stocks the shuttle.
    /// </summary>
    internal static class ShuttleManifestRepository
    {
        private static LoadoutPresetCollection Data =>
            LoadoutPresetRepository.Data;

        internal static List<ShuttleManifest> All => Data.Manifests;

        internal static ShuttleManifest Selected
        {
            get
            {
                var id = Data.SelectedManifestId;
                return Data.Manifests.FirstOrDefault(m =>
                    m != null && m.Id == id);
            }
        }

        internal static ShuttleManifest Save(string name,
            List<ShuttleManifestEntry> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));
            name = LoadoutPresetRepository.ClampName(name, SuggestName());
            var manifest = Data.Manifests.FirstOrDefault(m =>
                m != null && string.Equals(m.Name, name,
                    StringComparison.CurrentCultureIgnoreCase));
            if (manifest == null)
            {
                manifest = new ShuttleManifest
                {
                    Id = Guid.NewGuid().ToString("N"),
                };
                Data.Manifests.Add(manifest);
            }
            manifest.Name = name;
            manifest.Items = items;
            manifest.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            Data.SelectedManifestId = manifest.Id;
            LoadoutPresetRepository.PersistShared();
            return manifest;
        }

        internal static void Select(string id)
        {
            Data.SelectedManifestId = id;
            LoadoutPresetRepository.PersistShared();
        }

        internal static void Delete(string id)
        {
            Data.Manifests.RemoveAll(m => m == null || m.Id == id);
            if (Data.SelectedManifestId == id)
            {
                Data.SelectedManifestId =
                    Data.Manifests.FirstOrDefault()?.Id;
            }
            LoadoutPresetRepository.PersistShared();
        }

        internal static string SuggestName()
        {
            var used = new HashSet<string>(Data.Manifests
                    .Where(m => m != null).Select(m => m.Name),
                StringComparer.CurrentCultureIgnoreCase);
            for (var i = 1; i < 1000; i++)
            {
                var candidate = string.Format(
                    Localization.Get("qmcentral.shuttle_default_name"), i);
                if (!used.Contains(candidate))
                    return candidate;
            }
            return Localization.Get("qmcentral.preset_default_name_fallback");
        }
    }

    internal sealed class ShuttleRestockResult
    {
        internal bool Success;
        /// <summary>Nothing to do: the shuttle already holds the manifest.</summary>
        internal bool AlreadyStocked;
        internal int UnitsMoved;
        /// <summary>Item lines the hold could not supply in full.</summary>
        internal readonly List<string> Missing = new List<string>();
        /// <summary>Item lines that ran out of room in the shuttle.</summary>
        internal readonly List<string> NoRoom = new List<string>();
        /// <summary>Set when the restock aborted outright.</summary>
        internal string Error;

        internal IEnumerable<string> AllIssues => Missing.Concat(NoRoom);
    }

    /// <summary>
    /// Reads and fills the shuttle hold.
    ///
    /// Why this exists at all: `MagnumCargoSystem.ReturnShuttleItems` runs
    /// after every raid, returns the whole shuttle to cargo and calls
    /// `RemoveAllItems()`. The shuttle is therefore empty before every single
    /// deployment by design, and re-stocking it by hand every time is the
    /// tedium this replaces.
    /// </summary>
    internal static class ShuttleManifestService
    {
        /// <summary>
        /// The shuttle hold, or null when the technology is not built. Every
        /// caller gates on this rather than on a cached department: the
        /// department object exists whether or not the perk was purchased.
        /// </summary>
        internal static ItemStorage StorageOf(MagnumProgression progression)
        {
            var department = progression
                ?.GetDepartment<ShuttleCargoDepartment>();
            if (department == null || !department.IsActiveDepartment())
                return null;
            return department.ShuttleCargo;
        }

        /// <summary>
        /// What is in the hold right now, one line per item type, in a stable
        /// order so re-saving an unchanged shuttle produces an identical file.
        /// </summary>
        internal static List<ShuttleManifestEntry> Capture(
            ItemStorage shuttle)
        {
            var result = new List<ShuttleManifestEntry>();
            if (shuttle == null)
                return result;
            var index = new Dictionary<string, ShuttleManifestEntry>(
                StringComparer.Ordinal);
            foreach (var item in shuttle.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id)
                    || item.StackCount <= 0)
                {
                    continue;
                }
                if (index.TryGetValue(item.Id, out var entry))
                {
                    entry.Units += item.StackCount;
                    continue;
                }
                entry = new ShuttleManifestEntry
                {
                    ItemId = item.Id,
                    Units = item.StackCount,
                };
                index[item.Id] = entry;
                result.Add(entry);
            }
            result.Sort((a, b) => string.Compare(a.ItemId, b.ItemId,
                StringComparison.Ordinal));
            return result;
        }

        /// <summary>
        /// How many units of each manifest line are still missing from the
        /// hold. Top-up semantics: whatever the player already put in there
        /// counts, and anything extra is left strictly alone.
        /// </summary>
        internal static List<ShuttleManifestEntry> Shortfall(
            ShuttleManifest manifest, ItemStorage shuttle)
        {
            var result = new List<ShuttleManifestEntry>();
            if (manifest?.Items == null || shuttle == null)
                return result;
            foreach (var entry in manifest.Items)
            {
                if (entry == null || entry.Units <= 0
                    || string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    continue;
                }
                var missing = entry.Units - shuttle.CountItems(entry.ItemId);
                if (missing > 0)
                {
                    result.Add(new ShuttleManifestEntry
                    {
                        ItemId = entry.ItemId,
                        Units = missing,
                    });
                }
            }
            return result;
        }

        internal static string Summary(ShuttleManifest manifest)
        {
            if (manifest?.Items == null || manifest.Items.Count == 0)
                return Localization.Get("qmcentral.shuttle_summary_empty");
            var types = 0;
            var units = 0;
            foreach (var entry in manifest.Items)
            {
                if (entry == null || entry.Units <= 0)
                    continue;
                types++;
                units += entry.Units;
            }
            return string.Format(
                Localization.Get("qmcentral.shuttle_summary"), types, units);
        }

        /// <summary>
        /// Moves the shortfall from the ship's holds into the shuttle.
        ///
        /// The sourcing rules are the loadout presets' own, reached through
        /// <see cref="LoadoutPresetService.TakeFromCargo"/>: ship cargo, then
        /// the fridge, then the recycler when it is idle; locked items are
        /// never taken; the best-condition instance wins; stacks split with
        /// their expiry time intact. The shuttle hold is not among those
        /// sources, so a restock can never feed on itself.
        /// </summary>
        internal static ShuttleRestockResult Restock(ShuttleManifest manifest,
            MagnumCargo cargo, MagnumProgression progression,
            SpaceTime spaceTime, ItemStorage activeCargo)
        {
            var result = new ShuttleRestockResult();
            var shuttle = StorageOf(progression);
            if (shuttle == null || cargo == null || manifest == null)
            {
                result.Error = Localization.Get(
                    "qmcentral.shuttle_unavailable");
                return result;
            }

            var shortfall = Shortfall(manifest, shuttle);
            if (shortfall.Count == 0)
            {
                result.Success = true;
                result.AlreadyStocked = true;
                return result;
            }

            // Any mod that moves items owes the player a count check across
            // every storage it touches.
            var before = TotalUnits(cargo, progression, shuttle);
            try
            {
                foreach (var entry in shortfall)
                    MoveOneLine(entry, shuttle, cargo, progression,
                        spaceTime, activeCargo, result);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "shuttle restock failed: " + e);
                result.Error = Localization.Get("qmcentral.shuttle_error");
                return result;
            }
            finally
            {
                shuttle.RecalculateWeight();
            }

            // Logged, not turned into a failure. Two reasons. It cannot roll
            // anything back -- the items have already moved by the time this
            // runs, so reporting "failed" would be theatre over a completed
            // transfer. And a mismatch is not proof of loss: merging a full
            // stack into a partially-used one goes through
            // ItemInteractionSystem.Merge's usable-item branch, which moves
            // CHARGES and zeroes the source stack, so the unit tally
            // legitimately drops for medkits and the like. Same call the
            // station trade panel's sell path makes.
            var after = TotalUnits(cargo, progression, shuttle);
            if (before != after)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "shuttle restock changed the item count: "
                               + before + " -> " + after
                               + " (manifest '" + manifest.Name + "'). "
                               + "Expected only for usable items being "
                               + "re-stacked by charge.");
            }

            result.Success = true;
            return result;
        }

        private static void MoveOneLine(ShuttleManifestEntry entry,
            ItemStorage shuttle, MagnumCargo cargo,
            MagnumProgression progression, SpaceTime spaceTime,
            ItemStorage activeCargo, ShuttleRestockResult result)
        {
            var remaining = entry.Units;
            while (remaining > 0)
            {
                var source = LoadoutPresetService.TakeFromCargo(cargo,
                    progression, entry.ItemId, remaining, out _);
                if (source == null)
                {
                    result.Missing.Add(string.Format(
                        Localization.Get("qmcentral.shuttle_missing"),
                        ItemName(entry.ItemId), remaining));
                    return;
                }

                // Cargo fragments a stackable into several partial stacks, so
                // merging first can fill the line without spending another
                // shuttle cell. A merge can also be PARTIAL -- the shuttle's
                // own stack fills up and the rest stays behind -- so the
                // units that got through are counted from the stack itself
                // rather than assumed to be all of them.
                if (source.IsStackable)
                {
                    var beforeMerge = (int)source.StackCount;
                    if (ItemInteractionSystem.TryMergeIntoStorage(shuttle,
                            source, spaceTime, out var emptyAfterMerge))
                    {
                        var merged = emptyAfterMerge
                            ? beforeMerge
                            : beforeMerge - source.StackCount;
                        remaining -= merged;
                        result.UnitsMoved += merged;
                        if (emptyAfterMerge)
                            continue;
                    }
                }

                var moving = (int)source.StackCount;
                if (moving > 0 && ItemInteractionSystem.Move(source, shuttle,
                        CellPosition.Zero, sendEvent: true))
                {
                    remaining -= moving;
                    result.UnitsMoved += moving;
                    continue;
                }

                // Out of room. A split stack belongs to no storage at this
                // point, so it goes back to the hold first: an item the
                // player owned must never evaporate.
                if (source.Storage == null)
                    ReturnToCargo(source, cargo, activeCargo);
                if (remaining > 0)
                {
                    result.NoRoom.Add(string.Format(
                        Localization.Get("qmcentral.shuttle_no_room"),
                        ItemName(entry.ItemId), remaining));
                }
                return;
            }
        }

        private static void ReturnToCargo(BasePickupItem item,
            MagnumCargo cargo, ItemStorage activeCargo)
        {
            var target = activeCargo ?? cargo.ShipCargo.FirstOrDefault();
            if (target != null)
                target.AddItemAndReshuffleOptional(item);
        }

        /// <summary>
        /// Every storage a restock can move items between. Counts UNITS, not
        /// stacks: splitting and merging change the stack count constantly
        /// and only the units are conserved.
        /// </summary>
        private static int TotalUnits(MagnumCargo cargo,
            MagnumProgression progression, ItemStorage shuttle)
        {
            var total = 0;
            foreach (var storage in LoadoutPresetService.CargoStorages(cargo,
                         progression))
            {
                total += UnitsIn(storage);
            }
            return total + UnitsIn(shuttle);
        }

        private static int UnitsIn(ItemStorage storage)
        {
            if (storage == null)
                return 0;
            var total = 0;
            foreach (var item in storage.Items)
            {
                if (item != null)
                    total += item.StackCount;
            }
            return total;
        }

        private static string ItemName(string itemId)
        {
            var key = "item." + itemId + ".name";
            var name = Localization.Get(key);
            return string.IsNullOrWhiteSpace(name) || name == key
                ? itemId
                : name;
        }
    }
}

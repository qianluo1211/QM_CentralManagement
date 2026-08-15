using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    internal sealed class CentralInventoryEntry
    {
        internal string Id;
        internal string Name;
        internal ItemClass ItemClass;
        internal ItemRecord ItemRecord;
        internal WeaponRecord Weapon;
        internal AmmoRecord Ammo;
        internal ResistRecord Resist;
        internal ImplantRecord Implant;
        internal AugmentationRecord Augmentation;
        internal readonly List<BasePickupItem> Stacks =
            new List<BasePickupItem>();
        internal int Units;
        internal BasePickupItem Representative;
        internal bool HasRecyclable;
    }

    /// <summary>
    /// Owns the read-only, aggregated view of every reachable ship storage.
    /// UI state, filters, recycling selections and drag operations deliberately
    /// stay outside this class so rebuilding the index cannot mutate inventory.
    /// </summary>
    internal sealed class CentralInventoryIndex
    {
        internal readonly Dictionary<string, CentralInventoryEntry> Entries =
            new Dictionary<string, CentralInventoryEntry>(
                StringComparer.Ordinal);

        internal readonly Dictionary<string, string> EquipmentSetKeys =
            new Dictionary<string, string>(StringComparer.Ordinal);

        internal void Rebuild(MagnumCargo cargo,
            MagnumProgression progression,
            IDictionary<string, BasePickupItem> preferredStacks,
            Func<BasePickupItem, bool> canRecycle)
        {
            Entries.Clear();
            EquipmentSetKeys.Clear();
            if (cargo == null)
                return;

            foreach (var storage in cargo.ShipCargo)
                IndexStorage(storage);
            if (progression?.HasStoreFridge == true)
                IndexStorage(cargo.FridgeStorage);
            if (progression?.HasStoreConstructorDepartment == true
                && !cargo.RecyclingInProgress)
            {
                IndexStorage(cargo.RecyclingStorage);
            }

            foreach (var entry in Entries.Values)
            {
                entry.Name = GetDisplayName(entry.Id);
                entry.Representative = PickRepresentative(entry,
                    preferredStacks);
                entry.HasRecyclable = canRecycle != null
                    && entry.Stacks.Any(canRecycle);
            }
            BuildEquipmentSetKeys();
        }

        private void IndexStorage(ItemStorage storage)
        {
            if (storage == null)
                return;
            foreach (var item in storage.Items)
            {
                if (item == null)
                    continue;
                if (!Entries.TryGetValue(item.Id, out var entry))
                {
                    var composite = Data.Items.GetRecord(item.Id)
                        as CompositeItemRecord;
                    var itemRecord = composite?.GetRecord<ItemRecord>();
                    entry = new CentralInventoryEntry
                    {
                        Id = item.Id,
                        ItemClass = itemRecord?.ItemClass ?? ItemClass.None,
                        ItemRecord = itemRecord,
                        Weapon = composite?.GetRecord<WeaponRecord>(),
                        Ammo = composite?.GetRecord<AmmoRecord>(),
                        Resist = composite?.GetRecord<ResistRecord>(),
                        Implant = composite?.GetRecord<ImplantRecord>(),
                        Augmentation = composite
                            ?.GetRecord<AugmentationRecord>()
                    };
                    Entries.Add(item.Id, entry);
                }
                entry.Stacks.Add(item);
                entry.Units += Mathf.Max(1, item.StackCount);
            }
        }

        private void BuildEquipmentSetKeys()
        {
            if (Data.ArmorSets == null)
                return;
            foreach (var set in Data.ArmorSets.Records)
            {
                if (set == null || string.IsNullOrWhiteSpace(set.Id)
                    || set.Items == null || set.Items.Count < 2)
                {
                    continue;
                }

                var classes = new HashSet<ItemClass>();
                foreach (var itemId in set.Items)
                {
                    if (Entries.TryGetValue(itemId, out var entry)
                        && IsArmorSetPiece(entry.ItemClass))
                    {
                        classes.Add(entry.ItemClass);
                    }
                }
                if (classes.Count < 2)
                    continue;
                foreach (var itemId in set.Items)
                    if (Entries.ContainsKey(itemId))
                        EquipmentSetKeys[itemId] = set.Id;
            }
        }

        private static BasePickupItem PickRepresentative(
            CentralInventoryEntry entry,
            IDictionary<string, BasePickupItem> preferredStacks)
        {
            if (preferredStacks != null
                && preferredStacks.TryGetValue(entry.Id, out var preferred))
            {
                if (preferred != null && entry.Stacks.Contains(preferred))
                    return preferred;
                preferredStacks.Remove(entry.Id);
            }

            BasePickupItem best = null;
            foreach (var item in entry.Stacks)
            {
                if (best == null || item.StackCount > best.StackCount)
                    best = item;
            }
            return best;
        }

        private static bool IsArmorSetPiece(ItemClass itemClass)
        {
            return itemClass == ItemClass.Helmet
                   || itemClass == ItemClass.Armor
                   || itemClass == ItemClass.Leggings
                   || itemClass == ItemClass.Boots;
        }

        private static string GetDisplayName(string itemId)
        {
            var key = "item." + itemId + ".name";
            var result = Localization.Get(key);
            return string.IsNullOrEmpty(result) || result == key
                ? itemId
                : result;
        }
    }
}

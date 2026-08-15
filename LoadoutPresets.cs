using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    [Serializable]
    public sealed class LoadoutPresetCollection
    {
        public int Version = 1;
        public string SelectedId;
        public List<LoadoutPreset> Presets = new List<LoadoutPreset>();
    }

    [Serializable]
    public sealed class LoadoutPreset
    {
        public string Id;
        public string Name;
        public string SourceProfileId;
        public string SourceBodyTypeId;
        public long UpdatedUtcTicks;
        public List<LoadoutEquipmentEntry> Equipment =
            new List<LoadoutEquipmentEntry>();
        public List<string> Augmentations = new List<string>();
        public List<LoadoutSocketEntry> Sockets =
            new List<LoadoutSocketEntry>();
        public List<LoadoutImplantEntry> Implants =
            new List<LoadoutImplantEntry>();
        // Version 3 presets explicitly own the carried inventory state.  Old
        // presets leave this false so applying one cannot unexpectedly empty a
        // backpack that the old format was incapable of recording.
        public bool IncludesCarriedItems;
        public List<LoadoutCarriedEntry> CarriedItems =
            new List<LoadoutCarriedEntry>();
    }

    [Serializable]
    public sealed class LoadoutEquipmentEntry
    {
        public string Slot;
        public string ItemId;
    }

    [Serializable]
    public sealed class LoadoutSocketEntry
    {
        public string WoundSlotId;
        public string SlotType;
        public int TotalSockets;
    }

    [Serializable]
    public sealed class LoadoutImplantEntry
    {
        public string WoundSlotId;
        public string SlotType;
        public string ItemId;
        public int Order;
    }

    [Serializable]
    public sealed class LoadoutCarriedEntry
    {
        public string Storage;
        public string ItemId;
        public int Units;
    }

    internal static class LoadoutPresetRepository
    {
        private const string StorageKey =
            "QM_CentralManagement_LoadoutPresets_v1";
        private static LoadoutPresetCollection _data;

        internal static LoadoutPresetCollection Data
        {
            get
            {
                if (_data == null)
                    _data = Load();
                return _data;
            }
        }

        internal static LoadoutPreset Selected
        {
            get
            {
                var selectedId = Data.SelectedId;
                return Data.Presets.FirstOrDefault(p =>
                    p != null && p.Id == selectedId);
            }
        }

        internal static LoadoutPreset Save(string name,
            LoadoutPreset snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            name = NormalizeName(name);
            var existing = Data.Presets.FirstOrDefault(p =>
                p != null && string.Equals(p.Name, name,
                    StringComparison.CurrentCultureIgnoreCase));
            if (existing != null)
            {
                snapshot.Id = existing.Id;
                var index = Data.Presets.IndexOf(existing);
                Data.Presets[index] = snapshot;
            }
            else
            {
                snapshot.Id = Guid.NewGuid().ToString("N");
                Data.Presets.Add(snapshot);
            }
            snapshot.Name = name;
            snapshot.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
            Data.SelectedId = snapshot.Id;
            Persist();
            return snapshot;
        }

        internal static void Select(string id)
        {
            Data.SelectedId = id;
            Persist();
        }

        internal static void Delete(string id)
        {
            Data.Presets.RemoveAll(p => p == null || p.Id == id);
            if (Data.SelectedId == id)
                Data.SelectedId = Data.Presets.FirstOrDefault()?.Id;
            Persist();
        }

        internal static string SuggestName()
        {
            var used = new HashSet<string>(Data.Presets
                .Where(p => p != null)
                .Select(p => p.Name), StringComparer.CurrentCultureIgnoreCase);
            for (var i = 1; i < 1000; i++)
            {
                var candidate = string.Format(
                    Localization.Get("qmcentral.preset_default_name"), i);
                if (!used.Contains(candidate))
                    return candidate;
            }
            return Localization.Get("qmcentral.preset_default_name_fallback");
        }

        private static string NormalizeName(string name)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = SuggestName();
            return name.Length <= 28 ? name : name.Substring(0, 28);
        }

        // Storage format, hand written and hand parsed.
        //
        // UnityEngine.JsonUtility is NOT usable for this data: it silently
        // drops the whole List<LoadoutPreset> and emits only
        // {"Version":1,"SelectedId":"..."}. That was verified from the game's
        // own log with the round-trip check below -- 2 presets in, 0 out --
        // and it happens with the DTOs declared public just as it did when they
        // were internal, so it is not an accessibility problem. Rather than
        // keep guessing at the engine serialiser, the file is written directly.
        //
        // The layout is line based and tab separated, matching the game's own
        // config files: one record per line, first field is the record type.
        //
        //   V   <formatVersion>
        //   SEL <selectedPresetId>
        //   P   <id> <name> <profileId> <bodyTypeId> <updatedUtcTicks> <carriesItems>
        //   E   <slot> <itemId>                       equipment, belongs to P
        //   A   <augmentationId>                      augmentation, belongs to P
        //   S   <woundSlotId> <slotType> <sockets>    socket, belongs to P
        //   I   <woundSlotId> <slotType> <itemId> <order>   implant, belongs to P
        //   C   <storage> <itemId> <units>              carried item, belongs to P
        //
        // Every E/A/S/I/C line attaches to the most recent P line.
        private const int FileVersion = 3;
        private const string FileHeader =
            "# QM_CentralManagement loadout presets - tab separated, one record per line";

        private static string StoragePath =>
            Path.Combine(Application.persistentDataPath,
                "QM_CentralManagement_LoadoutPresets.txt");

        private static LoadoutPresetCollection Load()
        {
            try
            {
                var path = StoragePath;
                if (File.Exists(path))
                    return Parse(File.ReadAllLines(path));
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not read " + StoragePath + ": " + e);
            }
            return new LoadoutPresetCollection();
        }

        private static LoadoutPresetCollection Parse(string[] lines)
        {
            var result = new LoadoutPresetCollection();
            LoadoutPreset current = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] == '#')
                    continue;
                var f = line.Split('\t');
                switch (f[0])
                {
                    case "V":
                        if (f.Length > 1 && int.TryParse(f[1], out var version))
                            result.Version = version;
                        break;
                    case "SEL":
                        result.SelectedId = f.Length > 1 && f[1].Length > 0
                            ? f[1]
                            : null;
                        break;
                    case "P":
                        if (f.Length < 6)
                            break;
                        current = new LoadoutPreset
                        {
                            Id = f[1],
                            Name = f[2],
                            SourceProfileId = Blank(f[3]),
                            SourceBodyTypeId = Blank(f[4]),
                            UpdatedUtcTicks = long.TryParse(f[5],
                                NumberStyles.Integer, CultureInfo.InvariantCulture,
                                out var ticks) ? ticks : 0L,
                            IncludesCarriedItems = f.Length >= 7
                                && ParseInt(f[6]) != 0,
                        };
                        result.Presets.Add(current);
                        break;
                    case "E":
                        if (current != null && f.Length >= 3)
                        {
                            current.Equipment.Add(new LoadoutEquipmentEntry
                            {
                                Slot = f[1],
                                ItemId = f[2],
                            });
                        }
                        break;
                    case "A":
                        if (current != null && f.Length >= 2)
                            current.Augmentations.Add(f[1]);
                        break;
                    case "S":
                        if (current != null && f.Length >= 4)
                        {
                            current.Sockets.Add(new LoadoutSocketEntry
                            {
                                WoundSlotId = f[1],
                                SlotType = f[2],
                                TotalSockets = ParseInt(f[3]),
                            });
                        }
                        break;
                    case "I":
                        if (current != null && f.Length >= 5)
                        {
                            current.Implants.Add(new LoadoutImplantEntry
                            {
                                WoundSlotId = f[1],
                                SlotType = f[2],
                                ItemId = f[3],
                                Order = ParseInt(f[4]),
                            });
                        }
                        break;
                    case "C":
                        if (current != null && f.Length >= 4)
                        {
                            var units = ParseInt(f[3]);
                            if (!string.IsNullOrWhiteSpace(f[1])
                                && !string.IsNullOrWhiteSpace(f[2])
                                && units > 0)
                            {
                                current.CarriedItems.Add(
                                    new LoadoutCarriedEntry
                                    {
                                        Storage = f[1],
                                        ItemId = f[2],
                                        Units = units
                                    });
                            }
                        }
                        break;
                }
            }

            result.Presets.RemoveAll(p => p == null
                                          || string.IsNullOrEmpty(p.Id));
            return result;
        }

        private static string Serialize(LoadoutPresetCollection data)
        {
            var sb = new StringBuilder();
            sb.Append(FileHeader).Append('\n');
            sb.Append("V\t").Append(FileVersion).Append('\n');
            sb.Append("SEL\t").Append(Field(data.SelectedId)).Append('\n');

            foreach (var preset in data.Presets)
            {
                if (preset == null || string.IsNullOrEmpty(preset.Id))
                    continue;
                sb.Append("P\t").Append(Field(preset.Id))
                    .Append('\t').Append(Field(preset.Name))
                    .Append('\t').Append(Field(preset.SourceProfileId))
                    .Append('\t').Append(Field(preset.SourceBodyTypeId))
                    .Append('\t').Append(preset.UpdatedUtcTicks
                        .ToString(CultureInfo.InvariantCulture))
                    .Append('\t').Append(preset.IncludesCarriedItems ? "1" : "0")
                    .Append('\n');

                foreach (var e in preset.Equipment ?? EmptyEquipment)
                {
                    if (e != null)
                    {
                        sb.Append("E\t").Append(Field(e.Slot))
                            .Append('\t').Append(Field(e.ItemId)).Append('\n');
                    }
                }
                foreach (var augmentationId in preset.Augmentations
                                               ?? EmptyStrings)
                {
                    if (!string.IsNullOrEmpty(augmentationId))
                        sb.Append("A\t").Append(Field(augmentationId)).Append('\n');
                }
                foreach (var s in preset.Sockets ?? EmptySockets)
                {
                    if (s != null)
                    {
                        sb.Append("S\t").Append(Field(s.WoundSlotId))
                            .Append('\t').Append(Field(s.SlotType))
                            .Append('\t').Append(s.TotalSockets
                                .ToString(CultureInfo.InvariantCulture))
                            .Append('\n');
                    }
                }
                foreach (var i in preset.Implants ?? EmptyImplants)
                {
                    if (i != null)
                    {
                        sb.Append("I\t").Append(Field(i.WoundSlotId))
                            .Append('\t').Append(Field(i.SlotType))
                            .Append('\t').Append(Field(i.ItemId))
                            .Append('\t').Append(i.Order
                                .ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                    }
                }
                foreach (var carried in preset.CarriedItems
                                           ?? EmptyCarriedItems)
                {
                    if (carried == null || carried.Units <= 0
                        || string.IsNullOrWhiteSpace(carried.Storage)
                        || string.IsNullOrWhiteSpace(carried.ItemId))
                    {
                        continue;
                    }
                    sb.Append("C\t").Append(Field(carried.Storage))
                        .Append('\t').Append(Field(carried.ItemId))
                        .Append('\t').Append(carried.Units
                            .ToString(CultureInfo.InvariantCulture))
                        .Append('\n');
                }
            }
            return sb.ToString();
        }

        private static readonly List<LoadoutEquipmentEntry> EmptyEquipment =
            new List<LoadoutEquipmentEntry>();
        private static readonly List<LoadoutSocketEntry> EmptySockets =
            new List<LoadoutSocketEntry>();
        private static readonly List<LoadoutImplantEntry> EmptyImplants =
            new List<LoadoutImplantEntry>();
        private static readonly List<LoadoutCarriedEntry> EmptyCarriedItems =
            new List<LoadoutCarriedEntry>();
        private static readonly List<string> EmptyStrings = new List<string>();

        /// <summary>
        /// Tabs and newlines are the only characters that could break the
        /// format, and a preset name is the only free text in it.
        /// </summary>
        private static string Field(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace('\t', ' ').Replace('\r', ' ')
                .Replace('\n', ' ');
        }

        private static string Blank(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var result) ? result : 0;
        }

        private static void Persist()
        {
            try
            {
                var text = Serialize(Data);

                // Kept from the JsonUtility era, and it is what caught that
                // serialiser losing everything. Write to a temp file, read it
                // back, and only replace the real file once the presets are
                // confirmed to have survived the round trip.
                var expected = Data.Presets.Count(p => p != null
                    && !string.IsNullOrEmpty(p.Id));
                var temporaryPath = StoragePath + ".tmp";
                File.WriteAllText(temporaryPath, text);
                var roundTrip = Parse(File.ReadAllLines(temporaryPath));
                var actual = roundTrip.Presets.Count;
                var roundTripText = Serialize(roundTrip);
                if (actual != expected || roundTripText != text)
                {
                    Debug.LogError(Plugin.LogPrefix
                                   + "loadout presets did NOT survive the "
                                   + "round trip (" + expected + " in, "
                                   + actual + " out). Keeping the previous "
                                   + "file. Payload:\n" + text);
                    return;
                }

                File.Copy(temporaryPath, StoragePath, true);
                File.Delete(temporaryPath);
                Plugin.DebugLog("persisted " + expected
                                + " loadout preset(s) to " + StoragePath);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not save equipment presets: " + e);
            }
        }
    }

    internal sealed class LoadoutApplyResult
    {
        internal bool Success;
        internal string Message;
        internal readonly List<string> Missing = new List<string>();
        internal readonly List<string> Blocking = new List<string>();

        internal bool CanForce => !Success && Missing.Count > 0
                                  && Blocking.Count == 0;

        internal IEnumerable<string> AllIssues => Blocking.Concat(Missing);
    }

    /// <summary>
    /// Captures and applies complete character configurations.  It owns no UI
    /// and only talks to vanilla inventory/augmentation APIs, keeping future
    /// preset features independent from the central inventory panel.
    /// </summary>
    internal static class LoadoutPresetService
    {
        internal const string Primary = "primary";
        internal const string Secondary = "secondary";
        internal const string Additional = "additional";
        internal const string Servo = "servo";
        internal const string Backpack = "backpack";
        internal const string Vest = "vest";
        internal const string Armor = "armor";
        internal const string Helmet = "helmet";
        internal const string Leggings = "leggings";
        internal const string Boots = "boots";
        internal const string CarriedBackpack = "backpack";
        internal const string CarriedVest = "vest";

        private static readonly string[] EquipmentSlots =
        {
            Primary, Secondary, Additional, Servo, Backpack, Vest, Armor,
            Helmet, Leggings, Boots
        };

        private static readonly string[] UnequipOrder =
        {
            Servo, Additional, Secondary, Primary, Helmet, Armor, Leggings,
            Boots, Vest, Backpack
        };

        private static readonly string[] EquipOrder =
        {
            Backpack, Vest, Helmet, Armor, Leggings, Boots, Primary,
            Secondary, Additional, Servo
        };

        internal static LoadoutPreset Capture(Mercenary mercenary)
        {
            if (mercenary == null)
                return null;
            var preset = new LoadoutPreset
            {
                SourceProfileId = mercenary.ProfileId,
                SourceBodyTypeId = mercenary.CreatureData.BodyTypeId,
                IncludesCarriedItems = true
            };
            var inventory = mercenary.CreatureData.Inventory;
            foreach (var key in EquipmentSlots)
            {
                var slot = GetEquipmentSlot(inventory, key);
                preset.Equipment.Add(new LoadoutEquipmentEntry
                {
                    Slot = key,
                    ItemId = slot?.First?.Id ?? string.Empty
                });
            }

            foreach (var itemId in mercenary.CreatureData.AugmentationMap
                         .Values.Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal))
            {
                preset.Augmentations.Add(itemId);
            }

            foreach (var pair in mercenary.CreatureData.WoundSlotMap
                         .OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var record = Data.WoundSlots.GetRecord(pair.Key);
                preset.Sockets.Add(new LoadoutSocketEntry
                {
                    WoundSlotId = pair.Key,
                    SlotType = record?.SlotType ?? string.Empty,
                    TotalSockets = pair.Value?.TotalSockets ?? 0
                });
                if (pair.Value?.InstalledImplantsData == null)
                    continue;
                for (var i = 0;
                     i < pair.Value.InstalledImplantsData.Count; i++)
                {
                    var installed = pair.Value.InstalledImplantsData[i];
                    if (installed == null
                        || string.IsNullOrWhiteSpace(installed.ImplantId))
                    {
                        continue;
                    }
                    preset.Implants.Add(new LoadoutImplantEntry
                    {
                        WoundSlotId = pair.Key,
                        SlotType = record?.SlotType ?? string.Empty,
                        ItemId = installed.ImplantId,
                        Order = i
                    });
                }
            }
            CaptureCarriedItems(preset, inventory.BackpackStore,
                CarriedBackpack);
            CaptureCarriedItems(preset, inventory.VestStore, CarriedVest);
            return preset;
        }

        private static void CaptureCarriedItems(LoadoutPreset preset,
            ItemStorage storage, string storageKey)
        {
            if (preset == null || storage?.Items == null)
                return;
            foreach (var group in storage.Items.Where(item => item != null)
                         .GroupBy(item => item.Id, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var units = group.Sum(item => Math.Max(1,
                    (int)item.StackCount));
                if (units <= 0)
                    continue;
                preset.CarriedItems.Add(new LoadoutCarriedEntry
                {
                    Storage = storageKey,
                    ItemId = group.Key,
                    Units = units
                });
            }
        }

        internal static LoadoutApplyResult Apply(LoadoutPreset preset,
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression, SpaceTime spaceTime,
            PerkFactory perkFactory, bool forceMissing = false)
        {
            var result = Check(preset, mercenary, cargo, progression);
            if (!result.Success && !(forceMissing && result.CanForce))
                return result;

            try
            {
                if (!ApplyInternal(preset, mercenary, cargo, progression,
                        spaceTime, perkFactory, forceMissing, out var error))
                {
                    throw new InvalidOperationException(error);
                }
                result.Success = true;
                result.Message = Localization.Get(
                    forceMissing && result.Missing.Count > 0
                        ? "qmcentral.preset_applied_partial"
                        : "qmcentral.preset_applied");
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "preset application failed: " + e);
                return new LoadoutApplyResult
                {
                    Success = false,
                    Message = string.Format(Localization.Get(
                        "qmcentral.preset_apply_failed"), e.Message)
                };
            }
        }

        internal static string GetSummary(LoadoutPreset preset)
        {
            if (preset == null)
                return Localization.Get("qmcentral.preset_none_summary");
            var equipment = preset.Equipment?.Count(e =>
                e != null && !string.IsNullOrWhiteSpace(e.ItemId)) ?? 0;
            var augmentations = preset.Augmentations?.Count ?? 0;
            var implants = preset.Implants?.Count ?? 0;
            var carried = preset.IncludesCarriedItems
                ? preset.CarriedItems?.Sum(entry => entry?.Units ?? 0) ?? 0
                : -1;
            return string.Format(Localization.Get("qmcentral.preset_summary"),
                equipment, augmentations, implants,
                carried < 0
                    ? Localization.Get("qmcentral.preset_carried_legacy")
                    : carried.ToString(CultureInfo.InvariantCulture));
        }

        internal static LoadoutApplyResult Check(LoadoutPreset preset,
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression)
        {
            var result = new LoadoutApplyResult();
            if (preset == null || mercenary == null || cargo == null
                || cargo.ShipCargo.Count == 0)
            {
                result.Message = Localization.Get(
                    "qmcentral.preset_unavailable");
                return result;
            }

            var required = CountRequired(preset);
            var available = CountAvailable(mercenary, cargo, progression);
            foreach (var pair in required)
            {
                available.TryGetValue(pair.Key, out var count);
                if (count < pair.Value)
                {
                    result.Missing.Add(string.Format(
                        Localization.Get("qmcentral.preset_missing_item"),
                        GetItemName(pair.Key), pair.Value - count));
                }
            }

            var bodyAlreadyMatches = BodyMatchesPreset(preset, mercenary);
            var bodyPlan = BuildBodyPlan(preset, mercenary, cargo,
                progression, result.Blocking, result.Missing);
            if (!bodyAlreadyMatches
                && progression?.HasAugmentationsDepartment != true)
            {
                result.Blocking.Add(Localization.Get(
                    "qmcentral.preset_augmentation_station"));
            }

            if (!bodyAlreadyMatches)
            {
                var itemsToInstall = bodyPlan.Augmentations
                    .Where(choice => !choice.KeepCurrent)
                    .Select(choice => choice.ItemId);
                if (!ImplantsMatchPreset(preset, mercenary))
                {
                    itemsToInstall = itemsToInstall.Concat(
                        (preset.Implants
                         ?? new List<LoadoutImplantEntry>())
                        .Where(i => i != null).Select(i => i.ItemId));
                }
                foreach (var itemId in itemsToInstall
                             .Where(id => !string.IsNullOrWhiteSpace(id))
                             .Distinct(StringComparer.Ordinal))
                {
                    if (!CanInstallByProgress(itemId, progression))
                    {
                        result.Blocking.Add(string.Format(
                            Localization.Get(
                                "qmcentral.preset_locked_item"),
                            GetItemName(itemId)));
                    }
                }
            }

            var inventory = mercenary.CreatureData.Inventory;
            foreach (var entry in preset.Equipment
                         ?? new List<LoadoutEquipmentEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId))
                    continue;
                var slot = GetEquipmentSlot(inventory, entry.Slot);
                if (!IsPresetSlotAvailable(preset, entry.Slot, slot))
                {
                    result.Blocking.Add(string.Format(
                        Localization.Get("qmcentral.preset_slot_unavailable"),
                        entry.Slot));
                }
            }
            foreach (var slotKey in EquipmentSlots)
            {
                var current = GetEquipmentSlot(inventory, slotKey)?.First;
                var desiredId = preset.Equipment?.LastOrDefault(e =>
                    e != null && e.Slot == slotKey)?.ItemId
                                ?? string.Empty;
                if (current?.Locked == true && current.Id != desiredId)
                {
                    result.Blocking.Add(string.Format(Localization.Get(
                            "qmcentral.preset_locked_equipment"),
                        GetItemName(current.Id)));
                }
            }

            if (preset.IncludesCarriedItems
                && (!CarriedItemsMatchPreset(preset, inventory)
                    || CarrierEquipmentChanges(preset, inventory)))
            {
                foreach (var item in inventory.BackpackStore.Items.Concat(
                             inventory.VestStore.Items))
                {
                    if (item?.Locked != true)
                        continue;
                    result.Blocking.Add(string.Format(Localization.Get(
                            "qmcentral.preset_locked_carried"),
                        GetItemName(item.Id)));
                }
            }

            DeduplicateIssues(result.Missing);
            DeduplicateIssues(result.Blocking);
            result.Success = result.Missing.Count == 0
                             && result.Blocking.Count == 0;
            result.Message = result.Success
                ? string.Empty
                : Localization.Get("qmcentral.preset_missing_title");
            return result;
        }

        private static void DeduplicateIssues(List<string> issues)
        {
            if (issues == null || issues.Count < 2)
                return;
            var unique = issues.Where(issue => !string.IsNullOrWhiteSpace(issue))
                .Distinct(StringComparer.CurrentCulture).ToList();
            issues.Clear();
            issues.AddRange(unique);
        }

        private sealed class ProspectiveSocket
        {
            internal string WoundSlotId;
            internal string SlotType;
            internal string NatureType;
            internal int Capacity;
        }

        private sealed class AugmentationChoice
        {
            internal string ItemId;
            internal AugmentationRecord Record;
            internal bool KeepCurrent;
            internal BasePickupItem CargoItem;

            internal int CapacityFor(string woundSlotId,
                Mercenary mercenary)
            {
                if (KeepCurrent
                    && mercenary.CreatureData.WoundSlotMap.TryGetValue(
                        woundSlotId, out var current))
                {
                    return current.TotalSockets;
                }
                return CargoItem?.Comp<AugmentationComponent>()
                           ?.GetImplantSocketsByWoundSlotId(woundSlotId)
                       ?? Data.WoundSlots.GetRecord(woundSlotId)
                           ?.ImplantSocketsDefault
                       ?? 0;
            }
        }

        private sealed class BodyInstallPlan
        {
            internal readonly List<AugmentationChoice> Augmentations =
                new List<AugmentationChoice>();
        }

        private static BodyInstallPlan BuildBodyPlan(LoadoutPreset preset,
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression, List<string> blocking,
            List<string> missing)
        {
            var plan = new BodyInstallPlan();
            var sockets = new List<ProspectiveSocket>();
            var defaultWoundSlots = new List<string>();
            var profile = Data.MercenaryProfiles.GetRecord(
                mercenary.ProfileId);
            if (profile != null)
                defaultWoundSlots.AddRange(profile.WoundSlots);
            else
            {
                var body = Data.BodyTypes.GetRecord(
                    mercenary.CreatureData.BodyTypeId);
                if (body != null)
                    defaultWoundSlots.AddRange(body.WoundSlots);
            }
            foreach (var woundSlotId in defaultWoundSlots)
            {
                var record = Data.WoundSlots.GetRecord(woundSlotId);
                if (record == null)
                    continue;
                sockets.Add(new ProspectiveSocket
                {
                    WoundSlotId = woundSlotId,
                    SlotType = record.SlotType,
                    NatureType = record.NatureType,
                    Capacity = mercenary.CreatureData.WoundSlotMap
                        .TryGetValue(woundSlotId, out var current)
                        ? current.TotalSockets
                        : record.ImplantSocketsDefault
                });
            }

            foreach (var itemId in preset.Augmentations
                         ?? new List<string>())
            {
                var augmentation = Data.Items
                    .GetSimpleRecord<AugmentationRecord>(itemId);
                if (augmentation == null)
                {
                    blocking.Add(string.Format(Localization.Get(
                        "qmcentral.preset_invalid_item"), itemId));
                    continue;
                }

                var keepCurrent = augmentation.WoundSlotIds.All(
                    woundSlotId => mercenary.CreatureData.AugmentationMap
                        .TryGetValue(woundSlotId, out var installedId)
                        && installedId == itemId
                        && mercenary.CreatureData.WoundSlotMap.TryGetValue(
                            woundSlotId, out var socket)
                        && socket.TotalSockets >= RequiredSocketCapacity(
                            preset, woundSlotId,
                            Data.WoundSlots.GetRecord(woundSlotId)
                                ?.SlotType));
                BasePickupItem cargoItem = null;
                if (!keepCurrent)
                {
                    cargoItem = FindBestAvailableItem(cargo, progression,
                        mercenary.CreatureData.Inventory,
                        itemId, item => AugmentationCanHostPreset(
                            item, augmentation, preset));
                    if (cargoItem == null
                        && !WillReturnCompatibleAugmentation(
                            mercenary, augmentation, preset))
                    {
                        missing.Add(string.Format(Localization.Get(
                                "qmcentral.preset_augmentation_capacity"),
                            GetItemName(itemId)));
                    }
                }
                var choice = new AugmentationChoice
                {
                    ItemId = itemId,
                    Record = augmentation,
                    KeepCurrent = keepCurrent,
                    CargoItem = cargoItem
                };
                plan.Augmentations.Add(choice);
                foreach (var woundSlotId in augmentation.WoundSlotIds)
                {
                    var record = Data.WoundSlots.GetRecord(woundSlotId);
                    if (record == null)
                        continue;
                    sockets.RemoveAll(s => s.SlotType == record.SlotType);
                    sockets.Add(new ProspectiveSocket
                    {
                        WoundSlotId = woundSlotId,
                        SlotType = record.SlotType,
                        NatureType = record.NatureType,
                        Capacity = choice.CapacityFor(woundSlotId,
                            mercenary)
                    });
                }
            }

            var desiredBySocket = new Dictionary<ProspectiveSocket, int>();
            foreach (var implant in preset.Implants
                         ?? new List<LoadoutImplantEntry>())
            {
                if (implant == null)
                    continue;
                var record = Data.Items
                    .GetSimpleRecord<ImplantRecord>(implant.ItemId);
                var socket = sockets.FirstOrDefault(s =>
                                 !string.IsNullOrWhiteSpace(
                                     implant.WoundSlotId)
                                 && s.WoundSlotId == implant.WoundSlotId)
                             ?? sockets.FirstOrDefault(s =>
                                 s.SlotType == (record?.SlotType
                                                ?? implant.SlotType));
                if (record == null || socket == null
                    || !record.NatureTypes.Contains(socket.NatureType))
                {
                    blocking.Add(string.Format(Localization.Get(
                        "qmcentral.preset_body_incompatible"),
                        GetItemName(implant.ItemId)));
                    continue;
                }
                desiredBySocket.TryGetValue(socket, out var count);
                desiredBySocket[socket] = count + 1;
            }
            foreach (var socket in sockets)
            {
                var required = RequiredSocketCapacity(preset,
                    socket.WoundSlotId, socket.SlotType);
                if (required > socket.Capacity)
                {
                    blocking.Add(string.Format(Localization.Get(
                        "qmcentral.preset_socket_shortage"),
                        socket.SlotType, required, socket.Capacity));
                }
            }
            return plan;
        }

        private static bool WillReturnCompatibleAugmentation(
            Mercenary mercenary, AugmentationRecord record,
            LoadoutPreset preset)
        {
            if (!mercenary.CreatureData.AugmentationMap.Values
                    .Contains(record.Id))
            {
                return false;
            }
            return record.WoundSlotIds.All(woundSlotId =>
            {
                var woundSlot = Data.WoundSlots.GetRecord(woundSlotId);
                return woundSlot != null
                       && woundSlot.ImplantSocketsDefault
                       >= RequiredSocketCapacity(preset, woundSlotId,
                           woundSlot.SlotType);
            });
        }

        private static int DesiredImplantCount(LoadoutPreset preset,
            string woundSlotId, string slotType)
        {
            return (preset.Implants ?? new List<LoadoutImplantEntry>())
                .Count(i => i != null
                            && ((!string.IsNullOrWhiteSpace(i.WoundSlotId)
                                 && i.WoundSlotId == woundSlotId)
                                || (string.IsNullOrWhiteSpace(i.WoundSlotId)
                                    && !string.IsNullOrWhiteSpace(slotType)
                                    && i.SlotType == slotType)));
        }

        private static int RequiredSocketCapacity(LoadoutPreset preset,
            string woundSlotId, string slotType)
        {
            var saved = preset.Sockets?.FirstOrDefault(s => s != null
                            && s.WoundSlotId == woundSlotId)
                        ?? preset.Sockets?.FirstOrDefault(s => s != null
                            && !string.IsNullOrWhiteSpace(slotType)
                            && s.SlotType == slotType);
            return Math.Max(saved?.TotalSockets ?? 0,
                DesiredImplantCount(preset, woundSlotId, slotType));
        }

        private static bool AugmentationCanHostPreset(BasePickupItem item,
            AugmentationRecord record, LoadoutPreset preset)
        {
            var component = item?.Comp<AugmentationComponent>();
            if (component == null)
                return false;
            return record.WoundSlotIds.All(woundSlotId =>
            {
                var slotType = Data.WoundSlots.GetRecord(woundSlotId)
                    ?.SlotType;
                return component.GetImplantSocketsByWoundSlotId(woundSlotId)
                       >= RequiredSocketCapacity(preset, woundSlotId,
                           slotType);
            });
        }

        private static bool IsPresetSlotAvailable(LoadoutPreset preset,
            string slotKey, ItemStorage currentSlot)
        {
            if (currentSlot == null)
                return false;
            if (currentSlot.MaxCapacity > 0)
                return true;
            if (slotKey != Servo)
                return false;
            var backpackId = preset.Equipment?.FirstOrDefault(e =>
                e != null && e.Slot == Backpack)?.ItemId;
            return !string.IsNullOrWhiteSpace(backpackId)
                   && Data.Items.GetSimpleRecord<BackpackRecord>(backpackId)
                       ?.AddServoArm == true;
        }

        private static bool ApplyInternal(LoadoutPreset preset,
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression, SpaceTime spaceTime,
            PerkFactory perkFactory, bool forceMissing, out string error)
        {
            error = null;
            var activeCargo = cargo.ShipCargo[0];
            var inventory = mercenary.CreatureData.Inventory;
            var desired = (preset.Equipment
                           ?? new List<LoadoutEquipmentEntry>())
                .Where(e => e != null)
                .GroupBy(e => e.Slot, StringComparer.Ordinal)
                .ToDictionary(g => g.Key,
                    g => g.Last().ItemId ?? string.Empty,
                    StringComparer.Ordinal);

            // Validate again immediately before the first mutation.  Once the
            // original equipped instances are moved into cargo, every later
            // step consumes only resources already proven to exist.
            var finalCheck = Check(preset, mercenary, cargo, progression);
            if (!finalCheck.Success
                && !(forceMissing && finalCheck.CanForce))
            {
                error = string.Join("; ", finalCheck.AllIssues);
                return false;
            }
            var bodyBlocking = new List<string>();
            var bodyMissing = new List<string>();
            var bodyPlan = BuildBodyPlan(preset, mercenary, cargo,
                progression, bodyBlocking, bodyMissing);
            if (bodyBlocking.Count > 0)
            {
                error = string.Join("; ", bodyBlocking);
                return false;
            }

            // A body cannot safely be left without a limb. In forced mode a
            // shortage of a mechanical part or implant therefore leaves the
            // current body configuration intact while ordinary equipment is
            // still applied (missing equipment slots are intentionally empty).
            var applyBody = !forceMissing
                            || (bodyMissing.Count == 0
                                && HasAllRequiredBodyItems(preset, mercenary,
                                    cargo, progression));
            var carriedNeedsApply = preset.IncludesCarriedItems
                                    && (!CarriedItemsMatchPreset(preset,
                                            inventory)
                                        || CarrierEquipmentChanges(preset,
                                            inventory));
            if (carriedNeedsApply)
                MoveAllCarriedToCargo(inventory, activeCargo);

            foreach (var key in UnequipOrder)
            {
                var item = GetEquipmentSlot(inventory, key)?.First;
                desired.TryGetValue(key, out var desiredId);
                if (item != null && item.Id != desiredId)
                    activeCargo.AddItemAndReshuffleOptional(item);
            }

            var keepAugmentations = !applyBody
                                    || AugmentationsCanStay(preset, mercenary);
            var keepImplants = !applyBody
                               || ImplantsMatchPreset(preset, mercenary);
            if (!keepAugmentations)
            {
                foreach (var pair in mercenary.CreatureData.WoundSlotMap
                             .ToList())
                {
                    AugmentationSystem.RemoveAllImplants(
                        mercenary.CreatureData, pair.Key, activeCargo);
                }

                var desiredAugmentationIds = new HashSet<string>(
                    bodyPlan.Augmentations.Select(c => c.ItemId),
                    StringComparer.Ordinal);
                foreach (var installedId in mercenary.CreatureData
                             .AugmentationMap.Values
                             .Where(id => !string.IsNullOrWhiteSpace(id)
                                          && !desiredAugmentationIds
                                              .Contains(id))
                             .Distinct(StringComparer.Ordinal).ToList())
                {
                    RemoveInstalledAugmentation(mercenary, installedId,
                        activeCargo);
                }

                foreach (var choice in bodyPlan.Augmentations
                             .Where(c => !c.KeepCurrent))
                {
                    RemoveInstalledAugmentation(mercenary, choice.ItemId,
                        activeCargo);
                    var item = FindBestAvailableItem(cargo, progression,
                        inventory,
                        choice.ItemId, candidate =>
                            AugmentationCanHostPreset(candidate,
                                choice.Record, preset));
                    if (item == null)
                    {
                        error = "missing compatible augmentation "
                                + choice.ItemId;
                        return false;
                    }
                    AugmentationSystem.Augment(mercenary, item, activeCargo,
                        spaceTime, cargo);
                    if (!mercenary.CreatureData.AugmentationMap.Values
                            .Contains(choice.ItemId))
                    {
                        error = "could not install augmentation "
                                + choice.ItemId;
                        return false;
                    }
                }
                AugmentationSystem.RestoreDefaultWoundSlots(mercenary);
                AugmentationSystem.ConfigureImplicitEffects(
                    mercenary.CreatureData);
                CreatureSystem.SetBareHandSlot(mercenary.CreatureData);
                inventory.RefreshEffectsVestBonus(
                    mercenary.CreatureData.EffectsController);
            }
            else if (!keepImplants)
            {
                foreach (var pair in mercenary.CreatureData.WoundSlotMap
                             .ToList())
                {
                    AugmentationSystem.RemoveAllImplants(
                        mercenary.CreatureData, pair.Key, activeCargo);
                }
                AugmentationSystem.ConfigureImplicitEffects(
                    mercenary.CreatureData);
            }

            if (!keepAugmentations || !keepImplants)
            {
                foreach (var implant in BuildImplantInstallOrder(preset,
                             mercenary))
                {
                    var before = AugmentationSystem.GetInstalledImplantCount(
                        mercenary.CreatureData);
                    var item = FindBestAvailableItem(cargo, progression,
                        inventory, implant.ItemId);
                    if (item == null)
                    {
                        error = "missing implant " + implant.ItemId;
                        return false;
                    }
                    AugmentationSystem.Implant(mercenary, item, activeCargo,
                        perkFactory);
                    if (AugmentationSystem.GetInstalledImplantCount(
                            mercenary.CreatureData) != before + 1)
                    {
                        error = "could not install implant "
                                + implant.ItemId;
                        return false;
                    }
                }
            }

            foreach (var key in EquipOrder)
            {
                if (!desired.TryGetValue(key, out var itemId)
                    || string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }
                var slot = GetEquipmentSlot(inventory, key);
                if (slot?.First?.Id == itemId)
                    continue;
                var item = FindBestAvailableItem(cargo, progression,
                    inventory, itemId);
                if (item == null && forceMissing)
                    continue;
                if (slot == null || slot.MaxCapacity <= 0 || item == null
                    || !ItemInteractionSystem.Move(item, slot,
                        CellPosition.Zero, sendEvent: true))
                {
                    error = "could not equip " + itemId + " in " + key;
                    return false;
                }
            }
            if (carriedNeedsApply && !RestoreCarriedItems(preset, inventory,
                    cargo, progression, spaceTime, activeCargo,
                    forceMissing, out error))
            {
                return false;
            }
            return true;
        }

        private static bool CarrierEquipmentChanges(LoadoutPreset preset,
            Inventory inventory)
        {
            if (preset == null || inventory == null)
                return false;
            foreach (var slotKey in new[] { Backpack, Vest })
            {
                var desired = preset.Equipment?.LastOrDefault(entry =>
                    entry != null && entry.Slot == slotKey)?.ItemId
                              ?? string.Empty;
                var current = GetEquipmentSlot(inventory, slotKey)?.First?.Id
                              ?? string.Empty;
                if (current != desired)
                    return true;
            }
            return false;
        }

        private static bool HasAllRequiredBodyItems(LoadoutPreset preset,
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression)
        {
            var available = CountAvailable(mercenary, cargo, progression);
            var required = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    return;
                required.TryGetValue(id, out var count);
                required[id] = count + 1;
            }
            foreach (var id in preset.Augmentations ?? new List<string>())
                Add(id);
            foreach (var entry in preset.Implants
                         ?? new List<LoadoutImplantEntry>())
                Add(entry?.ItemId);
            return required.All(pair => available.TryGetValue(pair.Key,
                       out var units) && units >= pair.Value);
        }

        private static void MoveAllCarriedToCargo(Inventory inventory,
            ItemStorage activeCargo)
        {
            if (inventory == null || activeCargo == null)
                return;
            foreach (var item in inventory.BackpackStore.Items
                         .Concat(inventory.VestStore.Items).ToList())
            {
                if (item != null)
                    activeCargo.AddItemAndReshuffleOptional(item);
            }
        }

        private static bool RestoreCarriedItems(LoadoutPreset preset,
            Inventory inventory, MagnumCargo cargo,
            MagnumProgression progression, SpaceTime spaceTime,
            ItemStorage activeCargo, bool forceMissing, out string error)
        {
            error = null;
            if (preset?.IncludesCarriedItems != true || inventory == null)
                return true;

            foreach (var entry in (preset.CarriedItems
                                   ?? new List<LoadoutCarriedEntry>())
                         .Where(value => value != null && value.Units > 0
                                         && !string.IsNullOrWhiteSpace(
                                             value.ItemId))
                         .OrderBy(value => value.Storage == CarriedVest ? 0 : 1)
                         .ThenBy(value => value.ItemId,
                             StringComparer.Ordinal))
            {
                var target = entry.Storage == CarriedVest
                    ? inventory.VestStore
                    : inventory.BackpackStore;
                var remaining = entry.Units;
                while (remaining > 0)
                {
                    var source = FindBestCargoItem(cargo, progression,
                        entry.ItemId);
                    if (source == null)
                    {
                        if (forceMissing)
                            break;
                        error = "missing carried item " + entry.ItemId;
                        return false;
                    }

                    var units = Math.Min(remaining,
                        Math.Max(1, (int)source.StackCount));
                    var moving = units < source.StackCount
                        ? SplitCargoStack(source, (short)units)
                        : source;
                    if (moving == null)
                    {
                        error = "could not split carried item " + entry.ItemId;
                        return false;
                    }

                    // Cargo can contain many partially filled stacks. Merge
                    // those through the vanilla path before consuming another
                    // backpack cell, otherwise a preset that originally fit
                    // as one full ammo stack could fail merely because its
                    // replacement stock is fragmented across storage bays.
                    if (moving.IsStackable
                        && ItemInteractionSystem.TryMergeIntoStorage(target,
                            moving, spaceTime, out var emptyAfterMerge)
                        && emptyAfterMerge)
                    {
                        remaining -= units;
                        continue;
                    }

                    if (!ItemInteractionSystem.Move(moving, target,
                            CellPosition.Zero, sendEvent: true))
                    {
                        if (moving.Storage == null)
                            activeCargo.AddItemAndReshuffleOptional(moving);
                        if (forceMissing)
                            break;
                        error = "could not fit carried item " + entry.ItemId
                                + " in " + entry.Storage;
                        return false;
                    }
                    remaining -= units;
                }
            }
            inventory.BackpackStore.RecalculateWeight();
            inventory.VestStore.RecalculateWeight();
            return true;
        }

        private static BasePickupItem SplitCargoStack(BasePickupItem source,
            short units)
        {
            if (source == null || units <= 0 || units >= source.StackCount)
                return null;
            var split = SingletonMonoBehaviour<ItemFactory>.Instance
                .CreateForInventory(source.Id);
            if (split == null)
                return null;
            try
            {
                source.StackCount -= units;
                split.StackCount = units;
                split.ExaminedItem = false;
                split.CopyExpireTime(source);
                ItemInteractionSystem.TrySplitUsable(source, split);
                source.Storage?.RecalculateWeight();
                return split;
            }
            catch
            {
                source.StackCount += units;
                throw;
            }
        }

        private static void RemoveInstalledAugmentation(
            Mercenary mercenary, string itemId, ItemStorage activeCargo)
        {
            var woundSlotId = mercenary.CreatureData.AugmentationMap
                .FirstOrDefault(pair => pair.Value == itemId).Key;
            if (!string.IsNullOrWhiteSpace(woundSlotId))
            {
                AugmentationSystem.RemoveAugmentation(mercenary,
                    woundSlotId, activeCargo, isItemSpawn: true);
            }
        }

        private static bool BodyMatchesPreset(LoadoutPreset preset,
            Mercenary mercenary)
        {
            return AugmentationsCanStay(preset, mercenary)
                   && ImplantsMatchPreset(preset, mercenary);
        }

        private static bool AugmentationsCanStay(LoadoutPreset preset,
            Mercenary mercenary)
        {
            var currentAugmentations = mercenary.CreatureData
                .AugmentationMap.Values
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var desiredAugmentations = (preset.Augmentations
                                        ?? new List<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (!currentAugmentations.SequenceEqual(desiredAugmentations))
                return false;

            foreach (var itemId in desiredAugmentations)
            {
                var record = Data.Items
                    .GetSimpleRecord<AugmentationRecord>(itemId);
                if (record == null || record.WoundSlotIds.Any(woundSlotId =>
                        !mercenary.CreatureData.WoundSlotMap.TryGetValue(
                            woundSlotId, out var current)
                        || current.TotalSockets < RequiredSocketCapacity(
                            preset, woundSlotId,
                            Data.WoundSlots.GetRecord(woundSlotId)
                                ?.SlotType)))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ImplantsMatchPreset(LoadoutPreset preset,
            Mercenary mercenary)
        {
            foreach (var saved in preset.Sockets
                         ?? new List<LoadoutSocketEntry>())
            {
                if (saved == null)
                    continue;
                var current = FindCurrentSocket(mercenary, saved);
                if (current == null
                    || current.TotalSockets < RequiredSocketCapacity(preset,
                        saved.WoundSlotId, saved.SlotType))
                {
                    return false;
                }
                var desiredImplants = (preset.Implants
                                      ?? new List<LoadoutImplantEntry>())
                    .Where(i => i != null
                                && ((!string.IsNullOrWhiteSpace(
                                        i.WoundSlotId)
                                     && i.WoundSlotId == saved.WoundSlotId)
                                    || (string.IsNullOrWhiteSpace(
                                            i.WoundSlotId)
                                        && i.SlotType == saved.SlotType)))
                    .OrderBy(i => i.Order).Select(i => i.ItemId).ToArray();
                var currentImplants = current.InstalledImplantsData
                    .Select(i => i.ImplantId).ToArray();
                if (!currentImplants.SequenceEqual(desiredImplants))
                    return false;
            }
            return AugmentationSystem.GetInstalledImplantCount(
                       mercenary.CreatureData)
                   == (preset.Implants?.Count ?? 0);
        }

        private static ImplantSocketData FindCurrentSocket(
            Mercenary mercenary, LoadoutSocketEntry saved)
        {
            if (!string.IsNullOrWhiteSpace(saved.WoundSlotId)
                && mercenary.CreatureData.WoundSlotMap.TryGetValue(
                    saved.WoundSlotId, out var exact))
            {
                return exact;
            }
            if (string.IsNullOrWhiteSpace(saved.SlotType))
                return null;
            return mercenary.CreatureData.WoundSlotMap.FirstOrDefault(pair =>
                    Data.WoundSlots.GetRecord(pair.Key)?.SlotType
                    == saved.SlotType)
                .Value;
        }

        private static IEnumerable<LoadoutImplantEntry>
            BuildImplantInstallOrder(LoadoutPreset preset,
                Mercenary mercenary)
        {
            var entries = (preset.Implants
                           ?? new List<LoadoutImplantEntry>())
                .Where(i => i != null).ToList();
            var currentSlots = mercenary.CreatureData.WoundSlotMap;
            return entries
                .OrderBy(i => CurrentSlotOrder(currentSlots, i))
                .ThenBy(i => i.Order)
                .ThenBy(i => i.ItemId, StringComparer.Ordinal);
        }

        private static int CurrentSlotOrder(
            Dictionary<string, ImplantSocketData> currentSlots,
            LoadoutImplantEntry implant)
        {
            var index = 0;
            foreach (var pair in currentSlots)
            {
                var slotType = Data.WoundSlots.GetRecord(pair.Key)?.SlotType;
                if ((!string.IsNullOrWhiteSpace(implant.WoundSlotId)
                     && pair.Key == implant.WoundSlotId)
                    || (!string.IsNullOrWhiteSpace(implant.SlotType)
                        && slotType == implant.SlotType))
                {
                    return index;
                }
                index++;
            }
            return int.MaxValue;
        }

        private static Dictionary<string, int> CountRequired(
            LoadoutPreset preset)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string id, int units = 1)
            {
                if (string.IsNullOrWhiteSpace(id) || units <= 0)
                    return;
                result.TryGetValue(id, out var count);
                result[id] = count + units;
            }
            foreach (var entry in preset.Equipment
                         ?? new List<LoadoutEquipmentEntry>())
                Add(entry?.ItemId);
            foreach (var id in preset.Augmentations ?? new List<string>())
                Add(id);
            foreach (var entry in preset.Implants
                         ?? new List<LoadoutImplantEntry>())
                Add(entry?.ItemId);
            if (preset.IncludesCarriedItems)
            {
                foreach (var entry in preset.CarriedItems
                             ?? new List<LoadoutCarriedEntry>())
                    Add(entry?.ItemId, entry?.Units ?? 0);
            }
            return result;
        }

        private static Dictionary<string, int> CountAvailable(
            Mercenary mercenary, MagnumCargo cargo,
            MagnumProgression progression)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(string id, int count = 1)
            {
                if (string.IsNullOrWhiteSpace(id) || count <= 0)
                    return;
                result.TryGetValue(id, out var current);
                result[id] = current + count;
            }
            foreach (var item in EnumerateCargoItems(cargo, progression))
                if (!item.Locked)
                    Add(item.Id, Math.Max(1, (int)item.StackCount));
            foreach (var key in EquipmentSlots)
                Add(GetEquipmentSlot(mercenary.CreatureData.Inventory, key)
                    ?.First?.Id);
            foreach (var item in mercenary.CreatureData.Inventory
                         .BackpackStore.Items.Concat(mercenary.CreatureData
                             .Inventory.VestStore.Items))
            {
                Add(item?.Id, Math.Max(1, (int)(item?.StackCount ?? 0)));
            }
            foreach (var id in mercenary.CreatureData.AugmentationMap.Values
                         .Where(id => !string.IsNullOrWhiteSpace(id))
                         .Distinct(StringComparer.Ordinal))
                Add(id);
            foreach (var socket in mercenary.CreatureData.WoundSlotMap.Values)
            foreach (var implant in socket.InstalledImplantsData)
                Add(implant.ImplantId);
            return result;
        }

        private static bool CarriedItemsMatchPreset(LoadoutPreset preset,
            Inventory inventory)
        {
            if (preset?.IncludesCarriedItems != true || inventory == null)
                return true;
            return CarriedCounts(preset.CarriedItems)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(CarriedCounts(inventory)
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        private static Dictionary<string, int> CarriedCounts(
            IEnumerable<LoadoutCarriedEntry> entries)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in entries ?? Enumerable.Empty<LoadoutCarriedEntry>())
            {
                if (entry == null || entry.Units <= 0
                    || string.IsNullOrWhiteSpace(entry.Storage)
                    || string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    continue;
                }
                var key = entry.Storage + "\t" + entry.ItemId;
                result.TryGetValue(key, out var units);
                result[key] = units + entry.Units;
            }
            return result;
        }

        private static Dictionary<string, int> CarriedCounts(
            Inventory inventory)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            void Add(ItemStorage storage, string storageKey)
            {
                if (storage?.Items == null)
                    return;
                foreach (var item in storage.Items)
                {
                    if (item == null)
                        continue;
                    var key = storageKey + "\t" + item.Id;
                    result.TryGetValue(key, out var units);
                    result[key] = units + Math.Max(1, (int)item.StackCount);
                }
            }
            Add(inventory?.BackpackStore, CarriedBackpack);
            Add(inventory?.VestStore, CarriedVest);
            return result;
        }

        private static IEnumerable<BasePickupItem> EnumerateCargoItems(
            MagnumCargo cargo, MagnumProgression progression)
        {
            if (cargo == null)
                yield break;
            foreach (var storage in cargo.ShipCargo)
            foreach (var item in storage.Items)
                if (item != null)
                    yield return item;
            if (progression?.HasStoreFridge == true
                && cargo.FridgeStorage != null)
            {
                foreach (var item in cargo.FridgeStorage.Items)
                    if (item != null)
                        yield return item;
            }
            if (progression?.HasStoreConstructorDepartment == true
                && !cargo.RecyclingInProgress
                && cargo.RecyclingStorage != null)
            {
                foreach (var item in cargo.RecyclingStorage.Items)
                    if (item != null)
                        yield return item;
            }
        }

        private static BasePickupItem FindBestCargoItem(MagnumCargo cargo,
            MagnumProgression progression, string itemId,
            Func<BasePickupItem, bool> predicate = null)
        {
            BasePickupItem best = null;
            IEnumerable<ItemStorage> Storages()
            {
                foreach (var storage in cargo.ShipCargo)
                    if (storage != null)
                        yield return storage;
                if (progression?.HasStoreFridge == true
                    && cargo.FridgeStorage != null)
                    yield return cargo.FridgeStorage;
                if (progression?.HasStoreConstructorDepartment == true
                    && !cargo.RecyclingInProgress
                    && cargo.RecyclingStorage != null)
                {
                    yield return cargo.RecyclingStorage;
                }
            }
            foreach (var storage in Storages())
            foreach (var item in storage.Items)
            {
                if (item == null || item.Id != itemId || item.StackCount <= 0
                    || item.Locked
                    || (predicate != null && !predicate(item)))
                    continue;
                if (best == null || CompareCondition(item, best) > 0)
                    best = item;
            }
            return best;
        }

        private static BasePickupItem FindBestAvailableItem(
            MagnumCargo cargo, MagnumProgression progression,
            Inventory inventory, string itemId,
            Func<BasePickupItem, bool> predicate = null)
        {
            var best = FindBestCargoItem(cargo, progression, itemId,
                predicate);
            if (inventory == null)
                return best;
            foreach (var item in inventory.BackpackStore.Items.Concat(
                         inventory.VestStore.Items))
            {
                if (item == null || item.Id != itemId || item.Locked
                    || item.StackCount <= 0
                    || (predicate != null && !predicate(item)))
                {
                    continue;
                }
                if (best == null || CompareCondition(item, best) > 0)
                    best = item;
            }
            return best;
        }

        private static int CompareCondition(BasePickupItem left,
            BasePickupItem right)
        {
            var leftBreakable = left.Comp<BreakableItemComponent>();
            var rightBreakable = right.Comp<BreakableItemComponent>();
            var leftBroken = leftBreakable?.IsBroken == true;
            var rightBroken = rightBreakable?.IsBroken == true;
            if (leftBroken != rightBroken)
                return leftBroken ? -1 : 1;
            var leftCondition = leftBreakable?.CurrentPercent ?? 1f;
            var rightCondition = rightBreakable?.CurrentPercent ?? 1f;
            var condition = leftCondition.CompareTo(rightCondition);
            if (condition != 0)
                return condition;
            return left.StackCount.CompareTo(right.StackCount);
        }

        private static bool CanInstallByProgress(string itemId,
            MagnumProgression progression)
        {
            var augmentation = Data.Items
                .GetSimpleRecord<AugmentationRecord>(itemId);
            var implant = Data.Items.GetSimpleRecord<ImplantRecord>(itemId);
            var augmentationClass = augmentation?.AugmentationClass
                                    ?? implant?.AugmentationClass
                                    ?? AugmentationClass.None;
            return !Data.Global.QuasiAugmentationClasses.Contains(
                       augmentationClass)
                   || progression?.AugStationQuasiAugsAllowed == true;
        }

        private static ItemStorage GetEquipmentSlot(Inventory inventory,
            string key)
        {
            if (inventory == null)
                return null;
            return key switch
            {
                Primary => inventory.PrimarySlot,
                Secondary => inventory.SecondarySlot,
                Additional => inventory.AdditionalSlot,
                Servo => inventory.ServoArmSlot,
                Backpack => inventory.BackpackSlot,
                Vest => inventory.VestSlot,
                Armor => inventory.ArmorSlot,
                Helmet => inventory.HelmetSlot,
                Leggings => inventory.LeggingsSlot,
                Boots => inventory.BootsSlot,
                _ => null
            };
        }

        private static string GetItemName(string itemId)
        {
            var key = "item." + itemId + ".name";
            var name = Localization.Get(key);
            return string.IsNullOrWhiteSpace(name) || name == key
                ? itemId
                : name;
        }
    }
}

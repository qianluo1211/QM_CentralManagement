using System.Collections.Generic;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// What one repair pass actually did.
    /// </summary>
    internal sealed class RepairOutcome
    {
        /// <summary>Items whose condition actually went up.</summary>
        internal int ItemsRepaired;
        /// <summary>Individual consumable applications spent.</summary>
        internal int Applications;
        /// <summary>Consumable item id -> what was spent of it.</summary>
        internal readonly Dictionary<string, RepairSpend> Consumed =
            new Dictionary<string, RepairSpend>(System.StringComparer.Ordinal);
        /// <summary>
        /// Damaged items left untouched because nothing compatible was in
        /// storage, or because the only compatible consumable would have
        /// destroyed them.
        /// </summary>
        internal int Skipped;

        internal bool DidSomething => ItemsRepaired > 0;
    }

    /// <summary>
    /// How much of one consumable a repair pass used up.
    ///
    /// The distinction matters for reporting, not for the repair itself: a
    /// repair kit holds five charges, so spending seven of them is one and a
    /// bit kits, and a receipt that just says "x7" reads as seven whole kits.
    /// Scrap parts are single-use, where charges and items are the same thing.
    /// </summary>
    internal sealed class RepairSpend
    {
        internal int Amount;
        internal bool Charges;
    }

    /// <summary>
    /// Repairs the gear of the agent currently selected in the central panel,
    /// paying out of the ship's storage.
    ///
    /// Scope is deliberately the AGENT, not the hold: this action exists so a
    /// player can finish gearing someone up without dragging one consumable
    /// onto one item at a time -- and the repair kits carry five charges each,
    /// so a full kit of worn armor is dozens of drag operations. Repairing the
    /// entire hold instead would spend the player's whole supply on stock they
    /// may never take anywhere.
    ///
    /// "On the agent" means Inventory.AllContainers: every equipment slot plus
    /// the backpack and vest contents -- exactly what the panel's agent pane
    /// shows. The consumables come from ship storage ONLY; kits the agent is
    /// carrying are left alone, because those were packed to be taken on the
    /// raid and burning them at base is the opposite of helpful.
    ///
    /// Everything goes through the game's own ItemInteractionSystem.Repair, so
    /// compatibility rules, durability multipliers and consumption stay
    /// vanilla. Nothing here reimplements repair; it only decides WHAT to
    /// repair WITH, and when to refuse. Two refusals are deliberate:
    ///
    /// 1. **It never destroys anything.** Vanilla's Repair applies the
    ///    durability change FIRST and only then notices that the item's
    ///    max-durability penalty hit 100%, at which point the item is gone and
    ///    the caller replaces it with scrap. That is a defensible outcome when
    ///    a player chooses it one item at a time; it is not something a batch
    ///    action may do behind their back. So the last application that would
    ///    finish an item off is never made -- see <see cref="WouldDestroy"/>.
    ///
    /// 2. **It prefers consumables that do not cost max durability.** The
    ///    proper repair kits (armor/firearm/melee/tech/engineering) carry a
    ///    POSITIVE MaxCapacity, meaning they give some of the permanent
    ///    penalty back; the scrap parts (rags, springs, plates, ...) carry a
    ///    negative one and permanently shrink the item. Grabbing whatever
    ///    matched first would quietly grind good gear down, so kits are always
    ///    spent before scrap.
    /// </summary>
    internal static class CentralRepairService
    {
        /// <summary>
        /// Per item, and overall. Vanilla's Restore can legitimately return
        /// zero progress (a rounding-to-nothing restore amount on a very large
        /// max durability), and this loop must not spin on that. The progress
        /// check below is the real guard; these are the backstop.
        /// </summary>
        private const int MaxApplicationsPerItem = 64;
        private const int MaxApplicationsTotal = 512;

        /// <summary>
        /// The storages the consumables are paid out of.
        ///
        /// The recycling bay is deliberately NOT among them: everything in it
        /// is queued for destruction, and pulling kits back out of the shredder
        /// is not something a one-click action should decide to do. The fridge
        /// is included only because it is an ordinary storage the player can
        /// park things in.
        /// </summary>
        private static IEnumerable<ItemStorage> SupplyStorages(
            MagnumCargo cargo, MagnumProgression progression)
        {
            if (cargo == null)
                yield break;
            if (cargo.ShipCargo != null)
            {
                foreach (var storage in cargo.ShipCargo)
                    if (storage != null)
                        yield return storage;
            }
            if (progression?.HasStoreFridge == true
                && cargo.FridgeStorage != null)
            {
                yield return cargo.FridgeStorage;
            }
        }

        /// <summary>
        /// How many of this agent's damaged items a pass could actually
        /// improve. Used both to enable the button and to fill in the
        /// confirmation, so the number the player agrees to is the number that
        /// gets worked on.
        /// </summary>
        internal static int CountRepairable(Mercenary mercenary,
            MagnumCargo cargo, MagnumProgression progression)
        {
            var targets = CollectTargets(mercenary);
            if (targets.Count == 0)
                return 0;
            var sources = CollectSources(cargo, progression);
            if (sources.Count == 0)
                return 0;

            var count = 0;
            foreach (var target in targets)
            {
                if (PickSource(target, sources) != null)
                    count++;
            }
            return count;
        }

        internal static RepairOutcome RepairAll(Mercenary mercenary,
            MagnumCargo cargo, MagnumProgression progression,
            PerkFactory perkFactory)
        {
            var outcome = new RepairOutcome();
            var targets = CollectTargets(mercenary);
            if (targets.Count == 0)
                return outcome;
            var sources = CollectSources(cargo, progression);
            if (sources.Count == 0)
            {
                outcome.Skipped = targets.Count;
                return outcome;
            }

            var inventory = mercenary.CreatureData.Inventory;
            var totalApplications = 0;
            foreach (var target in targets)
            {
                var component = target.Comp<BreakableItemComponent>();
                if (component == null)
                    continue;

                var appliedToThis = 0;
                while (IsDamaged(component)
                       && appliedToThis < MaxApplicationsPerItem
                       && totalApplications < MaxApplicationsTotal)
                {
                    var source = PickSource(target, sources);
                    if (source == null)
                        break;

                    var before = component.CurrentPercent;
                    var usesBefore = RemainingUses(source);
                    // The inventory argument is load-bearing here in a way it
                    // was not when this repaired cargo: vanilla uses it to
                    // grow the servo-arm slot when the repaired item is the
                    // backpack this agent is actually wearing.
                    if (!ItemInteractionSystem.Repair(target, source,
                            inventory, out _))
                    {
                        // Should be unreachable: CanRepair covers every
                        // compatibility refusal and WouldDestroy covers the
                        // one case vanilla refuses only AFTER mutating the
                        // item. Give up on this target rather than on the
                        // consumable, which may still be right for the next
                        // one -- and never retry, or this spins.
                        Debug.LogWarning(Plugin.LogPrefix + "vanilla refused "
                                         + source.Id + " for " + target.Id
                                         + " after it passed CanRepair.");
                        break;
                    }

                    var spent = Mathf.Max(1,
                        usesBefore - RemainingUses(source));
                    Record(outcome.Consumed, source, spent);
                    outcome.Applications += spent;
                    totalApplications++;
                    appliedToThis++;
                    // Matched to vanilla's granularity: DragController raises
                    // this once per successful repair, so doing it by hand N
                    // times and pressing this button are worth the same.
                    if (perkFactory != null)
                    {
                        PerkSystem.RaisePerkAction(
                            PerkLevelUpActionType.RepairItem, mercenary,
                            perkFactory);
                    }

                    if (RemainingUses(source) <= 0)
                        sources.Remove(source);
                    // Vanilla's Restore can round to no progress at all. One
                    // wasted application is a bug worth logging, a loop that
                    // burns the whole supply is a disaster.
                    if (component.CurrentPercent <= before)
                    {
                        Debug.LogWarning(Plugin.LogPrefix + "repairing "
                                         + target.Id
                                         + " made no progress; stopping on it.");
                        break;
                    }
                }

                if (appliedToThis > 0)
                    outcome.ItemsRepaired++;
                else
                    outcome.Skipped++;
            }

            // Worn armor that just gained condition changes what the agent
            // actually resists. Vanilla recomputes after every single repair;
            // once at the end reaches the same state for a lot less work.
            if (outcome.DidSomething)
                CreatureSystem.RefreshResists(mercenary.CreatureData, mercenary);
            return outcome;
        }

        /// <summary>
        /// Everything the panel's agent pane shows: worn gear, the backpack
        /// and vest themselves, and their contents.
        /// </summary>
        private static List<BasePickupItem> CollectTargets(Mercenary mercenary)
        {
            var targets = new List<BasePickupItem>();
            var containers = mercenary?.CreatureData?.Inventory?.AllContainers;
            if (containers == null)
                return targets;
            foreach (var container in containers)
            {
                if (container == null)
                    continue;
                // Snapshot: repairing a worn backpack can resize a slot, and
                // a consumable running out mutates its own storage.
                foreach (var item in container.Items.ToArray())
                {
                    if (IsRepairTarget(item))
                        targets.Add(item);
                }
            }
            return targets;
        }

        private static List<BasePickupItem> CollectSources(MagnumCargo cargo,
            MagnumProgression progression)
        {
            var sources = new List<BasePickupItem>();
            foreach (var storage in SupplyStorages(cargo, progression))
            {
                foreach (var item in storage.Items.ToArray())
                {
                    if (item != null && !item.Locked
                        && item.Is<RepairRecord>()
                        && RemainingUses(item) > 0)
                    {
                        sources.Add(item);
                    }
                }
            }
            return sources;
        }

        private static bool IsRepairTarget(BasePickupItem item)
        {
            if (item == null || item.Locked || item.Storage == null)
                return false;
            // A consumable must never become its own target.
            if (item.Is<RepairRecord>())
                return false;
            var component = item.Comp<BreakableItemComponent>();
            return component != null && !component.Unbreakable
                   && IsDamaged(component);
        }

        /// <summary>
        /// Integer comparison on purpose. Durability and its penalty ceiling
        /// are both derived from the same float percentage, so an epsilon
        /// comparison on the percentages reports "damaged" for items already
        /// sitting at their capped maximum -- which is precisely the state
        /// that makes the repair loop spin.
        /// </summary>
        private static bool IsDamaged(BreakableItemComponent component)
        {
            return component.Durability < component.MaxDurabilityWithPenalty;
        }

        /// <summary>
        /// Faithful to ItemInteractionSystem.ConsumeItem's two branches: a
        /// multi-charge kit spends one charge, everything else spends one unit
        /// off the stack.
        /// </summary>
        private static int RemainingUses(BasePickupItem item)
        {
            if (item == null || item.Storage == null)
                return 0;
            var usable = item.Comp<UsableItemComponent>();
            if (usable != null && item.IsUsable)
                return Mathf.Max(0, usable.CurrentUsages);
            return Mathf.Max(0, item.StackCount);
        }

        /// <summary>
        /// The consumable to spend on this item, or null to leave it alone.
        ///
        /// Preference order: never destroy it; spend a kit that gives max
        /// durability back before scrap that takes it away; and within a
        /// group, the smallest restore that still covers the damage, falling
        /// back to the largest available. Smallest-that-covers wastes the
        /// least; largest-otherwise needs the fewest applications, and every
        /// application of scrap costs permanent durability.
        /// </summary>
        private static BasePickupItem PickSource(BasePickupItem target,
            List<BasePickupItem> sources)
        {
            var component = target.Comp<BreakableItemComponent>();
            if (component == null)
                return null;
            var missing = component.MaxDurabilityWithPenalty
                          - component.Durability;

            BasePickupItem best = null;
            var bestRank = int.MinValue;
            var bestRestore = 0;
            var bestCovers = false;

            foreach (var source in sources)
            {
                if (RemainingUses(source) <= 0)
                    continue;
                if (!ItemInteractionSystem.CanRepair(target, source))
                    continue;
                var record = source.Record<RepairRecord>();
                if (record == null || WouldDestroy(target, component, record))
                    continue;

                // Kits (MaxCapacity >= 0) outrank scrap outright.
                var rank = record.MaxCapacity >= 0 ? 1 : 0;
                var restore = record.RestoreAmount;
                var covers = restore >= missing;

                if (best == null || rank > bestRank)
                {
                    best = source;
                    bestRank = rank;
                    bestRestore = restore;
                    bestCovers = covers;
                    continue;
                }
                if (rank < bestRank)
                    continue;

                var better = bestCovers
                    // Both cover the damage: take the smaller one.
                    ? covers && restore < bestRestore
                    // Nothing covers it yet: take the biggest, or the first
                    // one that does cover it.
                    : covers || restore > bestRestore;
                if (!better)
                    continue;
                best = source;
                bestRestore = restore;
                bestCovers = covers;
            }
            return best;
        }

        /// <summary>
        /// Whether applying this consumable would push the item's permanent
        /// penalty to 100%, which is how vanilla turns a repair into scrap.
        ///
        /// Mirrors the clamp in BreakableItemComponent.Restore rather than
        /// calling it, because Restore mutates: by the time it could be asked,
        /// the item would already be ruined.
        /// </summary>
        private static bool WouldDestroy(BasePickupItem target,
            BreakableItemComponent component, RepairRecord record)
        {
            // A non-negative MaxCapacity gives penalty back or leaves it be,
            // so it can never be the application that finishes an item off.
            if (record.MaxCapacity >= 0)
                return false;
            if (component.MaxDurability <= 0)
                return true;
            // Restore clamps the penalty to 1 - MinDurabilityAfterRepair/Max,
            // so an item with any guaranteed floor can never reach 100%.
            var floor = component.MinDurabilityAfterRepair
                        / (float)component.MaxDurability;
            if (floor > 0f)
                return false;
            var delta = Mathf.Abs(ScaledMaxCapacity(target, record))
                        / (float)component.MaxDurability;
            return component.MaxPenaltyPercent + delta >= 1f;
        }

        /// <summary>
        /// The max-durability adjustment vanilla will ACTUALLY apply.
        ///
        /// ItemInteractionSystem.Repair scales both the restore amount and the
        /// capacity adjustment by the agent's weapon/armor durability
        /// multipliers before handing them to Restore, applying each in turn.
        /// Predicting destruction off the raw record value would therefore
        /// under-estimate the penalty for any agent whose multiplier is above
        /// 1 -- and under-estimating here means destroying an item the player
        /// never agreed to lose. Mirrored step for step, rounding included.
        /// </summary>
        private static int ScaledMaxCapacity(BasePickupItem target,
            RepairRecord record)
        {
            var adjustment = record.MaxCapacity;
            var factory = SingletonMonoBehaviour<ItemFactory>.Instance;
            if (factory == null)
                return adjustment;
            if (target.Is<WeaponRecord>())
            {
                adjustment = Mathf.RoundToInt(
                    adjustment * factory.WeaponDurabilityMult);
            }
            if (target.IsType<IArmorRecord>())
            {
                adjustment = Mathf.RoundToInt(
                    adjustment * factory.ArmorDurabilityMult);
            }
            return adjustment;
        }

        private static void Record(Dictionary<string, RepairSpend> counts,
            BasePickupItem source, int amount)
        {
            if (source == null || string.IsNullOrEmpty(source.Id)
                || amount <= 0)
            {
                return;
            }
            if (!counts.TryGetValue(source.Id, out var spend))
            {
                spend = new RepairSpend { Charges = IsChargeBased(source) };
                counts[source.Id] = spend;
            }
            spend.Amount += amount;
        }

        /// <summary>
        /// Whether spending this consumable spends a CHARGE rather than the
        /// whole item. Vanilla's own test, via UsableItemRecord.IsUsable
        /// (UsageCost &lt; MaxUsage): the five-charge repair kits answer true,
        /// the single-use scrap parts answer false. Deliberately not gated on
        /// Storage the way RemainingUses is -- this is asked right after a
        /// consumption that may have just emptied the stack.
        /// </summary>
        private static bool IsChargeBased(BasePickupItem item)
        {
            return item != null && item.IsUsable
                   && item.Comp<UsableItemComponent>() != null;
        }
    }
}

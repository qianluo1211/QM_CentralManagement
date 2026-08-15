using System;
using System.Collections.Generic;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private static readonly HashSet<string> WarnedMissingKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> EnglishText =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mgperk." + TechId + ".name"] = "Central Logistics Matrix",
                ["mgperk." + TechId + ".subName"] =
                    "Indexes and controls all stored materials",
                ["qmcentral.title"] = "CENTRAL MATERIAL MANAGEMENT",
                ["qmcentral.search"] = "Search stored item name or internal ID...",
                ["qmcentral.close"] = "CLOSE",
                ["qmcentral.operator_show"] = "AGENT GEAR",
                ["qmcentral.operator_hide"] = "HIDE GEAR",
                ["qmcentral.augment_show"] = "INSTALL AUGS",
                ["qmcentral.augment_hide"] = "AGENT GEAR",
                // Per-control guidance. This replaces the old persistent hint
                // strip: vanilla never draws one, and at 3.4pt it was
                // unreadable and hidden outright when the agent panel opened.
                ["qmcentral.tip.previous_operator"] = "Previous agent",
                ["qmcentral.tip.next_operator"] = "Next agent",
                ["qmcentral.tip.operator"] = "Click to pick an agent",
                ["qmcentral.tip.operator_panel"] = "Show or hide the agent's gear",
                ["qmcentral.tip.close"] = "Close central management",
                ["qmcentral.tip.search"] = "Search by item name or internal ID",
                ["qmcentral.tip.slot_filter"] = "Filter by body slot",
                ["qmcentral.tip.sort"] = "Change the sort order",
                ["qmcentral.tip.select_filtered"] = "Select everything the filter matches",
                ["qmcentral.tip.clear"] = "Clear the whole selection",
                ["qmcentral.tip.previous_page"] = "Previous page - the mouse wheel also turns pages",
                ["qmcentral.tip.next_page"] = "Next page - the mouse wheel also turns pages",
                ["qmcentral.tip.recycle"] = "Send the selection to recycling. Click twice to confirm.",
                ["qmcentral.tip.preset_select"] = "Click to pick a saved loadout",
                ["qmcentral.tip.preset_apply"] = "Equip the selected loadout on this agent",
                ["qmcentral.tip.preset_save"] = "Save this agent's current gear as a loadout",
                ["qmcentral.tip.preset_delete"] = "Delete the selected loadout permanently",
                ["qmcentral.preset_title"] = "LOADOUT PRESETS",
                ["qmcentral.preset_none"] = "No preset selected",
                ["qmcentral.preset_apply"] = "APPLY",
                ["qmcentral.preset_save"] = "SAVE",
                ["qmcentral.preset_delete"] = "DELETE",
                ["qmcentral.preset_summary"] =
                    "Gear {0}  |  Body parts {1}  |  Implants {2}  |  Carried {3}",
                ["qmcentral.preset_carried_legacy"] = "not recorded",
                ["qmcentral.preset_none_summary"] =
                    "Save the current agent's complete configuration",
                ["qmcentral.preset_default_name"] = "Loadout {0}",
                ["qmcentral.preset_default_name_fallback"] = "New loadout",
                ["qmcentral.preset_save_title"] = "SAVE LOADOUT",
                ["qmcentral.preset_save_body"] =
                    "Saves gear, weapons, mechanical body parts, implants, backpack contents and vest/quick-slot contents. A preset with the same name is overwritten.",
                ["qmcentral.preset_save_confirm"] = "SAVE / OVERWRITE",
                ["qmcentral.preset_name_placeholder"] = "Preset name",
                ["qmcentral.preset_apply_title"] = "APPLY LOADOUT",
                ["qmcentral.preset_apply_body"] =
                    "Apply '{0}'?\n{1}\nAll required items have been found in central storage.",
                ["qmcentral.preset_apply_confirm"] = "APPLY",
                ["qmcentral.preset_delete_title"] = "DELETE LOADOUT",
                ["qmcentral.preset_delete_body"] =
                    "Permanently delete '{0}'?",
                ["qmcentral.preset_delete_confirm"] = "DELETE",
                ["qmcentral.preset_cancel"] = "CANCEL",
                ["qmcentral.preset_close"] = "CLOSE",
                ["qmcentral.preset_missing_title"] =
                    "LOADOUT CANNOT BE APPLIED",
                ["qmcentral.preset_missing_item"] = "Missing {0} x{1}",
                ["qmcentral.preset_force_title"] = "ITEMS ARE MISSING",
                ["qmcentral.preset_force_explanation"] =
                    "Continue anyway? Missing equipment slots will be left empty; missing carried items are skipped. An incomplete body preset leaves the current body intact.",
                ["qmcentral.preset_force_confirm"] = "APPLY AVAILABLE",
                ["qmcentral.preset_locked_item"] =
                    "Required augmentation technology is locked: {0}",
                ["qmcentral.preset_augmentation_station"] =
                    "Augmentation department is required",
                ["qmcentral.preset_slot_unavailable"] =
                    "This agent does not have equipment slot: {0}",
                ["qmcentral.preset_locked_equipment"] =
                    "Unequip locked item first: {0}",
                ["qmcentral.preset_locked_carried"] =
                    "Unlock carried item before replacing backpack contents: {0}",
                ["qmcentral.preset_invalid_item"] =
                    "Preset references an unknown item: {0}",
                ["qmcentral.preset_body_incompatible"] =
                    "This agent has no compatible body slot for {0}",
                ["qmcentral.preset_socket_shortage"] =
                    "{0} needs {1} implant sockets, but only {2} are available",
                ["qmcentral.preset_augmentation_capacity"] =
                    "No stored {0} has enough implant sockets",
                ["qmcentral.preset_unavailable"] = "Loadout is unavailable",
                ["qmcentral.preset_applied"] = "Loadout applied",
                ["qmcentral.preset_applied_partial"] =
                    "Available parts of the loadout were applied",
                ["qmcentral.preset_apply_failed"] =
                    "Could not apply the loadout. All items already moved remain safely in central storage.\n{0}",
                ["qmcentral.preset_error_title"] = "LOADOUT ERROR",
                ["qmcentral.preset_ship_save_body"] =
                    "Saves this agent's gear, weapons, backpack contents and vest/quick-slot contents. A preset with the same name is overwritten.",
                ["qmcentral.preset_ship_apply_body"] =
                    "Apply '{0}'?\n{1}\nAll required items have been found in the ship's storage.",
                ["qmcentral.preset_ship_force_explanation"] =
                    "Continue anyway? Missing equipment slots will be left empty and missing carried items are skipped. The body is never changed.",
                ["qmcentral.preset_ship_apply_failed"] =
                    "Could not apply the loadout. All items already moved remain safely in the ship's cargo.\n{0}",
                ["qmcentral.sort_button"] = "SORT: {0}",
                ["qmcentral.slot_button"] = "SLOT: {0}",
                ["qmcentral.slot_all"] = "ALL",
                ["qmcentral.resist_value"] = "x{0} / RES {1}",
                ["qmcentral.damage_value"] = "x{0} / DMG {1}",
                ["qmcentral.sort.name"] = "NAME",
                ["qmcentral.sort.quantity"] = "QUANTITY",
                ["qmcentral.sort.set"] = "ARMOR SET",
                ["qmcentral.sort.totalresist"] = "TOTAL RESIST",
                ["qmcentral.sort.blunt"] = "BLUNT RESIST",
                ["qmcentral.sort.pierce"] = "PIERCE RESIST",
                ["qmcentral.sort.laceration"] = "LACERATION RESIST",
                ["qmcentral.sort.fire"] = "FIRE RESIST",
                ["qmcentral.sort.cold"] = "COLD RESIST",
                ["qmcentral.sort.poison"] = "POISON RESIST",
                ["qmcentral.sort.shock"] = "SHOCK RESIST",
                ["qmcentral.sort.beam"] = "BEAM RESIST",
                ["qmcentral.sort.damage.totaldamage"] = "DAMAGE",
                ["qmcentral.sort.damage.blunt"] = "BLUNT DAMAGE",
                ["qmcentral.sort.damage.pierce"] = "PIERCE DAMAGE",
                ["qmcentral.sort.damage.laceration"] = "LACERATION DAMAGE",
                ["qmcentral.sort.damage.fire"] = "FIRE DAMAGE",
                ["qmcentral.sort.damage.cold"] = "COLD DAMAGE",
                ["qmcentral.sort.damage.poison"] = "POISON DAMAGE",
                ["qmcentral.sort.damage.shock"] = "SHOCK DAMAGE",
                ["qmcentral.sort.damage.beam"] = "BEAM DAMAGE",
                ["qmcentral.sort.damage.explosion"] = "EXPLOSIVE DAMAGE",
                ["qmcentral.sort.damage.plasma"] = "PLASMA DAMAGE",
                ["qmcentral.sort.damage.chaos"] = "CHAOS DAMAGE",
                ["qmcentral.sort.damage.proton"] = "PROTON DAMAGE",
                ["qmcentral.sort.damage.cryo"] = "CRYO DAMAGE",
                ["qmcentral.count"] = "{0} TYPES / {1} UNITS",
                ["qmcentral.page"] = "{0}/{1}",
                ["qmcentral.empty"] = "No stored items match this filter",
                ["qmcentral.clear_filters"] = "CLEAR FILTERS",
                ["qmcentral.select"] = "FULL",
                ["qmcentral.ready_stack"] = "x{0} / TAKE {1}",
                // Entries for the vanilla context menu that replaced the
                // hand-built quantity popup.
                ["qmcentral.menu_select_all"] = "Select all {0}",
                ["qmcentral.menu_select_amount"] = "Select the amount above",
                ["qmcentral.menu_deselect"] = "Clear this selection",
                ["qmcentral.select_filtered"] = "SELECT FILTER",
                ["qmcentral.deselect_filtered"] = "CLEAR FILTER",
                ["qmcentral.clear"] = "CLEAR",
                ["qmcentral.recycle"] = "RECYCLE {0}",
                ["qmcentral.recycle_dialog"] =
                    "Send {0} unit(s) to recycling? This cannot be undone.",
                ["qmcentral.recycle_apply"] = "RECYCLE",
                ["qmcentral.recycle_busy"] = "BUSY",
                ["qmcentral.recycle_none"] = "NONE",
                ["qmcentral.category.all"] = "ALL",
                ["qmcentral.category.weapons"] = "WEAPONS",
                ["qmcentral.category.equipment"] = "EQUIPMENT",
                ["qmcentral.category.ammo"] = "AMMO",
                ["qmcentral.category.supplies"] = "SUPPLIES",
                ["qmcentral.category.augments"] = "AUGMENTS",
                ["qmcentral.category.materials"] = "MATERIALS",
                ["qmcentral.category.barter"] = "BARTER",
                ["qmcentral.category.special"] = "SPECIAL",
                ["qmcentral.detail.any"] = "ALL",
                ["qmcentral.detail.ranged"] = "RANGED",
                ["qmcentral.detail.melee"] = "MELEE",
                ["qmcentral.detail.pistol"] = "PISTOL",
                ["qmcentral.detail.shotgun"] = "SHOTGUN",
                ["qmcentral.detail.smg"] = "SMG",
                ["qmcentral.detail.rifle"] = "RIFLE",
                ["qmcentral.detail.heavy"] = "HEAVY",
                ["qmcentral.detail.head"] = "HELMETS",
                ["qmcentral.detail.body"] = "BODY",
                ["qmcentral.detail.legs"] = "LEGS",
                ["qmcentral.detail.boots"] = "BOOTS",
                ["qmcentral.detail.backpack"] = "PACKS",
                ["qmcentral.detail.vest"] = "VESTS",
                ["qmcentral.detail.ammunition"] = "AMMO",
                ["qmcentral.detail.grenades"] = "GRENADES",
                ["qmcentral.detail.mines"] = "MINES",
                ["qmcentral.detail.turrets"] = "TURRETS",
                ["qmcentral.detail.food"] = "FOOD",
                ["qmcentral.detail.drinks"] = "DRINKS",
                ["qmcentral.detail.medicine"] = "MEDICINE",
                ["qmcentral.detail.repair"] = "REPAIR",
                ["qmcentral.detail.auglimb"] = "LIMBS",
                ["qmcentral.detail.augimplant"] = "IMPLANTS",
                ["qmcentral.detail.bio"] = "FLESH",
                ["qmcentral.detail.cybernetic"] = "CYBERNETIC",
                ["qmcentral.detail.quasi"] = "QUASI",
                ["qmcentral.detail.parts"] = "PARTS",
                ["qmcentral.detail.data"] = "DATA",
                ["qmcentral.detail.blueprints"] = "BLUEPRINTS",
                ["qmcentral.detail.organs"] = "ORGANS",
                ["qmcentral.detail.military"] = "MILITARY",
                ["qmcentral.detail.science"] = "SCIENCE",
                ["qmcentral.detail.industrial"] = "INDUSTRIAL",
                ["qmcentral.detail.valuable"] = "VALUABLE",
                ["qmcentral.detail.quest"] = "QUEST",
                ["qmcentral.detail.keys"] = "KEYS",
                ["qmcentral.detail.deployables"] = "DEVICES",
                ["qmcentral.detail.cyborgs"] = "CYBORGS",
                ["qmcentral.detail.other"] = "OTHER",
                // Station trade panel (qmtrade).
                ["qmtrade.title"] = "STATION TRADE",
                ["qmtrade.close"] = "CLOSE",
                ["qmtrade.tab_buy"] = "BUY",
                ["qmtrade.tab_sell"] = "SELL",
                ["qmtrade.search"] = "Search item name or internal ID...",
                ["qmtrade.trade"] = "TRADE",
                ["qmtrade.clear"] = "CLEAR",
                ["qmtrade.cancel"] = "CANCEL",
                ["qmtrade.select_all"] = "SELECT ALL",
                ["qmtrade.clear_filters"] = "CLEAR FILTERS",
                ["qmtrade.points"] = "TRADE POINTS: {0}",
                ["qmtrade.discount"] = "DISCOUNT {0}",
                ["qmtrade.extra_charge"] = "MARKUP {0}",
                ["qmtrade.trade_summary"] =
                    "SELL +{0} / BUY -{1} / BALANCE {2}",
                ["qmtrade.dialog_trade"] = "Complete all trades?\n{0}",
                ["qmtrade.dialog_trade_details"] = "Sell +{0} / Buy -{1}",
                ["qmtrade.header.item"] = "ITEM",
                ["qmtrade.header.stock_buy"] = "THEIR STOCK",
                ["qmtrade.header.stock_sell"] = "OUR STOCK",
                ["qmtrade.header.price"] = "PRICE",
                ["qmtrade.header.amount"] = "AMOUNT",
                ["qmtrade.header.total"] = "TOTAL",
                ["qmtrade.quest"] = "QUEST",
                ["qmtrade.empty_buy"] = "This station has nothing for sale",
                ["qmtrade.empty_sell"] =
                    "You have nothing this station will accept",
                ["qmtrade.empty_filtered"] = "No items match this filter",
                ["qmtrade.low_reputation_buy"] =
                    "Reputation too low to buy from this faction",
                ["qmtrade.low_reputation_sell"] =
                    "Reputation too low to sell to this faction",
                ["qmtrade.cat.all"] = "ALL",
                ["qmtrade.cat.weapons"] = "WEAPONS",
                ["qmtrade.cat.armor"] = "ARMOR",
                ["qmtrade.cat.ammo"] = "AMMO",
                ["qmtrade.cat.medical"] = "MEDICAL",
                ["qmtrade.cat.food"] = "FOOD",
                ["qmtrade.cat.materials"] = "MATERIALS",
                ["qmtrade.cat.other"] = "OTHER",
                ["qmtrade.sort.name"] = "NAME",
                ["qmtrade.sort.priceascending"] = "CHEAPEST FIRST",
                ["qmtrade.sort.pricedescending"] = "PRICIEST FIRST",
                ["qmtrade.sort.quantitydescending"] = "MOST IN STOCK",
                ["qmtrade.tip.close"] = "Close the trade screen",
                ["qmtrade.tip.buy_tab"] = "Buy from the station",
                ["qmtrade.tip.sell_tab"] = "Sell to the station",
                ["qmtrade.tip.search"] = "Search by item name or internal ID",
                ["qmtrade.tip.sort"] = "Change the sort order",
                ["qmtrade.tip.minus"] =
                    "Decrease by 1. Shift+click: -10. Ctrl+click: -100. Ctrl+Shift+click: -1000",
                ["qmtrade.tip.plus"] =
                    "Increase by 1. Shift+click: +10. Ctrl+click: +100. Ctrl+Shift+click: +1000",
                ["qmtrade.tip.max"] = "Set to the maximum available",
                ["qmtrade.tip.row"] =
                    "Left click: type an amount\nRight click: MAX\nHold left and slide: rows swept change once - starting on - subtracts, on + adds, on MAX sets MAX\nShift/Ctrl/Ctrl+Shift while sweeping: x10/x100/x1000\nHold still on one row: slow repeat\nThe mouse wheel always turns pages",
                ["qmtrade.tip.trade"] =
                    "Complete all pending sells and buys at once",
                ["qmtrade.tip.clear"] = "Clear the cart",
                ["qmtrade.tip.select_all"] =
                    "Add every visible sellable item at full amount",
                ["qmtrade.tip.clear_filters"] = "Remove search text and category",
                ["qmtrade.tip.previous_page"] =
                    "Previous page - the mouse wheel also turns pages",
                ["qmtrade.tip.next_page"] =
                    "Next page - the mouse wheel also turns pages",
                // Mod Configuration Menu entries (qmcentral.mcm.*). MCM feeds
                // header/label/tooltip strings into the game's LocalizableLabel,
                // which treats them as localization keys and resolves them
                // through Localization.Get, so these follow the game language
                // and refresh on language switch automatically.
                ["qmcentral.mcm.header"] = "Central Management",
                ["qmcentral.mcm.stationTrade"] = "Station trade panel",
                ["qmcentral.mcm.stationTrade.tip"] =
                    "Replace the vanilla station trade UI with this mod's panel.",
                ["qmcentral.mcm.tradeConfirm"] = "Confirm trades",
                ["qmcentral.mcm.tradeConfirm.tip"] =
                    "Ask for confirmation before completing the whole deal (all pending sells and buys).",
                ["qmcentral.mcm.autoUnlockTech"] = "Auto-unlock technology",
                ["qmcentral.mcm.autoUnlockTech.tip"] =
                    "Unlock the Central Logistics Matrix technology on every save, including brand new games, without researching it.",
                ["qmcentral.mcm.debugTradeLayout"] = "Debug trade layout",
                ["qmcentral.mcm.debugTradeLayout.tip"] =
                    "Dump the vanilla trade screen hierarchy into Player.log (diagnostics).",
                // Barter exchange summary (AnCom data chip delivery etc.).
                ["qmtrade.exchange_title"] =
                    "EXCHANGE - THE STATION HANDED OVER",
                ["qmtrade.exchange_item"] = "{0} x{1}"
            };

        private static readonly Dictionary<string, string> ChineseText =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mgperk." + TechId + ".name"] = "中央物资管理",
                ["mgperk." + TechId + ".subName"] = "统一索引并调度全部库存物资",
                ["qmcentral.title"] = "中央物资管理",
                ["qmcentral.search"] = "搜索库存物品名称或内部 ID……",
                ["qmcentral.close"] = "关闭",
                ["qmcentral.operator_show"] = "特工装备",
                ["qmcentral.operator_hide"] = "收起装备",
                ["qmcentral.augment_show"] = "安装义体",
                ["qmcentral.augment_hide"] = "特工装备",
                // 逐控件引导，取代原来那条常驻提示行。
                ["qmcentral.tip.previous_operator"] = "上一位特工",
                ["qmcentral.tip.next_operator"] = "下一位特工",
                ["qmcentral.tip.operator"] = "点击选择特工",
                ["qmcentral.tip.operator_panel"] = "显示或隐藏特工装备面板",
                ["qmcentral.tip.close"] = "关闭中央物资管理",
                ["qmcentral.tip.search"] = "按物品名称或内部 ID 搜索",
                ["qmcentral.tip.slot_filter"] = "按身体部位筛选",
                ["qmcentral.tip.sort"] = "更改排序方式",
                ["qmcentral.tip.select_filtered"] = "选中当前筛选出的全部物品",
                ["qmcentral.tip.clear"] = "清空选择",
                ["qmcentral.tip.previous_page"] = "上一页（滚轮同样可以翻页）",
                ["qmcentral.tip.next_page"] = "下一页（滚轮同样可以翻页）",
                ["qmcentral.tip.recycle"] = "把选中物品送去回收。再点一次确认。",
                ["qmcentral.tip.preset_select"] = "点击选择已保存的装备预设",
                ["qmcentral.tip.preset_apply"] = "把选中预设装备到当前特工身上",
                ["qmcentral.tip.preset_save"] = "把当前特工的装备保存为预设",
                ["qmcentral.tip.preset_delete"] = "永久删除选中的预设",
                ["qmcentral.preset_title"] = "装备预设",
                ["qmcentral.preset_none"] = "未选择预设",
                ["qmcentral.preset_apply"] = "应用",
                ["qmcentral.preset_save"] = "保存当前",
                ["qmcentral.preset_delete"] = "删除",
                ["qmcentral.preset_summary"] =
                    "装备 {0}｜机械部件 {1}｜义体 {2}｜随身物品 {3}",
                ["qmcentral.preset_carried_legacy"] = "旧预设未记录",
                ["qmcentral.preset_none_summary"] =
                    "保存当前特工身上的完整配置",
                ["qmcentral.preset_default_name"] = "装备预设 {0}",
                ["qmcentral.preset_default_name_fallback"] = "新装备预设",
                ["qmcentral.preset_save_title"] = "保存装备预设",
                ["qmcentral.preset_save_body"] =
                    "保存当前武器、防具、机械替换部件、义体，以及背包与弹挂/快捷栏中的全部物品。使用同名预设会覆盖原记录。",
                ["qmcentral.preset_save_confirm"] = "保存 / 覆盖",
                ["qmcentral.preset_name_placeholder"] = "输入预设名称",
                ["qmcentral.preset_apply_title"] = "应用装备预设",
                ["qmcentral.preset_apply_body"] =
                    "确定应用“{0}”吗？\n{1}\n中央仓库已经找到全部所需物品。",
                ["qmcentral.preset_apply_confirm"] = "一键换装",
                ["qmcentral.preset_delete_title"] = "删除装备预设",
                ["qmcentral.preset_delete_body"] =
                    "确定永久删除“{0}”吗？",
                ["qmcentral.preset_delete_confirm"] = "删除",
                ["qmcentral.preset_cancel"] = "取消",
                ["qmcentral.preset_close"] = "关闭",
                ["qmcentral.preset_missing_title"] = "无法应用此预设",
                ["qmcentral.preset_missing_item"] = "缺少 {0} ×{1}",
                ["qmcentral.preset_force_title"] = "预设物品不足",
                ["qmcentral.preset_force_explanation"] =
                    "仍要继续吗？缺少的普通装备栏位会留空，缺少的随身物品会跳过；若身体部件或义体不完整，则保留当前身体配置。",
                ["qmcentral.preset_force_confirm"] = "强制应用现有物品",
                ["qmcentral.preset_locked_item"] =
                    "尚未解锁所需增强科技：{0}",
                ["qmcentral.preset_augmentation_station"] =
                    "需要先建造增强体部门",
                ["qmcentral.preset_slot_unavailable"] =
                    "当前特工没有装备栏位：{0}",
                ["qmcentral.preset_locked_equipment"] =
                    "请先解锁身上的 {0}",
                ["qmcentral.preset_locked_carried"] =
                    "替换背包内容前请先解锁随身物品：{0}",
                ["qmcentral.preset_invalid_item"] =
                    "预设引用了未知物品：{0}",
                ["qmcentral.preset_body_incompatible"] =
                    "当前特工没有可安装 {0} 的兼容身体部位",
                ["qmcentral.preset_socket_shortage"] =
                    "{0} 部位需要 {1} 个义体插槽，但当前只有 {2} 个",
                ["qmcentral.preset_augmentation_capacity"] =
                    "仓库中的 {0} 都没有足够的义体插槽",
                ["qmcentral.preset_unavailable"] = "当前预设不可用",
                ["qmcentral.preset_applied"] = "预设已应用",
                ["qmcentral.preset_applied_partial"] =
                    "已应用预设中现有的物品",
                ["qmcentral.preset_apply_failed"] =
                    "应用失败；已经移动的物品仍安全保存在中央仓库中。\n{0}",
                ["qmcentral.preset_error_title"] = "预设应用失败",
                ["qmcentral.preset_ship_save_body"] =
                    "保存当前特工的装备、武器、背包物品和战术背心/快捷栏物品。同名预设会被覆盖。",
                ["qmcentral.preset_ship_apply_body"] =
                    "确定应用「{0}」吗？\n{1}\n飞船仓库中已找到全部所需物品。",
                ["qmcentral.preset_ship_force_explanation"] =
                    "仍要继续吗？缺少的装备栏位会留空，缺少的随身物品会跳过。身体部位不会被改动。",
                ["qmcentral.preset_ship_apply_failed"] =
                    "应用失败；已经移动的物品都安全留在飞船货舱中。\n{0}",
                ["qmcentral.sort_button"] = "排序：{0}",
                ["qmcentral.slot_button"] = "部位：{0}",
                ["qmcentral.slot_all"] = "全部",
                ["qmcentral.resist_value"] = "×{0} / 抗 {1}",
                ["qmcentral.damage_value"] = "×{0} / 伤 {1}",
                ["qmcentral.sort.name"] = "名称",
                ["qmcentral.sort.quantity"] = "数量",
                ["qmcentral.sort.set"] = "套装",
                ["qmcentral.sort.totalresist"] = "总抗性",
                ["qmcentral.sort.blunt"] = "钝击抗性",
                ["qmcentral.sort.pierce"] = "穿刺抗性",
                ["qmcentral.sort.laceration"] = "切割抗性",
                ["qmcentral.sort.fire"] = "火焰抗性",
                ["qmcentral.sort.cold"] = "寒冷抗性",
                ["qmcentral.sort.poison"] = "毒素抗性",
                ["qmcentral.sort.shock"] = "电击抗性",
                ["qmcentral.sort.beam"] = "光束抗性",
                ["qmcentral.sort.damage.totaldamage"] = "伤害强度",
                ["qmcentral.sort.damage.blunt"] = "钝击伤害",
                ["qmcentral.sort.damage.pierce"] = "穿刺伤害",
                ["qmcentral.sort.damage.laceration"] = "切割伤害",
                ["qmcentral.sort.damage.fire"] = "火焰伤害",
                ["qmcentral.sort.damage.cold"] = "寒冷伤害",
                ["qmcentral.sort.damage.poison"] = "毒素伤害",
                ["qmcentral.sort.damage.shock"] = "电击伤害",
                ["qmcentral.sort.damage.beam"] = "光束伤害",
                ["qmcentral.sort.damage.explosion"] = "爆炸伤害",
                ["qmcentral.sort.damage.plasma"] = "等离子伤害",
                ["qmcentral.sort.damage.chaos"] = "混沌伤害",
                ["qmcentral.sort.damage.proton"] = "质子伤害",
                ["qmcentral.sort.damage.cryo"] = "冷冻伤害",
                ["qmcentral.count"] = "{0} 类 / {1} 件",
                ["qmcentral.page"] = "{0}/{1}",
                ["qmcentral.empty"] = "没有符合当前条件的库存物品",
                ["qmcentral.clear_filters"] = "清除筛选",
                ["qmcentral.select"] = "全选",
                ["qmcentral.ready_stack"] = "×{0} / 取 {1}",
                // 替换手搭数量弹窗后，原版上下文菜单用的条目。
                ["qmcentral.menu_select_all"] = "全选 {0} 件",
                ["qmcentral.menu_select_amount"] = "选中上方数量",
                ["qmcentral.menu_deselect"] = "取消该项选择",
                ["qmcentral.select_filtered"] = "全选当前",
                ["qmcentral.deselect_filtered"] = "取消当前",
                ["qmcentral.clear"] = "清空",
                ["qmcentral.recycle"] = "回收 {0}",
                ["qmcentral.recycle_dialog"] =
                    "确定把 {0} 件物资送去回收吗？此操作无法撤销。",
                ["qmcentral.recycle_apply"] = "回收",
                ["qmcentral.recycle_busy"] = "回收中",
                ["qmcentral.recycle_none"] = "未选择",
                ["qmcentral.category.all"] = "全部",
                ["qmcentral.category.weapons"] = "武器",
                ["qmcentral.category.equipment"] = "装备",
                ["qmcentral.category.ammo"] = "弹药",
                ["qmcentral.category.supplies"] = "补给",
                ["qmcentral.category.augments"] = "植入体",
                ["qmcentral.category.materials"] = "材料",
                ["qmcentral.category.barter"] = "贸易品",
                ["qmcentral.category.special"] = "特殊",
                ["qmcentral.detail.any"] = "全部",
                ["qmcentral.detail.ranged"] = "远程",
                ["qmcentral.detail.melee"] = "近战",
                ["qmcentral.detail.pistol"] = "手枪",
                ["qmcentral.detail.shotgun"] = "霰弹枪",
                ["qmcentral.detail.smg"] = "冲锋枪",
                ["qmcentral.detail.rifle"] = "步枪",
                ["qmcentral.detail.heavy"] = "重武器",
                ["qmcentral.detail.head"] = "头盔",
                ["qmcentral.detail.body"] = "护甲",
                ["qmcentral.detail.legs"] = "护腿",
                ["qmcentral.detail.boots"] = "靴子",
                ["qmcentral.detail.backpack"] = "背包",
                ["qmcentral.detail.vest"] = "背心",
                ["qmcentral.detail.ammunition"] = "弹药",
                ["qmcentral.detail.grenades"] = "手雷",
                ["qmcentral.detail.mines"] = "地雷",
                ["qmcentral.detail.turrets"] = "炮塔",
                ["qmcentral.detail.food"] = "食物",
                ["qmcentral.detail.drinks"] = "饮品",
                ["qmcentral.detail.medicine"] = "医疗",
                ["qmcentral.detail.repair"] = "维修",
                ["qmcentral.detail.auglimb"] = "肢体",
                ["qmcentral.detail.augimplant"] = "义体",
                ["qmcentral.detail.bio"] = "生体",
                ["qmcentral.detail.cybernetic"] = "机械",
                ["qmcentral.detail.quasi"] = "准形",
                ["qmcentral.detail.parts"] = "零件",
                ["qmcentral.detail.data"] = "数据",
                ["qmcentral.detail.blueprints"] = "蓝图",
                ["qmcentral.detail.organs"] = "器官",
                ["qmcentral.detail.military"] = "军用",
                ["qmcentral.detail.science"] = "科研",
                ["qmcentral.detail.industrial"] = "工业",
                ["qmcentral.detail.valuable"] = "贵重",
                ["qmcentral.detail.quest"] = "任务",
                ["qmcentral.detail.keys"] = "钥匙",
                ["qmcentral.detail.deployables"] = "装置",
                ["qmcentral.detail.cyborgs"] = "生化人",
                ["qmcentral.detail.other"] = "其他",
                // 空间站贸易面板（qmtrade）。
                ["qmtrade.title"] = "空间站贸易",
                ["qmtrade.close"] = "关闭",
                ["qmtrade.tab_buy"] = "购买",
                ["qmtrade.tab_sell"] = "出售",
                ["qmtrade.search"] = "搜索物品名称或内部 ID……",
                ["qmtrade.trade"] = "交易",
                ["qmtrade.clear"] = "清空",
                ["qmtrade.cancel"] = "取消",
                ["qmtrade.select_all"] = "全选可见",
                ["qmtrade.clear_filters"] = "清除筛选",
                ["qmtrade.trade_summary"] =
                    "出售 +{0} · 购买 -{1} · 交易后 {2}",
                ["qmtrade.dialog_trade"] = "完成全部交易？\n{0}",
                ["qmtrade.dialog_trade_details"] = "出售 +{0}，购买 -{1}",
                ["qmtrade.points"] = "贸易点 {0}",
                ["qmtrade.discount"] = "折扣 {0}",
                ["qmtrade.extra_charge"] = "加价 {0}",
                ["qmtrade.header.item"] = "物品",
                ["qmtrade.header.stock_buy"] = "对方库存",
                ["qmtrade.header.stock_sell"] = "我方持有",
                ["qmtrade.header.price"] = "单价",
                ["qmtrade.header.amount"] = "数量",
                ["qmtrade.header.total"] = "小计",
                ["qmtrade.quest"] = "任务",
                ["qmtrade.empty_buy"] = "该空间站没有可购买的商品",
                ["qmtrade.empty_sell"] = "当前仓库是空的",
                ["qmtrade.empty_filtered"] = "没有符合筛选条件的物品",
                ["qmtrade.low_reputation_buy"] = "声望不足，无法向该阵营购买",
                ["qmtrade.low_reputation_sell"] = "声望不足，无法向该阵营出售",
                ["qmtrade.cat.all"] = "全部",
                ["qmtrade.cat.weapons"] = "武器",
                ["qmtrade.cat.armor"] = "防具",
                ["qmtrade.cat.ammo"] = "弹药",
                ["qmtrade.cat.medical"] = "医疗",
                ["qmtrade.cat.food"] = "食物",
                ["qmtrade.cat.materials"] = "材料",
                ["qmtrade.cat.other"] = "其他",
                ["qmtrade.sort.name"] = "名称",
                ["qmtrade.sort.priceascending"] = "单价从低到高",
                ["qmtrade.sort.pricedescending"] = "单价从高到低",
                ["qmtrade.sort.quantitydescending"] = "数量从多到少",
                ["qmtrade.tip.close"] = "关闭贸易界面",
                ["qmtrade.tip.buy_tab"] = "从空间站购买商品",
                ["qmtrade.tip.sell_tab"] = "向空间站出售物品",
                ["qmtrade.tip.search"] = "按物品名称或内部 ID 搜索",
                ["qmtrade.tip.sort"] = "更改排序方式",
                ["qmtrade.tip.minus"] =
                    "减少 1 个；Shift 点击 -10；Ctrl 点击 -100；Ctrl+Shift 点击 -1000",
                ["qmtrade.tip.plus"] =
                    "增加 1 个；Shift 点击 +10；Ctrl 点击 +100；Ctrl+Shift 点击 +1000",
                ["qmtrade.tip.max"] = "设为最大可交易数量",
                ["qmtrade.tip.row"] =
                    "左键：输入数量\n右键：MAX\n按住左键滑动：划过的行变化一次——从 − 号出发为减，从 + 号出发为加，从 MAX 出发为拉满\n扫选时按住 Shift/Ctrl/Ctrl+Shift：×10/×100/×1000\n停在一行上不动：缓慢重复\n鼠标滚轮始终用于翻页",
                ["qmtrade.tip.trade"] = "一次完成全部待处理的出售与购买",
                ["qmtrade.tip.clear"] = "清空购物车",
                ["qmtrade.tip.select_all"] = "把当前列表里所有可出售物品按最大数量加入购物车",
                ["qmtrade.tip.clear_filters"] = "清除搜索词与分类筛选",
                ["qmtrade.tip.previous_page"] = "上一页——鼠标滚轮也可以翻页",
                ["qmtrade.tip.next_page"] = "下一页——鼠标滚轮也可以翻页",
                // Mod 配置菜单（MCM）条目：键值经 LocalizableLabel 走
                // Localization.Get 解析，跟随游戏语言并自动刷新。
                ["qmcentral.mcm.header"] = "中央管理",
                ["qmcentral.mcm.stationTrade"] = "启用空间站贸易界面",
                ["qmcentral.mcm.stationTrade.tip"] =
                    "用本模组的界面替换原版空间站交易界面。",
                ["qmcentral.mcm.tradeConfirm"] = "交易前确认",
                ["qmcentral.mcm.tradeConfirm.tip"] =
                    "完成整笔交易（全部待处理出售与购买）前弹出确认。",
                ["qmcentral.mcm.autoUnlockTech"] = "自动解锁科技",
                ["qmcentral.mcm.autoUnlockTech.tip"] =
                    "每个存档（包括新建存档）直接解锁「中央物资管理」科技，无需研究。",
                ["qmcentral.mcm.debugTradeLayout"] = "调试：输出交易界面布局",
                ["qmcentral.mcm.debugTradeLayout.tip"] =
                    "把原版交易界面层级信息写入 Player.log（诊断用）。",
                // 以物换物结算提示（如交付安共数据芯片等）。
                ["qmtrade.exchange_title"] = "以物换物——空间站交付了以下物品",
                ["qmtrade.exchange_item"] = "{0} ×{1}"
            };

        private static void PatchLocalization(Harmony harmony)
        {
            var current = AccessTools.Method(typeof(Localization),
                nameof(Localization.Get), new[] { typeof(string), typeof(bool) });
            var specified = AccessTools.Method(typeof(Localization),
                nameof(Localization.Get),
                new[] { typeof(string), typeof(Localization.Lang) });
            if (current == null || specified == null)
                throw new MissingMethodException("Localization.Get overloads were not found");
            harmony.Patch(current,
                postfix: new HarmonyMethod(typeof(Plugin),
                    nameof(LocalizationCurrentPostfix)));
            harmony.Patch(specified,
                postfix: new HarmonyMethod(typeof(Plugin),
                    nameof(LocalizationSpecifiedPostfix)));
        }

        private static void LocalizationCurrentPostfix(string key,
            ref string __result)
        {
            if (TryGetModText(key,
                    Singleton<Localization>.Instance.CurrentLang, out var text))
            {
                __result = text;
            }
        }

        private static void LocalizationSpecifiedPostfix(string key,
            Localization.Lang language, ref string __result)
        {
            if (TryGetModText(key, language, out var text))
                __result = text;
        }

        private static bool TryGetModText(string key,
            Localization.Lang language, out string text)
        {
            if (language == Localization.Lang.ChineseSimp
                && ChineseText.TryGetValue(key, out text))
            {
                return true;
            }
            if (EnglishText.TryGetValue(key, out text))
                return true;
            // A mod key that slipped through both dictionaries must never
            // be shown raw: humanize its last segment and log it once.
            // Scope it to keys this mod actually owns -- qmcentral.* and
            // qmtrade.* are exclusively ours, and for mgperk.* only OUR
            // technology. The bare "mgperk." prefix belongs to every vanilla
            // technology in the game, so touching it renamed the whole tech
            // tree to NAME/SUBNAME.
            if (key.StartsWith("qmtrade.", StringComparison.Ordinal)
                || key.StartsWith("qmcentral.", StringComparison.Ordinal)
                || key.StartsWith("mgperk." + TechId + ".",
                    StringComparison.Ordinal))
            {
                if (WarnedMissingKeys.Add(key))
                {
                    Debug.LogWarning(Plugin.LogPrefix
                                     + "missing localization for '" + key
                                     + "', using a generated label.");
                }
                var segment = key.Substring(key.LastIndexOf('.') + 1);
                text = string.Join(" ", segment.Split('_'))
                    .ToUpperInvariant();
                return true;
            }
            return false;
        }
    }
}

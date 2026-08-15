using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private const string ParentTechId = "store_constructor_department";

        private sealed class CentralManagementPerkRecord : MagnumPerkRecord
        {
            public CentralManagementPerkRecord()
            {
                Id = TechId;
            }
        }

        private static void RegisterTechnology(string modContentPath)
        {
            var parent = Data.MagnumPerks.GetRecord(ParentTechId);
            if (parent == null)
                throw new InvalidOperationException("parent Magnum technology '"
                                                    + ParentTechId + "' was not found");

            var active = LoadSprite(Path.Combine(modContentPath, "assets",
                "central_management_tech_active.png"),
                "CentralManagementTechActive");
            var locked = LoadSprite(Path.Combine(modContentPath, "assets",
                "central_management_tech_locked.png"),
                "CentralManagementTechLocked");
            FastAccessSprite = LoadSprite(Path.Combine(modContentPath, "assets",
                "central_management_fast.png"),
                "CentralManagementFastAccess");
            ActionNormalSprite = LoadSprite(Path.Combine(modContentPath,
                    "assets", "central_management_button_normal.png"),
                "CentralManagementActionNormal");
            ActionHoverSprite = LoadSprite(Path.Combine(modContentPath,
                    "assets", "central_management_button_hover.png"),
                "CentralManagementActionHover");
            ActionPressedSprite = LoadSprite(Path.Combine(modContentPath,
                    "assets", "central_management_button_pressed.png"),
                "CentralManagementActionPressed");

            var parentDescriptor = parent.ContentDescriptor as MagnumPerkDescriptor;
            active = active ?? parentDescriptor?.Icon;
            locked = locked ?? parentDescriptor?.LockedIcon ?? active;
            FastAccessSprite = FastAccessSprite ?? active;
            ActionNormalSprite = ActionNormalSprite ?? FastAccessSprite;
            ActionHoverSprite = ActionHoverSprite ?? ActionNormalSprite;
            ActionPressedSprite = ActionPressedSprite ?? ActionNormalSprite;
            if (active == null || locked == null || FastAccessSprite == null)
            {
                throw new InvalidOperationException(
                    "central-management sprites could not be loaded");
            }

            var descriptor = ScriptableObject.CreateInstance<MagnumPerkDescriptor>();
            descriptor.name = "QM_CentralManagement_Descriptor";
            Traverse.Create(descriptor).Field("_icon").SetValue(active);
            Traverse.Create(descriptor).Field("_lockedIcon").SetValue(locked);
            Traverse.Create(descriptor).Field("_fastActionIcon")
                .SetValue(FastAccessSprite);
            Traverse.Create(descriptor).Field("_fastActionLockedIcon")
                .SetValue(locked);
            OwnedAssets.Add(descriptor);

            var existing = Data.MagnumPerks.GetRecord(TechId);
            if (existing != null)
                return;

            var record = new CentralManagementPerkRecord
            {
                Enabled = true,
                ModuleId = parent.ModuleId,
                // An empty department id makes this a vanilla-style major
                // technology (large icon and major-upgrade sound).  Keep
                // IsDepartment false because this feature does not add a new
                // module department card or a simulated ship department.
                DepartmentId = string.Empty,
                IsDepartment = false,
                PositionInGraph = FindTechnologyPosition(parent),
                Parents = new List<string> { ParentTechId },
                Childs = new List<string>(),
                Modifiers = new List<MagnumParameterModifier>(),
                UpgradePrice = BuildUpgradePrice(),
                ContentDescriptor = descriptor
            };
            Data.MagnumPerks.AddRecord(record.Id, record);
            if (parent.Childs == null)
                parent.Childs = new List<string>();
            if (!parent.Childs.Contains(record.Id))
                parent.Childs.Add(record.Id);

            Debug.Log(LogPrefix + "registered Supply technology at "
                      + record.PositionInGraph + ".");
        }

        private static CellPosition FindTechnologyPosition(
            MagnumPerkRecord parent)
        {
            var occupied = new HashSet<CellPosition>();
            foreach (var record in Data.MagnumPerks.Records)
            {
                if (record.ModuleId == parent.ModuleId)
                    occupied.Add(record.PositionInGraph);
            }

            // The vanilla recycling department is at (3, 3); (3, 4) is the
            // free cell directly beneath it in the Supply tree.
            var preferred = new CellPosition(parent.PositionInGraph.X,
                parent.PositionInGraph.Y + 1);
            if (preferred.X >= 0 && preferred.X < 6 && preferred.Y >= 0
                && preferred.Y < 5 && !occupied.Contains(preferred))
            {
                return preferred;
            }

            var candidates = new[]
            {
                new CellPosition(parent.PositionInGraph.X + 1,
                    parent.PositionInGraph.Y),
                new CellPosition(parent.PositionInGraph.X - 1,
                    parent.PositionInGraph.Y),
                new CellPosition(parent.PositionInGraph.X,
                    parent.PositionInGraph.Y - 1)
            };
            foreach (var candidate in candidates)
            {
                if (candidate.X >= 0 && candidate.X < 6 && candidate.Y >= 0
                    && candidate.Y < 5 && !occupied.Contains(candidate))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "the Supply technology grid has no adjacent free cell");
        }

        private static List<string> BuildUpgradePrice()
        {
            var ids = new[]
            {
                "communication_relay",
                "automap",
                "electrical_parts_container",
                "ai_module"
            };
            var result = new List<string>(ids.Length);
            foreach (var id in ids)
            {
                if (Data.Items.GetRecord(id) == null)
                {
                    throw new InvalidOperationException(
                        "required central-management material '" + id
                        + "' was not found");
                }
                result.Add(id);
            }
            return result;
        }

        private static Sprite LoadSprite(string path, string assetName)
        {
            if (!File.Exists(path))
            {
                Debug.LogError(LogPrefix + "missing image: " + path);
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = assetName + "Texture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            var sprite = Sprite.Create(texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
            sprite.name = assetName;
            OwnedAssets.Add(texture);
            OwnedAssets.Add(sprite);
            return sprite;
        }
    }
}

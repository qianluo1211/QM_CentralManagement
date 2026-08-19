using System;
using System.Collections.Generic;
using System.IO;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        internal const string TechId = "qm_central_management";
        internal const string LogPrefix = "[CentralManagement] ";

        /// <summary>
        /// Keeps every asset this mod creates at runtime reachable for the
        /// whole session. Nothing reads the list -- holding the reference IS
        /// the job: Unity runs Resources.UnloadUnusedAssets on scene loads and
        /// would otherwise collect the technology icons and the perk
        /// descriptor, which are not owned by any scene.
        /// </summary>
        private static readonly List<UnityEngine.Object> OwnedAssets =
            new List<UnityEngine.Object>();

        /// <summary>
        /// Options retired by later versions. Accepted without complaint so an
        /// existing config.txt does not start reporting unknown keys, but they
        /// no longer feed anything:
        ///   recycleConfirmSeconds  batch recycling moved to the game's own
        ///                          ConfirmDialogWindow
        ///   preventTradeArbitrage  the anti-arbitrage price floor was removed;
        ///                          prices follow the vanilla formulas again
        ///   tradeWithoutTech       the trade screen is gated on the central
        ///                          management technology alone
        /// </summary>
        private static readonly HashSet<string> RetiredConfigKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "recycleConfirmSeconds",
                "preventTradeArbitrage",
                "tradeWithoutTech",
            };

        private static bool _ready;
        private static bool _debugLogging;
        private static string _modContentPath;
        private static KeyCode _centralShortcutKey = KeyCode.C;

        internal static State GameState { get; private set; }
        internal static Sprite FastAccessSprite { get; private set; }
        internal static Sprite ActionNormalSprite { get; private set; }
        internal static Sprite ActionHoverSprite { get; private set; }
        internal static Sprite ActionPressedSprite { get; private set; }
        internal static KeyCode CentralShortcutKey => _centralShortcutKey;

        [Hook(ModHookType.AfterConfigsLoaded)]
        public static void AfterConfigsLoaded(IModContext context)
        {
            try
            {
                LoadConfig(context.ModContentPath);
                RegisterTechnology(context.ModContentPath);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "technology registration failed: " + e);
            }
        }

        [Hook(ModHookType.AfterBootstrap)]
        public static void AfterBootstrap(IModContext context)
        {
            GameState = context.State;
            try
            {
                PatchGame();
                GlobalCurrencyBridge.Warmup();
                _ready = true;
                Debug.Log(LogPrefix + "ready. Technology = " + TechId + ".");
                RegisterWithMcm();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "initialization failed: " + e);
            }
        }

        [Hook(ModHookType.AfterSaveLoaded)]
        public static void AfterSaveLoaded(IModContext context)
        {
            GameState = context.State;
            DropSaveScopedState();
            RegisterWithMcm();
            TryAutoUnlockTech();
        }

        // A brand new game never goes through AfterSaveLoaded (see
        // GameModeStateMachine.ProcessStartGame: the new-game branch creates
        // the components and jumps straight into SpaceGameMode). SpaceStarted
        // fires at the end of SpaceGameMode.Run for every save, new or old,
        // so autoUnlockTech is applied there as well; AddPerk is idempotent.
        [Hook(ModHookType.SpaceStarted)]
        public static void SpaceStarted(IModContext context)
        {
            GameState = context.State;
            TryAutoUnlockTech();
        }

        /// <summary>
        /// Drops everything cached from the PREVIOUS save. The mod keeps no
        /// per-save storage of its own, so anything derived from save state
        /// has to be recomputed rather than carried across a load -- item
        /// stack sizes scale with the save's difficulty preset, and the
        /// preferred operator is a profile id that means a different agent
        /// (or none) in another campaign.
        /// </summary>
        private static void DropSaveScopedState()
        {
            CentralStationTradePanel.InvalidateSaveScopedCaches();
            _lastDeployedMercenaryProfileId = null;
            _centralOpenedFromRaidPrep = false;
        }

        /// <summary>
        /// autoUnlockTech: the Central Logistics Matrix perk is granted to
        /// every loaded save without researching it. Idempotent -- the game
        /// simply ignores an already purchased perk.
        /// </summary>
        private static void TryAutoUnlockTech()
        {
            if (!_autoUnlockTech)
                return;
            try
            {
                var progression = GameState?.Get<MagnumProgression>();
                if (progression == null)
                    return;
                if (!progression.IsPerkPurchased(TechId))
                    progression.AddPerk(TechId);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "could not auto-unlock the technology: " + e);
            }
        }

        internal static bool IsTechnologyUnlocked()
        {
            return _ready
                   && GameState?.Get<MagnumProgression>()
                       ?.IsPerkPurchased(TechId) == true;
        }

        internal static void DebugLog(string message)
        {
            if (_debugLogging)
                Debug.Log(LogPrefix + message);
        }

        private static void LoadConfig(string modContentPath)
        {
            _modContentPath = modContentPath;
            var path = Path.Combine(modContentPath ?? ".", "config.txt");
            if (File.Exists(path))
            {
                ParseConfigLines(File.ReadAllLines(path));
            }
            // Values saved through the Mod Configuration Menu override the
            // shipped defaults from config.txt.
            LoadMcmSettings();
        }

        private static void ParseConfigLines(string[] lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")
                                     || line.StartsWith("//"))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator < 0)
                    continue;
                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                if (RetiredConfigKeys.Contains(key))
                    continue;
                var option = FindConfigOption(key);
                if (option == null)
                {
                    Debug.LogWarning(LogPrefix + "unknown config key '"
                                     + key + "'.");
                    continue;
                }
                option.Parse(value);
            }
        }
    }
}

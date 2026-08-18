using System;
using System.Linq;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private static void PatchShuttleManifests(Harmony harmony)
        {
            // MagnumProgression.ResetDepartmentsForMissionStart is called from
            // exactly two places -- SpaceGameMode.StartMission and
            // SpaceGameMode.VisitStation -- and in both it sits immediately
            // before ShuttleCargoDepartment.MarkItemsForbidEvacuate(). That
            // makes it the one point that means "a deployment is starting,
            // the shuttle hold has not been sealed yet", which is exactly
            // when a manifest has to be filled. Patching the two callers
            // instead would be two private-method patches for the same
            // moment.
            PatchRequired(harmony, typeof(MagnumProgression),
                nameof(MagnumProgression.ResetDepartmentsForMissionStart),
                prefix: nameof(ShuttleRestockOnDeparturePrefix),
                argumentTypes: Type.EmptyTypes);
        }

        /// <summary>
        /// Fills the shuttle from the selected manifest as the raid launches,
        /// so the player never has to open the equipment screen at all.
        ///
        /// A PREFIX: the postfix position would be after
        /// MarkItemsForbidEvacuate has run over the hold, and items added
        /// afterwards would miss that pass -- data disks and skulls carried in
        /// the shuttle would come out evacuable when vanilla says they must
        /// not be.
        ///
        /// Silent, like the pre-departure top-up: this runs with the player
        /// mid-launch, where a modal about missing stock would interrupt the
        /// one moment they cannot act on it. Shortfalls go to Player.log.
        /// </summary>
        private static void ShuttleRestockOnDeparturePrefix(
            MagnumProgression __instance)
        {
            try
            {
                if (!ShuttleManifestsEnabled || !ShuttleAutoRestock)
                    return;
                var manifest = ShuttleManifestRepository.Selected;
                if (manifest == null
                    || ShuttleManifestService.StorageOf(__instance) == null)
                {
                    return;
                }
                var cargo = GameState?.Get<MagnumCargo>();
                if (cargo == null || cargo.ShipCargo.Count == 0)
                    return;
                var result = ShuttleManifestService.Restock(manifest, cargo,
                    __instance, GameState.Get<SpaceTime>(),
                    cargo.ShipCargo.FirstOrDefault());
                if (!string.IsNullOrEmpty(result.Error))
                {
                    Debug.LogWarning(LogPrefix + "departure restock of '"
                                     + manifest.Name + "' aborted: "
                                     + result.Error);
                    return;
                }
                var issues = result.AllIssues.ToList();
                DebugLog("departure restock '" + manifest.Name + "': "
                         + result.UnitsMoved + " unit(s) moved"
                         + (issues.Count == 0
                             ? "."
                             : ", " + issues.Count + " issue(s): "
                               + string.Join("; ", issues)));
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "departure shuttle restock failed: " + e);
            }
        }
    }
}

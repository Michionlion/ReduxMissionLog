using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.IO;
using KSP.Messages;
using KSP.OAB;
using KSP.Sim;
using KSP.Sim.impl;
using Newtonsoft.Json;
using UnityEngine;

namespace ReduxMissionLog
{
    internal sealed class SavedVehicleInfo
    {
        public string Id;
        public string Name;
        public string WorkspaceName;
        public string Description;
        public string DataLocation;
        public string Orientation;

        public string Key
        {
            get { return DataLocation + "|" + Id; }
        }
    }

    internal sealed class PlannedLaunchResult
    {
        public string PlanId;
        public string SlotId;
        public string CampaignId;
        public bool Success;
        public string VesselId;
        public string TravelObjectId;
        public string AssemblyName;
        public string Error;
        public float ResolvedRealtime;
    }

    // This adapter deliberately uses KSP's own workspace reader and launchpad flow.
    // Mission Log selects the requested craft, but it never spawns or flies a vessel.
    internal sealed class MissionPlanLaunchService : IDisposable
    {
        private sealed class PendingLaunch
        {
            public string PlanId;
            public string SlotId;
            public string CampaignId;
            public string ExpectedAssemblyName;
            public float StartedRealtime;
        }

        private const float LaunchTimeoutSeconds = 30f;
        private static readonly IOProvider.DataLocation[] PlayerLocations =
        {
            IOProvider.DataLocation.OABWorkspacesActiveCampaign,
            IOProvider.DataLocation.OABWorkspaces
        };

        private readonly Action<string> _info;
        private MessageCenter _messages;
        private SubscriptionHandle _launchSubscription;
        private bool _hasLaunchSubscription;
        private PendingLaunch _pending;

        public event Action<PlannedLaunchResult> LaunchResolved;

        public MissionPlanLaunchService(Action<string> info)
        {
            _info = info;
        }

        public bool HasPendingLaunch
        {
            get { return _pending != null; }
        }

        public List<SavedVehicleInfo> GetSavedVehicles()
        {
            var result = new List<SavedVehicleInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int locationIndex = 0;
                locationIndex < PlayerLocations.Length;
                locationIndex++)
            {
                IOProvider.DataLocation location = PlayerLocations[locationIndex];
                IEnumerable<string> files;
                try
                {
                    files = ObjectAssemblyBuilderFileIO.GetOABWorkspaceFileNames(location);
                }
                catch (Exception error)
                {
                    _info("Could not read " + location + " workspaces: " + error.Message);
                    continue;
                }
                if (files == null)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    string id = System.IO.Path.GetFileNameWithoutExtension(file);
                    string key = location + "|" + id;
                    if (string.IsNullOrWhiteSpace(id) || !seen.Add(key))
                    {
                        continue;
                    }
                    OABSavedWorkspaceMetaInfo metadata;
                    if (!ObjectAssemblyBuilderFileIO.PeekOABWorkspaceFromFile(
                        id,
                        location,
                        out metadata) || metadata == null || metadata.IsBackupWorkspace)
                    {
                        continue;
                    }

                    OABHistoricalSnapshot snapshot;
                    string orientation = string.Empty;
                    if (IOProvider.FromJsonFile<OABHistoricalSnapshot>(
                        location,
                        id,
                        out snapshot) && snapshot != null)
                    {
                        orientation = snapshot.oabOrientation.ToString();
                    }

                    result.Add(new SavedVehicleInfo
                    {
                        Id = id,
                        Name = FirstNonEmpty(metadata.VehicleName, metadata.Name, id),
                        WorkspaceName = FirstNonEmpty(metadata.Name, id),
                        Description = metadata.Description ?? string.Empty,
                        DataLocation = location.ToString(),
                        Orientation = orientation
                    });
                }
            }
            result.Sort(CompareVehicles);
            return result;
        }

        public bool TryLaunch(
            MissionPlan plan,
            MissionPlanVesselSlot slot,
            out string message)
        {
            message = string.Empty;
            if (plan == null || slot == null ||
                string.IsNullOrWhiteSpace(slot.SavedVehicleId))
            {
                message = "Select a saved vehicle before launching this slot.";
                return false;
            }
            if (_pending != null)
            {
                message = "Another planned vehicle is already entering KSP's launch flow.";
                return false;
            }

            IOProvider.DataLocation location;
            if (!Enum.TryParse(slot.SavedVehicleLocation, true, out location) ||
                (location != IOProvider.DataLocation.OABWorkspacesActiveCampaign &&
                 location != IOProvider.DataLocation.OABWorkspaces))
            {
                location = IOProvider.DataLocation.OABWorkspacesActiveCampaign;
            }
            OABSavedWorkspaceMetaInfo metadata;
            if (!ObjectAssemblyBuilderFileIO.PeekOABWorkspaceFromFile(
                slot.SavedVehicleId,
                location,
                out metadata))
            {
                message = "That saved vehicle is no longer available. Choose it again.";
                return false;
            }

            GameInstance game = GameManager.Instance == null
                ? null
                : GameManager.Instance.Game;
            if (game == null || game.OAB == null)
            {
                message = "KSP's launch flow is not ready in this scene.";
                return false;
            }

            OABHistoricalSnapshot snapshot;
            if (!IOProvider.FromJsonFile<OABHistoricalSnapshot>(
                location,
                slot.SavedVehicleId,
                out snapshot) || snapshot == null)
            {
                message = "KSP could not read that saved vehicle's launch identity.";
                return false;
            }
            string assemblyName = MainAssemblyName(snapshot);
            if (string.IsNullOrWhiteSpace(assemblyName))
            {
                message = "KSP could not read that saved vehicle's launch identity.";
                return false;
            }
            OABOrientation orientation = snapshot.oabOrientation;
            try
            {
                game.OAB.SetLaunchSite(orientation == OABOrientation.AIRPLANE
                    ? OABProvider.LaunchLocation.Runway_1
                    : OABProvider.LaunchLocation.Launchpad_1);
                var nearbyVessels = new List<VesselComponent>();
                if (!game.OAB.CheckSelectedLaunchSiteAvailabilityAndReturnVessels(
                    ref nearbyVessels))
                {
                    message = orientation == OABOrientation.AIRPLANE
                        ? "Runway 1 is occupied. Recover or move that vessel before launching."
                        : "Launchpad 1 is occupied. Recover or move that vessel before launching.";
                    return false;
                }
            }
            catch (Exception error)
            {
                message = "KSP's launch site is not ready: " + error.Message;
                return false;
            }

            if (game.Messages == null)
            {
                message = "KSP's launch event stream is not ready in this scene.";
                return false;
            }
            _messages = game.Messages;
            _pending = new PendingLaunch
            {
                PlanId = plan.PlanId,
                SlotId = slot.SlotId,
                CampaignId = plan.CampaignId,
                ExpectedAssemblyName = assemblyName,
                StartedRealtime = Time.realtimeSinceStartup
            };
            try
            {
                _launchSubscription = _messages.Subscribe<LaunchFromVABMessage>(
                    OnLaunchFromVab);
                _hasLaunchSubscription = true;
                new LaunchpadFileIO().LoadWorkspaceFromFile(
                    slot.SavedVehicleId,
                    location);
            }
            catch (Exception error)
            {
                ReleaseLaunchSubscription(true);
                message = "KSP could not begin the launch: " + error.Message;
                return false;
            }
            message = "KSP is launching " +
                FirstNonEmpty(slot.SavedVehicleName, metadata.VehicleName, slot.Name) + ".";
            return true;
        }

        public void Update(float realtime)
        {
            GameInstance game = GameManager.Instance == null
                ? null
                : GameManager.Instance.Game;
            if (_pending != null &&
                (game == null || !ReferenceEquals(game.Messages, _messages)))
            {
                ResolvePending(false, null, null, null,
                    "The active KSP campaign changed during launch.");
                return;
            }
            if (_pending != null &&
                realtime - _pending.StartedRealtime >= LaunchTimeoutSeconds)
            {
                ResolvePending(false, null, null, null,
                    "KSP did not confirm the planned launch in time.");
            }
        }

        public void Dispose()
        {
            if (_pending != null)
            {
                ResolvePending(
                    false,
                    null,
                    null,
                    null,
                    "The planned launch handoff was interrupted and needs review.");
            }
            else
            {
                ReleaseLaunchSubscription(true);
            }
            LaunchResolved = null;
        }

        private void OnLaunchFromVab(MessageCenterMessage raw)
        {
            LaunchFromVABMessage message = raw as LaunchFromVABMessage;
            if (_pending == null || message == null || message.SerializedVessel == null)
            {
                return;
            }
            string assemblyName = message.SerializedVessel.AssemblyDefinition.assemblyName;
            if (!string.Equals(
                _pending.ExpectedAssemblyName,
                assemblyName,
                StringComparison.Ordinal))
            {
                return;
            }
            VesselComponent vessel = message.vehicle == null
                ? null
                : message.vehicle.GetSimVessel(true);
            if (vessel == null)
            {
                ResolvePending(false, null, null, assemblyName,
                    "KSP created the assembly but did not expose its vessel identity.");
                return;
            }
            ResolvePending(
                true,
                vessel.GlobalId.ToString(),
                vessel.TravelObjectId.ToString(),
                assemblyName,
                null);
        }

        private void ResolvePending(
            bool success,
            string vesselId,
            string travelObjectId,
            string assemblyName,
            string error)
        {
            PendingLaunch pending = _pending;
            if (pending == null)
            {
                return;
            }
            ReleaseLaunchSubscription(true);
            Action<PlannedLaunchResult> handler = LaunchResolved;
            if (handler != null)
            {
                handler(new PlannedLaunchResult
                {
                    PlanId = pending.PlanId,
                    SlotId = pending.SlotId,
                    CampaignId = pending.CampaignId,
                    Success = success,
                    VesselId = vesselId,
                    TravelObjectId = travelObjectId,
                    AssemblyName = assemblyName,
                    Error = error,
                    ResolvedRealtime = Time.realtimeSinceStartup
                });
            }
        }

        private void ReleaseLaunchSubscription(bool clearPending)
        {
            if (_hasLaunchSubscription)
            {
                _launchSubscription.Release();
            }
            _launchSubscription = default(SubscriptionHandle);
            _hasLaunchSubscription = false;
            _messages = null;
            if (clearPending)
            {
                _pending = null;
            }
        }

        private static int CompareVehicles(SavedVehicleInfo left, SavedVehicleInfo right)
        {
            int name = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return name != 0
                ? name
                : string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase);
        }

        private static string MainAssemblyName(OABHistoricalSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Assemblies == null)
            {
                return null;
            }
            for (int index = 0; index < snapshot.Assemblies.Count; index++)
            {
                OABPlacedAssembly placed = snapshot.Assemblies[index];
                if (placed == null || !placed.isMainAssembly)
                {
                    continue;
                }
                SerializedAssembly assembly = placed.Assembly;
#pragma warning disable 618 // Older KSP workspaces still use the serialized string field.
                if (assembly == null && !string.IsNullOrWhiteSpace(placed.assembly))
                {
                    assembly = JsonConvert.DeserializeObject<SerializedAssembly>(
                        placed.assembly);
                }
#pragma warning restore 618
                return assembly == null
                    ? null
                    : assembly.AssemblyDefinition.assemblyName;
            }
            return null;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(values[index]))
                {
                    return values[index];
                }
            }
            return "Saved vehicle";
        }
    }
}

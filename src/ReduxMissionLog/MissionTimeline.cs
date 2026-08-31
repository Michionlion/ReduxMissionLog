using System;
using System.Collections.Generic;

namespace ReduxMissionLog
{
    internal sealed class MissionTimelineItem
    {
        public MissionEvent Event;
        public MissionRecord SourceMission;
        public string Category;
        public string CategoryLabel;
        public string Symbol;
        public bool IsDerived;
        public double Value;
        public string Unit;
    }

    internal static class MissionTimeline
    {
        private const double AltitudeThreshold = 10.0;
        private const double SpeedThreshold = 1.0;
        private const double GForceThreshold = 1.05;

        public static List<MissionTimelineItem> Build(
            MissionLineageResolver lineage,
            MissionRecord root)
        {
            var result = new List<MissionTimelineItem>();
            if (lineage == null || root == null)
            {
                return result;
            }

            var missions = new List<MissionRecord>();
            CollectMissions(lineage, root, missions, new HashSet<string>(StringComparer.Ordinal));

            var operationItems = new Dictionary<string, MissionTimelineItem>(StringComparer.Ordinal);
            for (int missionIndex = 0; missionIndex < missions.Count; missionIndex++)
            {
                MissionRecord mission = missions[missionIndex];
                for (int eventIndex = 0; eventIndex < mission.Events.Count; eventIndex++)
                {
                    MissionEvent entry = mission.Events[eventIndex];
                    if (!ShouldShow(entry) || IsPeak(entry.Kind))
                    {
                        continue;
                    }

                    MissionTimelineItem item = CreateItem(entry, mission, false, 0.0, string.Empty);
                    if (string.IsNullOrWhiteSpace(entry.OperationId))
                    {
                        result.Add(item);
                        continue;
                    }

                    MissionTimelineItem existing;
                    if (!operationItems.TryGetValue(entry.OperationId, out existing))
                    {
                        operationItems.Add(entry.OperationId, item);
                    }
                    else if (DisplayPriority(entry.Kind) > DisplayPriority(existing.Event.Kind))
                    {
                        operationItems[entry.OperationId] = item;
                    }
                }
            }

            foreach (MissionTimelineItem item in operationItems.Values)
            {
                result.Add(item);
            }

            AddPeak(result, missions, "peak_altitude", AltitudeThreshold);
            AddPeak(result, missions, "peak_speed", SpeedThreshold);
            AddPeak(result, missions, "peak_g_force", GForceThreshold);
            result.Sort(Compare);
            return result;
        }

        private static void CollectMissions(
            MissionLineageResolver lineage,
            MissionRecord mission,
            List<MissionRecord> result,
            HashSet<string> visited)
        {
            if (mission == null || !visited.Add(mission.MissionId))
            {
                return;
            }
            result.Add(mission);
            List<MissionRecord> children = lineage.GetChildren(mission);
            for (int index = 0; index < children.Count; index++)
            {
                CollectMissions(lineage, children[index], result, visited);
            }
        }

        private static void AddPeak(
            List<MissionTimelineItem> result,
            List<MissionRecord> missions,
            string kind,
            double threshold)
        {
            MissionRecord source = null;
            MissionEvent recorded = null;
            double maximum = 0.0;
            for (int index = 0; index < missions.Count; index++)
            {
                MissionRecord candidate = missions[index];
                double value = MetricValue(candidate, kind);
                if (!IsFiniteNonNegative(value))
                {
                    continue;
                }
                MissionEvent candidateEvent = FindEvent(candidate, kind);
                if (source == null || value > maximum ||
                    (value == maximum && PreferPeakSource(
                        candidate, candidateEvent, source, recorded)))
                {
                    source = candidate;
                    recorded = candidateEvent;
                    maximum = value;
                }
            }
            if (source == null || maximum < threshold)
            {
                return;
            }

            bool derived = recorded == null;
            MissionEvent entry = recorded ?? CreateDerivedPeak(source, kind, maximum);
            result.Add(CreateItem(entry, source, derived, maximum, MetricUnit(kind)));
        }

        private static MissionTimelineItem CreateItem(
            MissionEvent entry,
            MissionRecord source,
            bool derived,
            double value,
            string unit)
        {
            string category = Category(entry.Kind);
            return new MissionTimelineItem
            {
                Event = entry,
                SourceMission = source,
                Category = category,
                CategoryLabel = CategoryLabel(entry.Kind, category),
                Symbol = CategorySymbol(category),
                IsDerived = derived,
                Value = value,
                Unit = unit
            };
        }

        private static MissionEvent CreateDerivedPeak(
            MissionRecord source,
            string kind,
            double value)
        {
            return new MissionEvent
            {
                EventId = "derived-" + kind + "-" + source.MissionId,
                Kind = kind,
                Title = PeakTitle(kind, value),
                RecordedUtc = string.Empty,
                FlightTimeSeconds = 0.0,
                Body = string.Empty,
                Situation = string.Empty,
                RelatedMissionIds = new List<string>(),
                VesselIds = new List<string>(source.VesselIds)
            };
        }

        private static MissionEvent FindEvent(MissionRecord mission, string kind)
        {
            for (int index = 0; index < mission.Events.Count; index++)
            {
                if (string.Equals(mission.Events[index].Kind, kind,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return mission.Events[index];
                }
            }
            return null;
        }

        private static bool ShouldShow(MissionEvent entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Title))
            {
                return false;
            }
            return !string.Equals(entry.Kind, "situation_changed", StringComparison.Ordinal) &&
                !string.Equals(entry.Kind, "vessel_identity_changed", StringComparison.Ordinal);
        }

        private static bool IsPeak(string kind)
        {
            return string.Equals(kind, "peak_altitude", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "peak_speed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "peak_g_force", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0.0;
        }

        private static bool PreferPeakSource(
            MissionRecord candidate,
            MissionEvent candidateEvent,
            MissionRecord current,
            MissionEvent currentEvent)
        {
            if ((candidateEvent != null) != (currentEvent != null))
            {
                return candidateEvent != null;
            }
            string candidateTime = candidateEvent == null
                ? string.Empty
                : candidateEvent.RecordedUtc ?? string.Empty;
            string currentTime = currentEvent == null
                ? string.Empty
                : currentEvent.RecordedUtc ?? string.Empty;
            int time = string.Compare(candidateTime, currentTime, StringComparison.Ordinal);
            if (time != 0)
            {
                return time < 0;
            }
            return string.Compare(
                candidate.MissionId ?? string.Empty,
                current.MissionId ?? string.Empty,
                StringComparison.Ordinal) < 0;
        }

        private static double MetricValue(MissionRecord mission, string kind)
        {
            if (string.Equals(kind, "peak_altitude", StringComparison.Ordinal))
            {
                return mission.MaximumAltitudeMeters;
            }
            if (string.Equals(kind, "peak_speed", StringComparison.Ordinal))
            {
                return mission.MaximumSpeedMetersPerSecond;
            }
            return mission.MaximumGForce;
        }

        private static string MetricUnit(string kind)
        {
            if (string.Equals(kind, "peak_altitude", StringComparison.Ordinal))
            {
                return "m";
            }
            if (string.Equals(kind, "peak_speed", StringComparison.Ordinal))
            {
                return "m/s";
            }
            return "g";
        }

        private static string PeakTitle(string kind, double value)
        {
            if (string.Equals(kind, "peak_altitude", StringComparison.Ordinal))
            {
                return "Highest altitude — " + FormatDistance(value);
            }
            if (string.Equals(kind, "peak_speed", StringComparison.Ordinal))
            {
                return "Top speed — " + value.ToString(value >= 1000.0 ? "N0" : "N1") + " m/s";
            }
            return "Peak force — " + value.ToString("N2") + " g";
        }

        private static string FormatDistance(double meters)
        {
            return meters >= 1000.0
                ? (meters / 1000.0).ToString("N1") + " km"
                : meters.ToString("N0") + " m";
        }

        private static int Compare(MissionTimelineItem left, MissionTimelineItem right)
        {
            if (left.IsDerived != right.IsDerived)
            {
                return left.IsDerived ? 1 : -1;
            }
            int utc = string.Compare(
                left.Event.RecordedUtc ?? string.Empty,
                right.Event.RecordedUtc ?? string.Empty,
                StringComparison.Ordinal);
            if (utc != 0)
            {
                return utc;
            }
            int time = left.Event.FlightTimeSeconds.CompareTo(right.Event.FlightTimeSeconds);
            if (time != 0)
            {
                return time;
            }
            int operation = string.Compare(
                left.Event.OperationId ?? string.Empty,
                right.Event.OperationId ?? string.Empty,
                StringComparison.Ordinal);
            if (operation != 0)
            {
                return operation;
            }
            int source = string.Compare(
                left.SourceMission.MissionId ?? string.Empty,
                right.SourceMission.MissionId ?? string.Empty,
                StringComparison.Ordinal);
            if (source != 0)
            {
                return source;
            }
            return string.Compare(
                left.Event.EventId ?? string.Empty,
                right.Event.EventId ?? string.Empty,
                StringComparison.Ordinal);
        }

        private static int DisplayPriority(string kind)
        {
            if (string.Equals(kind, "missions_combined", StringComparison.Ordinal) ||
                string.Equals(kind, "missions_combined_manually", StringComparison.Ordinal) ||
                string.Equals(kind, "sibling_missions_combined", StringComparison.Ordinal) ||
                string.Equals(kind, "sub_mission_separated", StringComparison.Ordinal) ||
                string.Equals(kind, "sub_mission_rejoined", StringComparison.Ordinal) ||
                string.Equals(kind, "mission_adopted", StringComparison.Ordinal) ||
                string.Equals(kind, "mission_unlinked", StringComparison.Ordinal) ||
                string.Equals(kind, "vessel_binding_repaired", StringComparison.Ordinal))
            {
                return 100;
            }
            if (string.Equals(kind, "sub_mission_recovered", StringComparison.Ordinal) ||
                string.Equals(kind, "sub_mission_adopted", StringComparison.Ordinal) ||
                string.Equals(kind, "sub_mission_unlinked", StringComparison.Ordinal))
            {
                return 80;
            }
            if (string.Equals(kind, "joined_overarching_mission", StringComparison.Ordinal))
            {
                return 20;
            }
            return 60;
        }

        private static string Category(string kind)
        {
            if (string.Equals(kind, "launch", StringComparison.Ordinal) ||
                string.Equals(kind, "mission_started", StringComparison.Ordinal) ||
                string.Equals(kind, "observed_at_docking", StringComparison.Ordinal) ||
                string.Equals(kind, "sub_mission_started", StringComparison.Ordinal))
            {
                return "launch";
            }
            if (string.Equals(kind, "body_changed", StringComparison.Ordinal) ||
                string.Equals(kind, "orbit", StringComparison.Ordinal))
            {
                return "navigation";
            }
            if (string.Equals(kind, "landed", StringComparison.Ordinal) ||
                string.Equals(kind, "splashed", StringComparison.Ordinal))
            {
                return "surface";
            }
            if (IsPeak(kind))
            {
                return "record";
            }
            if (string.Equals(kind, "mission_completed", StringComparison.Ordinal))
            {
                return "outcome";
            }
            if (kind != null &&
                (kind.IndexOf("dock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kind.IndexOf("combined", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kind.IndexOf("joined", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kind.IndexOf("separated", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 kind.IndexOf("recovered", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "topology";
            }
            return "mission";
        }

        private static string CategoryLabel(string kind, string category)
        {
            if (string.Equals(kind, "orbit", StringComparison.Ordinal)) { return "ORBIT"; }
            if (string.Equals(kind, "body_changed", StringComparison.Ordinal)) { return "SOI"; }
            if (string.Equals(kind, "launch", StringComparison.Ordinal)) { return "LAUNCH"; }
            if (string.Equals(kind, "landed", StringComparison.Ordinal)) { return "LANDING"; }
            if (string.Equals(kind, "splashed", StringComparison.Ordinal)) { return "SPLASHDOWN"; }
            if (string.Equals(kind, "mission_completed", StringComparison.Ordinal)) { return "OUTCOME"; }
            if (string.Equals(category, "record", StringComparison.Ordinal)) { return "MISSION RECORD"; }
            if (string.Equals(category, "topology", StringComparison.Ordinal))
            {
                if (kind != null && kind.IndexOf("separat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "SEPARATION";
                }
                if (kind != null &&
                    (kind.IndexOf("rejoin", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     kind.IndexOf("recovered", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return "REUNION";
                }
                return "DOCKING";
            }
            return "MISSION";
        }

        private static string CategorySymbol(string category)
        {
            if (string.Equals(category, "launch", StringComparison.Ordinal)) { return "▲"; }
            if (string.Equals(category, "navigation", StringComparison.Ordinal)) { return "◎"; }
            if (string.Equals(category, "surface", StringComparison.Ordinal)) { return "▼"; }
            if (string.Equals(category, "topology", StringComparison.Ordinal)) { return "◇"; }
            if (string.Equals(category, "record", StringComparison.Ordinal)) { return "★"; }
            if (string.Equals(category, "outcome", StringComparison.Ordinal)) { return "■"; }
            return "•";
        }
    }
}

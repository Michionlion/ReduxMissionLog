using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;

namespace ReduxMissionLog
{
    internal static class MissionLineageScenarios
    {
        private const string CampaignId = "scenario-campaign";
        private const string CampaignName = "Scenario Campaign";
        private static int _assertions;
        private static int _passed;

        public static int Main()
        {
            try
            {
                Run("independent roots", IndependentRoots);
                Run("two-way docking", TwoWayDock);
                Run("sequential nested docking", SequentialNestedDock);
                Run("two composite missions dock", TwoCompositesDock);
                Run("duplicate operation is idempotent", DuplicateOperationIsIdempotent);
                Run("split creates a sortie", SplitCreatesSortie);
                Run("travel identity reactivates a sortie", TravelIdentityReactivatesSortie);
                Run("terminal aliases are not reused", TerminalAliasesAreNotReused);
                Run("non-descendant aliases are not reused", NonDescendantAliasIsNotReused);
                Run("conflicting split IDs are atomic", ConflictingSplitIdsAreAtomic);
                Run("split handles old ID on detached output", SplitHandlesOldIdOnDetachedOutput);
                Run("ambiguous split travel aliases are atomic", AmbiguousSplitTravelAliasesAreAtomic);
                Run("same-tree reunion", SameTreeReunion);
                Run("sibling sub-missions dock", SiblingDock);
                Run("descendant docks with an external root", ExternalRootDock);
                Run("manual reparent, unlink, and cycle rejection", ManualTreeEditing);
                Run("manual vessel assignment protects owners", ManualVesselAssignmentSafety);
                Run("travel rebind preserves mission identity", TravelRebindPreservesIdentity);
                Run("travel rebind survives an A-B-A operation cycle", TravelRebindOperationCycle);
                Run("terminal branch does not close its parent", TerminalStatusBranch);
                Run("validation detects invalid bindings", ValidationDetectsInvalidBindings);
                Run("schema 1 active mission migrates to schema 2", LegacyActiveMissionMigration);
                Run("malformed hierarchy and cycle are repaired", MalformedHierarchyRepair);
                Run("duplicate vessel ownership is repaired", DuplicateOwnershipRepair);
                Run("duplicate mission IDs unlink ambiguous children", DuplicateMissionIdRepair);
                Run("serialization round-trip", SerializationRoundTrip);

                Console.WriteLine(
                    "PASS: " + _passed + " lineage scenarios, " +
                    _assertions + " assertions.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: " + error.Message);
                Console.Error.WriteLine(error.StackTrace);
                return 1;
            }
        }

        private static void IndependentRoots()
        {
            Scenario scenario = NewScenario();
            MissionRecord x = scenario.Create("X", "v-x", "travel-x");
            scenario.AssertValid("after launch X");
            MissionRecord y = scenario.Create("Y", "v-y", "travel-y");
            scenario.AssertValid("after launch Y");

            Equal(2, scenario.Resolver.GetRoots(CampaignId).Count,
                "independent launches should remain separate roots");
            Same(x, scenario.Resolver.FindTrackedVessel(CampaignId, "v-x"),
                "X should own its live vessel");
            Same(y, scenario.Resolver.FindTrackedVessel(CampaignId, "v-y"),
                "Y should own its live vessel");
            Equal(null, x.ParentMissionId, "X should have no parent");
            Equal(null, y.ParentMissionId, "Y should have no parent");
        }

        private static void TwoWayDock()
        {
            Scenario scenario = NewScenario();
            MissionRecord x = scenario.Create("X", "v-x", "travel-x");
            MissionRecord y = scenario.Create("Y", "v-y", "travel-y");
            scenario.AssertValid("before two-way dock");

            MissionRecord combined = scenario.Resolver.Dock(
                CampaignId, "v-x", "v-y", "v-xy", "travel-xy", "X + Y",
                Moment(10), "dock-xy", false);
            scenario.AssertValid("after two-way dock");

            Equal(MissionLineageResolver.KindCombined, combined.MissionKind,
                "docking independent roots should create a combined mission");
            Equal(combined.MissionId, x.ParentMissionId, "X should become a child");
            Equal(combined.MissionId, y.ParentMissionId, "Y should become a child");
            Equal("Joined", x.Status, "X should be joined");
            Equal("Joined", y.Status, "Y should be joined");
            Equal("Active", combined.Status, "the combined mission should be active");
            Owns(combined, "v-xy", "the combined mission should own the result");
            Equal(2, scenario.Resolver.GetChildren(combined).Count,
                "the combined mission should contain both launches");
            Equal(1, scenario.Resolver.GetRoots(CampaignId).Count,
                "the docked tree should have one root");
        }

        private static void SequentialNestedDock()
        {
            Scenario scenario = NewScenario();
            MissionRecord x = scenario.Create("X", "v-x", "travel-x");
            MissionRecord y = scenario.Create("Y", "v-y", "travel-y");
            MissionRecord z = scenario.Create("Z", "v-z", "travel-z");
            scenario.AssertValid("after three launches");

            MissionRecord xy = scenario.Resolver.Dock(
                CampaignId, "v-x", "v-y", "v-xy", "travel-xy", "X + Y",
                Moment(10), "dock-xy", false);
            scenario.AssertValid("after first sequential dock");
            MissionRecord xyz = scenario.Resolver.Dock(
                CampaignId, "v-xy", "v-z", "v-xyz", "travel-xyz", "X + Y + Z",
                Moment(20), "dock-xyz", false);
            scenario.AssertValid("after second sequential dock");

            Equal(xyz.MissionId, xy.ParentMissionId,
                "the first combined mission should remain as a nested child");
            Equal(xyz.MissionId, z.ParentMissionId, "Z should join the new root");
            Equal(xy.MissionId, x.ParentMissionId, "X should remain below X + Y");
            Equal(xy.MissionId, y.ParentMissionId, "Y should remain below X + Y");
            Equal("Joined", xy.Status, "the intermediate composite should be joined");
            Owns(xyz, "v-xyz", "the outer mission should own the final vessel");
            Equal(1, scenario.Resolver.GetRoots(CampaignId).Count,
                "sequential docking should form one nested tree");
        }

        private static void TwoCompositesDock()
        {
            Scenario scenario = NewScenario();
            MissionRecord a = scenario.Create("A", "v-a", "travel-a");
            MissionRecord b = scenario.Create("B", "v-b", "travel-b");
            MissionRecord c = scenario.Create("C", "v-c", "travel-c");
            MissionRecord d = scenario.Create("D", "v-d", "travel-d");
            scenario.AssertValid("after four launches");

            MissionRecord ab = scenario.Resolver.Dock(
                CampaignId, "v-a", "v-b", "v-ab", "travel-ab", "A + B",
                Moment(10), "dock-ab", false);
            scenario.AssertValid("after A and B dock");
            MissionRecord cd = scenario.Resolver.Dock(
                CampaignId, "v-c", "v-d", "v-cd", "travel-cd", "C + D",
                Moment(20), "dock-cd", false);
            scenario.AssertValid("after C and D dock");
            MissionRecord all = scenario.Resolver.Dock(
                CampaignId, "v-ab", "v-cd", "v-all", "travel-all", "Full Stack",
                Moment(30), "dock-all", false);
            scenario.AssertValid("after both composites dock");

            Equal(all.MissionId, ab.ParentMissionId, "A + B should remain a subtree");
            Equal(all.MissionId, cd.ParentMissionId, "C + D should remain a subtree");
            Equal(ab.MissionId, a.ParentMissionId, "A should remain under A + B");
            Equal(ab.MissionId, b.ParentMissionId, "B should remain under A + B");
            Equal(cd.MissionId, c.ParentMissionId, "C should remain under C + D");
            Equal(cd.MissionId, d.ParentMissionId, "D should remain under C + D");
            Equal(7, scenario.Archive.Missions.Count,
                "four launches and three combinations should be retained");
            Owns(all, "v-all", "the outer composite should own the full stack");
        }

        private static void DuplicateOperationIsIdempotent()
        {
            Scenario scenario = NewScenario();
            scenario.Create("X", "v-x", "travel-x");
            scenario.Create("Y", "v-y", "travel-y");
            MissionRecord combined = scenario.Resolver.Dock(
                CampaignId, "v-x", "v-y", "v-xy", "travel-xy", "X + Y",
                Moment(10), "same-operation", false);
            scenario.AssertValid("after original operation");
            int missionCount = scenario.Archive.Missions.Count;
            int eventCount = CountEvents(scenario.Archive);

            MissionRecord duplicate = scenario.Resolver.Dock(
                CampaignId, "v-x", "v-y", "v-xy", "travel-xy", "X + Y",
                Moment(11), "same-operation", false);
            scenario.AssertValid("after duplicate operation");

            Equal(combined.MissionId, duplicate.MissionId,
                "a duplicate operation should resolve to the existing result");
            Equal(missionCount, scenario.Archive.Missions.Count,
                "a duplicate operation must not add a mission");
            Equal(eventCount, CountEvents(scenario.Archive),
                "a duplicate operation must not add events");
        }

        private static void SplitCreatesSortie()
        {
            Scenario scenario = NewScenario();
            MissionRecord carrierMission = scenario.Create(
                "Carrier", "v-stack", "travel-stack");
            scenario.AssertValid("before lander separation");

            MissionRecord lander = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-carrier", "travel-carrier", "Carrier",
                "v-lander", "travel-lander", "Lander",
                Moment(10), "split-lander", new[] { "Valentina Kerman" });
            scenario.AssertValid("after lander separation");

            Equal(MissionLineageResolver.KindSortie, lander.MissionKind,
                "a new detached craft should become a sortie");
            Equal(carrierMission.MissionId, lander.ParentMissionId,
                "the sortie should be a child of its source mission");
            Equal(MissionLineageResolver.RelationSeparatedCraft, lander.ParentRelation,
                "the parent relation should describe a separation");
            Owns(carrierMission, "v-carrier", "the source should follow the continuation");
            Owns(lander, "v-lander", "the sortie should follow the detached craft");
            Equal("Active", carrierMission.Status, "the carrier should remain active");
            Equal("Active", lander.Status, "the lander should be active");
            True(lander.Crew.Contains("Valentina Kerman"),
                "detached crew should be captured by the sortie");
        }

        private static void TravelIdentityReactivatesSortie()
        {
            Scenario scenario = NewScenario();
            MissionRecord carrierMission = scenario.Create(
                "Carrier", "v-stack", "travel-stack");
            MissionRecord firstSortie = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-carrier-1", "travel-carrier", "Carrier",
                "v-lander-1", "travel-lander", "Lander",
                Moment(10), "split-1", null);
            scenario.AssertValid("after first separation");

            MissionRecord reunited = scenario.Resolver.Dock(
                CampaignId,
                "v-carrier-1", "v-lander-1", "v-stack-2", "travel-stack-2", "Carrier + Lander",
                Moment(20), "redock-1", false);
            scenario.AssertValid("after first reunion");
            Same(carrierMission, reunited,
                "an ancestor and child reunion should reuse the ancestor");
            Equal("Rejoined", firstSortie.Status,
                "the returned sortie should be marked rejoined");
            Equal(0, firstSortie.TrackedVesselIds.Count,
                "the returned sortie should release vessel ownership");

            MissionRecord secondSortie = scenario.Resolver.Split(
                CampaignId,
                "v-stack-2", "v-carrier-2", "travel-carrier", "Carrier",
                "v-lander-2", "travel-lander", "Lander",
                Moment(30), "split-2", null);
            scenario.AssertValid("after second separation");

            Equal(firstSortie.MissionId, secondSortie.MissionId,
                "the original travel identity should reactivate the same sortie");
            Equal(2, scenario.Archive.Missions.Count,
                "repeated separation should not duplicate the sortie");
            Equal("Active", secondSortie.Status, "the sortie should be active again");
            Owns(secondSortie, "v-lander-2",
                "the reactivated sortie should own the new vessel alias");
            True(secondSortie.VesselIds.Contains("v-lander-1") &&
                 secondSortie.VesselIds.Contains("v-lander-2"),
                "the sortie should retain both vessel aliases");
        }

        private static void TerminalAliasesAreNotReused()
        {
            string[] terminalStatuses = { "Lost", "Recovered", "Completed" };
            for (int statusIndex = 0; statusIndex < terminalStatuses.Length; statusIndex++)
            {
                string terminalStatus = terminalStatuses[statusIndex];
                Scenario scenario = NewScenario();
                scenario.Create("Carrier", "v-stack", "travel-stack");
                MissionRecord original = scenario.Resolver.Split(
                    CampaignId,
                    "v-stack", "v-carrier-1", "travel-carrier", "Carrier",
                    "v-lander-1", "travel-lander", "Lander",
                    Moment(10), "split-original-" + terminalStatus, null);
                scenario.AssertValid("after original " + terminalStatus + " sortie split");
                scenario.Resolver.Dock(
                    CampaignId,
                    "v-carrier-1", "v-lander-1", "v-stack-2", "travel-stack-2", "Stack",
                    Moment(20), "redock-" + terminalStatus, false);
                scenario.AssertValid("after original " + terminalStatus + " sortie redock");

                original.Status = terminalStatus;
                original.EndedUtc = Moment(25).RecordedUtc;
                scenario.AssertValid("after marking old sortie " + terminalStatus);
                int before = scenario.Archive.Missions.Count;

                MissionRecord replacement = scenario.Resolver.Split(
                    CampaignId,
                    "v-stack-2", "v-carrier-2", "travel-carrier", "Carrier",
                    "v-lander-2", "travel-lander", "Lander",
                    Moment(30), "split-replacement-" + terminalStatus, null);
                scenario.AssertValid("after split beside terminal " + terminalStatus + " alias");

                True(!ReferenceEquals(original, replacement),
                    terminalStatus + " history must not be reactivated as a sortie");
                True(original.MissionId != replacement.MissionId,
                    terminalStatus + " history must retain a distinct identity");
                Equal(terminalStatus, original.Status,
                    terminalStatus + " history must retain its terminal status");
                Equal(before + 1, scenario.Archive.Missions.Count,
                    terminalStatus + " alias should cause a new sortie to be created");
                Equal(original.ParentMissionId, replacement.ParentMissionId,
                    "the new sortie should remain in the same source tree");
                Owns(replacement, "v-lander-2",
                    "the new sortie should own the detached vessel");
            }
        }

        private static void NonDescendantAliasIsNotReused()
        {
            Scenario scenario = NewScenario();
            MissionRecord source = scenario.Create("Carrier", "v-source", "travel-source");
            MissionRecord unrelated = scenario.Create(
                "Unrelated", "v-unrelated", "travel-detached");
            unrelated.Status = "Joined";
            unrelated.EndedUtc = Moment(5).RecordedUtc;
            unrelated.TrackedVesselIds.Clear();
            unrelated.TrackedTravelObjectId = null;
            scenario.AssertValid("after archiving unrelated alias");
            int before = scenario.Archive.Missions.Count;

            MissionRecord detached = scenario.Resolver.Split(
                CampaignId,
                "v-source", "v-continuation", "travel-continuation", "Carrier",
                "v-detached", "travel-detached", "Detached Craft",
                Moment(10), "split-with-unrelated-alias", null);
            scenario.AssertValid("after split with unrelated alias");

            True(!ReferenceEquals(unrelated, detached),
                "an alias in another root must not be reused");
            Equal(before + 1, scenario.Archive.Missions.Count,
                "a non-descendant alias should produce a new sortie");
            Equal(source.MissionId, detached.ParentMissionId,
                "the new sortie should be parented to the actual source");
            Equal(null, unrelated.ParentMissionId,
                "the unrelated alias should remain a separate root");
            Equal("Joined", unrelated.Status,
                "the unrelated archive record should remain unchanged");
            Owns(detached, "v-detached", "the new sortie should own the detached craft");
        }

        private static void ConflictingSplitIdsAreAtomic()
        {
            Scenario scenario = NewScenario();
            MissionRecord source = scenario.Create("Source", "v-source", "travel-source");
            MissionRecord other = scenario.Create("Other", "v-other", "travel-other");
            scenario.AssertValid("before rejected split conflicts");

            AssertRejectedSplitIsAtomic(
                scenario,
                () => scenario.Resolver.Split(
                    CampaignId,
                    "v-source", "v-other", "travel-continuation", "Source",
                    "v-new", "travel-new", "Detached",
                    Moment(10), "conflict-continuation", null),
                "a continuation ID owned by another mission should be rejected");
            Same(source, scenario.Resolver.FindTrackedVessel(CampaignId, "v-source"),
                "source ownership should survive a continuation conflict");
            Same(other, scenario.Resolver.FindTrackedVessel(CampaignId, "v-other"),
                "other ownership should survive a continuation conflict");

            AssertRejectedSplitIsAtomic(
                scenario,
                () => scenario.Resolver.Split(
                    CampaignId,
                    "v-source", "v-new", "travel-continuation", "Source",
                    "v-other", "travel-other", "Detached",
                    Moment(20), "conflict-detached", null),
                "a detached ID owned by another mission should be rejected");
            Same(source, scenario.Resolver.FindTrackedVessel(CampaignId, "v-source"),
                "source ownership should survive a detached conflict");
            Same(other, scenario.Resolver.FindTrackedVessel(CampaignId, "v-other"),
                "other ownership should survive a detached conflict");

            AssertRejectedSplitIsAtomic(
                scenario,
                () => scenario.Resolver.Split(
                    CampaignId,
                    "v-source", "v-same", "travel-same", "Source",
                    "v-same", "travel-same", "Detached",
                    Moment(30), "conflict-same-result", null),
                "continuation and detached IDs must differ");
            scenario.AssertValid("after all rejected split conflicts");
        }

        private static void SplitHandlesOldIdOnDetachedOutput()
        {
            Scenario scenario = NewScenario();
            MissionRecord parent = scenario.Create(
                "PreSplit", "v-retained-by-detached", "travel-pre-split");
            scenario.AssertValid("before detached-side ID retention split");

            MissionRecord detached = scenario.Resolver.Split(
                CampaignId,
                "v-event-source-no-longer-tracked",
                "v-continuing-output",
                "travel-continuing-output",
                "Continuing Craft",
                "v-retained-by-detached",
                "travel-detached-output",
                "Detached Craft",
                Moment(10),
                "split-old-id-on-detached",
                new[] { "Bob Kerman" });
            scenario.AssertValid("after detached-side ID retention split");

            Equal(parent.MissionId, detached.ParentMissionId,
                "the detached result should become a child of the pre-split mission");
            Equal(MissionLineageResolver.KindSortie, detached.MissionKind,
                "the detached result should be represented as a sortie");
            Owns(parent, "v-continuing-output",
                "the pre-split mission should follow the remaining output");
            Owns(detached, "v-retained-by-detached",
                "the new sortie should take ownership of the detached old ID");
            Same(parent, scenario.Resolver.FindTrackedVessel(
                    CampaignId, "v-continuing-output"),
                "the continuing output should resolve to the parent mission");
            Same(detached, scenario.Resolver.FindTrackedVessel(
                    CampaignId, "v-retained-by-detached"),
                "the retained old ID should resolve to the detached sortie");
            True(parent.VesselIds.Contains("v-retained-by-detached") &&
                 parent.VesselIds.Contains("v-continuing-output"),
                "the parent should retain historical and continuing aliases");
            True(detached.Crew.Contains("Bob Kerman"),
                "the detached sortie should capture its crew");
            Equal(2, scenario.Archive.Missions.Count,
                "the split should leave one continuing parent and one sortie");
        }

        private static void AmbiguousSplitTravelAliasesAreAtomic()
        {
            Scenario scenario = NewScenario();
            MissionRecord continuationCandidate = scenario.Create(
                "ContinuationCandidate", "v-candidate-a", "travel-continuation");
            MissionRecord detachedCandidate = scenario.Create(
                "DetachedCandidate", "v-candidate-b", "travel-detached");
            scenario.AssertValid("before ambiguous travel-alias split");

            AssertRejectedSplitIsAtomic(
                scenario,
                () => scenario.Resolver.Split(
                    CampaignId,
                    "v-source-unmatched",
                    "v-continuation-unmatched",
                    "travel-continuation",
                    "Continuing Output",
                    "v-detached-unmatched",
                    "travel-detached",
                    "Detached Output",
                    Moment(10),
                    "ambiguous-travel-alias-split",
                    null),
                "two active travel aliases must not be guessed into one split source");

            Same(continuationCandidate, scenario.Resolver.FindTrackedVessel(
                    CampaignId, "v-candidate-a"),
                "the continuation alias candidate should retain its vessel");
            Same(detachedCandidate, scenario.Resolver.FindTrackedVessel(
                    CampaignId, "v-candidate-b"),
                "the detached alias candidate should retain its vessel");
            Equal(2, scenario.Resolver.GetRoots(CampaignId).Count,
                "ambiguous aliases should remain independent roots");
            scenario.AssertValid("after rejected ambiguous travel-alias split");
        }

        private static void SameTreeReunion()
        {
            Scenario scenario = NewScenario();
            MissionRecord parent = scenario.Create("Orbiter", "v-stack", "travel-stack");
            MissionRecord child = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-orbiter", "travel-orbiter", "Orbiter",
                "v-lander", "travel-lander", "Lander",
                Moment(10), "split", null);
            scenario.AssertValid("after same-tree split");

            int before = scenario.Archive.Missions.Count;
            MissionRecord result = scenario.Resolver.Dock(
                CampaignId,
                "v-orbiter", "v-lander", "v-rejoined", "travel-rejoined", "Rejoined Stack",
                Moment(20), "rejoin", false);
            scenario.AssertValid("after same-tree reunion");

            Same(parent, result, "same-tree reunion should reuse the ancestor mission");
            Equal(before, scenario.Archive.Missions.Count,
                "same-tree reunion should not create an overarching root");
            Equal(parent.MissionId, child.ParentMissionId,
                "the child relationship should be retained");
            Equal("Rejoined", child.Status, "the child should be marked rejoined");
            Owns(parent, "v-rejoined", "the ancestor should own the reunited vessel");
        }

        private static void SiblingDock()
        {
            Scenario scenario = NewScenario();
            MissionRecord carrier = scenario.Create("Carrier", "v-stack", "travel-stack");
            MissionRecord landerOne = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-carrier-1", "travel-carrier", "Carrier",
                "v-lander-1", "travel-lander-1", "Lander One",
                Moment(10), "split-1", null);
            scenario.AssertValid("after first sibling split");
            MissionRecord landerTwo = scenario.Resolver.Split(
                CampaignId,
                "v-carrier-1", "v-carrier-2", "travel-carrier", "Carrier",
                "v-lander-2", "travel-lander-2", "Lander Two",
                Moment(20), "split-2", null);
            scenario.AssertValid("after second sibling split");

            MissionRecord landerStack = scenario.Resolver.Dock(
                CampaignId,
                "v-lander-1", "v-lander-2", "v-lander-stack", "travel-lander-stack",
                "Lander Pair", Moment(30), "dock-landers", false);
            scenario.AssertValid("after sibling docking");

            Equal(carrier.MissionId, landerStack.ParentMissionId,
                "the sibling composite should remain under their common parent");
            Equal(landerStack.MissionId, landerOne.ParentMissionId,
                "the first sibling should move below the composite");
            Equal(landerStack.MissionId, landerTwo.ParentMissionId,
                "the second sibling should move below the composite");
            Owns(carrier, "v-carrier-2", "the separate carrier should retain its vessel");
            Owns(landerStack, "v-lander-stack",
                "the sibling composite should own the docked landers");
            Equal(1, scenario.Resolver.GetRoots(CampaignId).Count,
                "sibling docking should not create a new top-level mission");
        }

        private static void ExternalRootDock()
        {
            Scenario scenario = NewScenario();
            MissionRecord carrier = scenario.Create("Carrier", "v-stack", "travel-stack");
            MissionRecord lander = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-carrier", "travel-carrier", "Carrier",
                "v-lander", "travel-lander", "Lander",
                Moment(10), "split", null);
            scenario.AssertValid("after external scenario split");
            MissionRecord visitor = scenario.Create("Visitor", "v-visitor", "travel-visitor");
            scenario.AssertValid("after visitor launch");

            MissionRecord joint = scenario.Resolver.Dock(
                CampaignId,
                "v-lander", "v-visitor", "v-joint", "travel-joint", "Joint Lander",
                Moment(20), "dock-external", false);
            scenario.AssertValid("after descendant docks an external root");

            Equal(joint.MissionId, carrier.ParentMissionId,
                "the descendant's whole prior root should join the new root");
            Equal(joint.MissionId, visitor.ParentMissionId,
                "the external mission should join the new root");
            Equal(carrier.MissionId, lander.ParentMissionId,
                "the descendant should keep its prior containment");
            Equal("Joined", lander.Status, "the participating descendant should be joined");
            Equal("Joined", visitor.Status, "the external participant should be joined");
            Owns(carrier, "v-carrier",
                "the non-participating carrier should retain its vessel");
            Owns(joint, "v-joint", "the new root should own the docked result");
            Equal(1, scenario.Resolver.GetRoots(CampaignId).Count,
                "the operation should produce one overarching tree");
        }

        private static void ManualTreeEditing()
        {
            Scenario scenario = NewScenario();
            MissionRecord a = scenario.Create("A", "v-a", "travel-a");
            MissionRecord b = scenario.Create("B", "v-b", "travel-b");
            scenario.AssertValid("before manual adoption");

            scenario.Resolver.Reparent(
                b, a, MissionLineageResolver.RelationManual,
                Moment(10), "adopt-b");
            scenario.AssertValid("after manual adoption");
            Equal(a.MissionId, b.ParentMissionId, "B should be manually adopted by A");
            Owns(a, "v-a", "manual grouping should retain A's vessel");
            Owns(b, "v-b", "manual grouping should retain B's vessel");

            string originalParent = b.ParentMissionId;
            Throws<InvalidOperationException>(() => scenario.Resolver.Reparent(
                    a, b, MissionLineageResolver.RelationManual,
                    Moment(20), "cycle"),
                "reparenting an ancestor below its child should be rejected");
            Equal(null, a.ParentMissionId,
                "cycle rejection should not partially modify the ancestor");
            Equal(originalParent, b.ParentMissionId,
                "cycle rejection should not modify the child");
            scenario.AssertValid("after rejected cycle");

            scenario.Resolver.Unlink(b, Moment(30), "unlink-b");
            scenario.AssertValid("after manual unlink");
            Equal(null, b.ParentMissionId, "unlink should restore B as a root");
            Equal(null, b.ParentRelation, "unlink should clear the parent relation");
            Equal(2, scenario.Resolver.GetRoots(CampaignId).Count,
                "unlink should restore two roots");
            Owns(b, "v-b", "unlink should preserve vessel ownership");
        }

        private static void ManualVesselAssignmentSafety()
        {
            Scenario scenario = NewScenario();
            MissionRecord originalOwner = scenario.Create(
                "Original", "v-original", "travel-original");
            MissionRecord target = scenario.Create(
                "Target", "v-target", "travel-target");
            scenario.AssertValid("before protected manual assignment");

            string beforeRejected = Snapshot(scenario.Archive);
            Throws<InvalidOperationException>(() => scenario.Resolver.TrackAsMission(
                    target,
                    CampaignId,
                    "v-original",
                    "travel-original",
                    "Original Craft",
                    Moment(10),
                    "manual-refused"),
                "manual assignment must not silently drop the target's different live craft");
            Equal(beforeRejected, Snapshot(scenario.Archive),
                "a refused manual assignment should be mutation-free");
            Owns(originalOwner, "v-original",
                "the original owner should remain bound after refusal");
            Owns(target, "v-target",
                "the target should retain its different craft after refusal");
            scenario.AssertValid("after refused manual assignment");

            target.Status = "Joined";
            target.EndedUtc = Moment(15).RecordedUtc;
            target.TrackedVesselIds.Clear();
            target.TrackedTravelObjectId = null;
            scenario.AssertValid("after making the target available");

            scenario.Resolver.TrackAsMission(
                target,
                CampaignId,
                "v-original",
                "travel-original",
                "Original Craft",
                Moment(20),
                "manual-reassign");
            scenario.AssertValid("after manual owner displacement");

            Equal("Joined", originalOwner.Status,
                "the displaced owner should be closed as joined");
            True(originalOwner.NeedsReview,
                "the displaced owner should be marked for review");
            True(!string.IsNullOrWhiteSpace(originalOwner.EndedUtc),
                "the displaced owner should receive an end time");
            Equal(0, originalOwner.TrackedVesselIds.Count,
                "the displaced owner should release the vessel");
            Equal(null, originalOwner.TrackedTravelObjectId,
                "the displaced owner should release its live travel binding");
            Equal("Active", target.Status,
                "the selected mission should become active");
            Owns(target, "v-original",
                "the selected mission should become the sole vessel owner");
            True(HasEvent(originalOwner, "vessel_binding_reassigned"),
                "the displaced owner should retain an audit event");
            True(HasEvent(target, "vessel_binding_repaired"),
                "the selected mission should retain an audit event");
        }

        private static void TravelRebindPreservesIdentity()
        {
            Scenario scenario = NewScenario();
            MissionRecord mission = scenario.Create(
                "Continuity", "v-before", "travel-stable");
            string missionId = mission.MissionId;
            int beforeCount = scenario.Archive.Missions.Count;
            scenario.AssertValid("before travel identity rebind");

            scenario.Resolver.RebindTravelIdentity(
                mission,
                CampaignId,
                "v-after",
                "travel-stable",
                "Renamed Craft",
                Moment(10),
                "travel-rebind");
            scenario.AssertValid("after travel identity rebind");

            Equal(missionId, mission.MissionId,
                "a vessel identity change should preserve mission identity");
            Equal(beforeCount, scenario.Archive.Missions.Count,
                "a vessel identity change should not create a mission");
            Owns(mission, "v-after", "the mission should own the replacement vessel ID");
            True(mission.VesselIds.Contains("v-before"),
                "the original vessel ID should remain an alias");
            True(mission.VesselIds.Contains("v-after"),
                "the replacement vessel ID should be added as an alias");
            True(mission.TravelObjectIds.Contains("travel-stable"),
                "the stable travel alias should remain available");
            Equal("travel-stable", mission.TrackedTravelObjectId,
                "the live travel binding should be preserved");
            Same(mission, scenario.Resolver.FindAlias(CampaignId, "v-before"),
                "the original vessel alias should resolve to the same mission");
            Same(mission, scenario.Resolver.FindTrackedVessel(CampaignId, "v-after"),
                "the replacement vessel should resolve to the same mission");
            True(HasEvent(mission, "vessel_identity_changed"),
                "the rebind should be auditable");
        }

        private static void TravelRebindOperationCycle()
        {
            Scenario scenario = NewScenario();
            MissionRecord mission = scenario.Create(
                "Cycle", "v-a", "travel-cycle");
            scenario.AssertValid("before A-B-A identity cycle");

            scenario.Resolver.RebindTravelIdentity(
                mission,
                CampaignId,
                "v-b",
                "travel-cycle",
                "Cycle B",
                Moment(10),
                "reused-cycle-operation");
            scenario.AssertValid("after A to B identity change");
            Owns(mission, "v-b", "the first rebind should move ownership to B");

            scenario.Resolver.RebindTravelIdentity(
                mission,
                CampaignId,
                "v-a",
                "travel-cycle",
                "Cycle A",
                Moment(20),
                "reused-cycle-operation");
            scenario.AssertValid("after B to A identity change with reused operation ID");

            Owns(mission, "v-a",
                "operation deduplication must not block a real B-to-A identity change");
            Same(mission, scenario.Resolver.FindTrackedVessel(CampaignId, "v-a"),
                "A should resolve to the original mission after the cycle");
            True(mission.VesselIds.Contains("v-a") && mission.VesselIds.Contains("v-b"),
                "the identity cycle should retain both vessel aliases");
            Equal(1, scenario.Archive.Missions.Count,
                "an identity cycle should not create another mission");
            Equal(2, CountEventsWithOperation(mission, "reused-cycle-operation"),
                "both real identity changes should remain auditable despite operation-ID reuse");

            int eventsBeforeExactDuplicate = mission.Events.Count;
            scenario.Resolver.RebindTravelIdentity(
                mission,
                CampaignId,
                "v-a",
                "travel-cycle",
                "Cycle A",
                Moment(30),
                "reused-cycle-operation");
            scenario.AssertValid("after exact duplicate identity notification");
            Equal(eventsBeforeExactDuplicate, mission.Events.Count,
                "an exact duplicate notification should remain idempotent");
        }

        private static void TerminalStatusBranch()
        {
            Scenario scenario = NewScenario();
            MissionRecord orbiter = scenario.Create("Orbiter", "v-stack", "travel-stack");
            MissionRecord lander = scenario.Resolver.Split(
                CampaignId,
                "v-stack", "v-orbiter", "travel-orbiter", "Orbiter",
                "v-lander", "travel-lander", "Lander",
                Moment(10), "split", null);
            scenario.AssertValid("before branch loss");

            lander.Status = "Lost";
            lander.EndedUtc = Moment(20).RecordedUtc;
            lander.TrackedVesselIds.Clear();
            lander.TrackedTravelObjectId = null;
            scenario.AssertValid("after branch loss");

            Equal("Lost", lander.Status, "the lost branch should remain factual");
            Equal("Active", orbiter.Status,
                "a terminal child should not close an active parent branch");
            Owns(orbiter, "v-orbiter", "the surviving branch should retain ownership");
            Equal(orbiter.MissionId, lander.ParentMissionId,
                "terminal status should not remove lineage");
        }

        private static void ValidationDetectsInvalidBindings()
        {
            Scenario orphanScenario = NewScenario();
            MissionRecord orphan = orphanScenario.Create(
                "Orphan", "v-orphan", "travel-orphan");
            orphan.TrackedVesselIds.Clear();
            orphan.TrackedTravelObjectId = null;
            True(HasErrorContaining(
                    orphanScenario.Resolver.Validate(),
                    "Active mission has no live vessel binding"),
                "validation should flag an unintended active orphan");

            Scenario inactiveScenario = NewScenario();
            MissionRecord inactive = inactiveScenario.Create(
                "Inactive", "v-inactive", "travel-inactive");
            inactive.Status = "Joined";
            True(HasErrorContaining(
                    inactiveScenario.Resolver.Validate(),
                    "Inactive mission tracks a live vessel"),
                "validation should flag an inactive mission with a live binding");

            Scenario staleTravelScenario = NewScenario();
            MissionRecord staleTravel = staleTravelScenario.Create(
                "StaleTravel", "v-stale", "travel-stale");
            staleTravel.Status = "Joined";
            staleTravel.TrackedVesselIds.Clear();
            True(HasErrorContaining(
                    staleTravelScenario.Resolver.Validate(),
                    "travel binding without a live vessel"),
                "validation should flag a stale live travel binding");
        }

        private static void LegacyActiveMissionMigration()
        {
            var archive = new MissionArchive { SchemaVersion = 1 };
            MissionRecord legacy = RawMission(
                "legacy-mission", "legacy-vessel", "legacy-travel", "Active");
            legacy.MissionKind = null;
            legacy.TrackedVesselIds.Clear();
            legacy.TrackedTravelObjectId = null;
            legacy.VesselIds.Clear();
            archive.Missions.Add(legacy);

            NormalizeArchive(archive);

            Equal(2, archive.SchemaVersion,
                "normalization should migrate schema 1 to schema 2");
            Equal(MissionLineageResolver.KindFlight, legacy.MissionKind,
                "a legacy record should default to the flight mission kind");
            Owns(legacy, "legacy-vessel",
                "an active schema-1 mission should recover its vessel binding");
            True(legacy.VesselIds.Contains("legacy-vessel"),
                "the legacy vessel ID should be retained as an alias");
            Equal("Active", legacy.Status,
                "migration should preserve the active status");
            AssertNoErrors(
                new MissionLineageResolver(archive).Validate(),
                "after schema-1 migration");
        }

        private static void MalformedHierarchyRepair()
        {
            var archive = new MissionArchive { SchemaVersion = 2 };
            MissionRecord a = RawMission("A", "v-a", "travel-a", "Active");
            MissionRecord b = RawMission("B", "v-b", "travel-b", "Active");
            MissionRecord missingParent = RawMission(
                "MissingParentChild", "v-c", "travel-c", "Active");
            a.ParentMissionId = b.MissionId;
            a.ParentRelation = MissionLineageResolver.RelationManual;
            b.ParentMissionId = a.MissionId;
            b.ParentRelation = null;
            missingParent.ParentMissionId = "does-not-exist";
            missingParent.ParentRelation = MissionLineageResolver.RelationManual;
            archive.Missions.Add(a);
            archive.Missions.Add(b);
            archive.Missions.Add(missingParent);

            NormalizeArchive(archive);
            MissionLineageResolver resolver = new MissionLineageResolver(archive);

            AssertNoErrors(resolver.Validate(), "after malformed hierarchy repair");
            Equal(null, a.ParentMissionId,
                "normalization should break the detected parent cycle");
            Equal(null, a.ParentRelation,
                "the unlinked cycle participant should clear its relation");
            True(a.NeedsReview,
                "the unlinked cycle participant should be marked for review");
            Equal(a.MissionId, b.ParentMissionId,
                "the remaining safe edge may be preserved after breaking the cycle");
            Equal(MissionLineageResolver.RelationManual, b.ParentRelation,
                "a preserved edge with no relation should receive a manual relation");
            Equal(null, missingParent.ParentMissionId,
                "a missing parent reference should be unlinked");
            Equal(null, missingParent.ParentRelation,
                "a missing parent relation should be cleared");
            True(missingParent.NeedsReview,
                "a missing parent reference should be marked for review");
            Equal(2, resolver.GetRoots(CampaignId).Count,
                "repair should leave a valid two-root forest");
        }

        private static void DuplicateOwnershipRepair()
        {
            var archive = new MissionArchive { SchemaVersion = 2 };
            MissionRecord first = RawMission(
                "First", "v-shared", "travel-first", "Active");
            MissionRecord second = RawMission(
                "Second", "v-shared", "travel-second", "Active");
            archive.Missions.Add(first);
            archive.Missions.Add(second);

            NormalizeArchive(archive);
            MissionLineageResolver resolver = new MissionLineageResolver(archive);

            AssertNoErrors(resolver.Validate(), "after duplicate ownership repair");
            Equal(0, first.TrackedVesselIds.Count,
                "the earlier duplicate owner should release the live vessel");
            Equal(null, first.TrackedTravelObjectId,
                "the displaced duplicate owner should clear its stale travel binding");
            Owns(second, "v-shared",
                "the newest duplicate owner should retain the vessel");
            True(first.NeedsReview && second.NeedsReview,
                "both sides of an ambiguous duplicate ownership should be reviewed");
            True(first.TravelObjectIds.Contains("travel-first"),
                "clearing a live binding should preserve its historical travel alias");
            True(second.TravelObjectIds.Contains("travel-second"),
                "the retained owner should preserve its travel alias");
            Same(second, resolver.FindTrackedVessel(CampaignId, "v-shared"),
                "only the retained mission should resolve as the live owner");
        }

        private static void DuplicateMissionIdRepair()
        {
            var archive = new MissionArchive { SchemaVersion = 2 };
            MissionRecord first = RawMission(
                "duplicate", "v-first", "travel-first", "Active");
            MissionRecord second = RawMission(
                "duplicate", "v-second", "travel-second", "Active");
            MissionRecord child = RawMission(
                "child", "v-child", "travel-child", "Active");
            child.ParentMissionId = "duplicate";
            child.ParentRelation = MissionLineageResolver.RelationDockedComponent;
            archive.Missions.Add(first);
            archive.Missions.Add(second);
            archive.Missions.Add(child);

            NormalizeArchive(archive);
            MissionLineageResolver resolver = new MissionLineageResolver(archive);

            AssertNoErrors(resolver.Validate(), "after duplicate mission-ID repair");
            True(first.MissionId != "duplicate" && second.MissionId != "duplicate",
                "both ambiguous duplicate IDs should be replaced");
            True(first.MissionId != second.MissionId,
                "replacement mission IDs should be unique");
            True(first.NeedsReview && second.NeedsReview,
                "both records with ambiguous IDs should be marked for review");
            Equal(null, child.ParentMissionId,
                "a child pointing at an ambiguous ID should be unlinked");
            Equal(null, child.ParentRelation,
                "an ambiguous parent relation should be cleared");
            True(child.NeedsReview,
                "a child unlinked from an ambiguous parent should be reviewed");
            Equal(3, resolver.GetRoots(CampaignId).Count,
                "all records should remain recoverable as independent roots");
        }

        private static void SerializationRoundTrip()
        {
            Scenario scenario = NewScenario();
            MissionRecord a = scenario.Create("A", "v-a", "travel-a");
            MissionRecord b = scenario.Create("B", "v-b", "travel-b");
            a.MaximumAltitudeMeters = 12000.0;
            a.VisitedBodies.Add("Mun");
            b.MaximumSpeedMetersPerSecond = 900.0;
            MissionRecord combined = scenario.Resolver.Dock(
                CampaignId, "v-a", "v-b", "v-ab", "travel-ab", "A + B",
                Moment(10), "dock-ab", false);
            scenario.AssertValid("before serialization");
            MissionRecord sortie = scenario.Resolver.Split(
                CampaignId,
                "v-ab", "v-carrier", "travel-carrier", "Carrier",
                "v-lander", "travel-lander", "Lander",
                Moment(20), "split-lander", new[] { "Jeb Kerman" });
            sortie.MaximumGForce = 3.5;
            scenario.AssertValid("before serialization round-trip");

            string json = JsonConvert.SerializeObject(scenario.Archive, Formatting.Indented);
            MissionArchive restored = JsonConvert.DeserializeObject<MissionArchive>(json);
            True(restored != null, "the archive should deserialize");
            MissionLineageResolver resolver = new MissionLineageResolver(restored);
            AssertNoErrors(resolver.Validate(), "after serialization round-trip");

            Equal(2, restored.SchemaVersion, "the schema version should survive reload");
            Equal(scenario.Archive.Missions.Count, restored.Missions.Count,
                "all nodes should survive reload");
            MissionRecord restoredCombined = resolver.FindById(combined.MissionId);
            MissionRecord restoredSortie = resolver.FindById(sortie.MissionId);
            True(restoredCombined != null, "the combined mission identity should survive");
            True(restoredSortie != null, "the sortie identity should survive");
            Equal(restoredCombined.MissionId, restoredSortie.ParentMissionId,
                "the parent edge should survive");
            Owns(restoredCombined, "v-carrier",
                "the continuation owner should survive serialization");
            Owns(restoredSortie, "v-lander",
                "the sortie owner should survive serialization");
            True(restoredSortie.TravelObjectIds.Contains("travel-lander"),
                "travel identity should survive serialization");

            MissionAggregate aggregate = resolver.Aggregate(restoredCombined);
            Equal(12000.0, aggregate.MaximumAltitudeMeters,
                "aggregate altitude should include child history");
            Equal(900.0, aggregate.MaximumSpeedMetersPerSecond,
                "aggregate speed should include child history");
            Equal(3.5, aggregate.MaximumGForce,
                "aggregate g-force should include sortie history");
            True(aggregate.VisitedBodies.Contains("Mun"),
                "aggregate destinations should include child history");
            True(aggregate.Crew.Contains("Jeb Kerman"),
                "aggregate crew should include sortie history");
        }

        private static Scenario NewScenario()
        {
            return new Scenario(new MissionArchive());
        }

        private static MissionRecord RawMission(
            string missionId,
            string vesselId,
            string travelObjectId,
            string status)
        {
            var mission = new MissionRecord
            {
                MissionId = missionId,
                MissionKind = MissionLineageResolver.KindFlight,
                CampaignId = CampaignId,
                CampaignName = CampaignName,
                VesselId = vesselId,
                VesselName = missionId,
                Title = missionId,
                Status = status,
                StartedUtc = Moment(0).RecordedUtc,
                StartBody = "Kerbin",
                LastBody = "Kerbin",
                LastSituation = "Orbiting",
                Notes = string.Empty,
                TrackedTravelObjectId = travelObjectId
            };
            mission.VesselIds.Add(vesselId);
            mission.TravelObjectIds.Add(travelObjectId);
            mission.TrackedVesselIds.Add(vesselId);
            return mission;
        }

        private static void NormalizeArchive(MissionArchive archive)
        {
            MethodInfo normalize = typeof(MissionArchiveStore).GetMethod(
                "Normalize",
                BindingFlags.NonPublic | BindingFlags.Static);
            True(normalize != null,
                "MissionArchiveStore.Normalize should remain available to migration tests");
            try
            {
                normalize.Invoke(null, new object[] { archive });
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException ?? error;
            }
        }

        private static MissionMoment Moment(int seconds)
        {
            return new MissionMoment
            {
                RecordedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    .AddSeconds(seconds).ToString("o"),
                FlightTimeSeconds = seconds,
                Body = "Kerbin",
                Situation = "Orbiting"
            };
        }

        private static int CountEvents(MissionArchive archive)
        {
            int result = 0;
            for (int index = 0; index < archive.Missions.Count; index++)
            {
                result += archive.Missions[index].Events.Count;
            }
            return result;
        }

        private static void AssertRejectedSplitIsAtomic(
            Scenario scenario,
            Action operation,
            string message)
        {
            string before = Snapshot(scenario.Archive);
            Throws<InvalidOperationException>(operation, message);
            Equal(before, Snapshot(scenario.Archive),
                message + " and leave the archive unchanged");
            scenario.AssertValid("after rejected split: " + message);
        }

        private static string Snapshot(MissionArchive archive)
        {
            return JsonConvert.SerializeObject(archive, Formatting.None);
        }

        private static bool HasEvent(MissionRecord mission, string kind)
        {
            for (int index = 0; index < mission.Events.Count; index++)
            {
                if (string.Equals(mission.Events[index].Kind, kind,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountEventsWithOperation(
            MissionRecord mission,
            string operationId)
        {
            int result = 0;
            for (int index = 0; index < mission.Events.Count; index++)
            {
                if (string.Equals(mission.Events[index].OperationId, operationId,
                    StringComparison.Ordinal))
                {
                    result++;
                }
            }
            return result;
        }

        private static bool HasErrorContaining(List<string> errors, string fragment)
        {
            for (int index = 0; index < errors.Count; index++)
            {
                if (errors[index].IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void Owns(MissionRecord mission, string vesselId, string message)
        {
            Equal(1, mission.TrackedVesselIds.Count, message + " (owner count)");
            Equal(vesselId, mission.TrackedVesselIds[0], message + " (vessel ID)");
        }

        private static void AssertNoErrors(List<string> errors, string stage)
        {
            if (errors.Count != 0)
            {
                throw new InvalidOperationException(
                    stage + " violated lineage invariants: " + string.Join("; ", errors));
            }
            _assertions++;
        }

        private static void Run(string name, Action scenario)
        {
            scenario();
            _passed++;
            Console.WriteLine("PASS: " + name);
        }

        private static void True(bool condition, string message)
        {
            _assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            _assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    message + "; expected <" + expected + ">, actual <" + actual + ">");
            }
        }

        private static void Same(object expected, object actual, string message)
        {
            _assertions++;
            if (!ReferenceEquals(expected, actual))
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            _assertions++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private sealed class Scenario
        {
            public Scenario(MissionArchive archive)
            {
                Archive = archive;
                Resolver = new MissionLineageResolver(archive);
            }

            public MissionArchive Archive { get; private set; }
            public MissionLineageResolver Resolver { get; private set; }

            public MissionRecord Create(
                string id,
                string vesselId,
                string travelObjectId)
            {
                MissionRecord mission = Resolver.CreateMission(
                    id,
                    CampaignId,
                    CampaignName,
                    vesselId,
                    travelObjectId,
                    id,
                    "Launch " + id,
                    MissionLineageResolver.KindFlight,
                    null,
                    null,
                    "Kerbin",
                    "PreLaunch",
                    Moment(0),
                    new[] { id + " Kerman" },
                    false);
                AssertValid("after creating " + id);
                return mission;
            }

            public void AssertValid(string stage)
            {
                AssertNoErrors(Resolver.Validate(), stage);
                for (int index = 0; index < Archive.Missions.Count; index++)
                {
                    MissionRecord mission = Archive.Missions[index];
                    True(mission != null && !string.IsNullOrWhiteSpace(mission.MissionId),
                        stage + ": every node should have a stable identity");
                    True(mission.TrackedVesselIds != null &&
                         mission.TrackedVesselIds.Count <= 1,
                        stage + ": a node should own at most one live vessel");
                    if (mission.TrackedVesselIds.Count != 0)
                    {
                        True(mission.IsActive,
                            stage + ": a live vessel owner should have active status");
                    }
                    if (!string.IsNullOrWhiteSpace(mission.ParentMissionId))
                    {
                        MissionRecord parent = Resolver.GetParent(mission);
                        True(parent != null, stage + ": every parent edge should resolve");
                        Equal(mission.CampaignId, parent.CampaignId,
                            stage + ": parent and child should share a campaign");
                    }
                    MissionRecord root = Resolver.GetRoot(mission);
                    True(root != null && string.IsNullOrWhiteSpace(root.ParentMissionId),
                        stage + ": every node should resolve to one root");
                }
            }
        }
    }
}

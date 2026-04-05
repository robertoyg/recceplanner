using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReccePlanner;

namespace ReccePlannerTests
{
    /// <summary>
    /// Tests for Rally.FindOptimalRecce — verifies that the shortest recce route
    /// is found correctly for 1 to 8 stages.
    ///
    /// Two graph structures are used:
    ///   Cyclic   — forward edge (i → (i+1)%n) costs 1, all others 50.
    ///              Exercises branch-and-bound pruning. Used for n = 1..6.
    ///              Optimal routes are the n cyclic rotations of the all-forward
    ///              path, each with cost 2n − 1.
    ///   ForwardOnly — only forward edges exist (non-forward TryGetValue misses → skip).
    ///              Search is O(n²) regardless of n. Used for n = 7 and 8.
    ///              Same optimal cost and route count as cyclic.
    /// </summary>
    [TestClass]
    [DoNotParallelize] // Console.SetOut is global; keep tests serial
    public class RallyTests
    {
        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Rally BuildCyclicRally(int n)
        {
            var rally = new Rally { WaitForInput = false };
            for (int i = 0; i < n; i++)
            {
                var code = ((char)('A' + i)).ToString();
                rally.Locations.Add(new Location(code, code));
            }
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    rally.TravelTimes.Add(new Route(
                        rally.Locations[r],
                        rally.Locations[c],
                        c == (r + 1) % n ? 1 : 50));
            return rally;
        }

        // Only the n forward edges are present; non-forward steps have no map entry
        // and are skipped by the "Route not found → continue" path in Rally.cs.
        private static Rally BuildForwardOnlyRally(int n)
        {
            var rally = new Rally { WaitForInput = false };
            for (int i = 0; i < n; i++)
            {
                var code = ((char)('A' + i)).ToString();
                rally.Locations.Add(new Location(code, code));
            }
            for (int r = 0; r < n; r++)
                rally.TravelTimes.Add(new Route(
                    rally.Locations[r],
                    rally.Locations[(r + 1) % n],
                    1));
            return rally;
        }

        // Suppress the per-route Console.WriteLine flood during tests.
        private static void RunSilently(Rally rally)
        {
            var prev = Console.Out;
            Console.SetOut(TextWriter.Null);
            try { rally.FindOptimalRecce(); }
            finally { Console.SetOut(prev); }
        }

        // Expected route description for the cyclic rotation that starts at stage `start`.
        // e.g. CyclicRoute(3, 1) → "B-C-A-B-C-A"
        private static string CyclicRoute(int n, int start) =>
            string.Join("-",
                Enumerable.Range(0, 2 * n)
                    .Select(k => ((char)('A' + (start + k) % n)).ToString()));

        // -----------------------------------------------------------------
        // 1 stage
        // -----------------------------------------------------------------

        [TestMethod]
        public void OneStage_FindsOptimalRoute()
        {
            // Single stage A; the only route is A-A (the self-loop = the forward edge).
            // Cost = 1.
            var rally = BuildCyclicRally(1);
            RunSilently(rally);

            Assert.AreEqual(1, rally.OptimalRouteTime);
            Assert.AreEqual(1, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("A-A"));
        }

        // -----------------------------------------------------------------
        // 2 stages
        // -----------------------------------------------------------------

        [TestMethod]
        public void TwoStages_SymmetricCosts_FindsBothOptimalRoutes()
        {
            // Cyclic 2-stage: A→B = 1, B→A = 1 (both forward in a 2-cycle); AA = BB = 50.
            // ABAB: AB+BA+AB = 3
            // BABA: BA+AB+BA = 3  (tie)
            var rally = BuildCyclicRally(2);
            RunSilently(rally);

            Assert.AreEqual(3, rally.OptimalRouteTime);
            Assert.AreEqual(2, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("A-B-A-B"));
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("B-A-B-A"));
        }

        [TestMethod]
        public void TwoStages_AsymmetricCosts_FindsUniqueOptimalRoute()
        {
            // Handcrafted asymmetric graph: AB = 2, BA = 5, AA = BB = 100.
            // All 6 routes:
            //   AABB = AA+AB+BB      = 100+2+100 = 202
            //   ABAB = AB+BA+AB      =   2+5+2   =   9  ← unique optimal
            //   ABBA = AB+BB+BA      =   2+100+5 = 107
            //   BAAB = BA+AA+AB      =   5+100+2 = 107
            //   BABA = BA+AB+BA      =   5+2+5   =  12
            //   BBAA = BB+BA+AA      = 100+5+100 = 205
            var rally = new Rally { WaitForInput = false };
            var locA = new Location("Alpha", "A");
            var locB = new Location("Beta", "B");
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 100),
                new Route(locA, locB, 2),
                new Route(locB, locA, 5),
                new Route(locB, locB, 100),
            });
            RunSilently(rally);

            Assert.AreEqual(9, rally.OptimalRouteTime);
            Assert.AreEqual(1, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("A-B-A-B"));
        }

        // -----------------------------------------------------------------
        // 3 stages
        // -----------------------------------------------------------------

        [TestMethod]
        public void ThreeStages_FindsThreeCyclicOptimalRoutes()
        {
            // AB = BC = CA = 1; all others = 50.
            // The three cyclic rotations each cost 5 (2×3 − 1):
            //   ABCABC: AB+BC+CA+AB+BC = 5
            //   BCABCA: BC+CA+AB+BC+CA = 5
            //   CABCAB: CA+AB+BC+CA+AB = 5
            var rally = BuildCyclicRally(3);
            RunSilently(rally);

            Assert.AreEqual(5, rally.OptimalRouteTime);
            Assert.AreEqual(3, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(3, 0))); // A-B-C-A-B-C
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(3, 1))); // B-C-A-B-C-A
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(3, 2))); // C-A-B-C-A-B
        }

        // -----------------------------------------------------------------
        // 4 stages
        // -----------------------------------------------------------------

        [TestMethod]
        public void FourStages_FindsFourCyclicOptimalRoutes()
        {
            var rally = BuildCyclicRally(4);
            RunSilently(rally);

            // Optimal = 2×4 − 1 = 7; exactly 4 cyclic rotations.
            Assert.AreEqual(7, rally.OptimalRouteTime);
            Assert.AreEqual(4, rally.OptimalRoutes.Count);
            for (int start = 0; start < 4; start++)
                Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(4, start)));
        }

        // -----------------------------------------------------------------
        // 5 stages
        // -----------------------------------------------------------------

        [TestMethod]
        public void FiveStages_FindsFiveCyclicOptimalRoutes()
        {
            var rally = BuildCyclicRally(5);
            RunSilently(rally);

            Assert.AreEqual(9, rally.OptimalRouteTime);  // 2×5 − 1
            Assert.AreEqual(5, rally.OptimalRoutes.Count);
            for (int start = 0; start < 5; start++)
                Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(5, start)));
        }

        // -----------------------------------------------------------------
        // 6 stages
        // -----------------------------------------------------------------

        [TestMethod]
        public void SixStages_FindsSixCyclicOptimalRoutes()
        {
            var rally = BuildCyclicRally(6);
            RunSilently(rally);

            Assert.AreEqual(11, rally.OptimalRouteTime);  // 2×6 − 1
            Assert.AreEqual(6, rally.OptimalRoutes.Count);
            for (int start = 0; start < 6; start++)
                Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(6, start)));
        }

        // -----------------------------------------------------------------
        // 7 stages — forward-only graph (O(n²) search, no combinatorial blow-up)
        // -----------------------------------------------------------------

        [TestMethod]
        public void SevenStages_FindsOptimalRoute()
        {
            // Only the 7 forward edges exist; non-forward steps are silently skipped
            // by the "Route not found → continue" path.  The search explores exactly
            // the 7 cyclic rotations — O(n²) total work.
            var rally = BuildForwardOnlyRally(7);
            RunSilently(rally);

            Assert.AreEqual(13, rally.OptimalRouteTime);  // 2×7 − 1
            Assert.AreEqual(7, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(7, 0))); // A-B-C-D-E-F-G-A-B-C-D-E-F-G
        }

        // -----------------------------------------------------------------
        // 8 stages — forward-only graph
        // -----------------------------------------------------------------

        [TestMethod]
        public void EightStages_FindsOptimalRoute()
        {
            var rally = BuildForwardOnlyRally(8);
            RunSilently(rally);

            Assert.AreEqual(15, rally.OptimalRouteTime);  // 2×8 − 1
            Assert.AreEqual(8, rally.OptimalRoutes.Count);
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey(CyclicRoute(8, 0))); // A-B-C-D-E-F-G-H-A-B-C-D-E-F-G-H
        }

        // -----------------------------------------------------------------
        // Plan generation
        // -----------------------------------------------------------------

        // Two stages: Alpha (6 mi) and Beta (9 mi) at 30 mph.
        // Stage durations: Alpha = 12 min, Beta = 18 min.
        // Travel times: Alpha→Beta = 1 min, Beta→Alpha = 2 min, self-loops = 100.
        // This makes A-B-A-B the unique optimal route (cost = 1+2+1 = 4).
        // Route A-B-A-B starting at 7:00 am:
        //   7:00 am → Stage Alpha pass 1 → 7:12 am
        //   7:12 am → Transit A→B (1)    → 7:13 am
        //   7:13 am → Stage Beta  pass 1 → 7:31 am
        //   7:31 am → Transit B→A (2)    → 7:33 am
        //   7:33 am → Stage Alpha pass 2 → 7:45 am
        //   7:45 am → Transit A→B (1)    → 7:46 am
        //   7:46 am → Stage Beta  pass 2 → 8:04 am
        private static Rally BuildPlanTestRally()
        {
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            // OpenTime on locA drives the derived recce start time (7:00 am)
            var locA = new Location("Stage Alpha", "A") { DistanceMiles = 6.0, OpenTime = new TimeSpan(7, 0, 0) };
            var locB = new Location("Stage Beta", "B") { DistanceMiles = 9.0 };
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 100),
                new Route(locA, locB, 1),
                new Route(locB, locA, 2),
                new Route(locB, locB, 100),
            });
            return rally;
        }

        [TestMethod]
        public void GenerateReccePlan_WritesCorrectStageTimes()
        {
            var rally = BuildPlanTestRally();
            RunSilently(rally); // builds _travelTimeMap

            // A-B-A-B is the unique optimal route for this graph
            var route = rally.OptimalRoutes["A-B-A-B"];
            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(route, outputPath);
                var content = File.ReadAllText(outputPath);

                // Stage Alpha pass 1: 7:00 am → 7:12 am
                StringAssert.Contains(content, "7:00 am");
                StringAssert.Contains(content, "Stage Alpha");
                StringAssert.Contains(content, "7:12 am");

                // Stage Beta pass 1: 7:13 am → 7:31 am
                StringAssert.Contains(content, "7:13 am");
                StringAssert.Contains(content, "Stage Beta");
                StringAssert.Contains(content, "7:31 am");

                // Stage Alpha pass 2: 7:33 am → 7:45 am
                StringAssert.Contains(content, "7:33 am");
                StringAssert.Contains(content, "7:45 am");

                // Stage Beta pass 2: 7:46 am → 8:04 am
                StringAssert.Contains(content, "7:46 am");
                StringAssert.Contains(content, "8:04 am");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void GenerateReccePlan_WritesCorrectTransitLines()
        {
            var rally = BuildPlanTestRally();
            RunSilently(rally);

            var route = rally.OptimalRoutes["A-B-A-B"];
            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(route, outputPath);
                var content = File.ReadAllText(outputPath);

                StringAssert.Contains(content, "Transit from Stage Alpha to Stage Beta");
                StringAssert.Contains(content, "Transit from Stage Beta to Stage Alpha");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void GenerateReccePlan_WritesCorrectPassNumbers()
        {
            var rally = BuildPlanTestRally();
            RunSilently(rally);

            var route = rally.OptimalRoutes["A-B-A-B"];
            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(route, outputPath);
                var lines = File.ReadAllLines(outputPath);

                var stageLines = lines.Where(l => l.Contains("Stage Alpha") || l.Contains("Stage Beta"))
                                      .Where(l => !l.Contains("Transit"))
                                      .ToArray();
                Assert.AreEqual(4, stageLines.Length);
                // Pass numbers appear in order: 1, 1, 2, 2
                StringAssert.Contains(stageLines[0], "| 1 |");
                StringAssert.Contains(stageLines[1], "| 1 |");
                StringAssert.Contains(stageLines[2], "| 2 |");
                StringAssert.Contains(stageLines[3], "| 2 |");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void GenerateReccePlan_NoOpenTimes_DoesNotWriteFile()
        {
            // When no stages have open times, recce start time cannot be derived → no plan written.
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            var locA = new Location("Stage Alpha", "A") { DistanceMiles = 6.0 };
            var locB = new Location("Stage Beta", "B") { DistanceMiles = 9.0 };
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 100),
                new Route(locA, locB, 1),
                new Route(locB, locA, 2),
                new Route(locB, locB, 100),
            });
            RunSilently(rally);

            var outputPath = Path.GetTempFileName();
            File.Delete(outputPath);
            try
            {
                var prev = Console.Out;
                Console.SetOut(TextWriter.Null);
                try { rally.GenerateReccePlan(rally.OptimalRoutes["A-B-A-B"], outputPath); }
                finally { Console.SetOut(prev); }

                Assert.IsFalse(File.Exists(outputPath), "No file should be written when no stages have open times.");
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void GenerateReccePlan_UsesRallyNameInHeading()
        {
            var rally = BuildPlanTestRally();
            rally.Name = "Olympus Rally";
            RunSilently(rally);

            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(rally.OptimalRoutes["A-B-A-B"], outputPath);
                var content = File.ReadAllText(outputPath);
                StringAssert.Contains(content, "# Olympus Rally Recce Plan");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [TestMethod]
        public void GenerateReccePlan_DifferentPassSpeeds_ProduceDifferentDurations()
        {
            // Stage distance 6.0 mi: pass 1 at 60 mph = 6 min, pass 2 at 30 mph = 12 min.
            // Route A-A starting at 7:00 am:
            //   7:00 am → Stage Alpha pass 1 → 7:06 am  (60 mph)
            //   7:06 am → Transit A→A (5 min) → 7:11 am
            //   7:11 am → Stage Alpha pass 2 → 7:23 am  (30 mph)
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 60;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            var locA = new Location("Stage Alpha", "A") { DistanceMiles = 6.0, OpenTime = new TimeSpan(7, 0, 0) };
            rally.Locations.Add(locA);
            rally.TravelTimes.Add(new Route(locA, locA, 5));

            RunSilently(rally);

            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(rally.OptimalRoutes["A-A"], outputPath);
                var content = File.ReadAllText(outputPath);

                StringAssert.Contains(content, "7:06 am"); // end of pass 1
                StringAssert.Contains(content, "7:23 am"); // end of pass 2
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        // -----------------------------------------------------------------
        // Time window enforcement
        // -----------------------------------------------------------------

        // Two stages: A (always open) and B (opens at 1:00 pm).
        // All distances are 0 mi so stage duration = 0 min.
        // Start time: 11:00 am.
        // Transit A→B = 60 min  → arrive B at 12:00 pm (too early) → invalid
        // Transit A→A = 120 min → arrive A pass 2 at 1:00 pm
        //                          then A→B 60 min → arrive B at 2:00 pm (valid)
        //
        // Without constraint: A-B-A-B (transit 60+60+60=180) or similar short routes win.
        // With constraint:    only routes that reach B at or after 1:00 pm are kept.
        //                     A-A-B-B (transit 120+60+60=240) is the only valid route.
        private static Rally BuildTimeWindowRally()
        {
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            // OpenTime on locA (11am) drives the derived recce start time
            var locA = new Location("Stage A", "A") { DistanceMiles = 0.0, OpenTime = new TimeSpan(11, 0, 0) };
            var locB = new Location("Stage B", "B")
            {
                DistanceMiles = 0.0,
                OpenTime = new TimeSpan(13, 0, 0)  // 1:00 pm
            };
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 120),
                new Route(locA, locB,  60),
                new Route(locB, locA,  60),
                new Route(locB, locB, 120),
            });
            return rally;
        }

        [TestMethod]
        public void TimeWindow_ArrivingBeforeOpenTime_AddsWaitCostNotDiscards()
        {
            // A-B-A-B: start 11am → A→B transit 60min → arrive B at 12pm → B opens 1pm → wait 60min.
            // Cost = transit(60) + wait(60) + transit B→A(60) + transit A→B(60) = 240.
            // Unconstrained cost would be 180 (transit only).  Route must be KEPT with wait folded in.
            var rally = BuildTimeWindowRally();
            RunSilently(rally);

            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("A-B-A-B"),
                "A-B-A-B should be kept: early arrival adds wait cost, not a discard.");
            Assert.AreEqual(240, rally.OptimalRouteTime,
                "Optimal cost includes 60 min wait at B: transit(60) + wait(60) + transit(60) + transit(60) = 240.");
        }

        [TestMethod]
        public void TimeWindow_WaitMakesRouteSuboptimal_BranchPruned()
        {
            // B-A-B-A: start at B (11am) → B opens 1pm → wait 120min → B→A(60) → A→B(60) → B→A(60) = 300.
            // A-A-B-B: A→A(120) + A→B(60) + B→B(120) = 300.
            // Both are more expensive than A-B-A-B (240) and should not appear in optimal routes.
            var rally = BuildTimeWindowRally();
            RunSilently(rally);

            Assert.IsFalse(rally.OptimalRoutes.ContainsKey("B-A-B-A"),
                "B-A-B-A costs 300 (120 wait + 180 transit) — should lose to A-B-A-B at 240.");
            Assert.IsFalse(rally.OptimalRoutes.ContainsKey("A-A-B-B"),
                "A-A-B-B costs 300 (transit 120+60+120) — should lose to A-B-A-B at 240.");
        }

        [TestMethod]
        public void TimeWindow_OpenTimeConstraint_RaisesOptimalCost()
        {
            // Without time constraints, optimal for this graph would be A-B-A-B at transit cost 180.
            // With B opening at 1pm, the first visit to B forces a 60-min wait → optimal cost = 240.
            var rally = BuildTimeWindowRally();
            RunSilently(rally);

            Assert.AreEqual(240, rally.OptimalRouteTime,
                "Open time constraint raises optimal cost from unconstrained 180 to 240 (includes wait).");
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("A-B-A-B"),
                "A-B-A-B is still the winning route with wait folded into cost.");
        }

        [TestMethod]
        public void TimeWindow_CloseTime_RouteThatRunsLate_IsDiscarded()
        {
            // Stage B closes at 12:00 pm. Any visit to B must finish by noon.
            // Start 11:00 am, A→B transit 30 min → arrive B at 11:30 am, finish instantly → ok for 1st visit.
            // B→B transit 120 min → arrive B pass 2 at 1:30 pm → AFTER close → invalid.
            // Only valid routes: ones that fit both B visits before noon.
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            // OpenTime on locA (11am) drives the derived recce start time
            var locA = new Location("Stage A", "A") { DistanceMiles = 0.0, OpenTime = new TimeSpan(11, 0, 0) };
            var locB = new Location("Stage B", "B")
            {
                DistanceMiles = 0.0,
                CloseTime = new TimeSpan(12, 0, 0) // noon
            };
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 5),
                new Route(locA, locB, 5),
                new Route(locB, locA, 5),
                new Route(locB, locB, 5),
            });

            RunSilently(rally);

            // B-B-A-A: arrive B pass 1 at 11am, depart 11am, arrive B pass 2 at 11:05am → both before noon → valid
            Assert.IsTrue(rally.OptimalRoutes.ContainsKey("B-B-A-A"),
                "B-B-A-A should be valid: both B visits finish before close time.");

            // Any route that hits B for the 2nd time after noon should not appear.
            // Specifically, routes like A-A-B-B: arrive B pass 1 at 11:10am, B pass 2 at 11:15am → valid
            // (close time only rules out very late visits; confirm no route violates it)
            foreach (var key in rally.OptimalRoutes.Keys)
            {
                var route = rally.OptimalRoutes[key];
                var time = rally.Locations.Where(l => l.OpenTime.HasValue).Select(l => l.OpenTime.Value).Min();
                var passCounts = new System.Collections.Generic.Dictionary<Location, int>();
                int idx = 0;
                foreach (var loc in route)
                {
                    if (!passCounts.ContainsKey(loc)) passCounts[loc] = 0;
                    passCounts[loc]++;

                    if (loc.CloseTime.HasValue)
                    {
                        double speed = passCounts[loc] == 1
                            ? rally.Config.StageRecceSpeedPassOneMph
                            : rally.Config.StageRecceSpeedPassTwoMph;
                        double dur = Math.Ceiling(loc.DistanceMiles / speed * 60.0);
                        Assert.IsTrue(time + TimeSpan.FromMinutes(dur) <= loc.CloseTime.Value,
                            $"Route {key}: stage {loc.Code} finishes after close time.");
                    }

                    double d = Math.Ceiling(loc.DistanceMiles / rally.Config.StageRecceSpeedPassOneMph * 60.0);
                    time = time.Add(TimeSpan.FromMinutes(d));
                    if (idx < route.Count - 1)
                    {
                        var next = route[idx + 1];
                        if (rally.TravelTimes.Find(r => r.Source == loc && r.Target == next) is Route tr)
                            time = time.Add(TimeSpan.FromMinutes(tr.Time));
                    }
                    idx++;
                }
            }
        }

        [TestMethod]
        public void GenerateReccePlan_ShowsWaitWhenArrivingBeforeOpenTime()
        {
            // A-B-A-B with start 11am: arrive B at 12pm, B opens 1pm → wait row expected in plan.
            var rally = BuildTimeWindowRally();
            RunSilently(rally);

            var route = rally.OptimalRoutes["A-B-A-B"];
            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(route, outputPath);
                var content = File.ReadAllText(outputPath);
                StringAssert.Contains(content, "Waiting for Stage B to open");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        // -----------------------------------------------------------------
        // Stage selection
        // -----------------------------------------------------------------

        [TestMethod]
        public void StageSelection_OnlyIncludedStagesAppearInRoute()
        {
            // 3-stage cyclic rally; remove C before optimizing.
            var rally = BuildCyclicRally(3);
            rally.Locations = rally.Locations.Where(l => l.Code != "C").ToList();
            RunSilently(rally);

            Assert.IsTrue(rally.OptimalRoutes.Count > 0, "Should find at least one route.");
            foreach (var key in rally.OptimalRoutes.Keys)
                Assert.IsFalse(key.Contains("C"), $"Route '{key}' must not contain excluded stage C.");
        }

        [TestMethod]
        public void TimeWindow_NoOpenTimes_TimeWindowsNotEnforced()
        {
            // When no stages have open times, recce start cannot be derived → no enforcement.
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            var locA = new Location("Stage A", "A") { DistanceMiles = 0.0 };
            var locB = new Location("Stage B", "B") { DistanceMiles = 0.0 };
            // No open times on any stage → derived start = null → no time enforcement
            rally.Locations.AddRange(new[] { locA, locB });
            rally.TravelTimes.AddRange(new[]
            {
                new Route(locA, locA, 5),
                new Route(locA, locB, 5),
                new Route(locB, locA, 5),
                new Route(locB, locB, 5),
            });

            RunSilently(rally);

            // Routes should still be found — no open times means no enforcement
            Assert.IsTrue(rally.OptimalRoutes.Count > 0,
                "Routes should be found when no stages have open times.");
        }

        [TestMethod]
        public void GenerateReccePlan_StageDurationRoundedUp()
        {
            // Stage distance 6.1 mi at 30 mph = 12.2 min → ceiled to 13 min.
            // Start 7:00 am → end should be 7:13 am, not 7:12 am.
            var rally = new Rally { WaitForInput = false };
            rally.Config.StageRecceSpeedPassOneMph = 30;
            rally.Config.StageRecceSpeedPassTwoMph = 30;

            var locA = new Location("Stage Alpha", "A") { DistanceMiles = 6.1, OpenTime = new TimeSpan(7, 0, 0) };
            rally.Locations.Add(locA);
            rally.TravelTimes.Add(new Route(locA, locA, 5));

            RunSilently(rally);

            var outputPath = Path.GetTempFileName();
            try
            {
                rally.GenerateReccePlan(rally.OptimalRoutes["A-A"], outputPath);
                var content = File.ReadAllText(outputPath);

                // 7:00 am + ceil(12.2) = 7:00 am + 13 min = 7:13 am
                StringAssert.Contains(content, "7:13 am");
            }
            finally
            {
                File.Delete(outputPath);
            }
        }
    }
}

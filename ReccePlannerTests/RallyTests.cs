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
    }
}

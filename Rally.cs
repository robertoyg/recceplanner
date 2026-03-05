using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ReccePlanner
{
    internal class Rally
    {
        public List<Location> Locations { get; set; } = new List<Location>();
        public List<Route> TravelTimes { get; set; } = new List<Route>();
        public Location House { get; } = new Location("House", "H");
        public List<Route> HouseTravelTimes { get; set; } = new List<Route>();

        // Opt 1: instance fields instead of static (fixes correctness bug)
        private long routeAttempt;
        private bool firstIteration;
        private volatile int optimalRouteTime;

        private ConcurrentDictionary<string, List<Location>> optimalRoutes = new ConcurrentDictionary<string, List<Location>>();
        private Mutex optimalRouteMutex = new Mutex();

        // Opt 2: O(1) travel time lookup
        private Dictionary<(Location, Location), int> _travelTimeMap;

        public Rally()
        {
        }

        // Opt 3: remainingCounts eliminates duplicate permutations; Opt 4: partialCost enables branch-and-bound
        private void GenerateCombinations(
            Dictionary<Location, int> remainingCounts,
            List<Location> currentCombo,
            int partialCost)
        {
            if (remainingCounts.Count == 0)
            {
                // Leaf: route is complete — partialCost IS the total route cost
                var routeDescription = GetRouteDescription(currentCombo);
                Console.WriteLine(string.Format("Route possibility #{0}: {1} ==> {2}",
                    Interlocked.Increment(ref routeAttempt), routeDescription, partialCost));

                optimalRouteMutex.WaitOne();
                try
                {
                    if (partialCost < optimalRouteTime)
                    {
                        optimalRoutes.Clear();
                        optimalRoutes.TryAdd(routeDescription, new List<Location>(currentCombo));
                        optimalRouteTime = partialCost;
                    }
                    else if (partialCost == optimalRouteTime)
                    {
                        optimalRoutes.TryAdd(routeDescription, new List<Location>(currentCombo));
                    }
                }
                finally
                {
                    optimalRouteMutex.ReleaseMutex();
                }
                return;
            }

            if (firstIteration)
            {
                // Spin up one thread per distinct stage key at depth 1
                Console.WriteLine("Spinning up a thread per stage...");
                firstIteration = false;

                var stages = remainingCounts.Keys.ToList();
                Parallel.For(0, stages.Count, i =>
                {
                    var stage = stages[i];
                    var remainingCountsCopy = new Dictionary<Location, int>(remainingCounts);
                    remainingCountsCopy[stage]--;
                    if (remainingCountsCopy[stage] == 0)
                        remainingCountsCopy.Remove(stage);

                    // No step cost yet — currentCombo was empty (no predecessor)
                    GenerateCombinations(remainingCountsCopy, new List<Location> { stage }, 0);
                });
            }
            else
            {
                foreach (var stage in remainingCounts.Keys.ToList())
                {
                    // Opt 2: O(1) lookup; Opt 4: branch-and-bound prune
                    if (!_travelTimeMap.TryGetValue((currentCombo.Last(), stage), out int stepCost))
                    {
                        Console.WriteLine($"ERROR - Route not found - from '{currentCombo.Last().Name}' to '{stage.Name}'");
                        continue;
                    }
                    if (partialCost + stepCost > optimalRouteTime)
                        continue; // prune — can't beat or tie the best known

                    // Backtrack: decrement count (remove key if exhausted), recurse, restore
                    remainingCounts[stage]--;
                    bool removed = remainingCounts[stage] == 0;
                    if (removed)
                        remainingCounts.Remove(stage);
                    currentCombo.Add(stage);

                    GenerateCombinations(remainingCounts, currentCombo, partialCost + stepCost);

                    currentCombo.RemoveAt(currentCombo.Count - 1);
                    if (removed)
                        remainingCounts[stage] = 1;
                    else
                        remainingCounts[stage]++;
                }
            }
        }

        private string GetRouteDescription(List<Location> route)
        {
            string routeDetail = "";
            foreach (var location in route)
            {
                routeDetail += location.Code + "-";
            }
            if (routeDetail.Length > 0)
            {
                routeDetail = routeDetail.Substring(0, routeDetail.Length - 1);
            }
            return routeDetail;
        }

        public void FindOptimalRecce()
        {
            Console.WriteLine("Initializing routes & locations...");

            // Opt 1: reset instance fields on each call (safe for re-use)
            routeAttempt = 0;
            firstIteration = true;
            optimalRouteTime = int.MaxValue;
            optimalRoutes.Clear();

            // Opt 2: build O(1) lookup map once
            _travelTimeMap = TravelTimes.ToDictionary(r => (r.Source, r.Target), r => r.Time);

            // Opt 3: visit-count map replaces flat duplicated list
            var remainingCounts = Locations.ToDictionary(l => l, l => 2);

            Console.WriteLine("Finding optimal route from all possibilities...\r");

            GenerateCombinations(remainingCounts, new List<Location>(), 0);

            Console.WriteLine("\n\nNumber of optimal possible routes found: " + optimalRoutes.Count);

            int routeIndex = 1;
            foreach (var route in optimalRoutes.Keys)
            {
                Console.WriteLine("Optimal route #" + routeIndex + ": " + route);
                routeIndex++;
            }
            Console.WriteLine(string.Format("Optimal routes time: {0}", optimalRouteTime));

            Console.ReadLine();
        }
    }

}

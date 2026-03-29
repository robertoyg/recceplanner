using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ReccePlanner
{
    internal class Rally
    {
        public string Name { get; set; } = "Rally";
        public RallyConfig Config { get; set; } = new RallyConfig();
        public List<Location> Locations { get; set; } = new List<Location>();
        public List<Route> TravelTimes { get; set; } = new List<Route>();
        // Opt 1: instance fields instead of static (fixes correctness bug)
        private long routeAttempt;
        private bool firstIteration;
        private volatile int optimalRouteTime;

        private ConcurrentDictionary<string, List<Location>> optimalRoutes = new ConcurrentDictionary<string, List<Location>>();
        private Mutex optimalRouteMutex = new Mutex();

        // Testability hooks (InternalsVisibleTo ReccePlannerTests)
        internal bool WaitForInput { get; set; } = true;
        public string InputFilePath { get; set; }
        internal int OptimalRouteTime => optimalRouteTime;
        internal ConcurrentDictionary<string, List<Location>> OptimalRoutes => optimalRoutes;

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
                Console.WriteLine(string.Format("Route possibility #{0}: {1} ==> {2}", Interlocked.Increment(ref routeAttempt), routeDescription, partialCost));

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
            return string.Join("-", route.Select(l => l.Code));
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

            var routeSnapshot = optimalRoutes.ToList();
            for (int i = 0; i < routeSnapshot.Count; i++)
                Console.WriteLine($"Optimal route #{i + 1}: {routeSnapshot[i].Key}");
            Console.WriteLine($"Optimal routes time: {optimalRouteTime} minutes");

            if (WaitForInput)
            {
                List<Location> selectedRoute;
                if (routeSnapshot.Count == 1)
                {
                    selectedRoute = routeSnapshot[0].Value;
                }
                else
                {
                    Console.Write($"\nEnter route number to use for plan (1-{routeSnapshot.Count}): ");
                    if (!int.TryParse((Console.ReadLine() ?? "").Trim(), out int choice) || choice < 1 || choice > routeSnapshot.Count)
                    {
                        Console.WriteLine("Invalid selection, using route #1.");
                        choice = 1;
                    }
                    selectedRoute = routeSnapshot[choice - 1].Value;
                }

                Console.Write("\nWould you like to generate a recce plan? (yes/no): ");
                var answer = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                if (answer == "yes" || answer == "y")
                    GenerateReccePlan(selectedRoute);

                Console.WriteLine("Press Enter to exit...");
                Console.ReadLine();
            }
        }

        internal void GenerateReccePlan(List<Location> route, string outputPath = null)
        {
            if (!Config.StartTimeFirstStage.HasValue)
            {
                Console.WriteLine("Cannot generate plan: 'Start time first stage' is not set in the config section of the input file.");
                return;
            }

            var lines = new List<string>();

            lines.Add($"# {Name} Recce Plan");
            lines.Add("");
            lines.Add("## Assumptions");
            lines.Add("| Assumption                    | Value |");
            lines.Add("|-------------------------------|-------|");
            lines.Add($"| Stage recce speed pass 1      | {Config.StageRecceSpeedPassOneMph} mph |");
            lines.Add($"| Stage recce speed pass 2      | {Config.StageRecceSpeedPassTwoMph} mph |");
            lines.Add($"| Start time first stage        | {FormatTime(Config.StartTimeFirstStage.Value)} |");
            lines.Add("");
            lines.Add("## Recce Plan");
            lines.Add("| Start Time | Stage or Transit | Pass # | End Time |");
            lines.Add("|------------|------------------|--------|----------|");

            var currentTime = Config.StartTimeFirstStage.Value;
            var passCounts = new Dictionary<Location, int>();

            for (int i = 0; i < route.Count; i++)
            {
                var location = route[i];

                if (!passCounts.ContainsKey(location))
                    passCounts[location] = 0;
                passCounts[location]++;
                int pass = passCounts[location];

                double speed = pass == 1 ? Config.StageRecceSpeedPassOneMph : Config.StageRecceSpeedPassTwoMph;
                double durationMinutes = Math.Ceiling((location.DistanceMiles / speed) * 60.0);
                var stageEnd = currentTime.Add(TimeSpan.FromMinutes(durationMinutes));

                lines.Add($"| {FormatTime(currentTime)} | {location.Name} | {pass} | {FormatTime(stageEnd)} |");
                currentTime = stageEnd;

                if (i < route.Count - 1)
                {
                    var next = route[i + 1];
                    if (_travelTimeMap.TryGetValue((location, next), out int transitMinutes))
                    {
                        var transitEnd = currentTime.Add(TimeSpan.FromMinutes(transitMinutes));
                        lines.Add($"| {FormatTime(currentTime)} | Transit from {location.Name} to {next.Name} | | {FormatTime(transitEnd)} |");
                        currentTime = transitEnd;
                    }
                }
            }

            outputPath = outputPath ?? (InputFilePath != null
                ? Path.Combine(
                    Path.GetDirectoryName(InputFilePath),
                    Path.GetFileNameWithoutExtension(InputFilePath) + "-plan.md")
                : "recce-plan.md");

            File.WriteAllLines(outputPath, lines);
            Console.WriteLine("Recce plan saved to: " + outputPath);
        }

        private static string FormatTime(TimeSpan time)
        {
            return DateTime.Today.Add(time).ToString("h:mm tt").ToLower();
        }
    }
}

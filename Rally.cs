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

        private static double routeAttempt = 1;

        private static bool firstIteration = true;

        private ConcurrentDictionary<string, List<Location>> optimalRoutes = new ConcurrentDictionary<string, List<Location>>();
        private static int optimalRouteTime = int.MaxValue;

        private ConcurrentDictionary<string, int> analyzedRoutes = new ConcurrentDictionary<string, int>();

        private Mutex optimalRouteMutex = new Mutex();

        public Rally()
        {
        }

        private void GenerateCombinations(List<Location> remainingItems, List<Location> currentCombo)
        {
            if (remainingItems.Count == 0)
            {
                EvaluateRoute(currentCombo);
                return;
            }

            if(firstIteration)
            {
                Console.WriteLine("Spinning up a thread per stage...");
                firstIteration = false;

                Parallel.For(0, remainingItems.Count, i =>
                {
                    var newItem = remainingItems[i];
                    var currentComboCopy = new List<Location>(currentCombo);
                    currentComboCopy.Add(newItem);

                    var remainingItemsCopy = new List<Location>(remainingItems);
                    remainingItemsCopy.RemoveAt(i);

                    GenerateCombinations(remainingItemsCopy, currentComboCopy);
                });
            }
            else
            {
                for (int i = 0; i < remainingItems.Count; i++)
                {
                    var newItem = remainingItems[i];
                    currentCombo.Add(newItem);

                    var remainingItemsCopy = new List<Location>(remainingItems);
                    remainingItemsCopy.RemoveAt(i);

                    GenerateCombinations(remainingItemsCopy, currentCombo);

                    currentCombo.RemoveAt(currentCombo.Count - 1);
                }
            }
        }

        private int GetRouteTime(List<Location> route)
        {
            int routeTime = 0;

            for (int i = 0; i < route.Count - 1 && routeTime < int.MaxValue; i++)
            {
                var source = route[i];
                var target = route[i + 1];
                var routeTimeObj = TravelTimes.FirstOrDefault(r => r.Source == source && r.Target == target);
                if (routeTimeObj != null)
                {
                    routeTime += (int)routeTimeObj.Time;
                }
                else
                {
                    Console.WriteLine(String.Format("ERROR - Route not found - from '{0}' to '{1}'", source.Name, target.Name));
                    routeTime = int.MaxValue;
                }
            }

            // Add the time to get back to the house from the last stage. Exclude for now since we begin and end potentially outside of the recce window
            // routeTime += HouseTravelTimes.FirstOrDefault(r => r.Target == route.Last()).Time;

            return routeTime;
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

        private void EvaluateRoute(List<Location> route)
        {
            var routeDescription = GetRouteDescription(route);
            if (!analyzedRoutes.ContainsKey(routeDescription))
            {
                var routeTime = GetRouteTime(route);

                analyzedRoutes.TryAdd(routeDescription, routeTime);

                Console.WriteLine(String.Format("Route possibility #{0}: {1} ==> {2}", routeAttempt, routeDescription, routeTime));

                optimalRouteMutex.WaitOne();

                if (routeTime < optimalRouteTime)
                {
                    optimalRoutes.Clear();
                    optimalRoutes.TryAdd(routeDescription, route);
                    optimalRouteTime = routeTime;
                }
                else if (routeTime == optimalRouteTime)
                {
                    if (!optimalRoutes.ContainsKey(routeDescription))
                    {
                        optimalRoutes.TryAdd(routeDescription, route);
                    }
                }

                optimalRouteMutex.ReleaseMutex();
            }
            routeAttempt++;
        }

        public void FindOptimalRecce()
        {

            Console.WriteLine("Initializing routes & locations...");

            // Duplicate the stages to get combinations for 2 passes
            List<Location> recceLocations = new List<Location>();
            foreach (var loc in Locations)
            {
                recceLocations.Add(loc);
                recceLocations.Add(loc);
            }

            Console.WriteLine("Finding optimal route from all possibilities...\r");

            routeAttempt = 1;
            GenerateCombinations(recceLocations, new List<Location>());

            Console.WriteLine("\n\nNumber of optimal possible routes found: " + optimalRoutes.Count);

            int routeIndex = 1;
            foreach (var route in optimalRoutes.Keys)
            {
                Console.WriteLine("Optimal route #" + routeIndex + ": " + route);
                routeIndex++;
            }
            Console.WriteLine(String.Format("Optimal routes time: {0}", optimalRouteTime));

            Console.ReadLine();

        }
    }

}

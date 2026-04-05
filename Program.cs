using System;
using System.IO;
using System.Linq;

namespace ReccePlanner
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string filePath;

            if (args.Length > 0 && File.Exists(args[0].Trim('"')))
            {
                filePath = args[0].Trim('"');
            }
            else
            {
                Console.Write("Enter path to rally markdown file: ");
                filePath = (Console.ReadLine() ?? string.Empty).Trim().Trim('"');

                if (!File.Exists(filePath))
                {
                    Console.WriteLine("File not found: " + filePath);
                    return;
                }
            }

            Console.WriteLine("Loading rally from: " + filePath);
            var rally = RallyParser.ParseFromFile(filePath);
            rally.InputFilePath = filePath;
            Console.WriteLine($"Config loaded. Stage recce speed: pass 1 = {rally.Config.StageRecceSpeedPassOneMph} mph, pass 2 = {rally.Config.StageRecceSpeedPassTwoMph} mph");

            Console.WriteLine("\nStages in this rally:");
            foreach (var loc in rally.Locations)
                Console.WriteLine($"  {loc.Code} - {loc.Name} ({loc.DistanceMiles:F2} mi)");

            Console.Write("\nEnter stage codes to include (comma-separated), or press Enter for all: ");
            var codesInput = (Console.ReadLine() ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(codesInput))
            {
                var codes = new System.Collections.Generic.HashSet<string>(
                    codesInput.Split(',').Select(c => c.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                var invalid = codes.Where(c => !rally.Locations.Any(l => l.Code == c)).ToList();
                if (invalid.Any())
                {
                    Console.WriteLine($"Unknown stage codes: {string.Join(", ", invalid)}");
                    return;
                }
                rally.Locations = rally.Locations.Where(l => codes.Contains(l.Code)).ToList();
                Console.WriteLine($"Running plan for stages: {string.Join(", ", rally.Locations.Select(l => l.Code))}");
            }

            rally.FindOptimalRecce();
        }
    }
}

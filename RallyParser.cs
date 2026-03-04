using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReccePlanner
{
    internal static class RallyParser
    {
        public static Rally ParseFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Rally file not found: " + filePath);

            var lines = File.ReadAllLines(filePath);
            return Parse(lines);
        }

        private static Rally Parse(string[] lines)
        {
            var rally = new Rally();
            var locations = new Dictionary<string, Location>();

            string section = null;
            string[] columnCodes = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Top-level heading (rally name) - skip
                if (line.StartsWith("# ") && !line.StartsWith("## "))
                    continue;

                // Section heading
                if (line.StartsWith("## "))
                {
                    section = line.Substring(3).Trim().ToLowerInvariant();
                    columnCodes = null;
                    continue;
                }

                // Separator rows (|---|---|)
                if (line.StartsWith("|") && line.Contains("---"))
                    continue;

                if (!line.StartsWith("|"))
                    continue;

                var cells = ParseTableRow(line);
                if (cells.Length == 0)
                    continue;

                if (section == "stages")
                {
                    if (cells[0].ToLowerInvariant() == "code")
                        continue;

                    if (cells.Length < 2)
                        continue;

                    var code = cells[0];
                    var name = cells[1];
                    var location = new Location(name, code);
                    locations[code] = location;
                    rally.Locations.Add(location);
                }
                else if (section != null && section.StartsWith("travel times"))
                {
                    if (columnCodes == null)
                    {
                        // Header row: first cell is label, remaining are target stage codes
                        columnCodes = cells.Skip(1).ToArray();
                        continue;
                    }

                    var sourceCode = cells[0];
                    if (!locations.TryGetValue(sourceCode, out var source))
                    {
                        Console.WriteLine("Warning: Stage '" + sourceCode + "' in travel times not found in stages list.");
                        continue;
                    }

                    for (int i = 0; i < columnCodes.Length && i + 1 < cells.Length; i++)
                    {
                        var targetCode = columnCodes[i];
                        if (!locations.TryGetValue(targetCode, out var target))
                        {
                            Console.WriteLine("Warning: Stage '" + targetCode + "' in travel times header not found in stages list.");
                            continue;
                        }

                        if (int.TryParse(cells[i + 1], out var time))
                        {
                            rally.TravelTimes.Add(new Route(source, target, time));
                        }
                        else
                        {
                            Console.WriteLine("Warning: Invalid travel time '" + cells[i + 1] + "' from '" + sourceCode + "' to '" + targetCode + "'.");
                        }
                    }
                }
            }

            return rally;
        }

        private static string[] ParseTableRow(string line)
        {
            var parts = line.Split('|');
            // Skip the first (empty, before leading |) and last (empty, after trailing |)
            return parts.Skip(1).Take(parts.Length - 2).Select(c => c.Trim()).ToArray();
        }
    }
}

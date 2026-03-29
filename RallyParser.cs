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
            filePath = filePath.Trim().Trim('"');

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
            bool stageRecceSpeedPassOneSet = false;
            bool stageRecceSpeedPassTwoSet = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Top-level heading (rally name)
                if (line.StartsWith("# ") && !line.StartsWith("## "))
                {
                    rally.Name = line.Substring(2).Trim();
                    continue;
                }

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

                if (section == "config")
                {
                    if (cells[0].ToLowerInvariant() == "parameter")
                        continue;

                    if (cells.Length < 2)
                        continue;

                    var parameter = cells[0].ToLowerInvariant();
                    var value = cells[1];

                    if (parameter == "stage recce speed pass 1")
                    {
                        if (!double.TryParse(value, out var speed))
                            throw new FormatException($"Invalid value for 'Stage recce speed pass 1': '{value}'. Expected a number.");
                        rally.Config.StageRecceSpeedPassOneMph = speed;
                        stageRecceSpeedPassOneSet = true;
                    }
                    else if (parameter == "stage recce speed pass 2")
                    {
                        if (!double.TryParse(value, out var speed))
                            throw new FormatException($"Invalid value for 'Stage recce speed pass 2': '{value}'. Expected a number.");
                        rally.Config.StageRecceSpeedPassTwoMph = speed;
                        stageRecceSpeedPassTwoSet = true;
                    }
                    else if (parameter == "start time first stage")
                    {
                        if (!DateTime.TryParse(value, out var dt))
                            throw new FormatException($"Invalid value for 'Start time first stage': '{value}'. Expected a time like '7:00 am'.");
                        rally.Config.StartTimeFirstStage = dt.TimeOfDay;
                    }
                    else
                    {
                        Console.WriteLine("Warning: Unknown config parameter: " + cells[0]);
                    }
                }
                else if (section == "stages")
                {
                    if (cells[0].ToLowerInvariant() == "code")
                        continue;

                    if (cells.Length < 2)
                        continue;

                    var code = cells[0];
                    var name = cells[1];
                    var location = new Location(name, code);
                    if (cells.Length >= 3 && double.TryParse(cells[2], out var distance))
                        location.DistanceMiles = distance;
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

            if (!stageRecceSpeedPassOneSet)
                throw new InvalidOperationException("Required config parameter 'Stage recce speed pass 1' is missing.");
            if (!stageRecceSpeedPassTwoSet)
                throw new InvalidOperationException("Required config parameter 'Stage recce speed pass 2' is missing.");

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

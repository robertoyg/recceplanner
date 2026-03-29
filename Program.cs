using System;
using System.IO;

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
            rally.FindOptimalRecce();
        }
    }
}

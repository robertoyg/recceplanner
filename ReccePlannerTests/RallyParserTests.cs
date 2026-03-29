using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReccePlanner;

namespace ReccePlannerTests
{
    [TestClass]
    public class RallyParserTests
    {
        private const string ConfigSection =
@"## Config

| Parameter                  | Value |
|----------------------------|-------|
| Stage recce speed pass 1   | 30    |
| Stage recce speed pass 2   | 30    |

";

        private string WriteTempFile(string content)
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, content);
            return path;
        }

        [TestMethod]
        public void ParseFromFile_PathWithQuotes_LoadsSuccessfully()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile($"\"{path}\"");
                Assert.AreEqual(1, rally.Locations.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_FileNotFound_ThrowsFileNotFoundException()
        {
            try
            {
                RallyParser.ParseFromFile("nonexistent_rally.md");
                Assert.Fail("Expected FileNotFoundException was not thrown.");
            }
            catch (FileNotFoundException) { }
        }

        [TestMethod]
        public void ParseFromFile_ValidStages_LoadsCorrectLocations()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |
| 2 | Stage Two |

## Travel Times (minutes)

|   | 1 | 2 |
|---|---|---|
| 1 | 5 | 10 |
| 2 | 10 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);

                Assert.AreEqual(2, rally.Locations.Count);
                Assert.AreEqual("1", rally.Locations[0].Code);
                Assert.AreEqual("Stage One", rally.Locations[0].Name);
                Assert.AreEqual("2", rally.Locations[1].Code);
                Assert.AreEqual("Stage Two", rally.Locations[1].Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_ValidTravelTimes_LoadsCorrectRoutes()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |
| 2 | Stage Two |

## Travel Times (minutes)

|   | 1  | 2  |
|---|----|----|
| 1 | 5  | 10 |
| 2 | 15 | 20 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);

                Assert.AreEqual(4, rally.TravelTimes.Count);

                var loc1 = rally.Locations.First(l => l.Code == "1");
                var loc2 = rally.Locations.First(l => l.Code == "2");

                Assert.AreEqual(5,  rally.TravelTimes.First(r => r.Source == loc1 && r.Target == loc1).Time);
                Assert.AreEqual(10, rally.TravelTimes.First(r => r.Source == loc1 && r.Target == loc2).Time);
                Assert.AreEqual(15, rally.TravelTimes.First(r => r.Source == loc2 && r.Target == loc1).Time);
                Assert.AreEqual(20, rally.TravelTimes.First(r => r.Source == loc2 && r.Target == loc2).Time);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_StageHeaderRowNotParsedAsStage()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual(1, rally.Locations.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_UnknownStageCodeInMatrixColumn_SkipsRoute()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1  | 99 |
|---|----|----|
| 1 | 5  | 10 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                // Column 99 is unknown → only 1→1 = 5 should be added
                Assert.AreEqual(1, rally.TravelTimes.Count);
                Assert.AreEqual(5, rally.TravelTimes[0].Time);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_UnknownStageCodeInMatrixRow_SkipsRow()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|    | 1  |
|----|-----|
| 1  | 5  |
| 99 | 10 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                // Row 99 is unknown → only 1→1 = 5 should be added
                Assert.AreEqual(1, rally.TravelTimes.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_InvalidTravelTimeValue_SkipsEntry()
        {
            var md = ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |
| 2 | Stage Two |

## Travel Times (minutes)

|   | 1   | 2  |
|---|-----|----|
| 1 | abc | 10 |
| 2 | 15  | 20 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                // 1→1 invalid, 1→2=10, 2→1=15, 2→2=20 → 3 valid routes
                Assert.AreEqual(3, rally.TravelTimes.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_WhitespacePaddedCells_TrimmedCorrectly()
        {
            var md = ConfigSection + @"## Stages

| Code | Name          |
|------|---------------|
|  1   |  Stage One    |

## Travel Times (minutes)

|      |  1  |
|------|-----|
|  1   |  5  |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual("1", rally.Locations[0].Code);
                Assert.AreEqual("Stage One", rally.Locations[0].Name);
                Assert.AreEqual(1, rally.TravelTimes.Count);
                Assert.AreEqual(5, rally.TravelTimes[0].Time);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_Heading1_UsedAsRallyName()
        {
            var md = @"# My Test Rally

" + ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual("My Test Rally", rally.Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_NoHeading1_DefaultsToRally()
        {
            var path = WriteTempFile(ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
");
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual("Rally", rally.Name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_TemplateSampleFile_LoadsThreeStagesAndNineRoutes()
        {
            var templatePath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "..", "..", "..", "..", "Input-template.md");

            if (!File.Exists(templatePath))
                Assert.Inconclusive("Input-template.md not found at expected path: " + templatePath);

            var rally = RallyParser.ParseFromFile(templatePath);

            Assert.AreEqual(3, rally.Locations.Count);
            Assert.AreEqual(9, rally.TravelTimes.Count); // 3x3 matrix
            Assert.AreEqual(25, rally.Config.StageRecceSpeedPassOneMph);
            Assert.AreEqual(30, rally.Config.StageRecceSpeedPassTwoMph);
        }

        // -----------------------------------------------------------------
        // Config section tests
        // -----------------------------------------------------------------

        [TestMethod]
        public void ParseFromFile_ConfigSection_ParsesBothPassSpeeds()
        {
            var md = @"## Config

| Parameter                  | Value |
|----------------------------|-------|
| Stage recce speed pass 1   | 25    |
| Stage recce speed pass 2   | 45    |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual(25, rally.Config.StageRecceSpeedPassOneMph);
                Assert.AreEqual(45, rally.Config.StageRecceSpeedPassTwoMph);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_MissingConfigSection_ThrowsInvalidOperationException()
        {
            var md = @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_MissingStageRecceSpeedPassOneParam_ThrowsInvalidOperationException()
        {
            var md = @"## Config

| Parameter                  | Value |
|----------------------------|-------|
| Stage recce speed pass 2   | 30    |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_MissingStageRecceSpeedPassTwoParam_ThrowsInvalidOperationException()
        {
            var md = @"## Config

| Parameter                  | Value |
|----------------------------|-------|
| Stage recce speed pass 1   | 30    |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_InvalidStageRecceSpeedPassOneValue_ThrowsFormatException()
        {
            var md = @"## Config

| Parameter                  | Value  |
|----------------------------|--------|
| Stage recce speed pass 1   | notnum |
| Stage recce speed pass 2   | 30     |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected FormatException was not thrown.");
                }
                catch (FormatException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_InvalidStageRecceSpeedPassTwoValue_ThrowsFormatException()
        {
            var md = @"## Config

| Parameter                  | Value  |
|----------------------------|--------|
| Stage recce speed pass 1   | 30     |
| Stage recce speed pass 2   | notnum |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected FormatException was not thrown.");
                }
                catch (FormatException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_EmptyFile_ThrowsInvalidOperationException()
        {
            var path = WriteTempFile(string.Empty);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected InvalidOperationException was not thrown.");
                }
                catch (InvalidOperationException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        // -----------------------------------------------------------------
        // Start time config tests
        // -----------------------------------------------------------------

        [TestMethod]
        public void ParseFromFile_StartTimeFirstStage_ParsedCorrectly()
        {
            var md = @"## Config

| Parameter                  | Value   |
|----------------------------|---------|
| Stage recce speed pass 1   | 30      |
| Stage recce speed pass 2   | 30      |
| Start time first stage     | 7:00 am |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.IsTrue(rally.Config.StartTimeFirstStage.HasValue);
                Assert.AreEqual(new TimeSpan(7, 0, 0), rally.Config.StartTimeFirstStage.Value);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_StartTimeFirstStage_Missing_IsNull()
        {
            // Start time is optional — no exception if absent
            var path = WriteTempFile(ConfigSection + @"## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
");
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.IsFalse(rally.Config.StartTimeFirstStage.HasValue);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_InvalidStartTimeValue_ThrowsFormatException()
        {
            var md = @"## Config

| Parameter                  | Value    |
|----------------------------|----------|
| Stage recce speed pass 1   | 30       |
| Stage recce speed pass 2   | 30       |
| Start time first stage     | notaTime |

## Stages

| Code | Name |
|------|------|
| 1 | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                try
                {
                    RallyParser.ParseFromFile(path);
                    Assert.Fail("Expected FormatException was not thrown.");
                }
                catch (FormatException) { }
            }
            finally
            {
                File.Delete(path);
            }
        }

        // -----------------------------------------------------------------
        // Stage distance tests
        // -----------------------------------------------------------------

        [TestMethod]
        public void ParseFromFile_StagesWithDistance_LoadsDistanceMiles()
        {
            var md = ConfigSection + @"## Stages

| Code | Name      | Distance (mi) |
|------|-----------|---------------|
| 1    | Stage One | 6.3           |
| 2    | Stage Two | 9.8           |

## Travel Times (minutes)

|   | 1 | 2 |
|---|---|---|
| 1 | 5 | 10 |
| 2 | 10 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual(6.3, rally.Locations[0].DistanceMiles, 0.001);
                Assert.AreEqual(9.8, rally.Locations[1].DistanceMiles, 0.001);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_StagesWithoutDistance_DefaultsToZero()
        {
            var md = ConfigSection + @"## Stages

| Code | Name      |
|------|-----------|
| 1    | Stage One |

## Travel Times (minutes)

|   | 1 |
|---|---|
| 1 | 5 |
";
            var path = WriteTempFile(md);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual(0.0, rally.Locations[0].DistanceMiles);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_TemplateSampleFile_LoadsStartTimeAndDistances()
        {
            var templatePath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "..", "..", "..", "..", "Input-template.md");

            if (!File.Exists(templatePath))
                Assert.Inconclusive("Input-template.md not found at expected path: " + templatePath);

            var rally = RallyParser.ParseFromFile(templatePath);

            Assert.IsTrue(rally.Config.StartTimeFirstStage.HasValue, "StartTimeFirstStage should be set");
            Assert.AreEqual(new TimeSpan(7, 0, 0), rally.Config.StartTimeFirstStage.Value);
            Assert.AreEqual(6.3, rally.Locations[0].DistanceMiles, 0.001);
            Assert.AreEqual(9.8, rally.Locations[1].DistanceMiles, 0.001);
            Assert.AreEqual(7.36, rally.Locations[2].DistanceMiles, 0.001);
        }
    }
}

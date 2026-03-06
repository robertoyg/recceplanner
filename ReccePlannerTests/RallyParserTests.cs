using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReccePlanner;

namespace ReccePlannerTests
{
    [TestClass]
    public class RallyParserTests
    {
        private string WriteTempFile(string content)
        {
            var path = Path.GetTempFileName();
            File.WriteAllText(path, content);
            return path;
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
            var md = @"# Test Rally

## Stages

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
            var md = @"## Stages

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
            var md = @"## Stages

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
            var md = @"## Stages

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
            var md = @"## Stages

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
        public void ParseFromFile_EmptyFile_ReturnsEmptyRally()
        {
            var path = WriteTempFile(string.Empty);
            try
            {
                var rally = RallyParser.ParseFromFile(path);
                Assert.AreEqual(0, rally.Locations.Count);
                Assert.AreEqual(0, rally.TravelTimes.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void ParseFromFile_WhitespacePaddedCells_TrimmedCorrectly()
        {
            var md = @"## Stages

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
        public void ParseFromFile_TemplateSampleFile_LoadsThreeStagesAndNineRoutes()
        {
            var templatePath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "..", "..", "..", "..", "template.md");

            if (!File.Exists(templatePath))
                Assert.Inconclusive("template.md not found at expected path: " + templatePath);

            var rally = RallyParser.ParseFromFile(templatePath);

            Assert.AreEqual(3, rally.Locations.Count);
            Assert.AreEqual(9, rally.TravelTimes.Count); // 3x3 matrix
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Tests;

/// <summary>
/// Manual regression tests used for large game files not included in CI.
/// </summary>
public class GameExtractTests
{
    static readonly (int AppId, string Game, string AssetName)[] AnimGraphs = [
        (730, "game/csgo", "characters/models/shared/animgraphs/player_ct.vanmgrph")
    ];

    static readonly (int AppId, string Game)[] NmGraphGames = [
        (730, "game/csgo")
    ];

    [Test, TestCaseSource(nameof(AnimGraphs))]
    public void ExtractAnimGraph((int appId, string gameFolder, string assetName) testCase)
    {
        var testLocalPath = Path.Combine("Files", "GameExtract", $"{testCase.appId}", Path.GetFileName(testCase.assetName));
        var outputPathRepo = Path.Combine(TestContext.CurrentContext.TestDirectory, "../../", testLocalPath);

        // Use this to create or update correct output files
        var updateFiles = false;

        if (updateFiles == false && !File.Exists(outputPathRepo))
        {
            Assert.Ignore($"Sample output file not present.");
        }

        var game = GameFolderLocator.FindSteamGameByAppId(testCase.appId);
        if (!game.HasValue)
        {
            Assert.Ignore($"Steam game with AppId {testCase.appId} not present.");
        }

        var gamePath = game.Value.GamePath;
        var pak01 = Path.Combine(gamePath, testCase.gameFolder, "pak01_dir.vpk");

        using var archive = new Package();
        archive.Read(pak01);

        using var fileLoader = new GameFileLoader(archive, null);
        using var asset = fileLoader.LoadFileCompiled(testCase.assetName);

        if (asset == null)
        {
            Assert.Ignore($"{testCase.assetName} no longer exists on {pak01}.");
        }

        var outputPath = Path.Combine(TestContext.CurrentContext.TestDirectory, testLocalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var extractor = new AnimationGraphExtract(asset, fileLoader);
        var content = extractor.ToContentFile();

        Assert.That(content.Data, Is.Not.Null, $"Failed to extract {testCase.assetName}.");
        Debug.Assert(content.Data != null);

        if (updateFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPathRepo)!);
            using var stream = new FileStream(outputPathRepo, FileMode.Create);
            stream.Write(content.Data);
            return;
        }

        // Compare with existing file
        using var expectedStream = new FileStream(outputPath, FileMode.Open);
        using var expectedReader = new StreamReader(expectedStream);

        var expectedKv3 = expectedReader.ReadToEnd();
        var actualKv3 = System.Text.Encoding.UTF8.GetString(content.Data);

        Assert.That(actualKv3, Is.EqualTo(expectedKv3), $"Extracted file differs. {testCase.assetName}.");
    }

    [Test, TestCaseSource(nameof(NmGraphGames))]
    public void ExtractNmGraphsFromGameFiles((int appId, string gameFolder) testCase)
    {
        var game = GameFolderLocator.FindSteamGameByAppId(testCase.appId);
        if (!game.HasValue)
        {
            Assert.Ignore($"Steam game with AppId {testCase.appId} not present.");
        }

        var gamePath = game.Value.GamePath;
        var pak01 = Path.Combine(gamePath, testCase.gameFolder, "pak01_dir.vpk");

        using var archive = new Package();
        archive.Read(pak01);

        if (archive.Entries == null || !archive.Entries.TryGetValue("vnmgraph_c", out var entries))
        {
            Assert.Ignore($"No vnmgraph_c entries found in {pak01}.");
            return;
        }

        using var fileLoader = new GameFileLoader(archive, null);
        var assetNames = entries
            .Select(entry => entry.GetFullPath())
            .OrderBy(path => path)
            .Select(path => path[..^GameFileLoader.CompiledFileSuffix.Length])
            .ToArray();

        Assert.That(assetNames, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var assetName in assetNames)
            {
                using var resource = fileLoader.LoadFileCompiled(assetName);

                if (resource == null)
                {
                    Assert.Fail($"{assetName} no longer exists on {pak01}.");
                    continue;
                }

                Assert.DoesNotThrow(() => FileExtract.Extract(resource, fileLoader), assetName);
            }
        });
    }
}

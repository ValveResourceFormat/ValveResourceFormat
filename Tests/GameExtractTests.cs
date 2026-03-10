using System.IO;
using NUnit.Framework;
using ValveResourceFormat.IO;
using SteamDatabase.ValvePak;

namespace Tests;

/// <summary>
/// Manual regression tests used for large game files not included in CI.
/// </summary>
public class GameExtractTests
{
    static readonly (int AppId, string Game, string AssetName)[] AnimGraphs = [
        (730, "game/csgo", "characters/models/shared/animgraphs/player_ct.vanmgrph")
    ];


    [Test, TestCaseSource(nameof(AnimGraphs))]
    public void ExtractAnimGraph((int appId, string gameFolder, string assetName) testCase)
    {
        var testLocalPath = Path.Combine("Files", "GameExtract", $"{testCase.appId}", Path.GetFileName(testCase.assetName));
        var outputPathRepo = Path.Combine(TestContext.CurrentContext.TestDirectory, "../../", testLocalPath);

        // Use this to create or update correct output files
        var UpdateFiles = false;

        if (UpdateFiles == false && !File.Exists(outputPathRepo))
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

        // Remove the test case if you hit this
        Assert.That(asset, Is.Not.Null, $"{testCase.assetName} no longer exists on {pak01}.");

        var outputPath = Path.Combine(TestContext.CurrentContext.TestDirectory, testLocalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var extractor = new AnimationGraphExtract(asset, fileLoader);
        var content = extractor.ToContentFile();

        Assert.That(content.Data, Is.Not.Null, $"Failed to extract {testCase.assetName}.");

        if (UpdateFiles)
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
}

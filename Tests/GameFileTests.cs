using System.IO;
using System.Linq;
using NUnit.Framework;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SoftbodyPhysics;

namespace Tests;

/// <summary>
/// Manual tests various game files not included in CI.
/// </summary>
public class GameFileTests
{
    static readonly (int AppId, string Game, string AssetName)[] JiggleOrClothModels = [
        (730, "game/csgo", "models/props/de_mirage/tarp_a.vmdl"),
        (730, "game/csgo", "weapons/keychains/missinglink/vmdl/kc_missinglink_cat.vmdl"),
    ];

    [Test]
    public void ParseFeModelFromPhysicsFile()
    {
        using var resource = new Resource();
        var physPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "juggernaut.vphys_c");
        resource.Read(physPath);

        Assert.That(resource.DataBlock, Is.InstanceOf<PhysAggregateData>());
        var phys = (PhysAggregateData)resource.DataBlock!;

        Assert.That(phys.FeModel, Is.Not.Null, "juggernaut.vphys_c should contain an FeModel.");

        SoftbodyPhysicsParse(phys.FeModel!, "juggernaut.vphys_c");
    }

    [Test, TestCaseSource(nameof(JiggleOrClothModels))]
    public void ParseFeModel((int appId, string gameFolder, string assetName) testCase)
    {
        var game = GameFolderLocator.FindSteamGameByAppId(testCase.appId);
        if (!game.HasValue)
        {
            Assert.Ignore($"Steam game with AppId {testCase.appId} not present.");
        }

        var gamePath = game.Value.GamePath;
        var pak01 = Path.Combine(gamePath, testCase.gameFolder, "pak01_dir.vpk");
        if (!File.Exists(pak01))
        {
            Assert.Ignore($"{pak01} not present.");
        }

        using var archive = new Package();
        archive.Read(pak01);

        using var fileLoader = new GameFileLoader(archive, null);
        using var asset = fileLoader.LoadFileCompiled(testCase.assetName);

        if (asset == null)
        {
            Assert.Ignore($"{testCase.assetName} no longer exists on {pak01}.");
        }

        Assert.That(asset.DataBlock, Is.InstanceOf<Model>(), testCase.assetName);
        var model = (Model)asset.DataBlock!;

        // Resolve physics the same way ModelExtract does: referenced first, then embedded.
        PhysAggregateData? phys;
        var refPhysics = model.GetReferencedPhysNames()?.FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFileCompiled(refPhysics);
            phys = physResource?.DataBlock as PhysAggregateData;
        }
        else
        {
            phys = model.GetEmbeddedPhys();
        }

        if (phys?.FeModel is not PhysFeModel feModel)
        {
            Assert.Ignore($"No FeModel present in {testCase.assetName}.");
            return;
        }

        SoftbodyPhysicsParse(feModel, testCase.assetName);
    }

    static void SoftbodyPhysicsParse(PhysFeModel feModel, string assetName)
    {
        // Poke every getter so the whole parse runs without throwing.
        Assert.DoesNotThrow(() =>
        {
            var ctrlNames = feModel.CtrlName;
            var nodes = feModel.Nodes;
            var ropes = feModel.Ropes;
            var freeNodes = feModel.FreeNodes;

            var nodeBases = feModel.NodeBases;
            var rods = feModel.Rods;
            var springIntegrators = feModel.SpringIntegrators;
            var axialEdges = feModel.AxialEdges;
            var followNodes = feModel.FollowNodes;
            var ctrlOffsets = feModel.CtrlOffsets;
            var ctrlOsOffsets = feModel.CtrlOsOffsets;
            var nodeStrayBoxes = feModel.NodeStrayBoxes;
            var treeChildren = feModel.TreeChildren;

            var spheres = feModel.SphereRigids;
            var capsules = feModel.TaperedCapsuleRigids;
            var boxes = feModel.BoxRigids;

            // Poke the remaining getters without keeping their results.
            _ = feModel.CtrlHash;
            _ = feModel.NodeInvMasses;
            _ = feModel.WorldCollisionParams;
            _ = feModel.WorldCollisionNodes;
            _ = feModel.TreeParents;
            _ = feModel.TreeCollisionMasks;
            _ = feModel.StaticNodeFlags;
            _ = feModel.DynamicNodeFlags;
            _ = feModel.NodeCount;
            _ = feModel.DefaultGravityScale;
            _ = feModel.AddWorldCollisionRadius;

            TestContext.Out.WriteLine($"{assetName}: ctrlNames={ctrlNames.Length} nodes={nodes.Length} " +
                $"ropes={ropes.Length} freeNodes={freeNodes.Length} nodeBases={nodeBases.Length} rods={rods.Length} " +
                $"followNodes={followNodes.Length} ctrlOffsets={ctrlOffsets.Length} ctrlOsOffsets={ctrlOsOffsets.Length} " +
                $"strayBoxes={nodeStrayBoxes.Length} springs={springIntegrators.Length} axialEdges={axialEdges.Length} " +
                $"treeChildren={treeChildren.Length} spheres={spheres.Length} capsules={capsules.Length} boxes={boxes.Length}");
        }, assetName);
    }
}

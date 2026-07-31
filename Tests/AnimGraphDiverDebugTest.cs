using System;
using System.IO;
using NUnit.Framework;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests
{
    /// <summary>Local-only debug harness for the CS2 worldmodel graph. Requires a CS2 install.</summary>
    [TestFixture]
    public class AnimGraphDiverDebugTest
    {
        private const string VpkPath = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk";

        [Test]
        public void WorldmodelActiveChain()
        {
            if (!File.Exists(VpkPath))
            {
                Assert.Ignore("CS2 not installed");
            }

            using var package = new Package();
            package.Read(VpkPath);
            var loader = new GameFileLoader(package, VpkPath);

            var res = loader.LoadFileCompiled("animation/graphs/worldmodel/worldmodel.vnmgraph");
            var graph = new AnimationGraph((NmGraphDefinition)res!.DataBlock!, loader);

            graph.IdParameters["weapon_type"] = "weapon_ak47";
            graph.IdParameters["weapon_category"] = "weapon_category_rifle";
            graph.IdParameters["move_type"] = "move_type_ground";
            graph.IdParameters["ground_action"] = "ground_action_idle";

            var controller = new AnimationController(graph.Skeleton, []);
            controller.SetAnimationGraph(graph);

            for (var i = 0; i < 5; i++)
            {
                controller.Update(1f / 60f);
            }

            var ctx = graph.Context;

            // The top pose chain of worldmodel.vnmgraph plus the first locomotion levels
            foreach (short i in (short[])[64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74])
            {
                if (ctx.Nodes[i] is PoseNode poseNode)
                {
                    Console.WriteLine($"[{i}] {poseNode.GetType().Name} valid={poseNode.IsValid} t={poseNode.CurrentTime:F2} dur={poseNode.Duration:F2}");
                }
                else
                {
                    Console.WriteLine($"[{i}] {ctx.Nodes[i].GetType().Name} (value node)");
                }
            }

            var invalidCount = 0;
            for (short i = 0; i < ctx.Nodes.Length; i++)
            {
                if (ctx.Nodes[i] is PoseNode poseNode && !poseNode.IsValid)
                {
                    invalidCount++;
                }
            }

            Console.WriteLine($"invalid pose nodes: {invalidCount} / total {ctx.Nodes.Length}");
        }
    }
}

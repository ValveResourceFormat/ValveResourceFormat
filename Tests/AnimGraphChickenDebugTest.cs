using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests
{
    /// <summary>
    /// Local-only repro harness driving the CS2 chicken graph's action parameter.
    /// Requires a CS2 install; run explicitly with --filter AnimGraphChickenDebugTest.
    /// </summary>
    [TestFixture]
    public class AnimGraphChickenDebugTest
    {
        private const string VpkPath = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk";

        [Test]
        public void ChickenActionSquatAndBack()
        {
            if (!File.Exists(VpkPath))
            {
                Assert.Ignore("CS2 not installed");
            }

            using var package = new Package();
            package.Read(VpkPath);
            var loader = new GameFileLoader(package, VpkPath);

            var res = loader.LoadFileCompiled("animation/graphs/chicken.vnmgraph");
            var graph = new AnimationGraph((NmGraphDefinition)res!.DataBlock!, loader);

            var sm = (StateMachineNode)graph.Context.Nodes[8];

            void Tick(int frames, string label)
            {
                for (var i = 0; i < frames; i++)
                {
                    graph.Update(1f / 60f);

                    if (i % 30 == 0 || i == frames - 1)
                    {
                        var stateTimes = string.Join(" ", Enumerable.Range(0, 7).Select(s =>
                        {
                            var st = sm.States[s].StateNode;
                            return $"s{s}:{st.CurrentTime:F2}{(st.IsTransitioning ? "*" : "")}";
                        }));
                        Console.WriteLine($"[{label} f{i:D3}] active={sm.ActiveStateIndex} trans={(sm.ActiveTransition != null ? "Y" : "n")} t={sm.CurrentTime:F3} dur={sm.Duration:F2} | {stateTimes}");
                    }
                }
            }

            graph.IdParameters["action"] = "action_idle";
            Tick(90, "idle ");

            graph.IdParameters["action"] = "action_squat";
            Tick(240, "squat");

            graph.IdParameters["action"] = "action_idle";
            Tick(240, "back ");
        }
    }
}

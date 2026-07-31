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
            var squatSm = (StateMachineNode)graph.Context.Nodes[35];

            var prevPose = default(ValveResourceFormat.ResourceTypes.ModelAnimation.FrameBone[]);

            void Tick(int frames, string label, bool everyFrame = false)
            {
                for (var i = 0; i < frames; i++)
                {
                    var pose = graph.Update(1f / 60f);

                    // Detect pose discontinuities (a "glitchy frame"): max bone position jump and
                    // max bone rotation jump (degrees) per frame
                    var maxDelta = 0f;
                    var maxAngle = 0f;
                    if (prevPose != null && prevPose.Length == pose.Length)
                    {
                        for (var b = 0; b < pose.Length; b++)
                        {
                            maxDelta = Math.Max(maxDelta, (pose[b].Position - prevPose[b].Position).Length());
                            var dot = Math.Clamp(Math.Abs(System.Numerics.Quaternion.Dot(pose[b].Angle, prevPose[b].Angle)), 0f, 1f);
                            maxAngle = Math.Max(maxAngle, float.RadiansToDegrees(2f * MathF.Acos(dot)));
                        }
                    }

                    prevPose = [.. pose];

                    if (everyFrame || i % 30 == 0 || i == frames - 1 || maxDelta > 1f || maxAngle > 45f)
                    {
                        var stateTimes = string.Join(" ", new[] { 0, 1 }.Select(s =>
                        {
                            var st = sm.States[s].StateNode;
                            return $"s{s}:{st.CurrentTime:F2}{(st.IsTransitioning ? "*" : "")}";
                        }));
                        var squatStates = string.Join(" ", new[] { 0, 1, 2 }.Select(s =>
                        {
                            var st = squatSm.States[s].StateNode;
                            return $"q{s}:{st.CurrentTime:F2}/{st.Duration:F2}{(st.IsTransitioning ? "*" : "")}";
                        }));
                        Console.WriteLine($"[{label} f{i:D3}] active={sm.ActiveStateIndex} trans={(sm.ActiveTransition != null ? "Y" : "n")} t={sm.CurrentTime:F3} dur={sm.Duration:F2} | {stateTimes} | squatSm: act={squatSm.ActiveStateIndex} trans={(squatSm.ActiveTransition != null ? "Y" : "n")} {squatStates} | dPose={maxDelta:F2} dAng={maxAngle:F0}");
                    }
                }
            }

            graph.IdParameters["action"] = "action_idle";
            Tick(90, "idle ");

            graph.IdParameters["action"] = "action_squat";
            Tick(12, "sq>  ", everyFrame: true);
            Tick(490, "squat");
            Tick(60, "endsq", everyFrame: true);
            Tick(150, "hold ");

            graph.IdParameters["action"] = "action_idle";
            Tick(12, "bk>  ", everyFrame: true);
            Tick(120, "back ");

            // An action_reset pulse while idle should swap to the other variation state and resume playing
            graph.BoolParameters["action_reset"] = true;
            Tick(2, "pulse", everyFrame: true);
            graph.BoolParameters["action_reset"] = false;
            Tick(120, "rerol");
        }
    }
}

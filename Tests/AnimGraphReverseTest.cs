using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests
{
    /// <summary>
    /// Exercises clip play-in-reverse (no CS2 graph authors it, so a controllable reverse input is
    /// attached to a live clip node): toggling mirrors the current time, reversed playback samples
    /// the pose at (1 - t), and toggling back restores forward sampling.
    /// Requires a CS2 install.
    /// </summary>
    [TestFixture]
    public class AnimGraphReverseTest
    {
        private const string VpkPath = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk";

        private sealed class StubBoolNode(KVObject data) : BoolValueNode(data)
        {
            public bool Value;
            protected override bool GetValueInternal(GraphContext ctx) => Value;
        }

        [Test]
        public void ClipPlaysInReverse()
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
            graph.IdParameters["action"] = "action_idle";

            const float Dt = 1f / 60f;

            // Warm up and find an actively playing, multi-frame clip node
            var clipNodes = graph.Context.Nodes.OfType<ClipNode>()
                .Where(n => n.Clip != null && n.Clip.FrameCount > 1)
                .ToArray();
            var previousTimes = clipNodes.Select(n => n.CurrentTime).ToArray();

            for (var i = 0; i < 10; i++)
            {
                graph.Update(Dt);
            }

            var clipNode = clipNodes.Where((n, i) => n.CurrentTime > previousTimes[i] && n.CurrentTime < 0.5f && n.Duration > 2f).FirstOrDefault();
            Assert.That(clipNode, Is.Not.Null, "expected an advancing clip node in the idle chicken graph");

            var clip = clipNode!.Clip!;
            var scratch = new FrameBone[graph.ParentSpaceReferencePose.Length];

            void AssertPoseSampledAt(float clipTime, string label)
            {
                clip.SamplePoseAtPercentage(clipTime, scratch);
                for (var b = 0; b < scratch.Length; b++)
                {
                    Assert.That((clipNode.PoseTransforms[b].Position - scratch[b].Position).Length(), Is.LessThan(1e-5f), $"{label}: bone {b} position");
                    Assert.That(MathF.Abs(System.Numerics.Quaternion.Dot(clipNode.PoseTransforms[b].Angle, scratch[b].Angle)), Is.GreaterThan(1f - 1e-6f), $"{label}: bone {b} rotation");
                }
            }

            // Forward playback samples at the current time
            graph.Update(Dt);
            AssertPoseSampledAt(clipNode.CurrentTime, "forward");

            // Attach the reverse input and toggle: the current time mirrors, and subsequent frames
            // sample at (1 - t)
            var stubData = new KVObject();
            stubData.Add("m_nNodeIdx", 0);
            var stub = new StubBoolNode(stubData) { Value = true };
            clipNode.PlayInReverseValueNode = stub;

            var timeBeforeToggle = clipNode.CurrentTime;
            graph.Update(Dt);

            var expectedAfterToggle = (1f - timeBeforeToggle) + (Dt / clipNode.Duration);
            Assert.That(clipNode.CurrentTime, Is.EqualTo(expectedAfterToggle).Within(1e-4f), "toggle mirrors the current time before advancing");

            for (var i = 0; i < 30; i++)
            {
                graph.Update(Dt);
                AssertPoseSampledAt(1f - clipNode.CurrentTime, $"reversed frame {i}");
            }

            // Toggle back: mirrored again, forward sampling resumes
            stub.Value = false;
            timeBeforeToggle = clipNode.CurrentTime;
            graph.Update(Dt);

            expectedAfterToggle = (1f - timeBeforeToggle) + (Dt / clipNode.Duration);
            Assert.That(clipNode.CurrentTime, Is.EqualTo(expectedAfterToggle).Within(1e-4f), "toggling back mirrors the current time again");

            for (var i = 0; i < 30; i++)
            {
                graph.Update(Dt);
                AssertPoseSampledAt(clipNode.CurrentTime, $"forward-again frame {i}");
            }
        }
    }
}

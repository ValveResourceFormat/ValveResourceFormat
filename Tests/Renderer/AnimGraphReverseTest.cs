using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValvePak;

namespace Tests.Renderer
{
    /// <summary>
    /// Exercises clip play-in-reverse (no CS2 graph authors it, so a controllable reverse input is
    /// attached to a live clip node): toggling mirrors the current time, reversed playback samples
    /// the pose at (1 - t), and toggling back restores forward sampling.
    /// Requires a CS2 install.
    /// </summary>
    public class AnimGraphReverseTest
    {
        private const string VpkPath = @"C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive\game\csgo\pak01_dir.vpk";

        private sealed class StubBoolNode(KVObject data) : BoolValueNode(data)
        {
            public bool Value;
            protected override bool GetValueInternal(GraphContext ctx) => Value;
        }

        [Test]
        public async Task ClipPlaysInReverse()
        {
            if (!File.Exists(VpkPath))
            {
                Skip.Test("CS2 not installed");
            }

            using var package = new Package();
            package.Read(VpkPath);
            var loader = new GameFileLoader(package, VpkPath);

            var res = loader.LoadFileCompiled("animation/graphs/chicken/chicken.vnmgraph");
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
            await Assert.That(clipNode).IsNotNull().Because("expected an advancing clip node in the idle chicken graph");

            var clip = clipNode!.Clip!;
            var scratch = new FrameBone[graph.ParentSpaceReferencePose.Length];

            async Task AssertPoseSampledAt(float clipTime, string label)
            {
                clip.SamplePoseAtPercentage(clipTime, scratch);
                for (var b = 0; b < scratch.Length; b++)
                {
                    await Assert.That((clipNode.PoseTransforms[b].Position - scratch[b].Position).Length()).IsLessThan(1e-5f).Because($"{label}: bone {b} position");
                    await Assert.That(MathF.Abs(Quaternion.Dot(clipNode.PoseTransforms[b].Angle, scratch[b].Angle))).IsGreaterThan(1f - 1e-6f).Because($"{label}: bone {b} rotation");
                }
            }

            // Forward playback samples at the current time
            graph.Update(Dt);
            await AssertPoseSampledAt(clipNode.CurrentTime, "forward");

            // Attach the reverse input and toggle: the current time mirrors, and subsequent frames
            // sample at (1 - t)
            var stubData = new KVObject();
            stubData.Add("m_nNodeIdx", 0);
            var stub = new StubBoolNode(stubData) { Value = true };
            clipNode.PlayInReverseValueNode = stub;

            var timeBeforeToggle = clipNode.CurrentTime;
            graph.Update(Dt);

            var expectedAfterToggle = (1f - timeBeforeToggle) + (Dt / clipNode.Duration);
            await Assert.That(clipNode.CurrentTime).IsEqualTo(expectedAfterToggle).Within(1e-4f).Because("toggle mirrors the current time before advancing");

            for (var i = 0; i < 30; i++)
            {
                graph.Update(Dt);
                await AssertPoseSampledAt(1f - clipNode.CurrentTime, $"reversed frame {i}");
            }

            // Toggle back: mirrored again, forward sampling resumes
            stub.Value = false;
            timeBeforeToggle = clipNode.CurrentTime;
            graph.Update(Dt);

            expectedAfterToggle = (1f - timeBeforeToggle) + (Dt / clipNode.Duration);
            await Assert.That(clipNode.CurrentTime).IsEqualTo(expectedAfterToggle).Within(1e-4f).Because("toggling back mirrors the current time again");

            for (var i = 0; i < 30; i++)
            {
                graph.Update(Dt);
                await AssertPoseSampledAt(clipNode.CurrentTime, $"forward-again frame {i}");
            }
        }
    }
}

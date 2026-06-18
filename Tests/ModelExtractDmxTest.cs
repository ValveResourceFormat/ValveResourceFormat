using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace Tests
{
    [TestFixture]
    public class ModelExtractDmxTest
    {
        [Test]
        public void DmxAnimationWritesRootMotionToModelChannel()
        {
            var channels = ExtractChannels("box_creature_leggy_walk", out _);

            // Root motion is written to the separate model-level transform channel ("_p"), not baked into
            // bones, so the DMX keeps the root track intact. It must travel the animation's full ~47.92
            // source-unit displacement.
            var modelRoot = RootChannelValues(channels, "");
            var displacement = modelRoot[^1] - modelRoot[0];
            Assert.That(displacement.X, Is.EqualTo(47.92f).Within(0.1f));

            // The root_motion bone itself carries no baked travel - only its raw per-frame data.
            var rootMotionBone = RootChannelValues(channels, "root_motion");
            var boneDisplacement = rootMotionBone[^1] - rootMotionBone[0];
            Assert.That(boneDisplacement.X, Is.LessThan(1.0f));
        }

        [Test]
        public void DmxAnimationWithoutMovementHasNoRootMotionChannel()
        {
            var channels = ExtractChannels("box_creature_leggy_idle", out var anim);
            Assert.That(anim.HasMovementData(), Is.False);

            // With no movement data, no model-level root motion channel is emitted.
            Assert.That(channels.Cast<Datamodel.Element>().Any(c => c.Name is "_p" or "_o"), Is.False);
        }

        private static Datamodel.ElementArray ExtractChannels(string animationName, out Animation anim)
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;
            anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == animationName);

            var bytes = ModelExtract.ToDmxAnim(model, anim);
            using var ms = new MemoryStream(bytes);
            // Eager load so deferred attributes don't read from the stream after it is disposed.
            var dm = Datamodel.Datamodel.Load(ms, Datamodel.Codecs.DeferredMode.Disabled);

            var clip = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)dm.Root!["animationList"]!)["animations"]!)[0]!;
            return (Datamodel.ElementArray)clip["channels"]!;
        }

        private static Datamodel.Vector3Array RootChannelValues(Datamodel.ElementArray channels, string boneName)
        {
            var channel = channels.Cast<Datamodel.Element>().Single(c => c.Name == $"{boneName}_p");
            var layer = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)channel["log"]!)["layers"]!)[0]!;
            return (Datamodel.Vector3Array)layer["values"]!;
        }
    }
}

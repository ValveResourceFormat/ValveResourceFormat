using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class ModelExtractDmxTest
    {
        [Test]
        public void DmxAnimationBakesRootMotionIntoRootBone()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == "box_creature_leggy_walk");

            var bytes = ModelExtract.ToDmxAnim(model, anim);

            using var ms = new MemoryStream(bytes);
            var dm = Datamodel.Datamodel.Load(ms);

            var clip = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)dm.Root!["animationList"]!)["animations"]!)[0]!;
            var channels = (Datamodel.ElementArray)clip["channels"]!;

            Datamodel.Vector3Array RootChannelValues(string boneName)
            {
                var channel = channels.Cast<Datamodel.Element>().Single(c => c.Name == $"{boneName}_p");
                var layer = (Datamodel.Element)((Datamodel.ElementArray)((Datamodel.Element)channel["log"]!)["layers"]!)[0]!;
                return (Datamodel.Vector3Array)layer["values"]!;
            }

            // The root_motion bone has no per-frame animation, so its position channel is pure baked root
            // motion. It must travel the animation's full ~47.92 source-unit displacement.
            var rootMotion = RootChannelValues("root_motion");
            var displacement = rootMotion[^1] - rootMotion[0];
            Assert.That(displacement.X, Is.EqualTo(47.92f).Within(0.1f));

            // The model-level transform channel that importers ignored must no longer be emitted.
            Assert.That(channels.Cast<Datamodel.Element>().Any(c => c.Name is "_p" or "_o"), Is.False);
        }
    }
}

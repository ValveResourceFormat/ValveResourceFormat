using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class AnimationTest
    {
        [Test]
        public void TestEmbeddedAnimations()
        {
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var model = (Model)resource.DataBlock!;

            var animGroupPaths = model.GetReferencedAnimationGroupNames();
            var animations = model.GetEmbeddedAnimations().ToList();

            Assert.That(animGroupPaths.Count, Is.Zero);
            Assert.That(animations, Has.Count.EqualTo(3));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(animations[0].Name, Is.EqualTo("ref_pose"));
                Assert.That(animations[0].Fps, Is.EqualTo(30));
                Assert.That(animations[0].FrameCount, Is.EqualTo(1));

                Assert.That(animations[1].Name, Is.EqualTo("box_creature_leggy_idle"));
                Assert.That(animations[1].Fps, Is.EqualTo(30));
                Assert.That(animations[1].FrameCount, Is.EqualTo(49));

                Assert.That(animations[2].Name, Is.EqualTo("box_creature_leggy_walk"));
                Assert.That(animations[2].Fps, Is.EqualTo(30));
                Assert.That(animations[2].FrameCount, Is.EqualTo(25));
            }
        }
    }
}

using System.IO;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace Tests
{
    [TestFixture]
    public class AnimationTest
    {
        // box_creature_leggy_walk travels ~47.92 source units forward (+X) over its 25-frame cycle,
        // stored in the movement array (root motion), not in the bone frames.
        private const float FullDisplacementX = 47.92f;

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

            using (Assert.EnterMultipleScope())
            {
                Assert.That(animGroupPaths.Count(), Is.Zero);
                Assert.That(animations, Has.Count.EqualTo(3));

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

        [Test]
        public void MovementOffsetReachesFullDisplacement()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == "box_creature_leggy_walk");

            Assert.That(anim.HasMovementData(), Is.True);

            var lastFrame = anim.FrameCount - 1;
            var byFrame = anim.GetMovementOffsetData(lastFrame);
            var byTime = anim.GetMovementOffsetData(lastFrame / anim.Fps);

            using (Assert.EnterMultipleScope())
            {
                // Both overloads must reach the full displacement; the time-based one previously under-shot.
                Assert.That(byFrame.Position.X, Is.EqualTo(FullDisplacementX).Within(0.05f));
                Assert.That(byTime.Position.X, Is.EqualTo(FullDisplacementX).Within(0.05f));
            }
        }
    }
}

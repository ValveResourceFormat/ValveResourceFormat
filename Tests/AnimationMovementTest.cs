using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    [TestFixture]
    public class AnimationMovementTest
    {
        // box_creature_leggy_walk travels ~47.92 source units forward (+X) over its 25-frame cycle,
        // stored entirely in the animation movement array (root motion), not in the bone frames.
        private const float FullDisplacementX = 47.92f;

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
                // Both overloads must reach the animation's total root-motion displacement. The time-based
                // overload previously under-shot (it interpolated by seconds instead of a normalized factor).
                Assert.That(byFrame.Position.X, Is.EqualTo(FullDisplacementX).Within(0.05f));
                Assert.That(byTime.Position.X, Is.EqualTo(FullDisplacementX).Within(0.05f));
            }
        }
    }
}

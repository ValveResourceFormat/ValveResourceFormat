using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace Tests
{
    public class SequenceAnimationTest
    {
        // box_creature_leggy_walk travels ~47.92 source units forward (+X) over its 25-frame cycle,
        // stored in the movement array (root motion), not in the bone frames.
        private const float FullDisplacementX = 47.92f;

        [Test]
        public async Task TestEmbeddedAnimations()
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c");
            using var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var model = (Model)resource.DataBlock!;

            var animGroupPaths = model.GetReferencedAnimationGroupNames();
            var animations = model.GetEmbeddedAnimations().ToList();

            using (Assert.Multiple())
            {
                await Assert.That(animGroupPaths.Count()).IsZero();
                await Assert.That(animations).Count().IsEqualTo(3);
                await Assert.That(animations).All(animation => animation is SequenceAnimation);

                await Assert.That(animations[0].Name).IsEqualTo("ref_pose");
                await Assert.That(animations[0].Fps).IsEqualTo(30);
                await Assert.That(animations[0].FrameCount).IsEqualTo(1);

                await Assert.That(animations[1].Name).IsEqualTo("box_creature_leggy_idle");
                await Assert.That(animations[1].Fps).IsEqualTo(30);
                await Assert.That(animations[1].FrameCount).IsEqualTo(49);

                await Assert.That(animations[2].Name).IsEqualTo("box_creature_leggy_walk");
                await Assert.That(animations[2].Fps).IsEqualTo(30);
                await Assert.That(animations[2].FrameCount).IsEqualTo(25);
            }
        }

        [Test]
        public async Task MovementOffsetReachesFullDisplacement()
        {
            using var resource = new Resource();
            resource.Read(Path.Combine(TestContext.TestDirectory!, "Files", "box_creature_ik_model.vmdl_c"));
            var model = (Model)resource.DataBlock!;
            var anim = model.GetAllAnimations(new NullFileLoader()).First(a => a.Name == "box_creature_leggy_walk");

            await Assert.That(anim.HasMovementData()).IsTrue();

            var lastFrame = anim.FrameCount - 1;
            var byFrame = anim.GetMovementOffsetData(lastFrame);
            var byTime = anim.GetMovementOffsetData(lastFrame / anim.Fps);

            using (Assert.Multiple())
            {
                // Both overloads must reach the full displacement; the time-based one previously under-shot.
                await Assert.That(byFrame.Position.X).IsEqualTo(FullDisplacementX).Within(0.05f);
                await Assert.That(byTime.Position.X).IsEqualTo(FullDisplacementX).Within(0.05f);
            }
        }
    }
}

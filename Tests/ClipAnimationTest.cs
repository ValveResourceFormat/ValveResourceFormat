using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests
{
    [TestFixture]
    public class ClipAnimationTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", name);

        private static AnimationClip LoadAkClip(Resource resource)
        {
            resource.Read(FilePath("idle_ak.vnmclip_c"));
            var clip = (AnimationClip)resource.DataBlock!;

            return clip.SkeletonName.EndsWith("ak47.vnmskel", StringComparison.Ordinal)
                ? clip
                : clip.SecondaryAnimations.First(c => c.SkeletonName.EndsWith("ak47.vnmskel", StringComparison.Ordinal));
        }

        [Test]
        public void ConstructionMapsClipProperties()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            var animation = new ClipAnimation(clip);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(animation.Name, Is.EqualTo(clip.Name));
                Assert.That(animation.FrameCount, Is.EqualTo(1));
                Assert.That(animation.Fps, Is.EqualTo(1), "a zero-duration clip falls back to 1 fps");
                Assert.That(animation.IsAdditive, Is.False);
            }
        }

        [Test]
        public void DecodeFrameReadsClipPose()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            using var skeletonResource = new Resource();
            skeletonResource.Read(FilePath("ak47.vnmskel_c"));
            var skeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            var animation = new ClipAnimation(clip);
            var frameCache = new AnimationFrameCache(skeleton, []);

            var frame = frameCache.GetFrame(animation, 0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(frame.Bones, Has.Length.EqualTo(skeleton.Bones.Length));
                Assert.That(frame.Bones.Select(b => b.Angle.Length()), Is.All.EqualTo(1f).Within(0.001f), "decoded rotations are unit quaternions");
                Assert.That(frame.Movement, Is.EqualTo(default(AnimationMovement.MovementData)));
            }
        }

        [Test]
        public void ClipAnimationHasNoMovementData()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            var animation = new ClipAnimation(clip);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(animation.HasMovementData(), Is.False);
                Assert.That(animation.GetMovementOffsetData(0), Is.EqualTo(default(AnimationMovement.MovementData)));
                Assert.That(animation.GetMovementOffsetData(0f), Is.EqualTo(default(AnimationMovement.MovementData)));
            }
        }
    }
}

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests
{
    public class ClipAnimationTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.TestDirectory!, "Files", name);

        private static AnimationClip LoadAkClip(Resource resource)
        {
            resource.Read(FilePath("idle_ak.vnmclip_c"));
            var clip = (AnimationClip)resource.DataBlock!;

            return clip.SkeletonName.EndsWith("ak47.vnmskel", StringComparison.Ordinal)
                ? clip
                : clip.SecondaryAnimations.First(c => c.SkeletonName.EndsWith("ak47.vnmskel", StringComparison.Ordinal));
        }

        [Test]
        public async Task ConstructionMapsClipProperties()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            var animation = new ClipAnimation(clip);

            using (Assert.Multiple())
            {
                await Assert.That(animation.Name).IsEqualTo(clip.Name);
                await Assert.That(animation.FrameCount).IsEqualTo(1);
                await Assert.That(animation.Fps).IsEqualTo(1).Because("a zero-duration clip falls back to 1 fps");
                await Assert.That(animation.IsAdditive).IsFalse();
            }
        }

        [Test]
        public async Task DecodeFrameReadsClipPose()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            using var skeletonResource = new Resource();
            skeletonResource.Read(FilePath("ak47.vnmskel_c"));
            var skeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            var animation = new ClipAnimation(clip);
            var frameCache = new AnimationFrameCache(skeleton, []);

            var frame = frameCache.GetFrame(animation, 0);

            using (Assert.Multiple())
            {
                await Assert.That(frame.Bones).Count().IsEqualTo(skeleton.Bones.Length);
                await Assert.That(frame.Bones.Select(b => b.Angle.Length())).All(length => MathF.Abs(length - 1f) <= 0.001f).Because("decoded rotations are unit quaternions");
                await Assert.That(frame.Movement).IsEqualTo(default(AnimationMovement.MovementData));
            }
        }

        private static ClipAnimation LoadNovaShootAnimation(Resource resource)
        {
            resource.Read(FilePath("shoot1_nova.vnmclip_c"));

            return new ClipAnimation((AnimationClip)resource.DataBlock!);
        }

        private static readonly string[] ClipEventClasses = ["CNmIDEvent", "CNmParticleEvent", "CNmSoundEvent", "CNmParticleEvent"];

        [Test]
        public async Task EventsAreReadFromClip()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            var soundEvent = animation.Events.OfType<NmSoundEvent>().Single();
            var idEvent = animation.Events.OfType<NmIDEvent>().Single();

            using (Assert.Multiple())
            {
                await Assert.That(animation.Duration).IsEqualTo(0.8f).Within(0.0001f);
                await Assert.That(animation.Events.Select(e => e.ClassName)).IsEquivalentTo(ClipEventClasses, CollectionOrdering.Matching);

                // Times are stored normalized in the resource, they are exposed in seconds
                await Assert.That(soundEvent.Name).IsEqualTo("Weapon_Nova.Pump_Q");
                await Assert.That(soundEvent.StartTime).IsEqualTo(0.233333f).Within(0.0001f);
                await Assert.That(soundEvent.DurationInterruptionThreshold).IsEqualTo(0.9f).Because("unset properties fall back to their engine default");

                await Assert.That(idEvent.ID).IsEqualTo("WPN_BLOCK_INSPECT");
                await Assert.That(idEvent.Duration).IsEqualTo(0.7f).Within(0.0001f);

                await Assert.That(animation.Events.OfType<NmParticleEvent>().Last().ParticleSystemName).IsEqualTo("particles/weapons/cs_weapon_fx/weapon_shell_casing_shotgun_nova.vpcf");
            }
        }

        [Test]
        public async Task StaticClipHasNoMovementData()
        {
            using var resource = new Resource();
            var clip = LoadAkClip(resource);

            var animation = new ClipAnimation(clip);

            using (Assert.Multiple())
            {
                await Assert.That(animation.HasMovementData()).IsFalse();
                await Assert.That(animation.GetMovementOffsetData(0)).IsEqualTo(default(AnimationMovement.MovementData));
                await Assert.That(animation.GetMovementOffsetData(0f)).IsEqualTo(default(AnimationMovement.MovementData));
            }
        }
    }
}

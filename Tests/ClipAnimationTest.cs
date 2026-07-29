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

        private static ClipAnimation LoadNovaShootAnimation(Resource resource)
        {
            resource.Read(FilePath("shoot1_nova.vnmclip_c"));

            return new ClipAnimation((AnimationClip)resource.DataBlock!);
        }

        [Test]
        public void EventsAreReadFromClip()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            var soundEvent = animation.Events.OfType<NmSoundEvent>().Single();
            var idEvent = animation.Events.OfType<NmIDEvent>().Single();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(animation.Duration, Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(animation.Events.Select(e => e.ClassName), Is.EqualTo(
                    new[] { "CNmIDEvent", "CNmParticleEvent", "CNmSoundEvent", "CNmParticleEvent" }));

                // Times are stored normalized in the resource, they are exposed in seconds
                Assert.That(soundEvent.Name, Is.EqualTo("Weapon_Nova.Pump_Q"));
                Assert.That(soundEvent.StartTime, Is.EqualTo(0.233333f).Within(0.0001f));
                Assert.That(soundEvent.DurationInterruptionThreshold, Is.EqualTo(0.9f), "unset properties fall back to their engine default");

                Assert.That(idEvent.ID, Is.EqualTo("WPN_BLOCK_INSPECT"));
                Assert.That(idEvent.Duration, Is.EqualTo(0.7f).Within(0.0001f));

                Assert.That(animation.Events.OfType<NmParticleEvent>().Last().ParticleSystemName,
                    Is.EqualTo("particles/weapons/cs_weapon_fx/weapon_shell_casing_shotgun_nova.vpcf"));
            }
        }

        // Event start times of the nova shoot clip, which is 0.8 seconds long
        private static readonly float[] ClipStartEvents = [0f, 0f];
        private static readonly float[] PumpSoundEvent = [0.2333333f];
        private static readonly float[] ShellCasingEvent = [0.3333333f];

        [Test]
        public void SampledEventsAreCrossedInPlaybackRange()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            static float[] Sampled(SampledAnimationEvents<NmClipEvent> events)
            {
                var times = new List<float>();

                foreach (var clipEvent in events)
                {
                    times.Add(clipEvent.StartTime);
                }

                return [.. times];
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Sampled(animation.SampleEvents(0f, 0.1f)), Is.EqualTo(ClipStartEvents), "events at the very start fire on the first update");
                Assert.That(Sampled(animation.SampleEvents(0.1f, 0.2f)), Is.Empty);
                Assert.That(Sampled(animation.SampleEvents(0.2f, 0.3f)), Is.EqualTo(PumpSoundEvent));
                Assert.That(Sampled(animation.SampleEvents(0.7f, 0.9f)), Is.EqualTo(ClipStartEvents), "looping playback wraps around the end of the clip");
                Assert.That(Sampled(animation.SampleEvents(0f, 1.5f)), Has.Length.EqualTo(4), "a range longer than the clip fires everything once");
                Assert.That(Sampled(animation.SampleEvents(0.4f, 0.2f)), Is.Empty, "a restart does not re-fire events");
                Assert.That(Sampled(animation.SampleEvents(1f, 1f)), Is.Empty, "a paused clip does not re-fire the events it is sitting on");
                Assert.That(Sampled(animation.SampleEvents(0f, 0f)), Is.Empty);
                Assert.That(Sampled(animation.SampleEvents(0.3f, 0.35f, finished: true)), Is.EqualTo(ShellCasingEvent),
                    "the last update of a non looping clip fires the events up to its end");
            }
        }

        [Test]
        public void SampledEventsFireOncePerLoop()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            const int Loops = 5;
            const float TimeStep = 1 / 60f;

            var fireCounts = new Dictionary<NmClipEvent, int>();

            // Playback time keeps growing while looping, exactly like the animation player advances a clip.
            // The step ending on the final loop point fires the events at time zero again, so it is left out.
            for (var time = 0f; time + TimeStep <= animation.Duration * Loops; time += TimeStep)
            {
                foreach (var clipEvent in animation.SampleEvents(time, time + TimeStep))
                {
                    fireCounts.TryGetValue(clipEvent, out var count);
                    fireCounts[clipEvent] = count + 1;
                }
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(fireCounts, Has.Count.EqualTo(animation.Events.Length), "every event fires");
                Assert.That(fireCounts.Values, Is.All.EqualTo(Loops), "no event is skipped or fired twice on the loop point");
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

using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests.Renderer
{
    public class SampledAnimationEventsTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", name);

        private static ClipAnimation LoadNovaShootAnimation(Resource resource)
        {
            resource.Read(FilePath("shoot1_nova.vnmclip_c"));

            return new ClipAnimation((AnimationClip)resource.DataBlock!);
        }

        private static SampledAnimationEvents<NmClipEvent> Sample(ClipAnimation animation, float previousTime, float newTime, bool finished = false)
            => new(animation.Events, animation.Duration, previousTime, newTime, finished);

        // Event start times of the nova shoot clip, which is 0.8 seconds long
        private static readonly float[] ClipStartEvents = [0f, 0f];
        private static readonly float[] PumpSoundEvent = [0.2333333f];
        private static readonly float[] ShellCasingEvent = [0.3333333f];

        [Test]
        public void EventsAreCrossedInPlaybackRange()
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
                Assert.That(Sampled(Sample(animation, 0f, 0.1f)), Is.EqualTo(ClipStartEvents), "events at the very start fire on the first update");
                Assert.That(Sampled(Sample(animation, 0.1f, 0.2f)), Is.Empty);
                Assert.That(Sampled(Sample(animation, 0.2f, 0.3f)), Is.EqualTo(PumpSoundEvent));
                Assert.That(Sampled(Sample(animation, 0.7f, 0.9f)), Is.EqualTo(ClipStartEvents), "looping playback wraps around the end of the clip");
                Assert.That(Sampled(Sample(animation, 0f, 1.5f)), Has.Length.EqualTo(4), "a range longer than the clip fires everything once");
                Assert.That(Sampled(Sample(animation, 0.4f, 0.2f)), Is.Empty, "a restart does not re-fire events");
                Assert.That(Sampled(Sample(animation, 1f, 1f)), Is.Empty, "a paused clip does not re-fire the events it is sitting on");
                Assert.That(Sampled(Sample(animation, 0f, 0f)), Is.Empty);
                Assert.That(Sampled(Sample(animation, 0.3f, 0.35f, finished: true)), Is.EqualTo(ShellCasingEvent),
                    "the last update of a non looping clip fires the events up to its end");
            }
        }

        [Test]
        public void EventsFireOncePerLoop()
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
                foreach (var clipEvent in Sample(animation, time, time + TimeStep))
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
        public void SamplingDoesNotAllocate()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            // Sequence events are a struct, so they are sampled through their own array rather than
            // an interface one, which would box every event handed out
            var sequenceEvents = new AnimationEvent[4];

            static float SampleBoth(ClipAnimation animation, AnimationEvent[] sequenceEvents, float time)
            {
                var sum = 0f;

                foreach (var clipEvent in Sample(animation, time, time + 0.05f))
                {
                    sum += clipEvent.StartTime;
                }

                foreach (var sequenceEvent in new SampledAnimationEvents<AnimationEvent>(sequenceEvents, 1f, time, time + 0.05f, false))
                {
                    sum += sequenceEvent.StartCycle;
                }

                return sum;
            }

            SampleBoth(animation, sequenceEvents, 0f);

            var before = GC.GetAllocatedBytesForCurrentThread();

            for (var time = 0f; time < animation.Duration * 2f; time += 0.05f)
            {
                SampleBoth(animation, sequenceEvents, time);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero, "sampling runs every frame, it must not allocate");
        }

        [Test]
        public void PlayerFiresClipEvents()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            using var skeletonResource = new Resource();
            skeletonResource.Read(FilePath("ak47.vnmskel_c"));
            var skeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            var pose = new Matrix4x4[skeleton.Bones.Length];
            var player = new AnimationPlayer(skeleton, [], new Matrix4x4[skeleton.Bones.Length], pose);
            var fired = new List<NmClipEvent>();

            player.ClipEventFired += fired.Add;
            player.SetAnimation(animation, blendTime: 0f, looping: true);

            player.Update(0.1f, Matrix4x4.Identity);
            Assert.That(fired, Has.Count.EqualTo(2), "the events at the start of the clip fire on the first update");

            fired.Clear();
            player.Update(0.15f, Matrix4x4.Identity);

            Assert.That(fired.Select(e => e.StartTime), Is.EqualTo(PumpSoundEvent).Within(0.0001f));
        }
    }
}

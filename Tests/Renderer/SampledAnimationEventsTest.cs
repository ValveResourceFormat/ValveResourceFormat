using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace Tests.Renderer
{
    public class SampledAnimationEventsTest
    {

        private static ClipAnimation LoadNovaShootAnimation(Resource resource)
        {
            resource.Read(TestFixtures.Path("shoot1_nova.vnmclip_c"));

            return new ClipAnimation((AnimationClip)resource.DataBlock!);
        }

        private static SampledAnimationEvents<NmClipEvent> Sample(ClipAnimation animation, float previousTime, float newTime, bool finished = false)
            => new(animation.Events, animation.Duration, previousTime, newTime, finished);

        // Event start times of the nova shoot clip, which is 0.8 seconds long
        private static readonly float[] ClipStartEvents = [0f, 0f];
        private static readonly float[] PumpSoundEvent = [0.2333333f];
        private static readonly float[] ShellCasingEvent = [0.3333333f];

        [Test]
        public async Task EventsAreCrossedInPlaybackRange()
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

            using (Assert.Multiple())
            {
                await Assert.That(Sampled(Sample(animation, 0f, 0.1f))).IsEquivalentTo(ClipStartEvents, CollectionOrdering.Matching).Because("events at the very start fire on the first update");
                await Assert.That(Sampled(Sample(animation, 0.1f, 0.2f))).IsEmpty();
                await Assert.That(Sampled(Sample(animation, 0.2f, 0.3f))).IsEquivalentTo(PumpSoundEvent, CollectionOrdering.Matching);
                await Assert.That(Sampled(Sample(animation, 0.7f, 0.9f))).IsEquivalentTo(ClipStartEvents, CollectionOrdering.Matching).Because("looping playback wraps around the end of the clip");
                await Assert.That(Sampled(Sample(animation, 0f, 1.5f))).Count().IsEqualTo(4).Because("a range longer than the clip fires everything once");
                await Assert.That(Sampled(Sample(animation, 0.4f, 0.2f))).IsEmpty().Because("a restart does not re-fire events");
                await Assert.That(Sampled(Sample(animation, 1f, 1f))).IsEmpty().Because("a paused clip does not re-fire the events it is sitting on");
                await Assert.That(Sampled(Sample(animation, 0f, 0f))).IsEmpty();
                await Assert.That(Sampled(Sample(animation, 0.3f, 0.35f, finished: true))).IsEquivalentTo(ShellCasingEvent, CollectionOrdering.Matching).Because("the last update of a non looping clip fires the events up to its end");
            }
        }

        [Test]
        public async Task EventsFireOncePerLoop()
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

            using (Assert.Multiple())
            {
                await Assert.That(fireCounts).Count().IsEqualTo(animation.Events.Length).Because("every event fires");
                await Assert.That(fireCounts.Values).All(count => count == Loops).Because("no event is skipped or fired twice on the loop point");
            }
        }

        [Test]
        public async Task PlayerFiresClipEvents()
        {
            using var resource = new Resource();
            var animation = LoadNovaShootAnimation(resource);

            using var skeletonResource = new Resource();
            skeletonResource.Read(TestFixtures.Path("ak47.vnmskel_c"));
            var skeleton = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            var pose = new Matrix4x4[skeleton.Bones.Length];
            var player = new AnimationPlayer(skeleton, [], new Matrix4x4[skeleton.Bones.Length], pose);
            var fired = new List<NmClipEvent>();

            player.ClipEventFired += fired.Add;
            player.SetAnimation(animation, blendTime: 0f, looping: true);

            player.Update(0.1f, Matrix4x4.Identity);
            await Assert.That(fired).Count().IsEqualTo(2).Because("the events at the start of the clip fire on the first update");

            fired.Clear();
            player.Update(0.15f, Matrix4x4.Identity);

            await Assert.That(fired.Select(e => e.StartTime)).IsEquivalentTo(PumpSoundEvent, CollectionOrdering.Matching)
                .Using((a, b) => MathF.Abs(a - b) <= 0.0001f);
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Curves;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace Tests
{
    public class ChoreoTest
    {
        private static Resource ReadChoreo(string filename, out ChoreoSceneFileData scene)
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", filename);
            var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);

            var dataBlock = (ChoreoSceneFileData?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(dataBlock);

            scene = dataBlock;
            return resource;
        }
        private static async Task AssertEvents(ChoreoEvent[] events, params ChoreoEventType[] eventTypes)
        {
            var addedEvents = events.Select(ev => ev.Type).Order().ToArray();
            var requiredEvents = eventTypes.Order().ToArray();
            await Assert.That(addedEvents).Count().IsEqualTo(requiredEvents.Length);
            for (var i = 0; i < addedEvents.Length; i++)
            {
                await Assert.That(addedEvents[i]).IsEqualTo(requiredEvents[i]);
            }
        }
        private static ChoreoScene GetScene(ChoreoSceneFileData sceneList, string name)
        {
            return sceneList.Scenes.First(vcd => vcd.Name == name);
        }
        private static async Task<ChoreoActor> GetActor(ChoreoScene scene, string name, int? expectedChannels = null)
        {
            var actor = scene.Actors.First(actor => actor.Name == name);
            if (expectedChannels.HasValue)
            {
                await Assert.That(actor.Channels).Count().IsEqualTo(expectedChannels.Value);
            }
            return actor;
        }
        private static async Task<ChoreoChannel> GetChannel(ChoreoActor actor, string name, int? expectedEvents = null)
        {
            var channel = actor.Channels.First(channel => channel.Name == name);
            if (expectedEvents.HasValue)
            {
                await Assert.That(channel.Events).Count().IsEqualTo(expectedEvents.Value);
            }
            return channel;
        }
        private static async Task<ChoreoEvent> GetEvent(ChoreoEvent[] events, string name, ChoreoEventType? expectedType = null)
        {
            var ev = events.First(ev => ev.Name == name);
            if (expectedType.HasValue)
            {
                await Assert.That(ev.Type).IsEqualTo(expectedType.Value);
            }
            return ev;
        }
        private static async Task<ChoreoEvent> GetEvent(ChoreoChannel channel, string name, ChoreoEventType? expectedType = null)
        {
            return await GetEvent(channel.Events, name, expectedType);
        }
        private static async Task<ChoreoEvent> GetEvent(ChoreoScene scene, string name, ChoreoEventType? expectedType = null)
        {
            return await GetEvent(scene.Events, name, expectedType);
        }
        [Test]
        public async Task SaveTestChoreo()
        {
            using var choreo1 = ReadChoreo("test.vcdlist_c", out var choreo1List);
            var choreoExtract = new ChoreoExtract(choreo1);
            var contentFile = choreoExtract.ToContentFile();

            foreach (var item in contentFile.SubFiles)
            {
                await Assert.That(item.Extract?.Invoke()).Count().IsGreaterThan(1);
            }
            await Assert.That(contentFile.Data).Count().IsGreaterThan(1);

            using var textWriter = new IndentedTextWriter();
            choreo1List.WriteText(textWriter);
            var vcdListText = textWriter.ToString();
            await Assert.That(vcdListText).StartsWith("allevents.vcd");
            await Assert.That(vcdListText).EndsWith("\n");
        }
        [Test]
        public async Task LoadTestChoreoVersion8()
        {
            using var choreoResource = ReadChoreo("dev_zoo.vcdlist_c", out var choreoList);
            await Assert.That(choreoList.Scenes).Count().IsEqualTo(28);

            var vcd = GetScene(choreoList, "dev/zoo/choreozoo_moveto_pausepoint.vcd");
            using (Assert.Multiple())
            {
                await Assert.That(vcd.Version).IsEqualTo((byte)8);
                await Assert.That(vcd.HasSounds).IsFalse();
                await Assert.That(vcd.Actors).Count().IsEqualTo(1);
                await Assert.That(vcd.IgnorePhonemes).IsFalse();
            }
            await AssertEvents(vcd.Events, ChoreoEventType.Section, ChoreoEventType.Section, ChoreoEventType.Loop);

            var target1Actor = await GetActor(vcd, "!Target1", 7);

            var moveChannel = await GetChannel(target1Actor, "Move", 2);
            await AssertEvents(moveChannel.Events, ChoreoEventType.MoveTo, ChoreoEventType.MoveTo);

            var lookChannel = await GetChannel(target1Actor, "LookAt", 1);
            await AssertEvents(lookChannel.Events, ChoreoEventType.LookAt);
            var lookAtEvent = await GetEvent(lookChannel, "Look at !self", ChoreoEventType.LookAt);
            using (Assert.Multiple())
            {
                await Assert.That(lookAtEvent.Param1).IsEqualTo("!self");
                await Assert.That(lookAtEvent.Param2).IsEmpty();
                await Assert.That(lookAtEvent.Param3).IsEmpty();
                await Assert.That(lookAtEvent.StartTime).IsZero();
                await Assert.That(lookAtEvent.EndTime).IsEqualTo(6.620370f);
                await Assert.That(lookAtEvent.SoundStartDelay).IsZero();
                await Assert.That(lookAtEvent.Id).IsEqualTo(6);
            }
        }
        [Test]
        public async Task LoadTestChoreoVersion17()
        {
            using var choreoResource = ReadChoreo("test.vcdlist_c", out var choreoList);
            await Assert.That(choreoList.Scenes).Count().IsEqualTo(1);

            var vcd = GetScene(choreoList, "allevents.vcd");
            using (Assert.Multiple())
            {
                await Assert.That(vcd.Version).IsEqualTo((byte)17);
                await Assert.That(vcd.HasSounds).IsTrue();
                await Assert.That(vcd.Actors).Count().IsEqualTo(2);
                await Assert.That(vcd.IgnorePhonemes).IsTrue();
            }

            await AssertEvents(vcd.Events, ChoreoEventType.Loop, ChoreoEventType.StopPoint);
            var loopEvent = await GetEvent(vcd, "loop", ChoreoEventType.Loop);
            using (Assert.Multiple())
            {
                await Assert.That(loopEvent.LoopCount).IsEqualTo((byte)255);
                await Assert.That(loopEvent.Param1).IsEqualTo("0.1");
                await Assert.That(loopEvent.StartTime).IsEqualTo(5.088889f);
                await Assert.That(loopEvent.EndTime).IsEqualTo(-1f);
            }

            var actor1 = await GetActor(vcd, "actor 1", 1);

            var actor1Channel1 = await GetChannel(actor1, "channel 1");
            await AssertEvents(actor1Channel1.Events, ChoreoEventType.Expression, ChoreoEventType.Speak);

            var actor2 = await GetActor(vcd, "actor 2", 2);

            var actor2Channel1 = await GetChannel(actor2, "channel 1");
            await AssertEvents(actor2Channel1.Events,
                ChoreoEventType.Expression,
                ChoreoEventType.Speak);

            var actor2Channel2 = await GetChannel(actor2, "channel 2");
            await AssertEvents(actor2Channel2.Events,
                ChoreoEventType.Gesture,
                ChoreoEventType.Gesture,
                ChoreoEventType.LookAt,
                ChoreoEventType.Face,
                ChoreoEventType.FireTrigger,
                ChoreoEventType.Generic,
                ChoreoEventType.Sequence,
                ChoreoEventType.AnimgraphController,
                ChoreoEventType.IKLockLeftArm,
                ChoreoEventType.IKLockRightArm,
                ChoreoEventType.SubScene,
                ChoreoEventType.Interrupt,
                ChoreoEventType.PermitResponses,
                ChoreoEventType.Script,
                ChoreoEventType.FlexAnimation,
                ChoreoEventType.MoodBody,
                ChoreoEventType.NoBlink,
                ChoreoEventType.HolsterWeapon,
                ChoreoEventType.UnholsterWeapon,
                ChoreoEventType.AimAt,
                ChoreoEventType.IgnoreCollision,
                ChoreoEventType.IgnoreLookAts);

            var flexEvent = await GetEvent(actor2Channel2, "flex animation event", ChoreoEventType.FlexAnimation);

            Debug.Assert(flexEvent.Ramp.LeftEdge != null);
            Debug.Assert(flexEvent.Ramp.RightEdge != null);

            using (Assert.Multiple())
            {
                await Assert.That(flexEvent.Ramp.Samples).Count().IsEqualTo(4);
                await Assert.That(flexEvent.ConstrainedEventId).IsEqualTo(19);

                await Assert.That(flexEvent.Ramp.LeftEdge.CurveType.InTypeName).IsEqualTo("linear_interp");
                await Assert.That(flexEvent.Ramp.LeftEdge.CurveType.OutTypeName).IsEqualTo("kochanek");
                await Assert.That(flexEvent.Ramp.LeftEdge.ZeroValue).IsEqualTo(0.1f);

                await Assert.That(flexEvent.Ramp.RightEdge.CurveType.InTypeName).IsEqualTo("simple_cubic");
                await Assert.That(flexEvent.Ramp.RightEdge.CurveType.OutTypeName).IsEqualTo("catmullrom_tangent");
                await Assert.That(flexEvent.Ramp.RightEdge.ZeroValue).IsEqualTo(0.2f);
                await Assert.That(flexEvent.FlexTimingTags).Count().IsEqualTo(1);
                await Assert.That(flexEvent.FlexTimingTags[0].Name).IsEqualTo("flex timing tag");
                await Assert.That(flexEvent.FlexTimingTags[0].Fraction).IsEqualTo(0.5f).Within(0.01f);
                await Assert.That(flexEvent.RelativeTags).Count().IsEqualTo(1);
                await Assert.That(flexEvent.RelativeTags[0].Name).IsEqualTo("relative tag");
                await Assert.That(flexEvent.RelativeTags[0].Fraction).IsEqualTo(0.25f).Within(0.01f);
                await Assert.That(flexEvent.PlaybackTimeTags).Count().IsEqualTo(1);
                await Assert.That(flexEvent.PlaybackTimeTags[0].Name).IsEqualTo("playback tag");
                await Assert.That(flexEvent.PlaybackTimeTags[0].Fraction).IsEqualTo(1f).Within(0.01f);
                await Assert.That(flexEvent.ShiftedTimeTags).Count().IsEqualTo(1);
                await Assert.That(flexEvent.ShiftedTimeTags[0].Name).IsEqualTo("shifted tag");
                await Assert.That(flexEvent.ShiftedTimeTags[0].Fraction).IsEqualTo(2.5f).Within(0.01f);
            }

            var flexTrack = flexEvent.EventFlex.Tracks.First();

            Debug.Assert(flexTrack.Ramp.LeftEdge != null);
            Debug.Assert(flexTrack.Ramp.RightEdge != null);
            Debug.Assert(flexTrack.ComboRamp != null);
            Debug.Assert(flexTrack.ComboRamp.LeftEdge != null);
            Debug.Assert(flexTrack.ComboRamp.RightEdge != null);

            var leftCurve = flexTrack.Ramp.LeftEdge.CurveType;
            var rightCurve = flexTrack.Ramp.RightEdge.CurveType;
            using (Assert.Multiple())
            {
                await Assert.That(leftCurve.InTypeName).IsEqualTo("easein");
                await Assert.That(leftCurve.OutTypeName).IsEqualTo("default");
                await Assert.That(rightCurve.InTypeName).IsEqualTo("easein");
                await Assert.That(rightCurve.OutTypeName).IsEqualTo("easeout");
                await Assert.That(flexTrack.ComboRamp.LeftEdge.CurveType).IsEqualTo(leftCurve);
                await Assert.That(flexTrack.ComboRamp.RightEdge.CurveType).IsEqualTo(rightCurve);
                await Assert.That(flexTrack.Ramp.Samples).Count().IsEqualTo(3);
                await Assert.That(flexTrack.ComboRamp.Samples).Count().IsEqualTo(3);
            }

            var sceneRamp = vcd.Ramp;
            Debug.Assert(sceneRamp.LeftEdge != null);
            Debug.Assert(sceneRamp.RightEdge != null);
            using (Assert.Multiple())
            {
                await Assert.That(sceneRamp.Samples).Count().IsEqualTo(4);

                await Assert.That(sceneRamp.LeftEdge.CurveType.InTypeName).IsEqualTo("bspline");
                await Assert.That(sceneRamp.LeftEdge.CurveType.OutTypeName).IsEqualTo("exponential_decay");
                await Assert.That(sceneRamp.LeftEdge.ZeroValue).IsEqualTo(0.3f);

                await Assert.That(sceneRamp.RightEdge.CurveType.InTypeName).IsEqualTo("kochanek_early");
                await Assert.That(sceneRamp.RightEdge.CurveType.OutTypeName).IsEqualTo("hold");
                await Assert.That(sceneRamp.RightEdge.ZeroValue).IsEqualTo(0.4f);
            }

            var bezierTrack = sceneRamp.Samples[0];
            using (Assert.Multiple())
            {
                await Assert.That(bezierTrack.Curve).IsNotNull();
                await Assert.That(bezierTrack.Bezier).IsNotNull();
            }
            using (Assert.Multiple())
            {
                Debug.Assert(bezierTrack.Curve != null);
                Debug.Assert(bezierTrack.Bezier != null);
                await Assert.That(bezierTrack.Curve.Value.InTypeName).IsEqualTo("bezier");
                await Assert.That(bezierTrack.Curve.Value.OutTypeName).IsEqualTo("bezier");
                await Assert.That(bezierTrack.Bezier.Value.Flags).IsEqualTo(BezierFlags.Unified);
                await Assert.That(bezierTrack.Bezier.Value.InWeight).IsEqualTo(0.1f);
                await Assert.That(bezierTrack.Bezier.Value.InDegrees).IsEqualTo(180f);
                await Assert.That(bezierTrack.Bezier.Value.OutWeight).IsEqualTo(0.1f);
                await Assert.That(bezierTrack.Bezier.Value.OutDegrees).IsZero();
            }
        }
    }
}

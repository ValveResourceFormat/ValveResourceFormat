using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
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
            var file = Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", filename);
            var resource = new Resource
            {
                FileName = file,
            };
            resource.Read(file);
            scene = (ChoreoSceneFileData)resource.DataBlock;
            return resource;
        }
        private static void AssertEvents(ChoreoEvent[] events, params ChoreoEventType[] eventTypes)
        {
            var addedEvents = events.Select(ev => ev.Type).Order().ToArray();
            var requiredEvents = eventTypes.Order().ToArray();
            Assert.That(addedEvents, Has.Length.EqualTo(requiredEvents.Length));
            for (var i = 0; i < addedEvents.Length; i++)
            {
                Assert.That(addedEvents[i], Is.EqualTo(requiredEvents[i]));
            }
        }
        private static ChoreoScene GetScene(ChoreoSceneFileData sceneList, string name)
        {
            return sceneList.Scenes.First(vcd => vcd.Name == name);
        }
        private static ChoreoActor GetActor(ChoreoScene scene, string name, int? expectedChannels = null)
        {
            var actor = scene.Actors.First(actor => actor.Name == name);
            if (expectedChannels.HasValue)
            {
                Assert.That(actor.Channels, Has.Length.EqualTo(expectedChannels.Value));
            }
            return actor;
        }
        private static ChoreoChannel GetChannel(ChoreoActor actor, string name, int? expectedEvents = null)
        {
            var channel = actor.Channels.First(channel => channel.Name == name);
            if (expectedEvents.HasValue)
            {
                Assert.That(channel.Events, Has.Length.EqualTo(expectedEvents.Value));
            }
            return channel;
        }
        private static ChoreoEvent GetEvent(ChoreoEvent[] events, string name, ChoreoEventType? expectedType = null)
        {
            var ev = events.First(ev => ev.Name == name);
            if (expectedType.HasValue)
            {
                Assert.That(ev.Type, Is.EqualTo(expectedType.Value));
            }
            return ev;
        }
        private static ChoreoEvent GetEvent(ChoreoChannel channel, string name, ChoreoEventType? expectedType = null)
        {
            return GetEvent(channel.Events, name, expectedType);
        }
        private static ChoreoEvent GetEvent(ChoreoScene scene, string name, ChoreoEventType? expectedType = null)
        {
            return GetEvent(scene.Events, name, expectedType);
        }
        [Test]
        public void SaveTestChoreo()
        {
            using var choreo1 = ReadChoreo("test.vcdlist_c", out var choreo1List);
            var choreoExtract = new ChoreoExtract(choreo1);
            var contentFile = choreoExtract.ToContentFile();

            foreach (var item in contentFile.SubFiles)
            {
                Assert.That(item.Extract(), Has.Length.GreaterThan(1));
            }
            Assert.That(contentFile.Data, Has.Length.GreaterThan(1));

            using var textWriter = new IndentedTextWriter();
            choreo1List.WriteText(textWriter);
            var vcdListText = textWriter.ToString();
            Assert.That(vcdListText, Does.StartWith("allevents.vcd"));
            Assert.That(vcdListText, Does.EndWith("\n"));
        }
        [Test]
        public void LoadTestChoreoVersion8()
        {
            using var choreoResource = ReadChoreo("dev_zoo.vcdlist_c", out var choreoList);
            Assert.That(choreoList.Scenes, Has.Length.EqualTo(28));

            var vcd = GetScene(choreoList, "dev/zoo/choreozoo_moveto_pausepoint.vcd");
            Assert.Multiple(() =>
            {
                Assert.That(vcd.Version, Is.EqualTo(8));
                Assert.That(vcd.HasSounds, Is.False);
                Assert.That(vcd.Actors, Has.Length.EqualTo(1));
                Assert.That(vcd.IgnorePhonemes, Is.False);
            });
            AssertEvents(vcd.Events, ChoreoEventType.Section, ChoreoEventType.Section, ChoreoEventType.Loop);

            //actor
            var target1Actor = GetActor(vcd, "!Target1", 7);

            //move channel
            var moveChannel = GetChannel(target1Actor, "Move", 2);
            AssertEvents(moveChannel.Events, ChoreoEventType.MoveTo, ChoreoEventType.MoveTo);

            //look channel
            var lookChannel = GetChannel(target1Actor, "LookAt", 1);
            AssertEvents(lookChannel.Events, ChoreoEventType.LookAt);
            var lookAtEvent = GetEvent(lookChannel, "Look at !self", ChoreoEventType.LookAt);
            Assert.Multiple(() =>
            {
                Assert.That(lookAtEvent.Param1, Is.EqualTo("!self"));
                Assert.That(lookAtEvent.Param2, Is.Empty);
                Assert.That(lookAtEvent.Param3, Is.Empty);
                Assert.That(lookAtEvent.StartTime, Is.EqualTo(0f));
                Assert.That(lookAtEvent.EndTime, Is.EqualTo(6.620370f));
                Assert.That(lookAtEvent.SoundStartDelay, Is.EqualTo(0f));
                Assert.That(lookAtEvent.Id, Is.EqualTo(6));
            });
        }
        [Test]
        public void LoadTestChoreoVersion17()
        {
            using var choreoResource = ReadChoreo("test.vcdlist_c", out var choreoList);
            Assert.That(choreoList.Scenes, Has.Length.EqualTo(1));

            var vcd = GetScene(choreoList, "allevents.vcd");
            Assert.Multiple(() =>
            {
                Assert.That(vcd.Version, Is.EqualTo(17));
                Assert.That(vcd.HasSounds, Is.True);
                Assert.That(vcd.Actors, Has.Length.EqualTo(2));
                Assert.That(vcd.IgnorePhonemes, Is.True);
            });

            //scene events
            AssertEvents(vcd.Events, ChoreoEventType.Loop, ChoreoEventType.StopPoint);
            var loopEvent = GetEvent(vcd, "loop", ChoreoEventType.Loop);
            Assert.Multiple(() =>
            {
                Assert.That(loopEvent.LoopCount, Is.EqualTo(255));
                Assert.That(loopEvent.Param1, Is.EqualTo("0.1"));
                Assert.That(loopEvent.StartTime, Is.EqualTo(5.088889f));
                Assert.That(loopEvent.EndTime, Is.EqualTo(-1f));
            });

            //actor 1
            var actor1 = GetActor(vcd, "actor 1", 1);

            var actor1Channel1 = GetChannel(actor1, "channel 1");
            AssertEvents(actor1Channel1.Events, ChoreoEventType.Expression, ChoreoEventType.Speak);

            //actor 2
            var actor2 = GetActor(vcd, "actor 2", 2);

            var actor2Channel1 = GetChannel(actor2, "channel 1");
            AssertEvents(actor2Channel1.Events,
                ChoreoEventType.Expression,
                ChoreoEventType.Speak);

            var actor2Channel2 = GetChannel(actor2, "channel 2");
            AssertEvents(actor2Channel2.Events,
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

            //flex animation event
            var flexEvent = GetEvent(actor2Channel2, "flex animation event", ChoreoEventType.FlexAnimation);
            Assert.Multiple(() =>
            {
                Assert.That(flexEvent.Ramp.Samples, Has.Length.EqualTo(4));
                Assert.That(flexEvent.ConstrainedEventId, Is.EqualTo(19));

                Assert.That(flexEvent.Ramp.LeftEdge.CurveType.InTypeName, Is.EqualTo("linear_interp"));
                Assert.That(flexEvent.Ramp.LeftEdge.CurveType.OutTypeName, Is.EqualTo("kochanek"));
                Assert.That(flexEvent.Ramp.LeftEdge.ZeroValue, Is.EqualTo(0.1f));

                Assert.That(flexEvent.Ramp.RightEdge.CurveType.InTypeName, Is.EqualTo("simple_cubic"));
                Assert.That(flexEvent.Ramp.RightEdge.CurveType.OutTypeName, Is.EqualTo("catmullrom_tangent"));
                Assert.That(flexEvent.Ramp.RightEdge.ZeroValue, Is.EqualTo(0.2f));

                Assert.That(flexEvent.FlexTimingTags, Has.Length.EqualTo(1));
                Assert.That(flexEvent.FlexTimingTags[0].Name, Is.EqualTo("flex timing tag"));
                Assert.That(flexEvent.FlexTimingTags[0].Fraction, Is.EqualTo(0.5f).Within(0.01f));

                Assert.That(flexEvent.RelativeTags, Has.Length.EqualTo(1));
                Assert.That(flexEvent.RelativeTags[0].Name, Is.EqualTo("relative tag"));
                Assert.That(flexEvent.RelativeTags[0].Fraction, Is.EqualTo(0.25f).Within(0.01f));

                Assert.That(flexEvent.PlaybackTimeTags, Has.Length.EqualTo(1));
                Assert.That(flexEvent.PlaybackTimeTags[0].Name, Is.EqualTo("playback tag"));
                Assert.That(flexEvent.PlaybackTimeTags[0].Fraction, Is.EqualTo(1f).Within(0.01f));

                Assert.That(flexEvent.ShiftedTimeTags, Has.Length.EqualTo(1));
                Assert.That(flexEvent.ShiftedTimeTags[0].Name, Is.EqualTo("shifted tag"));
                Assert.That(flexEvent.ShiftedTimeTags[0].Fraction, Is.EqualTo(2.5f).Within(0.01f));
            });
            var flexTrack = flexEvent.EventFlex.Tracks.First();
            var leftCurve = flexTrack.Ramp.LeftEdge.CurveType;
            var rightCurve = flexTrack.Ramp.RightEdge.CurveType;
            Assert.Multiple(() =>
            {
                Assert.That(leftCurve.InTypeName, Is.EqualTo("easein"));
                Assert.That(leftCurve.OutTypeName, Is.EqualTo("default"));
                Assert.That(rightCurve.InTypeName, Is.EqualTo("easein"));
                Assert.That(rightCurve.OutTypeName, Is.EqualTo("easeout"));
                Assert.That(flexTrack.ComboRamp.LeftEdge.CurveType, Is.EqualTo(leftCurve));
                Assert.That(flexTrack.ComboRamp.RightEdge.CurveType, Is.EqualTo(rightCurve));
                Assert.That(flexTrack.Ramp.Samples, Has.Length.EqualTo(3));
                Assert.That(flexTrack.ComboRamp.Samples, Has.Length.EqualTo(3));
            });

            //scene ramp
            var sceneRamp = vcd.Ramp;
            Assert.Multiple(() =>
            {
                Assert.That(sceneRamp.Samples, Has.Length.EqualTo(4));

                Assert.That(sceneRamp.LeftEdge.CurveType.InTypeName, Is.EqualTo("bspline"));
                Assert.That(sceneRamp.LeftEdge.CurveType.OutTypeName, Is.EqualTo("exponential_decay"));
                Assert.That(sceneRamp.LeftEdge.ZeroValue, Is.EqualTo(0.3f));

                Assert.That(sceneRamp.RightEdge.CurveType.InTypeName, Is.EqualTo("kochanek_early"));
                Assert.That(sceneRamp.RightEdge.CurveType.OutTypeName, Is.EqualTo("hold"));
                Assert.That(sceneRamp.RightEdge.ZeroValue, Is.EqualTo(0.4f));
            });

            var bezierTrack = sceneRamp.Samples[0];
            Assert.Multiple(() =>
            {
                Assert.That(bezierTrack.Curve, Is.Not.Null);
                Assert.That(bezierTrack.Bezier, Is.Not.Null);
            });
            Assert.Multiple(() =>
            {
                Debug.Assert(bezierTrack.Curve != null);
                Debug.Assert(bezierTrack.Bezier != null);
                Assert.That(bezierTrack.Curve.Value.InTypeName, Is.EqualTo("bezier"));
                Assert.That(bezierTrack.Curve.Value.OutTypeName, Is.EqualTo("bezier"));
                Assert.That(bezierTrack.Bezier.Value.Flags, Is.EqualTo(BezierFlags.Unified));
                Assert.That(bezierTrack.Bezier.Value.InWeight, Is.EqualTo(0.1f));
                Assert.That(bezierTrack.Bezier.Value.InDegrees, Is.EqualTo(180f));
                Assert.That(bezierTrack.Bezier.Value.OutWeight, Is.EqualTo(0.1f));
                Assert.That(bezierTrack.Bezier.Value.OutDegrees, Is.EqualTo(0f));
            });
        }
    }
}

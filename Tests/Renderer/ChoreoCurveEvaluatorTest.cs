using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace Tests.Renderer
{
    public class ChoreoCurveEvaluatorTest
    {
        private const CurveInterpolator EaseIn = CurveInterpolator.EaseIn;
        private const CurveInterpolator Linear = CurveInterpolator.Linear;
        private const CurveInterpolator Hold = CurveInterpolator.Hold;
        private const float Tolerance = 1e-4f;

        private static ChoreoSample Sample(float time, float value, CurveInterpolator inType, CurveInterpolator outType)
        {
            var sample = new ChoreoSample(time, value);
            sample.SetCurveType(inType, outType);
            return sample;
        }

        private static ChoreoCurveData Curve(params ChoreoSample[] samples) => new(samples, null, null);

        private static ChoreoEvent Gesture(ChoreoTag[] playback, ChoreoTag[] shifted) => new()
        {
            Name = "gesture",
            Param1 = string.Empty,
            Param2 = string.Empty,
            Param3 = string.Empty,
            Ramp = Curve(),
            RelativeTags = [],
            FlexTimingTags = [],
            PlaybackTimeTags = playback,
            ShiftedTimeTags = shifted,
            EventFlex = new ChoreoEventFlex([]),
        };

        [Test]
        public async Task CurveWithoutSamplesIsFull()
        {
            await Assert.That(ChoreoCurveEvaluator.Evaluate(Curve(), 0.5f, 1f, 0f)).IsEqualTo(1f);
        }

        [Test]
        public async Task TimeOutsideOfTheCurveIsZero()
        {
            var curve = Curve(Sample(0f, 1f, Linear, Linear), Sample(1f, 1f, Linear, Linear));

            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, -0.1f, 1f, 0f)).IsEqualTo(0f);
            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, 1.1f, 1f, 0f)).IsEqualTo(0f);
        }

        [Test]
        public async Task LinearSegmentInterpolates()
        {
            var curve = Curve(Sample(0f, 0f, Linear, Linear), Sample(1f, 1f, Linear, Linear));

            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, 0.25f, 1f, 0f)).IsEqualTo(0.25f).Within(Tolerance);
        }

        [Test]
        public async Task HoldKeepsTheStartOfTheSegment()
        {
            var curve = Curve(Sample(0f, 0.25f, Linear, Hold), Sample(1f, 1f, Linear, Linear));

            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, 0.9f, 1f, 0f)).IsEqualTo(0.25f).Within(Tolerance);
        }

        [Test]
        public async Task SegmentEndsWithDifferentInterpolatorsBlendAcrossIt()
        {
            var curve = Curve(Sample(0f, 0f, Linear, EaseIn), Sample(1f, 1f, Linear, Linear));
            var easeIn = MathF.Sin(MathF.PI * 0.25f);

            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, 0.5f, 1f, 0f)).IsEqualTo(float.Lerp(easeIn, 0.5f, 0.5f)).Within(Tolerance);
        }

        [Test]
        public async Task EdgesUseTheDefaultZeroValue()
        {
            var curve = Curve(Sample(0.5f, 1f, Linear, Linear));

            await Assert.That(ChoreoCurveEvaluator.Evaluate(curve, 1f, 1f, 0.5f)).IsEqualTo(0.5f).Within(Tolerance);
        }

        [Test]
        public async Task GestureWithoutMatchingTagsRunsStraightThrough()
        {
            var gesture = Gesture([new ChoreoTag("apex", 0.5f)], []);

            await Assert.That(ChoreoCurveEvaluator.GetGestureCycle(gesture, 0.3f)).IsEqualTo(0.3f);
        }

        [Test]
        public async Task GestureIsLinearBetweenLockedTags()
        {
            var gesture = Gesture(
                [new ChoreoTag("loop", 0.25f), new ChoreoTag("end", 0.75f)],
                [new ChoreoTag("loop", 0.5f), new ChoreoTag("end", 0.6f)]);

            await Assert.That(ChoreoCurveEvaluator.GetGestureCycle(gesture, 0.5f)).IsEqualTo(0.55f).Within(Tolerance);
        }

        [Test]
        public async Task GesturePassesThroughItsTags()
        {
            var gesture = Gesture([new ChoreoTag("apex", 0.5f)], [new ChoreoTag("apex", 0.2f)]);

            await Assert.That(ChoreoCurveEvaluator.GetGestureCycle(gesture, 0.5f)).IsEqualTo(0.2f).Within(Tolerance);
        }

        [Test]
        [Arguments("techies", "models/heroes/techies/techies.vmdl")]
        [Arguments("Eli", "models/characters/eli/eli_end.vmdl")]
        [Arguments("hero_ghost", "models/heroes/ghost/ghost.vmdl")]
        [Arguments("gman_2", "models/characters/gman/gman.vmdl")]
        public async Task NameOfModelMatches(string name, string model)
        {
            await Assert.That(ChoreoActorMatcher.IsNameOfModel(name, model)).IsTrue();
        }

        [Test]
        [Arguments("elias", "models/characters/eli/eli.vmdl")]
        [Arguments("!target1", "models/characters/eli/eli.vmdl")]
        public async Task NameOfOtherModelDoesNotMatch(string name, string model)
        {
            await Assert.That(ChoreoActorMatcher.IsNameOfModel(name, model)).IsFalse();
        }

        [Test]
        public async Task FindModelPrefersTheModelNamedAfterItsFolder()
        {
            var scene = new ChoreoScene(0, [], [new ChoreoActor("!target1", [], true), new ChoreoActor("ogre_magi", [], true)], Curve(), false);
            string[] models =
            [
                "models/items/ogre_magi/a.vmdl_c",
                "models/heroes/ogre_magi/ogre_magi.vmdl_c",
                "models/heroes/lina/lina.vmdl_c",
            ];

            var match = ChoreoActorMatcher.FindModel(scene, models);

            await Assert.That(match?.Model).IsEqualTo("models/heroes/ogre_magi/ogre_magi.vmdl_c");
            await Assert.That(match?.Actor.Name).IsEqualTo("ogre_magi");
        }

        [Test]
        public async Task FindModelFallsBackToTheFolderOfTheScene()
        {
            var scene = new ChoreoScene(0, [], [new ChoreoActor("hero", [], true)], Curve(), false) { Name = "riki/riki_laugh.vcd" };

            var match = ChoreoActorMatcher.FindModel(scene, ["models/heroes/riki/riki.vmdl_c"]);

            await Assert.That(match?.Actor.Name).IsEqualTo("hero");
        }
    }
}

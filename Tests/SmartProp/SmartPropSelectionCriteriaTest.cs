using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropSelectionCriteriaTest
    {
        private static SmartPropEvaluationContext Context() => new();

        private static KVObject Child(params KVObject[] criteria)
        {
            var child = KVObject.Collection();
            var list = KVObject.Array();
            foreach (var criteriaNode in criteria)
            {
                list.Add(criteriaNode);
            }

            child["m_SelectionCriteria"] = list;
            return child;
        }

        private static KVObject Criteria(string className, params (string Key, KVObject Value)[] fields)
        {
            var node = KVObject.Collection();
            node["generic_data_type"] = new KVObject($"CSmartPropSelectionCriteria_{className}");
            foreach (var (key, value) in fields)
            {
                node[key] = value;
            }

            return node;
        }

        private static KVObject Str(string value) => new(value);

        private static KVObject Bool(bool value) => new(value);

        private static KVObject Float(float value) => new(value);

        [Test]
        public async Task ChildrenWithoutCriteriaAlwaysMatch()
        {
            var context = Context();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(null, 0, 5, context)).IsTrue();

            var bare = KVObject.Collection();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(bare, 3, 5, context)).IsTrue();

            var emptyList = Child();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(emptyList, 4, 5, context)).IsTrue();
        }

        [Test]
        public async Task PathPositionAllAllowsEveryInstance()
        {
            var child = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("ALL"))));
            var context = Context();

            for (var i = 0; i < 5; i++)
            {
                await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, i, 5, context)).IsTrue();
            }
        }

        [Test]
        public async Task PathPositionStartAndEndAndControlPointsMatchOnlyCaps()
        {
            var context = Context();
            var startEnd = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("START_AND_END"))));
            var controlPoints = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("CONTROL_POINTS"))));

            for (var i = 0; i < 5; i++)
            {
                var expected = i == 0 || i == 4;
                await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startEnd, i, 5, context)).IsEqualTo(expected);
                await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(controlPoints, i, 5, context)).IsEqualTo(expected);
            }
        }

        [Test]
        public async Task PathPositionStartEndAndInternalModes()
        {
            var context = Context();
            var start = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("START"))));
            var end = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("END"))));
            var internalOnly = Child(Criteria("PathPosition", ("m_PlaceAtPositions", Str("INTERNAL"))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(start, 0, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(start, 1, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(end, 4, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(end, 3, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(internalOnly, 0, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(internalOnly, 2, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(internalOnly, 4, 5, context)).IsFalse();
        }

        [Test]
        public async Task PathPositionNthStepsAndOffsets()
        {
            var context = Context();
            var everyThird = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", Str("NTH")),
                ("m_nPlaceEveryNthPosition", Float(3f))));

            for (var i = 0; i < 7; i++)
            {
                await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(everyThird, i, 7, context))
                    .IsEqualTo(i % 3 == 0);
            }

            // A positive offset shifts the sequence; early indices before the offset fail
            var offsetTwo = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", Str("NTH")),
                ("m_nPlaceEveryNthPosition", Float(3f)),
                ("m_nNthPositionIndexOffset", Float(2f))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(offsetTwo, 0, 7, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(offsetTwo, 1, 7, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(offsetTwo, 2, 7, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(offsetTwo, 5, 7, context)).IsTrue();
        }

        [Test]
        public async Task PathPositionNumericEnumMapsToModes()
        {
            var context = Context();
            var nth = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", new KVObject(1)),
                ("m_nPlaceEveryNthPosition", Float(2f))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(nth, 0, 4, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(nth, 1, 4, context)).IsFalse();

            var startEnd = Child(Criteria("PathPosition", ("m_PlaceAtPositions", new KVObject(2))));
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startEnd, 2, 4, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startEnd, 3, 4, context)).IsTrue();
        }

        [Test]
        public async Task PathPositionBindingsResolveThroughContext()
        {
            var binding = KVObject.Collection();
            binding["m_SourceName"] = Str("position_mode");

            var child = Child(Criteria("PathPosition", ("m_PlaceAtPositions", binding)));
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["position_mode"] = "END",
            });

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 2, 4, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 3, 4, context)).IsTrue();
        }

        [Test]
        public async Task PathPositionAllowAtStartAndEndFlags()
        {
            var context = Context();
            var noStart = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", Str("ALL")),
                ("m_bAllowAtStart", Bool(false))));
            var noEnd = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", Str("ALL")),
                ("m_bAllowAtEnd", Str("false"))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(noStart, 0, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(noStart, 1, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(noEnd, 4, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(noEnd, 3, 5, context)).IsTrue();
        }

        [Test]
        public async Task EndCapMatchesOnlySelectedCaps()
        {
            var context = Context();
            var startOnly = Child(Criteria("EndCap", ("m_bStart", Bool(true))));
            var both = Child(Criteria("EndCap",
                ("m_bStart", Bool(true)),
                ("m_bEnd", Bool(true))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startOnly, 0, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startOnly, 4, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(startOnly, 2, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(both, 0, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(both, 4, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(both, 2, 5, context)).IsFalse();
        }

        [Test]
        public async Task EndCapWithoutFlagsMatchesNothing()
        {
            var child = Child(Criteria("EndCap"));
            var context = Context();

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 0, 5, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 4, 5, context)).IsFalse();
        }

        [Test]
        public async Task IsValidEvaluatesExpressionBindings()
        {
            var context = new SmartPropEvaluationContext(new Dictionary<string, object?>
            {
                ["threshold"] = 4f,
            });

            var passes = Child(Criteria("IsValid", ("m_Expression", Str("threshold > 2"))));
            var fails = Child(Criteria("IsValid", ("m_Expression", Str("threshold > 10"))));
            var zero = Child(Criteria("IsValid", ("m_Expression", Str("0"))));
            var missing = Child(Criteria("IsValid"));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(passes, 0, 3, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(fails, 0, 3, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(zero, 0, 3, context)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(missing, 0, 3, context)).IsTrue();
        }

        [Test]
        public async Task MultipleCriteriaMustAllPass()
        {
            var context = Context();
            var child = Child(
                Criteria("PathPosition", ("m_PlaceAtPositions", Str("INTERNAL"))),
                Criteria("IsValid", ("m_Expression", Str("1"))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 2, 5, context)).IsTrue();
            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 0, 5, context)).IsFalse();
        }

        [Test]
        public async Task DisabledCriteriaAreSkipped()
        {
            var context = Context();
            var child = Child(Criteria("PathPosition",
                ("m_PlaceAtPositions", Str("END")),
                ("m_bEnabled", Bool(false))));

            await Assert.That(SmartPropSelectionCriteria.MatchesSelectionCriteria(child, 1, 5, context)).IsTrue();
        }

        [Test]
        public async Task LinearLengthIsExtractedAndScalesLines()
        {
            var context = Context();
            var child = Child(Criteria("LinearLength",
                ("m_flLength", Float(100f)),
                ("m_bAllowScale", Bool(true)),
                ("m_flMinLength", Float(20f)),
                ("m_flMaxLength", Float(160f))));

            await Assert.That(SmartPropSelectionCriteria.TryGetLinearLength(child, context, out var linearLength)).IsTrue();
            await Assert.That(linearLength.Length).IsEqualTo(100f);
            await Assert.That(linearLength.AllowScale).IsTrue();
            await Assert.That(linearLength.MinLength).IsEqualTo(20f);
            await Assert.That(linearLength.MaxLength).IsEqualTo(160f);

            // Exact ratio
            await Assert.That(linearLength.ComputeScale(50f)).IsEqualTo(0.5f);
            // Clamped up to the minimum ratio 0.2
            await Assert.That(linearLength.ComputeScale(10f)).IsEqualTo(0.2f);
            // Clamped down to the maximum ratio 1.6
            await Assert.That(linearLength.ComputeScale(200f)).IsEqualTo(1.6f);
            // Non-positive lengths refuse to scale
            await Assert.That(linearLength.ComputeScale(0f)).IsEqualTo(1f);
        }

        [Test]
        public async Task LinearLengthWithoutScaleIsUnclamped()
        {
            var context = Context();
            var child = Child(Criteria("LinearLength",
                ("m_flLength", Float(100f)),
                ("m_bAllowScale", Bool(false))));

            await Assert.That(SmartPropSelectionCriteria.TryGetLinearLength(child, context, out var linearLength)).IsTrue();
            await Assert.That(linearLength.ComputeScale(400f)).IsEqualTo(4f);
            await Assert.That(linearLength.ComputeScale(25f)).IsEqualTo(0.25f);
        }

        [Test]
        public async Task LinearLengthAbsentReturnsFalse()
        {
            var context = Context();
            var child = Child(Criteria("EndCap", ("m_bStart", Bool(true))));

            await Assert.That(SmartPropSelectionCriteria.TryGetLinearLength(child, context, out _)).IsFalse();
            await Assert.That(SmartPropSelectionCriteria.TryGetLinearLength(null, context, out _)).IsFalse();
        }

        [Test]
        public async Task ChoiceWeightDefaultsToOneAndReadsValue()
        {
            var context = Context();
            var weighted = Child(Criteria("ChoiceWeight", ("m_flWeight", Float(2.5f))));

            await Assert.That(SmartPropSelectionCriteria.GetChoiceWeight(weighted, context)).IsEqualTo(2.5f);
            await Assert.That(SmartPropSelectionCriteria.GetChoiceWeight(Child(), context)).IsEqualTo(1f);
            await Assert.That(SmartPropSelectionCriteria.GetChoiceWeight(null, context)).IsEqualTo(1f);
        }
    }
}

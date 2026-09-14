using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    public class SoundEventCurveTest
    {
        private static SoundEventCurve ParseCurve(string points, bool decibels = false)
        {
            var text = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n{\n\tcurve = " + points + "\n}\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return SoundEventCurve.Parse(KVDocumentExtensions.ParseKV3(stream).Root, "curve", decibels)!;
        }

        private static SoundEventCurve LinearCurve(float x0, float y0, float x1, float y1)
            => (SoundEventCurve)typeof(SoundEventCurve)
                .GetMethod("Linear", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [x0, y0, x1, y1])!;

        private static SoundEventCurve AddCutoff(SoundEventCurve curve, float x)
            => (SoundEventCurve)typeof(SoundEventCurve)
                .GetMethod("WithCutoff", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(curve, [x])!;

        [Test]
        public async Task LinearTangentsEvaluateLinearly()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(0f)).IsEqualTo(1f);
                await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.75f);
                await Assert.That(curve.Evaluate(50f)).IsEqualTo(0.5f);
                await Assert.That(curve.Evaluate(100f)).IsEqualTo(0f);
            }
        }

        [Test]
        public async Task SplineTangentsBendTheSpan()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 0.0, 1.0, 1.0], [50.0, 1.0, 0.0, 0.0, 1.0, 1.0], [100.0, 0.0, 0.0, 0.0, 1.0, 1.0]]");

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.625f).Because("a linear read gives 0.5");
                await Assert.That(curve.Evaluate(75f)).IsEqualTo(0.625f).Because("a linear read gives 0.5");
                await Assert.That(curve.Evaluate(50f)).IsEqualTo(1f);
            }
        }

        [Test]
        public async Task FreeTangentsUseTheAuthoredValues()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, -0.02, 2.0, 2.0], [100.0, 0.0, -0.02, 0.0, 2.0, 2.0]]");

            await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.65625f).Because("a linear read gives 0.75");
        }

        [Test]
        public async Task MirrorTangentCopiesTheOppositeHandle()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 0.0, 0.0, 0.0], [50.0, 1.0, 0.0, -0.02, 3.0, 2.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.75f).Because("a linear read gives 0.5");
        }

        [Test]
        public async Task SineTangentsUseFixedSlopes()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 4.0, 4.0], [100.0, 0.0, 0.0, 0.0, 4.0, 4.0]]");

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.92470610f).Within(0.000001f).Because("a linear read gives 0.75");
                await Assert.That(curve.Evaluate(75f)).IsEqualTo(0.38361669f).Within(0.000001f).Because("a linear read gives 0.25");
            }
        }

        [Test]
        public async Task LinearFactoryRemainsLinear()
        {
            var curve = LinearCurve(0f, 1f, 100f, 0f);
            var reversed = LinearCurve(100f, 0f, 0f, 1f);

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(25f)).IsEqualTo(0.75f);
                await Assert.That(reversed.Evaluate(25f)).IsEqualTo(0.75f);
            }
        }

        [Test]
        public async Task CutoffAddsALinearFinalSpan()
        {
            var curve = AddCutoff(
                ParseCurve("[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], [100.0, 0.5, 0.0, 0.0, 0.0, 0.0]]"),
                200f);

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(125f)).IsEqualTo(0.375f);
                await Assert.That(curve.Evaluate(200f)).IsEqualTo(0f);
            }
        }

        [Test]
        public async Task DecibelCurveKeepsLegacyLinearInterpolationUntilTangentUnitsAreProved()
        {
            var curve = ParseCurve(
                "[[0.0, 0.0, 8.0, -8.0, 2.0, 2.0], [100.0, -6.0, -8.0, 8.0, 2.0, 2.0]]",
                decibels: true);
            var end = MathF.Pow(10f, -6f / 20f);

            await Assert.That(curve.Evaluate(25f)).IsEqualTo(float.Lerp(1f, end, 0.25f));
        }

        [Test]
        public async Task FootstepDistanceCurveMatchesTheGameShape()
        {
            var curve = ParseCurve(
                "[[49.591427, 0.45, 0.010452, 0.010452, 3.0, 1.0], " +
                "[116.563492, 1.0, -0.001429, -0.001429, 2.0, 3.0], " +
                "[402.285736, 0.488971, -0.000991, -0.000991, 3.0, 1.0], " +
                "[1095.0, 0.03, -0.000701, -0.000701, 1.0, 1.0], " +
                "[1100.0, 0.0, -0.000476, -0.000476, 2.0, 3.0]]");

            await Assert.That(curve.Evaluate(300f)).IsEqualTo(0.64675820f).Within(0.000001f).Because("a linear read gives 0.6719");
        }

        [Test]
        public async Task GrenadeExplosionDistanceCurveMatchesTheGameShape()
        {
            var curve = ParseCurve(
                "[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], " +
                "[231.428574, 1.0, -0.000553, -0.000553, 1.0, 1.0], " +
                "[779.142883, 0.569231, -0.000405, -0.000405, 1.0, 1.0], " +
                "[2700.0, 0.0, -0.000096, -0.000096, 2.0, 3.0]]");

            await Assert.That(curve.Evaluate(1800f)).IsEqualTo(0.19140819f).Within(0.000001f).Because("a linear read gives 0.2667");
        }

        [Test]
        public async Task EvaluateClampsToTheEndPoints()
        {
            var curve = ParseCurve("[[0.0, 1.0, 0.0, 0.0, 0.0, 0.0], [100.0, 0.0, 0.0, 0.0, 0.0, 0.0]]");

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(-10f)).IsEqualTo(1f);
                await Assert.That(curve.Evaluate(200f)).IsEqualTo(0f);
            }
        }

        [Test]
        public async Task EvaluateClampsOvershootToTheAuthoredExtent()
        {
            var curve = ParseCurve("[[0.0, 0.0, 0.0, 5.0, 2.0, 2.0], [10.0, 1.0, -5.0, 0.0, 2.0, 2.0]]");

            await Assert.That(curve.Evaluate(5f)).IsEqualTo(1f);
        }

        [Test]
        public async Task SingleKnotEvaluatesToItsValue()
        {
            var curve = ParseCurve("[[50.0, 0.75, 0.0, 0.0, 0.0, 0.0]]");

            using (Assert.Multiple())
            {
                await Assert.That(curve.Evaluate(0f)).IsEqualTo(0.75f);
                await Assert.That(curve.Evaluate(50f)).IsEqualTo(0.75f);
                await Assert.That(curve.Evaluate(100f)).IsEqualTo(0.75f);
            }
        }
    }
}

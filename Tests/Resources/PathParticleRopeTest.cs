using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Resources
{
    public class PathParticleRopeTest
    {
        private const string RealPathNodes = @"
[
    [
        0.0, 0.0, 0.0, 0.0,
        0.0, 0.0, 92.189651, 142.793289,
        60.658333,
    ],
    [
        276.56897, 428.379883, 181.975006, -92.189651,
        -142.793289, -60.658333, 166.666656, -252.499969,
        -42.333328,
    ],
]";

        [Test]
        public async Task ParsesNodesIntoPositionAndTangents()
        {
            var nodes = PathParticleRope.ParseNodes(RealPathNodes);

            await Assert.That(nodes).Count().IsEqualTo(2);

            await Assert.That(nodes[0].Position).IsEqualTo(Vector3.Zero);
            await Assert.That(nodes[0].InTangent).IsEqualTo(Vector3.Zero);
            await Assert.That(nodes[0].OutTangent.X).IsEqualTo(92.189651f).Within(1e-3f);
            await Assert.That(nodes[0].OutTangent.Z).IsEqualTo(60.658333f).Within(1e-3f);

            await Assert.That(nodes[1].Position.X).IsEqualTo(276.56897f).Within(1e-3f);
            await Assert.That(nodes[1].InTangent).IsEqualTo(-nodes[0].OutTangent);
        }

        [Test]
        public async Task EmptyAndDegenerateValuesParseToNoNodes()
        {
            await Assert.That(PathParticleRope.ParseNodes("[  ]")).IsEmpty();
            await Assert.That(PathParticleRope.ParseNodes("")).IsEmpty();
            await Assert.That(PathParticleRope.ParseNodes(null)).IsEmpty();
            await Assert.That(PathParticleRope.ParseNodes("[ [ 1, 2")).IsEmpty();
        }

        [Test]
        public async Task ShortNodeIsPaddedAndDoesNotShiftLaterNodes()
        {
            var nodes = PathParticleRope.ParseNodes("[ [ 1, 2, 3, 4, 5 ], [ 0, 0, 0, 0, 0, 0, 7, 8, 9, 10 ], null ]");

            await Assert.That(nodes).Count().IsEqualTo(3);
            await Assert.That(nodes[0].InTangent).IsEqualTo(new Vector3(4, 5, 0));
            await Assert.That(nodes[0].OutTangent).IsEqualTo(Vector3.Zero);
            await Assert.That(nodes[1].OutTangent).IsEqualTo(new Vector3(7, 8, 9));
            await Assert.That(nodes[2].Position).IsEqualTo(Vector3.Zero);
        }

        [Test]
        public async Task RadiusScalesAreClampedToNodeCountAndDefaultToOne()
        {
            float[] expected = [1.4f, 0f, 1f];
            await Assert.That(PathParticleRope.ParseRadiusScales("[ 1.4, \"bad\", ]", 3)).IsEquivalentTo(expected, EqualityComparer<float>.Default, CollectionOrdering.Matching);
            await Assert.That(PathParticleRope.ParseRadiusScales("[ 1, 2, 3 ]", 2)).Count().IsEqualTo(2);
        }

        [Test]
        public async Task ColorsRequireThreeComponents()
        {
            var colors = PathParticleRope.ParseColors("[ [ 0.5, 0.25, 0.125 ], [ 1.0, 1.0 ] ]", 3);

            await Assert.That(colors[0]).IsEqualTo(new Vector3(0.5f, 0.25f, 0.125f));
            await Assert.That(colors[1]).IsEqualTo(Vector3.Zero);
            await Assert.That(colors[2]).IsEqualTo(Vector3.One);
        }

        [Test]
        public async Task PinsAcceptBoolsAndNumbersAndDefaultToTrue()
        {
            bool[] expected = [true, false, false, true];
            await Assert.That(PathParticleRope.ParsePins("[ true, false, 0 ]", 4)).IsEquivalentTo(expected, EqualityComparer<bool>.Default, CollectionOrdering.Matching);
        }
    }
}

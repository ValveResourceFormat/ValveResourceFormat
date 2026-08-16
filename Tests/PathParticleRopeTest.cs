using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.Utils;

namespace Tests
{
    public class PathParticleRopeTest
    {
        // First two nodes of a real cs_italy path_particle_rope_clientside, triple-quoted KV3 style.
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

            // Authoritative invariant: inTangent[i] == -outTangent[i-1] (C1 continuity).
            await Assert.That(nodes[1].InTangent).IsEqualTo(-nodes[0].OutTangent);
        }

        [Test]
        public async Task EmptyAndDegenerateBlobsParseToEmpty()
        {
            await Assert.That(PathParticleRope.ParseNodes("[  ]")).IsEmpty();
            await Assert.That(PathParticleRope.ParseNodes("")).IsEmpty();
            await Assert.That(PathParticleRope.ParseNodes(null)).IsEmpty();
            await Assert.That(PathParticleRope.ParseFloatBlob("[ ]")).IsEmpty();
        }

        [Test]
        public async Task IncompleteTrailingNodeIsDropped()
        {
            // 11 floats = one full 9-float node plus 2 stragglers, which must be ignored.
            var nodes = PathParticleRope.ParseNodes("[ 0,0,0, 0,0,0, 1,2,3, 4,5 ]");
            await Assert.That(nodes).Count().IsEqualTo(1);
        }

        [Test]
        public async Task ParsesRadiusScalesWithTrailingCommas()
        {
            var scales = PathParticleRope.ParseRadiusScales("[ 1.4, 1.0, 2.0, ]");
            float[] expected = [1.4f, 1.0f, 2.0f];
            await Assert.That(scales).IsEquivalentTo(expected, CollectionOrdering.Matching);
        }

        [Test]
        public async Task ParsesNestedPerNodeColors()
        {
            var colors = PathParticleRope.ParseColors("[ [ 0.109804, 0.109804, 0.117647 ], [ 1.0, 1.0, 1.0 ] ]");
            await Assert.That(colors).Count().IsEqualTo(2);
            await Assert.That(colors[0].X).IsEqualTo(0.109804f).Within(1e-5f);
            await Assert.That(colors[1]).IsEqualTo(Vector3.One);
        }

        [Test]
        public async Task ParsesPins()
        {
            bool[] expectedWords = [true, false];
            bool[] expectedDigits = [true, false, true];
            await Assert.That(PathParticleRope.ParsePins("[ true, false ]")).IsEquivalentTo(expectedWords, CollectionOrdering.Matching);
            await Assert.That(PathParticleRope.ParsePins("[ 1, 0, 1 ]")).IsEquivalentTo(expectedDigits, CollectionOrdering.Matching);
        }

        [Test]
        public async Task ParsePinsAcceptsFloatTokensAndKeepsAlignment()
        {
            // Float-encoded pins (1.0 / 0.0) must map to pinned / unpinned, not be dropped.
            bool[] expectedFloats = [true, false, true];
            await Assert.That(PathParticleRope.ParsePins("[ 1.0, 0.0, 1.0 ]")).IsEquivalentTo(expectedFloats, CollectionOrdering.Matching);

            // An unrecognized token must keep the array aligned with the node list (default pinned)
            // instead of silently shortening it and misaligning every later node.
            bool[] expectedAligned = [true, true, false];
            await Assert.That(PathParticleRope.ParsePins("[ true, nonsense, false ]")).IsEquivalentTo(expectedAligned, CollectionOrdering.Matching);
        }
    }
}

using NUnit.Framework;
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
        public void ParsesNodesIntoPositionAndTangents()
        {
            var nodes = PathParticleRope.ParseNodes(RealPathNodes);

            Assert.That(nodes, Has.Count.EqualTo(2));

            Assert.That(nodes[0].Position, Is.EqualTo(Vector3.Zero));
            Assert.That(nodes[0].InTangent, Is.EqualTo(Vector3.Zero));
            Assert.That(nodes[0].OutTangent.X, Is.EqualTo(92.189651f).Within(1e-3));
            Assert.That(nodes[0].OutTangent.Z, Is.EqualTo(60.658333f).Within(1e-3));

            Assert.That(nodes[1].Position.X, Is.EqualTo(276.56897f).Within(1e-3));

            // Authoritative invariant: inTangent[i] == -outTangent[i-1] (C1 continuity).
            Assert.That(nodes[1].InTangent, Is.EqualTo(-nodes[0].OutTangent));
        }

        [Test]
        public void EmptyAndDegenerateBlobsParseToEmpty()
        {
            Assert.That(PathParticleRope.ParseNodes("[  ]"), Is.Empty);
            Assert.That(PathParticleRope.ParseNodes(""), Is.Empty);
            Assert.That(PathParticleRope.ParseNodes(null), Is.Empty);
            Assert.That(PathParticleRope.ParseFloatBlob("[ ]"), Is.Empty);
        }

        [Test]
        public void IncompleteTrailingNodeIsDropped()
        {
            // 11 floats = one full 9-float node plus 2 stragglers, which must be ignored.
            var nodes = PathParticleRope.ParseNodes("[ 0,0,0, 0,0,0, 1,2,3, 4,5 ]");
            Assert.That(nodes, Has.Count.EqualTo(1));
        }

        [Test]
        public void ParsesRadiusScalesWithTrailingCommas()
        {
            var scales = PathParticleRope.ParseRadiusScales("[ 1.4, 1.0, 2.0, ]");
            float[] expected = [1.4f, 1.0f, 2.0f];
            Assert.That(scales, Is.EqualTo(expected));
        }

        [Test]
        public void ParsesNestedPerNodeColors()
        {
            var colors = PathParticleRope.ParseColors("[ [ 0.109804, 0.109804, 0.117647 ], [ 1.0, 1.0, 1.0 ] ]");
            Assert.That(colors, Has.Length.EqualTo(2));
            Assert.That(colors[0].X, Is.EqualTo(0.109804f).Within(1e-5));
            Assert.That(colors[1], Is.EqualTo(Vector3.One));
        }

        [Test]
        public void ParsesPins()
        {
            bool[] expectedWords = [true, false];
            bool[] expectedDigits = [true, false, true];
            Assert.That(PathParticleRope.ParsePins("[ true, false ]"), Is.EqualTo(expectedWords));
            Assert.That(PathParticleRope.ParsePins("[ 1, 0, 1 ]"), Is.EqualTo(expectedDigits));
        }

        [Test]
        public void ParsePinsAcceptsFloatTokensAndKeepsAlignment()
        {
            // Float-encoded pins (1.0 / 0.0) must map to pinned / unpinned, not be dropped.
            bool[] expectedFloats = [true, false, true];
            Assert.That(PathParticleRope.ParsePins("[ 1.0, 0.0, 1.0 ]"), Is.EqualTo(expectedFloats));

            // An unrecognized token must keep the array aligned with the node list (default pinned)
            // instead of silently shortening it and misaligning every later node.
            bool[] expectedAligned = [true, true, false];
            Assert.That(PathParticleRope.ParsePins("[ true, nonsense, false ]"), Is.EqualTo(expectedAligned));
        }
    }
}

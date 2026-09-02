using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

namespace Tests
{
    public class RubikonPhysicsTest
    {
        /// <summary>
        /// A hull stores its positions in m_VertexPositions once it carries explicit vertex indices, and
        /// in m_Vertices before that, so both eras have to read back the same way.
        /// </summary>
        [Test]
        public async Task HullVertexPositionsAreReadFromEitherEra()
        {
            using var withIndices = TestFixtures.Load("arch_apartment_ixia_01_top_cap_l_01.vmdl_c");
            var indexed = ((Model)withIndices.DataBlock!).GetEmbeddedPhys()!.Parts[0].Shape.Hulls[0].Shape;

            using var withoutIndices = TestFixtures.Load("juggernaut.vphys_c");
            var plain = ((PhysAggregateData)withoutIndices.DataBlock!).Parts[0].Shape.Hulls[0].Shape;

            using (Assert.Multiple())
            {
                await Assert.That(indexed.GetVertices().Length).IsEqualTo(8);
                await Assert.That(indexed.GetVertexPositions().Length).IsEqualTo(8);
                await Assert.That(indexed.GetVertexPositions()[0]).IsEqualTo(new Vector3(7.6293945E-06f, 0.00024414062f, 160f));

                await Assert.That(plain.GetVertices().Length).IsZero();
                await Assert.That(plain.GetVertexPositions().Length).IsEqualTo(8);
                await Assert.That(plain.GetVertexPositions()[0]).IsEqualTo(new Vector3(-14.162005f, 15.413235f, -2.2308178f));
            }
        }

        /// <summary>
        /// A hull face is a polygon of any size. Walking its edge loop yields its vertices in winding
        /// order, and fanning it from the loop's first vertex yields two fewer triangles than it has
        /// vertices. Both are read by the decompiler, the glTF exporter, the Hammer mesh builder and the
        /// renderer, so they have to agree on the winding.
        /// </summary>
        [Test]
        public async Task FaceLoopsAndFansAgreeOnWinding()
        {
            using var resource = TestFixtures.Load("juggernaut.vphys_c");
            var hull = ((PhysAggregateData)resource.DataBlock!).Parts[0].Shape.Hulls[0].Shape;

            var edges = hull.GetEdges();
            var faces = hull.GetFaces();

            var faceCount = faces.Length;
            var edgeCount = edges.Length;
            var vertexCount = hull.GetVertexPositions().Length;

            var loops = new List<int[]>();
            var fans = new List<(int A, int B, int C)[]>();

            foreach (var face in faces)
            {
                var loop = new List<int>();
                foreach (var vertex in Hull.GetFaceVertices(edges, face))
                {
                    loop.Add(vertex);
                }

                var fan = new List<(int A, int B, int C)>();
                foreach (var triangle in Hull.GetFaceTriangles(edges, face))
                {
                    fan.Add(triangle);
                }

                loops.Add([.. loop]);
                fans.Add([.. fan]);
            }

            using (Assert.Multiple())
            {
                // A box: six quads, each fanning into two triangles.
                await Assert.That(faceCount).IsEqualTo(6);
                await Assert.That(loops).All(loop => loop.Length == 4);
                await Assert.That(fans).All(fan => fan.Length == 2);

                // Euler's formula holds for a closed convex hull, which is what the extractors assert on.
                await Assert.That(faceCount + vertexCount).IsEqualTo((edgeCount / 2) + 2);

                for (var i = 0; i < faceCount; i++)
                {
                    // Every triangle of the fan starts at the loop's first vertex, and the fan visits the
                    // rest of the loop in order.
                    await Assert.That(fans[i].Select(triangle => triangle.A)).All(a => a == loops[i][0]);
                    await Assert.That(fans[i][0]).IsEqualTo((loops[i][0], loops[i][1], loops[i][2]));
                    await Assert.That(fans[i][1]).IsEqualTo((loops[i][0], loops[i][2], loops[i][3]));

                    // A loop never repeats a vertex.
                    await Assert.That(loops[i].Distinct().Count()).IsEqualTo(loops[i].Length);
                }
            }
        }

        /// <summary>
        /// A shape's collision tags moved key at some point: assets compiled before the rename carry them
        /// under m_PhysicsTagStrings. Every consumer reads them through one accessor, because a reader
        /// that only knows the new key gets nothing back for an older asset.
        /// </summary>
        [Test]
        public async Task CollisionTagsAreReadFromEitherKey()
        {
            using var resource = TestFixtures.Load("juggernaut.vphys_c");
            var phys = (PhysAggregateData)resource.DataBlock!;

            var modern = KVObject.Collection();
            modern.Add("m_InteractAsStrings", Tags("solid", "player"));

            var legacy = KVObject.Collection();
            legacy.Add("m_PhysicsTagStrings", Tags("solid", "player"));

            var neither = KVObject.Collection();

            using (Assert.Multiple())
            {
                await Assert.That(PhysAggregateData.GetInteractAsTags(modern)).IsEquivalentTo(["solid", "player"], CollectionOrdering.Matching);
                await Assert.That(PhysAggregateData.GetInteractAsTags(legacy)).IsEquivalentTo(["solid", "player"], CollectionOrdering.Matching);

                // No tags at all is empty, never null, so a caller can spread it without a check.
                await Assert.That(PhysAggregateData.GetInteractAsTags(neither)).IsEmpty();

                // The indexed overload agrees with the direct one, and an index out of range is empty.
                await Assert.That(phys.GetInteractAsTags(0))
                    .IsEquivalentTo(PhysAggregateData.GetInteractAsTags(phys.CollisionAttributes[0]), CollectionOrdering.Matching);
                await Assert.That(phys.GetInteractAsTags(phys.CollisionAttributes.Count)).IsEmpty();
                await Assert.That(phys.GetInteractAsTags(-1)).IsEmpty();
            }
        }

        private static KVObject Tags(params string[] values)
        {
            var array = KVObject.Array();

            foreach (var value in values)
            {
                array.Add(value);
            }

            return array;
        }
    }
}

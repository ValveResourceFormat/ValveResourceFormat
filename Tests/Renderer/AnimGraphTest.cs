using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Renderer
{
    /// <summary>
    /// Parses a compiled animation graph and skeleton through the AnimLib definition classes,
    /// covering the KV3 access patterns (transform arrays, symbol arrays, nested collections)
    /// the graph runtime depends on.
    /// </summary>
    public class AnimGraphTest
    {
        private static KVObject Load(string name)
        {
            using var resource = TestFixtures.Load(name);
            return ((BinaryKV3)resource.DataBlock!).Data.Root;
        }

        [Test]
        public async Task ParsesSkeleton()
        {
            var skeleton = new Skeleton(Load("chicken.vnmskel_c"));

            await Assert.That(skeleton.BoneIDs).IsNotEmpty();
            await Assert.That(skeleton.ParentIndices.Length).IsEqualTo(skeleton.BoneIDs.Length);
            await Assert.That(skeleton.ParentSpaceReferencePose.Length).IsEqualTo(skeleton.BoneIDs.Length);
            await Assert.That(skeleton.ModelSpaceReferencePose.Length).IsEqualTo(skeleton.BoneIDs.Length);

            // A parsed reference pose has valid (non-zero) rotations on every bone.
            await Assert.That(skeleton.ParentSpaceReferencePose.All(t => t.Angle != default)).IsTrue();
        }

        [Test]
        public async Task Blend2DKeepsPointsOnSharedEdgesInsideATriangle()
        {
            // A centre and four directions, as locomotion blend spaces are laid out
            Vector2[] points = [new(0, 0), new(225, 0), new(0, -225), new(-225, 0), new(0, 225)];
            uint[] triangles = [0, 4, 1, 3, 4, 0, 3, 0, 2, 2, 0, 1];
            uint[] hull = [3, 4, 1, 2, 3];

            // Along the edge from the centre to the first direction, and a rounding error either side of it
            foreach (var y in new[] { 0f, 1e-7f, -1e-7f })
            {
                var result = default(BlendSpace2D.Result);
                BlendSpace2D.CalculateWeights(points, triangles, hull, new Vector2(28f, y), ref result);

                int[] sources = [result.Src0, result.Src1, result.Src2];
                var weight = result.Weight01;

                // The centre and that direction, not the two rim points of the nearest hull edge
                await Assert.That(sources).IsEquivalentTo([1, 0, -1]);
                await Assert.That(weight).IsBetween(0.87f, 0.88f);
            }
        }

        [Test]
        public async Task CreatesEveryGraphNode()
        {
            var graph = Load("viewmodel_inspects.vnmgraph+ak47.vnmgraph_c");
            var nodes = graph.GetArray("m_nodes");
            await Assert.That(nodes).IsNotEmpty();

            for (var i = 0; i < nodes.Count; i++)
            {
                var className = nodes[i].GetStringProperty("_class");
                await Assert.That(GraphNodeFactory.Create(nodes[i])).IsNotNull().Because($"node {i} ({className})");
            }
        }

        [Test]
        public async Task NodeFactoryKnowsEveryNodeType()
        {
            var nodeTypes = typeof(GraphNode).Assembly.GetTypes()
                .Where(static type => type.IsSubclassOf(typeof(GraphNode)) && !type.IsAbstract);

            foreach (var nodeType in nodeTypes)
            {
                var nodeData = KVObject.Collection();
                nodeData["_class"] = new KVObject($"CNm{nodeType.Name}::CDefinition");

                // Constructors may reject the empty data, the factory just has to reach them
                string? unknownTypeError = null;
                try
                {
                    GraphNodeFactory.Create(nodeData);
                }
                catch (InvalidOperationException e) when (e.Message.StartsWith("Unknown graph node type", StringComparison.Ordinal))
                {
                    unknownTypeError = e.Message;
                }
                catch (Exception)
                {
                }

                await Assert.That(unknownTypeError).IsNull().Because(nodeType.Name);
            }
        }
    }
}

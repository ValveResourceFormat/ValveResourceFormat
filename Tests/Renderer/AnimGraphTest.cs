using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests.Renderer
{
    /// <summary>
    /// Parses real animation graph and skeleton dumps through the AnimLib definition classes,
    /// covering the KV3 access patterns (transform arrays, symbol arrays, nested collections)
    /// the graph runtime depends on.
    /// </summary>
    public class AnimGraphTest
    {
        private static KVObject Load(string name)
            => KVDocumentExtensions.ParseKV3(TestFixtures.Path("AnimGraph", name)).Root;

        [Test]
        public async Task ParsesExampleSkeleton()
        {
            var skeleton = new Skeleton(Load("ExampleSkeleton.kv3"));

            await Assert.That(skeleton.BoneIDs).IsNotEmpty();
            await Assert.That(skeleton.ParentIndices.Length).IsEqualTo(skeleton.BoneIDs.Length);
            await Assert.That(skeleton.ParentSpaceReferencePose.Length).IsEqualTo(skeleton.BoneIDs.Length);
            await Assert.That(skeleton.ModelSpaceReferencePose.Length).IsEqualTo(skeleton.BoneIDs.Length);

            // A parsed reference pose has valid (non-zero) rotations on every bone.
            await Assert.That(skeleton.ParentSpaceReferencePose.All(t => t.Angle != default)).IsTrue();
        }

        [Test]
        public async Task CreatesEveryExampleGraphNode()
        {
            var graph = Load("ExampleGraph.kv3");
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

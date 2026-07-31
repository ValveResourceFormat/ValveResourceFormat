using System.IO;
using System.Linq;
using NUnit.Framework;
using ValveKeyValue;
using ValveResourceFormat.Renderer.AnimLib;
using ValveResourceFormat.Serialization.KeyValues;

namespace Tests
{
    /// <summary>
    /// Parses real animation graph and skeleton dumps through the AnimLib definition classes,
    /// covering the KV3 access patterns (transform arrays, symbol arrays, nested collections)
    /// the graph runtime depends on.
    /// </summary>
    [TestFixture]
    public class AnimGraphTest
    {
        private static KVObject Load(string name)
            => KVDocumentExtensions.ParseKV3(Path.Combine(TestContext.CurrentContext.TestDirectory, "Files", "AnimGraph", name)).Root;

        [Test]
        public void ParsesExampleSkeleton()
        {
            var skeleton = new Skeleton(Load("ExampleSkeleton.kv3"));

            Assert.That(skeleton.BoneIDs, Is.Not.Empty);
            Assert.That(skeleton.ParentIndices, Has.Length.EqualTo(skeleton.BoneIDs.Length));
            Assert.That(skeleton.ParentSpaceReferencePose, Has.Length.EqualTo(skeleton.BoneIDs.Length));
            Assert.That(skeleton.ModelSpaceReferencePose, Has.Length.EqualTo(skeleton.BoneIDs.Length));

            // A parsed reference pose has valid (non-zero) rotations on every bone.
            Assert.That(skeleton.ParentSpaceReferencePose.All(t => t.Angle != default), Is.True);
        }

        [Test]
        public void CreatesEveryExampleGraphNode()
        {
            var graph = Load("ExampleGraph.kv3");
            var nodes = graph.GetArray("m_nodes");
            Assert.That(nodes, Is.Not.Empty);

            for (var i = 0; i < nodes.Count; i++)
            {
                var className = nodes[i].GetStringProperty("_class");
                Assert.That(GraphContext.CreateNode(nodes[i]), Is.Not.Null, $"node {i} ({className})");
            }
        }
    }
}

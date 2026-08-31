using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    public class GraphBuilderTest
    {
        private static string FilePath(string name)
            => Path.Combine(TestContext.TestDirectory!, "Files", name);

        private static KVObject LoadData(string name)
        {
            using var resource = new Resource();
            resource.Read(FilePath(name));
            return ((BinaryKV3)resource.DataBlock!).Data;
        }

        [Test]
        [Arguments("box_creature_model.vanmgrph_c", 4, 3)]
        [Arguments("slork_kv3_v5_zstd.vanmgrph_c", 475, 798)]
        public async Task BuildsAnimGraph1Document(string file, int expectedNodes, int expectedWires)
        {
            var document = new GraphDocument();
            using var builder = new AnimGraph1Builder(LoadData(file), new NullFileLoader());
            builder.Build(document);

            using (Assert.Multiple())
            {
                await Assert.That(document.NodeCount).IsEqualTo(expectedNodes);
                await Assert.That(document.WireCount).IsEqualTo(expectedWires);
            }
        }

        [Test]
        public async Task BuildsNmGraphDocument()
        {
            var document = new GraphDocument();
            var builder = new NmGraphBuilder(LoadData("viewmodel_inspects.vnmgraph+ak47.vnmgraph_c"))
            {
                DrawStateMachines = true,
                DrawParameterWires = true,
            };
            builder.Build(document);

            using (Assert.Multiple())
            {
                await Assert.That(document.NodeCount).IsEqualTo(58);
                await Assert.That(document.WireCount).IsEqualTo(75);
                await Assert.That(builder.HasControlParameters).IsTrue();
            }
        }

        [Test]
        public async Task BuildsPulseGraphDocument()
        {
            var document = new GraphDocument();
            var builder = new PulseGraphBuilder(LoadData("de_inferno_script.vpulse_c"));
            builder.Build(document);

            using (Assert.Multiple())
            {
                await Assert.That(document.NodeCount).IsEqualTo(14);
                await Assert.That(document.WireCount).IsEqualTo(12);
            }
        }

        [Test]
        public async Task BuildsEntityIOGraphDocument()
        {
            using var resource = new Resource();
            resource.Read(FilePath("ascent_speedup_switch_template_ents.vents_c"));
            var entityLump = (EntityLump)resource.DataBlock!;
            var entities = entityLump.GetEntities().ToList();

            var document = new GraphDocument();
            var groupMembers = new Dictionary<GraphNode, List<EntityLump.Entity>>();
            EntityIOGraphBuilder.Build(document, entities, groupMembers);

            // One node for the template entity, one per distinct connection target it fires at.
            using (Assert.Multiple())
            {
                await Assert.That(document.NodeCount).IsEqualTo(6);
                await Assert.That(document.WireCount).IsEqualTo(5);
            }
        }
    }
}

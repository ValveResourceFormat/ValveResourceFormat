using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Graphs;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Tests.IO
{
    public class GraphBuilderTest
    {
        private static KVObject LoadData(string name)
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path(name));
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
        public async Task BuildsPulseGraphWithTempVarsAndLoopBreak()
        {
            const string Graph = """
                <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
                {
                    m_Cells =
                    [
                        { _class = "CPulseCell_Inflow_Method" m_nEditorNodeID = -1 m_EntryChunk = 0 m_MethodName = "Run" },
                    ]
                    m_Chunks =
                    [
                        {
                            m_nTempVarBank = 0
                            m_Registers =
                            [
                                { m_nReg = 0 m_Type = "PVAL_INT" },
                                { m_nReg = 1 m_Type = "PVAL_INT" },
                            ]
                            m_Instructions =
                            [
                                { m_nCode = "GET_CONST" m_nReg0 = 0 m_nReg1 = -1 m_nReg2 = -1 m_nConstIdx = 0 },
                                { m_nCode = "SET_TEMPVAR" m_nReg0 = 0 m_nReg1 = -1 m_nReg2 = -1 m_nTempVarIdx = 0 },
                                { m_nCode = "GET_TEMPVAR" m_nReg0 = 1 m_nReg1 = -1 m_nReg2 = -1 m_nTempVarIdx = 0 },
                                { m_nCode = "SET_TEMPVAR_OBSERVABLE" m_nReg0 = 1 m_nReg1 = -1 m_nReg2 = -1 m_nTempVarIdx = 0 },
                                { m_nCode = "LOOP_BREAK" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                            ]
                        },
                    ]
                    m_TempVarBanks =
                    [
                        { m_TempVars = [ { m_Name = "counter" m_Type = "PVAL_INT" m_nEditorNodeID = -1 m_bIsObservable = false } ] },
                    ]
                    m_Constants = [ { m_Type = "PVAL_INT" m_Value = 5 } ]
                    m_InvokeBindings = []
                    m_DomainValues = []
                    m_Vars = []
                    m_PublicOutputs = []
                    m_CallInfos = []
                }
                """;

            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(Graph));
            var data = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(stream).Root;

            var document = new GraphDocument();
            new PulseGraphBuilder(data).Build(document);

            var nodes = document.Nodes.ToList();

            using (Assert.Multiple())
            {
                // Two setters and one getter share a single hub for the temporary
                await Assert.That(nodes.Count(n => n.Title == "Set Temporary Variable")).IsEqualTo(2);
                await Assert.That(nodes.Count(n => n.Title == "Get Temporary Variable")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "counter")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "Break")).IsEqualTo(1);
            }
        }

        [Test]
        public async Task BuildsEntityIOGraphDocument()
        {
            using var resource = new Resource();
            resource.Read(TestFixtures.Path("ascent_speedup_switch_template_ents.vents_c"));
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

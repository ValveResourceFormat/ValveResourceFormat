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
        private sealed class CollectingProgress : IProgress<string>
        {
            public List<string> Messages { get; } = [];

            public void Report(string value) => Messages.Add(value);
        }

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

                // No call carries a break destination, so there is no loop to leave
                var breakRows = nodes.Single(n => n.Title == "Break").Rows.OfType<TextRow>().ToList();
                await Assert.That(breakRows.Any(r => r.IsMessage && r.Text == "No enclosing loop, halts the cursor")).IsTrue();
            }
        }

        [Test]
        public async Task BuildsPulseGraphWithDetachedVariablesBlackboardAndChildCursors()
        {
            // A single cell with two chunks used to leave the second chunk unnamed, so the leap into it threw
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
                            m_Registers =
                            [
                                { m_nReg = 0 m_Type = "PVAL_INT" },
                                { m_nReg = 1 m_Type = "PVAL_INT" },
                                { m_nReg = 2 m_Type = "PVAL_FLOAT" },
                                { m_nReg = 3 m_Type = "PVAL_INT" },
                                { m_nReg = 4 m_Type = "PVAL_VDATA_CHOICE" },
                            ]
                            m_Instructions =
                            [
                                { m_nCode = "GET_CONST" m_nReg0 = 0 m_nReg1 = -1 m_nReg2 = -1 m_nConstIdx = 0 },
                                { m_nCode = "SET_VAR_OBSERVABLE" m_nVar = 0 m_nReg0 = 0 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "GET_VAR_DETACH" m_nVar = 0 m_nReg0 = 1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "DETACH_REGISTER" m_nReg0 = 1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "GET_BLACKBOARD_REFERENCE" m_nReg0 = 2 m_nReg1 = -1 m_nReg2 = -1 m_nBlackboardReferenceIdx = 0 },
                                { m_nCode = "SET_BLACKBOARD_REFERENCE" m_nReg0 = 2 m_nReg1 = -1 m_nReg2 = -1 m_nBlackboardReferenceIdx = 0 },
                                { m_nCode = "GET_CONST" m_nReg0 = 3 m_nReg1 = -1 m_nReg2 = -1 m_nConstIdx = 0 },
                                { m_nCode = "SET_VAR_ARRAY_ELEMENT_1D" m_nVar = 1 m_nReg0 = 1 m_nReg1 = -1 m_nReg2 = 3 },
                                { m_nCode = "CREATE_CHILD_CURSOR_OUTFLOW" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 m_nChunk = 0 m_nDestInstruction = 10 },
                                { m_nCode = "CHUNK_LEAP" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 m_nChunk = 1 m_nDestInstruction = 0 },
                                { m_nCode = "GET_VAR" m_nVar = 0 m_nReg0 = 4 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "SET_VAR" m_nVar = 0 m_nReg0 = 4 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                            ]
                        },
                        {
                            m_Registers = []
                            m_Instructions =
                            [
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                            ]
                        },
                    ]
                    m_Vars =
                    [
                        { m_Name = "health" m_Type = "PVAL_INT" m_DefaultValue = 0 m_bIsObservable = true },
                        { m_Name = "scores" m_Type = "PVAL_ARRAY:PVAL_INT" m_DefaultValue = [] },
                    ]
                    m_BlackboardReferences = [ { m_BlackboardResource = "" m_nNodeID = 1 m_NodeName = "speed" } ]
                    m_Constants = [ { m_Type = "PVAL_INT" m_Value = 5 } ]
                    m_InvokeBindings = []
                    m_DomainValues = []
                    m_PublicOutputs = []
                    m_CallInfos = []
                }
                """;

            using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(Graph));
            var data = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(stream).Root;

            var reports = new CollectingProgress();
            var document = new GraphDocument();
            new PulseGraphBuilder(data) { ProgressReporter = reports }.Build(document);

            var nodes = document.Nodes.ToList();
            var texts = nodes.SelectMany(n => n.Rows.OfType<TextRow>()).Select(r => r.Text).ToList();

            using (Assert.Multiple())
            {
                await Assert.That(reports.Messages).IsEmpty();
                await Assert.That(texts.Any(t => t.Contains("FAILED TO RESOLVE", StringComparison.Ordinal))).IsFalse();
                await Assert.That(texts).Contains("Target: Unnamed_1");

                // Reads, writes and the definition of a variable share one hub
                await Assert.That(nodes.Count(n => n.Title == "health")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "scores")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "speed")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "Get Variable")).IsEqualTo(2);
                await Assert.That(nodes.Count(n => n.Title == "Set Variable")).IsEqualTo(2);
                await Assert.That(nodes.Count(n => n.Title == "Set Array Element")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "Get Blackboard Reference")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "Set Blackboard Reference")).IsEqualTo(1);
                await Assert.That(nodes.Count(n => n.Title == "DETACH_REGISTER")).IsEqualTo(0);

                // The child cursor walks the instructions it starts at
                var childCursor = nodes.Single(n => n.Title == "Start Child Cursor");
                await Assert.That(childCursor.Outputs.Single(s => s.Name == "Child cursor").Wires).IsNotEmpty();
            }
        }

        [Test]
        public async Task BuildsPulseGraphOutflowIntoAnotherChunkFresh()
        {
            // The wait resumes in chunk 1 past chunk 0's length, where register 0 was never computed
            const string Graph = """
                <!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->
                {
                    m_Cells =
                    [
                        { _class = "CPulseCell_Inflow_Method" m_nEditorNodeID = -1 m_EntryChunk = 0 m_MethodName = "Run" },
                        { _class = "CPulseCell_Inflow_Wait" m_nEditorNodeID = -1 m_WakeResume = { m_SourceOutflowName = "m_WakeResume" m_nDestChunk = 1 m_nInstruction = 3 } },
                    ]
                    m_Chunks =
                    [
                        {
                            m_Registers = [ { m_nReg = 0 m_Type = "PVAL_INT" } ]
                            m_Instructions =
                            [
                                { m_nCode = "GET_CONST" m_nReg0 = 0 m_nReg1 = -1 m_nReg2 = -1 m_nConstIdx = 0 },
                                { m_nCode = "CELL_INVOKE" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 m_nInvokeBindingIndex = 0 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                            ]
                        },
                        {
                            m_Registers = [ { m_nReg = 0 m_Type = "PVAL_INT" } ]
                            m_Instructions =
                            [
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                                { m_nCode = "LIBRARY_INVOKE" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 m_nInvokeBindingIndex = 1 },
                                { m_nCode = "RETURN_VOID" m_nReg0 = -1 m_nReg1 = -1 m_nReg2 = -1 },
                            ]
                        },
                    ]
                    m_InvokeBindings =
                    [
                        { m_RegisterMap = { m_Inparams = null m_Outparams = null } m_FuncName = "CPulseCell_Inflow_Wait::Wait" m_nCellIndex = 1 },
                        { m_RegisterMap = { m_Inparams = { a = 0 } m_Outparams = null } m_FuncName = "CLib::Print" m_nCellIndex = -1 },
                    ]
                    m_Constants = [ { m_Type = "PVAL_INT" m_Value = 5 } ]
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

            // Not cut off by chunk 0's instruction range, and not reading chunk 0's register 0
            var print = document.Nodes.Single(n => n.Title == "CLib::Print");
            var texts = print.Rows.OfType<TextRow>().Select(r => r.Text).ToList();
            await Assert.That(texts).Contains("a = <FAILED TO RESOLVE>");
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

using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.ResourceTypes.SmartProps.Elements;
using ValveResourceFormat.ResourceTypes.SmartProps.Modifiers;
using ValveResourceFormat.Utils;

namespace Tests.Resources
{
    public class SmartPropElementsTest
    {
        private const string Header = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n";

        private static SmartPropOutput Evaluate(string children, SmartPropEvaluationInput? input = null)
        {
            var text = string.Concat(Header, "{ m_Children = [ ", children, " ] }");
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            var root = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(stream).Root;
            return new SmartPropEvaluator(null).Evaluate(SmartPropDefinition.Load(root), input ?? new SmartPropEvaluationInput());
        }

        private static string Model(int id, string modifiers = "", string extra = "")
            => $"{{ _class = \"CSmartPropElement_Model\" m_nElementID = {id} m_sModelName = \"models/m{id}.vmdl\" m_Modifiers = [ {modifiers} ] {extra} }}";

        [Test]
        public async Task BendDeformerMovesRigidPartsOntoTheArc()
        {
            var output = Evaluate("""
                {
                    _class = "CSmartPropElement_BendDeformer" m_nElementID = 1
                    m_vSize = [ 128.0, 16.0, 16.0 ] m_flBendAngle = 90.0
                    m_Children =
                    [
                        { _class = "CSmartPropElement_PropDynamic" m_nElementID = 2 m_sModelName = "models/a.vmdl"
                          m_Modifiers = [ { _class = "CSmartPropOperation_Translate" m_vPosition = [ 64.0, 0.0, 0.0 ] } ] },
                        { _class = "CSmartPropElement_Model" m_nElementID = 3 m_sModelName = "models/b.vmdl" m_bRigidDeformation = true
                          m_Modifiers = [ { _class = "CSmartPropOperation_Translate" m_vPosition = [ 64.0, 0.0, 0.0 ] } ] },
                    ]
                }
                """);

            var radius = 128f / (MathF.PI / 2f);
            var onArc = new Vector3(radius * MathF.Sin(MathF.PI / 4f), radius * MathF.Cos(MathF.PI / 4f) - radius, 0f);
            var tangent = new Vector3(MathF.Cos(MathF.PI / 4f), -MathF.Sin(MathF.PI / 4f), 0f);

            await Assert.That(output.Models.Count).IsEqualTo(2);

            foreach (var model in output.Models)
            {
                var forward = Vector3.Transform(Vector3.UnitX, model.Transform.Rotation);

                await Assert.That(model.DeformationMode).IsEqualTo(SmartPropDeformationMode.Rigid);
                await Assert.That(Vector3.Distance(model.Transform.Position, onArc)).IsLessThan(0.2f);
                await Assert.That(Vector3.Distance(forward, tangent)).IsLessThan(0.01f);
            }
        }

        [Test]
        public async Task EntityElementsTakeNoMaterialSetsAndKeepOwnership()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_Group" m_nElementID = 1
                    m_Modifiers =
                    [
                        { _class = "CSmartPropOperation_MaterialOverride" m_MaterialReplacements = [ { m_OriginalMaterial = "a.vmat" m_ReplacementMaterial = "b.vmat" } ] },
                        { _class = "CSmartPropOperation_MaterialTint" m_Material = "a.vmat" },
                    ]
                    m_Children =
                    [
                        { _class = "CSmartPropElement_PropPhysics" m_nElementID = 2 m_sModelName = "models/a.vmdl" m_bStartAsleep = true
                          m_nDeformableAttachmentMode = "SNAP" },
                        {{Model(3)}},
                    ]
                }
                """);

            var entity = output.Models[0];
            var model = output.Models[1];

            await Assert.That(entity.MaterialOverrideSetIndex).IsEqualTo(-1);
            await Assert.That(entity.MaterialTintSetIndex).IsEqualTo(-1);
            await Assert.That(model.MaterialOverrideSetIndex).IsEqualTo(0);
            await Assert.That(model.MaterialTintSetIndex).IsEqualTo(0);
            await Assert.That(output.MaterialOverrideSets.Count).IsEqualTo(1);

            await Assert.That(entity.EntityKeys!["classname"].GetString()).IsEqualTo("prop_physics");
            await Assert.That(entity.EntityKeys["deformable_attach_mode"].Kind).IsEqualTo(SmartPropValueKind.Int);
            await Assert.That(entity.EntityKeys["deformable_attach_mode"].GetInt()).IsEqualTo(1);
            await Assert.That(entity.EntityKeys["deformable_attach_orientation_mode"].GetInt()).IsEqualTo(4);
            await Assert.That(entity.SpawnFlags!).IsEquivalentTo(["Start Asleep"]);
            await Assert.That(entity.EntityKeys.ContainsKey("spawnflags")).IsFalse();
        }

        [Test]
        public async Task SurfaceAndDirectionTracesPassIgnoreFlags()
        {
            var queries = new List<SmartPropTraceQuery>();
            Evaluate(Model(1, """
                { _class = "CSmartPropOperation_TraceInDirection" },
                { _class = "CSmartPropOperation_TraceInDirection" m_bIgnoreModels = true m_bIgnoreSky = false },
                """), new SmartPropEvaluationInput
            {
                Trace = query =>
                {
                    queries.Add(query);
                    return null;
                },
            });

            const SmartPropTraceFlags Default = SmartPropTraceFlags.IgnoreToolMaterials | SmartPropTraceFlags.IgnoreSky
                | SmartPropTraceFlags.IgnoreNoDraw | SmartPropTraceFlags.IgnoreEntities;

            await Assert.That(queries.Count).IsEqualTo(3);
            await Assert.That((int)queries[0].Flags).IsEqualTo(39);
            await Assert.That(queries[1].Flags).IsEqualTo(Default);
            await Assert.That(queries[2].Flags).IsEqualTo((Default & ~SmartPropTraceFlags.IgnoreSky) | SmartPropTraceFlags.IgnoreModels);
        }

        [Test]
        [Arguments(180f, -90f, 90f, 90f)]
        [Arguments(-180f, -90f, 90f, 90f)]
        [Arguments(45f, -90f, 90f, 45f)]
        [Arguments(100f, -90f, 90f, 90f)]
        [Arguments(350f, 300f, 400f, 350f)]
        [Arguments(10f, 90f, 0f, 10f)]
        public async Task RotatorClampsAngleAcrossTheWrap(float angle, float min, float max, float expected)
        {
            await Assert.That(CreateRotatorOperation.ClampAngleToRange(angle, min, max)).IsEqualTo(expected).Within(1e-3f);
        }

        [Test]
        public async Task GridFillWithZeroSpacingPlacesOneColumn()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_Layout2DGrid" m_nElementID = 1 m_GridArrangement = "FILL"
                    m_flWidth = 100.0 m_flLength = 100.0 m_flSpacingWidth = 0.0 m_flSpacingLength = 20.0
                    m_Children = [ {{Model(2)}} ]
                }
                """);

            var positions = output.Models.Select(m => m.Transform.Position).ToArray();
            Vector3[] expected = [new(0f, -40f, 0f), new(0f, -20f, 0f), new(0f, 0f, 0f), new(0f, 20f, 0f), new(0f, 40f, 0f)];

            await Assert.That(positions).IsEquivalentTo(expected);
        }

        [Test]
        public async Task GridOverflowPushesPathEntryZero()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_Layout2DGrid" m_nElementID = 1 m_nCountW = 100 m_nCountL = 100
                    m_Children = [ {{Model(2)}} ]
                }
                """);

            await Assert.That(output.Models.Count).IsEqualTo(1);
            await Assert.That(output.Models[0].ElementPath).IsEquivalentTo([1, 0, 2]);
        }

        [Test]
        public async Task TintSelectionOutOfRangeChangesNothingAndUnknownModeMultipliesCurrentTint()
        {
            var output = Evaluate(Model(1, """
                { _class = "CSmartPropOperation_SetTintColor" m_Mode = "REPLACE" m_SelectionMode = "FIRST" m_ColorChoices = [ { m_Color = [ 128, 128, 128, 255 ] } ] },
                { _class = "CSmartPropOperation_SetTintColor" m_Mode = "REPLACE" m_SelectionMode = { m_Expression = "1 + 2" } m_ColorChoices = [ { m_Color = [ 0, 0, 0, 255 ] } ] },
                { _class = "CSmartPropOperation_SetTintColor" m_Mode = { m_Expression = "2 + 3" } m_SelectionMode = "FIRST" m_ColorChoices = [ { m_Color = [ 128, 128, 128, 255 ] } ] },
                """));

            await Assert.That(output.Models[0].Tint).IsEqualTo(new Color32(64, 64, 64, 255));
        }

        [Test]
        public async Task LocatorWithoutConfigurationStillClaimsItsHandle()
        {
            var state = new SmartPropElementState { ElementPath = [1] };
            state.HandleEdits["loc"] = new SmartPropHandleEdit { DeltaTransform = new SmartPropTransform(new Vector3(5f, 0f, 0f), 1f, Quaternion.Identity) };

            var output = Evaluate(Model(1, """
                { _class = "CSmartPropOperation_CreateLocator" m_LocatorName = "loc" m_bConfigurable = false m_vOffset = [ 0.0, 0.0, 8.0 ] m_flDisplayScale = 2.0 },
                { _class = "CSmartPropOperation_CreateLocator" m_LocatorName = "LOC" },
                """), new SmartPropEvaluationInput { ElementStates = [state] });

            await Assert.That(output.Handles.Count).IsEqualTo(1);

            var handle = output.Handles[0];
            await Assert.That(handle.Configurable).IsFalse();
            await Assert.That(handle.AllowedEdits).IsEqualTo((false, false, false));
            await Assert.That(handle.Transform.Position).IsEqualTo(new Vector3(0f, 0f, 8f));
            await Assert.That(handle.DisplaySize).IsEqualTo(2f);
            await Assert.That(output.Models[0].Transform.Position).IsEqualTo(Vector3.Zero);
        }

        [Test]
        public async Task RotatorAndSizerHandlesCarryTheirDisplayRecords()
        {
            var output = Evaluate(Model(1, """
                { _class = "CSmartPropOperation_CreateRotator" m_Name = "rot" m_vRotationAxis = [ 1.0, 0.0, 0.0 ] m_vOffset = [ 0.0, 4.0, 0.0 ] m_flSnappingIncrement = 15.0 },
                { _class = "CSmartPropOperation_CreateSizer" m_Name = "size" m_bDisplayModel = true m_OutputVariableMaxY = "y" m_flConstraintMinX = -1.0 m_flConstraintMaxX = 1.0 },
                """));

            var rotator = output.Handles[0];
            var sizer = output.Handles[1];

            await Assert.That(rotator.Kind).IsEqualTo(SmartPropHandleKind.Rotator);
            await Assert.That(rotator.Transform.Position).IsEqualTo(new Vector3(0f, 4f, 0f));
            await Assert.That(Quaternion.Dot(rotator.Transform.Rotation, Quaternion.Identity)).IsEqualTo(1f).Within(1e-5f);
            await Assert.That(rotator.DisplaySize).IsEqualTo(16f);
            await Assert.That(rotator.DisplayColor).IsEqualTo(new Color32(170, 170, 110, 255));
            await Assert.That(rotator.SnappingIncrement).IsEqualTo(15f);
            await Assert.That(rotator.AngleLimits).IsNull();

            await Assert.That(sizer.Kind).IsEqualTo(SmartPropHandleKind.Sizer);
            await Assert.That(sizer.DisplayModel).IsTrue();
            await Assert.That(sizer.EditableMin).IsEqualTo((false, false, false));
            await Assert.That(sizer.EditableMax).IsEqualTo((false, true, false));
            await Assert.That(sizer.ExtentLimits).IsEqualTo((new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f)));
        }

        private static string PickOne(string mode, string extra = "") => $$"""
            {
                _class = "CSmartPropElement_PickOne" m_nElementID = 1 m_SelectionMode = "{{mode}}" {{extra}}
                m_Children = [ {{Model(10)}}, {{Model(11)}}, {{Model(12)}} ]
            }
            """;

        [Test]
        [Arguments("RANDOM", SmartPropChoiceAction.Next, 11, 12)]
        [Arguments("FIRST", SmartPropChoiceAction.Next, 12, 10)]
        [Arguments("FIRST", SmartPropChoiceAction.Previous, 10, 12)]
        [Arguments("FIRST", SmartPropChoiceAction.Randomize, 11, 11)]
        [Arguments("FIRST", SmartPropChoiceAction.None, 11, 11)]
        [Arguments("FIRST", SmartPropChoiceAction.ChooseNone, 11, -1)]
        public async Task ChoiceRequestsMoveTheStoredChoice(string mode, SmartPropChoiceAction action, int stored, int expected)
        {
            var output = Evaluate(PickOne(mode), new SmartPropEvaluationInput
            {
                ElementStates = [new SmartPropElementState { ElementPath = [1], ChoiceValue = stored }],
                ChoiceRequests = [new SmartPropChoiceRequest([1], action)],
            });

            var state = output.ElementStates.Single(s => s.ElementPath is [1]);
            int[] placed = expected < 0 ? [] : [expected];

            await Assert.That(state.ChoiceValue).IsEqualTo(expected);
            await Assert.That(output.Models.Select(m => m.ElementPath[^1]).ToArray()).IsEquivalentTo(placed);
        }

        [Test]
        public async Task ChoiceRequestWithEmptyPathAppliesToEveryPickOne()
        {
            var output = Evaluate(PickOne("FIRST"), new SmartPropEvaluationInput
            {
                ChoiceRequests = [new SmartPropChoiceRequest([7], SmartPropChoiceAction.ChooseNone), new SmartPropChoiceRequest([], SmartPropChoiceAction.Next)],
                ElementStates = [new SmartPropElementState { ElementPath = [1], ChoiceValue = 10 }],
            });

            await Assert.That(output.Models.Single().ElementPath[^1]).IsEqualTo(11);
        }

        [Test]
        public async Task ConfigurablePickOneAddsAChoiceHandle()
        {
            var output = Evaluate(PickOne("FIRST", "m_vHandleOffset = [ 0.0, 0.0, 32.0 ]"));
            var handle = output.ChoiceHandles.Single();

            await Assert.That(handle.ElementPath).IsEquivalentTo([1]);
            await Assert.That(handle.Transform.Position).IsEqualTo(new Vector3(0f, 0f, 32f));
            await Assert.That(handle.Color).IsEqualTo(new Color32(0x90, 0x90, 0x90, 0xFF));
            await Assert.That(handle.Size).IsEqualTo(9);
            await Assert.That(handle.Shape).IsEqualTo(SmartPropHandleShape.Square);

            await Assert.That(Evaluate(PickOne("SPECIFIC")).ChoiceHandles.Count).IsEqualTo(0);
            await Assert.That(Evaluate(PickOne("FIRST", "m_bConfigurable = false")).ChoiceHandles.Count).IsEqualTo(0);
        }

        [Test]
        public async Task PlaceOnPathFollowsTheNamedInputPath()
        {
            var input = new SmartPropEvaluationInput
            {
                InputPaths =
                [
                    new SmartPropInputPath
                    {
                        Name = "Rail",
                        Transform = new SmartPropTransform(new Vector3(0f, 50f, 0f), 1f, Quaternion.Identity),
                        Knots =
                        [
                            new SmartPropPathKnot(Vector3.Zero, new Vector3(-100f / 3f, 0f, 0f), new Vector3(100f / 3f, 0f, 0f)),
                            new SmartPropPathKnot(new Vector3(100f, 0f, 0f), new Vector3(-100f / 3f, 0f, 0f), new Vector3(100f / 3f, 0f, 0f)),
                        ],
                    },
                ],
            };

            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_PlaceOnPath" m_nElementID = 1 m_PathName = "rail" m_flSpacing = 50.0
                    m_DefaultPath = [ [ 0.0, 0.0, 0.0 ], [ 0.0, 0.0, 300.0 ] ]
                    m_Children = [ {{Model(2)}} ]
                }
                """, input);

            var positions = output.Models.Select(m => m.Transform.Position).ToArray();

            await Assert.That(positions.Length).IsEqualTo(3);

            for (var i = 0; i < 3; i++)
            {
                await Assert.That(Vector3.Distance(positions[i], new Vector3(50f * i, 50f, 0f))).IsLessThan(0.01f);
            }
        }

        [Test]
        public async Task PlaceOnPathWithAnUnmatchedNameUsesTheDefaultPath()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_PlaceOnPath" m_nElementID = 1 m_PathName = "missing" m_flSpacing = 100.0
                    m_Children = [ {{Model(2)}} ]
                }
                """, new SmartPropEvaluationInput { InputPaths = [] });

            var positions = output.Models.Select(m => m.Transform.Position).ToArray();
            Vector3[] expected = [new(-100f, 0f, 0f), new(0f, 0f, 0f), new(100f, 0f, 0f)];

            await Assert.That(positions.Length).IsEqualTo(3);

            for (var i = 0; i < 3; i++)
            {
                await Assert.That(Vector3.Distance(positions[i], expected[i])).IsLessThan(0.01f);
            }
        }

        [Test]
        public async Task VertexDeformerBendsNormalsAndTangentsAroundTheVertex()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_BendDeformer" m_nElementID = 1 m_vSize = [ 128.0, 16.0, 16.0 ] m_flBendAngle = 90.0
                    m_Children = [ {{Model(2)}} ]
                }
                """);

            var deformer = output.GetVertexDeformer(output.Models[0])!;
            var vertex = new Vector3(64f, 0f, 0f);

            await Assert.That(Vector3.Distance(deformer.DeformTangent(vertex, Vector3.UnitX), new Vector3(MathF.Sqrt(0.5f), -MathF.Sqrt(0.5f), 0f))).IsLessThan(0.01f);
            await Assert.That(Vector3.Distance(deformer.DeformNormal(vertex, Vector3.UnitZ), Vector3.UnitZ)).IsLessThan(1e-4f);
            await Assert.That(Vector3.Distance(deformer.DeformNormal(vertex, Vector3.UnitY), new Vector3(MathF.Sqrt(0.5f), MathF.Sqrt(0.5f), 0f))).IsLessThan(0.01f);
        }

        [Test]
        public async Task VertexDeformerKeepsTheMirroredNormalOfAMirroredInstance()
        {
            var output = Evaluate($$"""
                {
                    _class = "CSmartPropElement_BendDeformer" m_nElementID = 1 m_vSize = [ 128.0, 16.0, 16.0 ] m_flBendAngle = 90.0
                    m_Children = [ {{Model(2, extra: "m_vModelScale = [ 1.0, -1.0, 1.0 ]")}} ]
                }
                """);

            var deformer = output.GetVertexDeformer(output.Models[0])!;

            await Assert.That(Vector3.Distance(deformer.DeformNormal(new Vector3(64f, 0f, 0f), Vector3.UnitZ), -Vector3.UnitZ)).IsLessThan(1e-4f);
        }

        [Test]
        public async Task MaterialSetsAreKeyedByNormalizedResourceNames()
        {
            var output = Evaluate(Model(1, """
                { _class = "CSmartPropOperation_MaterialTint" m_Material = "Materials\\Props//Curb.VMAT" m_Color = [ 10, 20, 30, 255 ] },
                { _class = "CSmartPropOperation_MaterialTint" m_Material = "materials/props/no_extension" m_Color = [ 0, 0, 0, 255 ] },
                { _class = "CSmartPropOperation_MaterialOverride" m_MaterialReplacements = [ { m_OriginalMaterial = "materials/./a/../b.vmat" m_ReplacementMaterial = "Materials/C.vmat" } ] },
                """));

            var model = output.Models[0];
            var tints = output.MaterialTintSets[model.MaterialTintSetIndex];
            var overrides = output.MaterialOverrideSets[model.MaterialOverrideSetIndex];

            await Assert.That(tints.Keys.ToArray()).IsEquivalentTo(["materials/props/curb.vmat"]);
            await Assert.That(overrides.Keys.ToArray()).IsEquivalentTo(["materials/b.vmat"]);
            await Assert.That(overrides["materials/b.vmat"]).IsEqualTo("materials/c.vmat");
        }

        [Test]
        [Arguments("models/a.vmdl", "models/a.vmdl")]
        [Arguments("Models\\A.VMDL", "models/a.vmdl")]
        [Arguments("models//x/./y/../a.vmdl", "models/x/a.vmdl")]
        [Arguments("models/a", "")]
        [Arguments("/models/a.vmdl", "")]
        [Arguments("c:/models/a.vmdl", "")]
        [Arguments("", "")]
        public async Task ResourceNamesNormalizeLikeTheResourceSystem(string name, string expected)
        {
            await Assert.That(SmartPropOutput.NormalizeResourceName(name)).IsEqualTo(expected);
        }

        [Test]
        public async Task QuickSortLeavesTiesWhereTheRuntimeSortDoes()
        {
            (int Index, float Radius)[] rings = [(0, 0f), (1, 1f), (2, 1f), (3, 0f)];

            NativeArithmetic.QuickSort<(int Index, float Radius)>(rings, static (a, b) => a.Radius > b.Radius ? -1 : 1);

            await Assert.That(rings.Select(r => r.Index).ToArray()).IsEquivalentTo([2, 1, 0, 3]);
        }

        [Test]
        public async Task TruncationMatchesTheNativeConversion()
        {
            await Assert.That(NativeArithmetic.Truncate(float.PositiveInfinity)).IsEqualTo(int.MinValue);
            await Assert.That(NativeArithmetic.Truncate(float.NaN)).IsEqualTo(int.MinValue);
            await Assert.That(NativeArithmetic.Truncate(-2.7f)).IsEqualTo(-2);
            await Assert.That(NativeArithmetic.Abs(int.MinValue)).IsEqualTo(int.MinValue);
        }
    }
}

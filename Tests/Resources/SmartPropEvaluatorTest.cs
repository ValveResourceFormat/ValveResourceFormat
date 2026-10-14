using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.SmartProps;
using ValveResourceFormat.Utils;

namespace Tests.Resources
{
    public class SmartPropEvaluatorTest
    {
        private const string Header = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n";

        private const string ChoiceDefinition = """
            {
                m_Variables =
                [
                    { _class = "CSmartPropVariable_Bool" m_VariableName = "Flag" m_nElementID = 10 },
                    { _class = "CSmartPropVariable_Int" m_VariableName = "Depth" m_nElementID = 11 m_DefaultValue = -1 },
                ]
                m_Choices =
                [
                    {
                        m_nElementID = 20
                        m_Name = "Colour"
                        m_DefaultOption = "Red"
                        m_Options =
                        [
                            {
                                m_Name = "red"
                                m_VariableValues =
                                [
                                    { m_TargetName = "Flag" m_DataType = "BOOL" m_Value = true },
                                    { m_TargetName = "Depth" m_DataType = "INTEGER" m_Value = { m_Expression = "EvaluationDepth()" } },
                                ]
                            },
                            { m_Name = "blue" m_VariableValues = [] },
                        ]
                    },
                ]
                m_Children =
                [
                    { _class = "CSmartPropElement_Model" m_nElementID = 1 m_sModelName = "models/flag.vmdl" m_bEnabled = { m_SourceName = "Flag" } },
                    { _class = "CSmartPropElement_Model" m_nElementID = 2 m_sModelName = "models/depth.vmdl" m_bEnabled = { m_Expression = "Depth == 0" } },
                ]
            }
            """;

        private static SmartPropDefinition Load(string body, bool isLegacy = false) => SmartPropDefinition.Load(TestFixtures.ParseKV3(Header + body), isLegacy);

        private static SmartPropOutput Evaluate(string body, SmartPropEvaluationInput? input = null)
            => new SmartPropEvaluator(null).Evaluate(Load(body), input ?? new SmartPropEvaluationInput());

        private static string Model(string name, string extra = "")
            => $$"""{ _class = "CSmartPropElement_Model" m_nElementID = 1 m_sModelName = "{{name}}" {{extra}} }""";

        private static string[] ModelNames(SmartPropOutput output) => [.. output.Models.Select(static model => model.ModelName)];

        [Test]
        public async Task ColorVariableDefaultsToTransparentBlack()
        {
            var definition = Load("""{ m_Variables = [ { _class = "CSmartPropVariable_Color" m_VariableName = "C" } ] }""");

            await Assert.That(definition.Variables[0].DefaultValue.GetColor()).IsEqualTo(new Color32(0, 0, 0, 0));
        }

        [Test]
        public async Task MissingElementIdsAreMinusOne()
        {
            var body = """
                {
                    m_Choices = [ { m_Name = "Choice" } ]
                    m_Children = [ { _class = "CSmartPropElement_Model" m_sModelName = "models/a.vmdl" } ]
                }
                """;

            await Assert.That(Load(body).Choices[0].ElementId).IsEqualTo(-1);
            await Assert.That(Evaluate(body).Models[0].ElementPath).IsEquivalentTo([-1]);
        }

        [Test]
        public async Task ContentVersionIsRead()
        {
            await Assert.That(Load("{ m_nContentVersion = 3 }").ContentVersion).IsEqualTo(3);
        }

        [Test]
        public async Task LegacyElementIdsAreAssignedBreadthFirst()
        {
            var definition = Load("""
                {
                    m_Children =
                    [
                        { _class = "CSmartPropElement_Group" m_Children = [ { _class = "CSmartPropElement_Model" m_sModelName = "models/a1.vmdl" } ] },
                        { _class = "CSmartPropElement_Model" m_sModelName = "models/b.vmdl" },
                    ]
                    m_Variables = [ { _class = "CSmartPropVariable_Float" m_VariableName = "F" } ]
                }
                """, isLegacy: true);

            var output = new SmartPropEvaluator(null).Evaluate(definition, new SmartPropEvaluationInput());

            await Assert.That(output.Models[0].ElementPath).IsEquivalentTo([1, 3]);
            await Assert.That(output.Models[1].ElementPath).IsEquivalentTo([2]);
            await Assert.That(definition.Variables[0].ElementId).IsEqualTo(4);
        }

        [Test]
        public async Task ChoiceDefaultOptionMatchesCaseInsensitively()
        {
            var output = Evaluate(ChoiceDefinition);

            await Assert.That(ModelNames(output)).IsEquivalentTo(["models/flag.vmdl", "models/depth.vmdl"]);
        }

        [Test]
        public async Task ChoicesApplyOnlyWithAParameterOverrideSet()
        {
            var output = Evaluate(ChoiceDefinition, new SmartPropEvaluationInput { ParameterOverrides = null });

            await Assert.That(output.Models).IsEmpty();
        }

        [Test]
        [Arguments("RED", true)]
        [Arguments("blue", false)]
        public async Task ChoiceOverrideSelectsAnOption(string option, bool expected)
        {
            var overrides = new Dictionary<int, SmartPropValue> { [20] = SmartPropValue.FromString(option) };
            var output = Evaluate(ChoiceDefinition, new SmartPropEvaluationInput { ParameterOverrides = overrides });

            await Assert.That(output.Models.Count == 2).IsEqualTo(expected);
        }

        [Test]
        public async Task ChoiceOverrideThatIsNotAStringSelectsNoOption()
        {
            var overrides = new Dictionary<int, SmartPropValue> { [20] = SmartPropValue.FromInt(0) };
            var output = Evaluate(ChoiceDefinition, new SmartPropEvaluationInput { ParameterOverrides = overrides });

            await Assert.That(output.Models).IsEmpty();
        }

        [Test]
        public async Task StoredElementStatesKeepTheFirstRecordAndReturnOnlyUsedOnes()
        {
            var body = $$"""
                {
                    m_Children =
                    [
                        { _class = "CSmartPropElement_Model" m_nElementID = 5 m_sModelName = "models/a.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_Probability" } ] },
                        { _class = "CSmartPropElement_Model" m_nElementID = 6 m_sModelName = "models/b.vmdl" },
                    ]
                }
                """;

            var output = Evaluate(body, new SmartPropEvaluationInput
            {
                ElementStates =
                [
                    new SmartPropElementState { ElementPath = [5], RandomSeed = 111 },
                    new SmartPropElementState { ElementPath = [5], RandomSeed = 222 },
                    new SmartPropElementState { ElementPath = [99], RandomSeed = 333 },
                ],
            });

            await Assert.That(output.ElementStates.Count).IsEqualTo(1);
            await Assert.That(output.ElementStates[0].ElementPath).IsEquivalentTo([5]);
            await Assert.That(output.ElementStates[0].RandomSeed).IsEqualTo(111);
        }

        [Test]
        public async Task BoolReadOfAFloatTruncates()
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_Float" m_VariableName = "F" m_DefaultValue = 0.5 } ]
                    m_Children =
                    [
                        {{Model("models/half.vmdl", """m_bEnabled = { m_SourceName = "F" }""")}},
                        {{Model("models/yes.vmdl", """m_bEnabled = "yes" """)}},
                    ]
                }
                """);

            await Assert.That(ModelNames(output)).IsEquivalentTo(["models/yes.vmdl"]);
        }

        [Test]
        [Arguments("\"1 2 3\"", 1f, 2f, 3f)]
        [Arguments("[2, 3]", 0f, 0f, 0f)]
        [Arguments("4", 0f, 0f, 0f)]
        [Arguments("""{ m_Components = 5 m_SourceName = "V" }""", 1f, 1f, 1f)]
        public async Task VectorLiteralsConvertWithTheExactCount(string literal, float x, float y, float z)
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_Vector3D" m_VariableName = "V" m_DefaultValue = [5, 5, 5] } ]
                    m_Children = [ {{Model("models/a.vmdl", "m_vModelScale = " + literal)}} ]
                }
                """);

            await Assert.That(output.Models[0].ModelScale).IsEqualTo(new Vector3(x, y, z));
        }

        [Test]
        [Arguments("\"UP\"", 0f, 0f, 1f)]
        [Arguments("2", 1f, 0f, 0f)]
        [Arguments("""{ m_Components = [ "UP" ] }""", 1f, 0f, 0f)]
        [Arguments("""{ m_Components = [ 1 ] }""", 0f, 1f, 0f)]
        public async Task EnumLiteralsAreNamesExceptInComponents(string literal, float x, float y, float z)
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_Vector3D" m_VariableName = "V" } ]
                    m_Children =
                    [
                        {{Model("models/a.vmdl", $$"""
                            m_Modifiers = [ { _class = "CSmartPropOperation_SaveDirection" m_DirectionVector = {{literal}} m_VariableName = "V" } ]
                            m_vModelScale = { m_SourceName = "V" }
                            """)}},
                    ]
                }
                """);

            await Assert.That(output.Models[0].ModelScale).IsEqualTo(new Vector3(x, y, z));
        }

        [Test]
        public async Task SetVariableEvaluatesEvenWhenTheVariableIsMissing()
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_Float" m_VariableName = "R" } ]
                    m_Children =
                    [
                        {{Model("models/a.vmdl", """
                            m_Modifiers =
                            [
                                { _class = "CSmartPropOperation_SetVariable" m_VariableValue = { m_TargetName = "Missing" m_DataType = "FLOAT" m_Value = { m_Expression = "RandomFloat(0, 1)" } } },
                                { _class = "CSmartPropOperation_SetVariable" m_VariableValue = { m_TargetName = "R" m_DataType = "FLOAT" m_Value = { m_Expression = "RandomFloat(0, 1)" } } },
                            ]
                            m_vModelScale = { m_Components = [ { m_Expression = "R" }, 1, 1 ] }
                            """)}},
                    ]
                }
                """);

            var stream = ElementStream();
            stream.RandomFloat();

            await Assert.That(output.Models[0].ModelScale.X).IsEqualTo(stream.RandomFloat());
        }

        [Test]
        public async Task VectorBetweenPointsEvaluatesTheFirstPointFirst()
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_Vector3D" m_VariableName = "V" } ]
                    m_Children =
                    [
                        {{Model("models/a.vmdl", """
                            m_Modifiers =
                            [
                                {
                                    _class = "CSmartPropOperation_ComputeVectorBetweenPoints3D"
                                    m_InputPositionA = { m_Components = [ { m_Expression = "RandomFloat(0, 1)" }, 0, 0 ] }
                                    m_InputPositionB = { m_Components = [ { m_Expression = "RandomFloat(0, 10)" }, 0, 0 ] }
                                    m_OutputVariableName = "V"
                                },
                            ]
                            m_vModelScale = { m_Components = [ { m_Expression = "V.x" }, 1, 1 ] }
                            """)}},
                    ]
                }
                """);

            var stream = ElementStream();
            var a = stream.RandomFloat(0f, 1f);
            var b = stream.RandomFloat(0f, 10f);

            await Assert.That(output.Models[0].ModelScale.X).IsEqualTo(b - a).Within(1e-5f);
        }

        [Test]
        public async Task RestoreStateNameIsAnAttribute()
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_String" m_VariableName = "StateName" m_DefaultValue = "Moved" } ]
                    m_Children =
                    [
                        {
                            _class = "CSmartPropElement_Group"
                            m_nElementID = 1
                            m_Modifiers =
                            [
                                { _class = "CSmartPropOperation_Translate" m_vPosition = [10, 0, 0] },
                                { _class = "CSmartPropOperation_SaveState" m_StateName = "Moved" },
                            ]
                        },
                        {{Model("models/a.vmdl", """
                            m_Modifiers = [ { _class = "CSmartPropOperation_RestoreState" m_StateName = { m_SourceName = "StateName" } m_bDiscardIfUknown = true } ]
                            """)}},
                    ]
                }
                """);

            await Assert.That(output.Models[0].Transform.Position).IsEqualTo(new Vector3(10f, 0f, 0f));
        }

        [Test]
        public async Task ExpressionFilterExpressionIsPlainText()
        {
            var output = Evaluate($$"""
                {
                    m_Variables = [ { _class = "CSmartPropVariable_String" m_VariableName = "E" m_DefaultValue = "1" } ]
                    m_Children = [ {{Model("models/a.vmdl", """m_Modifiers = [ { _class = "CSmartPropFilter_Expression" m_Expression = { m_SourceName = "E" } } ]""")}} ]
                }
                """);

            await Assert.That(output.Models).IsEmpty();
        }

        [Test]
        public async Task CircleWithDownwardPlaneTurnsAroundY()
        {
            var output = Evaluate($$"""
                {
                    m_Children =
                    [
                        {
                            _class = "CSmartPropElement_PlaceInSphere"
                            m_nElementID = 1
                            m_PlacementMode = "CIRCLE"
                            m_DistributionMode = "REGULAR"
                            m_vPlaneUpDirection = [0, 0, -1]
                            m_flPositionRadiusInner = 10.0
                            m_flPositionRadiusOuter = 10.0
                            m_Children = [ {{Model("models/a.vmdl")}} ]
                        },
                    ]
                }
                """);

            var position = output.Models[0].Transform.Position;

            await Assert.That(position.X).IsEqualTo(-10f).Within(1e-4f);
            await Assert.That(position.Y).IsEqualTo(0f).Within(1e-4f);
            await Assert.That(position.Z).IsEqualTo(0f).Within(1e-4f);
        }

        private const string SurfaceFilterDefinition = """
            {
                m_Children =
                [
                    { _class = "CSmartPropElement_Model" m_nElementID = 1 m_sModelName = "models/allowed.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_SurfaceProperties" m_AllowedSurfaceProperties = [ "metal" ] } ] },
                    { _class = "CSmartPropElement_Model" m_nElementID = 2 m_sModelName = "models/disallowed.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_SurfaceProperties" m_DisallowedSurfaceProperties = [ "METAL" ] } ] },
                    { _class = "CSmartPropElement_Model" m_nElementID = 3 m_sModelName = "models/dry.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_MaterialAttributes" m_AllowedMaterialAttributes = [ "isdry" ] } ] },
                    { _class = "CSmartPropElement_Model" m_nElementID = 4 m_sModelName = "models/wet.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_MaterialAttributes" m_AllowedMaterialAttributes = [ "iswet" ] m_DisallowedMaterialAttributes = [ "isdry" ] } ] },
                    { _class = "CSmartPropElement_Model" m_nElementID = 5 m_sModelName = "models/notdry.vmdl" m_Modifiers = [ { _class = "CSmartPropFilter_MaterialAttributes" m_DisallowedMaterialAttributes = [ "isdry" ] } ] },
                ]
            }
            """;

        [Test]
        public async Task SurfaceFiltersReadTheSurfaceMaterialThroughTheHost()
        {
            var output = Evaluate(SurfaceFilterDefinition, new SmartPropEvaluationInput
            {
                Trace = static query => new SmartPropTraceHit(query.Start, Vector3.UnitZ, "materials/floor.vmat"),
                GetSurfaceProperty = static material => material == "materials/floor.vmat" ? "Metal" : null,
                GetMaterialAttribute = static (material, attribute) => material == "materials/floor.vmat" && attribute == "iswet" ? 1 : 0,
            });

            await Assert.That(ModelNames(output)).IsEquivalentTo(["models/allowed.vmdl", "models/wet.vmdl", "models/notdry.vmdl"]);
        }

        [Test]
        public async Task SurfaceFiltersWithoutAHostSeeNoProperties()
        {
            var output = Evaluate(SurfaceFilterDefinition);

            await Assert.That(ModelNames(output)).IsEquivalentTo(["models/disallowed.vmdl", "models/notdry.vmdl"]);
        }

        [Test]
        public async Task OppositeVectorsTurnAroundTheEngineAxis()
        {
            var down = SmartPropMath.QuaternionFromTwoVectors(Vector3.UnitZ, -Vector3.UnitZ);
            var back = SmartPropMath.QuaternionFromTwoVectors(Vector3.UnitX, -Vector3.UnitX);

            await Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitX, down), -Vector3.UnitX)).IsLessThan(1e-5f);
            await Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitY, down), Vector3.UnitY)).IsLessThan(1e-5f);
            await Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitZ, back), -Vector3.UnitZ)).IsLessThan(1e-5f);
            await Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitY, back), Vector3.UnitY)).IsLessThan(1e-5f);
        }

        /// <summary>The random stream of the first element evaluated with master seed 0.</summary>
        private static UniformRandomStream ElementStream()
        {
            var master = new UniformRandomStream(0);
            return new UniformRandomStream(master.RandomInt(0, 0x7FFFFFFF));
        }
    }
}

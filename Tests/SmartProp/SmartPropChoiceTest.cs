using System.Linq;
using System.Threading.Tasks;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropChoiceTest
    {
        private static KVObject MakeRootWithChoices(KVObject[] variables, KVObject[] choices, params KVObject[] children)
        {
            var root = KVObject.Collection();
            root["generic_data_type"] = new KVObject("CSmartPropRoot");

            var varsArray = KVObject.Array();
            foreach (var v in variables)
            {
                varsArray.Add(v);
            }
            root["m_Variables"] = varsArray;

            var choicesArray = KVObject.Array();
            foreach (var c in choices)
            {
                choicesArray.Add(c);
            }
            root["m_Choices"] = choicesArray;

            var childArray = KVObject.Array();
            foreach (var child in children)
            {
                childArray.Add(child);
            }
            root["m_Children"] = childArray;

            return root;
        }

        private static KVObject MakeVariable(string name, string type, object? defaultValue)
        {
            var v = KVObject.Collection();
            v["generic_data_type"] = new KVObject($"CSmartPropVariable_{type}");
            v["m_VariableName"] = new KVObject(name);
            if (defaultValue != null)
            {
                v["m_DefaultValue"] = defaultValue switch
                {
                    bool b => new KVObject(b),
                    int i => new KVObject(i),
                    float f => new KVObject(f),
                    string s => new KVObject(s),
                    _ => new KVObject(defaultValue.ToString()!),
                };
            }
            return v;
        }

        private static KVObject MakeChoice(string name, string defaultOption, params KVObject[] options)
        {
            var choice = KVObject.Collection();
            choice["generic_data_type"] = new KVObject("CSmartPropChoice");
            choice["m_Name"] = new KVObject(name);
            choice["m_DefaultOption"] = new KVObject(defaultOption);

            var optionsArray = KVObject.Array();
            foreach (var opt in options)
            {
                optionsArray.Add(opt);
            }
            choice["m_Options"] = optionsArray;
            return choice;
        }

        private static KVObject MakeChoiceOption(string name, string displayName, params (string VarName, object Value)[] varValues)
        {
            var opt = KVObject.Collection();
            opt["generic_data_type"] = new KVObject("CSmartPropChoiceOption");
            opt["m_Name"] = new KVObject(name);
            opt["m_DisplayName"] = new KVObject(displayName);

            var valsArray = KVObject.Array();
            foreach (var (varName, val) in varValues)
            {
                var valObj = KVObject.Collection();
                valObj["m_TargetName"] = new KVObject(varName);
                valObj["m_Value"] = val switch
                {
                    bool b => new KVObject(b),
                    int i => new KVObject(i),
                    float f => new KVObject(f),
                    string s => new KVObject(s),
                    _ => new KVObject(val.ToString()!),
                };
                valsArray.Add(valObj);
            }
            opt["m_VariableValues"] = valsArray;
            return opt;
        }

        [Test]
        public async Task ReadChoicesParsesOptionsAndDefaults()
        {
            var opt1 = MakeChoiceOption("wood", "Wood Material", ("material", "wood.vmat"), ("roughness", 0.8f));
            var opt2 = MakeChoiceOption("metal", "Metal Material", ("material", "metal.vmat"), ("roughness", 0.2f));
            var choice = MakeChoice("MaterialStyle", "metal", opt1, opt2);

            var root = MakeRootWithChoices([], [choice]);
            var choices = SmartPropChoiceMap.ReadChoices(root);

            await Assert.That(choices).HasSingleItem();
            var parsed = choices[0];
            await Assert.That(parsed.Name).IsEqualTo("MaterialStyle");
            await Assert.That(parsed.DefaultOption).IsEqualTo("metal");
            await Assert.That(parsed.Options.Count).IsEqualTo(2);

            await Assert.That(parsed.Options[0].Name).IsEqualTo("wood");
            await Assert.That(parsed.Options[0].DisplayName).IsEqualTo("Wood Material");
            await Assert.That(parsed.Options[0].VariableValues["material"]).IsEqualTo("wood.vmat");
            await Assert.That(parsed.Options[0].VariableValues["roughness"]).IsEqualTo(0.8f);

            await Assert.That(parsed.Options[1].Name).IsEqualTo("metal");
            await Assert.That(parsed.Options[1].DisplayName).IsEqualTo("Metal Material");
            await Assert.That(parsed.Options[1].VariableValues["material"]).IsEqualTo("metal.vmat");
        }

        [Test]
        public async Task BuildAppliesDefaultChoiceOverrides()
        {
            var varMat = MakeVariable("material", "String", "default.vmat");
            var varRough = MakeVariable("roughness", "Float", 0.5f);

            var opt1 = MakeChoiceOption("wood", "Wood Material", ("material", "wood.vmat"), ("roughness", 0.8f));
            var opt2 = MakeChoiceOption("metal", "Metal Material", ("material", "metal.vmat"), ("roughness", 0.2f));
            var choice = MakeChoice("MaterialStyle", "metal", opt1, opt2);

            var root = MakeRootWithChoices([varMat, varRough], [choice]);
            var variables = SmartPropVariableMap.Build(root);

            // Default choice is "metal", which overrides material and roughness
            await Assert.That(variables["material"]).IsEqualTo("metal.vmat");
            await Assert.That(variables["roughness"]).IsEqualTo(0.2f);
        }

        [Test]
        public async Task BuildAppliesExplicitChoiceSelection()
        {
            var varMat = MakeVariable("material", "String", "default.vmat");
            var varRough = MakeVariable("roughness", "Float", 0.5f);

            var opt1 = MakeChoiceOption("wood", "Wood Material", ("material", "wood.vmat"), ("roughness", 0.8f));
            var opt2 = MakeChoiceOption("metal", "Metal Material", ("material", "metal.vmat"), ("roughness", 0.2f));
            var choice = MakeChoice("MaterialStyle", "metal", opt1, opt2);

            var root = MakeRootWithChoices([varMat, varRough], [choice]);

            var selected = new Dictionary<string, string>
            {
                ["MaterialStyle"] = "wood",
            };

            var variables = SmartPropVariableMap.Build(root, selected);

            await Assert.That(variables["material"]).IsEqualTo("wood.vmat");
            await Assert.That(variables["roughness"]).IsEqualTo(0.8f);
        }

        [Test]
        public async Task ReadChoicesWithNullOrMissingReturnsEmpty()
        {
            await Assert.That(SmartPropChoiceMap.ReadChoices(null)).IsEmpty();

            var empty = KVObject.Collection();
            await Assert.That(SmartPropChoiceMap.ReadChoices(empty)).IsEmpty();
        }
    }
}

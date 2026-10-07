using ValveResourceFormat.ResourceTypes.SmartProps.Elements;
using ValveResourceFormat.ResourceTypes.SmartProps.Modifiers;

namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    internal enum SmartPropSpace
    {
        World,
        Object,
        Element,
    }

    /// <summary>
    /// The part of the evaluation state that isolating elements restore and Save/RestoreState copies.
    /// </summary>
    internal record struct SmartPropStateBlock(SmartPropTransform Transform, Color32 Tint, string? SurfaceMaterial, Vector3 SurfaceNormal);

    /// <summary>
    /// The evaluation context: current element state, element path, random streams, variables and output.
    /// </summary>
    internal sealed class SmartPropContext : ISmartPropExpressionContext
    {
        public const int MaxEvaluatedElements = 50000;
        public const int MaxModelInstances = 10000;

        private const int RandomFresh = 0;
        private const int RandomSeeded = 1;
        private const int RandomFixed = 2;

        private readonly SmartPropEvaluator evaluator;
        private readonly SmartPropEvaluationInput input;
        private readonly UniformRandomStream master;
        private readonly UniformRandomStream elementStream = new(0);
        private int randomState = RandomFixed;
        private readonly List<int> elementPath = [];
        private readonly Dictionary<int[], SmartPropElementState> elementStates = new(ElementPathComparer.Instance);
        private readonly List<SmartPropDefinition> definitionStack = [];
        private Dictionary<string, SmartPropStateBlock> savedStates = new(StringComparer.OrdinalIgnoreCase);
        private SmartPropVariableTable variables = new();

        public SmartPropOutput Output { get; } = new();
        public int EvaluatedElements { get; set; }

        public SmartPropStateBlock State;
        public SmartPropTransform ObjectTransform { get; }
        public Color32 ObjectTint { get; }

        public float LineLength { get; set; }
        public int InstanceCount { get; set; }
        public int InstanceIndex { get; set; }
        public float PathParameter { get; set; }
        public float LinearScale { get; set; } = 1f;
        public float UScale { get; set; } = 1f;
        public float VScale { get; set; } = 1f;

        public int DeformerIndex { get; set; } = -1;
        public int MaterialOverrideSetIndex { get; set; } = -1;
        public bool MaterialOverrideSetOwned { get; set; }
        public int MaterialTintSetIndex { get; set; } = -1;
        public bool MaterialTintSetOwned { get; set; }

        public int EvaluationDepth => definitionStack.Count;
        public int MaxEvaluationDepth { get; private set; } = 1000;

        public SmartPropContext(SmartPropEvaluator evaluator, SmartPropEvaluationInput input)
        {
            this.evaluator = evaluator;
            this.input = input;
            master = new UniformRandomStream(input.MasterSeed);
            ObjectTransform = input.Placement;
            ObjectTint = input.Tint;
            State = new SmartPropStateBlock(input.Placement, input.Tint, null, Vector3.Zero);

            if (input.ElementStates != null)
            {
                foreach (var state in input.ElementStates)
                {
                    var copy = new SmartPropElementState
                    {
                        ElementPath = state.ElementPath,
                        RandomSeed = state.RandomSeed,
                        ChoiceValue = state.ChoiceValue,
                    };

                    foreach (var (name, edit) in state.HandleEdits)
                    {
                        copy.HandleEdits[name] = edit;
                    }

                    elementStates[state.ElementPath] = copy;
                }
            }
        }

        public SmartPropTransform Transform
        {
            get => State.Transform;
            set => State.Transform = value;
        }

        public void Run(SmartPropDefinition root)
        {
            var origin = input.Placement.Position;

            if (input.Trace != null && input.Trace(new SmartPropTraceQuery(origin + new Vector3(0f, 0f, 100f), -Vector3.UnitZ, 200f)) is { } hit)
            {
                State.SurfaceMaterial = hit.Material;
                State.SurfaceNormal = hit.Normal;
            }

            EvaluateSmartProp(root, null);

            Output.ElementStates.AddRange(elementStates.Values);
        }

        public SmartPropTraceHit? Trace(SmartPropTraceQuery query) => input.Trace?.Invoke(query);

        public SmartPropDefinition? LoadDefinition(string name) => evaluator.LoadDefinition(name);

        public SmartPropDefinition CurrentDefinition => definitionStack[^1];

        #region Variables and expressions

        public SmartPropValue GetVariable(string name) => variables.Find(name)?.Value ?? SmartPropValue.Null;

        public SmartPropVariableTable.Entry? FindVariable(string name) => string.IsNullOrEmpty(name) ? null : variables.Find(name);

        public void SetVariable(string name, SmartPropValue value) => FindVariable(name)?.SetValue(value);

        public float EvaluateExpression(string text)
        {
            var expression = CurrentDefinition.GetExpression(text, variables);
            return expression?.Evaluate(this) ?? 0f;
        }

        public bool EvaluateExpressionBool(string text) => MathF.Abs(EvaluateExpression(text)) > 0.0001f;

        float ISmartPropExpressionContext.GetVariableComponent(int variableIndex, int component) => variables.Entries[variableIndex].Cache[component];

        #endregion

        #region Random

        /// <summary>
        /// The element random stream: seeded from the current element path's stored seed on first use in an
        /// element (a missing seed is drawn from the master stream and stored), restarted from seed 0 for every
        /// draw once a child element has finished.
        /// </summary>
        public UniformRandomStream GetRandomStream()
        {
            if (randomState == RandomFresh)
            {
                var state = GetElementState();

                if (state.RandomSeed == SmartPropElementState.Unset)
                {
                    state.RandomSeed = master.RandomInt(0, 0x7FFFFFFF);
                }

                elementStream.SetSeed(state.RandomSeed);
                randomState = RandomSeeded;
            }
            else if (randomState == RandomFixed)
            {
                elementStream.SetSeed(0);
            }

            return elementStream;
        }

        public UniformRandomStream MasterStream => master;

        public SmartPropElementState GetElementState()
        {
            var path = elementPath.ToArray();

            if (!elementStates.TryGetValue(path, out var state))
            {
                state = new SmartPropElementState { ElementPath = path };
                elementStates[path] = state;
            }

            return state;
        }

        private void ResetRandom(int state)
        {
            randomState = state;
            elementStream.SetSeed(0);
        }

        #endregion

        #region Driver

        public bool CanEvaluate => EvaluatedElements < MaxEvaluatedElements && Output.Models.Count < MaxModelInstances;

        public void EvaluateSmartProp(SmartPropDefinition definition, SmartPropVariableTable? outer)
        {
            if (EvaluationDepth >= MaxEvaluationDepth || !CanEvaluate)
            {
                return;
            }

            var isRoot = definitionStack.Count == 0;
            var savedVariables = variables;
            var outerStates = savedStates;
            savedStates = new(StringComparer.OrdinalIgnoreCase);

            variables = SmartPropVariableTable.Build(definition, outer, input.VariableOverrides, isRoot ? input.ParameterOverrides : null);
            definitionStack.Add(definition);

            if (isRoot)
            {
                ApplyChoiceOptions(definition);
                MaxEvaluationDepth = Math.Min(definition.MaxDepth.EvaluateInt(this), 1000);
            }

            ResetRandom(RandomFresh);

            if (ApplyModifiers(definition.Modifiers))
            {
                foreach (var child in definition.Children)
                {
                    EvaluateElement(child);
                }
            }

            ResetRandom(RandomFixed);
            definitionStack.RemoveAt(definitionStack.Count - 1);
            variables = savedVariables;
            savedStates = outerStates;
        }

        public void EvaluateNestedSmartProp(SmartPropDefinition definition) => EvaluateSmartProp(definition, variables);

        private void ApplyChoiceOptions(SmartPropDefinition definition)
        {
            foreach (var choice in definition.Choices)
            {
                var optionName = input.ParameterOverrides != null && input.ParameterOverrides.TryGetValue(choice.ElementId, out var chosen)
                    ? chosen.GetString()
                    : choice.DefaultOption;

                var option = choice.Options.Find(o => o.Name == optionName);

                if (option == null)
                {
                    continue;
                }

                foreach (var assignment in option.Values)
                {
                    SetVariable(assignment.TargetName, assignment.Evaluate(this));
                }
            }
        }

        public bool ApplyModifiers(List<SmartPropModifier> modifiers)
        {
            foreach (var modifier in modifiers)
            {
                if (!modifier.Enabled.EvaluateBool(this))
                {
                    continue;
                }

                if (modifier.Apply(this))
                {
                    return false;
                }
            }

            return true;
        }

        public void EvaluateChildren(List<SmartPropElement> children)
        {
            foreach (var child in children)
            {
                EvaluateElement(child);
            }
        }

        public void EvaluateElement(SmartPropElement element)
        {
            if (!element.Enabled.EvaluateBool(this) || !CanEvaluate)
            {
                return;
            }

            var isolate = element.IsolatesState(this);
            var savedState = State;
            var savedOverrideSet = MaterialOverrideSetIndex;
            var savedLineLength = LineLength;
            var savedDeformer = DeformerIndex;

            ResetRandom(RandomFresh);
            elementPath.Add(element.ElementId);

            if (ApplyModifiers(element.Modifiers))
            {
                element.Evaluate(this);
                EvaluatedElements++;
            }

            elementPath.RemoveAt(elementPath.Count - 1);
            ResetRandom(RandomFixed);

            if (isolate)
            {
                State = savedState;
                MaterialOverrideSetIndex = savedOverrideSet;
            }

            LineLength = savedLineLength;
            DeformerIndex = savedDeformer;
        }

        public void PushPath(int value) => elementPath.Add(value);

        public void PopPath() => elementPath.RemoveAt(elementPath.Count - 1);

        /// <summary>Saves the instance values (count, index, path parameter, linear scale, U/V scale).</summary>
        public (int, int, float, float, float, float) SaveInstanceValues() => (InstanceCount, InstanceIndex, PathParameter, LinearScale, UScale, VScale);

        public void RestoreInstanceValues((int, int, float, float, float, float) values)
            => (InstanceCount, InstanceIndex, PathParameter, LinearScale, UScale, VScale) = values;

        #endregion

        #region Saved states

        public void SaveState(string name) => savedStates[name] = State;

        public bool RestoreState(string name)
        {
            if (!savedStates.TryGetValue(name, out var state))
            {
                return false;
            }

            State = state;
            return true;
        }

        #endregion

        #region Spaces

        private SmartPropTransform SpaceTransform(SmartPropSpace space) => space == SmartPropSpace.Object ? ObjectTransform : Transform;

        public Vector3 ConvertPosition(SmartPropSpace to, SmartPropSpace from, Vector3 point)
        {
            if (to == from)
            {
                return point;
            }

            var world = from switch
            {
                SmartPropSpace.World => point,
                SmartPropSpace.Object or SmartPropSpace.Element => SpaceTransform(from).TransformPoint(point),
                _ => Vector3.Zero,
            };

            return to switch
            {
                SmartPropSpace.World => world,
                SmartPropSpace.Object or SmartPropSpace.Element => SpaceTransform(to).InverseTransformPoint(world),
                _ => Vector3.Zero,
            };
        }

        public Vector3 ConvertDirection(SmartPropSpace to, SmartPropSpace from, Vector3 direction)
        {
            if (to == from)
            {
                return direction;
            }

            var world = from switch
            {
                SmartPropSpace.World => direction,
                SmartPropSpace.Object or SmartPropSpace.Element => Vector3.Transform(direction, SpaceTransform(from).Rotation),
                _ => Vector3.Zero,
            };

            return to switch
            {
                SmartPropSpace.World => world,
                SmartPropSpace.Object or SmartPropSpace.Element => Vector3.Transform(world, Quaternion.Conjugate(Quaternion.Normalize(SpaceTransform(to).Rotation))),
                _ => Vector3.Zero,
            };
        }

        #endregion

        #region Output

        public SmartPropModelInstance AddModelInstance(SmartPropModelInstance instance)
        {
            Output.Models.Add(instance);
            MaterialOverrideSetOwned = false;
            MaterialTintSetOwned = false;
            return instance;
        }

        public int[] CurrentElementPath => elementPath.ToArray();

        #endregion

        private sealed class ElementPathComparer : IEqualityComparer<int[]>
        {
            public static readonly ElementPathComparer Instance = new();

            public bool Equals(int[]? x, int[]? y) => x.AsSpan().SequenceEqual(y);

            public int GetHashCode(int[] obj)
            {
                var hash = new HashCode();

                foreach (var value in obj)
                {
                    hash.Add(value);
                }

                return hash.ToHashCode();
            }
        }
    }
}

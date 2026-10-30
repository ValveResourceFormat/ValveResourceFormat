namespace ValveResourceFormat.Source1.Model;

/// <summary>An input slider of the facial animation of a Source 1 model.</summary>
internal sealed class Source1FlexController
{
    /// <summary>Controller name.</summary>
    public required string Name { get; init; }

    /// <summary>Controller type, the group of the face it drives.</summary>
    public required string Type { get; init; }

    /// <summary>Smallest value of the controller.</summary>
    public required float Min { get; init; }

    /// <summary>Largest value of the controller.</summary>
    public required float Max { get; init; }

    /// <summary>Position of the controller in the value array of <see cref="Source1Model.EvaluateFlexRules"/>. Controllers of the same name share a position.</summary>
    public required int ValueIndex { get; init; }
}

/// <summary>An operation of a flex rule, which runs on a stack of floats.</summary>
internal enum Source1FlexOpCode
{
    /// <summary>Pushes <see cref="Source1FlexOp.Value"/>.</summary>
    Const = 1,

    /// <summary>Pushes the value of a flex controller.</summary>
    Fetch1 = 2,

    /// <summary>Pushes the weight of a flex computed by an earlier rule.</summary>
    Fetch2 = 3,

    /// <summary>Adds the top two values.</summary>
    Add = 4,

    /// <summary>Subtracts the top value from the one below.</summary>
    Sub = 5,

    /// <summary>Multiplies the top two values.</summary>
    Mul = 6,

    /// <summary>Divides the second value by the top one, which gives 0 for a divisor that is not positive.</summary>
    Div = 7,

    /// <summary>Negates the top value.</summary>
    Neg = 8,

    /// <summary>Not implemented by the engine.</summary>
    Exp = 9,

    /// <summary>Only used while parsing expressions.</summary>
    Open = 10,

    /// <summary>Only used while parsing expressions.</summary>
    Close = 11,

    /// <summary>Only used while parsing expressions.</summary>
    Comma = 12,

    /// <summary>Keeps the larger of the top two values.</summary>
    Max = 13,

    /// <summary>Keeps the smaller of the top two values.</summary>
    Min = 14,

    /// <summary>Pushes the first half of a two way controller, ramping from 1 to 0 over the values -1 to 0.</summary>
    TwoWay0 = 15,

    /// <summary>Pushes the second half of a two way controller, ramping from 0 to 1 over the values 0 to 1.</summary>
    TwoWay1 = 16,

    /// <summary>Takes four ramp values and a value controller index from the stack and pushes the controller scaled by the ramp.</summary>
    NWay = 17,

    /// <summary>Multiplies the top N values.</summary>
    Combo = 18,

    /// <summary>Lowers the value below the top N values by the product of those.</summary>
    Dominate = 19,

    /// <summary>Pushes the weight of a lower eyelid from the eye up and down, close lid and close lid blend controllers.</summary>
    DmeLowerEyelid = 20,

    /// <summary>Pushes the weight of an upper eyelid from the eye up and down, close lid and close lid blend controllers.</summary>
    DmeUpperEyelid = 21,
}

/// <summary>One operation of a flex rule.</summary>
/// <param name="Code">The operation.</param>
/// <param name="Index">The controller index or count the operation reads.</param>
/// <param name="Value">The constant the operation pushes.</param>
internal readonly record struct Source1FlexOp(Source1FlexOpCode Code, int Index, float Value);

/// <summary>A rule that computes the weight of one flex from the flex controllers.</summary>
internal sealed class Source1FlexRule
{
    /// <summary>Index of the flex the rule sets, see <see cref="Source1Model.FlexDescriptions"/>.</summary>
    public required int Flex { get; init; }

    /// <summary>Operations, run in order.</summary>
    public required Source1FlexOp[] Ops { get; init; }
}

/// <summary>A vertex moved by a <see cref="Source1MeshFlex"/>.</summary>
/// <param name="VertexIndex">Index into <see cref="Source1Mesh.Vertices"/>.</param>
/// <param name="Side">How much the vertex follows the weight of the paired flex instead of the flex itself, from 0 to 1.</param>
/// <param name="Delta">Position change at full weight.</param>
/// <param name="NormalDelta">Normal change at full weight.</param>
/// <param name="Wrinkle">Wrinkle map blend change at full weight, 0 for a flex without wrinkle data.</param>
internal readonly record struct Source1FlexVertex(int VertexIndex, float Side, Vector3 Delta, Vector3 NormalDelta, float Wrinkle);

/// <summary>A morph target of a mesh, driven by the weight of a flex.</summary>
internal sealed class Source1MeshFlex
{
    /// <summary>Index of the flex that drives this target, see <see cref="Source1Model.FlexDescriptions"/>.</summary>
    public required int FlexDesc { get; init; }

    /// <summary>Index of the flex that drives the vertices of the other side, or 0 when the target is not stereo.</summary>
    public required int FlexPair { get; init; }

    /// <summary>The weight below which the target has no effect.</summary>
    public required float Target0 { get; init; }

    /// <summary>The weight from which the target is at full effect.</summary>
    public required float Target1 { get; init; }

    /// <summary>The weight up to which the target stays at full effect.</summary>
    public required float Target2 { get; init; }

    /// <summary>The weight above which the target has no effect.</summary>
    public required float Target3 { get; init; }

    /// <summary>Moved vertices.</summary>
    public required Source1FlexVertex[] Vertices { get; init; }

    /// <summary>Converts the weight of a flex into the share of the target that applies, ramping up from <see cref="Target0"/> to <see cref="Target1"/> and down from <see cref="Target2"/> to <see cref="Target3"/>.</summary>
    /// <param name="weight">The weight of the flex.</param>
    public float Ramp(float weight)
    {
        if (weight <= Target0 || weight >= Target3)
        {
            return 0f;
        }

        if (weight < Target1)
        {
            return (weight - Target0) / (Target1 - Target0);
        }

        if (weight > Target2)
        {
            return (Target3 - weight) / (Target3 - Target2);
        }

        return 1f;
    }
}

internal sealed partial class Source1Model
{
    private const int FlexStackSize = 32;

    /// <summary>Names of the flexes, the morph targets the flex rules compute weights for.</summary>
    public string[] FlexDescriptions { get; private set; } = [];

    /// <summary>Flex controllers.</summary>
    public Source1FlexController[] FlexControllers { get; private set; } = [];

    /// <summary>Number of values <see cref="EvaluateFlexRules"/> reads, one for each controller name.</summary>
    public int FlexValueCount { get; private set; }

    /// <summary>Flex rules, in the order they run.</summary>
    public Source1FlexRule[] FlexRules { get; private set; } = [];

    /// <summary>Gets the controller values of a face at rest, which hold 0 where the range of the controller allows it.</summary>
    public float[] CreateDefaultFlexValues()
    {
        var values = new float[FlexValueCount];

        foreach (var controller in FlexControllers)
        {
            values[controller.ValueIndex] = Math.Clamp(0f, controller.Min, controller.Max);
        }

        return values;
    }

    private void ReadFlexData(MdlFile mdl)
    {
        FlexDescriptions = mdl.FlexDescNames;
        FlexControllers = new Source1FlexController[mdl.FlexControllers.Length];

        var valueIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < FlexControllers.Length; i++)
        {
            var name = mdl.FlexControllerNames[i];

            if (!valueIndices.TryGetValue(name, out var valueIndex))
            {
                valueIndex = valueIndices.Count;
                valueIndices.Add(name, valueIndex);
            }

            FlexControllers[i] = new Source1FlexController
            {
                Name = name,
                Type = mdl.FlexControllerTypes[i],
                Min = mdl.FlexControllers[i].Min,
                Max = mdl.FlexControllers[i].Max,
                ValueIndex = valueIndex,
            };
        }

        FlexValueCount = valueIndices.Count;
        FlexRules = new Source1FlexRule[mdl.FlexRules.Length];

        for (var i = 0; i < FlexRules.Length; i++)
        {
            var ops = new Source1FlexOp[mdl.FlexRuleOps[i].Length];

            for (var j = 0; j < ops.Length; j++)
            {
                var op = mdl.FlexRuleOps[i][j];
                ops[j] = new Source1FlexOp((Source1FlexOpCode)op.Op, op.Index, op.Value);
            }

            FlexRules[i] = new Source1FlexRule { Flex = mdl.FlexRules[i].Flex, Ops = ops };
        }
    }

    private static Source1MeshFlex[] BuildMeshFlexes(MdlFile.MeshFlex[] flexes, int[] outputByOriginal)
    {
        var result = new List<Source1MeshFlex>(flexes.Length);

        foreach (var flex in flexes)
        {
            var vertices = new List<Source1FlexVertex>(flex.Vertices.Length);

            foreach (var vertex in flex.Vertices)
            {
                if ((uint)vertex.Index >= (uint)outputByOriginal.Length || outputByOriginal[vertex.Index] < 0)
                {
                    continue;
                }

                vertices.Add(new Source1FlexVertex(outputByOriginal[vertex.Index], vertex.Side, vertex.Delta, vertex.NormalDelta, vertex.Wrinkle));
            }

            if (vertices.Count == 0)
            {
                continue;
            }

            result.Add(new Source1MeshFlex
            {
                FlexDesc = flex.Header.FlexDesc,
                FlexPair = flex.Header.FlexPair,
                Target0 = flex.Header.Target0,
                Target1 = flex.Header.Target1,
                Target2 = flex.Header.Target2,
                Target3 = flex.Header.Target3,
                Vertices = [.. vertices],
            });
        }

        return [.. result];
    }

    /// <summary>
    /// Converts flex controller values into the weights of the flexes by running the flex rules.
    /// </summary>
    /// <param name="values">The value of each controller in the units of its range, indexed by <see cref="Source1FlexController.ValueIndex"/>.</param>
    /// <param name="weights">Receives the weight of each flex, indexed like <see cref="FlexDescriptions"/>.</param>
    public void EvaluateFlexRules(ReadOnlySpan<float> values, Span<float> weights)
    {
        weights[..FlexDescriptions.Length].Clear();

        Span<float> stack = stackalloc float[FlexStackSize];

        foreach (var rule in FlexRules)
        {
            if ((uint)rule.Flex >= (uint)FlexDescriptions.Length)
            {
                continue;
            }

            stack.Clear();
            var top = 0;

            foreach (var op in rule.Ops)
            {
                top = RunFlexOp(op, stack, top, values, weights);
            }

            weights[rule.Flex] = stack[0];
        }
    }

    // An operation that finds too few values on the stack, no room for its result or an invalid index does nothing
    private int RunFlexOp(Source1FlexOp op, Span<float> stack, int top, ReadOnlySpan<float> values, ReadOnlySpan<float> weights)
    {
        switch (op.Code)
        {
            case Source1FlexOpCode.Add:
            case Source1FlexOpCode.Sub:
            case Source1FlexOpCode.Mul:
            case Source1FlexOpCode.Div:
            case Source1FlexOpCode.Max:
            case Source1FlexOpCode.Min:
                if (top < 2)
                {
                    return top;
                }

                stack[top - 2] = ApplyBinary(op.Code, stack[top - 2], stack[top - 1]);
                return top - 1;

            case Source1FlexOpCode.Neg:
                if (top < 1)
                {
                    return top;
                }

                stack[top - 1] = -stack[top - 1];
                return top;

            case Source1FlexOpCode.Const:
                if (top >= FlexStackSize)
                {
                    return top;
                }

                stack[top] = op.Value;
                return top + 1;

            case Source1FlexOpCode.Fetch1:
            case Source1FlexOpCode.TwoWay0:
            case Source1FlexOpCode.TwoWay1:
                if (top >= FlexStackSize || !IsFlexController(op.Index))
                {
                    return top;
                }

                var controllerValue = values[FlexControllers[op.Index].ValueIndex];

                stack[top] = op.Code switch
                {
                    Source1FlexOpCode.Fetch1 => controllerValue,
                    Source1FlexOpCode.TwoWay0 => MathUtils.RemapValClamped(controllerValue, -1f, 0f, 1f, 0f),
                    _ => MathUtils.RemapValClamped(controllerValue, 0f, 1f, 0f, 1f),
                };
                return top + 1;

            case Source1FlexOpCode.Fetch2:
                if (top >= FlexStackSize || (uint)op.Index >= (uint)FlexDescriptions.Length)
                {
                    return top;
                }

                stack[top] = weights[op.Index];
                return top + 1;

            case Source1FlexOpCode.Combo:
                return RunFlexCombo(op.Index, stack, top);

            case Source1FlexOpCode.Dominate:
                return RunFlexDominate(op.Index, stack, top);

            case Source1FlexOpCode.NWay:
                return RunFlexNWay(op.Index, stack, top, values);

            case Source1FlexOpCode.DmeLowerEyelid:
                return RunFlexEyelid(op.Index, lower: true, stack, top, values);

            case Source1FlexOpCode.DmeUpperEyelid:
                return RunFlexEyelid(op.Index, lower: false, stack, top, values);

            default:
                return top;
        }
    }

    private static float ApplyBinary(Source1FlexOpCode code, float left, float right) => code switch
    {
        Source1FlexOpCode.Add => left + right,
        Source1FlexOpCode.Sub => left - right,
        Source1FlexOpCode.Mul => left * right,
        Source1FlexOpCode.Div => right > 0.0001f ? left / right : 0f,
        Source1FlexOpCode.Max => MathF.Max(left, right),
        _ => MathF.Min(left, right),
    };

    private bool IsFlexController(int index) => (uint)index < (uint)FlexControllers.Length;

    private int RunFlexCombo(int count, Span<float> stack, int top)
    {
        if (!IsFlexController(count) || top < count || (count == 0 && top >= FlexStackSize))
        {
            return top;
        }

        var first = top - count;

        for (var i = first + 1; i < top; i++)
        {
            stack[first] *= stack[i];
        }

        return first + 1;
    }

    private int RunFlexDominate(int count, Span<float> stack, int top)
    {
        if (!IsFlexController(count) || top < count + 1)
        {
            return top;
        }

        var first = top - count;
        var dominance = stack[first];

        for (var i = first + 1; i < top; i++)
        {
            dominance *= stack[i];
        }

        stack[first - 1] *= 1f - dominance;
        return first;
    }

    private int RunFlexNWay(int controller, Span<float> stack, int top, ReadOnlySpan<float> values)
    {
        if (top < 5 || !IsFlexController(controller))
        {
            return top;
        }

        var valueController = (int)stack[top - 1];

        if (!IsFlexController(valueController))
        {
            return top;
        }

        var value = values[FlexControllers[valueController].ValueIndex];
        var amount = values[FlexControllers[controller].ValueIndex];

        var rampStart = stack[top - 5];
        var rampFullStart = stack[top - 4];
        var rampFullEnd = stack[top - 3];
        var rampEnd = stack[top - 2];

        if (value <= rampStart || value >= rampEnd)
        {
            value = 0f;
        }
        else if (value < rampFullStart)
        {
            value = MathUtils.RemapValClamped(value, rampStart, rampFullStart, 0f, 1f);
        }
        else if (value > rampFullEnd)
        {
            value = MathUtils.RemapValClamped(value, rampFullEnd, rampEnd, 1f, 0f);
        }
        else
        {
            value = 1f;
        }

        stack[top - 5] = value * amount;
        return top - 4;
    }

    private int RunFlexEyelid(int closeLidVController, bool lower, Span<float> stack, int top, ReadOnlySpan<float> values)
    {
        if (top < 3 || !IsFlexController(closeLidVController))
        {
            return top;
        }

        var closeLidController = (int)stack[top - 1];
        var unusedController = (int)stack[top - 2];
        var eyeUpDownController = (int)stack[top - 3];

        if (!IsFlexController(closeLidController) || !IsFlexController(unusedController) || !IsFlexController(eyeUpDownController))
        {
            return top;
        }

        var closeLidV = GetNormalizedFlexValue(closeLidVController, values);
        var closeLid = GetNormalizedFlexValue(closeLidController, values);
        var eyeUpDownRange = FlexControllers[eyeUpDownController];
        var eyeUpDown = MathUtils.RemapValClamped(values[eyeUpDownRange.ValueIndex], eyeUpDownRange.Min, eyeUpDownRange.Max, -1f, 1f);

        if (lower)
        {
            var weight = (1f - closeLidV) * closeLid;
            stack[top - 3] = eyeUpDown > 0f ? (1f - eyeUpDown) * weight : weight;
        }
        else
        {
            var weight = closeLidV * closeLid;
            stack[top - 3] = eyeUpDown < 0f ? (1f + eyeUpDown) * weight : weight;
        }

        return top - 2;
    }

    private float GetNormalizedFlexValue(int controllerIndex, ReadOnlySpan<float> values)
    {
        var controller = FlexControllers[controllerIndex];
        return MathUtils.RemapValClamped(values[controller.ValueIndex], controller.Min, controller.Max, 0f, 1f);
    }
}

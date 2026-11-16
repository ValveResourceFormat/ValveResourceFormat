using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Graphs;

/// <summary>Every serialized instruction code, in order. Typed variants such as ADD_INT only exist at runtime.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/PulseInstructionCode_t">PulseInstructionCode_t</seealso>
internal enum PulseInstructionCode
{
    INVALID,
    IMMEDIATE_HALT,
    RETURN_VOID,
    RETURN_VALUE,
    LOOP_BREAK,
    NOP,
    JUMP,
    JUMP_COND,
    CHUNK_LEAP,
    CHUNK_LEAP_COND,
    PULSE_CALL_SYNC,
    PULSE_CALL_ASYNC_FIRE,
    CREATE_CHILD_CURSOR_OUTFLOW,
    CELL_INVOKE,
    LIBRARY_INVOKE,
    SET_VAR,
    GET_VAR,
    GET_VAR_DETACH,
    DETACH_REGISTER,
    SET_VAR_ARRAY_ELEMENT_1D,
    SET_VAR_OBSERVABLE,
    GET_CONST,
    GET_ARRAY_ELEMENT,
    GET_DOMAIN_VALUE,
    COPY,
    NOT,
    NEGATE,
    ADD,
    SUB,
    MUL,
    DIV,
    MOD,
    LT,
    LTE,
    EQ,
    NE,
    AND,
    OR,
    SCALE,
    SCALE_INV,
    ELEMENT_ACCESS,
    CONVERT_VALUE,
    REINTERPRET_INSTANCE,
    GET_BLACKBOARD_REFERENCE,
    SET_BLACKBOARD_REFERENCE,
    GET_TEMPVAR,
    SET_TEMPVAR,
    SET_TEMPVAR_OBSERVABLE,
}

/// <summary>The value types a register or variable can hold.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/PulseValueType_t">PulseValueType_t</seealso>
internal enum PulseValueType
{
    PVAL_INVALID,
    PVAL_VOID,
    PVAL_BOOL,
    PVAL_INT,
    PVAL_FLOAT,
    PVAL_STRING,
    PVAL_VEC2,
    PVAL_VEC3,
    PVAL_VEC3_WORLDSPACE,
    PVAL_VEC4,
    PVAL_QANGLE,
    PVAL_TRANSFORM,
    PVAL_TRANSFORM_WORLDSPACE,
    PVAL_COLOR_RGB,
    PVAL_EHANDLE,
    PVAL_PARTICLE_EHANDLE,
    PVAL_RESOURCE,
    PVAL_RESOURCE_NAME,
    PVAL_SNDEVT_NAME,
    PVAL_SNDEVT_GUID,
    PVAL_ENTITY_NAME,
    PVAL_LEAF,
    PVAL_TYPESAFE_INT,
    PVAL_TYPESAFE_INT64,
    PVAL_GAMETIME,
    PVAL_SCHEMA_ENUM,
    PVAL_ENUM,
    PVAL_PANORAMA_PANEL_HANDLE,
    PVAL_MODEL_MATERIAL_GROUP,
    PVAL_OPAQUE_HANDLE,
    PVAL_TEST_HANDLE,
    PVAL_ARRAY,
    PVAL_VARIANT,
    PVAL_ANY,
    PVAL_CURSOR_FLOW,
    PVAL_UNKNOWN,
    PVAL_ANIM_SEQUENCE,
    PVAL_VDATA_CHOICE,
    PVAL_COUNT,
}

/// <summary>One bytecode instruction, with every index it can refer to.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/PGDInstruction_t">PGDInstruction_t</seealso>
internal sealed class PulseInstruction
{
    /// <summary>The parsed code, <see cref="PulseInstructionCode.INVALID"/> for a code this reader does not know.</summary>
    public PulseInstructionCode Code { get; }

    /// <summary>The code as serialized.</summary>
    public string CodeName { get; }

    public int Reg0 { get; }
    public int Reg1 { get; }
    public int Reg2 { get; }
    public int Var { get; }
    public int InvokeBindingIndex { get; }
    public int Chunk { get; }
    public int DestInstruction { get; }
    public int CallInfoIndex { get; }
    public int ConstIdx { get; }
    public int DomainValueIdx { get; }
    public int BlackboardReferenceIdx { get; }
    public int TempVarIdx { get; }

    public PulseInstruction(KVObject data)
    {
        CodeName = data.GetStringProperty("m_nCode", string.Empty);
        Code = Enum.TryParse<PulseInstructionCode>(CodeName, out var code) ? code : PulseInstructionCode.INVALID;
        Reg0 = data.GetInt32Property("m_nReg0", -1);
        Reg1 = data.GetInt32Property("m_nReg1", -1);
        Reg2 = data.GetInt32Property("m_nReg2", -1);
        Var = data.GetInt32Property("m_nVar", -1);
        InvokeBindingIndex = data.GetInt32Property("m_nInvokeBindingIndex", -1);
        Chunk = data.GetInt32Property("m_nChunk", -1);
        DestInstruction = data.GetInt32Property("m_nDestInstruction", -1);
        CallInfoIndex = data.GetInt32Property("m_nCallInfoIndex", -1);
        ConstIdx = data.GetInt32Property("m_nConstIdx", -1);
        DomainValueIdx = data.GetInt32Property("m_nDomainValueIdx", -1);
        BlackboardReferenceIdx = data.GetInt32Property("m_nBlackboardReferenceIdx", -1);
        TempVarIdx = data.GetInt32Property("m_nTempVarIdx", -1);
    }
}

/// <summary>A register of a chunk and the node port it was compiled from.</summary>
/// <param name="Index">The register number, <c>m_nReg</c>.</param>
/// <param name="Type">The type name, with an optional ":subtype" such as PVAL_EHANDLE:func_button.</param>
/// <param name="ValueType">The type without its subtype.</param>
/// <param name="OriginNode">Editor node id from <c>m_OriginName</c>, -1 for compiler generated registers.</param>
/// <param name="OriginPort">Port name from <c>m_OriginName</c>, empty without an origin node.</param>
/// <param name="WrittenByInstruction">Index of the instruction that writes it.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_RegisterInfo">CPulse_RegisterInfo</seealso>
internal sealed record PulseRegister(int Index, string Type, PulseValueType ValueType, int OriginNode, string OriginPort, int WrittenByInstruction);

/// <summary>The editor node and sequence point one instruction was compiled from.</summary>
/// <param name="FlowNode">Flow node id, or -1.</param>
/// <param name="ValueNode">Value node id, or -1.</param>
/// <param name="SequencePoint">Sequence point or port name the instruction runs at.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_InstructionDebug">CPulse_InstructionDebug</seealso>
internal readonly record struct PulseInstructionDebug(int FlowNode, int ValueNode, string SequencePoint);

/// <summary>Named registers a binding reads its inputs from and writes its outputs to.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/PulseRegisterMap_t">PulseRegisterMap_t</seealso>
internal sealed class PulseRegisterMap
{
    public static PulseRegisterMap Empty { get; } = new(null);

    public IReadOnlyList<(string Name, int Register)> InParams { get; }
    public IReadOnlyList<(string Name, int Register)> OutParams { get; }

    private PulseRegisterMap(KVObject? data)
    {
        InParams = ReadParams(data, "m_Inparams");
        OutParams = ReadParams(data, "m_Outparams");
    }

    public static PulseRegisterMap Read(KVObject owner, string key)
        => owner.TryGetValue(key, out var data) && !data.IsNull ? new PulseRegisterMap(data) : Empty;

    public bool TryGetInParam(string name, out int register)
    {
        foreach (var (paramName, paramRegister) in InParams)
        {
            if (paramName == name)
            {
                register = paramRegister;
                return true;
            }
        }

        register = -1;
        return false;
    }

    private static List<(string Name, int Register)> ReadParams(KVObject? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var registerParams) || registerParams.IsNull)
        {
            return [];
        }

        return [.. registerParams.Select(static param => (param.Key, param.Value.ToInt32OrDefault()))];
    }
}

/// <summary>A cell method or library function an instruction invokes.</summary>
/// <param name="FuncName">The invoked function, "&lt;class&gt;::&lt;method&gt;".</param>
/// <param name="CellIndex">Index into <c>m_Cells</c>, -1 for a library function.</param>
/// <param name="RegisterMap">Registers of the arguments and results.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_InvokeBinding">CPulse_InvokeBinding</seealso>
internal sealed record PulseInvokeBinding(string FuncName, int CellIndex, PulseRegisterMap RegisterMap);

/// <summary>A call into a method or into one outflow of a node.</summary>
/// <param name="PortName">The outflow called, ending in ":no_port" for a call from a sequence point.</param>
/// <param name="EditorNode">Editor node id of the calling node, or -1.</param>
/// <param name="CallMethodId">Editor node id of the called method, -1 for an outflow call.</param>
/// <param name="RegisterMap">Registers of the arguments and results.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_CallInfo">CPulse_CallInfo</seealso>
internal sealed record PulseCallInfo(string PortName, int EditorNode, int CallMethodId, PulseRegisterMap RegisterMap);

/// <summary>An outflow of a cell and the instruction it continues at.</summary>
/// <param name="Name">The outflow name, <c>m_SourceOutflowName</c>.</param>
/// <param name="DestChunk">Destination chunk, or -1 when nothing is connected.</param>
/// <param name="DestInstruction">Destination instruction, or -1 when nothing is connected.</param>
/// <param name="RegisterMap">Registers the outflow writes its outparams to.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_OutflowConnection">CPulse_OutflowConnection</seealso>
internal sealed record PulseOutflow(string Name, int DestChunk, int DestInstruction, PulseRegisterMap RegisterMap);

/// <summary>
/// An entity output the graph fires. <c>m_SourceOutput</c> is a public output name, or for an entity fire
/// <c>Step_EntFire:&lt;node id&gt;</c>, optionally prefixed with <c>&lt;flow name&gt; =&gt; </c>.
/// </summary>
/// <param name="SourceOutput">The raw <c>m_SourceOutput</c>.</param>
/// <param name="EntFireNode">Editor node id of the entity fire, or -1 for a public output.</param>
/// <param name="TargetEntity">The entity name value the output fires at.</param>
/// <param name="TargetInput">The input fired on <paramref name="TargetEntity"/>.</param>
/// <param name="Param">The parameter text, empty without one.</param>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_OutputConnection">CPulse_OutputConnection</seealso>
internal sealed record PulseOutputConnection(string SourceOutput, int EntFireNode, KVObject TargetEntity, string TargetInput, string Param);

/// <summary>A chunk of instructions with its registers and debug info.</summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulse_Chunk">CPulse_Chunk</seealso>
internal sealed class PulseChunk
{
    public IReadOnlyList<PulseInstruction> Instructions { get; }

    /// <summary>Index into <c>m_TempVarBanks</c>, or -1.</summary>
    public int TempVarBank { get; }

    /// <summary>Whether every instruction has a debug info entry.</summary>
    public bool HasDebugInfo { get; }

    private readonly PulseInstructionDebug[] debugInfos;
    private readonly Dictionary<int, PulseRegister> registers = [];
    private readonly Dictionary<(int Node, string Port), PulseRegister> registersByOrigin = [];

    public PulseChunk(KVObject data, Func<string, PulseValueType> parseValueType)
    {
        Instructions = [.. (data.GetArray("m_Instructions") ?? []).Select(static instruction => new PulseInstruction(instruction))];
        TempVarBank = data.GetInt32Property("m_nTempVarBank", -1);

        var debugData = data.GetArray("m_InstructionDebugInfos") ?? [];
        debugInfos = new PulseInstructionDebug[debugData.Count];

        for (var i = 0; i < debugData.Count; i++)
        {
            debugInfos[i] = new PulseInstructionDebug(
                debugData[i].GetInt32Property("m_nFlowNodeID", -1),
                debugData[i].GetInt32Property("m_nValueNodeID", -1),
                debugData[i].GetStringProperty("m_SequencePointName", string.Empty));
        }

        HasDebugInfo = debugInfos.Length >= Instructions.Count;

        foreach (var info in data.GetArray("m_Registers") ?? [])
        {
            var type = info.GetStringProperty("m_Type", "PVAL_INVALID");
            var hasOrigin = TryParseOriginName(info.GetStringProperty("m_OriginName", string.Empty), out var originNode, out var originPort);

            var register = new PulseRegister(
                info.GetInt32Property("m_nReg", -1),
                type,
                parseValueType(type),
                originNode,
                originPort,
                info.GetInt32Property("m_nWrittenByInstruction", -1));

            registers[register.Index] = register;

            if (hasOrigin)
            {
                registersByOrigin.TryAdd((originNode, originPort), register);
            }
        }
    }

    public IReadOnlyList<PulseInstructionDebug> DebugInfos => debugInfos;

    public IEnumerable<PulseRegister> Registers => registers.Values;

    /// <summary>The debug info of an instruction, or an empty one past the end of <c>m_InstructionDebugInfos</c>.</summary>
    public PulseInstructionDebug DebugInfo(int instruction)
        => instruction >= 0 && instruction < debugInfos.Length ? debugInfos[instruction] : new PulseInstructionDebug(-1, -1, string.Empty);

    public PulseRegister? Register(int register) => registers.GetValueOrDefault(register);

    public string RegisterType(int register) => Register(register)?.Type ?? "PVAL_INVALID";

    public PulseValueType RegisterValueType(int register) => Register(register)?.ValueType ?? PulseValueType.PVAL_INVALID;

    public int RegisterWriteInstruction(int register) => Register(register)?.WrittenByInstruction ?? -1;

    /// <summary>The register an editor node port was compiled to, or -1.</summary>
    public int FindRegister(int node, string port) => registersByOrigin.TryGetValue((node, port), out var register) ? register.Index : -1;

    /// <summary>Reads <c>m_OriginName</c>, "&lt;node id&gt;:&lt;port&gt;". Compiler generated registers such as casts have no node.</summary>
    public static bool TryParseOriginName(string originName, out int nodeId, out string port)
    {
        var separator = originName.IndexOf(':', StringComparison.Ordinal);
        if (separator > 0 && ValueParsing.TryParseInt(originName.AsSpan(0, separator), out nodeId))
        {
            port = originName[(separator + 1)..];
            return true;
        }

        nodeId = -1;
        port = string.Empty;
        return false;
    }
}

/// <summary>
/// Reads a compiled Pulse graph: its tables, chunks, registers, debug info and the outflows of its cells.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/pulse_runtime_lib/CPulseGraphDef">CPulseGraphDef</seealso>
internal sealed class PulseGraphDefinition
{
    public KVObject Data { get; }

    public IReadOnlyList<KVObject> Cells { get; }
    public IReadOnlyList<PulseChunk> Chunks { get; }
    public IReadOnlyList<PulseInvokeBinding> InvokeBindings { get; }
    public IReadOnlyList<PulseCallInfo> CallInfos { get; }
    public IReadOnlyList<KVObject> Constants { get; }
    public IReadOnlyList<KVObject> DomainValues { get; }
    public IReadOnlyList<KVObject> Vars { get; }
    public IReadOnlyList<KVObject> PublicOutputs { get; }
    public IReadOnlyList<KVObject> TempVarBanks { get; }
    public IReadOnlyList<KVObject> BlackboardReferences { get; }
    public IReadOnlyList<PulseOutputConnection> OutputConnections { get; }

    private readonly IProgress<string>? progressReporter;
    private readonly HashSet<string> reportedMessages = [];
    private readonly CellOutflows?[] cellOutflows;

    // Outflows of a cell and the top level cell keys they were found under
    private sealed record CellOutflows(List<PulseOutflow> Outflows, HashSet<string> Keys);

    public PulseGraphDefinition(KVObject data, IProgress<string>? progressReporter = null)
    {
        Data = data;
        this.progressReporter = progressReporter;

        Cells = data.GetArray("m_Cells") ?? [];
        Chunks = [.. (data.GetArray("m_Chunks") ?? []).Select(chunk => new PulseChunk(chunk, ParseValueType))];
        Constants = data.GetArray("m_Constants") ?? [];
        DomainValues = data.GetArray("m_DomainValues") ?? [];
        Vars = data.GetArray("m_Vars") ?? [];
        PublicOutputs = data.GetArray("m_PublicOutputs") ?? [];
        TempVarBanks = data.GetArray("m_TempVarBanks") ?? [];
        BlackboardReferences = data.GetArray("m_BlackboardReferences") ?? [];

        InvokeBindings = [.. (data.GetArray("m_InvokeBindings") ?? []).Select(static binding => new PulseInvokeBinding(
            binding.GetStringProperty("m_FuncName", string.Empty),
            binding.GetInt32Property("m_nCellIndex", -1),
            PulseRegisterMap.Read(binding, "m_RegisterMap")))];

        CallInfos = [.. (data.GetArray("m_CallInfos") ?? []).Select(static callInfo => new PulseCallInfo(
            callInfo.GetStringProperty("m_PortName", "OutDefault"),
            callInfo.GetInt32Property("m_nEditorNodeID", -1),
            callInfo.GetInt32Property("m_CallMethodID", -1),
            PulseRegisterMap.Read(callInfo, "m_RegisterMap")))];

        OutputConnections = [.. (data.GetArray("m_OutputConnections") ?? []).Select(ReadOutputConnection)];

        cellOutflows = new CellOutflows?[Cells.Count];
    }

    public string CellClass(int cell) => Cells[cell].GetStringProperty("_class", string.Empty);

    /// <summary>Every outflow of a cell, found by shape anywhere in its data.</summary>
    public IReadOnlyList<PulseOutflow> Outflows(int cell) => GetCellOutflows(cell).Outflows;

    /// <summary>Whether a top level key of a cell holds any of its outflows.</summary>
    public bool IsOutflowKey(int cell, string key) => GetCellOutflows(cell).Keys.Contains(key);

    /// <summary>A temporary variable of the bank a chunk uses, or null.</summary>
    public KVObject? TempVar(int chunk, int index)
    {
        var bank = Chunks[chunk].TempVarBank;
        if (bank < 0 || bank >= TempVarBanks.Count)
        {
            return null;
        }

        var tempVars = TempVarBanks[bank].GetArray("m_TempVars") ?? [];
        return index >= 0 && index < tempVars.Count ? tempVars[index] : null;
    }

    /// <summary>
    /// Reads a type name. Handle and enum types name their subject after a colon (PVAL_EHANDLE:func_button),
    /// which is not part of the type itself.
    /// </summary>
    public PulseValueType ParseValueType(string? typeName)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return PulseValueType.PVAL_VOID;
        }

        var subtype = typeName.IndexOf(':', StringComparison.Ordinal);
        var baseType = subtype >= 0 ? typeName[..subtype] : typeName;

        if (Enum.TryParse(baseType, out PulseValueType valueType))
        {
            return valueType;
        }

        Report($"Unknown pulse value type \"{baseType}\".");
        return PulseValueType.PVAL_VOID;
    }

    /// <summary>Reports a diagnostic once, however many times it is raised.</summary>
    public void Report(string message)
    {
        if (reportedMessages.Add(message))
        {
            progressReporter?.Report(message);
        }
    }

    private CellOutflows GetCellOutflows(int cell)
    {
        if (cellOutflows[cell] is { } cached)
        {
            return cached;
        }

        var result = new CellOutflows([], []);

        foreach (var (key, value) in Cells[cell])
        {
            var count = result.Outflows.Count;
            FindOutflows(value, result.Outflows);

            if (result.Outflows.Count > count)
            {
                result.Keys.Add(key);
            }
        }

        cellOutflows[cell] = result;
        return result;
    }

    private static void FindOutflows(KVObject value, List<PulseOutflow> outflows)
    {
        if (value.IsCollection)
        {
            if (value.ContainsKey("m_SourceOutflowName") && value.ContainsKey("m_nDestChunk"))
            {
                outflows.Add(new PulseOutflow(
                    value.GetStringProperty("m_SourceOutflowName", "OutDefault"),
                    value.GetInt32Property("m_nDestChunk", -1),
                    value.GetInt32Property("m_nInstruction", -1),
                    PulseRegisterMap.Read(value, "m_OutflowRegisterMap")));
                return;
            }

            foreach (var (_, child) in value)
            {
                FindOutflows(child, outflows);
            }
        }
        else if (value.IsArray)
        {
            foreach (var element in value.AsArraySpan())
            {
                FindOutflows(element, outflows);
            }
        }
    }

    private static PulseOutputConnection ReadOutputConnection(KVObject connection)
    {
        const string EntFirePrefix = "Step_EntFire:";

        var sourceOutput = connection.GetStringProperty("m_SourceOutput", string.Empty);
        var prefixIndex = sourceOutput.LastIndexOf(EntFirePrefix, StringComparison.Ordinal);
        var entFireNode = prefixIndex >= 0 && ValueParsing.TryParseInt(sourceOutput.AsSpan(prefixIndex + EntFirePrefix.Length), out var nodeId) ? nodeId : -1;

        return new PulseOutputConnection(
            sourceOutput,
            entFireNode,
            connection.TryGetValue("m_TargetEntity", out var targetEntity) ? targetEntity : KVObject.Null(),
            connection.GetStringProperty("m_TargetInput", string.Empty),
            connection.GetStringProperty("m_Param", string.Empty));
    }
}

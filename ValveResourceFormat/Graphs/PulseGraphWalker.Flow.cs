using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Graphs;

internal sealed partial class PulseGraphWalker
{
    /// <summary>One cursor flow being followed through a chunk.</summary>
    private sealed class FlowPath
    {
        public required int Chunk { get; set; }
        public required int Instruction { get; set; }
        public required Dictionary<int, ValueRef> Registers { get; set; }

        // The editor node the instructions currently being walked belong to
        public required int CurrentNode { get; set; }

        public string? LastSequencePoint { get; set; }

        // Outflow taken when leaving the current node if no sequence point names one, e.g. the branch of an If
        public string? DefaultOutflow { get; set; }

        public FlowPath Fork(int chunk, int instruction, string? defaultOutflow) => new()
        {
            Chunk = chunk,
            Instruction = instruction,
            Registers = chunk == Chunk ? new Dictionary<int, ValueRef>(Registers) : [],
            CurrentNode = CurrentNode,
            LastSequencePoint = LastSequencePoint,
            DefaultOutflow = defaultOutflow,
        };
    }

    private readonly Queue<FlowPath> pathQueue = [];
    private readonly HashSet<int> enteredFlowNodes = [];
    private readonly Dictionary<int, HashSet<string>> arrivalSequencePoints = [];
    private readonly HashSet<int> fireAndForgetNodes = [];

    // Instructions that fork the flow, followed once no matter how many paths reach them, e.g. through a loop's back edge
    private readonly HashSet<(int Chunk, int Instruction)> forkingInstructions = [];

    // Loop nodes whose body reads the loop index from this register
    private readonly Dictionary<int, int> loopIndexRegisters = [];

    // Outflows each node runs through calls, which is all a simultaneous outflow compiles to
    private readonly Dictionary<int, List<string>> outflowCallPorts = [];

    // Registers of entry nodes with several outparams, which the compiler numbers in the order of the connections reading them
    private readonly Dictionary<int, Dictionary<string, int>> entryOutparamRegisters = [];

    private readonly Dictionary<int, HashSet<int>> nonVariableValueNodes = [];

    // Expression nodes by the value node their instructions were compiled under
    private readonly Dictionary<int, int> expressionOwners = [];
    private readonly Dictionary<(int Expression, int Part), int> renumberedExpressionParts = [];

    // First node writing each temporary variable that has no editor node, by temp var bank and index
    private readonly Dictionary<(int Bank, int Index), int> undeclaredTempVarWriters = [];

    // Nodes standing for instructions the document has no class for, by the instruction they stand for
    private readonly Dictionary<(int Chunk, int Instruction), int> instructionNodes = [];

    private void AddEntryNode(int cellIndex)
    {
        var cell = Definition.Cells[cellIndex];
        var id = cellNodeIds[cellIndex];
        var className = Definition.CellClass(cellIndex);
        PulseEditorNode node;

        switch (className)
        {
            case "CPulseCell_Inflow_Method":
                node = AddNode(id, "Inflow_Method", isFlow: true);
                node.Name = cell.GetStringProperty("m_MethodName", string.Empty);
                node.Fields.Add("m_bIsPublic", cell.GetBooleanProperty("m_bIsPublic"));
                node.Fields.Add("m_Description", cell.GetStringProperty("m_Description", string.Empty));
                node.Fields.Add("m_Args", BuildMethodArgs(cell.GetArray("m_Args")));
                node.Fields.Add("m_ReturnValues", BuildMethodArgs(cell.GetArray("m_ReturnValues")));
                break;

            case "CPulseCell_Inflow_EventHandler":
                node = AddGenericNode(id, cell.GetStringProperty("m_EventName", string.Empty), isFlow: true);
                break;

            case "CPulseCell_Inflow_GraphHook":
                node = AddGenericNode(id, cell.GetStringProperty("m_HookName", string.Empty), isFlow: true);
                break;

            case "CPulseCell_Inflow_EntOutputHandler":
                node = AddNode(id, "Inflow_EntOutput", isFlow: true);
                node.Fields.Add("m_SourceEntity", cell.GetStringProperty("m_SourceEntity", string.Empty));
                node.Fields.Add("m_SourceOutput", cell.GetStringProperty("m_SourceOutput", string.Empty));
                node.Fields.Add("m_CachedOutputType", cell.GetStringProperty("m_ExpectedParamType", "PVAL_VOID"));
                break;

            default:
                node = AddGenericNode(id, className, isFlow: true);
                node.Fields.Add("m_GenericKeys", GetGenericKeys(cellIndex));
                break;
        }

        node.IsEntry = true;
        node.CellClass = className;

        var entryChunk = cell.GetInt32Property("m_EntryChunk");
        var registerMap = PulseRegisterMap.Read(cell, "m_RegisterMap");
        var registers = new Dictionary<int, ValueRef>();
        AddOutparamRegisters(registers, entryChunk, registerMap, id);

        var outparamRegisters = new Dictionary<string, int>();

        foreach (var (name, register) in registerMap.OutParams)
        {
            outparamRegisters[name] = register;
        }

        if (outparamRegisters.Count > 1)
        {
            entryOutparamRegisters[id] = outparamRegisters;
        }

        enteredFlowNodes.Add(id);
        pathQueue.Enqueue(new FlowPath
        {
            Chunk = entryChunk,
            Instruction = 0,
            Registers = registers,
            CurrentNode = id,
        });
    }

    /// <summary>The arguments or return values of a method, as the document declares them.</summary>
    public static KVObject BuildMethodArgs(IReadOnlyList<KVObject>? args)
    {
        var array = KVObject.Array();

        foreach (var arg in args ?? [])
        {
            var entry = KVObject.Collection();
            entry.Add("m_Name", arg.GetStringProperty("m_Name", string.Empty));
            entry.Add("m_Type", arg.GetStringProperty("m_Type", "PVAL_INVALID"));
            entry.Add("m_Description", arg.GetStringProperty("m_Description", string.Empty));
            array.Add(entry);
        }

        return array;
    }

    private PulseEditorNode AddGenericNode(int id, string nodeType, bool isFlow)
    {
        var node = AddNode(id, isFlow ? "Flow_Generic" : "Value_Generic", isFlow);
        node.Fields.Add("m_GenericNodeType", nodeType);
        node.Fields.Add("m_GenericPorts", KVObject.Collection());
        return node;
    }

    // Cell settings the editor keeps as a custom attribute, a table holding the setting under the same name
    private static readonly HashSet<string> CustomAttributeKeys = ["m_strEntityOutput"];

    // The settings of a cell, leaving out the bookkeeping the compiler fills in
    private KVObject GetGenericKeys(int cellIndex)
    {
        var keys = KVObject.Collection();

        foreach (var (key, value) in Definition.Cells[cellIndex])
        {
            if (key is "_class" or "m_nEditorNodeID" or "m_EntryChunk" or "m_RegisterMap" || Definition.IsOutflowKey(cellIndex, key))
            {
                continue;
            }

            if (CustomAttributeKeys.Contains(key))
            {
                var attribute = KVObject.Collection();
                attribute.Add(key, value);
                keys.Add(key, attribute);
                continue;
            }

            keys.Add(key, value);
        }

        return keys;
    }

    private void AddOutparamRegisters(Dictionary<int, ValueRef> registers, int chunk, PulseRegisterMap registerMap, int nodeId)
    {
        foreach (var (name, register) in registerMap.OutParams)
        {
            registers[register] = new NodeOutput(nodeId, name);
            RecordOutputType(chunk, register, nodeId, name);
        }
    }

    private void SetRegister(FlowPath path, int register, ValueRef value)
    {
        path.Registers[register] = value;

        if (value is NodeOutput output)
        {
            RecordOutputType(path.Chunk, register, output.NodeId, output.Port);
        }
    }

    private void RecordOutputType(int chunk, int register, int nodeId, string port)
    {
        if (chunk >= 0 && chunk < Definition.Chunks.Count && Definition.Chunks[chunk].Register(register) is { } info)
        {
            outputTypes.TryAdd((nodeId, port), info.ValueType);
        }
    }

    private void WalkAllPaths()
    {
        while (pathQueue.TryDequeue(out var path))
        {
            WalkPath(path);
        }

        foreach (var (nodeId, ports) in outflowCallPorts)
        {
            if (!nodes.ContainsKey(nodeId))
            {
                var node = AddNode(nodeId, "Outflow_Simultaneous", isFlow: true);
                node.Fields.Add("m_nNumExits", ports.Count);
            }
        }

        ResolvePendingFlowConnections();
    }

    private void WalkPath(FlowPath path)
    {
        var visited = new HashSet<(int Chunk, int Instruction)>();

        while (true)
        {
            if (path.Chunk < 0 || path.Chunk >= Definition.Chunks.Count)
            {
                return;
            }

            var chunk = Definition.Chunks[path.Chunk];

            if (!chunk.HasDebugInfo)
            {
                unwalkedFlows.Add(new PulseUnwalkedFlow(path.CurrentNode, GetLeavingOutflow(path), path.Chunk));
                return;
            }

            if (path.Instruction < 0 || path.Instruction >= chunk.Instructions.Count || !visited.Add((path.Chunk, path.Instruction)))
            {
                return;
            }

            var (flowNode, valueNode, sequencePoint) = chunk.DebugInfo(path.Instruction);

            if (flowNode >= 0 && flowNode != path.CurrentNode)
            {
                ConnectFlow(path.CurrentNode, GetLeavingOutflow(path), flowNode, sequencePoint);

                if (!enteredFlowNodes.Add(flowNode))
                {
                    return;
                }

                path.CurrentNode = flowNode;
                path.DefaultOutflow = null;

                if (!IsSequencePointName(sequencePoint))
                {
                    GetArrivalSequencePoints(flowNode).Add(sequencePoint);
                }
            }

            path.LastSequencePoint = sequencePoint;

            if (!ExecuteInstruction(path, chunk.Instructions[path.Instruction], valueNode, sequencePoint))
            {
                return;
            }
        }
    }

    private HashSet<string> GetArrivalSequencePoints(int nodeId)
    {
        if (!arrivalSequencePoints.TryGetValue(nodeId, out var points))
        {
            points = [];
            arrivalSequencePoints[nodeId] = points;
        }

        return points;
    }

    private string GetLeavingOutflow(FlowPath path)
    {
        var last = path.LastSequencePoint;

        if (!IsSequencePointName(last) && !GetArrivalSequencePoints(path.CurrentNode).Contains(last!))
        {
            return last!;
        }

        return path.DefaultOutflow ?? "OutDefault";
    }

    private ValueRef GetRegister(FlowPath path, int register)
    {
        if (path.Registers.TryGetValue(register, out var value))
        {
            return value;
        }

        Report($"Register {register} in chunk {path.Chunk} is read before anything on the walked flow writes it.");
        return new UnknownValue();
    }

    /// <returns>Whether the flow continues past this instruction.</returns>
    private bool ExecuteInstruction(FlowPath path, PulseInstruction instruction, int valueNode, string sequencePoint)
    {
        var code = instruction.Code;

        switch (code)
        {
            case PulseInstructionCode.NOP:
            case PulseInstructionCode.DETACH_REGISTER:
                break;

            case PulseInstructionCode.IMMEDIATE_HALT:
            case PulseInstructionCode.RETURN_VALUE:
                return false;

            case PulseInstructionCode.RETURN_VOID:
                // A skipped node returns through its requirements skip point, the nodes after it compile to nothing
                if (sequencePoint == "m_OptionalRequirementsCheckPointForInlineSkip" && nodes.TryGetValue(path.CurrentNode, out var skippedNode))
                {
                    skippedNode.Fields["m_bNodeSkip"] = true;
                    Report($"Node {skippedNode.Id} is skipped, any nodes after it are not kept.");
                }

                return false;

            case PulseInstructionCode.JUMP:
                path.Instruction = instruction.DestInstruction;
                return true;

            case PulseInstructionCode.CHUNK_LEAP:
                path.Chunk = instruction.Chunk;
                path.Instruction = 0;
                path.Registers = [];
                return true;

            case PulseInstructionCode.GET_CONST:
            {
                var constant = Definition.Constants[instruction.ConstIdx];
                SetRegister(path, instruction.Reg0, new LiteralValue(constant["m_Value"], constant.GetStringProperty("m_Type", "PVAL_INVALID")));
                break;
            }

            case PulseInstructionCode.GET_DOMAIN_VALUE:
                SetRegister(path, instruction.Reg0, new DomainValue(Definition.DomainValues[instruction.DomainValueIdx]["m_Value"]));
                break;

            case PulseInstructionCode.GET_VAR:
            case PulseInstructionCode.GET_VAR_DETACH:
                SetRegister(path, instruction.Reg0, ReadVariable(path, variableNodeIds[instruction.Var], valueNode));
                break;

            // Casts are inserted by the compiler wherever types differ, the document does not spell them out
            case PulseInstructionCode.CONVERT_VALUE:
            case PulseInstructionCode.REINTERPRET_INSTANCE:
            case PulseInstructionCode.COPY:
                SetRegister(path, instruction.Reg0, GetRegister(path, instruction.Reg1));
                break;

            case PulseInstructionCode.NOT:
            case PulseInstructionCode.NEGATE:
            case PulseInstructionCode.ADD:
            case PulseInstructionCode.SUB:
            case PulseInstructionCode.MUL:
            case PulseInstructionCode.DIV:
            case PulseInstructionCode.MOD:
            case PulseInstructionCode.LT:
            case PulseInstructionCode.LTE:
            case PulseInstructionCode.EQ:
            case PulseInstructionCode.NE:
            case PulseInstructionCode.AND:
            case PulseInstructionCode.OR:
            case PulseInstructionCode.SCALE:
            case PulseInstructionCode.SCALE_INV:
                AddExpression(path, instruction, valueNode);
                break;

            case PulseInstructionCode.LIBRARY_INVOKE:
                AddLibraryInvoke(path, instruction, valueNode);
                break;

            case PulseInstructionCode.CELL_INVOKE:
                AddCellInvoke(path, instruction, valueNode);
                break;

            case PulseInstructionCode.PULSE_CALL_SYNC:
            case PulseInstructionCode.PULSE_CALL_ASYNC_FIRE:
                AddMethodCall(path, instruction, code == PulseInstructionCode.PULSE_CALL_ASYNC_FIRE);
                break;

            case PulseInstructionCode.CREATE_CHILD_CURSOR_OUTFLOW:
                AddChildCursor(path, instruction, sequencePoint);
                break;

            case PulseInstructionCode.SET_VAR:
            case PulseInstructionCode.SET_VAR_OBSERVABLE:
                AddSetVariable(path, instruction, code == PulseInstructionCode.SET_VAR_OBSERVABLE);
                break;

            case PulseInstructionCode.GET_TEMPVAR:
            {
                var tempVar = GetTempVar(path.Chunk, instruction);
                var name = tempVar?.GetStringProperty("m_Name", string.Empty) ?? string.Empty;
                SetRegister(path, instruction.Reg0, new LocalNameValue(name, TempVarDeclaringNode(path.Chunk, instruction, tempVar)));
                break;
            }

            case PulseInstructionCode.SET_TEMPVAR:
            case PulseInstructionCode.SET_TEMPVAR_OBSERVABLE:
                AddSetTempVar(path, instruction);
                break;

            case PulseInstructionCode.SET_VAR_ARRAY_ELEMENT_1D:
            {
                // Writes reg0 into the element reg2 indexes, reg1 is unused
                var node = AddInstructionNode(path, instruction, valueNode, isFlow: true);
                node?.Fields.Add("m_VarDefID", variableNodeIds[instruction.Var]);
                SetInstructionPort(path, node, "m_Index", instruction.Reg2);
                SetInstructionPort(path, node, "m_Value", instruction.Reg0);
                node?.AddVariableWrite(PulseVariableKind.Variable, variableNodeIds[instruction.Var]);
                break;
            }

            case PulseInstructionCode.GET_ARRAY_ELEMENT:
            case PulseInstructionCode.ELEMENT_ACCESS:
            {
                var node = AddInstructionNode(path, instruction, valueNode, isFlow: false);
                SetInstructionPort(path, node, "arg1", instruction.Reg1);
                SetInstructionPort(path, node, "arg2", instruction.Reg2);
                SetRegister(path, instruction.Reg0, new NodeOutput(instructionNodes[(path.Chunk, path.Instruction)], "retval"));
                break;
            }

            case PulseInstructionCode.GET_BLACKBOARD_REFERENCE:
            {
                var node = AddInstructionNode(path, instruction, valueNode, isFlow: false);
                node?.Fields.Add("m_nBlackboardReferenceIdx", instruction.BlackboardReferenceIdx);
                node?.AddVariableRead(PulseVariableKind.BlackboardReference, instruction.BlackboardReferenceIdx, "var");
                SetRegister(path, instruction.Reg0, new NodeOutput(instructionNodes[(path.Chunk, path.Instruction)], "retval"));
                break;
            }

            case PulseInstructionCode.SET_BLACKBOARD_REFERENCE:
            {
                var node = AddInstructionNode(path, instruction, valueNode, isFlow: true);
                node?.Fields.Add("m_nBlackboardReferenceIdx", instruction.BlackboardReferenceIdx);
                SetInstructionPort(path, node, "m_Value", instruction.Reg0);
                node?.AddVariableWrite(PulseVariableKind.BlackboardReference, instruction.BlackboardReferenceIdx);
                break;
            }

            case PulseInstructionCode.LOOP_BREAK:
                if (!nodes.ContainsKey(path.CurrentNode))
                {
                    AddNode(path.CurrentNode, "Outflow_Break", isFlow: true);
                }

                return false;

            case PulseInstructionCode.JUMP_COND:
            case PulseInstructionCode.CHUNK_LEAP_COND:
                return AddBranch(path, instruction, valueNode, sequencePoint, code == PulseInstructionCode.CHUNK_LEAP_COND);

            default:
                Report($"Instruction {instruction.CodeName} is not supported yet (chunk {path.Chunk}, instruction {path.Instruction}).");
                break;
        }

        path.Instruction++;
        return true;
    }

    /// <summary>
    /// Adds the node standing for an instruction the document has no class for, once per instruction.
    /// </summary>
    /// <returns>The new node, or null when an earlier path already added it.</returns>
    private PulseEditorNode? AddInstructionNode(FlowPath path, PulseInstruction instruction, int valueNode, bool isFlow)
    {
        if (instructionNodes.ContainsKey((path.Chunk, path.Instruction)))
        {
            return null;
        }

        var nodeId = isFlow ? path.CurrentNode : valueNode >= 0 && valueNode != path.CurrentNode ? valueNode : -1;
        if (nodeId < 0 || nodes.ContainsKey(nodeId))
        {
            nodeId = AllocateNodeId();
        }

        instructionNodes[(path.Chunk, path.Instruction)] = nodeId;

        var node = AddNode(nodeId, instruction.CodeName, isFlow);
        node.Instruction = instruction.Code;
        return node;
    }

    private void SetInstructionPort(FlowPath path, PulseEditorNode? node, string port, int register)
    {
        if (node != null && register >= 0)
        {
            SetInputPort(node, node.Fields, port, port, GetRegister(path, register));
        }
    }

    // A variable read through its own Var Reference node, rather than inline on the port, compiles under that node's id
    private ValueRef ReadVariable(FlowPath path, int variableNodeId, int valueNode)
    {
        if (valueNode < 0 || valueNode == path.CurrentNode || GetNonVariableValueNodes(path.Chunk).Contains(valueNode))
        {
            return new VariableValue(variableNodeId);
        }

        if (!nodes.ContainsKey(valueNode))
        {
            var node = AddNode(valueNode, "Value_VarReference", isFlow: false);
            node.Fields.Add("m_SourceID", variableNodeId);
            node.AddVariableRead(PulseVariableKind.Variable, variableNodeId, "var");
        }

        return new NodeOutput(valueNode, "value");
    }

    // Value nodes that compiled to something other than variable reads, such as text expressions reading variables
    private HashSet<int> GetNonVariableValueNodes(int chunkIndex)
    {
        if (!nonVariableValueNodes.TryGetValue(chunkIndex, out var valueNodes))
        {
            valueNodes = [];
            var chunk = Definition.Chunks[chunkIndex];

            for (var index = 0; index < chunk.Instructions.Count; index++)
            {
                var valueNode = chunk.DebugInfo(index).ValueNode;

                if (valueNode >= 0 && chunk.Instructions[index].Code is not (PulseInstructionCode.GET_VAR or PulseInstructionCode.GET_VAR_DETACH))
                {
                    valueNodes.Add(valueNode);
                }
            }

            nonVariableValueNodes[chunkIndex] = valueNodes;
        }

        return valueNodes;
    }

    private static string? GetExpressionClass(PulseInstructionCode code) => code switch
    {
        PulseInstructionCode.NOT => "ExprNot",
        PulseInstructionCode.NEGATE => "ExprNegate",
        PulseInstructionCode.ADD => "ExprAdd",
        PulseInstructionCode.SUB => "ExprSub",
        PulseInstructionCode.MUL or PulseInstructionCode.SCALE => "ExprMul",
        PulseInstructionCode.DIV or PulseInstructionCode.SCALE_INV => "ExprDiv",
        PulseInstructionCode.MOD => "ExprMod",
        PulseInstructionCode.LT => "ExprLess",
        PulseInstructionCode.LTE => "ExprLessEq",
        PulseInstructionCode.EQ => "ExprEqual",
        PulseInstructionCode.NE => "ExprNotEqual",
        PulseInstructionCode.AND => "ExprAnd",
        PulseInstructionCode.OR => "ExprOr",
        _ => null,
    };

    private void AddExpression(FlowPath path, PulseInstruction instruction, int valueNode)
    {
        var chunk = Definition.Chunks[path.Chunk];
        var code = instruction.Code;
        var (reg0, reg1, reg2) = (instruction.Reg0, instruction.Reg1, instruction.Reg2);

        var origin = chunk.Register(reg0);
        var hasOrigin = origin is { OriginNode: >= 0 };
        var nodeId = origin?.OriginNode ?? -1;
        var port = origin?.OriginPort ?? string.Empty;

        // Flow nodes such as loops and switches compute their own conditions
        if (hasOrigin && nodeId == path.CurrentNode)
        {
            ValueRef value = (code, port) switch
            {
                (PulseInstructionCode.NOT, "not_condition") => new InvertedValue(GetRegister(path, reg1)),
                (PulseInstructionCode.EQ, "__case_cond") => new CaseCondition(GetRegister(path, reg1), GetRegister(path, reg2), chunk.RegisterValueType(reg1)),
                _ => new InternalValue(port, reg1 >= 0 ? GetRegister(path, reg1) : null, reg2 >= 0 ? GetRegister(path, reg2) : null),
            };

            SetRegister(path, reg0, value);
            return;
        }

        if (!hasOrigin)
        {
            nodeId = valueNode >= 0 && valueNode != path.CurrentNode ? valueNode : AllocateNodeId();
            port = "retval";
        }

        // Parts of a text expression are numbered within their expression, so they can clash with other nodes
        if (renumberedExpressionParts.TryGetValue((valueNode, nodeId), out var renumbered))
        {
            nodeId = renumbered;
        }
        else if (nodes.ContainsKey(nodeId) && expressionOwners.GetValueOrDefault(nodeId, valueNode) != valueNode)
        {
            renumbered = AllocateNodeId();
            renumberedExpressionParts[(valueNode, nodeId)] = renumbered;
            nodeId = renumbered;
        }

        if (!nodes.ContainsKey(nodeId))
        {
            var className = GetExpressionClass(code);
            if (className == null)
            {
                Report($"Instruction {instruction.CodeName} has no expression node (chunk {path.Chunk}, instruction {path.Instruction}).");
                SetRegister(path, reg0, new UnknownValue());
                return;
            }

            // Greater-than compiles to less-than with its operands swapped, but its first operand is still evaluated first
            if (code is PulseInstructionCode.LT or PulseInstructionCode.LTE && chunk.RegisterWriteInstruction(reg2) < chunk.RegisterWriteInstruction(reg1))
            {
                className = code == PulseInstructionCode.LT ? "ExprGreater" : "ExprGreaterEq";
                (reg1, reg2) = (reg2, reg1);
            }

            var node = AddNode(nodeId, className, isFlow: false);
            expressionOwners[nodeId] = valueNode;

            if (code is PulseInstructionCode.NOT or PulseInstructionCode.NEGATE)
            {
                SetInputPort(node, node.Fields, "m_Arg", "value", GetRegister(path, reg1), refineLiteralType: true);
            }
            else
            {
                SetInputPort(node, node.Fields, "m_FirstArg", "first", GetRegister(path, reg1), refineLiteralType: true);
                SetInputPort(node, node.Fields, "m_SecondArg", "second", GetRegister(path, reg2), refineLiteralType: true);
            }
        }

        SetRegister(path, reg0, new NodeOutput(nodeId, port));
    }

    private void SetRegisterMapPorts(FlowPath path, PulseEditorNode node, KVObject container, PulseRegisterMap registerMap)
    {
        var chunk = Definition.Chunks[path.Chunk];

        foreach (var (name, register) in registerMap.InParams)
        {
            // A literal passed to a port of any type has to say which type it is
            var takesAnyType = chunk.RegisterValueType(register) is PulseValueType.PVAL_VARIANT or PulseValueType.PVAL_ANY;

            SetInputPort(node, container, name, name, GetRegister(path, register), refineLiteralType: takesAnyType);
        }
    }

    private void AddLibraryInvoke(FlowPath path, PulseInstruction instruction, int valueNode)
    {
        var binding = Definition.InvokeBindings[instruction.InvokeBindingIndex];
        var isValue = valueNode >= 0 && valueNode != path.CurrentNode;
        var nodeId = isValue ? valueNode : path.CurrentNode;

        if (!nodes.ContainsKey(nodeId))
        {
            var node = AddGenericNode(nodeId, binding.FuncName, !isValue);
            SetRegisterMapPorts(path, node, node.Fields["m_GenericPorts"], binding.RegisterMap);
        }

        AddOutparamRegisters(path.Registers, path.Chunk, binding.RegisterMap, nodeId);
    }

    private void AddCellInvoke(FlowPath path, PulseInstruction instruction, int valueNode)
    {
        var binding = Definition.InvokeBindings[instruction.InvokeBindingIndex];
        var cellIndex = binding.CellIndex;
        var nodeId = cellNodeIds[cellIndex];
        var isValue = nodeId != path.CurrentNode && valueNode == nodeId;

        if (!nodes.ContainsKey(nodeId))
        {
            AddCellNode(path, cellIndex, nodeId, binding, isValue);

            if (!isValue)
            {
                QueueCellOutflows(path, cellIndex, nodeId);
            }
        }

        AddOutparamRegisters(path.Registers, path.Chunk, binding.RegisterMap, nodeId);
    }

    private void AddCellNode(FlowPath path, int cellIndex, int nodeId, PulseInvokeBinding binding, bool isValue)
    {
        var cell = Definition.Cells[cellIndex];
        var className = Definition.CellClass(cellIndex);
        var registerMap = binding.RegisterMap;
        var chunk = Definition.Chunks[path.Chunk];
        PulseEditorNode node;

        ValueRef InParam(string name) => registerMap.TryGetInParam(name, out var register) ? GetRegister(path, register) : new UnknownValue();

        switch (className)
        {
            case "CPulseCell_Step_EntFire":
                node = AddNode(nodeId, "Step_EntFire", isFlow: true);

                if (registerMap.TryGetInParam("hTarget", out var targetRegister))
                {
                    node.Fields.Add("m_nFireMode", "PulseEntFireMode_t::HANDLE");
                    SetInputPort(node, node.Fields, "m_TargetHandle", "hTarget", GetRegister(path, targetRegister));
                }

                if (registerMap.TryGetInParam("TargetName", out var targetNameRegister))
                {
                    SetInputPort(node, node.Fields, "m_TargetName", "TargetName", GetRegister(path, targetNameRegister));
                }

                node.Fields.Add("m_Input", cell.GetStringProperty("m_Input", string.Empty));

                if (registerMap.TryGetInParam("pParam", out var paramRegister))
                {
                    SetInputPort(node, node.Fields, "m_Param", "pParam", GetRegister(path, paramRegister));
                    node.Fields.Add("m_SingleParamType", chunk.RegisterType(paramRegister));
                }

                break;

            case "CPulseCell_Step_DebugLog":
                node = AddNode(nodeId, "Step_DebugLog", isFlow: true);
                SetInputPort(node, node.Fields, "m_Message", "pMessage", InParam("pMessage"));
                break;

            case "CPulseCell_Step_PublicOutput":
            {
                node = AddNode(nodeId, "Step_PublicOutput", isFlow: true);
                var outputIndex = cell.GetInt32Property("m_OutputIndex", -1);
                node.Fields.Add("m_OutputDefID", outputIndex >= 0 && outputIndex < publicOutputNodeIds.Length ? publicOutputNodeIds[outputIndex] : -1);
                var argList = KVObject.Collection();
                node.Fields.Add("m_ArgList", argList);
                SetRegisterMapPorts(path, node, argList, registerMap);
                break;
            }

            case "CPulseCell_ReturnValues":
            {
                node = AddNode(nodeId, "Outflow_Return", isFlow: true);
                var returnValues = KVObject.Collection();
                node.Fields.Add("m_ReturnValues", returnValues);
                SetRegisterMapPorts(path, node, returnValues, registerMap);
                break;
            }

            case "CPulseCell_FireCursors":
                node = AddNode(nodeId, "Step_FireCursors", isFlow: true);
                node.Fields.Add("m_nChildCursors", cell.GetInt32Property("m_nChildCursors"));
                node.InflowName = "Start";

                if (!cell.GetBooleanProperty("m_bWaitForChildOutflows"))
                {
                    node.Fields.Add("m_nCallMode", "ASYNC_FIRE_AND_FORGET");
                }
                break;

            case "CPulseCell_Timeline":
            {
                node = AddNode(nodeId, "Step_Timeline", isFlow: true);
                node.InflowName = "Start";

                // Events left unconnected lose their port name, the editor names them by position
                var events = KVObject.Array();
                var timelineEvents = cell.GetArray("m_TimelineEvents") ?? [];

                for (var index = 0; index < timelineEvents.Count; index++)
                {
                    var timelineEvent = timelineEvents[index];
                    var portName = timelineEvent.GetSubCollection("m_EventOutflow")?.GetStringProperty("m_SourceOutflowName", string.Empty);
                    var definition = KVObject.Collection();
                    definition.Add("m_InternalPortName", string.IsNullOrEmpty(portName) ? string.Create(CultureInfo.InvariantCulture, $"event_{index}") : portName);
                    definition.Add("m_FriendlyName", string.Empty);
                    definition.Add("m_flTimeFromPrevious", timelineEvent.GetFloatProperty("m_flTimeFromPrevious"));
                    events.Add(definition);
                }

                node.Fields.Add("m_EventDefinitions", events);
                node.Fields.Add("m_bWaitForChildOutflows", cell.GetBooleanProperty("m_bWaitForChildOutflows"));
                break;
            }

            case "CPulseCell_Outflow_CycleOrdered":
            case "CPulseCell_Outflow_CycleShuffled":
            case "CPulseCell_Outflow_CycleRandom":
                node = AddNode(nodeId, "Outflow_Cycle", isFlow: true);
                node.Fields.Add("m_nNumExits", cell.GetArray("m_Outputs")?.Count ?? 0);
                node.Fields.Add("m_nCycleMode", className switch
                {
                    "CPulseCell_Outflow_CycleShuffled" => "PulseCycleMode_t::SHUFFLED",
                    "CPulseCell_Outflow_CycleRandom" => "PulseCycleMode_t::RANDOM",
                    _ => "PulseCycleMode_t::ORDERED",
                });
                break;

            default:
            {
                node = AddGenericNode(nodeId, className, !isValue);
                node.Fields.Add("m_GenericKeys", GetGenericKeys(cellIndex));
                SetRegisterMapPorts(path, node, node.Fields["m_GenericPorts"], registerMap);

                // Generic cells are entered through the method the invoke binding names
                var separator = binding.FuncName.LastIndexOf("::", StringComparison.Ordinal);
                if (separator >= 0)
                {
                    node.FallbackInflowName = binding.FuncName[(separator + 2)..];
                }

                break;
            }
        }

        node.CellClass = className;
    }

    private void QueueCellOutflows(FlowPath path, int cellIndex, int nodeId)
    {
        foreach (var outflow in Definition.Outflows(cellIndex))
        {
            if (outflow.DestChunk < 0 || outflow.DestInstruction < 0)
            {
                continue;
            }

            var outflowPath = path.Fork(outflow.DestChunk, outflow.DestInstruction, outflow.Name);
            outflowPath.CurrentNode = nodeId;
            AddOutparamRegisters(outflowPath.Registers, outflow.DestChunk, outflow.RegisterMap, nodeId);
            pathQueue.Enqueue(outflowPath);
        }
    }

    private void AddMethodCall(FlowPath path, PulseInstruction instruction, bool isAsync)
    {
        var callInfo = Definition.CallInfos[instruction.CallInfoIndex];
        var targetChunk = instruction.Chunk;

        // A call that targets no method runs one of the node's outflows and comes back, e.g. each iteration of a loop
        // or each exit of a simultaneous outflow. Flows reached from several places are compiled into a chunk of their own.
        if (callInfo.CallMethodId < 0)
        {
            if (!forkingInstructions.Add((path.Chunk, path.Instruction)))
            {
                return;
            }

            var ownerId = callInfo.EditorNode >= 0 ? callInfo.EditorNode : path.CurrentNode;

            // Calls made from a sequence point rather than a port carry on through the node's usual outflow
            var portName = callInfo.PortName;
            var isPortless = portName.EndsWith(":no_port", StringComparison.Ordinal);
            var body = path.Fork(targetChunk, instruction.DestInstruction, isPortless ? path.DefaultOutflow : portName);
            body.CurrentNode = ownerId;
            body.LastSequencePoint = null;

            if (targetChunk == path.Chunk && loopIndexRegisters.TryGetValue(ownerId, out var indexRegister))
            {
                SetRegister(body, indexRegister, new NodeOutput(ownerId, "Index"));
            }

            if (!isPortless)
            {
                if (!outflowCallPorts.TryGetValue(ownerId, out var ports))
                {
                    ports = [];
                    outflowCallPorts[ownerId] = ports;
                }

                ports.Add(portName);
            }

            pathQueue.Enqueue(body);
            return;
        }

        var nodeId = callInfo.EditorNode;
        if (nodeId < 0)
        {
            nodeId = path.CurrentNode >= 0 ? path.CurrentNode : AllocateNodeId();
        }

        if (!nodes.ContainsKey(nodeId))
        {
            var node = AddNode(nodeId, "Step_CallMethod", isFlow: true);
            node.Fields.Add("m_MethodID", callInfo.CallMethodId);

            if (isAsync)
            {
                node.Fields.Add("m_nCallMode", "ASYNC_FIRE_AND_FORGET");
            }

            var argList = KVObject.Collection();
            node.Fields.Add("m_ArgList", argList);
            SetRegisterMapPorts(path, node, argList, callInfo.RegisterMap);
        }

        AddOutparamRegisters(path.Registers, path.Chunk, callInfo.RegisterMap, nodeId);
    }

    // A yielding node set to not wait for completion runs in a child cursor while the flow carries on past it
    private void AddChildCursor(FlowPath path, PulseInstruction instruction, string sequencePoint)
    {
        var isFireAndForget = sequencePoint == "m_OptionalFireAndForgetPoint";

        if (isFireAndForget)
        {
            fireAndForgetNodes.Add(path.CurrentNode);
        }
        else
        {
            Report($"Node {path.CurrentNode} starts a child cursor at sequence point '{sequencePoint}', it is kept as a plain outflow.");
        }

        if (!forkingInstructions.Add((path.Chunk, path.Instruction)))
        {
            return;
        }

        // The node itself is walked in the child cursor, the flow leaving it is the parent's
        var outflow = isFireAndForget || IsSequencePointName(sequencePoint) ? path.DefaultOutflow : sequencePoint;
        var child = path.Fork(instruction.Chunk, instruction.DestInstruction, outflow);
        child.LastSequencePoint = null;
        pathQueue.Enqueue(child);
    }

    private void AddSetVariable(FlowPath path, PulseInstruction instruction, bool notifiesListeners)
    {
        var nodeId = path.CurrentNode >= 0 ? path.CurrentNode : AllocateNodeId();
        if (nodes.ContainsKey(nodeId))
        {
            return;
        }

        var node = AddNode(nodeId, "Step_SetVar", isFlow: true);
        node.Fields.Add("m_VarDefID", variableNodeIds[instruction.Var]);
        SetInputPort(node, node.Fields, "m_Value", "m_Value", GetRegister(path, instruction.Reg0));
        node.AddVariableWrite(PulseVariableKind.Variable, variableNodeIds[instruction.Var]);

        // Writes to an observable variable notify its listeners unless the node opted out
        if (!notifiesListeners && Definition.Vars[instruction.Var].GetBooleanProperty("m_bIsObservable"))
        {
            node.Fields.Add("m_bDoNotNotifyListeners", true);
        }
    }

    private KVObject? GetTempVar(int chunk, PulseInstruction instruction)
    {
        var tempVar = Definition.TempVar(chunk, instruction.TempVarIdx);

        if (tempVar == null)
        {
            Report($"Could not find temporary variable {instruction.TempVarIdx} of chunk {chunk}.");
        }

        return tempVar;
    }

    // The node that declares a temporary variable writes its initial value, any other write is a Set Variable
    private void AddSetTempVar(FlowPath path, PulseInstruction instruction)
    {
        if (nodes.ContainsKey(path.CurrentNode))
        {
            return;
        }

        var tempVar = GetTempVar(path.Chunk, instruction);
        var declaringNode = tempVar?.GetInt32Property("m_nEditorNodeID", -1) ?? -1;
        var value = GetRegister(path, instruction.Reg0);

        if (declaringNode == path.CurrentNode || declaringNode < 0)
        {
            var node = AddNode(path.CurrentNode, "Step_CreateTempVar", isFlow: true);
            node.Fields.Add("m_VarName", tempVar?.GetStringProperty("m_Name", string.Empty) ?? string.Empty);
            node.Fields.Add("m_ValueType", tempVar?.GetStringProperty("m_Type", "PVAL_INVALID") ?? "PVAL_INVALID");
            SetInputPort(node, node.Fields, "m_Value", "m_Value", value);

            if (declaringNode < 0)
            {
                undeclaredTempVarWriters.TryAdd((Definition.Chunks[path.Chunk].TempVarBank, instruction.TempVarIdx), node.Id);
            }
        }
        else
        {
            var node = AddNode(path.CurrentNode, "Step_SetVar", isFlow: true);
            node.Fields.Add("m_VarDefID", declaringNode);
            SetInputPort(node, node.Fields, "m_Value", "m_Value", value);
            node.AddVariableWrite(PulseVariableKind.TempVar, declaringNode);
        }
    }

    // Temporary variables with no editor node are declared by every node writing them, reads link to the first
    private int TempVarDeclaringNode(int chunk, PulseInstruction instruction, KVObject? tempVar)
    {
        var editorNode = tempVar?.GetInt32Property("m_nEditorNodeID", -1) ?? -1;
        return editorNode >= 0
            ? editorNode
            : undeclaredTempVarWriters.GetValueOrDefault((Definition.Chunks[chunk].TempVarBank, instruction.TempVarIdx), -1);
    }

    /// <returns>Whether the flow continues past this instruction.</returns>
    private bool AddBranch(FlowPath path, PulseInstruction instruction, int valueNode, string sequencePoint, bool isChunkLeap)
    {
        if (!forkingInstructions.Add((path.Chunk, path.Instruction)))
        {
            return false;
        }

        var condition = GetRegister(path, instruction.Reg0);
        var takenPath = isChunkLeap
            ? path.Fork(instruction.Chunk, 0, null)
            : path.Fork(path.Chunk, instruction.DestInstruction, null);

        if (!isChunkLeap && valueNode >= 0 && valueNode != path.CurrentNode)
        {
            return AddTernary(path, takenPath, condition, valueNode);
        }

        if (sequencePoint == "m_LoopHeader")
        {
            AddLoop(path, condition);
            takenPath.DefaultOutflow = "m_Outflow_Loop";
            path.DefaultOutflow = "m_Outflow_Completed";
        }
        else if (condition is CaseCondition caseCondition)
        {
            var caseName = AddSwitchCase(path, caseCondition);
            takenPath.DefaultOutflow = caseName;
            path.DefaultOutflow = "m_DefaultCaseOutflow";
        }
        else
        {
            AddIf(path, condition);
            takenPath.DefaultOutflow = "m_IsTrue";
            path.DefaultOutflow = "m_IsFalse";
        }

        pathQueue.Enqueue(takenPath);
        path.Instruction++;
        return true;
    }

    private void AddIf(FlowPath path, ValueRef condition)
    {
        if (nodes.TryGetValue(path.CurrentNode, out var existing))
        {
            if (existing.ClassName != "Outflow_If")
            {
                Report($"Node {existing.Id} ({existing.ClassName}) branches in a way that is not supported yet.");
            }

            return;
        }

        var node = AddNode(path.CurrentNode, "Outflow_If", isFlow: true);
        SetInputPort(node, node.Fields, "m_Condition", "m_bCondition", UnwrapNotCondition(node, condition));
    }

    // Branch nodes invert their condition themselves through m_bNotCondition
    private static ValueRef UnwrapNotCondition(PulseEditorNode node, ValueRef condition)
    {
        if (condition is not InvertedValue inverted)
        {
            return condition;
        }

        node.Fields.Add("m_bNotCondition", true);
        return inverted.Inner;
    }

    private void AddLoop(FlowPath path, ValueRef condition)
    {
        var nodeId = path.CurrentNode;
        if (nodes.ContainsKey(nodeId))
        {
            return;
        }

        if (condition is InternalValue { Port: "__loop_cond" })
        {
            AddRangeLoop(path, nodeId);
            return;
        }

        var node = AddNode(nodeId, "Outflow_LoopWhile", isFlow: true);
        node.InflowName = "m_Inflow_Start";
        SetInputPort(node, node.Fields, "m_Condition", "m_Condition", UnwrapNotCondition(node, condition));
    }

    // A range loop keeps its index in the register its start value was loaded into, and tests it against the stop value
    private void AddRangeLoop(FlowPath path, int nodeId)
    {
        var chunk = Definition.Chunks[path.Chunk];
        var indexRegister = chunk.FindRegister(nodeId, "m_Start");
        var stepRegister = chunk.FindRegister(nodeId, "m_Step");
        var includesEndpoint = chunk.FindRegister(nodeId, "index <= stop") >= 0;
        var stopTestRegister = chunk.FindRegister(nodeId, includesEndpoint ? "index <= stop" : "index < stop");

        var stop = stopTestRegister >= 0 && path.Registers.GetValueOrDefault(stopTestRegister) is InternalValue { Second: { } stopValue }
            ? stopValue
            : new UnknownValue();

        var isFloat = chunk.RegisterValueType(indexRegister) == PulseValueType.PVAL_FLOAT;

        var node = AddNode(nodeId, isFloat ? "Outflow_LoopFloatRange" : "Outflow_LoopIntegerRange", isFlow: true);
        node.InflowName = "m_Inflow_Start";
        SetInputPort(node, node.Fields, "m_Start", "m_Start", indexRegister >= 0 ? GetRegister(path, indexRegister) : new UnknownValue());
        SetInputPort(node, node.Fields, "m_Stop", "m_Stop", stop);
        SetInputPort(node, node.Fields, "m_Step", "m_Step", stepRegister >= 0 ? GetRegister(path, stepRegister) : new UnknownValue());

        if (includesEndpoint)
        {
            node.Fields.Add("m_bRangeIncludesEndpoint", true);
        }

        if (indexRegister >= 0)
        {
            loopIndexRegisters[nodeId] = indexRegister;
        }
    }

    // Switches test each case in turn, the outflow of a case is named after its value
    private string AddSwitchCase(FlowPath path, CaseCondition condition)
    {
        if (!nodes.TryGetValue(path.CurrentNode, out var node))
        {
            var (className, portName) = condition.SwitchType switch
            {
                PulseValueType.PVAL_STRING => ("Outflow_StringSwitch", "pSwitchValue"),
                PulseValueType.PVAL_SCHEMA_ENUM or PulseValueType.PVAL_ENUM => ("Outflow_SchemaEnumSwitch", "nSwitchValue"),
                _ => ("Outflow_IntSwitch", "nSwitchValue"),
            };

            node = AddNode(path.CurrentNode, className, isFlow: true);
            SetInputPort(node, node.Fields, "m_SwitchValue", portName, condition.SwitchValue);
            node.Fields.Add("m_Cases", KVObject.Array());
        }

        if (condition.CaseValue is not LiteralValue caseValue)
        {
            Report($"Switch {node.Id} compares against a case value that is not a constant.");
            return "m_DefaultCaseOutflow";
        }

        node.Fields["m_Cases"].Add(caseValue.Value);
        return caseValue.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A ternary compiles to a branch inside a value: each side copies its value into the result register, then both
    /// meet again. Both sides are evaluated here and the flow continues where they meet.
    /// </summary>
    /// <returns>Whether the flow continues past this instruction.</returns>
    private bool AddTernary(FlowPath path, FlowPath truePath, ValueRef condition, int nodeId)
    {
        var falsePath = path.Fork(path.Chunk, path.Instruction + 1, null);

        if (!TryEvaluateTernaryBranch(falsePath, out var falseValue, out var resultRegister, out var mergeInstruction)
            || !TryEvaluateTernaryBranch(truePath, out var trueValue, out _, out _))
        {
            Report($"Could not follow the branches of ternary {nodeId}.");
            return false;
        }

        if (!nodes.ContainsKey(nodeId))
        {
            var node = AddNode(nodeId, "ExprTernaryConditional", isFlow: false);
            SetInputPort(node, node.Fields, "m_Condition", "cond", condition);
            SetInputPort(node, node.Fields, "m_TrueVal", "true_val", trueValue, refineLiteralType: true);
            SetInputPort(node, node.Fields, "m_FalseVal", "false_val", falseValue, refineLiteralType: true);
        }

        SetRegister(path, resultRegister, new NodeOutput(nodeId, "retval"));
        path.Instruction = mergeInstruction;
        return true;
    }

    private bool TryEvaluateTernaryBranch(FlowPath branch, out ValueRef value, out int resultRegister, out int mergeInstruction)
    {
        var chunk = Definition.Chunks[branch.Chunk];
        var instructions = chunk.Instructions;

        for (var steps = 0; steps < instructions.Count && branch.Instruction >= 0 && branch.Instruction < instructions.Count; steps++)
        {
            var instruction = instructions[branch.Instruction];

            if (instruction.Code == PulseInstructionCode.COPY)
            {
                value = GetRegister(branch, instruction.Reg1);
                resultRegister = instruction.Reg0;
                mergeInstruction = branch.Instruction + 1;

                if (mergeInstruction < instructions.Count && instructions[mergeInstruction].Code == PulseInstructionCode.JUMP)
                {
                    mergeInstruction = instructions[mergeInstruction].DestInstruction;
                }

                return true;
            }

            var debugInfo = chunk.DebugInfo(branch.Instruction);
            if (!ExecuteInstruction(branch, instruction, debugInfo.ValueNode, debugInfo.SequencePoint))
            {
                break;
            }
        }

        value = new UnknownValue();
        resultRegister = -1;
        mergeInstruction = -1;
        return false;
    }
}

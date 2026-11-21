using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Graphs;

internal sealed partial class PulseGraphWalker
{
    private sealed class FlowPath
    {
        public required int Chunk { get; set; }
        public required int Instruction { get; set; }
        public required Dictionary<int, ValueRef> Registers { get; set; }
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

    // Flow nodes the walk entered, with the sequence point the cursor arrived at
    private readonly Dictionary<int, string?> enteredFlowNodes = [];
    private readonly HashSet<int> fireAndForgetNodes = [];

    // Instructions that fork the flow, followed once no matter how many paths reach them, e.g. through a loop's back edge
    private readonly HashSet<(int Chunk, int Instruction)> forkingInstructions = [];

    // Number of outflows each node runs through calls, which is all a simultaneous outflow compiles to
    private readonly Dictionary<int, int> outflowCallCounts = [];

    // Registers of entry nodes with several outparams, which the compiler numbers in the order of the connections reading them
    private readonly Dictionary<int, Dictionary<string, int>> entryOutparamRegisters = [];

    private readonly Dictionary<int, HashSet<int>> nonVariableValueNodes = [];

    // Expression nodes by the value node their instructions were compiled under
    private readonly Dictionary<int, int> expressionOwners = [];
    private readonly Dictionary<(int Expression, int Part), int> renumberedExpressionParts = [];

    // First node writing each temporary variable that has no editor node, by temp var bank and index
    private readonly Dictionary<(int Bank, int Index), int> undeclaredTempVarWriters = [];

    private void AddEntryNode(int cellIndex)
    {
        var cell = Definition.Cells[cellIndex];
        var id = cellNodeIds[cellIndex];
        var className = Definition.CellClass(cellIndex);
        PulseEditorNode node;

        switch (className)
        {
            case "CPulseCell_Inflow_Method":
                node = AddNode(id, "Inflow_Method");
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
                node = AddNode(id, "Inflow_EntOutput");
                node.Fields.Add("m_SourceEntity", cell.GetStringProperty("m_SourceEntity", string.Empty));
                node.Fields.Add("m_SourceOutput", cell.GetStringProperty("m_SourceOutput", string.Empty));
                node.Fields.Add("m_CachedOutputType", cell.GetStringProperty("m_ExpectedParamType", "PVAL_VOID"));
                break;

            default:
                node = AddGenericNode(id, className, isFlow: true);
                node.Fields.Add("m_GenericKeys", GetGenericKeys(cellIndex));
                break;
        }

        node.CellClass = className;

        var entryChunk = cell.GetInt32Property("m_EntryChunk");
        var registerMap = PulseRegisterMap.Read(cell, "m_RegisterMap");
        var path = new FlowPath
        {
            Chunk = entryChunk,
            Instruction = 0,
            Registers = [],
            CurrentNode = id,
        };

        AddOutparamRegisters(path, registerMap, id);

        if (registerMap.OutParams.Count > 1)
        {
            entryOutparamRegisters[id] = registerMap.OutParams.ToDictionary(param => param.Name, param => param.Register);
        }

        enteredFlowNodes.Add(id, null);
        pathQueue.Enqueue(path);
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
        var node = AddNode(id, isFlow ? "Flow_Generic" : "Value_Generic");
        node.Fields.Add("m_GenericNodeType", nodeType);
        node.Fields.Add("m_GenericPorts", KVObject.Collection());
        return node;
    }

    private KVObject GetGenericKeys(int cellIndex)
    {
        var keys = KVObject.Collection();

        foreach (var (key, value) in Definition.Cells[cellIndex])
        {
            if (key is "_class" or "m_nEditorNodeID" or "m_EntryChunk" or "m_RegisterMap" || Definition.IsOutflowKey(cellIndex, key))
            {
                continue;
            }

            // The editor keeps this setting as a custom attribute, a table holding it under the same name
            if (key == "m_strEntityOutput")
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

    private void AddOutparamRegisters(FlowPath path, PulseRegisterMap registerMap, int nodeId)
    {
        foreach (var (name, register) in registerMap.OutParams)
        {
            SetRegister(path, register, new NodeOutput(nodeId, name));
        }
    }

    private void SetRegister(FlowPath path, int register, ValueRef value)
    {
        path.Registers[register] = value;

        if (value is NodeOutput output && Definition.Chunks[path.Chunk].Register(register) is { } info)
        {
            outputTypes.TryAdd((output.NodeId, output.Port), info.ValueType);
        }
    }

    private void WalkPath(FlowPath path)
    {
        var visited = new HashSet<(int Chunk, int Instruction)>();

        while (true)
        {
            var chunk = Definition.Chunks[path.Chunk];

            if (!chunk.HasDebugInfo)
            {
                unwalkedFlows.Add((path.CurrentNode, GetLeavingOutflow(path), path.Chunk));
                return;
            }

            if (path.Instruction < 0 || path.Instruction >= chunk.Instructions.Count || !visited.Add((path.Chunk, path.Instruction)))
            {
                return;
            }

            var (flowNode, valueNode, sequencePoint) = chunk.DebugInfos[path.Instruction];

            if (flowNode >= 0 && flowNode != path.CurrentNode)
            {
                ConnectFlow(path.CurrentNode, GetLeavingOutflow(path), flowNode, sequencePoint);

                if (!enteredFlowNodes.TryAdd(flowNode, sequencePoint))
                {
                    return;
                }

                path.CurrentNode = flowNode;
                path.DefaultOutflow = null;
            }

            path.LastSequencePoint = sequencePoint;

            if (!ExecuteInstruction(path, chunk.Instructions[path.Instruction], valueNode, sequencePoint))
            {
                return;
            }
        }
    }

    private string GetLeavingOutflow(FlowPath path)
    {
        var last = path.LastSequencePoint;

        if (!IsSequencePointName(last) && enteredFlowNodes.GetValueOrDefault(path.CurrentNode) != last)
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

    // Returns whether the flow continues past this instruction
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
                var type = constant.GetStringProperty("m_Type", "PVAL_INVALID");

                if (Definition.Chunks[path.Chunk].Register(instruction.Reg0)?.OriginPort == "__empty_array")
                {
                    if (!nodes.ContainsKey(valueNode))
                    {
                        var node = AddNode(valueNode, "Value_MakeArray");
                        node.Fields.Add("m_UnifiedElementType", type[(type.IndexOf(':', StringComparison.Ordinal) + 1)..]);
                        node.Fields.Add("m_In_Elements", KVObject.Collection());
                    }

                    SetRegister(path, instruction.Reg0, new NodeOutput(valueNode, "retval"));
                    break;
                }

                SetRegister(path, instruction.Reg0, new LiteralValue(constant["m_Value"], type));
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

            // Invokes the cell for every element of reg0, each passed in reg1
            case PulseInstructionCode.BROADCAST_INVOKE:
                SetRegister(path, instruction.Reg1, GetRegister(path, instruction.Reg0));
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
                // Temporary variables with no editor node are declared by every node writing them, reads link to the first
                var tempVar = Definition.TempVar(path.Chunk, instruction.TempVarIdx);
                var declaringNode = tempVar.GetInt32Property("m_nEditorNodeID", -1);
                if (declaringNode < 0)
                {
                    declaringNode = undeclaredTempVarWriters.GetValueOrDefault((Definition.Chunks[path.Chunk].TempVarBank, instruction.TempVarIdx), -1);
                }

                SetRegister(path, instruction.Reg0, new LocalNameValue(tempVar.GetStringProperty("m_Name", string.Empty), declaringNode));
                break;
            }

            case PulseInstructionCode.SET_TEMPVAR:
            case PulseInstructionCode.SET_TEMPVAR_OBSERVABLE:
                AddSetTempVar(path, instruction);
                break;

            // Writes reg0 into the element reg2 indexes, reg1 is unused
            case PulseInstructionCode.SET_VAR_ARRAY_ELEMENT_1D:
                if (AddSetVariable(path, instruction, notifiesListeners: true) is { } arraySet)
                {
                    var indexPorts = KVObject.Collection();
                    arraySet.Fields.Add("m_nArrayIndexDimension", 1);
                    arraySet.Fields.Add("m_ArrayIndexPorts", indexPorts);
                    SetInputPort(arraySet, indexPorts, "ArrayIndex0", "ArrayIndex0", GetRegister(path, instruction.Reg2));
                }

                break;

            case PulseInstructionCode.GET_ARRAY_ELEMENT:
            case PulseInstructionCode.ELEMENT_ACCESS:
                AddElementRead(path, instruction, valueNode);
                break;

            case PulseInstructionCode.GET_BLACKBOARD_REFERENCE:
                SetRegister(path, instruction.Reg0, new BlackboardValue(instruction.BlackboardReferenceIdx, Definition.Chunks[path.Chunk].RegisterType(instruction.Reg0)));
                break;

            case PulseInstructionCode.SET_BLACKBOARD_REFERENCE:
                AddSetBlackboardReference(path, instruction);
                break;

            case PulseInstructionCode.LOOP_BREAK:
                if (!nodes.ContainsKey(path.CurrentNode))
                {
                    AddNode(path.CurrentNode, "Outflow_Break");
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

    // Reads reg2 of reg1: an array element by index, or a component whose name the index register keeps
    private void AddElementRead(FlowPath path, PulseInstruction instruction, int valueNode)
    {
        var isArray = instruction.Code == PulseInstructionCode.GET_ARRAY_ELEMENT;

        // A for-each loop reads the element of each iteration itself
        if (valueNode < 0)
        {
            SetRegister(path, instruction.Reg0, new NodeOutput(path.CurrentNode, "element"));
            return;
        }

        if (!nodes.ContainsKey(valueNode))
        {
            if (isArray)
            {
                var node = AddNode(valueNode, "Value_ArrayElement");
                SetInputPort(node, node.Fields, "m_In_Array", "Array", GetRegister(path, instruction.Reg1));
                SetInputPort(node, node.Fields, "m_In_Index", "Index", GetRegister(path, instruction.Reg2));
            }
            else
            {
                var node = AddNode(valueNode, "ExprGetProperty");
                SetInputPort(node, node.Fields, "m_ObjectParam", "_Target", GetRegister(path, instruction.Reg1));
                node.Fields.Add("m_PropertyName", Definition.Chunks[path.Chunk].Register(instruction.Reg2)?.OriginPort ?? string.Empty);
            }
        }

        SetRegister(path, instruction.Reg0, new NodeOutput(valueNode, isArray ? "element" : "retval"));
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
            var node = AddNode(valueNode, "Value_VarReference");
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
                var valueNode = chunk.DebugInfos[index].ValueNode;

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
        var nodeId = origin?.OriginNode ?? -1;
        var port = origin?.OriginPort ?? string.Empty;

        // Flow nodes such as loops and switches compute their own conditions
        if (nodeId == path.CurrentNode)
        {
            ValueRef value = (code, port) switch
            {
                (PulseInstructionCode.NOT, "not_condition") => new InvertedValue(GetRegister(path, reg1)),
                (PulseInstructionCode.EQ, "__case_cond") => new CaseCondition(GetRegister(path, reg1), GetRegister(path, reg2), chunk.RegisterValueType(reg1)),
                (PulseInstructionCode.ADD, "start + (index * delta)") => new NodeOutput(nodeId, "value"),
                (PulseInstructionCode.DIV, "(end - start) / (count - 1)") => new NodeOutput(nodeId, "step"),
                _ => new InternalValue(port, reg2 >= 0 ? GetRegister(path, reg2) : null),
            };

            SetRegister(path, reg0, value);
            return;
        }

        if (nodeId < 0)
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

            var node = AddNode(nodeId, className);
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

        // A for-each loop counts its array before the first iteration
        if (!isValue && binding.FuncName == "CPulseArraylib::ArrayCount")
        {
            SetRegister(path, binding.RegisterMap.OutParams[0].Register, new InternalValue(ArrayCountPort, GetRegister(path, binding.RegisterMap.InParams[0].Register)));
            return;
        }

        // An array node appends its elements one by one to an empty array
        if (binding.FuncName == "CPulseArraylib::ArrayAppend" && nodes.TryGetValue(nodeId, out var array) && array.ClassName == "Value_MakeArray")
        {
            AddArrayElement(path, array, binding.RegisterMap);
            AddOutparamRegisters(path, binding.RegisterMap, nodeId);
            return;
        }

        if (!nodes.ContainsKey(nodeId))
        {
            var node = AddGenericNode(nodeId, binding.FuncName, !isValue);
            SetRegisterMapPorts(path, node, node.Fields["m_GenericPorts"], binding.RegisterMap);
        }

        AddOutparamRegisters(path, binding.RegisterMap, nodeId);
    }

    private void AddArrayElement(FlowPath path, PulseEditorNode array, PulseRegisterMap registerMap)
    {
        var chunk = Definition.Chunks[path.Chunk];
        var elements = array.Fields["m_In_Elements"];
        var position = 0;

        for (var index = 0; index < path.Instruction; index++)
        {
            if (chunk.Instructions[index].Code == PulseInstructionCode.LIBRARY_INVOKE && chunk.DebugInfos[index].ValueNode == array.Id)
            {
                position++;
            }
        }

        if (position < elements.Count || !registerMap.TryGetInParam("element", out var register))
        {
            return;
        }

        var name = position.ToString(CultureInfo.InvariantCulture);
        SetInputPort(array, elements, name, name, GetRegister(path, register));
    }

    private void AddCellInvoke(FlowPath path, PulseInstruction instruction, int valueNode)
    {
        var binding = Definition.InvokeBindings[instruction.InvokeBindingIndex];
        var cellIndex = binding.CellIndex;
        var nodeId = cellNodeIds[cellIndex];
        var isValue = nodeId != path.CurrentNode && valueNode == nodeId;

        if (!nodes.ContainsKey(nodeId))
        {
            AddCellNode(path, cellIndex, nodeId, binding, isValue, instruction.Code == PulseInstructionCode.BROADCAST_INVOKE ? instruction.Reg0 : -1);

            if (!isValue)
            {
                QueueCellOutflows(path, cellIndex, nodeId);
            }
        }

        AddOutparamRegisters(path, binding.RegisterMap, nodeId);
    }

    private void AddCellNode(FlowPath path, int cellIndex, int nodeId, PulseInvokeBinding binding, bool isValue, int broadcastRegister)
    {
        var cell = Definition.Cells[cellIndex];
        var className = Definition.CellClass(cellIndex);
        var registerMap = binding.RegisterMap;
        var chunk = Definition.Chunks[path.Chunk];
        PulseEditorNode node;

        switch (className)
        {
            case "CPulseCell_Step_EntFire":
                node = AddNode(nodeId, "Step_EntFire");

                // Firing at an entity name is broadcast to every entity of that name
                if (chunk.RegisterValueType(broadcastRegister) == PulseValueType.PVAL_ENTITY_NAME)
                {
                    SetInputPort(node, node.Fields, "m_TargetName", "TargetName", GetRegister(path, broadcastRegister));
                }
                else if (registerMap.TryGetInParam("hTarget", out var targetRegister))
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
                node = AddNode(nodeId, "Step_DebugLog");
                SetInputPort(node, node.Fields, "m_Message", "pMessage", registerMap.TryGetInParam("pMessage", out var messageRegister) ? GetRegister(path, messageRegister) : new UnknownValue());
                break;

            case "CPulseCell_Step_PublicOutput":
            {
                node = AddNode(nodeId, "Step_PublicOutput");
                node.Fields.Add("m_OutputDefID", publicOutputNodeIds[cell.GetInt32Property("m_OutputIndex")]);
                var argList = KVObject.Collection();
                node.Fields.Add("m_ArgList", argList);
                SetRegisterMapPorts(path, node, argList, registerMap);
                break;
            }

            case "CPulseCell_ReturnValues":
            {
                node = AddNode(nodeId, "Outflow_Return");
                var returnValues = KVObject.Collection();
                node.Fields.Add("m_ReturnValues", returnValues);
                SetRegisterMapPorts(path, node, returnValues, registerMap);
                break;
            }

            case "CPulseCell_FireCursors":
                node = AddNode(nodeId, "Step_FireCursors");
                node.Fields.Add("m_nChildCursors", cell.GetInt32Property("m_nChildCursors"));
                node.InflowName = "Start";

                if (!cell.GetBooleanProperty("m_bWaitForChildOutflows"))
                {
                    node.Fields.Add("m_nCallMode", "ASYNC_FIRE_AND_FORGET");
                }
                break;

            case "CPulseCell_Timeline":
            {
                node = AddNode(nodeId, "Step_Timeline");
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
                node = AddNode(nodeId, "Outflow_Cycle");
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
            AddOutparamRegisters(outflowPath, outflow.RegisterMap, nodeId);
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

            if (!isPortless)
            {
                outflowCallCounts[ownerId] = outflowCallCounts.GetValueOrDefault(ownerId) + 1;
            }

            pathQueue.Enqueue(body);
            return;
        }

        var nodeId = callInfo.EditorNode >= 0 ? callInfo.EditorNode : path.CurrentNode;

        if (!nodes.ContainsKey(nodeId))
        {
            var node = AddNode(nodeId, "Step_CallMethod");
            node.Fields.Add("m_MethodID", callInfo.CallMethodId);

            if (isAsync)
            {
                node.Fields.Add("m_nCallMode", "ASYNC_FIRE_AND_FORGET");
            }

            var argList = KVObject.Collection();
            node.Fields.Add("m_ArgList", argList);
            SetRegisterMapPorts(path, node, argList, callInfo.RegisterMap);
        }

        AddOutparamRegisters(path, callInfo.RegisterMap, nodeId);
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

    // Null when an earlier path already added the node
    private PulseEditorNode? AddSetVariable(FlowPath path, PulseInstruction instruction, bool notifiesListeners)
    {
        if (nodes.ContainsKey(path.CurrentNode))
        {
            return null;
        }

        var node = AddNode(path.CurrentNode, "Step_SetVar");
        node.Fields.Add("m_VarDefID", variableNodeIds[instruction.Var]);
        SetInputPort(node, node.Fields, "m_Value", "m_Value", GetRegister(path, instruction.Reg0));
        node.AddVariableWrite(PulseVariableKind.Variable, variableNodeIds[instruction.Var]);

        // Writes to an observable variable notify its listeners unless the node opted out
        if (!notifiesListeners && Definition.Vars[instruction.Var].GetBooleanProperty("m_bIsObservable"))
        {
            node.Fields.Add("m_bDoNotNotifyListeners", true);
        }

        return node;
    }

    // A Set Variable that targets a variable of another graph instead of one of its own
    private void AddSetBlackboardReference(FlowPath path, PulseInstruction instruction)
    {
        if (nodes.ContainsKey(path.CurrentNode))
        {
            return;
        }

        var node = AddNode(path.CurrentNode, "Step_SetVar");
        node.Fields.Add("m_VarDefID", -1);
        SetInputPort(node, node.Fields, "m_Value", "m_Value", GetRegister(path, instruction.Reg0));
        node.Fields.Add("m_BlackboardReference", BuildBlackboardReference(instruction.BlackboardReferenceIdx, Definition.Chunks[path.Chunk].RegisterType(instruction.Reg0)));
        node.AddVariableWrite(PulseVariableKind.BlackboardReference, instruction.BlackboardReferenceIdx);
    }

    // The node that declares a temporary variable writes its initial value, any other write is a Set Variable
    private void AddSetTempVar(FlowPath path, PulseInstruction instruction)
    {
        if (nodes.ContainsKey(path.CurrentNode))
        {
            return;
        }

        var tempVar = Definition.TempVar(path.Chunk, instruction.TempVarIdx);
        var declaringNode = tempVar.GetInt32Property("m_nEditorNodeID", -1);
        var value = GetRegister(path, instruction.Reg0);

        if (declaringNode == path.CurrentNode || declaringNode < 0)
        {
            var node = AddNode(path.CurrentNode, "Step_CreateTempVar");
            node.Fields.Add("m_VarName", tempVar.GetStringProperty("m_Name", string.Empty));
            node.Fields.Add("m_ValueType", tempVar.GetStringProperty("m_Type", "PVAL_INVALID"));
            SetInputPort(node, node.Fields, "m_Value", "m_Value", value);

            if (declaringNode < 0)
            {
                undeclaredTempVarWriters.TryAdd((Definition.Chunks[path.Chunk].TempVarBank, instruction.TempVarIdx), node.Id);
            }
        }
        else
        {
            var node = AddNode(path.CurrentNode, "Step_SetVar");
            node.Fields.Add("m_VarDefID", declaringNode);
            SetInputPort(node, node.Fields, "m_Value", "m_Value", value);
            node.AddVariableWrite(PulseVariableKind.TempVar, declaringNode);
        }
    }

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
            AddLoop(path, takenPath, condition);
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

        var node = AddNode(path.CurrentNode, "Outflow_If");
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

    // Marks the element count a for-each loop reads from its array
    private const string ArrayCountPort = "array count";

    private void AddLoop(FlowPath path, FlowPath body, ValueRef condition)
    {
        var nodeId = path.CurrentNode;
        if (nodes.ContainsKey(nodeId))
        {
            return;
        }

        var chunk = Definition.Chunks[path.Chunk];

        if (condition is InternalValue { Port: "__loop_cond" }
            && chunk.FindWriter(nodeId, "index < stop") is { } stopTest && chunk.FindWriter(nodeId, "step > 0") is { } stepTest)
        {
            // The index lives in the register the start value was loaded into, a range including its endpoint tests it with LTE
            var range = AddNode(nodeId, "Outflow_LoopIntegerRange");
            range.InflowName = "m_Inflow_Start";
            SetInputPort(range, range.Fields, "m_Start", "m_Start", GetRegister(path, stopTest.Reg1));
            SetInputPort(range, range.Fields, "m_Stop", "m_Stop", GetRegister(path, stopTest.Reg2));
            SetInputPort(range, range.Fields, "m_Step", "m_Step", GetRegister(path, stepTest.Reg2));

            if (stopTest.Code == PulseInstructionCode.LTE)
            {
                range.Fields.Add("m_bRangeIncludesEndpoint", true);
            }

            SetRegister(body, stopTest.Reg1, new NodeOutput(nodeId, "index"));
            return;
        }

        if (condition is InternalValue { Port: "__loop_cond", Second: InternalValue { Port: ArrayCountPort, Second: { } array } }
            && chunk.FindWriter(nodeId, "__loop_cond") is { } countTest)
        {
            var forEach = AddNode(nodeId, "Outflow_LoopForEach");
            forEach.InflowName = "m_Inflow_Start";
            SetInputPort(forEach, forEach.Fields, "m_In_Array", "Array", array);
            SetRegister(body, countTest.Reg1, new NodeOutput(nodeId, "index"));
            return;
        }

        if (condition is InternalValue { Port: "index < stop", Second: { } count } && chunk.FindWriter(nodeId, "end - start") is { } span)
        {
            // A float range counts its steps up to m_Count and derives each value from the span between both ends
            var range = AddNode(nodeId, "Outflow_LoopFloatRange");
            range.InflowName = "m_Inflow_Start";
            SetInputPort(range, range.Fields, "m_Start", "m_Start", GetRegister(path, span.Reg2));
            SetInputPort(range, range.Fields, "m_Stop", "m_Stop", GetRegister(path, span.Reg1));
            SetInputPort(range, range.Fields, "m_Count", "m_Count", count);
            return;
        }

        var node = AddNode(nodeId, "Outflow_LoopWhile");
        node.InflowName = "m_Inflow_Start";
        SetInputPort(node, node.Fields, "m_Condition", "m_Condition", UnwrapNotCondition(node, condition));
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

            node = AddNode(path.CurrentNode, className);
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

    // A ternary compiles to a branch inside a value: each side copies its value into the result register, then both meet again
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
            var node = AddNode(nodeId, "ExprTernaryConditional");
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

            var debugInfo = chunk.DebugInfos[branch.Instruction];
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

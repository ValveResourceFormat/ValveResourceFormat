
namespace ValveResourceFormat.Graphs;

/// <summary>
/// The role a pulse graph node plays. Cell roles follow the pulse editor's own node palettes in
/// scripts/tools/pulse_scene_styles_v2; the instruction roles have no editor counterpart.
/// </summary>
internal enum PulseCategory
{
    /// <summary>The graph info card, and any cell class the table does not name.</summary>
    Other,
    /// <summary>Cell a flow starts at, taking no inflow.</summary>
    EntryPoint,
    /// <summary>Cell that suspends its cursor and resumes later.</summary>
    Yielding,
    /// <summary>Cell that computes a value instead of taking flow.</summary>
    ValueCell,
    /// <summary>Cell that picks between its outflows.</summary>
    Selector,
    /// <summary>Cell that answers a condition a selector reads.</summary>
    Requirement,
    /// <summary>Cell that performs an action and passes flow straight on.</summary>
    Step,
    /// <summary>Branch, loop, chunk leap and the ends of a flow.</summary>
    FlowControl,
    /// <summary>Bytecode instruction with no cell behind it.</summary>
    Instruction,
    /// <summary>Library binding or a call into another chunk.</summary>
    Call,
    /// <summary>Variable definition that reads and writes link back to.</summary>
    Variable,
}

/// <summary>
/// The pulse graph palette: node headers are coloured by <see cref="PulseCategory"/>, sockets and
/// wires by the value type they carry.
/// </summary>
internal static class PulseHues
{
    /// <summary>
    /// Colour slot a node category is drawn in. Entry point, yielding, value and flow control take
    /// the colours the pulse editor gives those roles in scripts/tools/pulse_scene_styles_v2.
    /// </summary>
    /// <param name="category">The category to colour.</param>
    public static GraphHue HueOf(PulseCategory category) => category switch
    {
        PulseCategory.EntryPoint => GraphHue.Green,
        PulseCategory.Yielding => GraphHue.Amber,
        PulseCategory.ValueCell => GraphHue.Teal,
        PulseCategory.Selector => GraphHue.Magenta,
        PulseCategory.Requirement => GraphHue.Olive,
        PulseCategory.Step => GraphHue.Maroon,
        PulseCategory.FlowControl => GraphHue.Neutral,
        PulseCategory.Instruction => GraphHue.Blue,
        PulseCategory.Call => GraphHue.Cyan,
        PulseCategory.Variable => GraphHue.Indigo,
        _ => GraphHue.Red,
    };

    /// <summary>Hue the dashed variable read and write links and their sockets share.</summary>
    public const GraphHue VariableLinkHue = GraphHue.Indigo;

    /// <summary>
    /// The legend the pulse viewer advertises: the line samples first, then the node colour
    /// swatches, matching the two sub-sections the legend panel draws.
    /// </summary>
    public static IEnumerable<GraphLegendEntry> Legend()
    {
        yield return new("Flow", GraphHue.Neutral, GraphLegendKind.Wire);
        yield return new("String value", GraphHue.Green, GraphLegendKind.Wire);
        yield return new("Number value", GraphHue.Amber, GraphLegendKind.Wire);
        yield return new("Bool value", GraphHue.Orange, GraphLegendKind.Wire);
        yield return new("Other value", GraphHue.Teal, GraphLegendKind.Wire);
        yield return new("Variable link", VariableLinkHue, GraphLegendKind.DashedWire);
        yield return new("Entry point", HueOf(PulseCategory.EntryPoint));
        yield return new("Action / step", HueOf(PulseCategory.Step));
        yield return new("Value cell", HueOf(PulseCategory.ValueCell));
        yield return new("Yielding cell", HueOf(PulseCategory.Yielding));
        yield return new("Selector", HueOf(PulseCategory.Selector));
        yield return new("Requirement", HueOf(PulseCategory.Requirement));
        yield return new("Flow control", HueOf(PulseCategory.FlowControl));
        yield return new("Instruction", HueOf(PulseCategory.Instruction));
        yield return new("Function call", HueOf(PulseCategory.Call));
        yield return new("Variable", HueOf(PulseCategory.Variable));
        yield return new("Graph info / unclassified", HueOf(PulseCategory.Other));
    }

    /// <summary>
    /// Buckets a compiled cell class. Yielding is the class's ancestry, which the compiled graph
    /// does not record - a yielding cell serialises its resume points under per-class names that
    /// read as ordinary outflows - so the set is named here, from the CS2, Dota 2 and Deadlock schema.
    /// </summary>
    /// <param name="cellClass">The cell's _class value.</param>
    public static PulseCategory CategoryOf(string cellClass) => cellClass switch
    {
        "CPulseCell_Inflow_BaseEntrypoint" or "CPulseCell_Inflow_EntOutputHandler"
            or "CPulseCell_Inflow_EventHandler" or "CPulseCell_Inflow_GraphHook"
            or "CPulseCell_Inflow_Method"
            or "CPulseCell_Inflow_ObservableVariableListener" => PulseCategory.EntryPoint,

        "CPulseCell_BaseLerp" or "CPulseCell_BaseState" or "CPulseCell_BaseYieldingInflow"
            or "CPulseCell_BooleanSwitchState" or "CPulseCell_CursorQueue"
            or "CPulseCell_FireCursors" or "CPulseCell_Inflow_Wait" or "CPulseCell_Inflow_Yield"
            or "CPulseCell_IntervalTimer" or "CPulseCell_LerpCameraSettings"
            or "CPulseCell_Outflow_ListenForAnimgraphTag"
            or "CPulseCell_Outflow_ListenForEntityOutput" or "CPulseCell_Outflow_PlayDynamicVCD"
            or "CPulseCell_Outflow_PlaySceneBase" or "CPulseCell_Outflow_PlaySequence"
            or "CPulseCell_Outflow_PlayVCD" or "CPulseCell_Outflow_PlayVCDBase"
            or "CPulseCell_Outflow_PlayVOLine" or "CPulseCell_Outflow_ScriptedSequence"
            or "CPulseCell_PlaySequence" or "CPulseCell_ShmupWaitForDuration"
            or "CPulseCell_Step_CallExternalMethod" or "CPulseCell_TestWaitWithAutoTracepoints"
            or "CPulseCell_TestWaitWithCursorState" or "CPulseCell_TestYieldForever"
            or "CPulseCell_TestYieldWithObservables"
            or "CPulseCell_Test_MultiOutflow_WithParams_Yielding" or "CPulseCell_Timeline"
            or "CPulseCell_WaitForCursorsWithTag" or "CPulseCell_WaitForCursorsWithTagBase"
            or "CPulseCell_WaitForObservable" or "CPulseCell_WaitForPanelClass" => PulseCategory.Yielding,

        "CPulseCell_BaseValue" or "CPulseCell_TestEnums"
            or "CPulseCell_Val_TestDomainFindEntityByName"
            or "CPulseCell_Val_TestDomainGetEntityName" or "CPulseCell_Value_Curve"
            or "CPulseCell_Value_Gradient" or "CPulseCell_Value_RandomFloat"
            or "CPulseCell_Value_RandomInt" or "CPulseCell_Value_TestValue50" => PulseCategory.ValueCell,

        "CPulseCell_ExampleSelector" or "CPulseCell_InlineNodeSkipSelector"
            or "CPulseCell_Outflow_CycleOrdered" or "CPulseCell_Outflow_CycleRandom"
            or "CPulseCell_Outflow_CycleShuffled"
            or "CPulseCell_PickBestOutflowSelector" => PulseCategory.Selector,

        "CPulseCell_BaseRequirement" or "CPulseCell_ExampleCriteria"
            or "CPulseCell_IsRequirementValid" or "CPulseCell_LimitCount" => PulseCategory.Requirement,

        "CPulseCell_Outflow_TestExplicitYesNo" or "CPulseCell_Outflow_TestRandomYesNo"
            or "CPulseCell_SoundEventStart" or "CPulseCell_Step_DebugLog"
            or "CPulseCell_Step_EntFire" or "CPulseCell_Step_FollowEntity"
            or "CPulseCell_Step_PublicOutput" or "CPulseCell_Step_SetAnimGraphParam"
            or "CPulseCell_Step_TestDomainCreateFakeEntity"
            or "CPulseCell_Step_TestDomainDestroyFakeEntity"
            or "CPulseCell_Step_TestDomainEntFire" or "CPulseCell_Step_TestDomainTracepoint"
            or "CPulseCell_Test_MultiInflow_NoDefault" or "CPulseCell_Test_MultiInflow_WithDefault"
            or "CPulseCell_Test_MultiOutflow_WithParams" or "CPulseCell_Test_NoInflow" => PulseCategory.Step,

        _ => PulseCategory.Other,
    };
}

using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_branch</c>. Holds a boolean and, when tested, fires <c>OnTrue</c> or <c>OnFalse</c> by it.
/// Every <see cref="LogicBranchListener"/> watching it hears when the value changes.
/// </summary>
public sealed class LogicBranch : BaseEntity
{
    private readonly List<LogicBranchListener> listeners = [];

    /// <summary>Gets the held value, the <c>InitialValue</c> keyvalue until an input changes it.</summary>
    public bool Value { get; private set; }

    /// <summary>Initializes a <c>logic_branch</c> from its keyvalues.</summary>
    public LogicBranch(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Value = KeyValues.GetBooleanProperty("initialvalue");
    }

    internal void AddListener(LogicBranchListener listener)
    {
        if (!listeners.Contains(listener))
        {
            listeners.Add(listener);
        }
    }

    [EntityInput("SetValue")]
    private void InputSetValue(EntityInputData data) => UpdateValue(data.Bool());

    [EntityInput("SetValueTest")]
    private void InputSetValueTest(EntityInputData data)
    {
        UpdateValue(data.Bool());
        Test(data.Activator);
    }

    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data) => UpdateValue(!Value);

    [EntityInput("ToggleTest")]
    private void InputToggleTest(EntityInputData data)
    {
        UpdateValue(!Value);
        Test(data.Activator);
    }

    [EntityInput("Test")]
    private void InputTest(EntityInputData data) => Test(data.Activator);

    private void UpdateValue(bool value)
    {
        if (Value == value)
        {
            return;
        }

        Value = value;

        // Queued rather than delivered, so listeners react after anything already waiting this tick
        foreach (var listener in listeners)
        {
            if (!listener.IsRemoved)
            {
                EntitySystem.QueueInput(listener, "_OnLogicBranchChanged", activator: this, caller: this);
            }
        }
    }

    private void Test(BaseEntity? activator)
        => EntitySystem.TriggerOutput(this, Value ? "OnTrue" : "OnFalse", activator);
}

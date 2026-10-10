using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_compare</c>. Compares a held value against another on request. Every output carries the
/// held value.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLogicCompare">CLogicCompare</seealso>
public sealed class LogicCompare : BaseEntity
{
    /// <summary>Gets the value that is compared, the <c>InitialValue</c> keyvalue until an input changes it.</summary>
    public float Value { get; private set; }

    /// <summary>Gets the value <see cref="Value"/> is compared against.</summary>
    public float CompareValue { get; private set; }

    /// <summary>Initializes a <c>logic_compare</c> from its keyvalues.</summary>
    public LogicCompare(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Value = KeyValues.GetFloatProperty("initialvalue");
        CompareValue = KeyValues.GetFloatProperty("comparevalue");
    }

    [EntityInput("SetValue")]
    private void InputSetValue(EntityInputData data) => Value = data.Float();

    [EntityInput("SetValueCompare")]
    private void InputSetValueCompare(EntityInputData data)
    {
        Value = data.Float();
        Compare(data.Activator);
    }

    [EntityInput("SetCompareValue")]
    private void InputSetCompareValue(EntityInputData data) => CompareValue = data.Float();

    [EntityInput("Compare")]
    private void InputCompare(EntityInputData data) => Compare(data.Activator);

    private void Compare(BaseEntity? activator)
    {
        var value = Value.ToString(CultureInfo.InvariantCulture);

        if (Value == CompareValue)
        {
            EntitySystem.TriggerOutput(this, "OnEqualTo", activator, value);
            return;
        }

        EntitySystem.TriggerOutput(this, "OnNotEqualTo", activator, value);
        EntitySystem.TriggerOutput(this, Value > CompareValue ? "OnGreaterThan" : "OnLessThan", activator, value);
    }
}

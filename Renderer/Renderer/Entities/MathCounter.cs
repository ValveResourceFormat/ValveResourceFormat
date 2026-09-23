using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>math_counter</c>. Holds a number, clamped to an authored range, reports every change through
/// <c>OutValue</c> and the number on request through <c>OnGetValue</c>, and tells when it reaches the ends
/// of that range.
/// </summary>
public sealed class MathCounter : BaseEntity
{
    /// <summary>Gets the current value.</summary>
    public float Value { get; private set; }

    /// <summary>Gets the lowest value the counter may hold.</summary>
    public float Min { get; private set; }

    /// <summary>Gets the highest value the counter may hold. Zero means unbounded, as in the engine.</summary>
    public float Max { get; private set; }

    /// <summary>Gets whether the counter accepts changes. The <c>Disable</c> input clears it.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>Initializes a <c>math_counter</c> from its keyvalues.</summary>
    public MathCounter(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Min = KeyValues.GetFloatProperty("min");
        Max = KeyValues.GetFloatProperty("max");
        Value = Clamp(KeyValues.GetFloatProperty("startvalue"));
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    [EntityInput("Add")]
    private void InputAdd(EntityInputData data) => SetValue(Value + data.Float(), data.Activator);

    [EntityInput("Subtract")]
    private void InputSubtract(EntityInputData data) => SetValue(Value - data.Float(), data.Activator);

    [EntityInput("SetValue")]
    private void InputSetValue(EntityInputData data) => SetValue(data.Float(), data.Activator);

    [EntityInput("SetValueNoFire")]
    private void InputSetValueNoFire(EntityInputData data)
    {
        if (IsEnabled)
        {
            Value = Clamp(data.Float());
        }
    }

    [EntityInput("SetHitMax")]
    private void InputSetHitMax(EntityInputData data)
    {
        Max = data.Float();
        SetValue(Value, data.Activator);
    }

    [EntityInput("SetHitMin")]
    private void InputSetHitMin(EntityInputData data)
    {
        Min = data.Float();
        SetValue(Value, data.Activator);
    }

    [EntityInput("GetValue")]
    private void InputGetValue(EntityInputData data) => EntitySystem.TriggerOutput(this, "OnGetValue", data.Activator, FormattedValue);

    /// <summary>The value as outputs carry it, which a <c>logic_case</c> compares its cases against.</summary>
    private string FormattedValue => Value.ToString(CultureInfo.InvariantCulture);

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => IsEnabled = false;

    private void SetValue(float value, BaseEntity? activator)
    {
        if (!IsEnabled)
        {
            return;
        }

        Value = Clamp(value);

        EntitySystem.TriggerOutput(this, "OutValue", activator, FormattedValue);

        // The engine reports hitting a limit every time it lands there, not only on the way in
        if (Max != 0f && Value >= Max)
        {
            EntitySystem.TriggerOutput(this, "OnHitMax", activator);
        }
        else if (Value <= Min && Min != 0f)
        {
            EntitySystem.TriggerOutput(this, "OnHitMin", activator);
        }
    }

    // An unset maximum means no upper bound
    private float Clamp(float value)
    {
        if (Max != 0f && value > Max)
        {
            return Max;
        }

        return value < Min ? Min : value;
    }
}

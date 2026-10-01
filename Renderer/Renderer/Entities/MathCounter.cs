using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>math_counter</c>. Holds a number, clamped to an authored range, and reports when it reaches or leaves
/// the ends of that range. <c>OutValue</c> and <c>OnGetValue</c> carry the held value.
/// </summary>
public sealed class MathCounter : BaseEntity
{
    private bool hitMin;
    private bool hitMax;

    /// <summary>Gets the current value.</summary>
    public float Value { get; private set; }

    /// <summary>Gets the lowest value the counter may hold. The range is ignored while both ends are zero.</summary>
    public float Min { get; private set; }

    /// <summary>Gets the highest value the counter may hold. The range is ignored while both ends are zero.</summary>
    public float Max { get; private set; }

    /// <summary>Gets whether the counter accepts changes. The <c>Disable</c> input clears it.</summary>
    public bool IsEnabled { get; private set; } = true;

    private bool HasRange => Min != 0f || Max != 0f;

    /// <summary>Initializes a <c>math_counter</c> from its keyvalues.</summary>
    public MathCounter(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        Min = KeyValues.GetFloatProperty("min");
        Max = KeyValues.GetFloatProperty("max");

        if (Min > Max)
        {
            (Min, Max) = (Max, Min);
        }

        // The engine reads the starting value as an integer, dropping any fraction
        Value = ClampToRange((int)KeyValues.GetDoubleProperty("startvalue"));
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    [EntityInput("Add")]
    private void InputAdd(EntityInputData data)
    {
        if (IsEnabled)
        {
            UpdateValue(Value + data.Float(), data.Activator);
        }
    }

    [EntityInput("Subtract")]
    private void InputSubtract(EntityInputData data)
    {
        if (IsEnabled)
        {
            UpdateValue(Value - data.Float(), data.Activator);
        }
    }

    [EntityInput("Multiply")]
    private void InputMultiply(EntityInputData data)
    {
        if (IsEnabled)
        {
            UpdateValue(Value * data.Float(), data.Activator);
        }
    }

    [EntityInput("Divide")]
    private void InputDivide(EntityInputData data)
    {
        if (!IsEnabled)
        {
            return;
        }

        // Dividing by zero keeps the value but still runs the update, outputs and all
        var divisor = data.Float();
        UpdateValue(divisor == 0f ? Value : Value / divisor, data.Activator);
    }

    [EntityInput("SetValue")]
    private void InputSetValue(EntityInputData data)
    {
        if (IsEnabled)
        {
            UpdateValue(data.Float(), data.Activator);
        }
    }

    [EntityInput("SetValueNoFire")]
    private void InputSetValueNoFire(EntityInputData data)
    {
        if (IsEnabled)
        {
            Value = ClampToRange(data.Float());
        }
    }

    // Moving a limit ignores Disable, and drags the other limit along rather than letting them cross
    [EntityInput("SetHitMax")]
    private void InputSetHitMax(EntityInputData data)
    {
        Max = data.Float();

        if (Min > Max)
        {
            Min = Max;
        }

        UpdateValue(Value, data.Activator);
    }

    [EntityInput("SetHitMin")]
    private void InputSetHitMin(EntityInputData data)
    {
        Min = data.Float();

        if (Min > Max)
        {
            Max = Min;
        }

        UpdateValue(Value, data.Activator);
    }

    [EntityInput("GetValue")]
    private void InputGetValue(EntityInputData data)
        => EntitySystem.TriggerOutput(this, "OnGetValue", data.Activator, FormatValue(), data.Caller);

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => IsEnabled = false;

    private void UpdateValue(float value, BaseEntity? activator)
    {
        if (HasRange)
        {
            // Tested against the unclamped value. A hit fires once on reaching a limit and rearms on leaving
            // it, and the changed-from outputs compare the value held before this update.
            if (value < Max)
            {
                if (Value == Max)
                {
                    EntitySystem.TriggerOutput(this, "OnChangedFromMax", activator);
                }

                hitMax = false;
            }
            else if (!hitMax)
            {
                hitMax = true;
                EntitySystem.TriggerOutput(this, "OnHitMax", activator);
            }

            if (Min < value)
            {
                if (Value == Min)
                {
                    EntitySystem.TriggerOutput(this, "OnChangedFromMin", activator);
                }

                hitMin = false;
            }
            else if (!hitMin)
            {
                hitMin = true;
                EntitySystem.TriggerOutput(this, "OnHitMin", activator);
            }

            value = MathUtils.Clamp(value, Min, Max);
        }

        Value = value;

        EntitySystem.TriggerOutput(this, "OutValue", activator, FormatValue());
    }

    private float ClampToRange(float value) => HasRange ? MathUtils.Clamp(value, Min, Max) : value;

    private string FormatValue() => Value.ToString(CultureInfo.InvariantCulture);
}

using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>The weight of a blend switching on and off over time.</summary>
struct BlendWeight
{
    public enum BlendWeightState : byte
    {
        Off,
        TurningOff,
        TurningOn,
        On,
    }

    public BlendWeightState State;
    public EasingOperation EasingOperation;
    public float BlendTime;
    public float DesiredBlendTime;

    public BlendWeight()
    {
        State = BlendWeightState.Off;
        EasingOperation = EasingOperation.None;
        DesiredBlendTime = 0.2f;
    }

    public void Reset(float desiredBlendTime, bool startOn)
    {
        State = startOn ? BlendWeightState.On : BlendWeightState.Off;
        DesiredBlendTime = desiredBlendTime;
        BlendTime = desiredBlendTime;
    }

    public readonly bool IsBlending => State is BlendWeightState.TurningOn or BlendWeightState.TurningOff;

    public readonly float GetWeight()
    {
        float weight;

        if (State == BlendWeightState.On)
        {
            weight = 1f;
        }
        else if (State == BlendWeightState.Off)
        {
            weight = 0f;
        }
        else
        {
            Debug.Assert(DesiredBlendTime > 0f);
            weight = BlendTime / DesiredBlendTime;

            // Some easing functions will under or overshoot
            if (EasingOperation is not EasingOperation.Linear and not EasingOperation.None)
            {
                weight = Math.Clamp(Easing.Evaluate(EasingOperation, weight), 0f, 1f);
            }

            if (State == BlendWeightState.TurningOff)
            {
                weight = 1f - weight;
            }
        }

        return weight;
    }

    /// <summary>Updates the weight and returns it, 0 being off and 1 on.</summary>
    public float Update(float deltaTime, bool isOn)
    {
        switch (State)
        {
            case BlendWeightState.On:
                if (!isOn)
                {
                    State = BlendWeightState.TurningOff;
                    BlendTime = 0f;
                }

                break;

            case BlendWeightState.Off:
                if (isOn)
                {
                    State = BlendWeightState.TurningOn;
                    BlendTime = 0f;
                }

                break;

            case BlendWeightState.TurningOn:
                if (!isOn)
                {
                    State = BlendWeightState.TurningOff;
                    BlendTime = DesiredBlendTime - BlendTime;
                }

                break;

            case BlendWeightState.TurningOff:
                if (isOn)
                {
                    State = BlendWeightState.TurningOn;
                    BlendTime = DesiredBlendTime - BlendTime;
                }

                break;
        }

        if (IsBlending)
        {
            BlendTime += deltaTime;

            // Is the blend complete?
            if (BlendTime >= DesiredBlendTime)
            {
                BlendTime = 0f;
                State = State == BlendWeightState.TurningOn ? BlendWeightState.On : BlendWeightState.Off;
            }
        }

        return GetWeight();
    }
}

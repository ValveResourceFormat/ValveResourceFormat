using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

static class Easing
{
    enum Function
    {
        Linear,
        Quad,
        Cubic,
        Quart,
        Quint,
        Sine,
        Expo,
        Circ,
    }

    static float In(Function function, float t) => function switch
    {
        Function.Linear => t,
        Function.Quad => t * t,
        Function.Cubic => t * t * t,
        Function.Quart => t * t * t * t,
        Function.Quint => t * t * t * t * t,
        Function.Sine => 1f - MathF.Cos(t * MathF.PI / 2f),
        Function.Expo => MathF.Pow(2f, 10f * (t - 1f)) - 0.001f,
        Function.Circ => -(MathF.Sqrt(1f - (t * t)) - 1f),
        _ => throw new UnreachableException(),
    };

    static float Out(Function function, float t) => 1f - In(function, 1f - t);

    static float InOut(Function function, float t)
    {
        if (t < 0.5f)
        {
            return In(function, 2f * t) * 0.5f;
        }

        return (Out(function, (2f * t) - 1f) * 0.5f) + 0.5f;
    }

    public static float Evaluate(EasingOperation operation, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return operation switch
        {
            EasingOperation.None or EasingOperation.Linear => t,

            EasingOperation.InQuad => In(Function.Quad, t),
            EasingOperation.OutQuad => Out(Function.Quad, t),
            EasingOperation.InOutQuad => InOut(Function.Quad, t),

            EasingOperation.InCubic => In(Function.Cubic, t),
            EasingOperation.OutCubic => Out(Function.Cubic, t),
            EasingOperation.InOutCubic => InOut(Function.Cubic, t),

            EasingOperation.InQuart => In(Function.Quart, t),
            EasingOperation.OutQuart => Out(Function.Quart, t),
            EasingOperation.InOutQuart => InOut(Function.Quart, t),

            EasingOperation.InQuint => In(Function.Quint, t),
            EasingOperation.OutQuint => Out(Function.Quint, t),
            EasingOperation.InOutQuint => InOut(Function.Quint, t),

            EasingOperation.InSine => In(Function.Sine, t),
            EasingOperation.OutSine => Out(Function.Sine, t),
            EasingOperation.InOutSine => InOut(Function.Sine, t),

            EasingOperation.InExpo => In(Function.Expo, t),
            EasingOperation.OutExpo => Out(Function.Expo, t),
            EasingOperation.InOutExpo => InOut(Function.Expo, t),

            EasingOperation.InCirc => In(Function.Circ, t),
            EasingOperation.OutCirc => Out(Function.Circ, t),
            EasingOperation.InOutCirc => InOut(Function.Circ, t),

            _ => throw new UnreachableException(),
        };
    }
}

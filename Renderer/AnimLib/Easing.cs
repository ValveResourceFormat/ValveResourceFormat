using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

// Robert Penner easing equations
static class Easing
{
    public static float Evaluate(EasingOperation operation, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return operation switch
        {
            EasingOperation.Linear => t,

            EasingOperation.InQuad => t * t,
            EasingOperation.OutQuad => t * (2f - t),
            EasingOperation.InOutQuad => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,

            EasingOperation.InCubic => t * t * t,
            EasingOperation.OutCubic => (t - 1f) * (t - 1f) * (t - 1f) + 1f,
            EasingOperation.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3) / 2f,

            EasingOperation.InQuart => t * t * t * t,
            EasingOperation.OutQuart => 1f - (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f),
            EasingOperation.InOutQuart => t < 0.5f ? 8f * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 4) / 2f,

            EasingOperation.InQuint => t * t * t * t * t,
            EasingOperation.OutQuint => (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) * (t - 1f) + 1f,
            EasingOperation.InOutQuint => t < 0.5f ? 16f * t * t * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 5) / 2f,

            EasingOperation.InSine => 1f - MathF.Cos(t * MathF.PI / 2f),
            EasingOperation.OutSine => MathF.Sin(t * MathF.PI / 2f),
            EasingOperation.InOutSine => 0.5f * (1f - MathF.Cos(MathF.PI * t)),

            EasingOperation.InExpo => t <= 0f ? 0f : MathF.Pow(2f, 10f * (t - 1f)),
            EasingOperation.OutExpo => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
            EasingOperation.InOutExpo => t <= 0f ? 0f : t >= 1f ? 1f : (t < 0.5f) ? MathF.Pow(2f, 20f * t - 10f) / 2f : (2f - MathF.Pow(2f, -20f * t + 10f)) / 2f,

            EasingOperation.InCirc => 1f - MathF.Sqrt(1f - t * t),
            EasingOperation.OutCirc => MathF.Sqrt(1f - (t - 1f) * (t - 1f)),
            EasingOperation.InOutCirc => t < 0.5f ? (1f - MathF.Sqrt(1f - 4f * t * t)) / 2f : (MathF.Sqrt(1f - (-2f * t + 2f) * (-2f * t + 2f)) + 1f) / 2f,

            EasingOperation.None => t,
            _ => throw new UnreachableException(),
        };
    }
}

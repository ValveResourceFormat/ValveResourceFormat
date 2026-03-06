using ValveResourceFormat.Renderer;

namespace ValveResourceFormat.Renderer.AnimLib;

static class Easing
{
    public static float Evaluate(EasingOperation operation, float t)
    {
        return MathUtils.Ease(operation, Math.Clamp(t, 0f, 1f));
    }
}

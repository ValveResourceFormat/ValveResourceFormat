using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneEnvironment;

/// <summary>
/// The fog an <c>env_fog_controller</c> sets: a color faded in with distance from a start to an end
/// distance, up to a maximum density. Of what is drawn, only the sky reads it: a sky fogged by distance
/// fades toward its color as far away as the far plane, and a sky fogged by angle fades toward its color.
/// </summary>
/// <param name="Color">The linear color the fog fades toward.</param>
/// <param name="Start">The distance the fog starts at.</param>
/// <param name="End">The distance the fog reaches its full density at.</param>
/// <param name="MaxDensity">The most the fog covers, from zero to one.</param>
/// <param name="Exponent">The power the fraction of the way from start to end is raised to.</param>
public readonly record struct LinearFog(Vector3 Color, float Start, float End, float MaxDensity, float Exponent)
{
    /// <summary>Gets the fog a map without a fog entity is drawn with: black, and nowhere dense.</summary>
    public static LinearFog None => default;

    /// <summary>Gets the linear color blended in while looking away from <see cref="BlendDirection"/>.</summary>
    public Vector3 SecondaryColor { get; init; }

    /// <summary>Gets the direction to look in to see only <see cref="Color"/>, or <see langword="null"/> to not blend colors.</summary>
    public Vector3? BlendDirection { get; init; }

    /// <summary>Gets the distance the view is clipped at, or zero or less to keep its far plane.</summary>
    public float FarZ { get; init; }

    /// <summary>Gets the color the fog fades toward while looking in a direction.</summary>
    public Vector3 GetColor(Vector3 viewForward)
    {
        if (BlendDirection is not { } direction)
        {
            return Color;
        }

        var blend = 0.5f * Vector3.Dot(viewForward, direction) + 0.5f;

        return Vector3.Lerp(SecondaryColor, Color, blend);
    }

    /// <summary>Gets how much of what is a distance away the fog covers.</summary>
    /// <param name="distance">The distance from the viewer, which may be infinite.</param>
    /// <returns>The amount, from zero to one.</returns>
    public float GetAmount(float distance)
    {
        float fraction;

        if (End == Start)
        {
            fraction = distance >= End ? 1f : 0f;
        }
        else if (float.IsPositiveInfinity(distance))
        {
            // Only a fog that thickens with distance covers what is infinitely far
            fraction = End > Start ? 1f : 0f;
        }
        else
        {
            fraction = MathUtils.Saturate((distance - Start) / (End - Start));
        }

        return MathUtils.Saturate(MathF.Min(MaxDensity, MathF.Pow(fraction, Exponent)));
    }
}

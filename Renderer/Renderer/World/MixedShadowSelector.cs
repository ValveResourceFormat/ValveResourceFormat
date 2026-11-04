using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.World;

/// <summary>
/// Picks the one stationary light whose real-time shadow is drawn over its baked shadows, as the VR light
/// binner does. A new pick waits for the shadow of the previous light to fade out, then fades in.
/// </summary>
public sealed class MixedShadowSelector
{
    // r_mixed_shadows_update_radius: the pick is only made again once the viewer moved this far
    private const float UpdateRadius = 48f;

    // r_mixed_shadows_fade_out_time and r_mixed_shadows_fade_in_time
    private const float FadeOutTime = 0.5f;
    private const float FadeInTime = 0.5f;

    private readonly List<(SceneLight Light, float ChangeTime)> fadingOut = [];
    private float changeTime = float.MinValue;
    private Vector3? lastPickPosition;
    private float time;

    /// <summary>Gets the shadow hints placed in the scene.</summary>
    public List<InfoDynamicShadowHint> Hints { get; } = [];

    /// <summary>Gets the light that currently has the mixed shadow, or <see langword="null"/> for none.</summary>
    public SceneLight? Current { get; private set; }

    /// <summary>Forgets the pick, so the next update picks again without fading.</summary>
    public void Reset()
    {
        Current = null;
        fadingOut.Clear();
        changeTime = float.MinValue;
        lastPickPosition = null;
    }

    /// <summary>Advances the fades, and picks the light again once the viewer has moved far enough.</summary>
    internal void Update(Vector3 viewPosition, float deltaTime, List<SceneLight> localLights, SceneLight? sun)
    {
        time += deltaTime;
        fadingOut.RemoveAll(fade => time - fade.ChangeTime >= FadeOutTime);

        if (lastPickPosition is { } last && Vector3.DistanceSquared(last, viewPosition) <= UpdateRadius * UpdateRadius)
        {
            return;
        }

        lastPickPosition = viewPosition;

        // Nothing new to pick keeps the current light
        var next = PickFromHints(viewPosition, localLights, sun) ?? PickNearest(viewPosition, localLights, sun);

        if (next == null || next == Current)
        {
            return;
        }

        fadingOut.RemoveAll(fade => fade.Light == next);

        // The first pick is shadowed at once, like a light that spawns with mixed shadows
        if (Current != null)
        {
            fadingOut.Add((Current, time));
            changeTime = time;
        }

        Current = next;
    }

    /// <summary>
    /// Gets how strongly the real-time shadow of a light darkens its baked strength, from 0 for no
    /// real-time shadow to 1 for a full one.
    /// </summary>
    public float GetStrength(SceneLight light)
    {
        if (light == Current)
        {
            var fadeInStart = changeTime + FadeOutTime;
            return Ease(Math.Clamp((time - fadeInStart) / FadeInTime, 0f, 1f));
        }

        foreach (var (fadingLight, fadeOutStart) in fadingOut)
        {
            if (fadingLight == light)
            {
                return Ease(1f - Math.Clamp((time - fadeOutStart) / FadeOutTime, 0f, 1f));
            }
        }

        return 0f;
    }

    /// <summary>
    /// Gets the strength of the one mixed shadow, of the light fading out until it is gone and then of the
    /// light fading in. The two fades never overlap.
    /// </summary>
    public float Strength
    {
        get
        {
            var strength = Current != null ? GetStrength(Current) : 0f;

            foreach (var (light, _) in fadingOut)
            {
                strength = MathF.Max(strength, GetStrength(light));
            }

            return strength;
        }
    }

    private static float Ease(float t) => (1f - MathF.Cos(t * MathF.PI)) * 0.5f;

    // The enabled hint around the viewer with the highest importance names the light, the nearest hint on a tie
    private SceneLight? PickFromHints(Vector3 viewPosition, List<SceneLight> localLights, SceneLight? sun)
    {
        InfoDynamicShadowHint? best = null;
        var bestDistance = float.MaxValue;

        foreach (var hint in Hints)
        {
            if (!hint.IsEnabled || hint.IsRemoved || hint.GetLight() is not { } light)
            {
                continue;
            }

            if ((light != sun && !localLights.Contains(light)) || !hint.Contains(viewPosition))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(hint.Position, viewPosition);

            if (best == null || hint.Importance > best.Importance || (hint.Importance == best.Importance && distance <= bestDistance))
            {
                best = hint;
                bestDistance = distance;
            }
        }

        return best?.GetLight();
    }

    // Without a hint, the nearest light authored with mixed shadows that reaches the viewer, otherwise the
    // sun. This stands in for how the binner scores each light by the shadows of the objects nearby.
    private static SceneLight? PickNearest(Vector3 viewPosition, List<SceneLight> localLights, SceneLight? sun)
    {
        SceneLight? best = null;
        var bestDistance = float.MaxValue;

        foreach (var light in localLights)
        {
            if (!light.MixedShadows)
            {
                continue;
            }

            var distance = Vector3.Distance(light.Position, viewPosition);

            if (distance < light.Range && distance < bestDistance && light.GetDistanceFade(viewPosition) > 0f)
            {
                best = light;
                bestDistance = distance;
            }
        }

        return best ?? sun;
    }
}

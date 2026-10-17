using System.Globalization;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The cone of a spot light while its entity is selected: the cone out to its range, and a dome over it
/// whose radius follows the brightness of the light from the inner to the outer angle.
/// </summary>
internal sealed class LightConeHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsVisual => false;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    private const float StepDegrees = 24f;
    private const float RingDegrees = 6f;
    private const byte Alpha = 180;

    /// <summary>Gets the key of the entity that holds the angle of the inner cone.</summary>
    private string InnerAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the angle of the outer cone.</summary>
    private string OuterAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the color of the light.</summary>
    private string ColorKey { get; }

    /// <summary>Gets what the pitch of the entity is multiplied by for the cone to point the way the light does.</summary>
    private float PitchScale { get; }

    public LightConeHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        InnerAngleKey = GetArgument(0)?.ToLowerInvariant() ?? "innerconeangle";
        OuterAngleKey = GetArgument(1)?.ToLowerInvariant() ?? "outerconeangle";
        ColorKey = GetArgument(2)?.ToLowerInvariant() ?? "color";
        PitchScale = GetArgument(3) is { } scale ? ParseFloat(scale) : 1f;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected)
        {
            return;
        }

        // A light that does not fade with distance draws nothing
        var constant = GetFloatKey("attenuation0");
        var linear = GetFloatKey("attenuation1");
        var quadratic = GetFloatKey("attenuation2", 1f);

        if (constant == 0f && linear == 0f && quadratic == 0f)
        {
            constant = 1f;
        }

        if (linear * 100f + quadratic * 10000f + constant == 0f)
        {
            return;
        }

        var range = GetFloatKey("range", -1f);
        var falloff = GetFloatKey("falloff", 1f);
        var innerAngle = GetFloatKey(InnerAngleKey);
        var outerAngle = GetFloatKey(OuterAngleKey, 45f);
        var color = Vector3.Normalize(GetVector3Key(ColorKey));

        if (!float.IsFinite(color.X))
        {
            color = Vector3.Zero;
        }

        var angles = GetVector3Key("angles");
        angles.X *= PitchScale;

        context.PushTransform(EntityTransformHelper.ToRigidTransformationMatrix(angles, Owner.RigidTransform.Translation));
        context.PushRenderMode(ToolRenderMode.Wireframe);

        if (outerAngle < 90f)
        {
            DrawCone(context, range, outerAngle, ToColor(color, 1f));
        }

        if (outerAngle <= 90f)
        {
            DrawDome(context, range, innerAngle, outerAngle, falloff, color);
        }

        context.PopRenderMode();
        context.PopTransform();
    }

    private static void DrawCone(ToolRenderContext context, float range, float outerAngle, Color32 color)
    {
        var baseRadius = MathF.Tan(float.DegreesToRadians(outerAngle)) * range;
        var previous = new Vector3(range, 0f, baseRadius);

        for (var angle = StepDegrees; angle <= 361f; angle += StepDegrees)
        {
            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
            var point = new Vector3(range, sin * baseRadius, cos * baseRadius);

            context.DrawLine(point, previous, color);
            context.DrawLine(previous, Vector3.Zero, color);
            context.DrawLine(Vector3.Zero, point, color);
            previous = point;
        }
    }

    // Rings every six degrees out to the outer angle, at the distance the light reaches at that angle
    private static void DrawDome(ToolRenderContext context, float range, float innerAngle, float outerAngle, float falloff, Vector3 color)
    {
        var cosInner = MathF.Cos(float.DegreesToRadians(innerAngle));
        var cosOuter = MathF.Cos(float.DegreesToRadians(outerAngle));
        var previousDistance = range;
        var previousRadius = 0f;
        var ring = RingDegrees;

        while (outerAngle + RingDegrees > ring)
        {
            var theta = MathF.Min(outerAngle, ring);
            var (sinTheta, cosTheta) = MathF.SinCos(float.DegreesToRadians(theta));
            var intensity = 1f;

            if (innerAngle < theta)
            {
                intensity = (cosTheta - cosOuter) / (cosInner - cosOuter);

                if (falloff is not 1f and not 0f)
                {
                    intensity = MathF.Pow(intensity, falloff);
                }
            }

            var distance = cosTheta * intensity * range;
            var radius = sinTheta * intensity * range;
            var ringColor = ToColor(color, intensity);
            var (previousSin, previousCos) = (0f, 1f);

            for (var angle = StepDegrees; angle <= 361f; angle += StepDegrees)
            {
                var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));

                ReadOnlySpan<Vector3> quad =
                [
                    new(distance, radius * previousSin, radius * previousCos),
                    new(distance, radius * sin, radius * cos),
                    new(previousDistance, previousRadius * sin, previousRadius * cos),
                    new(previousDistance, previousRadius * previousSin, previousRadius * previousCos),
                ];

                // At the tip the previous ring is a point
                var corners = previousRadius != 0f ? quad : quad[..3];

                for (var i = 0; i < corners.Length; i++)
                {
                    context.DrawLine(corners[i], corners[(i + 1) % corners.Length], ringColor);
                }

                (previousSin, previousCos) = (sin, cos);
            }

            previousDistance = distance;
            previousRadius = radius;
            ring = theta + RingDegrees;
        }
    }

    private static Color32 ToColor(Vector3 color, float intensity)
        => new((byte)(int)(color.X * 255f * intensity), (byte)(int)(color.Y * 255f * intensity), (byte)(int)(color.Z * 255f * intensity), Alpha);

    private static float ParseFloat(string value)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0f;
}

using System.Globalization;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The cone of a spot light while its entity is selected: the cone out to its range, and a dome over it
/// whose radius follows the brightness of the light from the inner to the outer angle.
/// </summary>
public sealed class LightConeHelper : EntityHelper
{
    private const float StepDegrees = 24f;
    private const float RingDegrees = 6f;
    private const byte Alpha = 180;

    /// <summary>Gets the key of the entity that holds the angle of the inner cone.</summary>
    public string InnerAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the angle of the outer cone.</summary>
    public string OuterAngleKey { get; }

    /// <summary>Gets the key of the entity that holds the color of the light.</summary>
    public string ColorKey { get; }

    /// <summary>Gets what the pitch of the entity is multiplied by for the cone to point the way the light does.</summary>
    public float PitchScale { get; }

    /// <summary>Initializes the cone of a light.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public LightConeHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        InnerAngleKey = GetArgument(0)?.ToLowerInvariant() ?? "innerconeangle";
        OuterAngleKey = GetArgument(1)?.ToLowerInvariant() ?? "outerconeangle";
        ColorKey = GetArgument(2)?.ToLowerInvariant() ?? "color";
        PitchScale = GetArgument(3) is { } scale ? ParseFloat(scale) : 1f;
    }

    internal override void AddSelectionShapes(in SelectionShapes shapes)
    {
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

        var lines = shapes.World(HelperPasses.Visible)
            .Transformed(EntityTransformHelper.ToRigidTransformationMatrix(angles, shapes.Placement.Translation));

        if (outerAngle < 90f)
        {
            AddCone(lines, range, outerAngle, ToColor(color, 1f));
        }

        if (outerAngle <= 90f)
        {
            AddDome(lines, range, innerAngle, outerAngle, falloff, color);
        }
    }

    private static void AddCone(EntityHelperLines lines, float range, float outerAngle, Color32 color)
    {
        var baseRadius = MathF.Tan(float.DegreesToRadians(outerAngle)) * range;
        var previous = new Vector3(range, 0f, baseRadius);

        for (var angle = StepDegrees; angle <= 361f; angle += StepDegrees)
        {
            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(angle));
            var point = new Vector3(range, sin * baseRadius, cos * baseRadius);

            lines.AddLine(point, previous, color);
            lines.AddLine(previous, Vector3.Zero, color);
            lines.AddLine(Vector3.Zero, point, color);
            previous = point;
        }
    }

    // Rings every six degrees out to the outer angle, at the distance the light reaches at that angle
    private static void AddDome(EntityHelperLines lines, float range, float innerAngle, float outerAngle, float falloff, Vector3 color)
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
                    lines.AddLine(corners[i], corners[(i + 1) % corners.Length], ringColor);
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

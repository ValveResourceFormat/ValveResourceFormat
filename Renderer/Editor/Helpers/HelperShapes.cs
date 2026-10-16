using ValveResourceFormat.Renderer.Editor.ToolRender;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>Shapes the light helpers draw with a <see cref="ToolRenderContext"/>, in the space it draws in, X forward.</summary>
internal static class HelperShapes
{
    /// <summary>
    /// A superellipse in the plane facing +X, spanning <paramref name="halfSize"/> along Y and Z. An exponent of
    /// one is an ellipse, and lower ones square it off down to a rectangle at zero.
    /// </summary>
    private static Vector3 SuperellipsePoint(Vector3 center, Vector2 halfSize, float exponent, float angle)
    {
        var (sin, cos) = MathF.SinCos(angle);

        return center + new Vector3(
            0f,
            MathF.CopySign(MathF.Pow(MathF.Abs(sin), exponent), sin) * halfSize.X,
            MathF.CopySign(MathF.Pow(MathF.Abs(cos), exponent), cos) * halfSize.Y);
    }

    /// <summary>Draws the outline of a superellipse in the draw color.</summary>
    public static void DrawSuperellipse(this ToolRenderContext context, Vector3 center, Vector2 halfSize, float exponent)
    {
        const int Segments = 48;

        var previous = SuperellipsePoint(center, halfSize, exponent, 0f);

        for (var i = 1; i <= Segments; i++)
        {
            var point = SuperellipsePoint(center, halfSize, exponent, i * MathF.Tau / Segments);

            // A rectangle puts many points on each corner
            if (point != previous)
            {
                context.DrawLine(previous, point);
            }

            previous = point;
        }
    }

    /// <summary>Draws lines in the draw color between two superellipses, at angles a step apart.</summary>
    public static void DrawSuperellipseConnectors(this ToolRenderContext context, Vector3 fromCenter, Vector2 fromHalfSize, Vector3 toCenter, Vector2 toHalfSize, float exponent, float startDegrees, float stepDegrees)
    {
        for (var angle = startDegrees; angle < 360f; angle += stepDegrees)
        {
            var radians = float.DegreesToRadians(angle);

            context.DrawLine(SuperellipsePoint(fromCenter, fromHalfSize, exponent, radians), SuperellipsePoint(toCenter, toHalfSize, exponent, radians));
        }
    }

    /// <summary>
    /// Draws curves in four planes through the forward axis, 45 degrees apart, in one color: a lobe of what a
    /// light emits, or the hemisphere it reaches with the circle at its base.
    /// </summary>
    public static void DrawEmissionCurves(this ToolRenderContext context, float radius, float startDegrees, bool lobe, Color32 color)
    {
        const int Points = 128;

        Span<Vector3> curve = stackalloc Vector3[Points];

        for (var plane = 0; plane < 4; plane++)
        {
            var rotation = Matrix4x4.CreateRotationX(float.DegreesToRadians(startDegrees + plane * 45f));

            for (var i = 0; i < Points; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.PI / (Points - 1));
                sin = MathF.Max(sin, 0f);

                var side = cos * radius * (lobe ? MathF.Cbrt(sin) : 1f);
                curve[i] = Vector3.Transform(new Vector3(sin * radius, 0f, side), rotation);
            }

            context.DrawColor = color;
            context.DrawPolyLine(curve, closed: false);

            if (!lobe)
            {
                context.DrawLine(Vector3.Transform(new Vector3(0f, 0f, radius), rotation), Vector3.Transform(new Vector3(0f, 0f, -radius), rotation), color);
            }
        }

        if (!lobe)
        {
            context.DrawColor = color;
            context.DrawCircle(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY, radius, Points);
        }
    }
}

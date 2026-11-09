using System.Globalization;
using System.Linq;
using System.Text;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.Material;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the <c>move_rope</c> and <c>keyframe_rope</c> chains of a map to paths. Each rope runs from its own origin to the
/// entity its <c>NextKey</c> names, settled into the hang it rests in, and becomes the nodes of a path that an effect draws
/// as a cable with the material of the rope.
/// </summary>
internal static class Source1RopeConverter
{
    private const string DefaultMaterial = "cable/cable.vmt";

    // The lengths of a rope are whole numbers, and every rope hangs this much shorter than its length and slack add up to
    private const int SlackFudge = -100;

    private const int DefaultNodeCount = 5;
    private const int MaxSubdivisions = 7;

    // The simulation of a rope at rest: 5 seconds of steps of a fiftieth of a second
    private const int SettleSteps = 250;
    private const float TimeStep = 1f / 50f;
    private const float Damping = 0.98f;
    private const int ConstraintIterations = 3;
    private static readonly Vector3 Gravity = new(0f, 0f, -1500f);

    // A rope gets a particle every spacing units, at least four to a segment, and the effect holds 1000 particles
    private const float MinParticleSpacing = 8f;
    private const float MaxSpacedParticles = 450f;

    private readonly record struct Rope(
        Vector3 Start,
        Vector3? End,
        bool LocksEnd,
        int NodeCount,
        int Length,
        int Slack,
        float Width,
        float TextureScale,
        int Subdivisions,
        string MaterialName);

    private readonly record struct EffectKey(string MaterialPath, float TextureRepeats);

    /// <summary>Gets whether the entity is a rope that <see cref="Convert"/> turns into a path.</summary>
    /// <param name="entity">The entity.</param>
    public static bool IsRope(BspEntity entity)
        => entity.ClassName is "move_rope" or "keyframe_rope";

    /// <summary>Converts the ropes of a map to paths and adds the effects that draw them to the converted map.</summary>
    /// <param name="map">The converted map, which gets the effects and the paths that were converted.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="getMaterialInfo">Reads the texture of a material, or returns null when it has none.</param>
    public static void Convert(Source1ConvertedMap map, BspFile bsp, Func<string, Source1MaterialInfo?> getMaterialInfo)
    {
        var named = new Dictionary<string, BspEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in bsp.Entities)
        {
            var name = entity.TargetName;

            if (name.Length > 0)
            {
                named.TryAdd(name, entity);
            }
        }

        var leaves = new BspLeafAmbient(bsp);
        var effects = new Dictionary<EffectKey, string>();

        foreach (var entity in bsp.Entities)
        {
            if (!IsRope(entity))
            {
                continue;
            }

            named.TryGetValue(entity.GetString("NextKey"), out var next);

            if (CreateRope(entity, next) is { } rope && CreatePath(map, effects, leaves, getMaterialInfo, rope) is { } path)
            {
                map.RopePaths.Add(entity, path);
            }
        }
    }

    private static Rope? CreateRope(BspEntity entity, BspEntity? next)
    {
        var locksEnd = entity.GetInt("Dangling") != 1;

        // A rope that has to end on another entity but has none is not drawn
        if (next == null && locksEnd)
        {
            return null;
        }

        var nodeCount = DefaultNodeCount;

        if (entity.Properties.ContainsKey("Type"))
        {
            nodeCount = entity.GetInt("Type") switch
            {
                0 => 10,
                1 => 4,
                _ => 2,
            };
        }

        var start = entity.Origin;
        var end = next?.Origin;
        var length = end is { } endPoint ? (int)(start - endPoint).Length() : 0;
        var subdivisions = Math.Clamp(entity.GetInt("Subdiv", 2), 0, MaxSubdivisions);

        return new Rope(
            start,
            end,
            locksEnd,
            nodeCount,
            length,
            entity.GetInt("Slack"),
            entity.GetFloat("Width", 2f),
            Math.Clamp(entity.GetFloat("TextureScale", 4f), 0.1f, 10f),
            subdivisions,
            GetMaterialName(entity));
    }

    private static string GetMaterialName(BspEntity entity)
    {
        var name = entity.GetString("RopeMaterial");

        if (name.Length > 0)
        {
            return name;
        }

        return entity.GetInt("RopeShader", -1) switch
        {
            1 => "cable/rope.vmt",
            2 => "cable/chain.vmt",
            _ => DefaultMaterial,
        };
    }

    private static Source1ConvertedMap.RopePath? CreatePath(
        Source1ConvertedMap map,
        Dictionary<EffectKey, string> effects,
        BspLeafAmbient leaves,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        Rope rope)
    {
        if (getMaterialInfo(rope.MaterialName) is not { } info)
        {
            return null;
        }

        var nodes = Settle(rope);
        var steps = GetSubdivisionSteps(rope);
        var points = Subdivide(nodes, steps, leaves, out var lights);

        var textureHeight = MathF.Max(info.TextureSize.Y, 1f);
        var pixelsPerUnit = 4f / rope.TextureScale;
        var totalTexCoord = pixelsPerUnit * (rope.Length + rope.Slack + SlackFudge);
        var pointCount = ((rope.NodeCount - 1) * steps.Length) + 1;
        var texCoordStep = totalTexCoord / pointCount / textureHeight;
        var textureRepeats = MathF.Round((points.Length - 1) * texCoordStep, 2);

        var materialPath = Source1Paths.AddVariant(Source1Paths.ToMaterialPath(rope.MaterialName), Source1ResourceProvider.CableVariant);
        var key = new EffectKey(materialPath, textureRepeats);

        if (!effects.TryGetValue(key, out var effectPath))
        {
            effectPath = string.Create(CultureInfo.InvariantCulture, $"{map.MapPath}/particles/rope_{effects.Count}.vpcf");
            Source1WorldConverter.AddFile(map, effectPath, Source1RopeEffect.Create(effectPath, materialPath, textureRepeats));
            effects.Add(key, effectPath);
        }

        var pathLength = 0f;

        for (var i = 1; i < points.Length; i++)
        {
            pathLength += Vector3.Distance(points[i - 1], points[i]);
        }

        var spacing = MathF.Max(MinParticleSpacing, pathLength / MaxSpacedParticles);

        return new Source1ConvertedMap.RopePath(
            effectPath,
            rope.Width * 0.5f,
            spacing,
            FormatNodes(points),
            FormatPins(points.Length),
            FormatColors(lights));
    }

    // The positions are relative to the first point. A node leans toward its neighbours by a third of the way to them,
    // which keeps the path on the straight lines between the points.
    private static string FormatNodes(Vector3[] points)
    {
        var text = new StringBuilder("[\n");
        var last = points.Length - 1;

        for (var i = 0; i <= last; i++)
        {
            var position = points[i] - points[0];
            var inTangent = i > 0 ? (points[i - 1] - points[i]) / 3f : Vector3.Zero;
            var outTangent = i < last ? (points[i + 1] - points[i]) / 3f : Vector3.Zero;

            text.Append(CultureInfo.InvariantCulture, $"\t[ {Source1EntityConverter.FormatVector(position)} {Source1EntityConverter.FormatVector(inTangent)} {Source1EntityConverter.FormatVector(outTangent)} ],\n");
        }

        return text.Append(']').ToString();
    }

    private static string FormatPins(int count)
        => $"[ {string.Join(", ", Enumerable.Repeat("true", count))} ]";

    private static string FormatColors(Vector3[] lights)
    {
        var text = new StringBuilder("[\n");

        foreach (var light in lights)
        {
            text.Append(CultureInfo.InvariantCulture, $"\t[ {Source1EntityConverter.FormatVector(light)} ],\n");
        }

        return text.Append(']').ToString();
    }

    // Lets the rope fall under gravity for the time a rope hangs before it is first drawn, held at the ends by
    // springs between the nodes that are as long as the rope and its slack
    private static Vector3[] Settle(Rope rope)
    {
        var nodeCount = rope.NodeCount;
        var springCount = nodeCount - 1;
        var nodes = new Vector3[nodeCount];
        var previousNodes = new Vector3[nodeCount];
        var end = rope.End ?? rope.Start;

        for (var i = 0; i < nodeCount; i++)
        {
            nodes[i] = Vector3.Lerp(rope.Start, end, (float)i / springCount);
            previousNodes[i] = nodes[i];
        }

        var springLength = MathF.Max((rope.Length + rope.Slack + SlackFudge) / springCount, 0f);
        var springLengthSquared = springLength * springLength;
        var acceleration = Gravity * (TimeStep * TimeStep * 0.5f);

        for (var step = 0; step < SettleSteps; step++)
        {
            for (var i = 0; i < nodeCount; i++)
            {
                var current = nodes[i];
                nodes[i] = current + ((current - previousNodes[i]) * Damping) + acceleration;
                previousNodes[i] = current;
            }

            for (var iteration = 0; iteration < ConstraintIterations; iteration++)
            {
                for (var i = 0; i < springCount; i++)
                {
                    var apart = nodes[i] - nodes[i + 1];
                    var distanceSquared = apart.LengthSquared();

                    if (distanceSquared <= springLengthSquared)
                    {
                        continue;
                    }

                    apart *= 1f - (springLength / MathF.Sqrt(distanceSquared));
                    nodes[i] -= apart * 0.5f;
                    nodes[i + 1] += apart * 0.5f;
                }

                nodes[0] = rope.Start;

                if (rope.LocksEnd)
                {
                    nodes[nodeCount - 1] = end;
                }
            }
        }

        return nodes;
    }

    // Runs a Catmull-Rom spline through the nodes, and gives each point the light of the nodes around it
    private static Vector3[] Subdivide(Vector3[] nodes, float[] steps, BspLeafAmbient leaves, out Vector3[] lights)
    {
        var nodeLights = new Vector3[nodes.Length];

        for (var i = 0; i < nodes.Length; i++)
        {
            nodeLights[i] = Vector3.Min(GetAverageAmbient(leaves, nodes[i]), Vector3.One);
        }

        var points = new List<Vector3>();
        var pointLights = new List<Vector3>();
        var last = nodes.Length - 1;
        var previous = 0;

        for (var i = 0; i < nodes.Length; i++)
        {
            points.Add(nodes[i]);
            pointLights.Add(nodeLights[i]);

            if (i == last)
            {
                break;
            }

            var next = i + 1;
            var afterNext = Math.Min(i + 2, last);

            for (var step = 0; step < steps.Length; step++)
            {
                var t = steps[step];

                points.Add(CatmullRom(nodes[previous], nodes[i], nodes[next], nodes[afterNext], t));
                pointLights.Add(Vector3.Lerp(nodeLights[i], nodeLights[next], (step + 1f) / (steps.Length + 1f)));
            }

            previous = i;
        }

        lights = [.. pointLights];

        return [.. points];
    }

    // A uniform Catmull-Rom spline between p1 (t = 0) and p2 (t = 1)
    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;

        return 0.5f * ((2f * p1)
            + ((-p0 + p2) * t)
            + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
            + ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
    }

    private static float[] GetSubdivisionSteps(Rope rope)
    {
        var steps = new float[rope.Subdivisions];

        for (var i = 0; i < steps.Length; i++)
        {
            steps[i] = (i + 1f) / (steps.Length + 1f);
        }

        return steps;
    }

    // The light that reaches a point from every side, averaged, or white when the leaf of the point has no ambient samples
    private static Vector3 GetAverageAmbient(BspLeafAmbient leaves, Vector3 point)
    {
        Span<Vector3> cube = stackalloc Vector3[BspLeafAmbient.FaceCount];

        if (!leaves.TryBlendAmbientCube(leaves.FindLeaf(point), point, cube))
        {
            return Vector3.One;
        }

        var sum = Vector3.Zero;

        foreach (var face in cube)
        {
            sum += face;
        }

        return sum / cube.Length;
    }
}

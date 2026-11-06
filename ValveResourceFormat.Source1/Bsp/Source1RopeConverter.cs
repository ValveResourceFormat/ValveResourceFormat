using System.Runtime.InteropServices;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Source1.Material;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Converts the <c>move_rope</c> and <c>keyframe_rope</c> chains of a map to ribbons, one model per material and map area.
/// Each rope runs from its own origin to the entity its <c>NextKey</c> names, settled into the hang it rests in, and the
/// cable shader turns the ribbon to face the camera.
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

    // Barbed wire follows the spline past both of its ends
    private static readonly float[] BarbedSubdivisions = [1.5f, -0.5f, 0.5f];

    [StructLayout(LayoutKind.Sequential)]
    private struct RopeVertex
    {
        public Vector3 Position;
        public Vector2 TexCoord;
        public Vector4 Color;
        public Vector4 Axis;
    }

    // The direction and half width of the ribbon read as the second texture coordinate
    private static readonly VertexField[] RopeFields =
    [
        new(nameof(RopeVertex.Position), "POSITION", 0, DXGI_FORMAT.R32G32B32_FLOAT),
        new(nameof(RopeVertex.TexCoord), "TEXCOORD", 0, DXGI_FORMAT.R32G32_FLOAT),
        new(nameof(RopeVertex.Color), "COLOR", 0, DXGI_FORMAT.R32G32B32A32_FLOAT),
        new(nameof(RopeVertex.Axis), "TEXCOORD", 1, DXGI_FORMAT.R32G32B32A32_FLOAT),
    ];

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
        bool IsBarbed,
        string MaterialName);

    /// <summary>Converts the ropes of a map and adds their models to the converted map.</summary>
    /// <param name="map">The converted map, which gets the models and the entities that were converted.</param>
    /// <param name="bsp">The map.</param>
    /// <param name="locator">Finds the 3D sky area.</param>
    /// <param name="getMaterialInfo">Reads the texture of a material, or returns null when it has none.</param>
    /// <param name="worldParts">Gets the paths of the models of the map.</param>
    /// <param name="skyParts">Gets the paths of the models in the 3D sky box area.</param>
    public static void Convert(
        Source1ConvertedMap map,
        BspFile bsp,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        List<string> worldParts,
        List<string> skyParts)
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
        var meshes = new Source1BakedMeshSet<RopeVertex>("rope", RopeFields);

        foreach (var entity in bsp.Entities)
        {
            if (entity.ClassName is not ("move_rope" or "keyframe_rope"))
            {
                continue;
            }

            named.TryGetValue(entity.GetString("NextKey"), out var next);

            if (CreateRope(entity, next) is { } rope && AddRope(meshes, leaves, locator, getMaterialInfo, rope))
            {
                map.DrawnEntities.Add(entity);
            }
        }

        meshes.AddModels(map, worldParts, skyParts);
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
            entity.GetInt("Barbed") == 1,
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

    private static bool AddRope(
        Source1BakedMeshSet<RopeVertex> meshes,
        BspLeafAmbient leaves,
        IBrushLightingLocator locator,
        Func<string, Source1MaterialInfo?> getMaterialInfo,
        Rope rope)
    {
        if (getMaterialInfo(rope.MaterialName) is not { } info)
        {
            return false;
        }

        var nodes = Settle(rope);
        var steps = GetSubdivisionSteps(rope);
        var points = Subdivide(nodes, steps, leaves, out var lights);

        var textureHeight = MathF.Max(info.TextureSize.Y, 1f);
        var pixelsPerUnit = 4f / rope.TextureScale;
        var totalTexCoord = pixelsPerUnit * (rope.Length + rope.Slack + SlackFudge);
        var pointCount = ((rope.NodeCount - 1) * steps.Length) + 1;
        var texCoordStep = totalTexCoord / pointCount / textureHeight;

        var materialPath = Source1Paths.AddVariant(Source1Paths.ToMaterialPath(rope.MaterialName), Source1ResourceProvider.CableVariant);
        var inSky = locator.IsInSky(rope.Start);
        var chunk = meshes.Get(inSky, rope.Start, materialPath);
        var halfWidth = rope.Width * 0.5f;
        var previous = -1;

        for (var i = 0; i < points.Length; i++)
        {
            var direction = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
            direction = MathUtils.SafeNormalize(direction);

            var v = i * texCoordStep;
            var color = new Vector4(lights[i], 1f);
            var first = chunk.AddVertex(CreateVertex(points[i], new Vector2(0f, v), color, direction, halfWidth), points[i], rope.Width);
            var second = chunk.AddVertex(CreateVertex(points[i], new Vector2(1f, v), color, direction, -halfWidth), points[i], rope.Width);

            if (previous >= 0)
            {
                chunk.AddQuad(previous, previous + 1, second, first);
            }

            previous = first;
        }

        return true;
    }

    private static RopeVertex CreateVertex(Vector3 position, Vector2 texCoord, Vector4 color, Vector3 direction, float halfWidth)
    {
        return new RopeVertex
        {
            Position = position,
            TexCoord = texCoord,
            Color = color,
            Axis = new Vector4(direction, halfWidth),
        };
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
        if (rope.IsBarbed)
        {
            return BarbedSubdivisions;
        }

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

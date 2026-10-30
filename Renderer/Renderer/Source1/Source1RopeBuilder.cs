using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Texture;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Turns the <c>move_rope</c> and <c>keyframe_rope</c> chains of a Source 1 map into scene nodes. Each rope runs from
    /// its own origin to the entity its <c>NextKey</c> names, settled into the hang it rests in, and is drawn as a ribbon
    /// that turns to face the camera.
    /// </summary>
    internal static class Source1RopeBuilder
    {
        private const string CableShaderName = "cable";
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
        private readonly struct RopeVertex
        {
            [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
            [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 TexCoord;
            [VertexAttribute(VertexSlot.Color)] public readonly Vector4 Color;
            [VertexAttribute("vCableAxis")] public readonly Vector4 Axis;

            public static readonly VertexInputLayout InputLayout = Source1VertexLayout.Create<RopeVertex>("vCableAxis");

            public RopeVertex(Vector3 position, Vector2 texCoord, Vector3 light, Vector3 direction, float halfWidth)
            {
                Position = position;
                TexCoord = texCoord;
                Color = new Vector4(light, 1f);
                Axis = new Vector4(direction, halfWidth);
            }
        }

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

        private sealed class Builder
        {
            private readonly Scene scene;
            private readonly Scene? skyScene;
            private readonly BspFile bsp;
            private readonly Func<string, RenderMaterial> resolveMaterial;
            private readonly Source1MapLighting? lighting;
            private readonly AABB? skyBounds;
            private readonly Dictionary<string, RenderMaterial?> materials = [];
            private readonly Dictionary<RenderMaterial, float> textureHeights = [];
            private readonly Source1MeshBatchSet<RopeVertex> batches = new();

            public Builder(Scene scene, Scene? skyScene, BspFile bsp, Func<string, RenderMaterial> resolveMaterial, Source1MapLighting? lighting, AABB? skyBounds)
            {
                this.scene = scene;
                this.skyScene = skyScene;
                this.bsp = bsp;
                this.resolveMaterial = resolveMaterial;
                this.lighting = lighting;
                this.skyBounds = skyBounds;
            }

            public void AddEntities()
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

                foreach (var entity in bsp.Entities)
                {
                    if (entity.ClassName is not ("move_rope" or "keyframe_rope"))
                    {
                        continue;
                    }

                    named.TryGetValue(entity.GetString("NextKey"), out var next);

                    if (CreateRope(entity, next) is { } rope)
                    {
                        AddRope(rope);
                    }
                }
            }

            public void AddBatches()
            {
                batches.AddTo($"{bsp.Name}/ropes", RopeVertex.InputLayout);
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

            private void AddRope(Rope rope)
            {
                var target = Source1MapLighting.SelectScene(skyBounds, scene, skyScene, rope.Start);

                if (target == null || GetMaterial(rope.MaterialName) is not { } material)
                {
                    return;
                }

                var nodes = Settle(rope);
                var points = Subdivide(nodes, rope, out var lights);

                var textureHeight = GetTextureHeight(material);
                var pixelsPerUnit = 4f / rope.TextureScale;
                var totalTexCoord = pixelsPerUnit * (rope.Length + rope.Slack + SlackFudge);
                var pointCount = ((rope.NodeCount - 1) * rope.Subdivisions) + 1;
                var texCoordStep = totalTexCoord / pointCount / textureHeight;

                var batch = batches.Get(target, material, rope.Start);
                var halfWidth = rope.Width * 0.5f;
                var previous = -1;

                for (var i = 0; i < points.Length; i++)
                {
                    var direction = points[Math.Min(i + 1, points.Length - 1)] - points[Math.Max(i - 1, 0)];
                    direction = MathUtils.SafeNormalize(direction);

                    var v = i * texCoordStep;
                    var first = batch.AddVertex(new RopeVertex(points[i], new Vector2(0f, v), lights[i], direction, halfWidth), points[i], rope.Width);
                    var second = batch.AddVertex(new RopeVertex(points[i], new Vector2(1f, v), lights[i], direction, -halfWidth), points[i], rope.Width);

                    if (previous >= 0)
                    {
                        batch.AddQuad(previous, previous + 1, second, first);
                    }

                    previous = first;
                }
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
            private Vector3[] Subdivide(Vector3[] nodes, Rope rope, out Vector3[] lights)
            {
                var steps = GetSubdivisionSteps(rope);
                var nodeLights = new Vector3[nodes.Length];

                for (var i = 0; i < nodes.Length; i++)
                {
                    nodeLights[i] = Vector3.Min(lighting?.GetAverageAmbient(nodes[i]) ?? Vector3.One, Vector3.One);
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

                        points.Add(CableMeshBuilder.CatmullRom(nodes[previous], nodes[i], nodes[next], nodes[afterNext], t));
                        pointLights.Add(Vector3.Lerp(nodeLights[i], nodeLights[next], (step + 1f) / (steps.Length + 1f)));
                    }

                    previous = i;
                }

                lights = pointLights.ToArray();

                return points.ToArray();
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

            // The material of a rope is always drawn by the cable shader, as the ribbons need its vertex format
            private RenderMaterial? GetMaterial(string materialName)
            {
                var path = Source1Paths.ToMaterialPath(materialName);

                if (materials.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                var source = resolveMaterial(materialName);
                RenderMaterial? material = null;

                if (source.Material.TextureParams.ContainsKey("g_tColor"))
                {
                    material = Source1MeshBatch.CreateMaterial(scene.RendererContext.MaterialLoader, source, CableShaderName);
                }

                materials.Add(path, material);

                return material;
            }

            // The v coordinate of the cable texture advances in texels, so it needs the height of the texture
            private float GetTextureHeight(RenderMaterial material)
            {
                if (textureHeights.TryGetValue(material, out var cached))
                {
                    return cached;
                }

                var height = ReadTextureHeight(material);
                textureHeights.Add(material, height);

                return height;
            }

            private float ReadTextureHeight(RenderMaterial material)
            {
                var fileSystem = scene.RendererContext.Source1FileSystem;

                if (fileSystem == null || !material.Material.TextureParams.TryGetValue("g_tColor", out var texturePath))
                {
                    return 1f;
                }

                try
                {
                    using var stream = fileSystem.OpenFile(texturePath);

                    return stream == null ? 1f : MathF.Max(VtfFile.ReadHeader(stream).Height, 1f);
                }
                catch (Exception e) when (e is InvalidDataException or IOException)
                {
                    return 1f;
                }
            }
        }

        /// <summary>Adds the ropes of a map to the scene.</summary>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="skyScene">The scene of the 3D sky box, or null to leave out the ropes of that area.</param>
        /// <param name="bsp">The map.</param>
        /// <param name="resolveMaterial">Loads the material of a material name such as <c>cable/cable.vmt</c>.</param>
        /// <param name="lighting">Lights the ropes from the ambient light of the map, or null for ropes that are fully lit.</param>
        /// <param name="skyBounds">The area of the 3D sky box, or null.</param>
        public static void Build(Scene scene, Scene? skyScene, BspFile bsp, Func<string, RenderMaterial> resolveMaterial, Source1MapLighting? lighting, AABB? skyBounds)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(bsp);
            ArgumentNullException.ThrowIfNull(resolveMaterial);

            var builder = new Builder(scene, skyScene, bsp, resolveMaterial, lighting, skyBounds);
            builder.AddEntities();
            builder.AddBatches();
        }
    }
}

using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Source1.Bsp;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Turns the detail props of a Source 1 map into scene nodes: sprites batched into meshes of quads per material and
    /// map area, and detail models as model nodes.
    /// </summary>
    internal static class Source1DetailPropBuilder
    {
        private const string SpriteShaderName = "detailsprite";
        private const int VerticesPerQuad = 4;

        // The distances of the cl_detaildist and cl_detailfade settings of the engine
        private const float DefaultDetailDistance = 1200f;
        private const float DefaultFadeWidth = 400f;

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct SpriteVertex
        {
            [VertexAttribute(VertexSlot.Position)] public readonly Vector3 Position;
            [VertexAttribute(VertexSlot.TexCoord)] public readonly Vector2 TexCoord;
            [VertexAttribute(VertexSlot.Color)] public readonly Vector4 Color;
            [VertexAttribute("vSpriteBillboard")] public readonly Vector3 Billboard;

            public static readonly VertexInputLayout InputLayout = Source1VertexLayout.Create<SpriteVertex>("vSpriteBillboard");

            public SpriteVertex(BspDetailVertex vertex)
            {
                var isBillboard = vertex.Orientation != BspDetailPropOrientation.Normal;

                // A billboard is placed from the origin of its prop, the other sprites are already in the world
                Position = isBillboard ? vertex.PropOrigin : vertex.Position;
                TexCoord = vertex.TexCoord;
                Color = new Vector4(vertex.Color, 1f);
                Billboard = new Vector3(isBillboard ? vertex.LocalOffset : Vector2.Zero, (float)vertex.Orientation);
            }
        }

        /// <summary>Adds the detail props of a map to the scene.</summary>
        /// <param name="scene">The scene to add the nodes to.</param>
        /// <param name="bsp">The map.</param>
        /// <param name="resolveMaterial">Loads the material of a material name such as <c>detail/detailsprites</c>.</param>
        /// <param name="createModel">Creates the node of a detail model from its model path and transform, or returns null to skip it.</param>
        /// <param name="skyBounds">The area of the 3D sky box, where no props are drawn, or null.</param>
        public static void Build(
            Scene scene,
            BspFile bsp,
            Func<string, RenderMaterial> resolveMaterial,
            Func<string, Matrix4x4, SceneNode?> createModel,
            AABB? skyBounds)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(bsp);
            ArgumentNullException.ThrowIfNull(resolveMaterial);
            ArgumentNullException.ThrowIfNull(createModel);

            var detailProps = bsp.DetailProps;

            if (detailProps.Props.Count == 0)
            {
                return;
            }

            AddSprites(scene, bsp, detailProps, resolveMaterial, skyBounds);
            AddModels(scene, detailProps, createModel, skyBounds);
        }

        private static void AddSprites(Scene scene, BspFile bsp, BspDetailProps detailProps, Func<string, RenderMaterial> resolveMaterial, AABB? skyBounds)
        {
            var spriteMesh = detailProps.BuildSpriteMesh();

            if (spriteMesh.Indices.Length == 0)
            {
                return;
            }

            var worldspawn = bsp.Entities.FirstOrDefault(static entity => entity.ClassName == "worldspawn");
            var materialName = worldspawn?.GetString("detailmaterial", BspDetailProps.SpriteMaterial) ?? BspDetailProps.SpriteMaterial;

            if (materialName.Length == 0)
            {
                materialName = BspDetailProps.SpriteMaterial;
            }

            var material = CreateSpriteMaterial(scene, bsp, resolveMaterial(materialName));
            var chunks = new Dictionary<(int X, int Y), Source1MeshBatch<SpriteVertex>>();
            var vertices = spriteMesh.Vertices;

            for (var quad = 0; quad < vertices.Length / VerticesPerQuad; quad++)
            {
                var firstVertex = quad * VerticesPerQuad;
                var origin = vertices[firstVertex].PropOrigin;

                if (Source1MapLighting.IsInSky(skyBounds, origin))
                {
                    continue;
                }

                var chunk = Source1MeshBatch.GetChunk(origin);

                if (!chunks.TryGetValue(chunk, out var batch))
                {
                    batch = new Source1MeshBatch<SpriteVertex>();
                    chunks.Add(chunk, batch);
                }

                AddQuad(batch, vertices.AsSpan(firstVertex, VerticesPerQuad));
            }

            foreach (var ((x, y), batch) in chunks)
            {
                batch.AddTo(scene, $"{bsp.Name}/detail/{x}_{y}", material, SpriteVertex.InputLayout);
            }
        }

        // Sprites fade out over the distance of the map's detail controller, or the defaults of the engine
        private static RenderMaterial CreateSpriteMaterial(Scene scene, BspFile bsp, RenderMaterial source)
        {
            var material = scene.RendererContext.MaterialLoader.CreateSource1MaterialWithShader(source, SpriteShaderName);

            var controller = bsp.Entities.FirstOrDefault(static entity => entity.ClassName == "env_detail_controller");
            var detailDistance = DefaultDetailDistance;
            var fadeWidth = DefaultFadeWidth;

            if (controller != null)
            {
                detailDistance = MathF.Min(detailDistance, controller.GetFloat("fademaxdist", DefaultDetailDistance));
                fadeWidth = MathF.Min(fadeWidth, controller.GetFloat("fademindist", DefaultFadeWidth));
            }

            var fadeStart = MathF.Max(detailDistance - fadeWidth, 0f);
            var endSquared = detailDistance * detailDistance;

            material.FloatParams["g_flDetailFadeStartSq"] = MathF.Min(fadeStart * fadeStart, endSquared - 1f);
            material.FloatParams["g_flDetailFadeEndSq"] = endSquared;

            return material;
        }

        private static void AddQuad(Source1MeshBatch<SpriteVertex> batch, ReadOnlySpan<BspDetailVertex> corners)
        {
            Span<int> indices = stackalloc int[VerticesPerQuad];

            for (var i = 0; i < corners.Length; i++)
            {
                var vertex = new SpriteVertex(corners[i]);
                indices[i] = batch.AddVertex(vertex, vertex.Position, corners[i].LocalOffset.Length());
            }

            batch.AddQuad(indices[0], indices[1], indices[2], indices[3]);
        }

        private static void AddModels(Scene scene, BspDetailProps detailProps, Func<string, Matrix4x4, SceneNode?> createModel, AABB? skyBounds)
        {
            foreach (var prop in detailProps.Props)
            {
                if (prop.Type != BspDetailPropType.Model || prop.DetailModel >= detailProps.ModelDictionary.Count)
                {
                    continue;
                }

                if (Source1MapLighting.IsInSky(skyBounds, prop.Origin))
                {
                    continue;
                }

                var transform = EntityTransformHelper.ToRigidTransformationMatrix(prop.Angles, prop.Origin);
                var node = createModel(detailProps.ModelDictionary[prop.DetailModel], transform);

                if (node == null)
                {
                    continue;
                }

                scene.Add(node, false);
            }
        }
    }
}

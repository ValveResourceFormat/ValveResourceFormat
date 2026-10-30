using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Texture;

namespace ValveResourceFormat.Renderer.Source1
{
    /// <summary>
    /// Finds what lights a point of a Source 1 map besides its lightmap: the ambient cube of the nearest leaf sample
    /// and the nearest <c>env_cubemap</c>, and creates the material instances that carry them.
    /// </summary>
    internal sealed class Source1MapLighting
    {
        private const string EnvMapSamplerName = "g_tEnvMap";
        private const string AmbientCubeUniformName = "g_vAmbientCube";

        private const float AmbientCellSize = 16f;

        // Positions in the same cell of a leaf share one instance
        private readonly record struct AmbientCell(int X, int Y, int Z)
        {
            public AmbientCell(Vector3 position)
                : this((int)MathF.Round(position.X / AmbientCellSize), (int)MathF.Round(position.Y / AmbientCellSize), (int)MathF.Round(position.Z / AmbientCellSize))
            {
            }
        }

        private readonly record struct InstanceKey(RenderMaterial Material, bool Skinned, bool Baked, int Cubemap, int AmbientLeaf, AmbientCell AmbientCell);

        private readonly record struct Node(int Plane, int Child0, int Child1);

        private readonly Scene scene;
        private readonly BspFile bsp;
        private readonly Source1FileSystem? fileSystem;
        private readonly Node[] nodes;
        private readonly LeafAmbientIndex[] ambientIndices;
        private readonly LeafAmbientLighting[] ambientSamples;
        private readonly RenderTexture?[] cubemapTextures;
        private readonly Dictionary<InstanceKey, RenderMaterial> instances = [];

        /// <summary>Creates the lookups of a map.</summary>
        public Source1MapLighting(Scene scene, BspFile bsp, Source1FileSystem? fileSystem)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(bsp);

            this.scene = scene;
            this.bsp = bsp;
            this.fileSystem = fileSystem;

            nodes = ReadNodes(bsp);
            ambientIndices = bsp.HasHdrLighting && bsp.LeafAmbientIndicesHdr.Length > 0 ? bsp.LeafAmbientIndicesHdr : bsp.LeafAmbientIndices;
            ambientSamples = bsp.HasHdrLighting && bsp.LeafAmbientLightingHdr.Length > 0 ? bsp.LeafAmbientLightingHdr : bsp.LeafAmbientLighting;
            cubemapTextures = new RenderTexture?[bsp.Cubemaps.Length];
            SkyBounds = FindSkyBounds();
        }

        /// <summary>
        /// Gets the bounds of the area of the <c>sky_camera</c>, the 3D sky box that sits inside the map away from the playable space,
        /// or <see langword="null"/> when the map has none or the playable space is in the same area.
        /// </summary>
        public AABB? SkyBounds { get; }

        /// <summary>Gets where the 3D sky is seen from, set when <see cref="SkyBounds"/> is.</summary>
        public SkyTransform? Sky { get; private set; }

        /// <summary>Gets whether a point at world coordinates is in the area of the 3D sky box.</summary>
        /// <param name="skyBounds">The area of the 3D sky box, or null.</param>
        /// <param name="position">The point.</param>
        public static bool IsInSky(AABB? skyBounds, Vector3 position)
            => skyBounds is { } sky && sky.Contains(position);

        /// <summary>Gets the scene a point at world coordinates is drawn in, or null for a point of the 3D sky area without a sky scene.</summary>
        /// <param name="skyBounds">The area of the 3D sky box, or null.</param>
        /// <param name="scene">The scene of the map.</param>
        /// <param name="skyScene">The scene of the 3D sky box, or null.</param>
        /// <param name="position">The point.</param>
        public static Scene? SelectScene(AABB? skyBounds, Scene scene, Scene? skyScene, Vector3 position)
            => IsInSky(skyBounds, position) ? skyScene : scene;

        private AABB? FindSkyBounds()
        {
            BspEntity? skyCamera = null;
            BspEntity? spawn = null;

            foreach (var entity in bsp.Entities)
            {
                if (entity.ClassName == "sky_camera")
                {
                    skyCamera ??= entity;
                }
                else if (entity.ClassName.StartsWith("info_player", StringComparison.Ordinal))
                {
                    spawn ??= entity;
                }
            }

            if (skyCamera == null)
            {
                return null;
            }

            var area = FindArea(skyCamera.Origin);

            if (area <= 0 || (spawn != null && FindArea(spawn.Origin) == area))
            {
                return null;
            }

            Sky = new SkyTransform(Vector3.Zero, skyCamera.Origin, MathF.Max(skyCamera.GetFloat("scale", 16f), 1f));

            return GetAreaBounds(area);
        }

        private static Node[] ReadNodes(BspFile bsp)
        {
            const int NodeSize = 32;

            var data = bsp.GetLumpData(LumpType.Nodes).Span;
            var result = new Node[data.Length / NodeSize];

            for (var i = 0; i < result.Length; i++)
            {
                var entry = data.Slice(i * NodeSize, NodeSize);

                result[i] = new Node(
                    BinaryPrimitives.ReadInt32LittleEndian(entry),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                    BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            }

            return result;
        }

        /// <summary>Gets the leaf of the world model that contains a point.</summary>
        /// <returns>The leaf index, or -1 for a map without nodes.</returns>
        public int FindLeaf(Vector3 point)
        {
            if (nodes.Length == 0 || bsp.Models.Length == 0)
            {
                return -1;
            }

            var node = bsp.Models[0].HeadNode;

            while (node >= 0)
            {
                var current = nodes[node];
                var plane = bsp.Planes[current.Plane];
                var distance = Vector3.Dot(plane.Normal, point) - plane.Dist;

                node = distance >= 0f ? current.Child0 : current.Child1;
            }

            return -1 - node;
        }

        /// <summary>Gets the area of the leaf that contains a point.</summary>
        public int FindArea(Vector3 point)
        {
            var leaf = FindLeaf(point);

            return leaf >= 0 && leaf < bsp.Leafs.Length ? bsp.Leafs[leaf].Area : -1;
        }

        /// <summary>Gets the bounds of the leaves of an area.</summary>
        public AABB? GetAreaBounds(int area)
        {
            var found = false;
            var bounds = new AABB();

            foreach (var leaf in bsp.Leafs)
            {
                if (leaf.Area != area || leaf.Contents == 1)
                {
                    continue;
                }

                var leafBounds = new AABB(leaf.Mins, leaf.Maxs);
                bounds = found ? bounds.Union(leafBounds) : leafBounds;
                found = true;
            }

            return found ? bounds : null;
        }

        /// <summary>Gets the light that reaches a point from every side, averaged, for things without a surface to light such as ropes.</summary>
        /// <param name="point">The point in the world.</param>
        /// <returns>The linear color, or white when the leaf of the point has no ambient samples.</returns>
        public Vector3 GetAverageAmbient(Vector3 point)
        {
            if (BlendAmbientCube(FindLeaf(point), point) is not { } cube)
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

        // Blends the samples of the leaf by inverse squared distance. A leaf without samples of its own
        // can name another leaf to take them from.
        private Vector3[]? BlendAmbientCube(int leafIndex, Vector3 point)
        {
            if ((uint)leafIndex >= (uint)ambientIndices.Length || leafIndex >= bsp.Leafs.Length)
            {
                return null;
            }

            var index = ambientIndices[leafIndex];

            if (index.AmbientSampleCount == 0 && index.FirstAmbientSample != 0)
            {
                leafIndex = index.FirstAmbientSample;

                if ((uint)leafIndex >= (uint)ambientIndices.Length || leafIndex >= bsp.Leafs.Length)
                {
                    return null;
                }

                index = ambientIndices[leafIndex];
            }

            if (index.AmbientSampleCount == 0)
            {
                return null;
            }

            var leaf = bsp.Leafs[leafIndex];
            var cube = new Vector3[6];
            var totalWeight = 0f;

            for (var i = 0; i < index.AmbientSampleCount; i++)
            {
                var sampleIndex = index.FirstAmbientSample + i;

                if (sampleIndex >= ambientSamples.Length)
                {
                    break;
                }

                var sample = ambientSamples[sampleIndex];
                var fraction = new Vector3(sample.X, sample.Y, sample.Z) / 255f;
                var position = leaf.Mins + (fraction * (leaf.Maxs - leaf.Mins));
                var weight = 1f / (Vector3.DistanceSquared(position, point) + 1f);

                totalWeight += weight;

                for (var face = 0; face < 6; face++)
                {
                    cube[face] += sample.Cube[face].ToAmbientLinear() * weight;
                }
            }

            if (totalWeight == 0f)
            {
                return null;
            }

            for (var face = 0; face < 6; face++)
            {
                cube[face] /= totalWeight;
            }

            return cube;
        }

        /// <summary>Gets the index of the <c>env_cubemap</c> nearest to a point, or -1 when the map has none.</summary>
        /// <param name="point">The point, such as the center of a face.</param>
        /// <param name="normal">
        /// The normal of the surface at the point. Cube maps in front of the surface are preferred,
        /// and the nearest of all is used when there are none.
        /// </param>
        public int FindCubemap(Vector3 point, Vector3? normal = null)
        {
            var best = -1;
            var bestDistance = float.MaxValue;

            for (var i = 0; i < bsp.Cubemaps.Length; i++)
            {
                var delta = bsp.Cubemaps[i].Origin - point;
                var distance = delta.Length();

                if (normal is { } surfaceNormal && distance > 0f && Vector3.Dot(delta / distance, surfaceNormal) < 0f)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            if (best < 0 && normal != null)
            {
                return FindCubemap(point);
            }

            return best;
        }

        /// <summary>Gets the position of an <c>env_cubemap</c>.</summary>
        public Vector3 GetCubemapOrigin(int index) => bsp.Cubemaps[index].Origin;

        private RenderTexture? GetCubemapTexture(int index)
        {
            if (index < 0 || fileSystem == null)
            {
                return null;
            }

            if (cubemapTextures[index] is { } cached)
            {
                return cached;
            }

            var sample = bsp.Cubemaps[index];
            var name = string.Create(CultureInfo.InvariantCulture, $"materials/maps/{bsp.Name}/c{sample.OriginX}_{sample.OriginY}_{sample.OriginZ}");
            var texture = bsp.HasHdrLighting
                ? LoadCubemap(name + ".hdr.vtf") ?? LoadCubemap(name + ".vtf")
                : LoadCubemap(name + ".vtf");

            cubemapTextures[index] = texture;

            return texture;
        }

        private RenderTexture? LoadCubemap(string path)
        {
            using var stream = fileSystem!.OpenFile(path);

            if (stream == null)
            {
                return null;
            }

            try
            {
                var vtf = new VtfFile(stream);

                if (vtf.FaceCount < 6)
                {
                    return null;
                }

                return MaterialLoader.LoadVtfTexture(vtf, Path.GetFileName(path), srgbRead: true);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
            {
                scene.RendererContext.Logger.LogWarning("Failed to load cube map {Path}: {Message}", path, e.Message);
                return null;
            }
        }

        /// <summary>
        /// Gets whether a material reflects the nearest <c>env_cubemap</c>, which is when its <c>$envmap</c> is <c>env_cubemap</c>.
        /// A material that names a texture of its own keeps it.
        /// </summary>
        public static bool UsesEnvMap(RenderMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);

            return material.IntParams.GetValueOrDefault("F_ENVMAP") != 0
                && material.Shader.UniformNames.Contains(EnvMapSamplerName)
                && !material.Material.TextureParams.ContainsKey(EnvMapSamplerName);
        }

        private static bool UsesAmbientCube(RenderMaterial material)
            => material.Shader.UniformNames.Contains(AmbientCubeUniformName + "0");

        /// <summary>
        /// Gets the material that shows a model or a piece of the world at a position: the material itself when it needs
        /// nothing of the position, otherwise an instance with the nearest cube map and the ambient cube of the nearest leaf sample.
        /// </summary>
        /// <param name="material">The material as the loader created it.</param>
        /// <param name="shaderArguments">The shader combos the material was loaded with.</param>
        /// <param name="position">Where the material is seen.</param>
        /// <param name="useAmbientCube">Whether the material is lit by the ambient cube, which only a model material is.</param>
        public RenderMaterial GetInstance(RenderMaterial material, Dictionary<string, byte>? shaderArguments, Vector3 position, bool useAmbientCube)
        {
            var baked = shaderArguments?.ContainsKey(Source1ModelSceneNode.BakedLightingCombo) == true;
            var skinned = shaderArguments?.ContainsKey("D_SKINNING") == true;
            var envMap = UsesEnvMap(material);
            var ambient = useAmbientCube && !baked && UsesAmbientCube(material);

            if (!envMap && !ambient)
            {
                return material;
            }

            var cubemap = envMap ? FindCubemap(position) : -1;
            var ambientLeaf = ambient ? FindLeaf(position) : -1;
            var ambientCell = ambient ? new AmbientCell(position) : default;
            var key = new InstanceKey(material, skinned, baked, cubemap, ambientLeaf, ambientCell);

            if (instances.TryGetValue(key, out var instance))
            {
                return instance;
            }

            instance = scene.RendererContext.MaterialLoader.CreateSource1MaterialInstance(material, shaderArguments);

            if (GetCubemapTexture(cubemap) is { } texture)
            {
                instance.Textures[EnvMapSamplerName] = texture;
            }

            if (ambient && BlendAmbientCube(ambientLeaf, position) is { } cube)
            {
                SetAmbientCube(instance, cube);
            }

            instances[key] = instance;

            return instance;
        }

        private static void SetAmbientCube(RenderMaterial material, Vector3[] cube)
        {
            material.IntParams["F_AMBIENT_CUBE"] = 1;

            for (var face = 0; face < 6; face++)
            {
                material.VectorParams[AmbientCubeUniformName + face.ToString(CultureInfo.InvariantCulture)] = new Vector4(cube[face], 0f);
            }
        }
    }
}

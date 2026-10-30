namespace ValveResourceFormat.Source1.Bsp
{
    /// <summary>A vertex of a BSP mesh. Normals, tangents and positions are in Source world units.</summary>
    internal struct BspVertex
    {
        public Vector3 Position;
        public Vector3 Normal;

        /// <summary>Texture UV in texture space (texels divided by texture size). Overlay meshes carry the overlay UV.</summary>
        public Vector2 TexCoord;

        /// <summary>UV into every page of <see cref="LightmapAtlas"/> (luxel centre correct).</summary>
        public Vector2 LightmapCoord;

        public float DisplacementAlpha;

        /// <summary>
        /// Tangent space: xyz is the unit direction of increasing texture U, made perpendicular to <see cref="Normal"/>.
        /// The V direction (bitangent) is <c>cross(Normal, Tangent.xyz) * Tangent.w</c>.
        /// </summary>
        public Vector4 Tangent;
    }

    /// <summary>Properties of a <see cref="BspMaterialMesh"/> that a renderer may act on.</summary>
    [Flags]
    internal enum BspMeshFlags
    {
        None = 0,

        /// <summary>The geometry is the sky box faces.</summary>
        Sky = 1,

        /// <summary>The material has the warp surface flag or its name contains "water".</summary>
        Water = 2,

        /// <summary>The geometry is an overlay lying on top of other faces and needs a depth bias.</summary>
        Overlay = 4,

        /// <summary>At least one face is a displacement.</summary>
        Displacement = 8,

        /// <summary>At least one face stores bump basis lightmaps.</summary>
        Bumped = 16,
    }

    /// <summary>The geometry of one material of one brush model, or of one overlay.</summary>
    internal sealed class BspMaterialMesh
    {
        public string Material { get; }
        public BspVertex[] Vertices { get; private set; } = [];
        public int[] Indices { get; private set; } = [];

        public BspMeshFlags Flags { get; internal set; }

        /// <summary>For overlays, the render order from 0 to 3. Higher orders draw on top.</summary>
        public int RenderOrder { get; internal set; }

        /// <summary>For overlays, the index into <see cref="BspFile.Overlays"/>, otherwise -1.</summary>
        public int OverlayIndex { get; internal set; } = -1;

        /// <summary>For overlays, the squared distance where the fade starts, or a negative value for no fade.</summary>
        public float FadeDistMinSq { get; internal set; } = -1f;

        /// <summary>For overlays, the squared distance where the overlay is gone.</summary>
        public float FadeDistMaxSq { get; internal set; }

        public bool IsOverlay => (Flags & BspMeshFlags.Overlay) != 0;

        internal List<BspVertex> VertexList { get; } = [];
        internal List<int> IndexList { get; } = [];

        internal BspMaterialMesh(string material)
        {
            Material = material;
        }

        internal void Finish()
        {
            Vertices = VertexList.ToArray();
            Indices = IndexList.ToArray();
        }
    }

    internal sealed class BspModelMesh
    {
        public int ModelIndex { get; }
        public Dictionary<string, BspMaterialMesh> Materials { get; } = new(StringComparer.OrdinalIgnoreCase);
        public BspMaterialMesh? Sky { get; internal set; }

        /// <summary>One mesh per overlay that lands on a face of this model, in overlay order.</summary>
        public List<BspMaterialMesh> Overlays { get; } = [];

        internal BspModelMesh(int modelIndex)
        {
            ModelIndex = modelIndex;
        }
    }

    internal static class BspMeshBuilder
    {
        private const SurfFlags SkippedFlags = SurfFlags.NoDraw | SurfFlags.Hint | SurfFlags.Skip | SurfFlags.Trigger;
        private const SurfFlags SkyFlags = SurfFlags.Sky | SurfFlags.Sky2D;

        internal readonly record struct BuildContext(BspFile Bsp, BspFace[] Faces, LightmapLayout Layout);

        /// <summary>
        /// Builds the meshes of every brush model, using the HDR lightmap lump and faces when the map has them.
        /// </summary>
        /// <param name="bsp">The map.</param>
        /// <param name="getDecalSize">Gets the world size of an <c>infodecal</c> texture, or null when the material is unknown. Decals are left out when this is null.</param>
        public static BspModelMesh[] BuildAll(BspFile bsp, Func<string, Vector2?>? getDecalSize = null)
        {
            var result = new BspModelMesh[bsp.Models.Length];

            for (var i = 0; i < result.Length; i++)
            {
                result[i] = Build(bsp, i, getDecalSize);
            }

            return result;
        }

        private static BspModelMesh Build(BspFile bsp, int modelIndex, Func<string, Vector2?>? getDecalSize)
        {
            var model = bsp.Models[modelIndex];
            var mesh = new BspModelMesh(modelIndex);
            var faces = bsp.GetLightmappedFaces(preferHdr: true);
            var context = new BuildContext(bsp, faces, bsp.GetLightmapLayout(preferHdr: true));
            var firstFace = Math.Max(model.FirstFace, 0);
            var end = Math.Min(model.FirstFace + model.NumFaces, faces.Length);

            for (var faceIndex = firstFace; faceIndex < end; faceIndex++)
            {
                AddFace(context, faceIndex, mesh);
            }

            BspOverlayBuilder.AddOverlays(context, firstFace, end, mesh, getDecalSize);

            foreach (var material in mesh.Materials.Values)
            {
                material.Finish();
            }

            mesh.Sky?.Finish();

            foreach (var overlay in mesh.Overlays)
            {
                overlay.Finish();
            }

            return mesh;
        }

        internal static bool TryGetFaceMaterial(BspFile bsp, BspFace face, out BspTextureInfo texInfo, out string material)
        {
            return TryGetTextureMaterial(bsp, face.TexInfo, out texInfo, out material);
        }

        internal static bool TryGetTextureMaterial(BspFile bsp, int texInfoIndex, out BspTextureInfo texInfo, out string material)
        {
            texInfo = default;
            material = string.Empty;

            if (texInfoIndex < 0 || texInfoIndex >= bsp.TextureInfos.Length)
            {
                return false;
            }

            texInfo = bsp.TextureInfos[texInfoIndex];

            if (texInfo.TexData < 0 || texInfo.TexData >= bsp.TextureData.Length)
            {
                return false;
            }

            material = bsp.GetTextureName(bsp.TextureData[texInfo.TexData].NameStringTableId);
            return true;
        }

        internal static BspMeshFlags GetMaterialFlags(BspTextureInfo texInfo, string material)
        {
            var isWater = (texInfo.Flags & SurfFlags.Warp) != 0
                || material.Contains("water", StringComparison.OrdinalIgnoreCase);

            return isWater ? BspMeshFlags.Water : BspMeshFlags.None;
        }

        private static void AddFace(BuildContext context, int faceIndex, BspModelMesh mesh)
        {
            var bsp = context.Bsp;
            var face = context.Faces[faceIndex];

            if (!TryGetFaceMaterial(bsp, face, out var texInfo, out var material) || (texInfo.Flags & SkippedFlags) != 0)
            {
                return;
            }

            BspMaterialMesh target;
            var flags = BspMeshFlags.None;

            if ((texInfo.Flags & SkyFlags) != 0)
            {
                target = mesh.Sky ??= new BspMaterialMesh("sky") { Flags = BspMeshFlags.Sky };
            }
            else if (!mesh.Materials.TryGetValue(material, out target!))
            {
                target = new BspMaterialMesh(material) { Flags = GetMaterialFlags(texInfo, material) };
                mesh.Materials.Add(material, target);
            }

            if (face.DispInfo >= 0)
            {
                flags |= BspMeshFlags.Displacement;
            }

            if (face.HasLightmap && context.Layout.IsBumped(faceIndex))
            {
                flags |= BspMeshFlags.Bumped;
            }

            target.Flags |= flags;
            AppendFace(context, faceIndex, target.VertexList, target.IndexList);
        }

        /// <summary>Appends the triangles of one face. Indices are absolute into <paramref name="vertices"/>.</summary>
        internal static void AppendFace(BuildContext context, int faceIndex, List<BspVertex> vertices, List<int> indices)
        {
            var bsp = context.Bsp;
            var face = context.Faces[faceIndex];

            if (!TryGetFaceMaterial(bsp, face, out var texInfo, out _))
            {
                return;
            }

            var texData = bsp.TextureData[texInfo.TexData];
            var textureScale = new Vector2(1f / Math.Max(texData.Width, 1), 1f / Math.Max(texData.Height, 1));
            var lightmapMin = context.Layout.DefaultUv;
            var lightmapSize = Vector2.Zero;

            if (face.HasLightmap)
            {
                context.Layout.GetUvs(faceIndex, out lightmapMin, out lightmapSize);
            }

            if (face.DispInfo >= 0)
            {
                if (face.NumEdges == 4 && face.DispInfo < bsp.DisplacementInfos.Length)
                {
                    AddDisplacement(bsp, face, texInfo, textureScale, lightmapMin, lightmapSize, vertices, indices);
                }

                return;
            }

            AddPolygon(context, face, texInfo, textureScale, lightmapMin, lightmapSize, vertices, indices);
        }

        private static Vector2 GetTexCoord(Vector3 position, BspTextureInfo texInfo, Vector2 textureScale)
        {
            var u = texInfo.TextureUAxis.GetCoordinate(position);
            var v = texInfo.TextureVAxis.GetCoordinate(position);
            return new Vector2(u, v) * textureScale;
        }

        internal static Vector4 GetTangent(BspTextureInfo texInfo, Vector3 normal)
        {
            var tangent = texInfo.TextureUAxis.Normal;
            tangent -= normal * Vector3.Dot(tangent, normal);

            var length = tangent.Length();

            if (length < 1e-6f)
            {
                return new Vector4(1f, 0f, 0f, 1f);
            }

            tangent /= length;

            var bitangent = Vector3.Cross(normal, tangent);
            var handedness = Vector3.Dot(bitangent, texInfo.TextureVAxis.Normal) < 0f ? -1f : 1f;
            return new Vector4(tangent, handedness);
        }

        private static void AddPolygon(BuildContext context, BspFace face, BspTextureInfo texInfo, Vector2 textureScale,
            Vector2 lightmapMin, Vector2 lightmapSize, List<BspVertex> vertices, List<int> indices)
        {
            var bsp = context.Bsp;

            if (face.NumEdges < 3 || face.PlaneNum >= bsp.Planes.Length)
            {
                return;
            }

            var normal = bsp.Planes[face.PlaneNum].Normal;
            var tangent = GetTangent(texInfo, normal);
            var lightmapDivisor = new Vector2(Math.Max(face.LightMapSizeX, 1), Math.Max(face.LightMapSizeY, 1));
            var lightmapOffset = new Vector2(face.LightMapOffsetX, face.LightMapOffsetY);

            BspVertex CreateVertex(Vector3 position)
            {
                var lightmapU = texInfo.LightmapUAxis.GetCoordinate(position);
                var lightmapV = texInfo.LightmapVAxis.GetCoordinate(position);
                var lightmapCoord = (new Vector2(lightmapU, lightmapV) - lightmapOffset) / lightmapDivisor;

                return new BspVertex
                {
                    Position = position,
                    Normal = normal,
                    TexCoord = GetTexCoord(position, texInfo, textureScale),
                    LightmapCoord = face.HasLightmap ? (lightmapCoord * lightmapSize) + lightmapMin : lightmapMin,
                    Tangent = tangent,
                };
            }

            if (face.PrimitiveCount > 0 && TryAddPrimitives(bsp, face, CreateVertex, vertices, indices))
            {
                return;
            }

            var baseIndex = vertices.Count;

            for (var i = 0; i < face.NumEdges; i++)
            {
                vertices.Add(CreateVertex(bsp.GetVertexFromSurfEdge(face.FirstEdge + i)));
            }

            // Source winds faces clockwise, the output winds counter-clockwise
            for (var i = 1; i < face.NumEdges - 1; i++)
            {
                AddTriangle(indices, baseIndex, baseIndex + i + 1, baseIndex + i);
            }
        }

        private static bool TryAddPrimitives(BspFile bsp, BspFace face, Func<Vector3, BspVertex> createVertex,
            List<BspVertex> vertices, List<int> indices)
        {
            var primitives = bsp.Primitives;
            var primitiveIndices = bsp.PrimitiveIndices;
            var primitiveVertices = bsp.PrimitiveVertices;
            var firstPrimitive = face.FirstPrimitive;
            var primitiveCount = face.PrimitiveCount;

            if (firstPrimitive + primitiveCount > primitives.Length)
            {
                return false;
            }

            var startVertices = vertices.Count;
            var startIndices = indices.Count;
            var faceVertexBase = -1;

            for (var p = 0; p < primitiveCount; p++)
            {
                var primitive = primitives[firstPrimitive + p];

                if (primitive.FirstIndex + primitive.IndexCount > primitiveIndices.Length)
                {
                    Rollback(vertices, indices, startVertices, startIndices);
                    return false;
                }

                var primitiveIndexSpan = primitiveIndices.AsSpan(primitive.FirstIndex, primitive.IndexCount);
                int vertexBase;
                int vertexCount;

                if (primitive.VertCount == 0)
                {
                    if (faceVertexBase < 0)
                    {
                        faceVertexBase = vertices.Count;

                        for (var i = 0; i < face.NumEdges; i++)
                        {
                            vertices.Add(createVertex(bsp.GetVertexFromSurfEdge(face.FirstEdge + i)));
                        }
                    }

                    vertexBase = faceVertexBase;
                    vertexCount = face.NumEdges;
                }
                else
                {
                    if (primitive.FirstVert + primitive.VertCount > primitiveVertices.Length)
                    {
                        Rollback(vertices, indices, startVertices, startIndices);
                        return false;
                    }

                    vertexBase = vertices.Count;
                    vertexCount = primitive.VertCount;

                    for (var i = 0; i < primitive.VertCount; i++)
                    {
                        vertices.Add(createVertex(primitiveVertices[primitive.FirstVert + i]));
                    }
                }

                var localOffset = primitive.VertCount == 0 ? 0 : primitive.FirstVert;

                if (!TryAddPrimitiveTriangles(primitive.Type, primitiveIndexSpan, localOffset, vertexBase, vertexCount, indices))
                {
                    Rollback(vertices, indices, startVertices, startIndices);
                    return false;
                }
            }

            return indices.Count > startIndices;
        }

        private static void Rollback(List<BspVertex> vertices, List<int> indices, int vertexCount, int indexCount)
        {
            vertices.RemoveRange(vertexCount, vertices.Count - vertexCount);
            indices.RemoveRange(indexCount, indices.Count - indexCount);
        }

        private static bool TryAddPrimitiveTriangles(PrimitiveType type, ReadOnlySpan<ushort> source, int localOffset,
            int vertexBase, int vertexCount, List<int> indices)
        {
            foreach (var value in source)
            {
                var local = value - localOffset;

                if (local < 0 || local >= vertexCount)
                {
                    return false;
                }
            }

            var shift = vertexBase - localOffset;

            switch (type)
            {
                case PrimitiveType.TriangleList:
                    for (var i = 0; i + 2 < source.Length; i += 3)
                    {
                        AddTriangle(indices, source[i] + shift, source[i + 2] + shift, source[i + 1] + shift);
                    }

                    return true;

                case PrimitiveType.TriangleStrip:
                    for (var i = 0; i + 2 < source.Length; i++)
                    {
                        var even = (i & 1) == 0;
                        var a = source[i] + shift;
                        var b = source[even ? i + 1 : i + 2] + shift;
                        var c = source[even ? i + 2 : i + 1] + shift;

                        if (a != b && b != c && a != c)
                        {
                            AddTriangle(indices, a, c, b);
                        }
                    }

                    return true;

                default:
                    return false;
            }
        }

        private static void AddDisplacement(BspFile bsp, BspFace face, BspTextureInfo texInfo, Vector2 textureScale,
            Vector2 lightmapMin, Vector2 lightmapSize, List<BspVertex> vertices, List<int> indices)
        {
            var displacement = bsp.DisplacementManager[face.DispInfo];
            displacement.GetCorners(out var c0, out var c1, out var c2, out var c3);

            var uv00 = GetTexCoord(c0, texInfo, textureScale);
            var uv10 = GetTexCoord(c3, texInfo, textureScale);
            var uv01 = GetTexCoord(c1, texInfo, textureScale);
            var uv11 = GetTexCoord(c2, texInfo, textureScale);

            var size = displacement.Size;
            var subdivisionScale = 1f / displacement.Subdivisions;
            var baseIndex = vertices.Count;

            for (var y = 0; y < size; y++)
            {
                var v = y * subdivisionScale;

                for (var x = 0; x < size; x++)
                {
                    var u = x * subdivisionScale;
                    var bottom = Vector2.Lerp(uv00, uv10, u);
                    var top = Vector2.Lerp(uv01, uv11, u);
                    var normal = displacement.GetNormal(x, y);

                    vertices.Add(new BspVertex
                    {
                        Position = displacement.GetPosition(x, y),
                        Normal = normal,
                        TexCoord = Vector2.Lerp(bottom, top, v),
                        LightmapCoord = (new Vector2(u, v) * lightmapSize) + lightmapMin,
                        DisplacementAlpha = displacement.GetAlpha(x, y),
                        Tangent = GetTangent(texInfo, normal),
                    });
                }
            }

            for (var y = 0; y < size - 1; y++)
            {
                for (var x = 0; x < size - 1; x++)
                {
                    var i00 = baseIndex + x + (y * size);
                    var i10 = i00 + 1;
                    var i01 = i00 + size;
                    var i11 = i01 + 1;

                    if (((x + y) & 1) == 0)
                    {
                        AddTriangle(indices, i00, i10, i11);
                        AddTriangle(indices, i00, i11, i01);
                    }
                    else
                    {
                        AddTriangle(indices, i00, i10, i01);
                        AddTriangle(indices, i10, i11, i01);
                    }
                }
            }
        }

        private static void AddTriangle(List<int> indices, int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }
}

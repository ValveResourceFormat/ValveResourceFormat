using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Combines morph target deformations for one mesh into its rect of the <see cref="RendererContext.MorphAtlas"/>.
    /// </summary>
    /// <remarks>
    /// The rect is the size of the morph set, laid out row by row at its width. Both of the atlas' fields hold it:
    /// position/speed is accumulated into the left one and normal/wrinkle into the right, matching Valve's compositor.
    /// Every active morph adds its weighted deltas on top of the others'.
    /// </remarks>
    public class MorphComposite
    {
        /// <summary>Width of the morph set in texels, which is also the row stride vertices are laid out at.</summary>
        public int Width { get; }

        /// <summary>Height of the morph set in texels.</summary>
        public int Height { get; }

        /// <summary>Left edge of the rect within an atlas field, valid when <see cref="IsPlaced"/>.</summary>
        public int AtlasX { get; internal set; }

        /// <summary>Bottom edge of the rect within an atlas field, valid when <see cref="IsPlaced"/>.</summary>
        public int AtlasY { get; internal set; }

        /// <summary>Whether the composite has a rect in the atlas, which it gets the first time it renders.</summary>
        public bool IsPlaced { get; internal set; }

        internal bool IsQueued { get; set; }

        private readonly RenderTexture sourceAtlas;
        private readonly int vao;
        private readonly int bufferHandle;

        // Four vertices per rect. A morph's rects are contiguous, so a morph names its range of them.
        private readonly MorphRectVertex[] allVertices;
        private readonly MorphRectVertex[] usedVertices;
        private readonly (int Start, int Count)[] morphRects;
        private readonly HashSet<int> activeMorphs = [];
        private readonly bool hasNormalWrinkleBundle;

        // Mutable because SetMorphValue pokes the current weight into PositionWeights in place.
        [StructLayout(LayoutKind.Sequential)]
        private struct MorphRectVertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector4 PositionWeights;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector4 TexCoords;
            [VertexAttribute(VertexSlot.TexCoord1)] public Vector4 OffsetsPositionSpeed;
            [VertexAttribute(VertexSlot.TexCoord2)] public Vector4 RangesPositionSpeed;
            [VertexAttribute(VertexSlot.TexCoord3)] public Vector4 OffsetsNormalWrinkle;
            [VertexAttribute(VertexSlot.TexCoord4)] public Vector4 RangesNormalWrinkle;

            /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<MorphRectVertex>();
        }

        /// <summary>Initializes the morph composite for the given morph data, uploading the atlas and building the vertex buffer.</summary>
        /// <param name="renderContext">Renderer context for loading textures.</param>
        /// <param name="morph">Morph data describing the morph targets and atlas layout.</param>
        public MorphComposite(RendererContext renderContext, Morph morph)
        {
            ArgumentNullException.ThrowIfNull(morph.TextureResource);
            sourceAtlas = renderContext.MaterialLoader.LoadTexture(morph.TextureResource);

            // The atlas is addressed texel by texel, so the filtering the vtex flags asked for must not apply.
            sourceAtlas.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            Width = morph.Data.GetInt32Property("m_nWidth");
            Height = morph.Data.GetInt32Property("m_nHeight");

            var morphDatas = morph.GetMorphDatas();
            morphRects = new (int, int)[Math.Max(morph.GetMorphCount(), morphDatas.Count)];

            var vertices = new List<MorphRectVertex>();
            hasNormalWrinkleBundle = FillVertices(morph, morphDatas, vertices);

            allVertices = [.. vertices];
            usedVertices = new MorphRectVertex[allVertices.Length];

            var label = $"{nameof(MorphComposite)}: {System.IO.Path.GetFileName(morph.TextureResource.FileName)}";

            bufferHandle = GraphicsDevice.CreateBuffer(label);
            vao = MorphRectVertex.InputLayout.CreateVertexArray(label, bufferHandle, renderContext.MeshBufferCache.QuadIndices.GLHandle);

            // Immutable storage cannot be empty
            GL.NamedBufferStorage(bufferHandle, Math.Max(1, allVertices.Length) * MorphRectVertex.InputLayout.Stride, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
        }

        /// <summary>Sets the blend weight for the specified morph target and marks it as active or inactive.</summary>
        /// <param name="morphId">Morph target identifier.</param>
        /// <param name="value">Blend weight to apply.</param>
        public void SetMorphValue(int morphId, float value)
        {
            var (start, count) = morphRects[morphId];

            foreach (ref var vertex in allVertices.AsSpan(start * 4, count * 4))
            {
                vertex.PositionWeights.Z = value;
                vertex.PositionWeights.W = value;
            }

            if (Math.Abs(value) > 0.001f)
            {
                activeMorphs.Add(morphId);
            }
            else
            {
                activeMorphs.Remove(morphId);
            }
        }

        /// <summary>Deactivates every morph, so the next draw leaves the rect empty and the mesh in its bind pose.</summary>
        public void Clear() => activeMorphs.Clear();

        /// <summary>Draws the active morphs into this composite's rect of the atlas, whose framebuffer is bound.</summary>
        internal void Draw(Shader shader)
        {
            var vertexCount = 0;

            foreach (var morphId in activeMorphs)
            {
                var (start, count) = morphRects[morphId];

                Array.Copy(allVertices, start * 4, usedVertices, vertexCount, count * 4);
                vertexCount += count * 4;
            }

            if (vertexCount == 0)
            {
                return;
            }

            GL.NamedBufferSubData(bufferHandle, IntPtr.Zero, vertexCount * MorphRectVertex.InputLayout.Stride, usedVertices);

            shader.SetUniform2("vAtlasOrigin", new Vector2(AtlasX, AtlasY));
            shader.SetTexture(0, "g_tSourceMorphAtlas", sourceAtlas);

            VertexArray.Bind(vao, shader);

            var indexCount = vertexCount / 4 * 6;

            shader.SetUniform1("bCompositeNormals", 0);
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, 0);

            if (hasNormalWrinkleBundle)
            {
                shader.SetUniform1("bCompositeNormals", 1);
                GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, 0);
            }
        }

        /// <summary>Deletes the composite's vertex buffer. Its rect is given up through <see cref="MorphCompositeAtlas.Release"/>.</summary>
        public void Delete()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(bufferHandle);
        }

        // Adds a quad per rect of every morph, and says whether any rect carries a normal/wrinkle bundle
        private bool FillVertices(Morph morph, IReadOnlyList<KVObject> morphDatas, List<MorphRectVertex> vertices)
        {
            var bundleTypes = morph.GetBundleTypes();

            // Older morph sets do not name their bundles, and put the position/speed one first like the named ones do
            var positionSpeedBundle = bundleTypes.Length > 0 ? Array.IndexOf(bundleTypes, MorphBundleType.PositionSpeed) : 0;
            var normalWrinkleBundle = Array.IndexOf(bundleTypes, MorphBundleType.NormalWrinkle);

            var hasNormalWrinkle = false;

            for (var morphId = 0; morphId < morphDatas.Count; morphId++)
            {
                var morphData = morphDatas[morphId];

                if (morphData.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var rectDatas = morphData.GetArray("m_morphRectDatas");
                morphRects[morphId] = (vertices.Count / 4, rectDatas.Count);

                foreach (var rectData in rectDatas)
                {
                    var bundleDatas = rectData.GetArray("m_bundleDatas");

                    // The destination rect is shared, each bundle only brings its own source rect and encoding
                    var positionSpeed = positionSpeedBundle >= 0 && positionSpeedBundle < bundleDatas.Count ? bundleDatas[positionSpeedBundle] : null;
                    var normalWrinkle = normalWrinkleBundle >= 0 && normalWrinkleBundle < bundleDatas.Count ? bundleDatas[normalWrinkleBundle] : null;

                    hasNormalWrinkle |= normalWrinkle != null;

                    AddRectVertices(vertices, rectData, positionSpeed, normalWrinkle);
                }
            }

            return hasNormalWrinkle;
        }

        private void AddRectVertices(List<MorphRectVertex> vertices, KVObject rectData, KVObject? positionSpeed, KVObject? normalWrinkle)
        {
            var widthU = rectData.GetFloatProperty("m_flUWidthSrc");
            var heightV = rectData.GetFloatProperty("m_flVHeightSrc");

            // Placed in texels of the morph set, whose rows are texel rows of the atlas rect; the vertex shader adds
            // the rect's origin
            float left = rectData.GetInt32Property("m_nXLeftDst");
            float top = rectData.GetInt32Property("m_nYTopDst");
            var right = left + (sourceAtlas.Width * widthU);
            var bottom = top + (sourceAtlas.Height * heightV);

            // Both bundles read the same sized rect out of the source atlas, only their origin differs
            var (leftU, topV) = SourceOrigin(positionSpeed);
            var (leftUNormal, topVNormal) = SourceOrigin(normalWrinkle);
            var (rightU, bottomV) = (leftU + widthU, topV + heightV);
            var (rightUNormal, bottomVNormal) = (leftUNormal + widthU, topVNormal + heightV);

            var vertex = new MorphRectVertex
            {
                OffsetsPositionSpeed = BundleVector(positionSpeed, "m_offsets"),
                RangesPositionSpeed = BundleVector(positionSpeed, "m_ranges"),
                OffsetsNormalWrinkle = BundleVector(normalWrinkle, "m_offsets"),
                RangesNormalWrinkle = BundleVector(normalWrinkle, "m_ranges"),
            };

            vertices.Add(vertex with { PositionWeights = new(left, top, 0f, 0f), TexCoords = new(leftU, topV, leftUNormal, topVNormal) });
            vertices.Add(vertex with { PositionWeights = new(right, top, 0f, 0f), TexCoords = new(rightU, topV, rightUNormal, topVNormal) });
            vertices.Add(vertex with { PositionWeights = new(right, bottom, 0f, 0f), TexCoords = new(rightU, bottomV, rightUNormal, bottomVNormal) });
            vertices.Add(vertex with { PositionWeights = new(left, bottom, 0f, 0f), TexCoords = new(leftU, bottomV, leftUNormal, bottomVNormal) });

            static (float U, float V) SourceOrigin(KVObject? bundle)
                => bundle == null ? default : (bundle.GetFloatProperty("m_flULeftSrc"), bundle.GetFloatProperty("m_flVTopSrc"));

            static Vector4 BundleVector(KVObject? bundle, string name)
                => bundle == null ? default : new Vector4(bundle.GetFloatArray(name));
        }
    }
}

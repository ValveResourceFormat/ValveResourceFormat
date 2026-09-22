using System.Diagnostics.CodeAnalysis;
using System.Linq;
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
    /// Both fields are accumulated additively over every active morph rect.
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

        private int vao;
        private int bufferHandle;
        private MorphRectVertex[] allVertices;
        private MorphRectVertex[] usedVertices;
        private readonly RenderTexture morphAtlas;
        private List<int>[] morphRects;
        private readonly HashSet<int> usedRects = [];
        private bool hasNormalWrinkleBundle;

        struct MorphCompositeRectData
        {
            public float LeftX;
            public float TopY;
            public float WidthU;
            public float HeightV;

            public float LeftU;
            public float TopV;
            public Vector4 Offsets;
            public Vector4 Ranges;

            public float LeftUNormalWrinkle;
            public float TopVNormalWrinkle;
            public Vector4 OffsetsNormalWrinkle;
            public Vector4 RangesNormalWrinkle;
        }

        /// <summary>Initializes the morph composite for the given morph data, uploading the atlas and building the vertex buffer.</summary>
        /// <param name="renderContext">Renderer context for loading textures.</param>
        /// <param name="morph">Morph data describing the morph targets and atlas layout.</param>
        public MorphComposite(RendererContext renderContext, Morph morph)
        {
            ArgumentNullException.ThrowIfNull(morph.TextureResource);
            morphAtlas = renderContext.MaterialLoader.LoadTexture(morph.TextureResource);

            // The atlas is addressed texel by texel, so the filtering the vtex flags asked for must not apply.
            morphAtlas.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            Width = morph.Data.GetInt32Property("m_nWidth");
            Height = morph.Data.GetInt32Property("m_nHeight");
            var label = $"{nameof(MorphComposite)}: {System.IO.Path.GetFileName(morph.TextureResource.FileName)}";

            InitVertexBuffer(renderContext, label);

            FillVertices(morph);

            GL.NamedBufferStorage(bufferHandle, allVertices.Length * MorphRectVertex.InputLayout.Stride, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
        }

        private static int GetMorphDataBundleCount(KVObject morphData)
        {
            var rectDatas = morphData.GetSubCollection("m_morphRectDatas");
            return rectDatas.Count;
        }

        /// <summary>Draws the active morph rects into this composite's rect of the atlas, whose framebuffer is bound.</summary>
        internal void Draw(Shader shader)
        {
            var usedVertexCount = usedRects.Count * 4;

            if (usedVertexCount == 0)
            {
                return;
            }

            BuildVertexBuffer();

            GL.NamedBufferSubData(bufferHandle, IntPtr.Zero, usedVertexCount * MorphRectVertex.InputLayout.Stride, usedVertices);

            shader.SetUniform2("vAtlasOrigin", new Vector2(AtlasX, AtlasY));
            shader.SetTexture(0, "morphAtlas", morphAtlas);

            VertexArray.Bind(vao, shader);

            var indexCount = usedRects.Count * 6;

            shader.SetUniform1("bCompositeNormals", 0);
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, 0);

            if (hasNormalWrinkleBundle)
            {
                shader.SetUniform1("bCompositeNormals", 1);
                GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedShort, 0);
            }
        }

        /// <summary>Deactivates every morph, so the next draw leaves the rect empty and the mesh in its bind pose.</summary>
        public void Clear() => usedRects.Clear();

        /// <summary>Deletes the composite's vertex buffer. Its rect is given up through <see cref="MorphCompositeAtlas.Release"/>.</summary>
        public void Delete()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(bufferHandle);
        }

        // Mutable because SetVertexMorphValue pokes the current weight into PositionWeights in place.
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

        private void InitVertexBuffer(RendererContext renderContext, string label)
        {
            bufferHandle = GraphicsDevice.CreateBuffer(label);
            vao = MorphRectVertex.InputLayout.CreateVertexArray(label, bufferHandle, renderContext.MeshBufferCache.QuadIndices.GLHandle);
        }

        [MemberNotNull(nameof(allVertices), nameof(usedVertices), nameof(morphRects))]
        private void FillVertices(Morph morph)
        {
            var morphDatas = morph.GetMorphDatas();

            if (morphDatas == null || morphDatas.Count == 0)
            {
                allVertices = [];
                usedVertices = [];
                morphRects = [];
                return;
            }

            var bundleTypes = morph.GetBundleTypes();

            // Older morph sets do not name their bundles, and put the position/speed one first like the named ones do
            var positionSpeedBundle = bundleTypes.Length > 0 ? Array.IndexOf(bundleTypes, MorphBundleType.PositionSpeed) : 0;
            var normalWrinkleBundle = Array.IndexOf(bundleTypes, MorphBundleType.NormalWrinkle);

            var bundleCount = morphDatas.Sum(morphData => GetMorphDataBundleCount(morphData));

            allVertices = new MorphRectVertex[bundleCount * 4];
            usedVertices = new MorphRectVertex[allVertices.Length];
            morphRects = new List<int>[morph.GetMorphCount()];

            var rectCount = 0;
            for (var morphId = 0; morphId < morphDatas.Count; morphId++)
            {
                var morphDataChild = morphDatas[morphId];
                morphRects[morphId] = new List<int>(10);

                if (morphDataChild.ValueType != KVValueType.Collection)
                {
                    continue;
                }

                var morphRectDatas = morphDataChild.GetArray("m_morphRectDatas") ?? [];

                foreach (var rectPair in morphRectDatas)
                {
                    morphRects[morphId].Add(rectCount);

                    var bundleDatas = rectPair.GetArray("m_bundleDatas") ?? [];

                    var vertexData = new MorphCompositeRectData
                    {
                        LeftX = rectPair.GetInt32Property("m_nXLeftDst"),
                        TopY = rectPair.GetInt32Property("m_nYTopDst"),
                        WidthU = rectPair.GetFloatProperty("m_flUWidthSrc"),
                        HeightV = rectPair.GetFloatProperty("m_flVHeightSrc"),
                    };

                    // The destination rect is shared, each bundle only brings its own source rect and encoding.
                    if (positionSpeedBundle >= 0 && positionSpeedBundle < bundleDatas.Count)
                    {
                        var bundleData = bundleDatas[positionSpeedBundle];

                        vertexData.LeftU = bundleData.GetFloatProperty("m_flULeftSrc");
                        vertexData.TopV = bundleData.GetFloatProperty("m_flVTopSrc");
                        vertexData.Offsets = new Vector4(bundleData.GetFloatArray("m_offsets"));
                        vertexData.Ranges = new Vector4(bundleData.GetFloatArray("m_ranges"));
                    }

                    if (normalWrinkleBundle >= 0 && normalWrinkleBundle < bundleDatas.Count)
                    {
                        var bundleData = bundleDatas[normalWrinkleBundle];

                        vertexData.LeftUNormalWrinkle = bundleData.GetFloatProperty("m_flULeftSrc");
                        vertexData.TopVNormalWrinkle = bundleData.GetFloatProperty("m_flVTopSrc");
                        vertexData.OffsetsNormalWrinkle = new Vector4(bundleData.GetFloatArray("m_offsets"));
                        vertexData.RangesNormalWrinkle = new Vector4(bundleData.GetFloatArray("m_ranges"));

                        hasNormalWrinkleBundle = true;
                    }

                    SetRectData(rectCount, vertexData);
                    rectCount++;
                }
            }
        }

        private void BuildVertexBuffer()
        {
            var addedRects = 0;
            foreach (var rect in usedRects)
            {
                Array.Copy(allVertices, rect * 4, usedVertices, addedRects * 4, 4);
                addedRects++;
            }
        }

        private void SetRectData(int rectI, MorphCompositeRectData data)
        {
            var stride = rectI * 4;

            // Placed in texels of the morph set, whose rows are texel rows of the atlas rect; the vertex shader adds
            // the rect's origin
            var topLeftX = data.LeftX;
            var topLeftY = data.TopY;
            var bottomRightX = topLeftX + (morphAtlas.Width * data.WidthU);
            var bottomRightY = topLeftY + (morphAtlas.Height * data.HeightV);

            // Both bundles read the same sized rect out of the atlas, only their origin differs
            var leftU = data.LeftU;
            var topV = data.TopV;
            var rightU = leftU + data.WidthU;
            var bottomV = topV + data.HeightV;

            var leftUNormal = data.LeftUNormalWrinkle;
            var topVNormal = data.TopVNormalWrinkle;
            var rightUNormal = leftUNormal + data.WidthU;
            var bottomVNormal = topVNormal + data.HeightV;

            SetVertex(stride + 0, topLeftX, topLeftY, new Vector4(leftU, topV, leftUNormal, topVNormal), data);
            SetVertex(stride + 1, bottomRightX, topLeftY, new Vector4(rightU, topV, rightUNormal, topVNormal), data);
            SetVertex(stride + 2, bottomRightX, bottomRightY, new Vector4(rightU, bottomV, rightUNormal, bottomVNormal), data);
            SetVertex(stride + 3, topLeftX, bottomRightY, new Vector4(leftU, bottomV, leftUNormal, bottomVNormal), data);
        }

        private void SetVertex(int vertex, float x, float y, Vector4 texCoords, MorphCompositeRectData data)
        {
            allVertices[vertex] = new MorphRectVertex
            {
                PositionWeights = new Vector4(x, y, 0f, 0f),
                TexCoords = texCoords,
                OffsetsPositionSpeed = data.Offsets,
                RangesPositionSpeed = data.Ranges,
                OffsetsNormalWrinkle = data.OffsetsNormalWrinkle,
                RangesNormalWrinkle = data.RangesNormalWrinkle,
            };
        }

        private void SetVertexMorphValue(int vertex, float val)
        {
            ref var positionWeights = ref allVertices[vertex].PositionWeights;

            positionWeights.Z = val;
            positionWeights.W = val;
        }

        /// <summary>Sets the blend weight for the specified morph target and marks its rects as active or inactive.</summary>
        /// <param name="morphId">Morph target identifier.</param>
        /// <param name="value">Blend weight to apply.</param>
        public void SetMorphValue(int morphId, float value)
        {
            var isUsed = Math.Abs(value) > 0.001f;

            foreach (var rect in morphRects[morphId])
            {
                var stride = rect * 4;
                SetVertexMorphValue(stride + 0, value);
                SetVertexMorphValue(stride + 1, value);
                SetVertexMorphValue(stride + 2, value);
                SetVertexMorphValue(stride + 3, value);

                if (isUsed)
                {
                    usedRects.Add(rect);
                }
                else
                {
                    usedRects.Remove(rect);
                }
            }
        }
    }
}

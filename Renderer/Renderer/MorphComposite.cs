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
    /// Combines morph target deformations into GPU texture for facial animation rendering.
    /// </summary>
    public class MorphComposite
    {
        /// <summary>Gets the GPU texture containing the composited morph target offsets.</summary>
        public RenderTexture CompositeTexture { get; }

        private readonly int frameBuffer;
        private readonly Shader shader;
        private int vao;
        private int bufferHandle;
        private MorphRectVertex[] allVertices;
        private readonly RenderTexture morphAtlas;
        private List<int>[] morphRects;
        private readonly HashSet<int> usedRects = [];
        private int morphCount;
        private bool renderTargetInitialized;

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
        }

        /// <summary>Initializes the morph composite for the given morph data, uploading the atlas and building the vertex buffer.</summary>
        /// <param name="renderContext">Renderer context for loading shaders and textures.</param>
        /// <param name="morph">Morph data describing the morph targets and atlas layout.</param>
        public MorphComposite(RendererContext renderContext, Morph morph)
        {
            ArgumentNullException.ThrowIfNull(morph.TextureResource);
            morphAtlas = renderContext.MaterialLoader.LoadTexture(morph.TextureResource);
            shader = renderContext.ShaderLoader.LoadShader("morph_composite");

            var width = morph.Data.GetInt32Property("m_nWidth");
            var height = morph.Data.GetInt32Property("m_nHeight");
            CompositeTexture = new(TextureTarget.Texture2D, width, height, 1, 1);

            GL.CreateFramebuffers(1, out frameBuffer);

            InitVertexBuffer(renderContext);

            FillVertices(morph);

#if DEBUG
            var label = $"{nameof(MorphComposite)}: {System.IO.Path.GetFileName(morph.TextureResource.FileName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, bufferHandle, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, CompositeTexture.Handle, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
            GL.ObjectLabel(ObjectLabelIdentifier.Framebuffer, frameBuffer, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
#endif
        }

        private static int GetMorphDataBundleCount(KVObject morphData)
        {
            var rectDatas = morphData.GetSubCollection("m_morphRectDatas");
            return rectDatas.Count;
        }

        private void InitRenderTarget()
        {
            const int TextureSize = 2048;

            CompositeTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            CompositeTexture.SetWrapMode(TextureWrapMode.ClampToEdge);

            GL.TextureStorage2D(CompositeTexture.Handle, 1, SizedInternalFormat.Rgb16f, TextureSize, TextureSize);
            GL.NamedFramebufferTexture(frameBuffer, FramebufferAttachment.ColorAttachment0, CompositeTexture.Handle, 0);
        }

        /// <summary>Composites all active morph targets into <see cref="CompositeTexture"/>.</summary>
        public void Render()
        {
            var usedVertexCount = usedRects.Count * 4;

            GL.NamedBufferData(bufferHandle, usedVertexCount * MorphRectVertex.InputLayout.Stride, allVertices, BufferUsageHint.DynamicDraw);

            if (!renderTargetInitialized)
            {
                InitRenderTarget();
                renderTargetInitialized = true;
            }

            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.DstAlpha, BlendingFactor.One);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);
            shader.Use();
            shader.SetTexture(0, "morphAtlas", morphAtlas);

            GL.Viewport(0, 0, 2048, 2048);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            VertexArray.Bind(vao, shader);

            GL.DrawElements(PrimitiveType.Triangles, usedRects.Count * 6, DrawElementsType.UnsignedShort, 0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }

        // Mutable because SetVertexMorphValue pokes the current weight into PositionWeights in place.
        [StructLayout(LayoutKind.Sequential)]
        private struct MorphRectVertex
        {
            [VertexAttribute(VertexSlot.Position)] public Vector4 PositionWeights;
            [VertexAttribute(VertexSlot.TexCoord)] public Vector4 TexCoords;
            [VertexAttribute(VertexSlot.TexCoord1)] public Vector4 OffsetsPositionSpeed;
            [VertexAttribute(VertexSlot.TexCoord2)] public Vector4 RangesPositionSpeed;

            /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
            public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<MorphRectVertex>();
        }

        private void InitVertexBuffer(RendererContext renderContext)
        {
            GL.CreateBuffers(1, out bufferHandle);

            vao = MorphRectVertex.InputLayout.CreateVertexArray(nameof(MorphComposite), bufferHandle, renderContext.MeshBufferCache.QuadIndices.GLHandle);
        }

        [MemberNotNull(nameof(allVertices), nameof(morphRects))]
        private void FillVertices(Morph morph)
        {
            var morphDatas = morph.GetMorphDatas();

            if (morphDatas == null || morphDatas.Count == 0)
            {
                allVertices = [];
                morphRects = [];
                return;
            }

            var bundleCount = morphDatas.Sum(morphData => GetMorphDataBundleCount(morphData));

            allVertices = new MorphRectVertex[bundleCount * 4];
            morphCount = morph.GetMorphCount();
            morphRects = new List<int>[morphCount];

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

                    //TODO: Implement normal/wrinkle bundle type (second bundle data usually, if exists)
                    var bundleData = (rectPair.GetArray("m_bundleDatas") ?? [])[0];

                    var offsets = bundleData.GetFloatArray("m_offsets");
                    var ranges = bundleData.GetFloatArray("m_ranges");

                    var vertexData = new MorphCompositeRectData
                    {
                        LeftX = rectPair.GetInt32Property("m_nXLeftDst"),
                        TopY = rectPair.GetInt32Property("m_nYTopDst"),
                        WidthU = rectPair.GetFloatProperty("m_flUWidthSrc"),
                        HeightV = rectPair.GetFloatProperty("m_flVHeightSrc"),

                        LeftU = bundleData.GetFloatProperty("m_flULeftSrc"),
                        TopV = bundleData.GetFloatProperty("m_flVTopSrc"),

                        Offsets = new Vector4(
                            offsets[0], offsets[1], offsets[2], offsets[3]
                        ),

                        Ranges = new Vector4(
                            ranges[0], ranges[1], ranges[2], ranges[3]
                        ),
                    };

                    SetRectData(rectCount, vertexData);
                    rectCount++;
                }
            }
        }

        private void BuildVertexBuffer(MorphRectVertex[] usedVertices)
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
            const float TextureSize = 2048f;
            const float VertexOffset = 2f / TextureSize;
            const float PixelSize = 1 / TextureSize;

            var stride = rectI * 4;

            var widthScale = morphAtlas.Width / TextureSize;
            var heightScale = morphAtlas.Height / TextureSize;

            var topLeftX = VertexOffset + (data.LeftX * PixelSize * 2) - 1;
            var topLeftY = 1 - (VertexOffset + data.TopY * PixelSize * 2);
            var bottomRightX = topLeftX + widthScale * data.WidthU * 2;
            var bottomRightY = topLeftY - heightScale * data.HeightV * 2;

            var topLeftU = data.LeftU;
            var topLeftV = data.TopV;
            var bottomRightU = topLeftU + data.WidthU;
            var bottomRightV = topLeftV + data.HeightV;

            SetVertex(stride + 0, topLeftX, topLeftY, topLeftU, topLeftV, data);
            SetVertex(stride + 1, bottomRightX, topLeftY, bottomRightU, topLeftV, data);
            SetVertex(stride + 2, bottomRightX, bottomRightY, bottomRightU, bottomRightV, data);
            SetVertex(stride + 3, topLeftX, bottomRightY, topLeftU, bottomRightV, data);
        }

        private void SetVertex(int vertex, float x, float y, float u, float v, MorphCompositeRectData data)
        {
            allVertices[vertex] = new MorphRectVertex
            {
                PositionWeights = new Vector4(x, y, 0f, 0f),
                TexCoords = new Vector4(u, v, u, v),
                OffsetsPositionSpeed = data.Offsets,
                RangesPositionSpeed = data.Ranges,
            };
        }

        private void SetVertexMorphValue(int vertex, float val)
        {
            ref var positionWeights = ref allVertices[vertex].PositionWeights;

            positionWeights.Z = val;
            positionWeights.W = val;
        }

        private float GetMorphValue(int morphId)
        {
            var rects = morphRects[morphId];
            if (rects.Count == 0)
            {
                return 0f;
            }

            return allVertices[rects.First() * 4].PositionWeights.X;
        }

        /// <summary>Sets the blend weight for the specified morph target and marks its rects as active or inactive.</summary>
        /// <param name="morphId">Morph target identifier.</param>
        /// <param name="value">Blend weight to apply.</param>
        public void SetMorphValue(int morphId, float value)
        {
            var morphValue = GetMorphValue(morphId);
            var isUsed = Math.Abs(morphValue) > 0.001f;

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

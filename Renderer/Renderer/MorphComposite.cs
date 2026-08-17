using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveKeyValue;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Combines morph target deformations into GPU texture for facial animation rendering.
    /// </summary>
    /// <remarks>
    /// The composite is twice as wide as the morph set: the position/speed field is accumulated into
    /// the left half and the normal/wrinkle field into the right half, matching Valve's compositor.
    /// Both fields are accumulated additively over every active morph rect.
    /// </remarks>
    public class MorphComposite
    {
        /// <summary>Gets the GPU texture containing the composited morph target offsets.</summary>
        public RenderTexture CompositeTexture { get; }

        private readonly int frameBuffer;
        private readonly Shader shader;
        private readonly RenderStateTracker renderState;
        private int vao;
        private int bufferHandle;
        private MorphRectVertex[] allVertices;
        private MorphRectVertex[] usedVertices;
        private readonly RenderTexture morphAtlas;
        private List<int>[] morphRects;
        private readonly HashSet<int> usedRects = [];
        private bool hasNormalWrinkleBundle;
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

            public float LeftUNormalWrinkle;
            public float TopVNormalWrinkle;
            public Vector4 OffsetsNormalWrinkle;
            public Vector4 RangesNormalWrinkle;
        }

        /// <summary>Initializes the morph composite for the given morph data, uploading the atlas and building the vertex buffer.</summary>
        /// <param name="renderContext">Renderer context for loading shaders and textures.</param>
        /// <param name="morph">Morph data describing the morph targets and atlas layout.</param>
        public MorphComposite(RendererContext renderContext, Morph morph)
        {
            ArgumentNullException.ThrowIfNull(morph.TextureResource);
            morphAtlas = renderContext.MaterialLoader.LoadTexture(morph.TextureResource);

            // The atlas is addressed texel by texel, so the filtering the vtex flags asked for must not apply.
            morphAtlas.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);

            shader = renderContext.ShaderLoader.LoadShader("morph_composite");
            renderState = renderContext.RenderState;

            var width = morph.Data.GetInt32Property("m_nWidth");
            var height = morph.Data.GetInt32Property("m_nHeight");
            var label = $"{nameof(MorphComposite)}: {System.IO.Path.GetFileName(morph.TextureResource.FileName)}";

            CompositeTexture = new(TextureTarget.Texture2D, width * 2, height, 1, 1, label);

            GL.CreateFramebuffers(1, out frameBuffer);

            InitVertexBuffer(renderContext);

#if DEBUG
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, bufferHandle, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
            GL.ObjectLabel(ObjectLabelIdentifier.Framebuffer, frameBuffer, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
#endif

            FillVertices(morph);

            GL.NamedBufferData(bufferHandle, allVertices.Length * MorphRectVertex.InputLayout.Stride, IntPtr.Zero, BufferUsageHint.DynamicDraw);
        }

        private static int GetMorphDataBundleCount(KVObject morphData)
        {
            var rectDatas = morphData.GetSubCollection("m_morphRectDatas");
            return rectDatas.Count;
        }

        private void InitRenderTarget()
        {
            CompositeTexture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            CompositeTexture.SetWrapMode(TextureWrapMode.ClampToEdge);

            GL.TextureStorage2D(CompositeTexture.Handle, 1, SizedInternalFormat.Rgba16f, CompositeTexture.Width, CompositeTexture.Height);
            GL.NamedFramebufferTexture(frameBuffer, FramebufferAttachment.ColorAttachment0, CompositeTexture.Handle, 0);
        }

        /// <summary>Composites all active morph targets into <see cref="CompositeTexture"/>.</summary>
        public void Render()
        {
            if (!renderTargetInitialized)
            {
                InitRenderTarget();
                renderTargetInitialized = true;
            }

            var usedVertexCount = usedRects.Count * 4;
            BuildVertexBuffer();

            GL.NamedBufferSubData(bufferHandle, IntPtr.Zero, usedVertexCount * MorphRectVertex.InputLayout.Stride, usedVertices);

            // Every rect adds its weighted deltas on top of the ones already accumulated, alpha included.
            using var _ = renderState.Scope(cullMode: RsCullMode.None, multisampleEnable: false,
                depthTest: false, depthWrite: false,
                blend: true, srcBlend: RsBlendMode.One, dstBlend: RsBlendMode.One);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);
            GL.Viewport(0, 0, CompositeTexture.Width, CompositeTexture.Height);

            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            shader.Use();
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

        private void InitVertexBuffer(RendererContext renderContext)
        {
            GL.CreateBuffers(1, out bufferHandle);

            vao = MorphRectVertex.InputLayout.CreateVertexArray(nameof(MorphComposite), bufferHandle, renderContext.MeshBufferCache.QuadIndices.GLHandle);
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

            var compositeWidth = (float)CompositeTexture.Width;
            var compositeHeight = (float)CompositeTexture.Height;

            var rectWidth = morphAtlas.Width * data.WidthU;
            var rectHeight = morphAtlas.Height * data.HeightV;

            // The whole [-1, 1] range spans both fields, so a rect placed by its pixel coordinates
            // covers the left half and the second pass shifts it into the right one.
            // Y grows downwards like the source data, the vertex shader negates it.
            var topLeftX = (data.LeftX * 2f / compositeWidth) - 1f;
            var topLeftY = (data.TopY * 2f / compositeHeight) - 1f;
            var bottomRightX = topLeftX + (rectWidth * 2f / compositeWidth);
            var bottomRightY = topLeftY + (rectHeight * 2f / compositeHeight);

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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    public class MorphComposite
    {
        private const int VertexSize = 16;

        public RenderTexture CompositeTexture { get; }

        private readonly int frameBuffer;
        private readonly Shader shader;
        private int vao;
        private int bufferHandle;
        private float[] allVertices;
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

        public MorphComposite(RendererContext renderContext, Morph morph)
        {
            morphAtlas = renderContext.MaterialLoader.LoadTexture(morph.TextureResource);
            shader = renderContext.ShaderLoader.LoadShader("vrf.morph_composite");

            var width = morph.Data.GetInt32Property("m_nWidth");
            var height = morph.Data.GetInt32Property("m_nHeight");
            CompositeTexture = new(TextureTarget.Texture2D, width, height, 1, 1);

            GL.CreateFramebuffers(1, out frameBuffer);

            InitVertexBuffer(renderContext);

            FillVertices(morph);

#if DEBUG
            var label = $"{nameof(MorphComposite)}: {System.IO.Path.GetFileName(morph.TextureResource.FileName)}";
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, Math.Min(GLEnvironment.MaxLabelLength, label.Length), label);
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

        public void Render()
        {
            var usedVerticesLength = usedRects.Count * 4 * VertexSize;

            GL.NamedBufferData(bufferHandle, usedVerticesLength * sizeof(float), allVertices, BufferUsageHint.DynamicDraw);

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

            GL.BindVertexArray(vao);

            GL.DrawElements(PrimitiveType.Triangles, (usedVerticesLength / VertexSize / 4) * 6, DrawElementsType.UnsignedShort, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }

        private void InitVertexBuffer(RendererContext renderContext)
        {
            var stride = sizeof(float) * VertexSize;

            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out bufferHandle);
            GL.VertexArrayVertexBuffer(vao, 0, bufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, renderContext.MeshBufferCache.QuadIndices.GLHandle);

            var positionWeightsLocation = GL.GetAttribLocation(shader.Program, "vPositionWeights");
            var texCoordsLocation = GL.GetAttribLocation(shader.Program, "vTexCoords");
            var offsetsLocation = GL.GetAttribLocation(shader.Program, "vOffsetsPositionSpeed");
            var rangesLocation = GL.GetAttribLocation(shader.Program, "vRangesPositionSpeed");

            GL.EnableVertexArrayAttrib(vao, positionWeightsLocation);
            GL.EnableVertexArrayAttrib(vao, texCoordsLocation);
            GL.EnableVertexArrayAttrib(vao, offsetsLocation);
            GL.EnableVertexArrayAttrib(vao, rangesLocation);

            GL.VertexArrayAttribFormat(vao, positionWeightsLocation, 4, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribFormat(vao, texCoordsLocation, 4, VertexAttribType.Float, false, sizeof(float) * 4);
            GL.VertexArrayAttribFormat(vao, offsetsLocation, 4, VertexAttribType.Float, false, sizeof(float) * 8);
            GL.VertexArrayAttribFormat(vao, rangesLocation, 4, VertexAttribType.Float, false, sizeof(float) * 12);

            GL.VertexArrayAttribBinding(vao, positionWeightsLocation, 0);
            GL.VertexArrayAttribBinding(vao, texCoordsLocation, 0);
            GL.VertexArrayAttribBinding(vao, offsetsLocation, 0);
            GL.VertexArrayAttribBinding(vao, rangesLocation, 0);
        }

        [MemberNotNull(nameof(allVertices), nameof(morphRects))]
        private void FillVertices(Morph morph)
        {
            var morphDatas = morph.GetMorphDatas();
            var bundleCount = morphDatas.Sum(morphData => GetMorphDataBundleCount((KVObject)morphData.Value));

            allVertices = new float[bundleCount * 4 * VertexSize];
            morphCount = morph.GetMorphCount();
            morphRects = new List<int>[morphCount];

            var rectCount = 0;
            foreach (var pair in morphDatas)
            {
                var morphId = int.Parse(pair.Key, CultureInfo.InvariantCulture);
                morphRects[morphId] = new List<int>(10);

                if (pair.Value is not KVObject morphData)
                {
                    continue;
                }

                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");

                foreach (var rectPair in morphRectDatas)
                {
                    morphRects[morphId].Add(rectCount);

                    var morphRectData = (KVObject)rectPair.Value;
                    //TODO: Implement normal/wrinkle bundle type (second bundle data usually, if exists)
                    var bundleData = (KVObject)morphRectData.GetSubCollection("m_bundleDatas").First().Value;

                    var offsets = bundleData.GetFloatArray("m_offsets");
                    var ranges = bundleData.GetFloatArray("m_ranges");

                    var vertexData = new MorphCompositeRectData
                    {
                        LeftX = morphRectData.GetInt32Property("m_nXLeftDst"),
                        TopY = morphRectData.GetInt32Property("m_nYTopDst"),
                        WidthU = morphRectData.GetFloatProperty("m_flUWidthSrc"),
                        HeightV = morphRectData.GetFloatProperty("m_flVHeightSrc"),

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

        private void BuildVertexBuffer(float[] usedVertices)
        {
            var addedRects = 0;
            foreach (var rect in usedRects)
            {
                Array.Copy(allVertices, rect * 4 * VertexSize, usedVertices, addedRects * 4 * VertexSize, VertexSize * 4);
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
            var stride = vertex * VertexSize;

            allVertices[stride + 0] = x;
            allVertices[stride + 1] = y;
            allVertices[stride + 2] = 0f;
            allVertices[stride + 3] = 0f;
            allVertices[stride + 4] = u;
            allVertices[stride + 5] = v;
            allVertices[stride + 6] = u;
            allVertices[stride + 7] = v;
            allVertices[stride + 8] = data.Offsets.X;
            allVertices[stride + 9] = data.Offsets.Y;
            allVertices[stride + 10] = data.Offsets.Z;
            allVertices[stride + 11] = data.Offsets.W;
            allVertices[stride + 12] = data.Ranges.X;
            allVertices[stride + 13] = data.Ranges.Y;
            allVertices[stride + 14] = data.Ranges.Z;
            allVertices[stride + 15] = data.Ranges.W;
        }

        private void SetVertexMorphValue(int vertex, float val)
        {
            var stride = vertex * VertexSize;

            allVertices[stride + 2] = val;
            allVertices[stride + 3] = val;
        }

        private float GetMorphValue(int morphId)
        {
            var rects = morphRects[morphId];
            if (rects.Count == 0)
            {
                return 0f;
            }

            return allVertices[rects.First() * 4 * VertexSize];
        }

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

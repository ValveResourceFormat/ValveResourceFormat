using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    class MorphComposite
    {
        private const float VertexOffset = 2f / 2048f;
        private const int VertexSize = 16;

        public int CompositeTexture { get; }
        public Morph Morph { get; }
        public int Width { get; }
        public int Height { get; }

        private int frameBuffer;
        private Shader shader;
        private int vertexBufferHandle;
        private int vertexArray;
        private float[] allVertices;
        private float[] usedVerticies;
        private int usedVerticiesLength;
        private RenderTexture morphAtlas;
        private List<int>[] morphRects;
        private HashSet<int> usedRects = new();
        private int morphCount;

        private readonly QuadIndexBuffer quadIndices;

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

        public MorphComposite(VrfGuiContext vrfGuiContext, Morph morph)
        {
            morphAtlas = vrfGuiContext.MaterialLoader.LoadTexture(morph.TextureResource);
            Morph = morph;

            allVertices = new float[GetMorphBundleCount() * 4 * VertexSize];
            usedVerticies = new float[allVertices.Length];

            quadIndices = vrfGuiContext.QuadIndices;
            shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.morph_composite");

            GL.UseProgram(shader.Program);

            CompositeTexture = GL.GenTexture();
            frameBuffer = GL.GenFramebuffer();

            InitVertexBuffer();

            FillVertices();

            Width = Morph.Data.GetInt32Property("m_nWidth");
            Height = Morph.Data.GetInt32Property("m_nHeight");
        }

        private int GetMorphBundleCount()
        {
            var morphDatas = Morph.GetMorphDatas();
            return morphDatas.Sum(morphData => GetMorphDataBundleCount((IKeyValueCollection)morphData.Value));
        }

        private static int GetMorphDataBundleCount(IKeyValueCollection morphData)
        {
            var rectDatas = morphData.GetSubCollection("m_morphRectDatas");
            return rectDatas.Count();
        }

        public void Render()
        {
            BuildVertexBuffer();

            GL.UseProgram(shader.Program);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcColor, BlendingFactor.One);
            GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.One);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            GL.BlendFunc(BlendingFactor.DstAlpha, BlendingFactor.One);
            GL.BlendEquation(BlendEquationMode.FuncAdd);

            GL.Disable(EnableCap.CullFace);

            GL.BindVertexArray(vertexArray);
            GL.EnableVertexAttribArray(0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, quadIndices.GLHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, usedVerticiesLength * sizeof(float), usedVerticies, BufferUsageHint.DynamicDraw);

            //render target
            GL.BindTexture(TextureTarget.Texture2D, CompositeTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f, 2048, 2048, 0, PixelFormat.Rgba, PixelType.Float, 0);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            shader.SetTexture(0, "morphAtlas", morphAtlas);

            //draw
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);
            GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);

            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, CompositeTexture, 0);

            GL.Viewport(0, 0, 2048, 2048);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            GL.DrawElements(BeginMode.Triangles, (usedVerticiesLength / VertexSize / 4) * 6, DrawElementsType.UnsignedShort, 0);

            //unbind everything
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);

            GL.UseProgram(0);
        }

        private void InitVertexBuffer()
        {
            vertexArray = GL.GenVertexArray();
            GL.BindVertexArray(vertexArray);

            vertexBufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vertexBufferHandle);

            var stride = sizeof(float) * VertexSize;

            var positionWeightsLocation = GL.GetAttribLocation(shader.Program, "vPositionWeights");
            GL.VertexAttribPointer(positionWeightsLocation, 4, VertexAttribPointerType.Float, false, stride, 0);
            var texCoordsLocation = GL.GetAttribLocation(shader.Program, "vTexCoords");
            GL.VertexAttribPointer(texCoordsLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 4);
            var offsetsLocation = GL.GetAttribLocation(shader.Program, "vOffsetsPositionSpeed");
            GL.VertexAttribPointer(offsetsLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 8);
            var rangesLocation = GL.GetAttribLocation(shader.Program, "vRangesPositionSpeed");
            GL.VertexAttribPointer(rangesLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 12);

            GL.EnableVertexAttribArray(positionWeightsLocation);
            GL.EnableVertexAttribArray(texCoordsLocation);
            GL.EnableVertexAttribArray(offsetsLocation);
            GL.EnableVertexAttribArray(rangesLocation);

            GL.BindVertexArray(0);
        }

        private void FillVertices()
        {
            var morphDatas = Morph.GetMorphDatas();

            morphCount = Morph.GetMorphCount();
            morphRects = new List<int>[morphCount];

            var rectCount = 0;
            foreach (var pair in morphDatas)
            {
                var morphId = int.Parse(pair.Key);
                morphRects[morphId] = new List<int>(10);

                if (pair.Value is not IKeyValueCollection morphData)
                {
                    continue;
                }

                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");

                foreach (var rectPair in morphRectDatas)
                {
                    morphRects[morphId].Add(rectCount);

                    var morphRectData = (IKeyValueCollection)rectPair.Value;
                    //TODO: Implement normal/wrinkle bundle type (second bundle data usually, if exists)
                    var bundleData = (IKeyValueCollection)morphRectData.GetSubCollection("m_bundleDatas").First().Value;

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
        private void BuildVertexBuffer()
        {
            var rectCount = usedRects.Count;
            usedVerticiesLength = rectCount * 4 * VertexSize;

            var addedRects = 0;
            foreach (var rect in usedRects)
            {
                Array.Copy(allVertices, rect * 4 * VertexSize, usedVerticies, addedRects * 4 * VertexSize, VertexSize * 4);
                addedRects++;
            }
        }
        private void SetRectData(int rectI, MorphCompositeRectData data)
        {
            const float pixelSize = 1 / 2048f;
            var stride = rectI * 4;

            var widthScale = morphAtlas.Width / 2048f;
            var heightScale = morphAtlas.Height / 2048f;

            var topLeftX = VertexOffset + (data.LeftX * pixelSize * 2) - 1;
            var topLeftY = 1 - (VertexOffset + data.TopY * pixelSize * 2);
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

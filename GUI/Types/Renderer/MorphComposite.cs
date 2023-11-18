using System;
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

        private int frameBuffer;
        private Shader shader;
        private int vertexBufferHandle;
        private int vertexArray;
        private float[] rawVertices;
        private RenderTexture morphAtlas;

        private readonly QuadIndexBuffer quadIndices;

        struct MorphCompositeRectData
        {
            public int Width;
            public int Height;
            public float LeftX;
            public float TopY;
            public float WidthU;
            public float HeightV;
            public float LeftU;
            public float TopV;

            public float MorphState;

            public Vector4 Offsets;
            public Vector4 Ranges;
        }

        public MorphComposite(VrfGuiContext vrfGuiContext, Morph morph)
        {
            morphAtlas = vrfGuiContext.MaterialLoader.LoadTexture(morph.TextureResource);
            Morph = morph;

            rawVertices = new float[GetMorphBundleCount() * 4 * VertexSize];

            quadIndices = vrfGuiContext.QuadIndices;
            shader = vrfGuiContext.ShaderLoader.LoadShader("vrf.morph_composite");

            GL.UseProgram(shader.Program);

            CompositeTexture = GL.GenTexture();
            frameBuffer = GL.GenFramebuffer();

            InitVertexBuffer();

            FillVerticies();

            Render();
        }

        private int GetMorphBundleCount()
        {
            //TODO: Clean up
            return Morph.GetMorphDatas()
                .Sum(morphData => ((IKeyValueCollection)morphData.Value).GetSubCollection("m_morphRectDatas").Count());
        }
        public void Render()
        {
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
            GL.BufferData(BufferTarget.ArrayBuffer, rawVertices.Length * sizeof(float), rawVertices, BufferUsageHint.DynamicDraw);

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
            GL.DrawElements(BeginMode.Triangles, (rawVertices.Length / VertexSize / 4) * 6, DrawElementsType.UnsignedShort, 0);

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

        private void FillVerticies()
        {
            var morphDatas = Morph.GetMorphDatas();

            var width = Morph.Data.GetInt32Property("m_nWidth");
            var height = Morph.Data.GetInt32Property("m_nHeight");

            var i = 0;
            foreach (var pair in morphDatas)
            {
                if (pair.Value is not IKeyValueCollection morphData)
                {
                    continue;
                }

                var morphName = morphData.GetStringProperty("m_name");

                //TODO: Get morph state
                var morphState = 0f;
                switch (morphName)
                {
                    case "inflate_late":
                        morphState = 0.02083f;
                        break;
                    case "inflate_early":
                        morphState = 0.99074f;
                        break;
                    case "inflate_none":
                        morphState = -0.00988f;
                        break;
                    case "jaw":
                        morphState = 1f;
                        break;
                    default:
                        break;
                }


                var morphRectDatas = morphData.GetSubCollection("m_morphRectDatas");

                foreach (var rectPair in morphRectDatas)
                {
                    var morphRectData = (IKeyValueCollection)rectPair.Value;
                    //TODO: Implement normal/wrinkle bundle type (second bundle data usually, if exists)
                    var bundleData = (IKeyValueCollection)morphRectData.GetSubCollection("m_bundleDatas").First().Value;

                    var offsets = bundleData.GetFloatArray("m_offsets");
                    var ranges = bundleData.GetFloatArray("m_ranges");

                    var vertexData = new MorphCompositeRectData
                    {
                        Width = width,
                        Height = height,

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

                        MorphState = morphState,
                    };

                    SetRectData(i, vertexData);
                    i++;
                }
            }
        }
        private void SetRectData(int rectI, MorphCompositeRectData data)
        {
            var stride = rectI * 4;

            var topLeftX = VertexOffset + (data.LeftX * 2 / 2048f) - 1;
            var topLeftY = 1 - (VertexOffset + (data.TopY * 2 / 2048f));
            var bottomRightX = topLeftX + data.WidthU * 2;
            var bottomRightY = topLeftY - data.HeightV * 2;

            var topLeftU = data.LeftU;
            var topLeftV = data.TopV;
            var bottomRightU = topLeftU + data.WidthU;
            var bottomRightV = topLeftV - data.HeightV;

            SetVertex(stride + 0, topLeftX, topLeftY, topLeftU, topLeftV, data);
            SetVertex(stride + 1, bottomRightX, topLeftY, bottomRightU, topLeftV, data);
            SetVertex(stride + 2, bottomRightX, bottomRightY, bottomRightU, bottomRightV, data);
            SetVertex(stride + 3, topLeftX, bottomRightY, topLeftU, bottomRightV, data);
        }
        private void SetVertex(int vertex, float x, float y, float u, float v, MorphCompositeRectData data)
        {
            var stride = vertex * VertexSize;

            rawVertices[stride + 0] = x;
            rawVertices[stride + 1] = y;
            rawVertices[stride + 2] = data.MorphState;
            rawVertices[stride + 3] = data.MorphState;
            rawVertices[stride + 4] = u;
            rawVertices[stride + 5] = v;
            rawVertices[stride + 6] = u;
            rawVertices[stride + 7] = v;
            rawVertices[stride + 8] = data.Offsets.X;
            rawVertices[stride + 9] = data.Offsets.Y;
            rawVertices[stride + 10] = data.Offsets.Z;
            rawVertices[stride + 11] = data.Offsets.W;
            rawVertices[stride + 12] = data.Ranges.X;
            rawVertices[stride + 13] = data.Ranges.Y;
            rawVertices[stride + 14] = data.Ranges.Z;
            rawVertices[stride + 15] = data.Ranges.W;
        }
    }
}

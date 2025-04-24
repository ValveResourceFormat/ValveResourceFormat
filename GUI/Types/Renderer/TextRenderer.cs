using System.Buffers;
using System.Reflection;
using System.Runtime.InteropServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;

#nullable disable

namespace GUI.Types.Renderer
{
    class TextRenderer
    {
        private record FontMetric(Vector4 PlaneBounds, Vector4 AtlasBounds, float Advance);

        [StructLayout(LayoutKind.Sequential)]
        struct Vertex
        {
            public const int Size = 8;

            public Vector2 Position;
            public Vector2 TexCoord;
            public Vector4 Color;
        }

        private readonly VrfGuiContext guiContext;
        private RenderTexture fontTexture;
        private Shader shader;
        private int bufferHandle;
        private int vao;
        private Vector2 WindowSize;

        public TextRenderer(VrfGuiContext guiContext)
        {
            this.guiContext = guiContext;
        }

        public void Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var fontStream = assembly.GetManifestResourceStream("GUI.Utils.jetbrains_mono_msdf.png");
            using var bitmap = SKBitmap.Decode(fontStream);

            shader = guiContext.ShaderLoader.LoadShader("vrf.font_msdf");

            fontTexture = new RenderTexture(TextureTarget.Texture2D, (int)AtlasSize, (int)AtlasSize, 1, 1);
            fontTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            fontTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            GL.TextureStorage2D(fontTexture.Handle, 1, SizedInternalFormat.Rgba8, bitmap.Width, bitmap.Height);
            GL.TextureSubImage2D(fontTexture.Handle, 0, 0, 0, bitmap.Width, bitmap.Height, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());

            // Create VAO
            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 2),
                ("vTEXCOORD", 2),
                ("vCOLOR", 4),
            };
            var stride = sizeof(float) * Vertex.Size;
            var offset = 0;

            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out bufferHandle);
            GL.VertexArrayVertexBuffer(vao, 0, bufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, guiContext.MeshBufferCache.QuadIndices.GLHandle);

            foreach (var (name, size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, name);
                GL.EnableVertexArrayAttrib(vao, attributeLocation);
                GL.VertexArrayAttribFormat(vao, attributeLocation, size, VertexAttribType.Float, false, offset);
                GL.VertexArrayAttribBinding(vao, attributeLocation, 0);
                offset += sizeof(float) * size;
            }

#if DEBUG
            var objectLabel = nameof(TextRenderer);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, objectLabel.Length, objectLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, fontTexture.Handle, objectLabel.Length, objectLabel);
#endif
        }

        public void SetViewportSize(int viewportWidth, int viewportHeight)
        {
            WindowSize = new Vector2(viewportWidth, viewportHeight);
        }

        public void RenderTextBillboard(Camera camera, Vector3 position, float scale, Vector4 color, string text, bool center = false)
        {
            var screenPosition = Vector4.Transform(new Vector4(position, 1.0f), camera.ViewProjectionMatrix);
            screenPosition /= screenPosition.W;

            if (screenPosition.Z < 0f)
            {
                return;
            }

            var x = 0.5f * (screenPosition.X + 1.0f) * WindowSize.X;
            var y = 0.5f * (1.0f - screenPosition.Y) * WindowSize.Y;

            RenderText(x, y, scale * screenPosition.Z * 100f, color, text, center, writeDepth: true);
        }

        public void RenderText(float x, float y, float scale, Vector4 color, string text,
            bool center = false, bool writeDepth = false)
        {
            var letters = 0;
            var verticesSize = text.Length * Vertex.Size * 4;
            var vertexBuffer = ArrayPool<float>.Shared.Rent(verticesSize);

            if (center)
            {
                // For correctness it should use actual plane bounds for each letter (so use real width), but good enough for monospace.
                x -= text.Length * DefaultAdvance * scale / 2f;
            }

            try
            {
                var vertices = MemoryMarshal.Cast<float, Vertex>(vertexBuffer);
                var i = 0;

                foreach (var c in text)
                {
                    if ((uint)c - 33 > 93)
                    {
                        x += DefaultAdvance * scale;
                        continue;
                    }

                    letters++;
                    var metrics = FontMetrics[c - 33];

                    var x0 = x + metrics.PlaneBounds.X * scale;
                    var y0 = y + metrics.PlaneBounds.Y * scale;
                    var x1 = x + metrics.PlaneBounds.Z * scale;
                    var y1 = y + metrics.PlaneBounds.W * scale;

                    var le = metrics.AtlasBounds.X / AtlasSize;
                    var bo = metrics.AtlasBounds.Y / AtlasSize;
                    var ri = metrics.AtlasBounds.Z / AtlasSize;
                    var to = metrics.AtlasBounds.W / AtlasSize;

                    // left bottom
                    vertices[i++] = new Vertex { Position = new Vector2(x0, y0), TexCoord = new Vector2(le, bo), Color = color };

                    // left top
                    vertices[i++] = new Vertex { Position = new Vector2(x0, y1), TexCoord = new Vector2(le, to), Color = color };

                    // right top
                    vertices[i++] = new Vertex { Position = new Vector2(x1, y1), TexCoord = new Vector2(ri, to), Color = color };

                    // right bottom
                    vertices[i++] = new Vertex { Position = new Vector2(x1, y0), TexCoord = new Vector2(ri, bo), Color = color };

                    x += metrics.Advance * scale;
                }

                verticesSize = i * Vertex.Size * sizeof(float);
                GL.NamedBufferData(bufferHandle, verticesSize, vertexBuffer, BufferUsageHint.DynamicDraw);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertexBuffer);
            }

            GL.DepthMask(writeDepth);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.UseProgram(shader.Program);
            shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographicOffCenter(0f, WindowSize.X, WindowSize.Y, 0f, -100f, 100f));
            shader.SetTexture(0, "msdf", fontTexture);
            shader.SetUniform1("g_fRange", TextureRange);

            GL.BindVertexArray(vao);
            GL.DrawElements(BeginMode.Triangles, letters * 6, DrawElementsType.UnsignedShort, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        // Font metrics for JetBrainsMono-Regular.ttf generated using msdf-atlas-gen (use Misc/FontMsdfGen)
        private const float AtlasSize = 512f;
        private const float DefaultAdvance = 0.6f;
        private const float TextureRange = 0.03125f;
        private static readonly FontMetric[] FontMetrics =
        [
            new(new(0.08989899f, -0.8635101f, 0.510101f, 0.13851011f), new(415.5f, 80.5f, 441.5f, 142.5f), 0.6f),
            new(new(0.009090909f, -0.86282825f, 0.59090906f, -0.2971717f), new(474.5f, 0.5f, 510.5f, 35.5f), 0.6f),
            new(new(-0.0959596f, -0.8660101f, 0.69595957f, 0.1360101f), new(91.5f, 154.5f, 140.5f, 216.5f), 0.6f),
            new(new(-0.06363636f, -1.0033839f, 0.6636364f, 0.27338383f), new(0.5f, 0.5f, 45.5f, 79.5f), 0.6f),
            new(new(-0.12020202f, -0.8660101f, 0.720202f, 0.1360101f), new(185.5f, 154.5f, 237.5f, 216.5f), 0.6f),
            new(new(-0.10828283f, -0.87459093f, 0.74828285f, 0.14359091f), new(92.5f, 80.5f, 145.5f, 143.5f), 0.6f),
            new(new(0.11414141f, -0.86282825f, 0.4858586f, -0.2971717f), new(485.5f, 80.5f, 508.5f, 115.5f), 0.6f),
            new(new(0.05217172f, -0.9741414f, 0.6178283f, 0.25414142f), new(46.5f, 0.5f, 81.5f, 76.5f), 0.6f),
            new(new(-0.017828282f, -0.9741414f, 0.54782826f, 0.25414142f), new(82.5f, 0.5f, 117.5f, 76.5f), 0.6f),
            new(new(-0.0959596f, -0.7493788f, 0.69595957f, 0.026378788f), new(132.5f, 459.5f, 181.5f, 507.5f), 0.6f),
            new(new(-0.07171717f, -0.7017172f, 0.67171717f, 0.04171717f), new(182.5f, 459.5f, 228.5f, 505.5f), 0.6f),
            new(new(0.07331818f, -0.27582827f, 0.5096818f, 0.28982827f), new(484.5f, 280.5f, 511.5f, 315.5f), 0.6f),
            new(new(0.009090909f, -0.49969697f, 0.59090906f, -0.16030303f), new(474.5f, 36.5f, 510.5f, 57.5f), 0.6f),
            new(new(0.08181818f, -0.28918183f, 0.5181818f, 0.14718182f), new(364.5f, 459.5f, 391.5f, 486.5f), 0.6f),
            new(new(-0.055555556f, -0.9660606f, 0.65555555f, 0.24606061f), new(225.5f, 0.5f, 269.5f, 75.5f), 0.6f),
            new(new(-0.055555556f, -0.8740909f, 0.65555555f, 0.1440909f), new(146.5f, 80.5f, 190.5f, 143.5f), 0.6f),
            new(new(-0.040555555f, -0.8660101f, 0.67055553f, 0.1360101f), new(330.5f, 154.5f, 374.5f, 216.5f), 0.6f),
            new(new(-0.054055557f, -0.8710101f, 0.65705556f, 0.1310101f), new(375.5f, 154.5f, 419.5f, 216.5f), 0.6f),
            new(new(-0.06555556f, -0.8610101f, 0.64555556f, 0.1410101f), new(420.5f, 154.5f, 464.5f, 216.5f), 0.6f),
            new(new(-0.05939394f, -0.8660101f, 0.61939394f, 0.1360101f), new(0.5f, 217.5f, 42.5f, 279.5f), 0.6f),
            new(new(-0.060555555f, -0.8610101f, 0.65055555f, 0.1410101f), new(0.5f, 280.5f, 44.5f, 342.5f), 0.6f),
            new(new(-0.07171717f, -0.8610101f, 0.67171717f, 0.1410101f), new(465.5f, 154.5f, 511.5f, 216.5f), 0.6f),
            new(new(-0.049636364f, -0.8660101f, 0.6776364f, 0.1360101f), new(221.5f, 343.5f, 266.5f, 405.5f), 0.6f),
            new(new(-0.06363636f, -0.8740909f, 0.6636364f, 0.1440909f), new(191.5f, 80.5f, 236.5f, 143.5f), 0.6f),
            new(new(-0.07171717f, -0.8710101f, 0.67171717f, 0.1310101f), new(317.5f, 343.5f, 363.5f, 405.5f), 0.6f),
            new(new(0.08181818f, -0.695202f, 0.5181818f, 0.14520203f), new(484.5f, 343.5f, 511.5f, 395.5f), 0.6f),
            new(new(0.06873737f, -0.69292927f, 0.52126265f, 0.2929293f), new(364.5f, 343.5f, 392.5f, 404.5f), 0.6f),
            new(new(-0.047474746f, -0.7259596f, 0.64747477f, 0.065959595f), new(455.5f, 406.5f, 498.5f, 455.5f), 0.6f),
            new(new(-0.047474746f, -0.6209091f, 0.64747477f, -0.03909091f), new(274.5f, 459.5f, 317.5f, 495.5f), 0.6f),
            new(new(-0.047474746f, -0.7259596f, 0.64747477f, 0.065959595f), new(88.5f, 459.5f, 131.5f, 508.5f), 0.6f),
            new(new(0.00042929294f, -0.8635101f, 0.6145707f, 0.13851011f), new(182.5f, 343.5f, 220.5f, 405.5f), 0.6f),
            new(new(-0.08537879f, -0.869899f, 0.6903788f, 0.309899f), new(425.5f, 0.5f, 473.5f, 73.5f), 0.6f),
            new(new(-0.07979798f, -0.8660101f, 0.679798f, 0.1360101f), new(91.5f, 343.5f, 138.5f, 405.5f), 0.6f),
            new(new(-0.044055555f, -0.8660101f, 0.66705555f, 0.1360101f), new(46.5f, 343.5f, 90.5f, 405.5f), 0.6f),
            new(new(-0.040474746f, -0.8740909f, 0.65447474f, 0.1440909f), new(237.5f, 80.5f, 280.5f, 143.5f), 0.6f),
            new(new(-0.03739394f, -0.8660101f, 0.64139396f, 0.1360101f), new(139.5f, 343.5f, 181.5f, 405.5f), 0.6f),
            new(new(-0.02939394f, -0.8660101f, 0.6493939f, 0.1360101f), new(441.5f, 280.5f, 483.5f, 342.5f), 0.6f),
            new(new(-0.037474748f, -0.8660101f, 0.65747476f, 0.1360101f), new(397.5f, 280.5f, 440.5f, 342.5f), 0.6f),
            new(new(-0.044474747f, -0.8740909f, 0.6504747f, 0.1440909f), new(281.5f, 80.5f, 324.5f, 143.5f), 0.6f),
            new(new(-0.03939394f, -0.8660101f, 0.6393939f, 0.1360101f), new(310.5f, 280.5f, 352.5f, 342.5f), 0.6f),
            new(new(-0.031313132f, -0.8660101f, 0.63131315f, 0.1360101f), new(268.5f, 280.5f, 309.5f, 342.5f), 0.6f),
            new(new(-0.08555555f, -0.8610101f, 0.6255556f, 0.1410101f), new(223.5f, 280.5f, 267.5f, 342.5f), 0.6f),
            new(new(-0.037636362f, -0.8660101f, 0.68963635f, 0.1360101f), new(177.5f, 280.5f, 222.5f, 342.5f), 0.6f),
            new(new(0.0006060606f, -0.8660101f, 0.67939395f, 0.1360101f), new(134.5f, 280.5f, 176.5f, 342.5f), 0.6f),
            new(new(-0.06363636f, -0.8660101f, 0.6636364f, 0.1360101f), new(88.5f, 280.5f, 133.5f, 342.5f), 0.6f),
            new(new(-0.03939394f, -0.8660101f, 0.6393939f, 0.1360101f), new(45.5f, 280.5f, 87.5f, 342.5f), 0.6f),
            new(new(-0.047474746f, -0.8740909f, 0.64747477f, 0.1440909f), new(325.5f, 80.5f, 368.5f, 143.5f), 0.6f),
            new(new(-0.042636365f, -0.8660101f, 0.68463635f, 0.1360101f), new(466.5f, 217.5f, 511.5f, 279.5f), 0.6f),
            new(new(-0.052555557f, -0.869899f, 0.65855557f, 0.309899f), new(0.5f, 80.5f, 44.5f, 153.5f), 0.6f),
            new(new(-0.036555555f, -0.8660101f, 0.67455554f, 0.1360101f), new(377.5f, 217.5f, 421.5f, 279.5f), 0.6f),
            new(new(-0.06363636f, -0.8740909f, 0.6636364f, 0.1440909f), new(369.5f, 80.5f, 414.5f, 143.5f), 0.6f),
            new(new(-0.07979798f, -0.8660101f, 0.679798f, 0.1360101f), new(286.5f, 217.5f, 333.5f, 279.5f), 0.6f),
            new(new(-0.03939394f, -0.8610101f, 0.6393939f, 0.1410101f), new(243.5f, 217.5f, 285.5f, 279.5f), 0.6f),
            new(new(-0.07979798f, -0.8660101f, 0.679798f, 0.1360101f), new(195.5f, 217.5f, 242.5f, 279.5f), 0.6f),
            new(new(-0.11212121f, -0.8660101f, 0.7121212f, 0.1360101f), new(143.5f, 217.5f, 194.5f, 279.5f), 0.6f),
            new(new(-0.0959596f, -0.8660101f, 0.69595957f, 0.1360101f), new(93.5f, 217.5f, 142.5f, 279.5f), 0.6f),
            new(new(-0.0959596f, -0.8660101f, 0.69595957f, 0.1360101f), new(43.5f, 217.5f, 92.5f, 279.5f), 0.6f),
            new(new(-0.047474746f, -0.8660101f, 0.64747477f, 0.1360101f), new(353.5f, 280.5f, 396.5f, 342.5f), 0.6f),
            new(new(0.06891414f, -0.9660606f, 0.58608586f, 0.24606061f), new(270.5f, 0.5f, 302.5f, 75.5f), 0.6f),
            new(new(-0.055555556f, -0.9660606f, 0.65555555f, 0.24606061f), new(303.5f, 0.5f, 347.5f, 75.5f), 0.6f),
            new(new(0.013914142f, -0.9660606f, 0.53108585f, 0.24606061f), new(348.5f, 0.5f, 380.5f, 75.5f), 0.6f),
            new(new(-0.055555556f, -0.86631316f, 0.65555555f, -0.20368686f), new(229.5f, 459.5f, 273.5f, 500.5f), 0.6f),
            new(new(-0.07171717f, -0.10719697f, 0.67171717f, 0.23219697f), new(422.5f, 459.5f, 468.5f, 480.5f), 0.6f),
            new(new(0.032656565f, -0.9170202f, 0.5013434f, -0.5129798f), new(392.5f, 459.5f, 421.5f, 484.5f), 0.6f),
            new(new(-0.068055555f, -0.695202f, 0.64305556f, 0.14520203f), new(0.5f, 406.5f, 44.5f, 458.5f), 0.6f),
            new(new(-0.044974748f, -0.8610101f, 0.64997476f, 0.1410101f), new(141.5f, 154.5f, 184.5f, 216.5f), 0.6f),
            new(new(-0.041974746f, -0.695202f, 0.6529747f, 0.14520203f), new(440.5f, 343.5f, 483.5f, 395.5f), 0.6f),
            new(new(-0.049974747f, -0.8610101f, 0.64497477f, 0.1410101f), new(47.5f, 154.5f, 90.5f, 216.5f), 0.6f),
            new(new(-0.047474746f, -0.695202f, 0.64747477f, 0.14520203f), new(0.5f, 459.5f, 43.5f, 511.5f), 0.6f),
            new(new(-0.07921717f, -0.8660101f, 0.6642172f, 0.1360101f), new(0.5f, 154.5f, 46.5f, 216.5f), 0.6f),
            new(new(-0.04189394f, -0.6910101f, 0.6368939f, 0.3110101f), new(442.5f, 80.5f, 484.5f, 142.5f), 0.6f),
            new(new(-0.03839394f, -0.8660101f, 0.6403939f, 0.1360101f), new(334.5f, 217.5f, 376.5f, 279.5f), 0.6f),
            new(new(-0.051717173f, -0.9137525f, 0.69171715f, 0.13675253f), new(45.5f, 80.5f, 91.5f, 145.5f), 0.6f),
            new(new(-0.047151513f, -0.9126414f, 0.5831515f, 0.3156414f), new(118.5f, 0.5f, 157.5f, 76.5f), 0.6f),
            new(new(-0.035636365f, -0.8660101f, 0.6916364f, 0.1360101f), new(0.5f, 343.5f, 45.5f, 405.5f), 0.6f),
            new(new(-0.105959594f, -0.8660101f, 0.6859596f, 0.1360101f), new(267.5f, 343.5f, 316.5f, 405.5f), 0.6f),
            new(new(-0.07171717f, -0.6921212f, 0.67171717f, 0.1321212f), new(223.5f, 406.5f, 269.5f, 457.5f), 0.6f),
            new(new(-0.03839394f, -0.6921212f, 0.6403939f, 0.1321212f), new(318.5f, 406.5f, 360.5f, 457.5f), 0.6f),
            new(new(-0.047474746f, -0.695202f, 0.64747477f, 0.14520203f), new(44.5f, 459.5f, 87.5f, 511.5f), 0.6f),
            new(new(-0.044974748f, -0.6910101f, 0.64997476f, 0.3110101f), new(422.5f, 217.5f, 465.5f, 279.5f), 0.6f),
            new(new(-0.049974747f, -0.6910101f, 0.64497477f, 0.3110101f), new(286.5f, 154.5f, 329.5f, 216.5f), 0.6f),
            new(new(-0.01839394f, -0.6921212f, 0.66039395f, 0.1321212f), new(89.5f, 406.5f, 131.5f, 457.5f), 0.6f),
            new(new(-0.047474746f, -0.695202f, 0.64747477f, 0.14520203f), new(45.5f, 406.5f, 88.5f, 458.5f), 0.6f),
            new(new(-0.08821717f, -0.83734846f, 0.6552172f, 0.13234848f), new(393.5f, 343.5f, 439.5f, 403.5f), 0.6f),
            new(new(-0.03939394f, -0.6821212f, 0.6393939f, 0.14212121f), new(180.5f, 406.5f, 222.5f, 457.5f), 0.6f),
            new(new(-0.07979798f, -0.6871212f, 0.679798f, 0.13712122f), new(132.5f, 406.5f, 179.5f, 457.5f), 0.6f),
            new(new(-0.10404041f, -0.6871212f, 0.7040404f, 0.13712122f), new(404.5f, 406.5f, 454.5f, 457.5f), 0.6f),
            new(new(-0.07979798f, -0.6871212f, 0.679798f, 0.13712122f), new(270.5f, 406.5f, 317.5f, 457.5f), 0.6f),
            new(new(-0.07979798f, -0.6860101f, 0.679798f, 0.3160101f), new(238.5f, 154.5f, 285.5f, 216.5f), 0.6f),
            new(new(-0.03939394f, -0.6871212f, 0.6393939f, 0.13712122f), new(361.5f, 406.5f, 403.5f, 457.5f), 0.6f),
            new(new(-0.057474747f, -0.9660606f, 0.6374748f, 0.24606061f), new(381.5f, 0.5f, 424.5f, 75.5f), 0.6f),
            new(new(0.12222222f, -0.9660606f, 0.47777778f, 0.24606061f), new(202.5f, 0.5f, 224.5f, 75.5f), 0.6f),
            new(new(-0.037474748f, -0.9660606f, 0.65747476f, 0.24606061f), new(158.5f, 0.5f, 201.5f, 75.5f), 0.6f),
            new(new(-0.06363636f, -0.58684343f, 0.6636364f, -0.11815657f), new(318.5f, 459.5f, 363.5f, 488.5f), 0.6f),
        ];
    }
}

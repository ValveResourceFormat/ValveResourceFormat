using System.Buffers;
using System.Reflection;
using System.Runtime.InteropServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;

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
        private QuadIndexBuffer quadIndices;

        public TextRenderer(VrfGuiContext guiContext)
        {
            this.guiContext = guiContext;
        }

        public void Load()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var fontStream = assembly.GetManifestResourceStream("GUI.Utils.jetbrains_mono_msdf.png");
            using var bitmap = SKBitmap.Decode(fontStream);

            quadIndices = guiContext.MeshBufferCache.QuadIndices;
            shader = guiContext.ShaderLoader.LoadShader("vrf.font_msdf");

            fontTexture = new RenderTexture(TextureTarget.Texture2D, (int)AtlasSize, (int)AtlasSize, 1, 1);
            fontTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            fontTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            GL.TextureStorage2D(fontTexture.Handle, 1, SizedInternalFormat.Rgba8, bitmap.Width, bitmap.Height);
            GL.TextureSubImage2D(fontTexture.Handle, 0, 0, 0, bitmap.Width, bitmap.Height, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());

            // vao
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            bufferHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, bufferHandle);
            GL.EnableVertexAttribArray(0);

            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 2),
                ("vTEXCOORD", 2),
                ("vCOLOR", 4),
            };

            var stride = sizeof(float) * Vertex.Size;
            var offset = 0;

            foreach (var (Name, Size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, Name);
                if (attributeLocation > -1)
                {
                    GL.EnableVertexAttribArray(attributeLocation);
                    GL.VertexAttribPointer(attributeLocation, Size, VertexAttribPointerType.Float, false, stride, offset);
                }
                offset += sizeof(float) * Size;
            }

            GL.BindVertexArray(0); // Unbind VAO
        }

        public void RenderText(int width, int height, float x, float y, float scale, Vector4 color, string text)
        {
            GL.UseProgram(shader.Program);

            shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -100f, 100f));
            shader.SetTexture(0, "msdf", fontTexture);
            shader.SetUniform1("g_fRange", TextureRange);

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, bufferHandle);

            var letters = 0;
            var verticesSize = text.Length * Vertex.Size * 4;
            var vertexBuffer = ArrayPool<float>.Shared.Rent(verticesSize);
            var vertices = MemoryMarshal.Cast<float, Vertex>(vertexBuffer);

            try
            {
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

                GL.BufferData(BufferTarget.ArrayBuffer, verticesSize * sizeof(float), vertexBuffer, BufferUsageHint.DynamicDraw);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertexBuffer);
            }

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, quadIndices.GLHandle);

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.DrawElements(BeginMode.Triangles, letters * 6, DrawElementsType.UnsignedShort, 0);
            GL.Disable(EnableCap.Blend);
        }

        // Font metrics for JetBrainsMono-Regular.ttf generated using msdf-atlas-gen (use Misc/FontMsdfGen)
        private const float AtlasSize = 512f;
        private const float DefaultAdvance = 0.6f;
        private const float TextureRange = 0.01171875f;
        private static readonly FontMetric[] FontMetrics =
        [
            new(new(0.18688138f, -0.7709839f, 0.41311863f, 0.045983896f), new(356.5f, 88.5f, 374.5f, 153.5f), 0.6f),
            new(new(0.1051846f, -0.768531f, 0.4948154f, -0.39146897f), new(85.5f, 473.5f, 116.5f, 503.5f), 0.6f),
            new(new(-0.007934014f, -0.7734839f, 0.607934f, 0.043483898f), new(461.5f, 88.5f, 510.5f, 153.5f), 0.6f),
            new(new(0.029772192f, -0.91174f, 0.5702278f, 0.18173999f), new(0.5f, 0.5f, 43.5f, 87.5f), 0.6f),
            new(new(-0.026787117f, -0.7734839f, 0.6267871f, 0.043483898f), new(41.5f, 157.5f, 93.5f, 222.5f), 0.6f),
            new(new(-0.013071485f, -0.78026825f, 0.65307146f, 0.049268264f), new(45.5f, 88.5f, 98.5f, 154.5f), 0.6f),
            new(new(0.20573449f, -0.768531f, 0.3942655f, -0.39146897f), new(494.5f, 223.5f, 509.5f, 253.5f), 0.6f),
            new(new(0.14646897f, -0.8816025f, 0.523531f, 0.16160251f), new(44.5f, 0.5f, 74.5f, 83.5f), 0.6f),
            new(new(0.076468974f, -0.8816025f, 0.45353103f, 0.16160251f), new(75.5f, 0.5f, 105.5f, 83.5f), 0.6f),
            new(new(-0.007934014f, -0.66314965f, 0.607934f, -0.059850354f), new(397.5f, 421.5f, 446.5f, 469.5f), 0.6f),
            new(new(0.023487825f, -0.6065122f, 0.57651216f, -0.053487822f), new(447.5f, 421.5f, 491.5f, 465.5f), 0.6f),
            new(new(0.17209701f, -0.1878154f, 0.41090298f, 0.2018154f), new(492.5f, 421.5f, 511.5f, 452.5f), 0.6f),
            new(new(0.098900236f, -0.4116968f, 0.50109977f, -0.24830322f), new(473.5f, 66.5f, 505.5f, 79.5f), 0.6f),
            new(new(0.17431265f, -0.19040298f, 0.42568734f, 0.048402984f), new(161.5f, 473.5f, 181.5f, 492.5f), 0.6f),
            new(new(0.03605656f, -0.86903375f, 0.56394345f, 0.14903378f), new(200.5f, 0.5f, 242.5f, 81.5f), 0.6f),
            new(new(0.03605656f, -0.7797683f, 0.56394345f, 0.049768265f), new(99.5f, 88.5f, 141.5f, 154.5f), 0.6f),
            new(new(0.05105656f, -0.7734839f, 0.57894343f, 0.043483898f), new(223.5f, 157.5f, 265.5f, 222.5f), 0.6f),
            new(new(0.03755656f, -0.77848387f, 0.56544346f, 0.038483895f), new(266.5f, 157.5f, 308.5f, 222.5f), 0.6f),
            new(new(0.02605656f, -0.7684839f, 0.55394346f, 0.048483897f), new(309.5f, 157.5f, 351.5f, 222.5f), 0.6f),
            new(new(0.028625295f, -0.7734839f, 0.5313747f, 0.043483898f), new(352.5f, 157.5f, 392.5f, 222.5f), 0.6f),
            new(new(0.031056559f, -0.7684839f, 0.55894345f, 0.048483897f), new(393.5f, 157.5f, 435.5f, 222.5f), 0.6f),
            new(new(0.023487825f, -0.7684839f, 0.57651216f, 0.048483897f), new(320.5f, 223.5f, 364.5f, 288.5f), 0.6f),
            new(new(0.04377219f, -0.7734839f, 0.5842278f, 0.043483898f), new(409.5f, 223.5f, 452.5f, 288.5f), 0.6f),
            new(new(0.023487825f, -0.7797683f, 0.57651216f, 0.049768265f), new(142.5f, 88.5f, 186.5f, 154.5f), 0.6f),
            new(new(0.023487825f, -0.77848387f, 0.57651216f, 0.038483895f), new(85.5f, 355.5f, 129.5f, 420.5f), 0.6f),
            new(new(0.17431265f, -0.6017871f, 0.42568734f, 0.051787116f), new(266.5f, 355.5f, 286.5f, 407.5f), 0.6f),
            new(new(0.16302828f, -0.60219955f, 0.42697173f, 0.20219953f), new(478.5f, 157.5f, 499.5f, 221.5f), 0.6f),
            new(new(0.042340927f, -0.637934f, 0.5576591f, -0.022065986f), new(355.5f, 421.5f, 396.5f, 470.5f), 0.6f),
            new(new(0.042340927f, -0.53109974f, 0.5576591f, -0.12890023f), new(43.5f, 473.5f, 84.5f, 505.5f), 0.6f),
            new(new(0.042340927f, -0.637934f, 0.5576591f, -0.022065986f), new(313.5f, 421.5f, 354.5f, 470.5f), 0.6f),
            new(new(0.08754713f, -0.7709839f, 0.5274529f, 0.045983896f), new(49.5f, 355.5f, 84.5f, 420.5f), 0.6f),
            new(new(0.007134721f, -0.7827494f, 0.5978653f, 0.22274941f), new(382.5f, 0.5f, 429.5f, 80.5f), 0.6f),
            new(new(0.0109190885f, -0.7734839f, 0.5890809f, 0.043483898f), new(464.5f, 289.5f, 510.5f, 354.5f), 0.6f),
            new(new(0.053840928f, -0.7734839f, 0.5691591f, 0.043483898f), new(422.5f, 289.5f, 463.5f, 354.5f), 0.6f),
            new(new(0.049340926f, -0.7797683f, 0.56465906f, 0.049768265f), new(187.5f, 88.5f, 228.5f, 154.5f), 0.6f),
            new(new(0.050625294f, -0.7734839f, 0.5533747f, 0.043483898f), new(337.5f, 289.5f, 377.5f, 354.5f), 0.6f),
            new(new(0.058625296f, -0.7734839f, 0.5613747f, 0.043483898f), new(296.5f, 289.5f, 336.5f, 354.5f), 0.6f),
            new(new(0.05234093f, -0.7734839f, 0.5676591f, 0.043483898f), new(254.5f, 289.5f, 295.5f, 354.5f), 0.6f),
            new(new(0.045340925f, -0.7797683f, 0.56065905f, 0.049768265f), new(229.5f, 88.5f, 270.5f, 154.5f), 0.6f),
            new(new(0.05490966f, -0.7734839f, 0.5450903f, 0.043483898f), new(170.5f, 289.5f, 209.5f, 354.5f), 0.6f),
            new(new(0.06119403f, -0.7734839f, 0.53880596f, 0.043483898f), new(473.5f, 0.5f, 511.5f, 65.5f), 0.6f),
            new(new(0.0060565593f, -0.7684839f, 0.5339434f, 0.048483897f), new(86.5f, 289.5f, 128.5f, 354.5f), 0.6f),
            new(new(0.049487825f, -0.7734839f, 0.6025122f, 0.043483898f), new(41.5f, 289.5f, 85.5f, 354.5f), 0.6f),
            new(new(0.0886253f, -0.7734839f, 0.5913747f, 0.043483898f), new(0.5f, 289.5f, 40.5f, 354.5f), 0.6f),
            new(new(0.029772192f, -0.7734839f, 0.5702278f, 0.043483898f), new(210.5f, 289.5f, 253.5f, 354.5f), 0.6f),
            new(new(0.048625294f, -0.7734839f, 0.55137473f, 0.043483898f), new(453.5f, 223.5f, 493.5f, 288.5f), 0.6f),
            new(new(0.048625294f, -0.7797683f, 0.55137473f, 0.049768265f), new(271.5f, 88.5f, 311.5f, 154.5f), 0.6f),
            new(new(0.05077219f, -0.7734839f, 0.5912278f, 0.043483898f), new(365.5f, 223.5f, 408.5f, 288.5f), 0.6f),
            new(new(0.03905656f, -0.7827494f, 0.56694347f, 0.22274941f), new(430.5f, 0.5f, 472.5f, 80.5f), 0.6f),
            new(new(0.05505656f, -0.7734839f, 0.58294344f, 0.043483898f), new(277.5f, 223.5f, 319.5f, 288.5f), 0.6f),
            new(new(0.029772192f, -0.7797683f, 0.5702278f, 0.049768265f), new(312.5f, 88.5f, 355.5f, 154.5f), 0.6f),
            new(new(0.017203456f, -0.7734839f, 0.5827965f, 0.043483898f), new(190.5f, 223.5f, 235.5f, 288.5f), 0.6f),
            new(new(0.048625294f, -0.7684839f, 0.55137473f, 0.048483897f), new(149.5f, 223.5f, 189.5f, 288.5f), 0.6f),
            new(new(0.0109190885f, -0.7734839f, 0.5890809f, 0.043483898f), new(102.5f, 223.5f, 148.5f, 288.5f), 0.6f),
            new(new(-0.02050275f, -0.7734839f, 0.62050277f, 0.043483898f), new(50.5f, 223.5f, 101.5f, 288.5f), 0.6f),
            new(new(-0.0016496466f, -0.7734839f, 0.60164964f, 0.043483898f), new(130.5f, 355.5f, 178.5f, 420.5f), 0.6f),
            new(new(-0.007934014f, -0.7734839f, 0.607934f, 0.043483898f), new(0.5f, 223.5f, 49.5f, 288.5f), 0.6f),
            new(new(0.042340927f, -0.7734839f, 0.5576591f, 0.043483898f), new(436.5f, 157.5f, 477.5f, 222.5f), 0.6f),
            new(new(0.16410644f, -0.86903375f, 0.49089357f, 0.14903378f), new(243.5f, 0.5f, 269.5f, 81.5f), 0.6f),
            new(new(0.03605656f, -0.86903375f, 0.56394345f, 0.14903378f), new(270.5f, 0.5f, 312.5f, 81.5f), 0.6f),
            new(new(0.109106444f, -0.86903375f, 0.43589357f, 0.14903378f), new(313.5f, 0.5f, 339.5f, 81.5f), 0.6f),
            new(new(0.03605656f, -0.773806f, 0.56394345f, -0.29619402f), new(0.5f, 473.5f, 42.5f, 511.5f), 0.6f),
            new(new(0.017203456f, -0.012912411f, 0.5827965f, 0.13791241f), new(206.5f, 473.5f, 251.5f, 485.5f), 0.6f),
            new(new(0.122459546f, -0.8281186f, 0.41154045f, -0.6018814f), new(182.5f, 473.5f, 205.5f, 491.5f), 0.6f),
            new(new(0.023556558f, -0.6017871f, 0.55144346f, 0.051787116f), new(329.5f, 355.5f, 371.5f, 407.5f), 0.6f),
            new(new(0.051125295f, -0.7684839f, 0.55387473f, 0.048483897f), new(94.5f, 157.5f, 134.5f, 222.5f), 0.6f),
            new(new(0.047840927f, -0.6017871f, 0.56315905f, 0.051787116f), new(287.5f, 355.5f, 328.5f, 407.5f), 0.6f),
            new(new(0.046125293f, -0.7684839f, 0.5488747f, 0.048483897f), new(0.5f, 157.5f, 40.5f, 222.5f), 0.6f),
            new(new(0.042340927f, -0.6017871f, 0.5576591f, 0.051787116f), new(224.5f, 355.5f, 265.5f, 407.5f), 0.6f),
            new(new(0.015987825f, -0.7734839f, 0.56901217f, 0.043483898f), new(416.5f, 88.5f, 460.5f, 153.5f), 0.6f),
            new(new(0.046125293f, -0.5984839f, 0.5488747f, 0.2184839f), new(375.5f, 88.5f, 415.5f, 153.5f), 0.6f),
            new(new(0.049625296f, -0.7734839f, 0.5523747f, 0.043483898f), new(236.5f, 223.5f, 276.5f, 288.5f), 0.6f),
            new(new(0.043487824f, -0.815837f, 0.5965122f, 0.038837f), new(0.5f, 88.5f, 44.5f, 156.5f), 0.6f),
            new(new(0.041762765f, -0.8201025f, 0.49423724f, 0.22310251f), new(106.5f, 0.5f, 142.5f, 83.5f), 0.6f),
            new(new(0.057772193f, -0.7734839f, 0.5982278f, 0.043483898f), new(378.5f, 289.5f, 421.5f, 354.5f), 0.6f),
            new(new(-0.011649647f, -0.7734839f, 0.59164965f, 0.043483898f), new(0.5f, 355.5f, 48.5f, 420.5f), 0.6f),
            new(new(0.017203456f, -0.6005027f, 0.5827965f, 0.04050275f), new(41.5f, 421.5f, 86.5f, 472.5f), 0.6f),
            new(new(0.049625296f, -0.6005027f, 0.5523747f, 0.04050275f), new(0.5f, 421.5f, 40.5f, 472.5f), 0.6f),
            new(new(0.042340927f, -0.6017871f, 0.5576591f, 0.051787116f), new(414.5f, 355.5f, 455.5f, 407.5f), 0.6f),
            new(new(0.051125295f, -0.5984839f, 0.55387473f, 0.2184839f), new(182.5f, 157.5f, 222.5f, 222.5f), 0.6f),
            new(new(0.046125293f, -0.5984839f, 0.5488747f, 0.2184839f), new(129.5f, 289.5f, 169.5f, 354.5f), 0.6f),
            new(new(0.069625296f, -0.6005027f, 0.5723747f, 0.04050275f), new(456.5f, 355.5f, 496.5f, 406.5f), 0.6f),
            new(new(0.042340927f, -0.6017871f, 0.5576591f, 0.051787116f), new(372.5f, 355.5f, 413.5f, 407.5f), 0.6f),
            new(new(0.006987824f, -0.7484152f, 0.56001216f, 0.043415163f), new(179.5f, 355.5f, 223.5f, 418.5f), 0.6f),
            new(new(0.048625294f, -0.59050274f, 0.55137473f, 0.05050275f), new(87.5f, 421.5f, 127.5f, 472.5f), 0.6f),
            new(new(0.0109190885f, -0.5892184f, 0.5890809f, 0.03921838f), new(225.5f, 421.5f, 271.5f, 471.5f), 0.6f),
            new(new(-0.007934014f, -0.5892184f, 0.607934f, 0.03921838f), new(128.5f, 421.5f, 177.5f, 471.5f), 0.6f),
            new(new(0.0109190885f, -0.5892184f, 0.5890809f, 0.03921838f), new(178.5f, 421.5f, 224.5f, 471.5f), 0.6f),
            new(new(0.0109190885f, -0.5934839f, 0.5890809f, 0.22348389f), new(135.5f, 157.5f, 181.5f, 222.5f), 0.6f),
            new(new(0.048625294f, -0.5892184f, 0.55137473f, 0.03921838f), new(272.5f, 421.5f, 312.5f, 471.5f), 0.6f),
            new(new(0.032340925f, -0.86903375f, 0.5476591f, 0.14903378f), new(340.5f, 0.5f, 381.5f, 81.5f), 0.6f),
            new(new(0.21201885f, -0.86903375f, 0.38798115f, 0.14903378f), new(185.5f, 0.5f, 199.5f, 81.5f), 0.6f),
            new(new(0.05234093f, -0.86903375f, 0.5676591f, 0.14903378f), new(143.5f, 0.5f, 184.5f, 81.5f), 0.6f),
            new(new(0.029772192f, -0.49704045f, 0.5702278f, -0.20795955f), new(117.5f, 473.5f, 160.5f, 496.5f), 0.6f),
        ];
    }
}

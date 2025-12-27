using System.Buffers;
using System.Diagnostics;
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
            public const int Size = 5;

            public Vector2 Position;
            public Vector2 TexCoord;
            public Color32 Color;
        }

        public struct TextRenderRequest()
        {
            public float X;
            public float Y;
            public float Scale;
            public Color32 Color = Color32.White;
            public Vector2 TextOffset = Vector2.Zero;
            public required string Text;
            public bool CenterVertical = false;
            public bool CenterHorizontal = false;
        }

        private readonly List<TextRenderRequest> TextRenderRequests = new(10);

        private readonly VrfGuiContext guiContext;
        private readonly Camera camera;

        private RenderTexture? fontTexture;
        private Shader? shader;
        private int bufferHandle;
        private int vao;

        public TextRenderer(VrfGuiContext guiContext, Camera camera)
        {
            this.guiContext = guiContext;
            this.camera = camera;
        }

        public void Load()
        {
            using var fontStream = Program.Assembly.GetManifestResourceStream("GUI.Utils.jetbrains_mono_msdf.png");
            using var bitmap = SKBitmap.Decode(fontStream);

            shader = guiContext.ShaderLoader.LoadShader("vrf.font_msdf");

            fontTexture = new RenderTexture(TextureTarget.Texture2D, (int)AtlasSize, (int)AtlasSize, 1, 1);
            fontTexture.SetWrapMode(TextureWrapMode.ClampToEdge);
            fontTexture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            GL.TextureStorage2D(fontTexture.Handle, 1, SizedInternalFormat.Rgba8, bitmap.Width, bitmap.Height);
            GL.TextureSubImage2D(fontTexture.Handle, 0, 0, 0, bitmap.Width, bitmap.Height, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());

            // Create VAO
            var attributes = new List<(string Name, int Size, VertexAttribType Type, bool Normalized)>
            {
                ("vPOSITION", 2, VertexAttribType.Float, false),
                ("vTEXCOORD", 2, VertexAttribType.Float, false),
                ("vCOLOR", 4, VertexAttribType.UnsignedByte, true),
            };

            var stride = sizeof(float) * Vertex.Size;
            var offset = 0;

            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out bufferHandle);
            GL.VertexArrayVertexBuffer(vao, 0, bufferHandle, 0, stride);
            GL.VertexArrayElementBuffer(vao, guiContext.MeshBufferCache.QuadIndices.GLHandle);

            foreach (var (name, size, type, normalized) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, name);
                GL.EnableVertexArrayAttrib(vao, attributeLocation);
                GL.VertexArrayAttribFormat(vao, attributeLocation, size, type, normalized, offset);
                GL.VertexArrayAttribBinding(vao, attributeLocation, 0);
                offset += sizeof(float) * size;
            }

#if DEBUG
            var objectLabel = nameof(TextRenderer);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, objectLabel.Length, objectLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, fontTexture.Handle, objectLabel.Length, objectLabel);
#endif
        }

        public void AddTextBillboard(Vector3 position, TextRenderRequest textRenderRequest, bool fixedScale = true)
        {
            var screenPosition = Vector4.Transform(new Vector4(position, 1.0f), camera.ViewProjectionMatrix);
            screenPosition /= screenPosition.W;

            if (screenPosition.Z < 0f)
            {
                return;
            }

            textRenderRequest.X = 0.5f * (screenPosition.X + 1.0f) * camera.WindowSize.X;
            textRenderRequest.Y = 0.5f * (1.0f - screenPosition.Y) * camera.WindowSize.Y;

            if (!fixedScale)
            {
                textRenderRequest.Scale *= screenPosition.Z * 100f;
            }

            AddText(textRenderRequest);
        }

        public void AddTextRelative(TextRenderRequest textRenderRequest)
        {
            textRenderRequest.X = camera.WindowSize.X * Math.Clamp(textRenderRequest.X, 0, 1);
            textRenderRequest.Y = camera.WindowSize.Y * Math.Clamp(textRenderRequest.Y, 0, 1);
            TextRenderRequests.Add(textRenderRequest);
        }

        public void AddText(TextRenderRequest textRenderRequest)
        {
            TextRenderRequests.Add(textRenderRequest);
        }

        public void Render()
        {
            var letters = 0;
            var verticesSize = 0;

            foreach (var textRenderRequest in TextRenderRequests)
            {
                verticesSize += textRenderRequest.Text.Length;
            }

            if (verticesSize == 0)
            {
                return;
            }

            using var _ = new GLDebugGroup("Text Render");

            verticesSize *= Vertex.Size * 4;
            var vertexBuffer = ArrayPool<float>.Shared.Rent(verticesSize);

            try
            {
                var vertices = MemoryMarshal.Cast<float, Vertex>(vertexBuffer.AsSpan());
                var i = 0;

                foreach (var textRenderRequest in TextRenderRequests)
                {
                    var x = textRenderRequest.X;
                    var y = textRenderRequest.Y;

                    x += textRenderRequest.TextOffset.X;
                    y += textRenderRequest.TextOffset.Y;

                    if (textRenderRequest.CenterVertical)
                    {
                        // For correctness it should use actual plane bounds for each letter (so use real width), but good enough for monospace.
                        x -= textRenderRequest.Text.Length * DefaultAdvance * textRenderRequest.Scale / 2f;
                    }

                    if (textRenderRequest.CenterHorizontal)
                    {
                        y -= (Ascender + Descender) / 2f * textRenderRequest.Scale;
                    }

                    var originalX = x;

                    var color = textRenderRequest.Color;

                    for (var j = 0; j < textRenderRequest.Text.Length; j++)
                    {
                        var c = textRenderRequest.Text[j];

                        if (c == '\n')
                        {
                            y += textRenderRequest.Scale * LineHeight;
                            x = originalX;
                            continue;
                        }
                        else if (c == '\\')
                        {
                            var cNext = j + 1 < textRenderRequest.Text.Length ? textRenderRequest.Text[j + 1] : '\0';
                            if (cNext == '#')
                            {
                                j += 2;
                                if (j + 8 < textRenderRequest.Text.Length)
                                {
                                    if (byte.TryParse(textRenderRequest.Text.AsSpan(j + 0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)
                                    && byte.TryParse(textRenderRequest.Text.AsSpan(j + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)
                                    && byte.TryParse(textRenderRequest.Text.AsSpan(j + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)
                                    && byte.TryParse(textRenderRequest.Text.AsSpan(j + 6, 2), System.Globalization.NumberStyles.HexNumber, null, out var a))
                                    {
                                        color = new Color32(r, g, b, a);
                                        j += 7;
                                        continue;
                                    }
                                }
                            }
                        }

                        if (c < 33 || c > 126)
                        {
                            x += DefaultAdvance * textRenderRequest.Scale;
                            continue;
                        }

                        letters++;
                        var metrics = FontMetrics[c - 33];

                        var x0 = x + metrics.PlaneBounds.X * textRenderRequest.Scale;
                        var y0 = y + metrics.PlaneBounds.Y * textRenderRequest.Scale;
                        var x1 = x + metrics.PlaneBounds.Z * textRenderRequest.Scale;
                        var y1 = y + metrics.PlaneBounds.W * textRenderRequest.Scale;

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

                        x += metrics.Advance * textRenderRequest.Scale;
                    }

                }

                verticesSize = i * Vertex.Size * sizeof(float);
                GL.NamedBufferData(bufferHandle, verticesSize, vertexBuffer, BufferUsageHint.DynamicDraw);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(vertexBuffer);
            }

            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            Debug.Assert(shader != null);
            Debug.Assert(fontTexture != null);

            shader.Use();
            shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographicOffCenter(0f, camera.WindowSize.X, camera.WindowSize.Y, 0f, -100f, 100f));
            shader.SetTexture(0, "msdf", fontTexture);
            shader.SetUniform1("g_fRange", TextureRange);

            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, letters * 6, DrawElementsType.UnsignedShort, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);

            TextRenderRequests.Clear();
        }

        // Font metrics for JetBrainsMono-Regular.ttf generated using msdf-atlas-gen (use Misc/FontMsdfGen)
        private const float AtlasSize = 512f;
        private const float Ascender = -1.02f;
        private const float Descender = 0.3f;
        private const float LineHeight = 1.32f;
        private const float DefaultAdvance = 0.6f;
        private const float TextureRange = 0.03125f;
        private static readonly FontMetric[] FontMetrics =
        [
            new(new(0.088187374f, -0.87169045f, 0.5118126f, 0.13849287f), new(218.5f, 154.5f, 244.5f, 216.5f), 0.6f),
            new(new(0.0067209774f, -0.87169045f, 0.593279f, -0.28513238f), new(474.5f, 218.5f, 510.5f, 254.5f), 0.6f),
            new(new(-0.09918533f, -0.87169045f, 0.6991853f, 0.13849287f), new(245.5f, 154.5f, 294.5f, 216.5f), 0.6f),
            new(new(-0.058452137f, -1.0020367f, 0.65845215f, 0.28513238f), new(0.5f, 0.5f, 44.5f, 79.5f), 0.6f),
            new(new(-0.123625256f, -0.87169045f, 0.72362524f, 0.13849287f), new(295.5f, 154.5f, 347.5f, 216.5f), 0.6f),
            new(new(-0.1117719f, -0.87169045f, 0.75177187f, 0.15478615f), new(91.5f, 80.5f, 144.5f, 143.5f), 0.6f),
            new(new(0.11262729f, -0.87169045f, 0.4873727f, -0.28513238f), new(486.5f, 154.5f, 509.5f, 190.5f), 0.6f),
            new(new(0.04986762f, -0.9857434f, 0.6201324f, 0.25254583f), new(45.5f, 0.5f, 80.5f, 76.5f), 0.6f),
            new(new(-0.020132383f, -0.9857434f, 0.5501324f, 0.25254583f), new(81.5f, 0.5f, 116.5f, 76.5f), 0.6f),
            new(new(-0.09918533f, -0.7576375f, 0.6991853f, 0.040733196f), new(116.5f, 407.5f, 165.5f, 456.5f), 0.6f),
            new(new(-0.06659878f, -0.70875764f, 0.6665988f, 0.040733196f), new(166.5f, 407.5f, 211.5f, 453.5f), 0.6f),
            new(new(0.071540736f, -0.28513238f, 0.5114593f, 0.30142567f), new(301.5f, 407.5f, 328.5f, 443.5f), 0.6f),
            new(new(0.0067209774f, -0.5132383f, 0.593279f, -0.15478615f), new(403.5f, 407.5f, 439.5f, 429.5f), 0.6f),
            new(new(0.08004073f, -0.28513238f, 0.5199593f, 0.15478615f), new(375.5f, 407.5f, 402.5f, 434.5f), 0.6f),
            new(new(-0.058452137f, -0.9694501f, 0.65845215f, 0.25254583f), new(224.5f, 0.5f, 268.5f, 75.5f), 0.6f),
            new(new(-0.058452137f, -0.87169045f, 0.65845215f, 0.15478615f), new(145.5f, 80.5f, 189.5f, 143.5f), 0.6f),
            new(new(-0.04345214f, -0.87169045f, 0.67345214f, 0.13849287f), new(396.5f, 154.5f, 440.5f, 216.5f), 0.6f),
            new(new(-0.056952138f, -0.87169045f, 0.65995216f, 0.13849287f), new(441.5f, 154.5f, 485.5f, 216.5f), 0.6f),
            new(new(-0.06845214f, -0.87169045f, 0.64845216f, 0.15478615f), new(190.5f, 80.5f, 234.5f, 143.5f), 0.6f),
            new(new(-0.06215886f, -0.87169045f, 0.6221589f, 0.13849287f), new(0.5f, 218.5f, 42.5f, 280.5f), 0.6f),
            new(new(-0.06345214f, -0.87169045f, 0.65345216f, 0.15478615f), new(235.5f, 80.5f, 279.5f, 143.5f), 0.6f),
            new(new(-0.06659878f, -0.87169045f, 0.6665988f, 0.15478615f), new(280.5f, 80.5f, 325.5f, 143.5f), 0.6f),
            new(new(-0.05259878f, -0.87169045f, 0.6805988f, 0.13849287f), new(225.5f, 344.5f, 270.5f, 406.5f), 0.6f),
            new(new(-0.06659878f, -0.87169045f, 0.6665988f, 0.15478615f), new(326.5f, 80.5f, 371.5f, 143.5f), 0.6f),
            new(new(-0.06659878f, -0.87169045f, 0.6665988f, 0.13849287f), new(271.5f, 344.5f, 316.5f, 406.5f), 0.6f),
            new(new(0.08004073f, -0.69246435f, 0.5199593f, 0.15478615f), new(0.5f, 407.5f, 27.5f, 459.5f), 0.6f),
            new(new(0.06689409f, -0.69246435f, 0.5231059f, 0.30142567f), new(480.5f, 281.5f, 508.5f, 342.5f), 0.6f),
            new(new(-0.0503055f, -0.7413442f, 0.6503055f, 0.073319755f), new(411.5f, 460.5f, 454.5f, 510.5f), 0.6f),
            new(new(-0.0503055f, -0.62729126f, 0.6503055f, -0.024439918f), new(257.5f, 407.5f, 300.5f, 444.5f), 0.6f),
            new(new(-0.0503055f, -0.7413442f, 0.6503055f, 0.073319755f), new(455.5f, 460.5f, 498.5f, 510.5f), 0.6f),
            new(new(-0.0020723015f, -0.87169045f, 0.6170723f, 0.13849287f), new(473.5f, 0.5f, 511.5f, 62.5f), 0.6f),
            new(new(-0.0885387f, -0.87169045f, 0.6935387f, 0.31771895f), new(424.5f, 0.5f, 472.5f, 73.5f), 0.6f),
            new(new(-0.08289206f, -0.87169045f, 0.6828921f, 0.13849287f), new(87.5f, 344.5f, 134.5f, 406.5f), 0.6f),
            new(new(-0.0388055f, -0.87169045f, 0.6618055f, 0.13849287f), new(43.5f, 344.5f, 86.5f, 406.5f), 0.6f),
            new(new(-0.043305498f, -0.87169045f, 0.6573055f, 0.15478615f), new(372.5f, 80.5f, 415.5f, 143.5f), 0.6f),
            new(new(-0.04015886f, -0.87169045f, 0.64415884f, 0.13849287f), new(0.5f, 344.5f, 42.5f, 406.5f), 0.6f),
            new(new(-0.03215886f, -0.87169045f, 0.65215886f, 0.13849287f), new(437.5f, 281.5f, 479.5f, 343.5f), 0.6f),
            new(new(-0.0403055f, -0.87169045f, 0.6603055f, 0.13849287f), new(393.5f, 281.5f, 436.5f, 343.5f), 0.6f),
            new(new(-0.0473055f, -0.87169045f, 0.6533055f, 0.15478615f), new(416.5f, 80.5f, 459.5f, 143.5f), 0.6f),
            new(new(-0.04215886f, -0.87169045f, 0.64215887f, 0.13849287f), new(307.5f, 281.5f, 349.5f, 343.5f), 0.6f),
            new(new(-0.02586558f, -0.87169045f, 0.6258656f, 0.13849287f), new(266.5f, 281.5f, 306.5f, 343.5f), 0.6f),
            new(new(-0.08845214f, -0.87169045f, 0.6284521f, 0.15478615f), new(460.5f, 80.5f, 504.5f, 143.5f), 0.6f),
            new(new(-0.040598776f, -0.87169045f, 0.69259876f, 0.13849287f), new(177.5f, 281.5f, 222.5f, 343.5f), 0.6f),
            new(new(-0.0021588595f, -0.87169045f, 0.6821589f, 0.13849287f), new(134.5f, 281.5f, 176.5f, 343.5f), 0.6f),
            new(new(-0.058452137f, -0.87169045f, 0.65845215f, 0.13849287f), new(89.5f, 281.5f, 133.5f, 343.5f), 0.6f),
            new(new(-0.04215886f, -0.87169045f, 0.64215887f, 0.13849287f), new(46.5f, 281.5f, 88.5f, 343.5f), 0.6f),
            new(new(-0.0503055f, -0.87169045f, 0.6503055f, 0.15478615f), new(0.5f, 154.5f, 43.5f, 217.5f), 0.6f),
            new(new(-0.04559878f, -0.87169045f, 0.68759876f, 0.13849287f), new(0.5f, 281.5f, 45.5f, 343.5f), 0.6f),
            new(new(-0.05545214f, -0.87169045f, 0.6614521f, 0.31771895f), new(0.5f, 80.5f, 44.5f, 153.5f), 0.6f),
            new(new(-0.03945214f, -0.87169045f, 0.67745215f, 0.13849287f), new(429.5f, 218.5f, 473.5f, 280.5f), 0.6f),
            new(new(-0.058452137f, -0.87169045f, 0.65845215f, 0.15478615f), new(44.5f, 154.5f, 88.5f, 217.5f), 0.6f),
            new(new(-0.08289206f, -0.87169045f, 0.6828921f, 0.13849287f), new(335.5f, 218.5f, 382.5f, 280.5f), 0.6f),
            new(new(-0.04215886f, -0.87169045f, 0.64215887f, 0.15478615f), new(89.5f, 154.5f, 131.5f, 217.5f), 0.6f),
            new(new(-0.08289206f, -0.87169045f, 0.6828921f, 0.13849287f), new(238.5f, 218.5f, 285.5f, 280.5f), 0.6f),
            new(new(-0.11547861f, -0.87169045f, 0.7154786f, 0.13849287f), new(186.5f, 218.5f, 237.5f, 280.5f), 0.6f),
            new(new(-0.0910387f, -0.87169045f, 0.69103867f, 0.13849287f), new(137.5f, 218.5f, 185.5f, 280.5f), 0.6f),
            new(new(-0.09918533f, -0.87169045f, 0.6991853f, 0.13849287f), new(87.5f, 218.5f, 136.5f, 280.5f), 0.6f),
            new(new(-0.0503055f, -0.87169045f, 0.6503055f, 0.13849287f), new(43.5f, 218.5f, 86.5f, 280.5f), 0.6f),
            new(new(0.06680754f, -0.9694501f, 0.58819246f, 0.25254583f), new(269.5f, 0.5f, 301.5f, 75.5f), 0.6f),
            new(new(-0.058452137f, -0.9694501f, 0.65845215f, 0.25254583f), new(302.5f, 0.5f, 346.5f, 75.5f), 0.6f),
            new(new(0.011807536f, -0.9694501f, 0.53319246f, 0.25254583f), new(347.5f, 0.5f, 379.5f, 75.5f), 0.6f),
            new(new(-0.058452137f, -0.87169045f, 0.65845215f, -0.20366599f), new(212.5f, 407.5f, 256.5f, 448.5f), 0.6f),
            new(new(-0.07474542f, -0.105906315f, 0.67474544f, 0.23625255f), new(440.5f, 407.5f, 486.5f, 428.5f), 0.6f),
            new(new(0.030747455f, -0.92057025f, 0.50325257f, -0.5132383f), new(474.5f, 255.5f, 503.5f, 280.5f), 0.6f),
            new(new(-0.07095214f, -0.69246435f, 0.64595217f, 0.15478615f), new(407.5f, 344.5f, 451.5f, 396.5f), 0.6f),
            new(new(-0.03965886f, -0.87169045f, 0.64465886f, 0.15478615f), new(175.5f, 154.5f, 217.5f, 217.5f), 0.6f),
            new(new(-0.0448055f, -0.69246435f, 0.6558055f, 0.15478615f), new(28.5f, 407.5f, 71.5f, 459.5f), 0.6f),
            new(new(-0.04465886f, -0.87169045f, 0.63965887f, 0.15478615f), new(132.5f, 154.5f, 174.5f, 217.5f), 0.6f),
            new(new(-0.0503055f, -0.69246435f, 0.6503055f, 0.15478615f), new(72.5f, 407.5f, 115.5f, 459.5f), 0.6f),
            new(new(-0.08224542f, -0.87169045f, 0.6672454f, 0.13849287f), new(135.5f, 344.5f, 181.5f, 406.5f), 0.6f),
            new(new(-0.04465886f, -0.69246435f, 0.63965887f, 0.31771895f), new(223.5f, 281.5f, 265.5f, 343.5f), 0.6f),
            new(new(-0.04115886f, -0.87169045f, 0.64315885f, 0.13849287f), new(350.5f, 281.5f, 392.5f, 343.5f), 0.6f),
            new(new(-0.046598777f, -0.92057025f, 0.6865988f, 0.13849287f), new(45.5f, 80.5f, 90.5f, 145.5f), 0.6f),
            new(new(-0.049718942f, -0.92057025f, 0.5857189f, 0.31771895f), new(117.5f, 0.5f, 156.5f, 76.5f), 0.6f),
            new(new(-0.03859878f, -0.87169045f, 0.6945988f, 0.13849287f), new(383.5f, 218.5f, 428.5f, 280.5f), 0.6f),
            new(new(-0.101038694f, -0.87169045f, 0.6810387f, 0.13849287f), new(286.5f, 218.5f, 334.5f, 280.5f), 0.6f),
            new(new(-0.07474542f, -0.69246435f, 0.67474544f, 0.13849287f), new(87.5f, 460.5f, 133.5f, 511.5f), 0.6f),
            new(new(-0.04115886f, -0.69246435f, 0.64315885f, 0.13849287f), new(226.5f, 460.5f, 268.5f, 511.5f), 0.6f),
            new(new(-0.0503055f, -0.69246435f, 0.6503055f, 0.13849287f), new(182.5f, 460.5f, 225.5f, 511.5f), 0.6f),
            new(new(-0.03965886f, -0.69246435f, 0.64465886f, 0.31771895f), new(317.5f, 344.5f, 359.5f, 406.5f), 0.6f),
            new(new(-0.04465886f, -0.69246435f, 0.63965887f, 0.31771895f), new(182.5f, 344.5f, 224.5f, 406.5f), 0.6f),
            new(new(-0.02115886f, -0.69246435f, 0.66315883f, 0.13849287f), new(44.5f, 460.5f, 86.5f, 511.5f), 0.6f),
            new(new(-0.0503055f, -0.69246435f, 0.6503055f, 0.13849287f), new(0.5f, 460.5f, 43.5f, 511.5f), 0.6f),
            new(new(-0.09124542f, -0.8391039f, 0.65824544f, 0.13849287f), new(360.5f, 344.5f, 406.5f, 404.5f), 0.6f),
            new(new(-0.04215886f, -0.69246435f, 0.64215887f, 0.15478615f), new(452.5f, 344.5f, 494.5f, 396.5f), 0.6f),
            new(new(-0.08289206f, -0.69246435f, 0.6828921f, 0.13849287f), new(134.5f, 460.5f, 181.5f, 511.5f), 0.6f),
            new(new(-0.10733198f, -0.69246435f, 0.70733196f, 0.13849287f), new(317.5f, 460.5f, 367.5f, 511.5f), 0.6f),
            new(new(-0.08289206f, -0.69246435f, 0.6828921f, 0.13849287f), new(269.5f, 460.5f, 316.5f, 511.5f), 0.6f),
            new(new(-0.08289206f, -0.69246435f, 0.6828921f, 0.31771895f), new(348.5f, 154.5f, 395.5f, 216.5f), 0.6f),
            new(new(-0.04215886f, -0.69246435f, 0.64215887f, 0.13849287f), new(368.5f, 460.5f, 410.5f, 511.5f), 0.6f),
            new(new(-0.0603055f, -0.9694501f, 0.6403055f, 0.25254583f), new(380.5f, 0.5f, 423.5f, 75.5f), 0.6f),
            new(new(0.120773934f, -0.9694501f, 0.47922608f, 0.25254583f), new(201.5f, 0.5f, 223.5f, 75.5f), 0.6f),
            new(new(-0.0403055f, -0.9694501f, 0.6603055f, 0.25254583f), new(157.5f, 0.5f, 200.5f, 75.5f), 0.6f),
            new(new(-0.06659878f, -0.5947047f, 0.6665988f, -0.105906315f), new(329.5f, 407.5f, 374.5f, 437.5f), 0.6f),
        ];
    }
}

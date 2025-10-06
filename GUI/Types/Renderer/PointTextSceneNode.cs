using System.Runtime.InteropServices;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    internal class PointTextSceneNode : SceneNode
    {
        [StructLayout(LayoutKind.Sequential)]
        struct TextVertex
        {
            public const int Size = 5; // 2 floats for position + 2 floats for texcoord + 4 bytes for color = 5 floats worth

            public Vector2 Position;
            public Vector2 TexCoord;
            public Color32 Color;
        }

        private record FontMetric(Vector4 PlaneBounds, Vector4 AtlasBounds, float Advance);

        private readonly string text;
        private readonly Color32 textColor;
        private readonly float textSize;
        
        private readonly Shader fontShader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;

        public PointTextSceneNode(Scene scene, EntityLump.Entity entity) : base(scene)
        {
            text = entity.GetProperty<string>("message", string.Empty);
            textColor = Color32.FromVector4(new Vector4(entity.GetColor32Property("color"), 1));
            textSize = entity.GetPropertyUnchecked<float>("textsize", 10.0f);

            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
            EntityData = entity;

            // Set up rendering infrastructure
            fontShader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.font_msdf");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            
            var stride = sizeof(float) * TextVertex.Size;
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, stride);
            GL.VertexArrayElementBuffer(vaoHandle, Scene.GuiContext.MeshBufferCache.QuadIndices.GLHandle);

            // Set up vertex attributes to match font_msdf shader
            var positionLocation = GL.GetAttribLocation(fontShader.Program, "vPOSITION");
            var texcoordLocation = GL.GetAttribLocation(fontShader.Program, "vTEXCOORD");
            var colorLocation = GL.GetAttribLocation(fontShader.Program, "vCOLOR");

            GL.EnableVertexArrayAttrib(vaoHandle, positionLocation);
            GL.VertexArrayAttribFormat(vaoHandle, positionLocation, 2, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vaoHandle, positionLocation, 0);

            GL.EnableVertexArrayAttrib(vaoHandle, texcoordLocation);
            GL.VertexArrayAttribFormat(vaoHandle, texcoordLocation, 2, VertexAttribType.Float, false, sizeof(float) * 2);
            GL.VertexArrayAttribBinding(vaoHandle, texcoordLocation, 0);

            GL.EnableVertexArrayAttrib(vaoHandle, colorLocation);
            GL.VertexArrayAttribFormat(vaoHandle, colorLocation, 4, VertexAttribType.UnsignedByte, true, sizeof(float) * 4);
            GL.VertexArrayAttribBinding(vaoHandle, colorLocation, 0);

#if DEBUG
            var objectLabel = nameof(PointTextSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, objectLabel.Length, objectLabel);
#endif
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (string.IsNullOrEmpty(text))
            {
                vertexCount = 0;
                return;
            }

            // Generate billboard vertices for the text
            var vertices = new List<TextVertex>();
            GenerateBillboardVertices(vertices, text, textSize, textColor);

            vertexCount = vertices.Count;
            
            if (vertexCount > 0)
            {
                var vertexData = vertices.ToArray();
                GL.NamedBufferData(vboHandle, vertexData.Length * TextVertex.Size * sizeof(float), vertexData, BufferUsageHint.DynamicDraw);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (vertexCount == 0 || context.RenderPass != RenderPass.Translucent)
            {
                return;
            }

            // Get world position for billboard centering
            var worldPosition = Transform.Translation;
            
            // Create billboard matrix that faces the camera
            var cameraPosition = context.View.Camera.Location;
            var cameraUp = Vector3.UnitZ; // Use world up vector for billboard orientation
            var forward = Vector3.Normalize(cameraPosition - worldPosition);
            var right = Vector3.Normalize(Vector3.Cross(cameraUp, forward));
            var up = Vector3.Cross(forward, right);

            // Create billboard transform matrix
            var billboardMatrix = new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                up.X, up.Y, up.Z, 0,
                forward.X, forward.Y, forward.Z, 0,
                worldPosition.X, worldPosition.Y, worldPosition.Z, 1
            );

            // Render with depth testing enabled
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            fontShader.Use();
            fontShader.SetUniform4x4("transform", billboardMatrix * context.View.Camera.ViewProjectionMatrix);
            
            // Access font texture from the scene's TextRenderer
            var fontTexture = GetFontTextureFromScene(context);
            if (fontTexture != null)
            {
                fontShader.SetTexture(0, "msdf", fontTexture);
                fontShader.SetUniform1("g_fRange", TextureRange);

                GL.BindVertexArray(vaoHandle);
                GL.DrawElements(PrimitiveType.Triangles, (vertexCount / 4) * 6, DrawElementsType.UnsignedShort, 0);
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.Disable(EnableCap.Blend);
        }
        private static void GenerateBillboardVertices(List<TextVertex> vertices, string text, float scale, Color32 color)
        {
            var x = 0f;
            var y = 0f;

            // Center the text horizontally
            x -= text.Length * DefaultAdvance * scale / 2f;
            // Center the text vertically 
            y -= (Ascender + Descender) / 2f * scale;

            foreach (var c in text)
            {
                if (c == '\n')
                {
                    y += scale * LineHeight;
                    x = -text.Length * DefaultAdvance * scale / 2f; // Reset to left side
                    continue;
                }

                if ((uint)c - 33 > 93)
                {
                    x += DefaultAdvance * scale;
                    continue;
                }

                var metrics = FontMetrics[c - 33];

                var x0 = x + metrics.PlaneBounds.X * scale;
                var y0 = y + metrics.PlaneBounds.Y * scale;
                var x1 = x + metrics.PlaneBounds.Z * scale;
                var y1 = y + metrics.PlaneBounds.W * scale;

                var le = metrics.AtlasBounds.X / AtlasSize;
                var bo = metrics.AtlasBounds.Y / AtlasSize;
                var ri = metrics.AtlasBounds.Z / AtlasSize;
                var to = metrics.AtlasBounds.W / AtlasSize;

                // Create quad vertices (bottom-left, top-left, top-right, bottom-right)
                vertices.Add(new TextVertex { Position = new Vector2(x0, y0), TexCoord = new Vector2(le, bo), Color = color });
                vertices.Add(new TextVertex { Position = new Vector2(x0, y1), TexCoord = new Vector2(le, to), Color = color });
                vertices.Add(new TextVertex { Position = new Vector2(x1, y1), TexCoord = new Vector2(ri, to), Color = color });
                vertices.Add(new TextVertex { Position = new Vector2(x1, y0), TexCoord = new Vector2(ri, bo), Color = color });

                x += metrics.Advance * scale;
            }
        }

        private static RenderTexture? GetFontTextureFromScene(Scene.RenderContext context)
        {
            // Access the font texture from the TextRenderer
            var textRenderer = context.View.TextRenderer;
            var field = typeof(TextRenderer).GetField("fontTexture", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(textRenderer) as RenderTexture;
        }

        // Font metrics and constants copied from TextRenderer
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

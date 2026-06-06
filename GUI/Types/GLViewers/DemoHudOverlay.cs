using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Composites a CPU-rendered (Skia) HUD bitmap into the GL framebuffer as an alpha-blended
    /// screen-space quad, so the 3D map shows through the semi-transparent HUD (CS2 demo style).
    /// </summary>
    sealed class DemoHudOverlay : IDisposable
    {
        private readonly RendererContext rendererContext;
        private Shader? shader;
        private RenderTexture? texture;
        private int textureWidth;
        private int textureHeight;
        private int vao;
        private int vertexBuffer;
        private int indexBuffer;
        private bool loaded;

        public DemoHudOverlay(RendererContext rendererContext)
        {
            this.rendererContext = rendererContext;
        }

        private void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;

            shader = rendererContext.ShaderLoader.LoadShader("vrf.ui_texture");

            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out vertexBuffer);
            GL.CreateBuffers(1, out indexBuffer);

            // Two triangles for one quad (left-bottom, left-top, right-top, right-bottom).
            ushort[] indices = [0, 1, 2, 0, 2, 3];
            GL.NamedBufferData(indexBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);

            const int stride = 4 * sizeof(float); // vec2 position + vec2 texcoord
            GL.VertexArrayVertexBuffer(vao, 0, vertexBuffer, 0, stride);
            GL.VertexArrayElementBuffer(vao, indexBuffer);

            var positionLocation = GL.GetAttribLocation(shader.Program, "vPOSITION");
            GL.EnableVertexArrayAttrib(vao, positionLocation);
            GL.VertexArrayAttribFormat(vao, positionLocation, 2, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vao, positionLocation, 0);

            var texCoordLocation = GL.GetAttribLocation(shader.Program, "vTEXCOORD");
            GL.EnableVertexArrayAttrib(vao, texCoordLocation);
            GL.VertexArrayAttribFormat(vao, texCoordLocation, 2, VertexAttribType.Float, false, 2 * sizeof(float));
            GL.VertexArrayAttribBinding(vao, texCoordLocation, 0);
        }

        /// <summary>Uploads a BGRA (non-premultiplied) pixel buffer as the overlay texture.</summary>
        public void Upload(byte[] bgra, int width, int height)
        {
            EnsureLoaded();

            if (texture == null || textureWidth != width || textureHeight != height)
            {
                texture?.Delete();
                texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, 1);
                texture.SetWrapMode(TextureWrapMode.ClampToEdge);
                texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
                GL.TextureStorage2D(texture.Handle, 1, SizedInternalFormat.Rgba8, width, height);
                textureWidth = width;
                textureHeight = height;
            }

            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, bgra);
        }

        /// <summary>Draws the uploaded texture at the given pixel rectangle (top-left origin).</summary>
        public void Render(float x, float y, float screenWidth, float screenHeight)
        {
            if (shader == null || texture == null)
            {
                return;
            }

            var x0 = x;
            var y0 = y;
            var x1 = x + textureWidth;
            var y1 = y + textureHeight;

            // Interleaved position.xy + texcoord.uv. Row 0 of the bitmap is the top, so the
            // top vertices use v=0 and bottom vertices use v=1. Vertex order (top-left, bottom-left,
            // bottom-right, top-right) matches TextRenderer's winding so the quad is not back-face culled.
            float[] vertices =
            [
                x0, y0, 0f, 0f, // top-left
                x0, y1, 0f, 1f, // bottom-left
                x1, y1, 1f, 1f, // bottom-right
                x1, y0, 1f, 0f, // top-right
            ];

            GL.NamedBufferData(vertexBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographicOffCenter(0f, screenWidth, screenHeight, 0f, -100f, 100f));
            shader.SetTexture(0, "uTexture", texture);

            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedShort, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);
        }

        public void Dispose()
        {
            texture?.Delete();

            if (loaded)
            {
                GL.DeleteVertexArray(vao);
                GL.DeleteBuffer(vertexBuffer);
                GL.DeleteBuffer(indexBuffer);
                loaded = false;
            }
        }
    }
}

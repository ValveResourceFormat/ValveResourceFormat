using System.Linq;
using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Morph composite atlas combining all scene meshes' composited result.
    /// </summary>
    public sealed class MorphCompositeAtlas : IDisposable
    {
        private const int InitialFieldSize = 1024;

        private readonly RendererContext renderContext;
        private readonly List<MorphComposite> placed = [];
        private readonly List<MorphComposite> queued = [];
        private Shader? shader;
        private int frameBuffer;
        private int fieldWidth = InitialFieldSize;
        private int fieldHeight = InitialFieldSize;
        private bool clearAll;

        // Shelf packer state: rects fill rows left to right, and a row is as tall as its tallest rect
        private int shelfX;
        private int shelfY;
        private int shelfHeight;

        /// <summary>The atlas texture, created with the first rect.</summary>
        public RenderTexture? Texture { get; private set; }

        /// <summary>Changes whenever a composite's rect is placed or moved, so the draw entries that carry rects can follow.</summary>
        public int LayoutVersion { get; private set; }

        /// <summary>Creates an empty atlas. Nothing is allocated on the GPU until a composite renders.</summary>
        public MorphCompositeAtlas(RendererContext renderContext)
        {
            this.renderContext = renderContext;
        }

        /// <summary>Queues a composite whose weights changed, to be drawn by the next <see cref="Render"/>.</summary>
        public void Queue(MorphComposite composite)
        {
            if (!composite.IsQueued)
            {
                composite.IsQueued = true;
                queued.Add(composite);
            }
        }

        /// <summary>
        /// Gives up a composite's rect for good. The shelves cannot reuse the hole, but the next rect that does not fit
        /// repacks only the live ones, so the space comes back before the atlas grows.
        /// </summary>
        public void Release(MorphComposite composite)
        {
            if (composite.IsQueued)
            {
                queued.Remove(composite);
                composite.IsQueued = false;
            }

            if (composite.IsPlaced)
            {
                placed.Remove(composite);
                composite.IsPlaced = false;
            }
        }

        /// <summary>
        /// Places the queued composites that have no rect yet, then redraws every queued one. Runs after every scene
        /// has updated, since placing one scene's composite can move the rects of all of them.
        /// </summary>
        public void Render()
        {
            if (queued.Count == 0)
            {
                return;
            }

            var needsRepack = false;

            foreach (var composite in queued)
            {
                if (!composite.IsPlaced && !TryPlace(composite))
                {
                    needsRepack = true;
                    break;
                }
            }

            if (needsRepack)
            {
                Repack();
            }

            var texture = Texture;

            if (texture == null || texture.Width != fieldWidth * 2 || texture.Height != fieldHeight)
            {
                texture = CreateTexture();
            }

            Draw(texture);
        }

        private bool TryPlace(MorphComposite composite)
        {
            var (width, height) = (composite.Width, composite.Height);

            if (width > fieldWidth)
            {
                return false;
            }

            if (shelfX + width > fieldWidth)
            {
                shelfY += shelfHeight;
                shelfX = 0;
                shelfHeight = 0;
            }

            if (shelfY + height > fieldHeight)
            {
                return false;
            }

            composite.AtlasX = shelfX;
            composite.AtlasY = shelfY;
            composite.IsPlaced = true;

            shelfX += width;
            shelfHeight = Math.Max(shelfHeight, height);

            placed.Add(composite);
            LayoutVersion++;

            return true;
        }

        // Grows the fields until every placed and queued composite fits, packing the tallest first. Every rect moves,
        // so all of them are drawn again from the weights they keep.
        private void Repack()
        {
            var composites = placed
                .Concat(queued.Where(static composite => !composite.IsPlaced))
                .OrderByDescending(static composite => composite.Height)
                .ThenByDescending(static composite => composite.Width)
                .ToArray();

            fieldWidth = Math.Max(fieldWidth, composites.Max(static composite => composite.Width));

            var maxTextureSize = GL.GetInteger(GetPName.MaxTextureSize);
            var maxFieldWidth = maxTextureSize / 2;
            var maxFieldHeight = maxTextureSize;

            while (true)
            {
                placed.Clear();
                (shelfX, shelfY, shelfHeight) = (0, 0, 0);

                foreach (var composite in composites)
                {
                    composite.IsPlaced = false;
                }

                if (composites.All(TryPlace))
                {
                    break;
                }

                if (fieldWidth >= maxFieldWidth && fieldHeight >= maxFieldHeight)
                {
                    renderContext.Logger.LogWarning("Morph composite atlas is full at {Width}x{Height}, some meshes will not morph", fieldWidth * 2, fieldHeight);
                    break;
                }

                // Keep the fields roughly square so the shelves stay short
                if (fieldHeight > fieldWidth && fieldWidth < maxFieldWidth)
                {
                    fieldWidth = Math.Min(fieldWidth + fieldWidth / 4, maxFieldWidth);
                }
                else
                {
                    fieldHeight = Math.Min(fieldHeight + fieldHeight / 4, maxFieldHeight);
                }
            }

            foreach (var composite in placed)
            {
                Queue(composite);
            }

            clearAll = true;
        }

        private RenderTexture CreateTexture()
        {
            Texture?.Delete();

            var texture = RenderTexture.Create(fieldWidth * 2, fieldHeight, ImageFormat.RGBA16161616F, nameof(MorphCompositeAtlas));
            texture.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetWrapMode(RsTextureAddressMode.Clamp);
            Texture = texture;

            if (frameBuffer == 0)
            {
                frameBuffer = GraphicsDevice.CreateFramebuffer(nameof(MorphCompositeAtlas));
            }

            GL.NamedFramebufferTexture(frameBuffer, FramebufferAttachment.ColorAttachment0, texture.Handle, 0);

            clearAll = true;

            return texture;
        }

        private void Draw(RenderTexture texture)
        {
            shader ??= renderContext.ShaderLoader.LoadShader("morph_composite");

            // Every rect adds its weighted deltas on top of the ones already accumulated, alpha included
            using var _ = GraphicsContext.RenderState.Scope(cullMode: RsCullMode.None, multisampleEnable: false,
                depthTest: false, depthWrite: false,
                blend: true, srcBlend: RsBlendMode.One, dstBlend: RsBlendMode.One);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBuffer);
            GL.Viewport(0, 0, texture.Width, texture.Height);

            if (clearAll)
            {
                GL.ClearColor(0, 0, 0, 0);
                GL.Clear(ClearBufferMask.ColorBufferBit);
            }

            shader.Use();
            shader.SetUniform2("vAtlasFieldSize", new Vector2(fieldWidth, fieldHeight));

            foreach (var composite in queued)
            {
                composite.IsQueued = false;

                if (!composite.IsPlaced)
                {
                    continue;
                }

                if (!clearAll)
                {
                    ClearRect(texture, composite.AtlasX, composite.AtlasY, composite.Width, composite.Height);
                    ClearRect(texture, fieldWidth + composite.AtlasX, composite.AtlasY, composite.Width, composite.Height);
                }

                composite.Draw(shader);
            }

            queued.Clear();
            clearAll = false;
        }

        private static void ClearRect(RenderTexture texture, int x, int y, int width, int height)
            => GL.ClearTexSubImage(texture.Handle, 0, x, y, 0, width, height, 1, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);

        /// <inheritdoc/>
        public void Dispose()
        {
            Texture?.Delete();
            Texture = null;

            if (frameBuffer != 0)
            {
                GL.DeleteFramebuffer(frameBuffer);
                frameBuffer = 0;
            }
        }
    }
}

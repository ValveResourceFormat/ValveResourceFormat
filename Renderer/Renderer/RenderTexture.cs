using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// OpenGL texture object with metadata for dimensions and filtering configuration.
    /// </summary>
    [DebuggerDisplay("{Width}x{Height}x{Depth} mip:{NumMipLevels} ({Target})")]
    public class RenderTexture
    {
        /// <summary>Gets the OpenGL texture target (e.g. Texture2D, TextureCubeMap).</summary>
        public TextureTarget Target { get; }

        /// <summary>Gets the OpenGL texture object handle, or 0 once <see cref="Delete"/> has been called.</summary>
        public int Handle { get; private set; }

        /// <summary>Gets optional spritesheet layout data when the texture is a sprite atlas.</summary>
        public Texture.SpritesheetData? SpriteSheetData { get; }

        /// <summary>Gets the width of the texture in texels.</summary>
        public int Width { get; }

        /// <summary>Gets the height of the texture in texels.</summary>
        public int Height { get; }

        /// <summary>Gets the depth of the texture (number of slices for 3D or array textures).</summary>
        public int Depth { get; }

        /// <summary>Gets the number of mip levels.</summary>
        public int NumMipLevels { get; private set; }

        /// <summary>Gets the average color reflectivity used for environment lighting calculations.</summary>
        public Vector4 Reflectivity { get; internal set; }

        private long bindlessHandle;
        private Dictionary<int, long>? samplerHandles;

        /// <summary>
        /// Gets this texture's bindless handle, creating it and making it resident on first use.
        /// </summary>
        /// <remarks>
        /// A handle freezes the texture's sampler parameters for the rest of its life, so any later
        /// <see cref="SetParameter"/> is a GL error. To vary them afterwards, sample through
        /// <see cref="GetHandle(int)"/> instead.
        /// </remarks>
        public long BindlessHandle
        {
            get
            {
                if (bindlessHandle == 0)
                {
                    bindlessHandle = GL.Arb.GetTextureHandle(Handle);
                    Debug.Assert(bindlessHandle != 0, "Failed to create a bindless handle for this texture.");

                    GL.Arb.MakeTextureHandleResident(bindlessHandle);
                }

                return bindlessHandle;
            }
        }

        /// <summary>
        /// Gets the bindless handle pairing this texture with a sampler object, creating it and making it
        /// resident on first use. Handles are cached per sampler.
        /// </summary>
        /// <param name="sampler">The sampler object to sample this texture through.</param>
        /// <remarks>
        /// The sampler supplies the filtering and wrapping in place of the texture's own, which is how one
        /// texture is read more than one way. It must not be modified after being paired here.
        /// </remarks>
        public long GetHandle(int sampler)
        {
            if (sampler == 0)
            {
                return BindlessHandle;
            }

            samplerHandles ??= [];

            if (!samplerHandles.TryGetValue(sampler, out var handle))
            {
                handle = GL.Arb.GetTextureSamplerHandle(Handle, sampler);
                Debug.Assert(handle != 0, "Failed to create a bindless sampler handle for this texture.");

                GL.Arb.MakeTextureHandleResident(handle);
                samplerHandles.Add(sampler, handle);
            }

            return handle;
        }

        /// <summary>
        /// Gets the baked radiance of each cube map in this array as an L2 spherical harmonic,
        /// 9 coefficients per channel stored planar, 27 per cube map. Null unless the source
        /// texture carried them.
        /// </summary>
        public float[]? RadianceCoefficients { get; }

        RenderTexture(TextureTarget target)
        {
            Target = target;
            GL.CreateTextures(target, 1, out int handle);
            Handle = handle;
        }

        /// <summary>Creates a render texture and populates metadata from the given source texture resource.</summary>
        /// <param name="target">OpenGL texture target.</param>
        /// <param name="data">Source texture resource providing dimensions, mip count, spritesheet data and radiance harmonics.</param>
        public RenderTexture(TextureTarget target, Texture data) : this(target)
        {
            Width = data.Width;
            Height = data.Height;
            Depth = data.Depth;
            NumMipLevels = data.NumMipLevels;
            SpriteSheetData = data.GetSpriteSheetData();
            Reflectivity = data.Reflectivity;
            RadianceCoefficients = data.RadianceCoefficients;
        }

        /// <summary>Creates a render texture with explicit dimension and mip level metadata.</summary>
        /// <param name="target">OpenGL texture target.</param>
        /// <param name="width">Width in texels.</param>
        /// <param name="height">Height in texels.</param>
        /// <param name="depth">Depth or array layer count.</param>
        /// <param name="mipcount">Number of mip levels.</param>
        public RenderTexture(TextureTarget target, int width, int height, int depth, int mipcount)
            : this(target)
        {
            Width = width;
            Height = height;
            Depth = depth;
            NumMipLevels = mipcount;
        }

        /// <summary>Wraps an existing OpenGL texture handle without taking ownership of its storage.</summary>
        /// <param name="handle">Existing OpenGL texture handle.</param>
        /// <param name="target">OpenGL texture target.</param>
        public RenderTexture(int handle, TextureTarget target)
        {
            Handle = handle;
            Target = target;
        }

        /// <summary>Creates a 2D texture with immutable storage, optionally allocating a reduced mip chain sized by <see cref="MaxMipCount"/>.</summary>
        /// <param name="width">Texture width in texels.</param>
        /// <param name="height">Texture height in texels.</param>
        /// <param name="format">Internal pixel format.</param>
        /// <param name="mips">When <see langword="true"/>, allocates a reduced mip chain (see <see cref="MaxMipCount"/>) rather than a single level.</param>
        /// <returns>The newly created render texture.</returns>
        public static RenderTexture Create(int width, int height, ImageFormat format = ImageFormat.RGBA8888, bool mips = false)
        {
            var mipCount = mips
                ? MaxMipCount(width, height)
                : 1;

            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, mipCount);
            GL.TextureStorage2D(texture.Handle, mipCount, format.ToGLSizedInternalFormat(), width, height);
            return texture;
        }

        /// <summary>Creates a 2D texture with immutable storage and an explicit mip count.</summary>
        /// <param name="width">Texture width in texels.</param>
        /// <param name="height">Texture height in texels.</param>
        /// <param name="format">Internal pixel format.</param>
        /// <param name="mipCount">Number of mip levels to allocate.</param>
        /// <returns>The newly created render texture.</returns>
        public static RenderTexture Create(int width, int height, ImageFormat format, int mipCount)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, mipCount);
            GL.TextureStorage2D(texture.Handle, mipCount, format.ToGLSizedInternalFormat(), width, height);
            return texture;
        }

        /// <summary>Creates a texture view that reinterprets a subrange of this texture's storage.</summary>
        /// <param name="format">The reinterpreted pixel format for the view.</param>
        /// <param name="minLevel">First mip level visible through the view.</param>
        /// <param name="numLevels">Number of mip levels visible through the view.</param>
        /// <param name="minLayer">First array layer visible through the view.</param>
        /// <param name="numLayers">Number of array layers visible through the view.</param>
        /// <returns>A new <see cref="RenderTexture"/> wrapping the view.</returns>
        public RenderTexture CreateView(ImageFormat format, int minLevel = 0, int numLevels = 1, int minLayer = 0, int numLayers = 1)
        {
            var view = new RenderTexture(GL.GenTexture(), Target);
            GL.TextureView(view.Handle, Target, Handle, (PixelInternalFormat)format.ToGLSizedInternalFormat(), minLevel, numLevels, minLayer, numLayers);
            return view;
        }

        /// <summary>Sets the wrap mode for all relevant texture dimensions.</summary>
        /// <param name="wrap">The wrap mode to apply.</param>
        public void SetWrapMode(TextureWrapMode wrap)
        {
            SetParameter(TextureParameterName.TextureWrapS, (int)wrap);

            if (Height > 1)
            {
                SetParameter(TextureParameterName.TextureWrapT, (int)wrap);
            }

            if (Depth > 1)
            {
                SetParameter(TextureParameterName.TextureWrapR, (int)wrap);
            }
        }

        /// <summary>Sets the minification and magnification filters.</summary>
        /// <param name="min">Minification filter.</param>
        /// <param name="mag">Magnification filter.</param>
        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            SetParameter(TextureParameterName.TextureMinFilter, (int)min);
            SetParameter(TextureParameterName.TextureMagFilter, (int)mag);
        }

        /// <summary>Sets the base and maximum mip level accessible through this texture.</summary>
        /// <param name="baseLevel">Lowest mip level index.</param>
        /// <param name="maxLevel">Highest mip level index.</param>
        public void SetBaseMaxLevel(int baseLevel, int maxLevel)
        {
            SetParameter(TextureParameterName.TextureBaseLevel, baseLevel);
            SetParameter(TextureParameterName.TextureMaxLevel, maxLevel);
        }

        /// <summary>Sets a single integer texture parameter.</summary>
        /// <param name="parameter">The parameter name to set.</param>
        /// <param name="value">The integer value to assign.</param>
        /// <remarks>Only valid until <see cref="BindlessHandle"/> freezes this texture's parameters.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetParameter(TextureParameterName parameter, int value)
        {
            Debug.Assert(bindlessHandle == 0, $"Setting {parameter} on a texture that already has a bindless handle is a GL error, its parameters are frozen. Set it before first use, or sample through {nameof(GetHandle)} instead.");

            GL.TextureParameter(Handle, parameter, value);
        }

        /// <summary>Assigns a debug label to the OpenGL texture object.</summary>
        /// <param name="label">Label string visible in graphics debuggers.</param>
        public void SetLabel(string label)
        {
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, Handle, label.Length, label);
        }

        /// <summary>Deletes the underlying OpenGL texture object.</summary>
        /// <remarks>
        /// Every handle taken from this texture is made non-resident first. A texture deleted while one of its
        /// handles is still resident keeps its storage alive, so skipping this leaks the whole texture.
        /// </remarks>
        public void Delete()
        {
            if (bindlessHandle != 0)
            {
                GL.Arb.MakeTextureHandleNonResident(bindlessHandle);
                bindlessHandle = 0;
            }

            if (samplerHandles != null)
            {
                foreach (var handle in samplerHandles.Values)
                {
                    GL.Arb.MakeTextureHandleNonResident(handle);
                }

                samplerHandles = null;
            }

            GL.DeleteTexture(Handle);
            Handle = 0;
        }

        /// <summary>Calculates a reasonable mip count for a texture of the given dimensions.</summary>
        /// <param name="width">Texture width in texels.</param>
        /// <param name="height">Texture height in texels.</param>
        /// <returns>Number of mip levels to use.</returns>
        public static int MaxMipCount(int width, int height)
        {
            return Math.Max((int)MathF.Log(MathF.Max(width, height), 2) - 2, 1);
        }

        /// <summary>Attaches the specified mip level of this texture to a framebuffer attachment point.</summary>
        /// <param name="framebuffer">Target framebuffer.</param>
        /// <param name="attachment">Attachment point (e.g. color attachment 0, depth).</param>
        /// <param name="mipLevel">Mip level to attach.</param>
        public void AttachToFramebuffer(Framebuffer framebuffer, FramebufferAttachment attachment, int mipLevel)
        {
            if (mipLevel < 0 || mipLevel >= NumMipLevels)
            {
                throw new ArgumentOutOfRangeException(nameof(mipLevel), $"Mip level {mipLevel} is out of range for attachment with {NumMipLevels} mips.");
            }

            GL.NamedFramebufferTexture(framebuffer.FboHandle, attachment, Handle, mipLevel);
        }
    }
}

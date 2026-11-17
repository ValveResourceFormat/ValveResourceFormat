using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
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

        /// <summary>The streaming state of a texture whose mips arrive after load, null for any other.</summary>
        internal Materials.StreamedTexture? Stream { get; set; }

        /// <summary>Gets optional spritesheet layout data when the texture is a sprite atlas.</summary>
        public Texture.SpritesheetData? SpriteSheetData { get; }

        /// <summary>Gets the width of the texture in texels.</summary>
        public int Width { get; }

        /// <summary>Gets the height of the texture in texels.</summary>
        public int Height { get; }

        /// <summary>Gets the depth of the texture (number of slices for 3D or array textures).</summary>
        public int Depth { get; }

        /// <summary>Gets how far, in texels, a motion vector sheet displaces per frame at full deflection.</summary>
        public short MotionVectorsMaxDistance { get; }

        /// <summary>Gets the number of mip levels.</summary>
        public int NumMipLevels { get; private set; }

        /// <summary>Gets the average color reflectivity used for environment lighting calculations.</summary>
        public Vector4 Reflectivity { get; internal set; }

        /// <summary>
        /// Gets the baked radiance of each cube map in this array as an L2 spherical harmonic,
        /// 9 coefficients per channel stored planar, 27 per cube map. Null unless the source
        /// texture carried them.
        /// </summary>
        public float[]? RadianceCoefficients { get; }

        private long bindlessHandle;
        private Dictionary<int, long>? samplerHandles;

        // A handle freezes the parameters of the texture object it was made from, for good
        private bool parametersFrozen;

        // View over the mips in the base and max level, which the handles sample instead of a 2D texture
        // whose levels have to change after its parameters froze
        private int levelView;

        /// <summary>Gets a number that changes whenever <see cref="GetBindlessHandle"/> would return a different handle.</summary>
        public uint HandleVersion { get; private set; }

        RenderTexture(TextureTarget target, string label)
        {
            Target = target;
            Handle = GraphicsDevice.CreateTexture(target, label);
        }

        /// <summary>Creates a render texture and populates metadata from the given source texture resource.</summary>
        /// <param name="target">OpenGL texture target.</param>
        /// <param name="data">Source texture resource providing dimensions, mip count, spritesheet data and radiance harmonics.</param>
        /// <param name="label">Label string visible in graphics debuggers.</param>
        public RenderTexture(TextureTarget target, Texture data, string label) : this(target, label)
        {
            Width = data.Width;
            Height = data.Height;
            Depth = data.Depth;
            MotionVectorsMaxDistance = data.MotionVectorsMaxDistance;
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
        /// <param name="label">Label string visible in graphics debuggers.</param>
        public RenderTexture(TextureTarget target, int width, int height, int depth, int mipcount, string label)
            : this(target, label)
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
        /// <param name="label">Label string visible in graphics debuggers.</param>
        /// <param name="mips">When <see langword="true"/>, allocates a reduced mip chain (see <see cref="MaxMipCount"/>) rather than a single level.</param>
        /// <returns>The newly created render texture.</returns>
        public static RenderTexture Create(int width, int height, ImageFormat format, string label, bool mips = false)
        {
            var mipCount = mips
                ? MaxMipCount(width, height)
                : 1;

            return Create(width, height, format, mipCount, label);
        }

        /// <summary>Creates a 2D texture with immutable storage and an explicit mip count.</summary>
        /// <param name="width">Texture width in texels.</param>
        /// <param name="height">Texture height in texels.</param>
        /// <param name="format">Internal pixel format.</param>
        /// <param name="mipCount">Number of mip levels to allocate.</param>
        /// <param name="label">Label string visible in graphics debuggers.</param>
        /// <returns>The newly created render texture.</returns>
        public static RenderTexture Create(int width, int height, ImageFormat format, int mipCount, string label)
        {
            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, mipCount, label);
            GL.TextureStorage2D(texture.Handle, mipCount, format.ToGLSizedInternalFormat(), width, height);
            return texture;
        }

        /// <summary>Creates a texture with immutable three dimensional storage.</summary>
        /// <returns>The newly created render texture.</returns>
        public static RenderTexture Create3D(TextureTarget target, int width, int height, int depth, ImageFormat format, int mipCount, string label, bool srgb = false)
        {
            Debug.Assert(target is TextureTarget.Texture3D or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray,
                $"{target} does not take three dimensional storage.");

            var texture = new RenderTexture(target, width, height, depth, mipCount, label);
            GL.TextureStorage3D(texture.Handle, mipCount, format.ToGLSizedInternalFormat(srgb), width, height, depth);
            return texture;
        }

        /// <summary>Creates a texture view that reinterprets a subrange of this texture's storage.</summary>
        /// <param name="format">The reinterpreted pixel format for the view.</param>
        /// <param name="minLevel">First mip level visible through the view.</param>
        /// <param name="numLevels">Number of mip levels visible through the view.</param>
        /// <param name="minLayer">First array layer visible through the view.</param>
        /// <param name="numLayers">Number of array layers visible through the view.</param>
        /// <param name="label">Label string visible in graphics debuggers.</param>
        /// <returns>A new <see cref="RenderTexture"/> wrapping the view.</returns>
        public RenderTexture CreateView(ImageFormat format, string label, int minLevel = 0, int numLevels = 1, int minLayer = 0, int numLayers = 1)
        {
            var handle = GraphicsDevice.CreateTextureView(Handle, Target, format, minLevel, numLevels, minLayer, numLayers, label);

            return new RenderTexture(handle, Target);
        }

        // Sampler state set through the methods below is remembered, so ReplaceHandle can reapply it;
        // parameters do not carry over to a replacement texture object
        private (TextureMinFilter Min, TextureMagFilter Mag)? filtering;
        private (RsTextureAddressMode S, RsTextureAddressMode T, RsTextureAddressMode R)? wrapMode;
        private (int BaseLevel, int MaxLevel)? baseMaxLevel;
        private float maxAnisotropy;

        /// <summary>Sets one addressing mode for all relevant texture dimensions.</summary>
        /// <param name="mode">The addressing mode to apply.</param>
        public void SetWrapMode(RsTextureAddressMode mode) => SetWrapMode(mode, mode, mode);

        /// <summary>Sets the addressing mode per texture dimension, skipping the ones this texture does not have.</summary>
        /// <param name="s">Addressing mode across the width.</param>
        /// <param name="t">Addressing mode across the height.</param>
        /// <param name="r">Addressing mode across the depth.</param>
        public void SetWrapMode(RsTextureAddressMode s, RsTextureAddressMode t, RsTextureAddressMode r)
        {
            // A cached texture is set up again by each user, after a handle may have frozen it
            if (parametersFrozen && wrapMode == (s, t, r))
            {
                return;
            }

            wrapMode = (s, t, r);

            SetParameter(TextureParameterName.TextureWrapS, (int)s.ToGLTextureWrapMode());

            if (Height > 1)
            {
                SetParameter(TextureParameterName.TextureWrapT, (int)t.ToGLTextureWrapMode());
            }

            if (Depth > 1)
            {
                SetParameter(TextureParameterName.TextureWrapR, (int)r.ToGLTextureWrapMode());
            }
        }

        /// <summary>Sets the minification and magnification filters.</summary>
        /// <param name="min">Minification filter.</param>
        /// <param name="mag">Magnification filter.</param>
        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            if (parametersFrozen && filtering == (min, mag))
            {
                return;
            }

            filtering = (min, mag);

            SetParameter(TextureParameterName.TextureMinFilter, (int)min);
            SetParameter(TextureParameterName.TextureMagFilter, (int)mag);
        }

        /// <summary>Sets the base and maximum mip level accessible through this texture.</summary>
        /// <param name="baseLevel">Lowest mip level index.</param>
        /// <param name="maxLevel">Highest mip level index.</param>
        public void SetBaseMaxLevel(int baseLevel, int maxLevel)
        {
            if (baseMaxLevel == (baseLevel, maxLevel))
            {
                return;
            }

            baseMaxLevel = (baseLevel, maxLevel);

            if (UsesLevelView)
            {
                ReleaseBindlessHandles();
                HandleVersion++;

                if (parametersFrozen)
                {
                    return;
                }
            }

            SetParameter(TextureParameterName.TextureBaseLevel, baseLevel);
            SetParameter(TextureParameterName.TextureMaxLevel, maxLevel);
        }

        // Streamed textures change their levels while they are being sampled
        private bool UsesLevelView => GLEnvironment.BindlessTextures && Target == TextureTarget.Texture2D && baseMaxLevel != null;

        /// <summary>Sets the maximum anisotropic filtering level.</summary>
        /// <param name="anisotropy">Maximum anisotropy, typically <see cref="GLEnvironment"/>'s supported maximum.</param>
        public void SetMaxAnisotropy(float anisotropy)
        {
            maxAnisotropy = anisotropy;

            GL.TextureParameter(Handle, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, anisotropy);
        }

        /// <summary>Sets a single integer texture parameter.</summary>
        /// <param name="parameter">The parameter name to set.</param>
        /// <param name="value">The integer value to assign.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetParameter(TextureParameterName parameter, int value)
        {
            Debug.Assert(!parametersFrozen,
                "A bindless handle freezes the texture's parameters, set them before it is first sampled.");

            GL.TextureParameter(Handle, parameter, value);
        }

        /// <summary>
        /// This texture's bindless handle, created and made resident on first use. A handle cannot be freed
        /// and freezes the texture's parameters, so only ask for one you are about to sample through.
        /// </summary>
        /// <param name="sampler">A sampler object to read the texture through, or 0 for its own parameters.</param>
        public long GetBindlessHandle(int sampler = 0)
        {
            if (sampler == 0 && bindlessHandle != 0)
            {
                return bindlessHandle;
            }

            if (sampler != 0 && samplerHandles != null && samplerHandles.TryGetValue(sampler, out var cached))
            {
                return cached;
            }

            var source = Handle;

            if (UsesLevelView)
            {
                source = EnsureLevelView();
            }
            else
            {
                parametersFrozen = true;
            }

            if (sampler == 0)
            {
                return bindlessHandle = MakeResident(GL.Arb.GetTextureHandle(source));
            }

            var handle = MakeResident(GL.Arb.GetTextureSamplerHandle(source, sampler));
            samplerHandles ??= [];
            samplerHandles.Add(sampler, handle);

            return handle;
        }

        private int EnsureLevelView()
        {
            if (levelView != 0)
            {
                return levelView;
            }

            var (baseLevel, maxLevel) = baseMaxLevel!.Value;
            maxLevel = Math.Min(maxLevel, NumMipLevels - 1);

            GL.GetTextureLevelParameter(Handle, 0, GetTextureParameter.TextureInternalFormat, out int internalFormat);

            // A view needs a name without a target yet, which only the non-DSA path hands out
            levelView = GL.GenTexture();
            GL.TextureView(levelView, Target, Handle, (PixelInternalFormat)internalFormat, baseLevel, maxLevel - baseLevel + 1, 0, 1);

            if (filtering is { } filter)
            {
                GL.TextureParameter(levelView, TextureParameterName.TextureMinFilter, (int)filter.Min);
                GL.TextureParameter(levelView, TextureParameterName.TextureMagFilter, (int)filter.Mag);
            }

            if (wrapMode is { } wrap)
            {
                GL.TextureParameter(levelView, TextureParameterName.TextureWrapS, (int)wrap.S.ToGLTextureWrapMode());
                GL.TextureParameter(levelView, TextureParameterName.TextureWrapT, (int)wrap.T.ToGLTextureWrapMode());
            }

            if (maxAnisotropy > 0f)
            {
                GL.TextureParameter(levelView, (TextureParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, maxAnisotropy);
            }

            return levelView;
        }

        private static long MakeResident(long handle)
        {
            Debug.Assert(handle != 0);

            GL.Arb.MakeTextureHandleResident(handle);
            return handle;
        }

        private void ReleaseBindlessHandles()
        {
            ReleaseResidentHandles();

            if (levelView != 0)
            {
                GL.DeleteTexture(levelView);
                levelView = 0;
            }
        }

        private void ReleaseResidentHandles()
        {
            if (bindlessHandle != 0)
            {
                GL.Arb.MakeTextureHandleNonResident(bindlessHandle);
                bindlessHandle = 0;
            }

            if (samplerHandles == null)
            {
                return;
            }

            foreach (var handle in samplerHandles.Values)
            {
                GL.Arb.MakeTextureHandleNonResident(handle);
            }

            samplerHandles = null;
        }

        // Raw SetParameter writes are not remembered and do not survive the swap.
        internal void ReplaceHandle(int newHandle, int numMipLevels)
        {
            ReleaseBindlessHandles();
            HandleVersion++;
            parametersFrozen = false;

            GL.DeleteTexture(Handle);
            Handle = newHandle;
            NumMipLevels = numMipLevels;

            if (filtering is { } filter)
            {
                SetParameter(TextureParameterName.TextureMinFilter, (int)filter.Min);
                SetParameter(TextureParameterName.TextureMagFilter, (int)filter.Mag);
            }

            if (wrapMode is { } wrap)
            {
                SetWrapMode(wrap.S, wrap.T, wrap.R);
            }

            if (baseMaxLevel is { } levels)
            {
                SetParameter(TextureParameterName.TextureBaseLevel, levels.BaseLevel);
                SetParameter(TextureParameterName.TextureMaxLevel, levels.MaxLevel);
            }

            if (maxAnisotropy > 0f)
            {
                SetMaxAnisotropy(maxAnisotropy);
            }
        }

        /// <summary>Deletes the underlying OpenGL texture object.</summary>
        public void Delete()
        {
            ReleaseBindlessHandles();

            GL.DeleteTexture(Handle);
            Handle = 0;
        }

        /// <summary>Calculates a reasonable mip count for a texture of the given dimensions.</summary>
        /// <param name="width">Texture width in texels.</param>
        /// <param name="height">Texture height in texels.</param>
        /// <returns>Number of mip levels to use.</returns>
        public static int MaxMipCount(int width, int height)
        {
            return Math.Max(int.Log2(Math.Max(width, height)) - 2, 1);
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

    /// <summary>
    /// OpenGL sampler object: the filtering and addressing state a texture is read with, overriding
    /// the parameters set on the texture itself for the unit it is bound to.
    /// </summary>
    public sealed class Sampler
    {
        /// <summary>Gets the OpenGL sampler object handle.</summary>
        public int Handle { get; }

        /// <summary>Creates a sampler with default state.</summary>
        /// <param name="label">Label string visible in graphics debuggers.</param>
        public Sampler(string label)
        {
            Handle = GraphicsDevice.CreateSampler(label);
        }

        /// <summary>Sets the addressing mode across the width and height.</summary>
        /// <param name="s">Addressing mode across the width.</param>
        /// <param name="t">Addressing mode across the height.</param>
        public void SetWrapMode(RsTextureAddressMode s, RsTextureAddressMode t)
        {
            SetParameter(SamplerParameterName.TextureWrapS, (int)s.ToGLTextureWrapMode());
            SetParameter(SamplerParameterName.TextureWrapT, (int)t.ToGLTextureWrapMode());
        }

        /// <summary>Sets the minification and magnification filters.</summary>
        /// <param name="min">Minification filter.</param>
        /// <param name="mag">Magnification filter.</param>
        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            SetParameter(SamplerParameterName.TextureMinFilter, (int)min);
            SetParameter(SamplerParameterName.TextureMagFilter, (int)mag);
        }

        /// <summary>Sets how many samples anisotropic filtering may take.</summary>
        /// <param name="maxAnisotropy">Maximum anisotropy, clamped by the driver to what it supports.</param>
        public void SetMaxAnisotropy(float maxAnisotropy)
        {
            GL.SamplerParameter(Handle, (SamplerParameterName)ExtTextureFilterAnisotropic.TextureMaxAnisotropyExt, maxAnisotropy);
        }

        private void SetParameter(SamplerParameterName parameter, int value)
            => GL.SamplerParameter(Handle, parameter, value);
    }
}

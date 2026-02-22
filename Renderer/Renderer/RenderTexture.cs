using System.Diagnostics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// OpenGL texture object with metadata for dimensions and filtering configuration.
    /// </summary>
    [DebuggerDisplay("{Width}x{Height}x{Depth} mip:{NumMipLevels} srgb:{Srgb}")]
    public class RenderTexture
    {
        public TextureTarget Target { get; }
        public int Handle { get; }

        public Texture.SpritesheetData? SpriteSheetData { get; }

        public int Width { get; }
        public int Height { get; }
        public int Depth { get; }
        public int NumMipLevels { get; }

        public Vector4 Reflectivity { get; internal set; }

        RenderTexture(TextureTarget target)
        {
            Target = target;
            GL.CreateTextures(target, 1, out int handle);
            Handle = handle;
        }

        public RenderTexture(TextureTarget target, Texture data) : this(target)
        {
            Width = data.Width;
            Height = data.Height;
            Depth = data.Depth;
            NumMipLevels = data.NumMipLevels;
            SpriteSheetData = data.GetSpriteSheetData();
            Reflectivity = data.Reflectivity;
        }

        public RenderTexture(TextureTarget target, int width, int height, int depth, int mipcount)
            : this(target)
        {
            Width = width;
            Height = height;
            Depth = depth;
            NumMipLevels = mipcount;
        }

        public RenderTexture(int handle, TextureTarget target)
        {
            Handle = handle;
            Target = target;
        }

        public static RenderTexture Create(int width, int height, SizedInternalFormat format = SizedInternalFormat.Rgba8, bool mips = false)
        {
            var mipCount = mips
                ? MaxMipCount(width, height)
                : 1;

            var texture = new RenderTexture(TextureTarget.Texture2D, width, height, 1, mipCount);
            GL.TextureStorage2D(texture.Handle, mipCount, format, width, height);
            return texture;
        }

        public RenderTexture CreateView(PixelInternalFormat internalFormat, int minLevel = 0, int numLevels = 1, int minLayer = 0, int numLayers = 1)
        {
            var view = new RenderTexture(GL.GenTexture(), Target);
            GL.TextureView(view.Handle, Target, Handle, internalFormat, minLevel, numLevels, minLayer, numLayers);
            return view;
        }

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

        public void SetFiltering(TextureMinFilter min, TextureMagFilter mag)
        {
            SetParameter(TextureParameterName.TextureMinFilter, (int)min);
            SetParameter(TextureParameterName.TextureMagFilter, (int)mag);
        }

        public void SetBaseMaxLevel(int baseLevel, int maxLevel)
        {
            SetParameter(TextureParameterName.TextureBaseLevel, baseLevel);
            SetParameter(TextureParameterName.TextureMaxLevel, maxLevel);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetParameter(TextureParameterName parameter, int value)
            => GL.TextureParameter(Handle, parameter, value);

        public void SetLabel(string label)
        {
            GL.ObjectLabel(ObjectLabelIdentifier.Texture, Handle, label.Length, label);
        }

        public void Delete()
        {
            GL.DeleteTexture(Handle);
        }

        public static int MaxMipCount(int width, int height)
        {
            return Math.Max((int)MathF.Log(MathF.Max(width, height), 2) - 2, 1);
        }

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

using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// OpenGL framebuffer object with color and depth attachments.
/// </summary>
public class Framebuffer
{
    /// <summary>
    /// OpenGL framebuffer object handle.
    /// </summary>
    public int FboHandle { get; }

    /// <summary>
    /// Width of the framebuffer in pixels.
    /// </summary>
    public int Width { get; protected set; }

    /// <summary>
    /// Height of the framebuffer in pixels.
    /// </summary>
    public int Height { get; protected set; }

    /// <summary>
    /// Returns <see langword="true"/> if both <see cref="Width"/> and <see cref="Height"/> are greater than zero.
    /// </summary>
    public bool HasValidDimensions() => Width > 0 && Height > 0;

    /// <summary>
    /// Number of mip levels for color attachments.
    /// </summary>
    public int NumMips { get; set; } = 1;

    /// <summary>
    /// Number of MSAA samples; 0 means no multisampling.
    /// </summary>
    public int NumSamples { get; set; }

    /// <summary>
    /// Texture target used for attachments (<see cref="TextureTarget.Texture2D"/> or <see cref="TextureTarget.Texture2DMultisample"/>).
    /// </summary>
    public TextureTarget Target { get; protected set; }

    /// <summary>
    /// Color attachment texture, or <see langword="null"/> if none.
    /// </summary>
    public RenderTexture? Color { get; protected set; }

    /// <summary>
    /// Depth attachment texture, or <see langword="null"/> if none.
    /// </summary>
    public RenderTexture? Depth { get; protected set; }

    /// <summary>
    /// Stencil view texture, or <see langword="null"/> if none.
    /// </summary>
    public RenderTexture? Stencil { get; protected set; }

    // Maybe these can be in texture
    /// <summary>
    /// Pixel format specification for the color attachment.
    /// </summary>
    public AttachmentFormat? ColorFormat { get; protected set; }

    /// <summary>
    /// Pixel format specification for the depth attachment.
    /// </summary>
    public DepthAttachmentFormat? DepthFormat { get; protected set; }

    /// <summary>
    /// Framebuffer completeness status set after <see cref="Initialize"/> is called.
    /// </summary>
    public FramebufferErrorCode InitialStatus { get; private set; } = FramebufferErrorCode.FramebufferUndefined;

    /// <summary>
    /// The framebuffer target this object was last bound to.
    /// </summary>
    public FramebufferTarget TargetState { get; set; } = FramebufferTarget.Framebuffer;

    /// <summary>
    /// Binds this framebuffer to the specified target.
    /// </summary>
    public void Bind(FramebufferTarget targetState)
    {
        TargetState = targetState;
        GL.BindFramebuffer(targetState, FboHandle);
    }

    #region Render state
    /// <summary>
    /// Color used to clear the color attachment.
    /// </summary>
    public Color4 ClearColor { get; set; } = Color4.Black; // https://gpuopen.com/learn/rdna-performance-guide/#clears

    /// <summary>
    /// Buffer bits cleared when <see cref="BindAndClear"/> is called.
    /// </summary>
    public ClearBufferMask ClearMask { get; set; } = ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit;
    #endregion

    /// <summary>
    /// Binds this framebuffer and clears it with <see cref="ClearColor"/> and <see cref="ClearMask"/>.
    /// </summary>
    public void BindAndClear(FramebufferTarget targetState = FramebufferTarget.Framebuffer)
    {
        Bind(targetState);
        GL.ClearColor(ClearColor);
        GL.Clear(ClearMask);
    }

    /// <summary>
    /// Creates a new named OpenGL framebuffer object.
    /// </summary>
    /// <param name="name">Debug label applied to the framebuffer object.</param>
    public Framebuffer(string name)
    {
        GL.CreateFramebuffers(1, out int handle);
        GL.ObjectLabel(ObjectLabelIdentifier.Framebuffer, handle, name.Length, name);
        FboHandle = handle;
    }

    #region Default OpenGL Framebuffer instance, and equality checks
    Framebuffer(int fboHandle)
    {
        FboHandle = fboHandle;
        InitialStatus = FramebufferErrorCode.FramebufferComplete;
    }
    /// <summary>
    /// Creates a <see cref="Framebuffer"/> instance wrapping the default OpenGL framebuffer (handle 0).
    /// </summary>
    public static Framebuffer GLDefaultFramebuffer => new(fboHandle: 0);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Framebuffer other && other.FboHandle == FboHandle;

    /// <inheritdoc/>
    public override int GetHashCode() => FboHandle.GetHashCode();

    /// <summary>
    /// Returns <see langword="true"/> if both framebuffers wrap the same OpenGL handle.
    /// </summary>
    public static bool operator ==(Framebuffer? left, Framebuffer? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the framebuffers wrap different OpenGL handles.
    /// </summary>
    public static bool operator !=(Framebuffer? left, Framebuffer? right) => !(left == right);

    #endregion

    /// <summary>
    /// Color attachment pixel format and type specification.
    /// </summary>
    public record class AttachmentFormat(PixelInternalFormat InternalFormat, PixelFormat PixelFormat, PixelType PixelType);

    /// <summary>
    /// Depth attachment pixel format and type specification.
    /// </summary>
    public record class DepthAttachmentFormat(PixelInternalFormat InternalFormat, PixelType PixelType)
    {
        /// <summary>
        /// 16-bit unsigned integer depth format.
        /// </summary>
        public static readonly DepthAttachmentFormat Depth16 = new(PixelInternalFormat.DepthComponent16, PixelType.UnsignedShort);

        /// <summary>
        /// 32-bit floating-point depth format.
        /// </summary>
        public static readonly DepthAttachmentFormat Depth32F = new(PixelInternalFormat.DepthComponent32f, PixelType.Float);

        /// <summary>
        /// 32-bit floating-point depth with 8-bit stencil format.
        /// </summary>
        public static readonly DepthAttachmentFormat Depth32FStencil8 = new(PixelInternalFormat.Depth32fStencil8, PixelType.Float32UnsignedInt248Rev);

        /// <summary>
        /// Implicitly converts this depth format to a generic <see cref="AttachmentFormat"/>.
        /// </summary>
        public static implicit operator AttachmentFormat(DepthAttachmentFormat depthFormat) => depthFormat.ToAttachmentFormat();

        /// <summary>
        /// Converts this depth format to a generic <see cref="AttachmentFormat"/>.
        /// </summary>
        public AttachmentFormat ToAttachmentFormat()
        {
            return new(InternalFormat, PixelFormat.DepthComponent, PixelType);
        }
    }

    /// <summary>
    /// Creates and configures a framebuffer without allocating GPU attachments; call <see cref="Initialize"/> to allocate.
    /// </summary>
    /// <param name="name">Debug label for the framebuffer.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="msaa">Number of MSAA samples; 0 disables multisampling.</param>
    /// <param name="colorFormat">Color attachment format, or <see langword="null"/> for depth-only.</param>
    /// <param name="depthFormat">Depth attachment format, or <see langword="null"/> for color-only.</param>
    public static Framebuffer Prepare(string name, int width, int height, int msaa, AttachmentFormat? colorFormat, DepthAttachmentFormat? depthFormat)
    {
        var fbo = new Framebuffer(name)
        {
            NumSamples = msaa,
            Target = msaa > 0 ? TextureTarget.Texture2DMultisample : TextureTarget.Texture2D,
            ColorFormat = colorFormat,
            DepthFormat = depthFormat,
            Width = width,
            Height = height,
        };

        return fbo;
    }

    /// <summary>
    /// Allocates GPU textures for all attachments and checks framebuffer completeness.
    /// </summary>
    /// <returns>The OpenGL framebuffer completeness status code.</returns>
    public FramebufferErrorCode Initialize()
    {
        if (Target == 0)
        {
            throw new InvalidOperationException("Framebuffer target is not set");
        }

        if (ColorFormat == null && DepthFormat == null)
        {
            throw new InvalidOperationException("Framebuffer has no attachments");
        }

        if (!HasValidDimensions())
        {
            throw new InvalidOperationException("Framebuffer has invalid sizes: " + Width + "x" + Height);
        }

        if (InitialStatus != FramebufferErrorCode.FramebufferUndefined)
        {
            throw new InvalidOperationException("Framebuffer has already been initialized");
        }

        CreateAttachments();

        var fboTarget = FramebufferTarget.Framebuffer;
        Bind(fboTarget);

        InitialStatus = GL.CheckFramebufferStatus(fboTarget);
        return InitialStatus;
    }

    /// <summary>
    /// Resizes the framebuffer and changes the MSAA sample count, recreating attachments if anything changed.
    /// </summary>
    public void Resize(int width, int height, int msaa)
    {
        if (width == Width && height == Height && msaa == NumSamples)
        {
            return;
        }

        NumSamples = msaa;
        Resize(width, height);
    }

    /// <summary>
    /// Resizes the framebuffer, recreating attachments if dimensions changed.
    /// </summary>
    /// <returns><see langword="true"/> if the dimensions changed and attachments were recreated.</returns>
    public bool Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return false;
        }

        Width = width;
        Height = height;
        CreateAttachments();
        return true;
    }

    private void CreateAttachments()
    {
        Color?.Delete();
        Depth?.Delete();
        Stencil?.Delete();

        var (width, height) = (Width, Height);

        if (ColorFormat != null)
        {
            Color = CreateAttachment(ColorFormat, width, height, NumMips);
            Color.SetLabel("FramebufferColor");
            Color.AttachToFramebuffer(this, FramebufferAttachment.ColorAttachment0, 0);
        }

        if (DepthFormat != null)
        {
            Depth = CreateAttachment(DepthFormat, width, height);
            Depth.SetLabel("FramebufferDepth");
            Depth.AttachToFramebuffer(this, FramebufferAttachment.DepthAttachment, 0);

            if (DepthFormat == DepthAttachmentFormat.Depth32FStencil8)
            {
                Depth.AttachToFramebuffer(this, FramebufferAttachment.DepthStencilAttachment, 0);

                // Create stencil view
                Stencil = Depth.CreateView(DepthFormat.InternalFormat);

                Stencil.SetLabel("FramebufferStencil");
                Stencil.SetBaseMaxLevel(0, 0);
                GL.TextureParameter(Stencil.Handle, TextureParameterName.DepthStencilTextureMode, (int)DepthStencilTextureMode.StencilIndex);
            }
        }
    }

    private RenderTexture CreateAttachment(AttachmentFormat format, int width, int height, int numMips = 1)
    {
        var attachment = new RenderTexture(Target, width, height, 1, numMips);
        var mipCount = Math.Min(RenderTexture.MaxMipCount(width, height), attachment.NumMipLevels);

        if (Target == TextureTarget.Texture2DMultisample)
        {
            if (mipCount > 1)
            {
                throw new InvalidOperationException("Multisample textures do not support mipmaps");
            }

            GL.TextureStorage2DMultisample(attachment.Handle, NumSamples, (SizedInternalFormat)format.InternalFormat, width, height, fixedsamplelocations: false);
        }
        else
        {
            GL.TextureStorage2D(attachment.Handle, mipCount, (SizedInternalFormat)format.InternalFormat, width, height);
        }

        attachment.SetBaseMaxLevel(0, mipCount - 1);
        return attachment;
    }

    /// <summary>
    /// Changes the attachment formats and recreates the GPU attachments at the current dimensions.
    /// </summary>
    public void ChangeFormat(AttachmentFormat? colorFormat, DepthAttachmentFormat? depthFormat, FramebufferAttachment? framebufferAttachment = null)
    {
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;

        Resize(Width, Height);
    }

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> if the framebuffer is not complete.
    /// </summary>
    public void CheckStatus_ThrowIfIncomplete(string name = "")
    {
        if (InitialStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Fbo '{name} failed to initialize with error: {InitialStatus}");
        }
    }

    /// <summary>
    /// Attaches a specific mip level of the color texture to the color attachment point.
    /// </summary>
    /// <param name="mipLevel">Zero-based mip level to attach.</param>
    public void AttachColorMipLevel(int mipLevel)
    {
        Debug.Assert(Color != null, "Color attachment is null");

        Color.AttachToFramebuffer(this, FramebufferAttachment.ColorAttachment0, mipLevel);
    }

    /// <summary>
    /// Returns the pixel dimensions of the framebuffer at the given mip level.
    /// </summary>
    /// <param name="level">Zero-based mip level.</param>
    public Vector2i GetMipSize(int level)
    {
        var mipWidth = Math.Max(1, Width >> level);
        var mipHeight = Math.Max(1, Height >> level);

        return new(mipWidth, mipHeight);
    }

    /// <summary>
    /// Deletes the framebuffer object and all its attached textures.
    /// </summary>
    public void Delete()
    {
        GL.DeleteFramebuffer(FboHandle);

        if (Color != null)
        {
            GL.DeleteTexture(Color.Handle);
        }

        if (Depth != null)
        {
            GL.DeleteTexture(Depth.Handle);
        }

        if (Stencil != null)
        {
            GL.DeleteTexture(Stencil.Handle);
        }
    }

    /// <summary>
    /// Configures depth comparison sampling on the depth attachment for shadow map reads.
    /// </summary>
    /// <param name="lEqualCompare">When <see langword="true"/>, sets a less-or-equal compare function; otherwise keeps the default.</param>
    public void SetShadowDepthSamplerState(bool lEqualCompare = false)
    {
        if (Depth != null)
        {
            Depth.SetParameter(TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRToTexture);

            if (lEqualCompare)
            {
                Depth.SetParameter(TextureParameterName.TextureCompareFunc, (int)DepthFunction.Lequal);
            }

            Depth.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            Depth.SetWrapMode(TextureWrapMode.ClampToEdge);
        }
    }
}

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
    /// Color attachments past the first, in attachment order, for shaders with several outputs. See <see cref="ExtraColorFormats"/>.
    /// </summary>
    public RenderTexture[] ExtraColors { get; private set; } = [];

    /// <summary>
    /// Depth attachment texture, or <see langword="null"/> if none.
    /// </summary>
    public RenderTexture? Depth { get; protected set; }

    /// <summary>
    /// Depth texture owned by another framebuffer that this one tests against, or <see langword="null"/> when
    /// it has its own. Set with <see cref="ShareDepth"/>.
    /// </summary>
    public RenderTexture? SharedDepth { get; private set; }

    /// <summary>Number of layers of the depth attachment. More than one allocates it as a texture array; use <see cref="AttachDepthLayer"/> to select the layer rendering writes to.</summary>
    public int DepthLayers { get; set; } = 1;

    // Maybe these can be in texture
    /// <summary>
    /// Pixel format of the color attachment.
    /// </summary>
    public ImageFormat? ColorFormat { get; protected set; }

    /// <summary>
    /// Pixel formats of the color attachments past the first, in attachment order.
    /// </summary>
    public ImageFormat[] ExtraColorFormats { get; protected set; } = [];

    /// <summary>
    /// Pixel format of the depth attachment.
    /// </summary>
    public ImageFormat? DepthFormat { get; protected set; }

    /// <summary>
    /// Framebuffer completeness status set after <see cref="Initialize"/> is called.
    /// </summary>
    public FramebufferErrorCode InitialStatus { get; private set; } = FramebufferErrorCode.FramebufferUndefined;

    // Sampler state requested by callers, remembered so that it survives attachment recreation.
    private bool? shadowDepthSamplerLEqualCompare;
    private (TextureMinFilter Min, TextureMagFilter Mag)? colorFiltering;
    private RsTextureAddressMode? colorWrapMode;

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
    /// Debug name of the framebuffer, also applied as its object label.
    /// </summary>
    public string Name { get; }

    private readonly bool isDefaultFramebuffer;

    /// <summary>
    /// Creates a new named OpenGL framebuffer object.
    /// </summary>
    /// <param name="name">Debug label applied to the framebuffer object.</param>
    public Framebuffer(string name)
    {
        FboHandle = GraphicsDevice.CreateFramebuffer(name);
        Name = name;
    }

    #region Default OpenGL Framebuffer instance, and equality checks
    Framebuffer(int fboHandle)
    {
        FboHandle = fboHandle;
        InitialStatus = FramebufferErrorCode.FramebufferComplete;
        Name = "GLDefaultFramebuffer";
        isDefaultFramebuffer = true;
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
    /// Creates and configures a framebuffer without allocating GPU attachments; call <see cref="Initialize"/> to allocate.
    /// </summary>
    /// <param name="name">Debug label for the framebuffer.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="msaa">Number of MSAA samples; 0 disables multisampling.</param>
    /// <param name="colorFormat">Color attachment format, or <see langword="null"/> for depth-only.</param>
    /// <param name="depthFormat">Depth attachment format, or <see langword="null"/> for color-only.</param>
    /// <param name="extraColorFormats">Formats of further color attachments, for shaders with several outputs.</param>
    public static Framebuffer Prepare(string name, int width, int height, int msaa, ImageFormat? colorFormat, ImageFormat? depthFormat, params ImageFormat[] extraColorFormats)
    {
        var fbo = new Framebuffer(name)
        {
            NumSamples = msaa,
            Target = TargetForSampleCount(msaa),
            ColorFormat = colorFormat,
            ExtraColorFormats = extraColorFormats,
            DepthFormat = depthFormat,
            Width = width,
            Height = height,
        };

        return fbo;
    }

    /// <summary>
    /// Allocates GPU textures for all attachments and throws if the framebuffer is not complete.
    /// </summary>
    public void Initialize()
    {
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

        if (InitialStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Fbo '{Name}' failed to initialize with error: {InitialStatus}");
        }
    }

    /// <summary>
    /// Updates the MSAA sample count and resizes the framebuffer, recreating attachments if the dimensions or the sample count changed.
    /// </summary>
    public void Resize(int width, int height, int msaa)
    {
        var samplesChanged = msaa != NumSamples;

        if (width == Width && height == Height && !samplesChanged)
        {
            return;
        }

        NumSamples = msaa;

        // Resize only recreates attachments when the dimensions change, a sample count change has to do it here.
        if (!Resize(width, height) && samplesChanged)
        {
            CreateAttachments();
        }
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

    private static TextureTarget TargetForSampleCount(int numSamples)
        => numSamples > 0 ? TextureTarget.Texture2DMultisample : TextureTarget.Texture2D;

    private void CreateAttachments()
    {
        Target = TargetForSampleCount(NumSamples);

        if (isDefaultFramebuffer)
        {
            // implicitly created by window
            return;
        }

        Color?.Delete();
        Depth?.Delete();

        foreach (var extraColor in ExtraColors)
        {
            extraColor.Delete();
        }

        var (width, height) = (Width, Height);

        if (ColorFormat is { } colorFormat)
        {
            Color = CreateAttachment(colorFormat, width, height, $"{Name}Color", NumMips);
            Color.AttachToFramebuffer(this, FramebufferAttachment.ColorAttachment0, 0);

            ApplyColorSamplerState();
        }

        if (ExtraColorFormats.Length > 0)
        {
            ExtraColors = new RenderTexture[ExtraColorFormats.Length];
            var drawBuffers = new DrawBuffersEnum[ExtraColorFormats.Length + 1];
            drawBuffers[0] = DrawBuffersEnum.ColorAttachment0;

            for (var i = 0; i < ExtraColorFormats.Length; i++)
            {
                var attachment = i + 1;
                ExtraColors[i] = CreateAttachment(ExtraColorFormats[i], width, height, $"{Name}Color{attachment}");
                ExtraColors[i].AttachToFramebuffer(this, FramebufferAttachment.ColorAttachment0 + attachment, 0);
                drawBuffers[attachment] = DrawBuffersEnum.ColorAttachment0 + attachment;
            }

            GL.NamedFramebufferDrawBuffers(FboHandle, drawBuffers.Length, drawBuffers);
        }

        if (SharedDepth != null)
        {
            GL.NamedFramebufferTexture(FboHandle, FramebufferAttachment.DepthAttachment, SharedDepth.Handle, 0);
        }

        if (DepthFormat is { } depthFormat)
        {
            if (DepthLayers > 1)
            {
                if (Target != TextureTarget.Texture2D)
                {
                    throw new InvalidOperationException("Layered depth attachments do not support multisampling");
                }

                Depth = RenderTexture.Create3D(TextureTarget.Texture2DArray, width, height, DepthLayers, depthFormat, 1, $"{Name}Depth");
                AttachDepthLayer(0);
            }
            else
            {
                Depth = CreateAttachment(depthFormat, width, height, $"{Name}Depth");
                Depth.AttachToFramebuffer(this, FramebufferAttachment.DepthAttachment, 0);
            }

            ApplyShadowDepthSamplerState();

            if (ColorFormat == null)
            {
                // A depth only framebuffer is incomplete unless its color buffers are explicitly disabled.
                GL.NamedFramebufferDrawBuffer(FboHandle, DrawBufferMode.None);
                GL.NamedFramebufferReadBuffer(FboHandle, ReadBufferMode.None);
            }
        }
    }

    private RenderTexture CreateAttachment(ImageFormat format, int width, int height, string label, int numMips = 1)
    {
        var attachment = new RenderTexture(Target, width, height, 1, numMips, label);
        var mipCount = Math.Min(RenderTexture.MaxMipCount(width, height), attachment.NumMipLevels);

        if (Target == TextureTarget.Texture2DMultisample)
        {
            if (mipCount > 1)
            {
                throw new InvalidOperationException("Multisample textures do not support mipmaps");
            }

            GL.TextureStorage2DMultisample(attachment.Handle, NumSamples, format.ToGLSizedInternalFormat(), width, height, fixedsamplelocations: true);
        }
        else
        {
            GL.TextureStorage2D(attachment.Handle, mipCount, format.ToGLSizedInternalFormat(), width, height);
        }

        attachment.SetBaseMaxLevel(0, mipCount - 1);

        if (Target != TextureTarget.Texture2DMultisample && !format.IsBlockCompressed() && IsIntegerFormat(format.ToGLPixelFormat()))
        {
            // Sampling an integer texture with the default linear filtering is undefined.
            attachment.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        }

        return attachment;
    }

    private static bool IsIntegerFormat(PixelFormat pixelFormat) => pixelFormat
        is PixelFormat.RedInteger
        or PixelFormat.GreenInteger
        or PixelFormat.BlueInteger
        or PixelFormat.AlphaInteger
        or PixelFormat.RgInteger
        or PixelFormat.RgbInteger
        or PixelFormat.RgbaInteger
        or PixelFormat.BgrInteger
        or PixelFormat.BgraInteger;

    /// <summary>
    /// Changes the attachment formats and recreates the GPU attachments at the current dimensions.
    /// </summary>
    public void ChangeFormat(ImageFormat? colorFormat, ImageFormat? depthFormat)
    {
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;

        CreateAttachments();
    }

    /// <summary>
    /// Tests against another framebuffer's depth texture instead of allocating one. The owner must have the
    /// same dimensions and sample count. Cheap to call every frame; the owner recreating its depth
    /// attachment is picked up as a new texture.
    /// </summary>
    public void ShareDepth(RenderTexture depth)
    {
        Debug.Assert(DepthFormat == null, "A framebuffer with its own depth attachment cannot share another one");

        if (ReferenceEquals(SharedDepth, depth))
        {
            return;
        }

        SharedDepth = depth;
        GL.NamedFramebufferTexture(FboHandle, FramebufferAttachment.DepthAttachment, depth.Handle, 0);
    }

    /// <summary>Attaches a specific layer of the layered depth texture to the depth attachment point.</summary>
    /// <param name="layer">Zero-based layer to attach.</param>
    public void AttachDepthLayer(int layer)
    {
        Debug.Assert(Depth != null, "Depth attachment is null");
        Debug.Assert(layer >= 0 && layer < DepthLayers, "Depth layer out of range");

        GL.NamedFramebufferTextureLayer(FboHandle, FramebufferAttachment.DepthAttachment, Depth.Handle, 0, layer);
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

        foreach (var extraColor in ExtraColors)
        {
            GL.DeleteTexture(extraColor.Handle);
        }

        if (Depth != null)
        {
            GL.DeleteTexture(Depth.Handle);
        }
    }

    /// <summary>
    /// Configures depth comparison sampling on the depth attachment for shadow map reads.
    /// </summary>
    /// <param name="lEqualCompare">Set when the shadow map is rendered with conventional z. The default
    /// matches a reversed-z shadow map: the comparison passes where the reference is at least as close
    /// to the light as the stored depth.</param>
    public void SetShadowDepthSamplerState(bool lEqualCompare = false)
    {
        shadowDepthSamplerLEqualCompare = lEqualCompare;
        ApplyShadowDepthSamplerState();
    }

    private void ApplyShadowDepthSamplerState()
    {
        if (Depth == null || shadowDepthSamplerLEqualCompare is not bool lEqualCompare)
        {
            return;
        }

        Depth.SetParameter(TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRToTexture);
        Depth.SetParameter(TextureParameterName.TextureCompareFunc, (int)(lEqualCompare ? DepthFunction.Lequal : DepthFunction.Gequal));
        Depth.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
        Depth.SetWrapMode(RsTextureAddressMode.Clamp);
    }

    /// <summary>
    /// Sets the filtering and wrap mode of the color attachment. The state is remembered and re-applied
    /// whenever the attachment is recreated by a resize or a format change.
    /// </summary>
    public void SetColorSamplerState(TextureMinFilter minFilter, TextureMagFilter magFilter, RsTextureAddressMode wrapMode)
    {
        colorFiltering = (minFilter, magFilter);
        colorWrapMode = wrapMode;
        ApplyColorSamplerState();
    }

    private void ApplyColorSamplerState()
    {
        if (Color == null || Target == TextureTarget.Texture2DMultisample)
        {
            return;
        }

        if (colorFiltering.HasValue)
        {
            Color.SetFiltering(colorFiltering.Value.Min, colorFiltering.Value.Mag);
        }

        if (colorWrapMode is RsTextureAddressMode wrapMode)
        {
            Color.SetWrapMode(wrapMode);
        }
    }
}

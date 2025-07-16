using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace GUI.Types.Renderer;

class Framebuffer : IDisposable
{
    public int FboHandle { get; }

    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public bool HasValidDimensions() => Width > 0 && Height > 0;

    public TextureTarget Target { get; protected set; }
    public int NumSamples { get; set; }
    public RenderTexture? Color { get; protected set; }
    public RenderTexture? Depth { get; protected set; }

    // Maybe these can be in texture
    public AttachmentFormat? ColorFormat { get; protected set; }
    public DepthAttachmentFormat? DepthFormat { get; protected set; }

    public FramebufferErrorCode InitialStatus { get; private set; } = FramebufferErrorCode.FramebufferUndefined;
    public FramebufferTarget TargetState { get; set; } = FramebufferTarget.Framebuffer;

    public void Bind(FramebufferTarget targetState)
    {
        TargetState = targetState;
        GL.BindFramebuffer(targetState, FboHandle);
    }

    #region Render state
    public Color4 ClearColor { get; set; } = Color4.Black; // https://gpuopen.com/learn/rdna-performance-guide/#clears
    public ClearBufferMask ClearMask { get; set; } = ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit;
    #endregion

    public void BindAndClear(FramebufferTarget targetState = FramebufferTarget.Framebuffer)
    {
        Bind(targetState);
        GL.ClearColor(ClearColor);
        GL.Clear(ClearMask);
    }

    public Framebuffer()
    {
        GL.CreateFramebuffers(1, out int handle);
        FboHandle = handle;
    }

    #region Default OpenGL Framebuffer instance, and equality checks
    Framebuffer(int fboHandle)
    {
        FboHandle = fboHandle;
        InitialStatus = FramebufferErrorCode.FramebufferComplete;
    }
    public static Framebuffer GetGLDefaultFramebuffer() => new(fboHandle: 0);
    public override bool Equals(object? obj) => obj is Framebuffer other && other.FboHandle == FboHandle;
    public override int GetHashCode() => FboHandle.GetHashCode();

    public static bool operator ==(Framebuffer left, Framebuffer right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Framebuffer left, Framebuffer right) => !(left == right);

    #endregion

    public record class AttachmentFormat(PixelInternalFormat InternalFormat, PixelFormat PixelFormat, PixelType PixelType);
    public record class DepthAttachmentFormat(PixelInternalFormat InternalFormat, PixelType PixelType)
    {
        public static DepthAttachmentFormat Depth32F = new(PixelInternalFormat.DepthComponent32f, PixelType.Float);

        public static implicit operator AttachmentFormat(DepthAttachmentFormat depthFormat)
            => new(depthFormat.InternalFormat, PixelFormat.DepthComponent, depthFormat.PixelType);
    }

    public static Framebuffer Prepare(int width, int height, int msaa, AttachmentFormat? colorFormat, DepthAttachmentFormat? depthFormat)
    {
        var fbo = new Framebuffer
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
    public void Resize(int width, int height, int msaa)
    {
        NumSamples = msaa;
        Resize(width, height);
    }

    public void Resize(int width, int height)
    {
        Width = width;
        Height = height;
        CreateAttachments();
    }

    private void CreateAttachments()
    {
        Color?.Delete();
        Depth?.Delete();

        var (width, height) = (Width, Height);

        if (ColorFormat != null)
        {
            Color = CreateAttachment(ColorFormat, width, height);
            Color.SetLabel("FramebufferColor");
            GL.NamedFramebufferTexture(FboHandle, FramebufferAttachment.ColorAttachment0, Color.Handle, 0);
        }

        if (DepthFormat != null)
        {
            Depth = CreateAttachment(DepthFormat, width, height);
            Depth.SetLabel("FramebufferDepth");
            GL.NamedFramebufferTexture(FboHandle, FramebufferAttachment.DepthAttachment, Depth.Handle, 0);
        }
    }

    private RenderTexture CreateAttachment(AttachmentFormat format, int width, int height)
    {
        var attachment = new RenderTexture(Target, width, height, 1, 1);

        if (Target == TextureTarget.Texture2DMultisample)
        {
            GL.TextureStorage2DMultisample(attachment.Handle, NumSamples, (SizedInternalFormat)format.InternalFormat, width, height, fixedsamplelocations: false);
        }
        else
        {
            GL.TextureStorage2D(attachment.Handle, attachment.NumMipLevels, (SizedInternalFormat)format.InternalFormat, width, height);
        }

        attachment.SetFiltering(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        attachment.SetBaseMaxLevel(0, 0);
        return attachment;
    }

    public void ChangeFormat(AttachmentFormat? colorFormat, DepthAttachmentFormat? depthFormat)
    {
        ColorFormat = colorFormat;
        DepthFormat = depthFormat;

        Resize(Width, Height);
    }

    public void CheckStatus_ThrowIfIncomplete(string name = "")
    {
        if (InitialStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Fbo '{name} failed to initialize with error: {InitialStatus}");
        }
    }

    protected virtual void Dispose(bool diposing)
    {
        if (diposing)
        {
            GL.DeleteFramebuffer(FboHandle);
            GL.DeleteTexture(Color?.Handle ?? 0);
            GL.DeleteTexture(Depth?.Handle ?? 0);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

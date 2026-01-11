using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace ValveResourceFormat.Renderer;

public class Framebuffer
{
    public int FboHandle { get; }

    public int Width { get; protected set; }
    public int Height { get; protected set; }
    public bool HasValidDimensions() => Width > 0 && Height > 0;

    public TextureTarget Target { get; protected set; }
    public int NumSamples { get; set; }
    public RenderTexture? Color { get; protected set; }
    public RenderTexture? Depth { get; protected set; }
    public RenderTexture? Stencil { get; protected set; }

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
    public static Framebuffer GLDefaultFramebuffer => new(fboHandle: 0);
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
        public static readonly DepthAttachmentFormat Depth32F = new(PixelInternalFormat.DepthComponent32f, PixelType.Float);
        public static readonly DepthAttachmentFormat Depth32FStencil8 = new(PixelInternalFormat.Depth32fStencil8, PixelType.Float32UnsignedInt248Rev);

        public static implicit operator AttachmentFormat(DepthAttachmentFormat depthFormat) => depthFormat.ToAttachmentFormat();

        public AttachmentFormat ToAttachmentFormat()
        {
            return new(InternalFormat, PixelFormat.DepthComponent, PixelType);
        }
    }

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
        if (width == Width && height == Height && msaa == NumSamples)
        {
            return;
        }

        NumSamples = msaa;
        Resize(width, height);
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        CreateAttachments();
    }

    private void CreateAttachments()
    {
        Color?.Delete();
        Depth?.Delete();
        Stencil?.Delete();

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

            if (DepthFormat == DepthAttachmentFormat.Depth32FStencil8)
            {
                GL.NamedFramebufferTexture(FboHandle, FramebufferAttachment.DepthStencilAttachment, Depth.Handle, 0);

                // Create stencil view
                Stencil = Depth.CreateView(DepthFormat.InternalFormat);

                Stencil.SetLabel("FramebufferStencil");
                Stencil.SetBaseMaxLevel(0, 0);
                GL.TextureParameter(Stencil.Handle, TextureParameterName.DepthStencilTextureMode, (int)DepthStencilTextureMode.StencilIndex);
            }
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
}

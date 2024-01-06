
using System;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer;

#nullable enable
class Framebuffer : IDisposable
{
    public int FboHandle { get; }

    public int Width { get; protected set; }
    public int Height { get; protected set; }

    public TextureTarget Target { get; protected set; }
    public int NumSamples { get; protected set; }
    public RenderTexture? Color { get; protected set; }
    public RenderTexture? Depth { get; protected set; }

    // Maybe these can be in texture
    public AttachmentFormat? ColorFormat { get; protected set; }
    public DepthAttachmentFormat? DepthFormat { get; protected set; }

    public FramebufferErrorCode InitialStatus { get; private set; } = FramebufferErrorCode.FramebufferUndefined;
    public FramebufferTarget TargetState { get; set; } = FramebufferTarget.Framebuffer;

    public void Bind(FramebufferTarget targetState = FramebufferTarget.Framebuffer)
    {
        TargetState = targetState;
        GL.BindFramebuffer(targetState, FboHandle);
    }

    protected Framebuffer()
    {
        FboHandle = GL.GenFramebuffer();
    }

    public record class AttachmentFormat(PixelInternalFormat InternalFormat, PixelFormat PixelFormat, PixelType PixelType);
    public record class DepthAttachmentFormat(PixelInternalFormat InternalFormat, PixelType PixelType)
    {
        public static DepthAttachmentFormat Depth32F = new(PixelInternalFormat.DepthComponent32f, PixelType.Float);

        public static implicit operator AttachmentFormat(DepthAttachmentFormat depthFormat)
            => new(depthFormat.InternalFormat, PixelFormat.DepthComponent, depthFormat.PixelType);
    }

    public static Framebuffer Create(int width, int height, int msaa, AttachmentFormat? colorFormat, DepthAttachmentFormat? depthFormat)
    {
        var rt = new Framebuffer
        {
            NumSamples = msaa,
            Target = msaa > 0 ? TextureTarget.Texture2DMultisample : TextureTarget.Texture2D,
            ColorFormat = colorFormat,
            DepthFormat = depthFormat,
            Width = width,
            Height = height,
        };

        rt.Initialize();

        if (rt.InitialStatus != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Framebuffer failed to bind with error: {rt.InitialStatus}");
        }

        return rt;
    }

    protected FramebufferErrorCode Initialize()
    {
        if (Target == 0)
        {
            throw new InvalidOperationException("Framebuffer target is not set");
        }

        if (ColorFormat == null && DepthFormat == null)
        {
            throw new InvalidOperationException("Framebuffer has no attachments");
        }

        if (Width <= 0 || Height <= 0)
        {
            throw new InvalidOperationException("Framebuffer has invalid sizes: " + Width + "x" + Height);
        }

        Bind();
        var (width, height) = (Width, Height);
        var fboTarget = FramebufferTarget.Framebuffer;

        if (ColorFormat != null)
        {
            Color = new RenderTexture(Target, width, height, 1, 1);
            using (Color.BindingContext())
            {
                ResizeAttachment(Color, ColorFormat, width, height);
                GL.FramebufferTexture2D(fboTarget, FramebufferAttachment.ColorAttachment0, Color.Target, Color.Handle, 0);
            }
        }

        if (DepthFormat != null)
        {
            Depth = new RenderTexture(Target, width, height, 1, 1);
            using (Depth.BindingContext())
            {
                ResizeAttachment(Depth, DepthFormat, width, height);
                GL.FramebufferTexture2D(fboTarget, FramebufferAttachment.DepthAttachment, Depth.Target, Depth.Handle, 0);
            }
        }

        InitialStatus = GL.CheckFramebufferStatus(fboTarget);
        return InitialStatus;
    }

    private void ResizeAttachment(RenderTexture attachment, AttachmentFormat format, int width, int height)
    {
        if (Target == TextureTarget.Texture2DMultisample)
        {
            GL.TexImage2DMultisample((TextureTargetMultisample)attachment.Target, NumSamples, format.InternalFormat, width, height, false);
        }
        else
        {
            GL.TexImage2D(attachment.Target, 0, format.InternalFormat, width, height, 0, format.PixelFormat, format.PixelType, IntPtr.Zero);
        }
    }

    public virtual void Resize(int width, int height)
    {
        Width = width;
        Height = height;

        if (Color != null)
        {
            using (Color.BindingContext())
            {
                ResizeAttachment(Color, ColorFormat!, width, height);
            }
        }

        if (Depth != null)
        {
            using (Depth.BindingContext())
            {
                ResizeAttachment(Depth, DepthFormat!, width, height);
            }
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

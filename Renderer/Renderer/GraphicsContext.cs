using System.Threading;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// The window side of a graphics context: the surface whose commands the context drives, and
/// whose currency it follows. Implemented by whoever owns the window.
/// </summary>
/// <remarks>
/// Graphics APIs that have no current-context model implement these as no-ops and rely on the
/// context object alone to say which command stream is being recorded.
/// </remarks>
public interface IGraphicsSurface
{
    /// <summary>Binds the surface's command stream to the calling thread.</summary>
    void MakeCurrent();

    /// <summary>Releases the surface's command stream from the calling thread.</summary>
    void MakeNoneCurrent();
}

/// <summary>
/// A surface whose currency its owner manages rather than the context: a window made current once
/// and never released, or an API with no current-context model at all.
/// </summary>
public sealed class ExternalGraphicsSurface : IGraphicsSurface
{
    /// <summary>Gets the shared instance. The type carries no state.</summary>
    public static ExternalGraphicsSurface Instance { get; } = new();

    private ExternalGraphicsSurface()
    {
    }

    /// <inheritdoc/>
    public void MakeCurrent()
    {
    }

    /// <inheritdoc/>
    public void MakeNoneCurrent()
    {
    }
}

/// <summary>
/// One command stream recorded against a <see cref="GraphicsDevice"/>'s objects.
///
/// A device can own several contexts; each is current on at most one thread at a time, so
/// <see cref="Current"/> is per thread and names the context the calling thread is recording into.
/// Objects created through <see cref="GraphicsDevice"/> belong to <see cref="Device"/>, so every
/// context of that device can use them.
/// </summary>
public sealed class GraphicsContext
{
    [ThreadStatic]
    private static GraphicsContext? current;

    // 0 when this context is current on no thread. Written with Interlocked from any thread.
    private int currentThread;

    private readonly IGraphicsSurface surface;

    /// <summary>Gets the device whose objects this context records against.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Gets the debug name this context was created with.</summary>
    public string Name { get; }

    internal GraphicsContext(GraphicsDevice device, IGraphicsSurface surface, string name)
    {
        Device = device;
        Name = name;
        this.surface = surface;
    }

    /// <summary>
    /// Gets the context the calling thread is recording into.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no context is current on this thread, which means the caller is issuing graphics
    /// work without a context, or from a thread the context was never handed to.
    /// </exception>
    public static GraphicsContext Current => current
        ?? throw new InvalidOperationException(
            $"No graphics context is current on thread {Environment.CurrentManagedThreadId}. "
            + $"Graphics work can only be issued on a thread that has made a context current.");

    /// <summary>
    /// Makes this context the one the calling thread records into, binding its surface with it.
    /// Release it with <see cref="MakeNoneCurrent"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the context is still current on another thread, which is a context that was
    /// never released before being picked up elsewhere.
    /// </exception>
    public void MakeCurrent()
    {
        var thread = Environment.CurrentManagedThreadId;
        var holdingThread = Interlocked.CompareExchange(ref currentThread, thread, 0);

        if (holdingThread != 0 && holdingThread != thread)
        {
            throw new InvalidOperationException(
                $"Graphics context '{Name}' is current on thread {holdingThread} and cannot also be made current on thread {thread}. "
                + $"Release it there first.");
        }

        try
        {
            surface.MakeCurrent();
        }
        catch
        {
            Interlocked.CompareExchange(ref currentThread, 0, thread);
            throw;
        }

        current = this;
    }

    /// <summary>
    /// Releases this context and its surface from the calling thread. Like the surface release it
    /// drives, this is not nestable: the innermost call releases for good.
    /// </summary>
    public void MakeNoneCurrent()
    {
        if (current != this)
        {
            return;
        }

        current = null;
        Interlocked.CompareExchange(ref currentThread, 0, Environment.CurrentManagedThreadId);
        surface.MakeNoneCurrent();
    }
}

using System.Diagnostics;
using System.Threading;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// The window side of a graphics context: the surface whose commands the context drives.
/// Implemented by whoever owns the window.
/// </summary>
// End() matches the graphics vocabulary; the reserved-word clash only concerns languages this is not consumed from.
#pragma warning disable CA1716 // Identifiers should not match keywords
public interface IGraphicsSurface
{
    /// <summary>Opens the surface's command stream on the calling thread.</summary>
    void Begin();

    /// <summary>Closes the surface's command stream on the calling thread.</summary>
    void End();
}
#pragma warning restore CA1716

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

#if DEBUG
    // 0 when this context is open on no thread. Written with Interlocked from any thread.
    private int openOnThread;
#endif

    // Null when the caller owns the surface's currency, or the API has no current-context model.
    private readonly IGraphicsSurface? surface;

    /// <summary>The device whose objects this context records against.</summary>
    internal GraphicsDevice Device { get; }

    /// <summary>Gets the debug name this context was created with.</summary>
    public string Name { get; }

    private readonly RenderStateTracker renderState = new();

    /// <summary>Gets the render state applied by the context the calling thread records into.
    /// State is per context, not per device.</summary>
    public static RenderStateTracker RenderState => Current.renderState;

    internal GraphicsContext(GraphicsDevice device, IGraphicsSurface? surface, string name)
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
    /// Opens this context for recording on the calling thread, opening its surface with it.
    /// Close it with <see cref="End"/>.
    /// </summary>
    public void Begin()
    {
        TakeOwnership();

        try
        {
            surface?.Begin();
        }
        catch
        {
            ReleaseOwnership();
            throw;
        }

        current = this;
    }

    /// <summary>
    /// Closes this context and its surface on the calling thread. Like the surface it drives, this
    /// is not nestable: the innermost call closes for good.
    /// </summary>
    public void End()
    {
        if (current != this)
        {
            return;
        }

        current = null;
        ReleaseOwnership();
        surface?.End();
    }

    // Cross thread misuse is a programming error, not a runtime condition, so the tracking that
    // detects it costs an interlocked write per Begin and is not worth carrying into release.
#pragma warning disable CA1822 // The state these read only exists in debug builds
    [Conditional("DEBUG")]
    private void TakeOwnership()
    {
#if DEBUG
        var thread = Environment.CurrentManagedThreadId;
        var holdingThread = Interlocked.CompareExchange(ref openOnThread, thread, 0);

        if (holdingThread != 0 && holdingThread != thread)
        {
            throw new InvalidOperationException(
                $"Graphics context '{Name}' is open on thread {holdingThread} and cannot also be opened on thread {thread}. "
                + $"Close it there first.");
        }
#endif
    }

    [Conditional("DEBUG")]
    private void ReleaseOwnership()
    {
#if DEBUG
        Interlocked.CompareExchange(ref openOnThread, 0, Environment.CurrentManagedThreadId);
#endif
    }
#pragma warning restore CA1822
}

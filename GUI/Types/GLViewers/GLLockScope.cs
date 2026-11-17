using System.Threading;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

public readonly ref struct GLLockScope
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private readonly Lock.Scope lockScope;
#pragma warning restore CA2213 // Ref structs implicitly have Dispose method and do not implement the IDisposable interface
    private readonly GraphicsContext context;

    // The render loop keeps its context current past the scope, as it does between frames
    private readonly bool kept;

    /// <param name="contextLock">The lock that guards <paramref name="control"/>'s context.</param>
    /// <param name="control">Whose context is read once the lock is held, so that it cannot be disposed in between.</param>
    internal GLLockScope(GLContextLock contextLock, GLBaseControl control)
    {
        lockScope = contextLock.EnterReleased();

        try
        {
            context = control.GraphicsContext ?? throw new InvalidOperationException("Cannot acquire GLLockScope without a valid GLNativeWindow.");
            kept = contextLock.IsKeptByCurrentThread;
            context.Begin();
        }
        catch
        {
            lockScope.Dispose();
            throw;
        }
    }

    public readonly void Dispose()
    {
        if (!kept)
        {
            context.End();
        }

        lockScope.Dispose();
    }
}

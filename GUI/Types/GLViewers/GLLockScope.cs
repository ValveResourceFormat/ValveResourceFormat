using System.Threading;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

public readonly ref struct GLLockScope
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private readonly Lock.Scope lockScope;
#pragma warning restore CA2213 // Ref structs implicitly have Dispose method and do not implement the IDisposable interface
    private readonly GraphicsContext context;

    /// <param name="context">Gives the context once the lock is held, so that it cannot be disposed in between.</param>
    public GLLockScope(Lock glLock, Func<GraphicsContext> context)
    {
        lockScope = glLock.EnterScope();

        try
        {
            this.context = context();
            this.context.Begin();
        }
        catch
        {
            lockScope.Dispose();
            throw;
        }
    }

    public readonly void Dispose()
    {
        context.End();
        lockScope.Dispose();
    }
}

using System.Threading;
using OpenTK.Windowing.Desktop;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers;

public readonly ref struct GLLockScope
{
#pragma warning disable CA2213 // Disposable fields should be disposed
    private readonly Lock.Scope lockScope;
#pragma warning restore CA2213 // Ref structs implicitly have Dispose method and do not implement the IDisposable interface
    private readonly IGLFWGraphicsContext context;

    public GLLockScope(Lock glLock, IGLFWGraphicsContext context, GraphicsDevice device)
    {
        lockScope = glLock.EnterScope();
        this.context = context;

        context.MakeCurrent();
        device.MakeCurrent();
    }

    public readonly void Dispose()
    {
        GraphicsDevice.MakeNoneCurrent();
        context.MakeNoneCurrent();
        lockScope.Dispose();
    }
}

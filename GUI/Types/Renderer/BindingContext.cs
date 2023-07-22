using System;

namespace GUI.Types.Renderer;

readonly struct BindingContext : IDisposable
{
    readonly Action unbind;

    public BindingContext(Action bind, Action unbind)
    {
        this.unbind = unbind;
        bind.Invoke();
    }

    public void Dispose() => unbind.Invoke();
}

using System.ComponentModel;
using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Controls;

internal class ThemedContextMenuStrip : ContextMenuStrip
{
    public ThemedContextMenuStrip(IContainer container) : base()
    {
        // this constructor ensures ContextMenuStrip is disposed properly since its not parented to the form.
        ArgumentNullException.ThrowIfNull(container);

        container.Add(this);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();

        Themer.ThemeControl(this);
    }
}

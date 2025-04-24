using System.Windows.Forms;

namespace GUI;

#pragma warning disable WFO5001
// This is a temporary class until .NET 10 (?) does this by itself.
// See https://github.com/dotnet/winforms/pull/12471
internal class ThemedTabControl : TabControl
{
    public ThemedTabControl()
    {
        HandleCreated += ThemedTabControl_HandleCreated;
    }

    private void ThemedTabControl_HandleCreated(object? sender, EventArgs e)
    {
        if (Application.IsDarkModeEnabled)
        {
            Windows.Win32.PInvoke.SetWindowTheme((Windows.Win32.Foundation.HWND)Handle, null, "DarkMode::FileExplorerBannerContainer");
        }
    }
}

using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;

namespace GUI.Controls;

public partial class MainFormBottomPanel : Panel
{
    public MainFormBottomPanel()
    {
        InitializeComponent();

        if (!DesignMode)
        {
            newVersionAvailableToolStripMenuItem.Visible = false;
            newVersionAvailableToolStripMenuItem.Highlighted = true;
        }

        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var textBounds = ClientRectangle;

        textBounds.Width -= menuStrip1.Width;
        if (keybindingsPanel?.Visible == true)
        {
            textBounds.Width -= keybindingsPanel.Width;
        }

        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        Invalidate();
    }

    public void HideVersionLabel()
    {
        versionLabel.Visible = false;
    }

    public void SetVersionText(string text)
    {
        versionLabel.Text = text;
    }

    public void RefreshUpdateState()
    {
        if (IsDisposed)
        {
            return; // The About dialog restarted the application
        }

        if (UpdateInstaller.InstalledVersionText != null)
        {
            newVersionAvailableToolStripMenuItem.Text = "Restart to update";
            newVersionAvailableToolStripMenuItem.Visible = true;
            return;
        }

        newVersionAvailableToolStripMenuItem.Text = "Update Available";
        newVersionAvailableToolStripMenuItem.Visible = Settings.Config.Update.CheckAutomatically && UpdateChecker.IsUpdateAvailable;
    }

    /// <summary>
    /// Points at a build that may already contain a fix for an error that was just shown.
    /// Uses only the update state already in memory, and reverts to the regular state once the About dialog is opened.
    /// </summary>
    public void ShowUpdateAfterError()
    {
        RefreshUpdateState();

        if (IsDisposed || newVersionAvailableToolStripMenuItem.Visible || UpdateChecker.GetUpdateStatus() != UpdateChecker.UpdateStatus.NewerDev)
        {
            return;
        }

        // A stable channel user is not offered dev builds, but the fix is most likely there
        newVersionAvailableToolStripMenuItem.Text = "Newer dev build available";
        newVersionAvailableToolStripMenuItem.Visible = true;
    }

    public void ShowAboutDialog()
    {
        // The error dialog can ask for it while it is already open
        var openForm = Application.OpenForms.OfType<AboutForm>().FirstOrDefault();

        if (openForm != null)
        {
            openForm.Activate();
            return;
        }

        using var form = new AboutForm();
        form.ShowDialog(this);

        RefreshUpdateState();
    }

    private void OnAboutItemClick(object sender, EventArgs e)
    {
        ShowAboutDialog();
    }

    public void UpdateKeybindings(List<KeybindingInfo> keybindings)
    {
        if (keybindings.Count == 0)
        {
            keybindingsPanel.Visible = false;
        }
        else
        {
            keybindingsPanel.SetKeybindings(keybindings);
            keybindingsPanel.Visible = true;
        }

        Invalidate();
    }
}

using System.Windows.Forms;
using GUI.Forms;

namespace GUI.Controls;

public partial class MainFormBottomPanel : UserControl
{
    public MainFormBottomPanel()
    {
        InitializeComponent();

        if (!DesignMode)
        {
            newVersionAvailableToolStripMenuItem.Visible = false;
        }
    }

    public void SetVersionText(string text)
    {
        versionLabel.Text = text;
    }

    public void SetTitleText(string text)
    {
        titleLabel.Text = text;
    }

    public void SetNewVersionAvailable()
    {
        newVersionAvailableToolStripMenuItem.Visible = true;
    }

    private void OnAboutItemClick(object sender, EventArgs e)
    {
        using var form = new AboutForm();
        form.ShowDialog(this);
    }
}

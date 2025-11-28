using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;

namespace GUI.Controls;
public partial class MainFormBottomPanel : UserControl
{
    public MainFormBottomPanel()
    {
        InitializeComponent();
    }

    public void SetTitleText(string text)
    {
        this.label1.Text = text;
    }

    private void OnAboutItemClick(object sender, EventArgs e)
    {
        using var form = new AboutForm();
        form.ShowDialog(this);
    }

    private void CheckForUpdatesToolStripMenuItem_Click(object sender, EventArgs e) => Program.MainForm.CheckForUpdatesCore(true);


    private void NewVersionAvailableToolStripMenuItem_Click(object sender, EventArgs e)
    {
        // This happens when the auto update checker displays the new update label, but there is no actual update data available
        if (!UpdateChecker.IsNewVersionAvailable)
        {
            checkForUpdatesToolStripMenuItem.Visible = true;
            newVersionAvailableToolStripMenuItem.Visible = false;

            Task.Run(() => Program.MainForm.CheckForUpdates(true));

            return;
        }

        using var form = new UpdateAvailableForm();
        form.ShowDialog(this);
    }
}

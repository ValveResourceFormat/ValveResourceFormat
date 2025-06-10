using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
        public EntityInfoControl EntityInfoControl;

        public EntityInfoForm(AdvancedGuiFileLoader guiFileLoader)
        {
            Width = 800;
            Height = 450;
            Text = "EntityInfoForm";

            EntityInfoControl = new(guiFileLoader);

            EntityInfoControl.Dock = DockStyle.Fill;
            Controls.Add(EntityInfoControl);

            Icon = Program.MainForm.Icon;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape && ModifierKeys == Keys.None)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && (EntityInfoControl != null))
            {
                EntityInfoControl.Dispose();
            }
            base.Dispose(disposing);
        }

    }
}

using System.Windows.Forms;
using GUI.Utils;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
        public EntityInfoControl EntityInfoControl;

        public EntityInfoForm(VrfGuiContext vrfGuiContext)
        {
            Width = 800;
            Height = 450;
            Text = "EntityInfoForm";

            EntityInfoControl = new(vrfGuiContext);

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

        private void InitializeComponent()
        {
            SuspendLayout();
            // 
            // EntityInfoForm
            // 
            ClientSize = new System.Drawing.Size(284, 261);
            Font = new System.Drawing.Font("Segoe UI", 10F);
            Name = "EntityInfoForm";
            ResumeLayout(false);

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

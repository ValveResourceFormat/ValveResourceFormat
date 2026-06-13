using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Utils;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace GUI.Forms
{
    partial class EntityInfoForm : ThemedForm
    {
        public EntityInfoControl EntityInfoControl;
        private static WINDOWPLACEMENT? SavedWindowPlacement;

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

        protected override void OnShown(System.EventArgs e)
        {
            base.OnShown(e);

            if (SavedWindowPlacement is { } placement)
            {
                PInvoke.SetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, placement);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>(),
            };

            if (PInvoke.GetWindowPlacement((Windows.Win32.Foundation.HWND)Handle, ref placement))
            {
                SavedWindowPlacement = placement;
            }

            base.OnFormClosing(e);
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

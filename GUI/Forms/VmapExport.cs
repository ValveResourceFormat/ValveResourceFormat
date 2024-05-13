using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    public partial class VmapExport : Form
    {
        private bool Export3DSkybox;

        CheckBox Export3DSkyboxCheckbox;

        public VmapExport()
        {
            InitializeComponent();
            Export3DSkyboxCheckbox = (CheckBox)Controls["decompile_skybox"];
        }

        public DialogResult ShowVmapExportDialog()
        {
            var result = ShowDialog();

            return result;
        }

        private void VmapExport_Load(object sender, EventArgs e)
        {
            Export3DSkybox = Export3DSkyboxCheckbox.Checked;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            Export3DSkybox = ((CheckBox)sender).Checked;
        }

        public VmapOptions VmapExportFlags()
        {
            VmapOptions flags = 0;

            if(Export3DSkybox)
            {
                flags |= VmapOptions.Export3DSkybox;
            }

            return flags;
        }
    }
}

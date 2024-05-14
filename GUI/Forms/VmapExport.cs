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

        public VmapOptions VmapExportFlags()
        {
            VmapOptions flags = 0;

            if (Export3DSkyboxCheckbox.Checked)
            {
                flags |= VmapOptions.Export3DSkybox;
            }

            return flags;
        }
    }
}

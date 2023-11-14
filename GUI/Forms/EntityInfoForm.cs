using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
        public EntityInfoForm(AdvancedGuiFileLoader guiFileLoader)
        {
            InitializeComponent();

            Icon = Program.MainForm.Icon;

            Resource.AddDataGridExternalRefAction(guiFileLoader, dataGrid, ColumnValue.Name, (referenceFound) =>
            {
                if (referenceFound)
                {
                    Close();
                }
            });
        }

        public void AddColumn(string name, string value)
        {
            dataGrid.Rows.Add(new string[] { name, value });
        }
    }
}

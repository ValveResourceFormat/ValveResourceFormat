using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using System.Globalization;

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

        public void AddConnection(KVObject connectionData)
        {
            var outputName = connectionData.GetStringProperty("m_outputName");
            var targetName = connectionData.GetStringProperty("m_targetName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");

            dataGridOutputs.Rows.Add(new string[] {
                outputName,
                targetName,
                inputName,
                parameter,
                delay.ToString(NumberFormatInfo.InvariantInfo),
                timesToFire == 1 ? "Yes" : "No"
            });
        }
    }
}

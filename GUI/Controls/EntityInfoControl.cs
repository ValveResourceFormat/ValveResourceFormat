using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms
{
    partial class EntityInfoControl : UserControl
    {
        public DataGridView OutputsGrid => dataGridOutputs;

        public EntityInfoControl()
        {
            InitializeComponent();
        }

        public EntityInfoControl(VrfGuiContext vrfGuiContext) : this()
        {
            ResourceAddDataGridExternalRef(vrfGuiContext);
        }

        public void ResourceAddDataGridExternalRef(VrfGuiContext vrfGuiContext)
        {
            Resource.AddDataGridExternalRefAction(vrfGuiContext, dataGridProperties, ColumnValue.Name, (referenceFound) =>
            {
                if (referenceFound)
                {
                    if (Parent is Form form)
                    {
                        form.Close();
                    }
                }
            });
        }

        public void ShowPropertiesTab()
        {
            tabControl.SelectedIndex = 0;
        }

        public void ShowOutputsTabIfAnyData()
        {
            if (dataGridOutputs.RowCount > 0)
            {
                if (tabPageOutputs.Parent == null)
                {
                    tabControl.TabPages.Add(tabPageOutputs);
                }
            }
            else
            {
                if (tabPageOutputs.Parent != null)
                {
                    tabControl.TabPages.Remove(tabPageOutputs);
                }
            }
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
        }

        public void AddProperty(string name, string value)
        {
            dataGridProperties.Rows.Add([name, value]);
        }

        public void AddConnection(KVObject connectionData)
        {
            var outputName = connectionData.GetStringProperty("m_outputName");
            var targetName = connectionData.GetStringProperty("m_targetName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");

            var stimesToFire = timesToFire switch
            {
                1 => "Only Once",
                >= 2 => $"Only {timesToFire} Times",
                _ => "Infinite",
            };

            dataGridOutputs.Rows.Add([
                outputName,
                targetName,
                inputName,
                parameter,
                delay,
                stimesToFire
            ]);
        }
    }
}

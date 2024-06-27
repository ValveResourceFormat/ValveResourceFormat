using System.Globalization;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Forms
{
    partial class EntityInfoForm : Form
    {
        private readonly TabPage[] pages;

        public EntityInfoForm(AdvancedGuiFileLoader guiFileLoader)
        {
            InitializeComponent();

            Icon = Program.MainForm.Icon;

            Resource.AddDataGridExternalRefAction(guiFileLoader, dataGridProperties, ColumnValue.Name, (referenceFound) =>
            {
                if (referenceFound)
                {
                    Close();
                }
            });

            pages = new TabPage[tabControl.TabPages.Count];
            for (var i = 0; i < pages.Length; i++)
            {
                pages[i] = tabControl.TabPages[i];
            }
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

        private void SetTabCount(int count)
        {
            if (tabControl.TabCount == count)
            {
                return;
            }
            else if (tabControl.TabCount > count)
            {
                tabControl.TabIndex = count - 1;
                for (var i = tabControl.TabCount - 1; i >= count; i--)
                {
                    tabControl.TabPages.RemoveAt(i);
                }
            }
            else
            {
                for (var i = tabControl.TabCount; i < count; i++)
                {
                    tabControl.TabPages.Add(pages[i]);
                }
            }
        }

        public void SetEntityLayout(bool isEntity)
        {
            if (isEntity)
            {
                SetTabCount(pages.Length);
            }
            else
            {
                SetTabCount(1);
            }
        }

        public void Clear()
        {
            dataGridProperties.Rows.Clear();
            dataGridOutputs.Rows.Clear();
        }

        public void AddProperty(string name, string value)
        {
            dataGridProperties.Rows.Add(new string[] { name, value });
        }

        public void AddConnection(KVObject connectionData)
        {
            var outputName = connectionData.GetStringProperty("m_outputName");
            var targetName = connectionData.GetStringProperty("m_targetName");
            var inputName = connectionData.GetStringProperty("m_inputName");
            var parameter = connectionData.GetStringProperty("m_overrideParam");
            var delay = connectionData.GetFloatProperty("m_flDelay");
            var timesToFire = connectionData.GetInt32Property("m_nTimesToFire");

            string stimesToFire = "";

            switch (timesToFire)
            {
                case 1:
                    stimesToFire = "Only Once";
                    break;
                case >= 2:
                    stimesToFire = $"Only {timesToFire} Times";
                    break;
                default:
                    stimesToFire = "Infinite";
                    break;
            }

            dataGridOutputs.Rows.Add(new string[] {
                outputName,
                targetName,
                inputName,
                parameter,
                delay.ToString(NumberFormatInfo.InvariantInfo),
                stimesToFire
            });
        }
    }
}

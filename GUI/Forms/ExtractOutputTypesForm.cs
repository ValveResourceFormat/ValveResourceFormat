using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Forms
{
    public partial class ExtractOutputTypesForm : ThemedForm
    {
        public event EventHandler? ChangeTypeEvent;

        public ExtractOutputTypesForm()
        {
            InitializeComponent();
            ApplyTranslations();
        }

        private void ApplyTranslations()
        {
            Text = Locale.Extract_Title;
            label1.Text = Locale.Extract_Description;
            label2.Text = Locale.Extract_Input;
            label3.Text = Locale.Extract_Output;
            label4.Text = Locale.Extract_Count;
            button1.Text = Locale.Extract_Continue;
        }

        public void AddTypeToTable(string type, int count, List<string> outputTypes, int defaultType)
        {
            var row = typesTable.RowCount;
            typesTable.RowCount = row + 1;
            typesTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            typesTable.Controls.Add(new Label()
            {
                AutoSize = true,
                Text = type
            }, 0, row);

            var dropdown = new ThemedComboBox
            {
                Tag = type,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            dropdown.Items.AddRange(outputTypes.ToArray());
            dropdown.SelectedIndex = defaultType;
            dropdown.SelectedIndexChanged += ChangeTypeEvent; // TODO: leak?

            typesTable.Controls.Add(dropdown, 1, row);

            typesTable.Controls.Add(new Label()
            {
                Text = Locale.Extract_FileCount(count),
            }, 2, row);
        }
    }
}

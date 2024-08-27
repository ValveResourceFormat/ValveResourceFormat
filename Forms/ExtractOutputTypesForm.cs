using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class ExtractOutputTypesForm : Form
    {
        public event EventHandler ChangeTypeEvent;

        public ExtractOutputTypesForm()
        {
            InitializeComponent();

            MainForm.ThemeManager.RegisterControl(this);
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

            var dropdown = new ComboBox
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
                Text = $"{count} file{(count == 1 ? "" : "s")}",
            }, 2, row);
        }
    }
}

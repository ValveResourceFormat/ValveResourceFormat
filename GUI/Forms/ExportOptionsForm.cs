using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    /// <summary>
    /// Asks how to decompile: the layout, the output type of each file type, the dependencies, what to do with existing
    /// files and the options of the decompilers, which come from <see cref="ContentExportOption"/>.
    /// </summary>
    public sealed class ExportOptionsForm : ThemedForm
    {
        private const string HelpUrl = "https://www.source2.wiki/Basics/content-mounting?game=any";
        private const int ContentWidth = 540;

        private readonly ContentExportSettings settings;
        private readonly Func<IEnumerable<ContentExportOption>> getRelevantOptions;
        private readonly IReadOnlySet<ContentExportOption> hiddenOptions;
        private readonly TableLayoutPanel typesTable;
        private readonly FlowLayoutPanel optionsPanel;
        private readonly ThemedComboBox placementComboBox;
        private readonly CheckBox doNotShowAgainCheckBox;
        private readonly ToolTip toolTip = new();

        /// <summary>Occurs when the output type of a file type changes, the sender is its combo box with the type as its tag.</summary>
        public event EventHandler? ChangeTypeEvent;

        /// <summary>Gets whether the dialog is not shown for the next exports.</summary>
        public bool DoNotShowAgain => doNotShowAgainCheckBox.Checked;

        /// <param name="settings">The settings the controls show and change.</param>
        /// <param name="getRelevantOptions">Gets the options that apply to the chosen output types.</param>
        /// <param name="hiddenOptions">Options that other controls of the dialog or its caller set.</param>
        public ExportOptionsForm(ContentExportSettings settings, Func<IEnumerable<ContentExportOption>> getRelevantOptions, IReadOnlySet<ContentExportOption> hiddenOptions)
        {
            this.settings = settings;
            this.getRelevantOptions = getRelevantOptions;
            this.hiddenOptions = hiddenOptions;

            Text = "Decompile";
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Font = new Font("Segoe UI", 10F);
            ClientSize = new Size(ContentWidth + 60, 720);
            MinimumSize = new Size(ContentWidth + 60, 360);

            var content = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(20, 16, 20, 8),
            };

            content.Controls.Add(new Label
            {
                Text = "Choose how to extract & decompile",
                AutoSize = true,
                Font = new Font(Font.FontFamily, 13F, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 12),
            });

            AddLayoutChoices(content);

            typesTable = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Width = ContentWidth,
                Margin = new Padding(0, 4, 0, 12),
            };
            typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160F));
            typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
            typesTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140F));
            typesTable.Controls.Add(MakeLabel("Input", bold: true), 0, 0);
            typesTable.Controls.Add(MakeLabel("Output", bold: true), 1, 0);
            typesTable.Controls.Add(MakeLabel("Count", bold: true), 2, 0);
            content.Controls.Add(MakeHeading("Output types"));
            content.Controls.Add(typesTable);

            content.Controls.Add(MakeHeading("Files"));
            content.Controls.Add(MakeChoiceRow(ContentExportSettings.DependenciesOption, out _));
            content.Controls.Add(MakeChoiceRow(ContentExportSettings.DependencyPlacementOption, out placementComboBox));
            content.Controls.Add(MakeChoiceRow(ContentExportSettings.ExistingFilesOption, out _));

            optionsPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            content.Controls.Add(optionsPanel);

            var helpLink = new LinkLabel
            {
                Text = $"Don't know which option to choose? {HelpUrl}",
                AutoSize = true,
                MaximumSize = new Size(ContentWidth, 0),
                Margin = new Padding(0, 12, 0, 4),
            };
            helpLink.LinkArea = new LinkArea(helpLink.Text.Length - HelpUrl.Length, HelpUrl.Length);
            helpLink.LinkClicked += (_, _) => AboutForm.OpenUrl(HelpUrl);
            content.Controls.Add(helpLink);

            doNotShowAgainCheckBox = new CheckBox
            {
                Text = "Do not show again (it can be turned back on in Settings)",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 0),
            };
            content.Controls.Add(doNotShowAgainCheckBox);

            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
            };
            scrollPanel.Controls.Add(content);

            var buttonRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(20, 10, 20, 14),
            };
            var continueButton = MakeButton("Continue", DialogResult.Continue);
            var cancelButton = MakeButton("Cancel", DialogResult.Cancel);
            buttonRow.Controls.Add(continueButton);
            buttonRow.Controls.Add(cancelButton);

            Controls.Add(scrollPanel);
            Controls.Add(buttonRow);

            AcceptButton = continueButton;
            CancelButton = cancelButton;

            UpdatePlacementEnabled();
        }

        /// <summary>
        /// Adds a file type with the output types it can be exported as.
        /// </summary>
        public void AddTypeToTable(string type, int count, List<string> outputTypes, int selectedIndex)
        {
            var row = typesTable.RowCount;
            typesTable.RowCount = row + 1;

            var dropdown = new ThemedComboBox
            {
                Tag = type,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 200,
                Anchor = AnchorStyles.Left,
            };
            dropdown.Items.AddRange([.. outputTypes]);
            dropdown.SelectedIndex = selectedIndex;
            dropdown.SelectedIndexChanged += (sender, e) =>
            {
                ChangeTypeEvent?.Invoke(sender, e);
                UpdateOptions();
            };

            typesTable.Controls.Add(MakeLabel(type), 0, row);
            typesTable.Controls.Add(dropdown, 1, row);
            typesTable.Controls.Add(MakeLabel(count == 1 ? "1 file" : $"{count} files"), 2, row);
        }

        protected override void OnLoad(EventArgs e)
        {
            UpdateOptions();
            base.OnLoad(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                typesTable.Dispose();
                optionsPanel.Dispose();
                placementComboBox.Dispose();
                doNotShowAgainCheckBox.Dispose();
                toolTip.Dispose();
            }

            base.Dispose(disposing);
        }

        private void AddLayoutChoices(FlowLayoutPanel content)
        {
            var option = ContentExportSettings.LayoutOption;
            var selected = option.GetValue(settings);

            content.Controls.Add(MakeHeading(option.Label));

            foreach (var (value, label, description) in option.Choices)
            {
                var radioButton = new RadioButton
                {
                    Text = label,
                    AutoSize = true,
                    Checked = value == selected,
                    Margin = new Padding(0, 2, 0, 0),
                };
                radioButton.CheckedChanged += (_, _) =>
                {
                    if (radioButton.Checked)
                    {
                        option.TrySetValue(settings, value);
                        UpdatePlacementEnabled();
                    }
                };

                content.Controls.Add(radioButton);
                content.Controls.Add(MakeDescription(description, new Padding(18, 0, 0, 4)));
            }
        }

        private void UpdatePlacementEnabled()
        {
            placementComboBox.Enabled = settings.Layout != ContentExportLayout.ContentPaths;
        }

        /// <summary>
        /// Rebuilds the decompiler options for the output types chosen now, grouped by category.
        /// </summary>
        private void UpdateOptions()
        {
            optionsPanel.SuspendLayout();

            var oldControls = optionsPanel.Controls.Cast<Control>().ToList();
            optionsPanel.Controls.Clear();
            oldControls.ForEach(static control => control.Dispose());

            var options = getRelevantOptions()
                .Where(option => option.Category != ContentExportSettings.GeneralCategory && !hiddenOptions.Contains(option));

            foreach (var category in options.GroupBy(static option => option.Category))
            {
                optionsPanel.Controls.Add(MakeHeading(category.Key));

                foreach (var option in category)
                {
                    if (option.Kind == ContentExportOptionKind.Notice)
                    {
                        optionsPanel.Controls.Add(MakeLabel(option.Label, bold: true));
                    }

                    optionsPanel.Controls.Add(option.Kind switch
                    {
                        ContentExportOptionKind.Toggle => MakeCheckBox(option),
                        ContentExportOptionKind.Choice => MakeChoiceRow(option, out _),
                        _ => MakeDescription(option.Description, new Padding(0, 2, 0, 4)),
                    });
                }
            }

            Themer.ThemeControl(optionsPanel);
            optionsPanel.ResumeLayout(true);
        }

        private CheckBox MakeCheckBox(ContentExportOption option)
        {
            var checkBox = new CheckBox
            {
                Text = option.Label,
                AutoSize = true,
                Checked = option.GetValue(settings) == "true",
                Margin = new Padding(0, 2, 0, 2),
            };
            checkBox.CheckedChanged += (_, _) => option.TrySetValue(settings, checkBox.Checked ? "true" : "false");
            toolTip.SetToolTip(checkBox, option.Description);

            return checkBox;
        }

        private FlowLayoutPanel MakeChoiceRow(ContentExportOption option, out ThemedComboBox comboBox)
        {
            var dropdown = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 240,
                Anchor = AnchorStyles.Left,
            };

            var selected = option.GetValue(settings);

            foreach (var (value, choiceLabel, _) in option.Choices)
            {
                dropdown.Items.Add(choiceLabel);

                if (value == selected)
                {
                    dropdown.SelectedIndex = dropdown.Items.Count - 1;
                }
            }

            dropdown.SelectedIndexChanged += (_, _) =>
            {
                var choice = option.Choices[dropdown.SelectedIndex];
                option.TrySetValue(settings, choice.Value);
                toolTip.SetToolTip(dropdown, choice.Description);
            };

            if (dropdown.SelectedIndex >= 0)
            {
                toolTip.SetToolTip(dropdown, option.Choices[dropdown.SelectedIndex].Description);
            }

            var label = MakeLabel(option.Label);
            label.Width = 200;
            label.AutoSize = false;
            toolTip.SetToolTip(label, option.Description);

            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 2, 0, 2),
            };
            row.Controls.Add(label);
            row.Controls.Add(dropdown);

            comboBox = dropdown;
            return row;
        }

        private static Label MakeHeading(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            Margin = new Padding(0, 10, 0, 4),
        };

        private static Label MakeLabel(string text, bool bold = false)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
            };

            if (bold)
            {
                label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            }

            return label;
        }

        private static Label MakeDescription(string text, Padding margin) => new()
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(ContentWidth, 0),
            Margin = margin,
        };

        private static ThemedButton MakeButton(string text, DialogResult result) => new()
        {
            Text = text,
            DialogResult = result,
            Size = new Size(90, 30),
            Margin = new Padding(8, 0, 0, 0),
        };
    }
}

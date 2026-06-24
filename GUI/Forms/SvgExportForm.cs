using System.Drawing;
using System.Windows.Forms;
using GUI.Controls;

namespace GUI.Forms
{
    public enum SvgExportFormat
    {
        Svg,
        Png,
        Jpg,
    }

    /// <summary>
    /// Asks which format to export an svg as, and (for raster formats) the resolution to rasterize at.
    /// The chosen resolution is a uniform scale that preserves aspect ratio.
    /// </summary>
    public sealed class SvgExportForm : ThemedForm
    {
        private static readonly int[] PresetLongEdges = [64, 128, 256, 512, 1024, 2048, 4096];

        private readonly float sourceWidth;
        private readonly float sourceHeight;
        private readonly List<SvgExportFormat> formats = [];
        // Long edge in pixels for each resolution item; 0 means the svg's native size, -1 means the custom field.
        private readonly List<int> itemLongEdges = [];
        private readonly ThemedComboBox formatComboBox;
        private readonly ThemedComboBox presetComboBox;
        private readonly ThemedIntNumeric customNumeric;
        private readonly Label resolutionLabel;
        private readonly FlowLayoutPanel customRow;
        private readonly Label outputLabel;

        public SvgExportFormat SelectedFormat => formats[formatComboBox.SelectedIndex];

        // Guard against a degenerate zero-size svg so the scale math never divides by zero.
        private float SourceLongEdge => MathF.Max(MathF.Max(sourceWidth, sourceHeight), 1f);

        /// <summary>The scale to rasterize at, relative to the svg's native resolution (1 = native).</summary>
        public float SelectedScale => ChosenLongEdge / SourceLongEdge;

        private float ChosenLongEdge => itemLongEdges[presetComboBox.SelectedIndex] switch
        {
            0 => SourceLongEdge,
            -1 => customNumeric.Value,
            var longEdge => longEdge,
        };

        public SvgExportForm(float sourceWidth, float sourceHeight, bool canExportSvg)
        {
            this.sourceWidth = sourceWidth;
            this.sourceHeight = sourceHeight;

            Text = "Export image";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 17F);
            Font = new Font("Segoe UI", 10F);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var nativeLongEdge = (int)MathF.Ceiling(MathF.Max(sourceWidth, sourceHeight));
            var minLongEdge = nativeLongEdge;
            var maxLongEdge = Math.Max(nativeLongEdge, 8192);

            formatComboBox = MakeComboBox();
            foreach (var (format, label) in BuildFormats(canExportSvg))
            {
                formats.Add(format);
                formatComboBox.Items.Add(label);
            }
            // Default to SVG when available (lossless), otherwise PNG.
            var defaultFormat = formats.Contains(SvgExportFormat.Svg) ? SvgExportFormat.Svg : SvgExportFormat.Png;
            formatComboBox.SelectedIndex = formats.IndexOf(defaultFormat);

            presetComboBox = MakeComboBox();
            itemLongEdges.Add(0);
            presetComboBox.Items.Add($"Original  ({(int)sourceWidth} × {(int)sourceHeight})");

            foreach (var preset in PresetLongEdges)
            {
                if (preset > nativeLongEdge)
                {
                    itemLongEdges.Add(preset);
                    var (width, height) = DimensionsForLongEdge(preset);
                    presetComboBox.Items.Add($"{width} × {height}");
                }
            }

            itemLongEdges.Add(-1);
            presetComboBox.Items.Add("Custom…");
            presetComboBox.SelectedIndex = 0;

            customNumeric = new ThemedIntNumeric
            {
                MinValue = minLongEdge,
                MaxValue = maxLongEdge,
                Increment = 1,
                Value = Math.Clamp(1024, minLongEdge, maxLongEdge),
                Width = 72,
                Margin = new Padding(0, 0, 8, 0),
            };

            customRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
            };
            customRow.Controls.Add(customNumeric);
            customRow.Controls.Add(MakeFieldLabel("px (long edge)"));

            resolutionLabel = MakeFieldLabel("Resolution:");
            outputLabel = MakeFieldLabel(string.Empty);
            outputLabel.Margin = new Padding(0, 10, 0, 0);

            var layout = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(20, 18, 20, 14),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (var i = 0; i < layout.RowCount; i++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            layout.Controls.Add(MakeFieldLabel("Format:"), 0, 0);
            layout.Controls.Add(formatComboBox, 1, 0);
            layout.Controls.Add(resolutionLabel, 0, 1);
            layout.Controls.Add(presetComboBox, 1, 1);
            layout.Controls.Add(customRow, 1, 2);
            layout.Controls.Add(outputLabel, 0, 3);
            layout.SetColumnSpan(outputLabel, 2);

            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Anchor = AnchorStyles.Right,
                Margin = new Padding(0, 18, 0, 0),
            };
            var saveButton = MakeButton("Save", DialogResult.OK);
            var cancelButton = MakeButton("Cancel", DialogResult.Cancel);
            buttonRow.Controls.Add(saveButton);
            buttonRow.Controls.Add(cancelButton);

            layout.Controls.Add(buttonRow, 0, 4);
            layout.SetColumnSpan(buttonRow, 2);

            Controls.Add(layout);

            AcceptButton = saveButton;
            CancelButton = cancelButton;

            formatComboBox.SelectedIndexChanged += OnFormatChanged;
            presetComboBox.SelectedIndexChanged += OnPresetChanged;
            // CustomTextChanged fires on every keystroke (and on drag); ValueChanged only fires on commit.
            customNumeric.CustomTextChanged += (_, _) => UpdateOutputLabel();

            UpdateResolutionVisibility();
            UpdateOutputLabel();
        }

        private static (SvgExportFormat Format, string Label)[] BuildFormats(bool canExportSvg)
            => canExportSvg
                ? [(SvgExportFormat.Svg, "SVG (vector)"), (SvgExportFormat.Png, "PNG"), (SvgExportFormat.Jpg, "JPG")]
                : [(SvgExportFormat.Png, "PNG"), (SvgExportFormat.Jpg, "JPG")];

        private static ThemedComboBox MakeComboBox() => new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 210,
            Margin = new Padding(0, 4, 0, 4),
            Anchor = AnchorStyles.Left,
        };

        private static Label MakeFieldLabel(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 12, 0),
        };

        private static ThemedButton MakeButton(string text, DialogResult result) => new()
        {
            Text = text,
            DialogResult = result,
            Size = new Size(90, 30),
            Margin = new Padding(8, 0, 0, 0),
        };

        private void OnFormatChanged(object? sender, EventArgs e) => UpdateResolutionVisibility();

        private void OnPresetChanged(object? sender, EventArgs e)
        {
            UpdateResolutionVisibility();
            UpdateOutputLabel();
        }

        private void UpdateResolutionVisibility()
        {
            var isRaster = SelectedFormat != SvgExportFormat.Svg;
            var isCustom = itemLongEdges[presetComboBox.SelectedIndex] == -1;

            resolutionLabel.Visible = isRaster;
            presetComboBox.Visible = isRaster;
            outputLabel.Visible = isRaster;
            customRow.Visible = isRaster && isCustom;
        }

        private void UpdateOutputLabel()
        {
            var (width, height) = DimensionsForLongEdge(ChosenLongEdge);
            outputLabel.Text = $"Output: {width} × {height} px";
        }

        // Output dimensions when the svg is scaled so its long edge becomes longEdge, preserving aspect ratio.
        private (int Width, int Height) DimensionsForLongEdge(float longEdge)
        {
            var scale = longEdge / SourceLongEdge;
            return ((int)(sourceWidth * scale), (int)(sourceHeight * scale));
        }
    }
}

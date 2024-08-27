using GUI.Controls;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Theme
{
    public class ThemeController
    {
        public IThemeData CurrentTheme => _currentTheme;

        private List<IThemeData> _knownThemes;
        private List<Control> controls;
        private IThemeData _currentTheme;
        public ThemeController()
        {
            _knownThemes = new List<IThemeData>();
            controls = new List<Control>();
        }

        public IEnumerable<string> GetThemeNames()
        {
            return _knownThemes.ConvertAll(x => x.Name).ToArray();
        }

        public void AddTheme(IThemeData theme)
        {
            _knownThemes.Add(theme);
        }

        public bool ContainsTheme(IThemeData theme)
        {
            return ContainsTheme(theme.Name);
        }

        public bool ContainsTheme(string name)
        {
            return FindTheme(name) != null;
        }

        private IThemeData FindTheme(string name) => _knownThemes.Find(t => t.Name == name);

        /// <summary>
        /// Registers the given control to the theme system to be updated when the theme changes. This also applies the current theme immediately, if any.
        /// </summary>
        /// <param name="control">The control to be added to the theme system.</param>
        /// <returns>Returns <see langword="true"/> if added correctly, otherwise <see langword="false"/>.</returns>
        public bool RegisterControl(Control control)
        {
            if (_currentTheme != null)
                ApplyThemeToControl(_currentTheme, control, true);

            if (controls.Contains(control))
                return true;

            controls.Add(control);

            return true;
        }

        public bool UnregisterControl(Control control)
        {
            return controls.Remove(control);
        }

        public bool ApplyTheme(string name, bool recursively = true, bool isCurrentTheme = true)
        {
            IThemeData themeData = FindTheme(name);
            if (themeData == null)
                return false;

            ApplyTheme(themeData, recursively, isCurrentTheme);
            return true;
        }

        public void ApplyTheme(IThemeData theme, bool recursively = true, bool isCurrentTheme = true)
        {
            if (isCurrentTheme)
                _currentTheme = theme;

            foreach (Control control in controls)
            {
                ApplyThemeToControl(theme, control, recursively);
            }
        }

        public bool ReapplyCurrentTheme(bool recursively = true)
        {
            if (_currentTheme == null)
                return false;

            foreach (Control control in controls)
            {
                ApplyThemeToControl(_currentTheme, control, recursively);
            }

            return true;
        }

#pragma warning disable CA1822 // Mark members as static
        private void attc_menustrip_recursive(ToolStripMenuItem item, IThemeData theme)
#pragma warning restore CA1822 // Mark members as static
        {
            item.ForeColor = theme.Tertiary;
            item.BackColor = Color.Transparent;

            if (item.HasDropDownItems)
            {
                foreach (ToolStripItem iitem in item.DropDownItems)
                {
                    if (iitem is ToolStripMenuItem menuItem)
                        attc_menustrip_recursive(menuItem, theme);
                    else
                    {
                        iitem.BackColor = theme.Primary;
                        iitem.ForeColor = theme.Tertiary;
                    }
                }
            }
        }

        private void ApplyThemeToControl(IThemeData theme, Control c, bool recursive)
        {
            if (c is CustomTabControl cc)
            {
                cc.TCBackColor = theme.Secondary;
                cc.TCForeColor = theme.Tertiary;
                cc.PageBackColor = theme.Primary;
            }
            else if (c is TextBox tb)
            {
                tb.BorderStyle = BorderStyle.FixedSingle;
                tb.BackColor = theme.Primary;
                tb.ForeColor = theme.Tertiary;
            }
            else if (c is CheckedListBox clb)
            {
                clb.BackColor = theme.Primary;
                clb.ForeColor = theme.Tertiary;
            }
            else if (c is CodeTextBox ctb)
            {
                ctb.IndentBackColor = theme.Secondary;
                ctb.BackColor = theme.Primary;
                ctb.ForeColor = theme.Tertiary;
                ctb.ServiceLinesColor = Color.Transparent;
                ctb.ServiceColors.CollapseMarkerForeColor = theme.Tertiary;
                ctb.ServiceColors.CollapseMarkerBackColor = theme.Secondary;
                ctb.ServiceColors.CollapseMarkerBorderColor = theme.Quaternary;
                ctb.ServiceColors.ExpandMarkerForeColor = theme.Tertiary;
                ctb.ServiceColors.ExpandMarkerBackColor = theme.Secondary;
                ctb.ServiceColors.ExpandMarkerBorderColor = theme.Quaternary;
            }
            else if (c is CustomByteViewer cbv)
            {
                cbv.BackColor = theme.Primary;
                cbv.ForeColor = theme.Tertiary;
                cbv.BorderColor = theme.Quaternary;
            }
            else if (c is System.ComponentModel.Design.ByteViewer bv)
            {
                bv.BackColor = theme.Primary;
                bv.ForeColor = theme.Tertiary;
            }
            else if (c is DataGridView dgv)
            {
                dgv.BackgroundColor = theme.Primary;
                dgv.ForeColor = theme.Tertiary;
                dgv.GridColor = theme.Quaternary;

                dgv.AdvancedColumnHeadersBorderStyle.All = DataGridViewAdvancedCellBorderStyle.Single;

                dgv.DefaultCellStyle.ForeColor = theme.Tertiary;
                dgv.DefaultCellStyle.BackColor = theme.Primary;
                dgv.DefaultCellStyle.SelectionBackColor = theme.Quinary;
                dgv.DefaultCellStyle.SelectionForeColor = theme.Tertiary;

                dgv.RowHeadersDefaultCellStyle.ForeColor = theme.Tertiary;
                dgv.RowHeadersDefaultCellStyle.BackColor = theme.Primary;
                dgv.RowHeadersDefaultCellStyle.SelectionBackColor = theme.Quinary;
                dgv.RowHeadersDefaultCellStyle.SelectionForeColor = theme.Tertiary;

                dgv.AdvancedColumnHeadersBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedColumnHeadersBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedColumnHeadersBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.Single;
                dgv.AdvancedColumnHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;

                dgv.AdvancedCellBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedCellBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedCellBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.Single;
                dgv.AdvancedCellBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;

                dgv.AdvancedRowHeadersBorderStyle.Top = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedRowHeadersBorderStyle.Left = DataGridViewAdvancedCellBorderStyle.None;
                dgv.AdvancedRowHeadersBorderStyle.Right = DataGridViewAdvancedCellBorderStyle.Single;
                dgv.AdvancedRowHeadersBorderStyle.Bottom = DataGridViewAdvancedCellBorderStyle.Single;

                dgv.EnableHeadersVisualStyles = false;
                dgv.ColumnHeadersDefaultCellStyle.ForeColor = theme.Tertiary;
                dgv.ColumnHeadersDefaultCellStyle.BackColor = theme.Secondary;
                dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = theme.Quinary;
                dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = theme.Tertiary;
            }
            else if (c is Button btn)
            {
                btn.BackColor = theme.Primary;
                btn.ForeColor = theme.Tertiary;

                btn.FlatStyle = FlatStyle.Flat;

                btn.FlatAppearance.MouseOverBackColor = theme.Quinary;
                btn.FlatAppearance.MouseDownBackColor = theme.Senary;
                btn.FlatAppearance.BorderColor = theme.Quaternary;
                btn.FlatAppearance.BorderSize = 1;
            }
            else if (c is CustomGroupBox gb)
            {
                gb.BackColor = theme.Primary;
                gb.ForeColor = theme.Tertiary;
                gb.BorderWidth = 1;
                gb.BorderColor = theme.Quaternary;
            }
            else if (c is ListBox lb)
            {
                lb.BackColor = theme.Primary;
                lb.ForeColor = theme.Tertiary;
                lb.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (c is CustomButton cbut)
            {
                cbut.ForeColor = theme.Tertiary;
                cbut.BackColor = theme.Primary;
                cbut.DisabledForeColor = Color.FromArgb(255, (int)(theme.Tertiary.R * 0.5), (int)(theme.Tertiary.G * 0.5), (int)(theme.Tertiary.B * 0.5));
            }
            else if (c is ListView lv)
            {
                lv.BackColor = theme.Primary;
                lv.ForeColor = theme.Tertiary;
                lv.BorderStyle = BorderStyle.FixedSingle;

                lv.OwnerDraw = true;
                lv.DrawColumnHeader += (sender, e) =>
                {
                    using var backbrush = new SolidBrush(theme.Secondary);
                    e.Graphics.FillRectangle(backbrush, e.Bounds);

                    string text = e.Header?.Text ?? null;
                    int padding = TextRenderer.MeasureText(" ", e.Font).Width;
                    Rectangle newBounds = Rectangle.Inflate(e.Bounds, -padding, 0);

                    TextRenderer.DrawText(e.Graphics, text, e.Font, newBounds, theme.Tertiary, TextFormatFlags.VerticalCenter);
                };

                lv.DrawItem += (sender, e) =>
                {
                    e.DrawDefault = true;
                };

                lv.DrawSubItem += (sender, e) =>
                {
                    e.DrawDefault = true;
                };
            }
            else if (c is CustomNumericUpDown cnud)
            {
                cnud.BackColor = theme.Primary;
                cnud.ForeColor = theme.Tertiary;
                cnud.BorderColor = theme.Quaternary;
                cnud.ButtonHighlightColor = theme.Quinary;
                cnud.ButtonColor = theme.Quaternary;
            }
            else if (c is CustomComboBox ccb)
            {
                ccb.BackColor = theme.Primary;
                ccb.ForeColor = theme.Tertiary;
                ccb.ButtonColor = theme.Secondary;
                ccb.BorderColor = theme.Quaternary;
                ccb.HeaderColor = theme.Secondary;
                ccb.TextHoverColor = theme.Tertiary;
            }
            else if (c is ComboBox cb)
            {
                cb.BackColor = theme.Primary;
                cb.ForeColor = theme.Tertiary;
                cb.FlatStyle = FlatStyle.Flat;
            }
            else if (c is TreeView tv)
            {
                tv.BackColor = theme.Primary;
                tv.ForeColor = theme.Tertiary;

                tv.LineColor = theme.Quaternary;

                tv.DrawMode = TreeViewDrawMode.OwnerDrawText;

                // Special thanks to https://stackoverflow.com/a/21199864
                tv.DrawNode += (sender, e) =>
                {
                    if (e.Node == null) return;

                    // if treeview's HideSelection property is "True", 
                    // this will always returns "False" on unfocused treeview
                    var selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected;
                    var unfocused = !e.Node.TreeView.Focused;

                    // we need to do owner drawing only on a selected node
                    // and when the treeview is unfocused, else let the OS do it for us
                    if (selected)
                    {
                        var font = e.Node.NodeFont ?? e.Node.TreeView.Font;
                        e.Graphics.FillRectangle(new SolidBrush(theme.Senary), e.Bounds);
                        TextRenderer.DrawText(e.Graphics, e.Node.Text, font, e.Bounds, theme.Tertiary, TextFormatFlags.GlyphOverhangPadding);
                    }
                    else
                    {
                        e.DrawDefault = true;
                    }
                };
            }
            else if (c is MenuStrip ms)
            {
                ms.BackColor = theme.Primary;
                ms.ForeColor = theme.Tertiary;

                if (ms.Items != null && ms.Items.Count > 0)
                {
                    foreach (ToolStripItem item in ms.Items)
                    {
                        if (item is ToolStripMenuItem menuItem)
                            attc_menustrip_recursive(menuItem, theme);
                        else
                        {
                            item.BackColor = theme.Primary;
                            item.ForeColor = theme.Tertiary;
                        }
                    }
                }

                ms.RenderMode = ToolStripRenderMode.Professional;
                ms.Renderer = new CustomToolStripRenderer(theme.Senary, theme.Senary, theme.Quaternary, theme.Quinary, theme.Quinary, theme.Secondary, Color.Transparent, Color.Transparent, Color.Transparent, theme.Quaternary, theme.Quaternary);
            }
            else if (c is ToolStrip ts)
            {
                ts.BackColor = theme.Primary;
                ts.ForeColor = theme.Tertiary;

                if (ts.Items != null && ts.Items.Count > 0)
                {
                    foreach (ToolStripItem item in ts.Items)
                    {
                        item.ForeColor = theme.Tertiary;
                        item.BackColor = Color.Transparent;
                    }
                }

                ts.RenderMode = ToolStripRenderMode.Professional;
                ts.Renderer = new CustomToolStripRenderer(theme.Senary, theme.Senary, theme.Quaternary, theme.Quinary, theme.Quinary, theme.Secondary, Color.Transparent, Color.Transparent, Color.Transparent, theme.Quaternary, theme.Quaternary);
            }
            else
            {
                c.BackColor = theme.Primary;
                c.ForeColor = theme.Tertiary;
            }

            if (c is ContextMenuStrip cms)
            {
                cms.RenderMode = ToolStripRenderMode.Professional;
                cms.Renderer = new CustomToolStripRenderer(theme.Senary, theme.Senary, theme.Quaternary, theme.Quinary, theme.Quinary, theme.Secondary, Color.Transparent, Color.Transparent, Color.Transparent, theme.Quaternary, theme.Quaternary);
            }
            else if (c.ContextMenuStrip != null)
            {
                c.ContextMenuStrip.RenderMode = ToolStripRenderMode.Professional;
                c.ContextMenuStrip.Renderer = new CustomToolStripRenderer(theme.Senary, theme.Senary, theme.Quaternary, theme.Quinary, theme.Quinary, theme.Secondary, Color.Transparent, Color.Transparent, Color.Transparent, theme.Quaternary, theme.Quaternary);
            }

            c.Invalidate();

            if (recursive)
                if (c.Controls != null && c.Controls.Count > 0)
                    foreach (Control control in c.Controls)
                    {
                        ApplyThemeToControl(theme, control, true);
                    }
        }
    }
}

using GUI.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    public partial class ThemeEditorDialog : Form
    {
        private IThemeData themeData_prev;
        public ThemeEditorDialog()
        {
            InitializeComponent();
        }

        private void customButton1_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox1.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void customButton2_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox2.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void customButton3_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox3.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void customButton4_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox4.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void customButton5_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox5.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void customButton6_Click(object sender, EventArgs e)
        {
            if (colorDialog1.ShowDialog(this) == DialogResult.OK)
            {
                textBox6.Text = colorDialog1.Color.R + ", " + colorDialog1.Color.G + ", " + colorDialog1.Color.B;
            }
        }

        private void ThemeEditorDialog_Load(object sender, EventArgs e)
        {
            textBox1.TextChanged += TextBox_TextChanged;
            textBox2.TextChanged += TextBox_TextChanged;
            textBox3.TextChanged += TextBox_TextChanged;
            textBox4.TextChanged += TextBox_TextChanged;
            textBox5.TextChanged += TextBox_TextChanged;
            textBox6.TextChanged += TextBox_TextChanged;
        }

        private void TextBox_TextChanged(object sender, EventArgs e)
        {
            timer1.Stop();
            timer1.Start();
        }

        private Color correctColor = Color.GhostWhite;
        private Color failColor = Color.Red;

        private void timer1_Tick(object sender, EventArgs e)
        {
            timer1.Stop();

            string name = textBox7.Text;
            if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a valid name.", "Invalid Name", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string[] sp1 = textBox1.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp1.Length != 3)
            {
                textBox1.ForeColor = failColor;
                return;
            }

            textBox1.ForeColor = correctColor;

            string[] sp2 = textBox2.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp2.Length != 3)
            {
                textBox2.ForeColor = failColor;
                return;
            }

            textBox2.ForeColor = correctColor;

            string[] sp3 = textBox3.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp3.Length != 3)
            {
                textBox3.ForeColor = failColor;
                return;
            }

            textBox3.ForeColor = correctColor;

            string[] sp4 = textBox4.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp4.Length != 3)
            {
                textBox4.ForeColor = failColor;
                return;
            }

            textBox4.ForeColor = correctColor;

            string[] sp5 = textBox5.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp5.Length != 3)
            {
                textBox5.ForeColor = failColor;
                return;
            }

            textBox5.ForeColor = correctColor;

            string[] sp6 = textBox6.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (sp6.Length != 3)
            {
                textBox6.ForeColor = failColor;
                return;
            }

            textBox6.ForeColor = correctColor;

            if (!byte.TryParse(sp1[0], out byte s1_r))
            {
                textBox1.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp1[1], out byte s1_g))
            {
                textBox1.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp1[2], out byte s1_b))
            {
                textBox1.ForeColor = failColor;
                return;
            }

            textBox1.ForeColor = correctColor;

            if (!byte.TryParse(sp2[0], out byte s2_r))
            {
                textBox2.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp2[1], out byte s2_g))
            {
                textBox2.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp2[2], out byte s2_b))
            {
                textBox2.ForeColor = failColor;
                return;
            }

            textBox2.ForeColor = correctColor;

            if (!byte.TryParse(sp3[0], out byte s3_r))
            {
                textBox3.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp3[1], out byte s3_g))
            {
                textBox3.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp3[2], out byte s3_b))
            {
                textBox3.ForeColor = failColor;
                return;
            }

            textBox3.ForeColor = correctColor;

            if (!byte.TryParse(sp4[0], out byte s4_r))
            {
                textBox4.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp4[1], out byte s4_g))
            {
                textBox4.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp4[2], out byte s4_b))
            {
                textBox4.ForeColor = failColor;
                return;
            }

            textBox4.ForeColor = correctColor;

            if (!byte.TryParse(sp5[0], out byte s5_r))
            {
                textBox5.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp5[1], out byte s5_g))
            {
                textBox5.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp5[2], out byte s5_b))
            {
                textBox5.ForeColor = failColor;
                return;
            }

            textBox5.ForeColor = correctColor;

            if (!byte.TryParse(sp6[0], out byte s6_r))
            {
                textBox6.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp6[1], out byte s6_g))
            {
                textBox6.ForeColor = failColor;
                return;
            }
            if (!byte.TryParse(sp5[2], out byte s6_b))
            {
                textBox6.ForeColor = failColor;
                return;
            }

            textBox6.ForeColor = correctColor;

            ThemeCustom tc = new ThemeCustom(name, Color.FromArgb(255, s1_r, s1_g, s1_b), Color.FromArgb(255, s2_r, s2_g, s2_b), Color.FromArgb(255, s3_r, s3_g, s3_b), Color.FromArgb(255, s4_r, s4_g, s4_b), Color.FromArgb(255, s5_r, s5_g, s5_b), Color.FromArgb(255, s6_r, s6_g, s6_b));

            themeData_prev = MainForm.ThemeManager.CurrentTheme;
            MainForm.ThemeManager.ApplyTheme(tc);
        }

        private void customButton7_Click(object sender, EventArgs e)
        {
            if (themeData_prev != null)
                MainForm.ThemeManager.ApplyTheme(themeData_prev);

            Close();
        }

        private void customButton8_Click(object sender, EventArgs e)
        {
            Settings.Config.CurrentTheme = SettingsThemeState.FromThemeData(MainForm.ThemeManager.CurrentTheme);
            Settings.Save();

            Close();
        }
    }
}

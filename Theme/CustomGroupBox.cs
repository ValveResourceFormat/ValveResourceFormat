// Special thanks to https://stackoverflow.com/a/51663475
// Helped me a lot to save time!

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GUI.Theme
{
    static class GraphicsExtension
    {
        private static GraphicsPath GenerateRoundedRectangle(
            this Graphics graphics,
            RectangleF rectangle,
            float radius)
        {
            float diameter;
#pragma warning disable CA2000 // Dispose objects before losing scope
            GraphicsPath path = new GraphicsPath();
#pragma warning restore CA2000 // Dispose objects before losing scope
            if (radius <= 0.0F)
            {
                path.AddRectangle(rectangle);
                path.CloseFigure();
                return path;
            }
            else
            {
                if (radius >= (Math.Min(rectangle.Width, rectangle.Height)) / 2.0)
                    return graphics.GenerateCapsule(rectangle);
                diameter = radius * 2.0F;
                SizeF sizeF = new SizeF(diameter, diameter);
                RectangleF arc = new RectangleF(rectangle.Location, sizeF);
                path.AddArc(arc, 180, 90);
                arc.X = rectangle.Right - diameter;
                path.AddArc(arc, 270, 90);
                arc.Y = rectangle.Bottom - diameter;
                path.AddArc(arc, 0, 90);
                arc.X = rectangle.Left;
                path.AddArc(arc, 90, 90);
                path.CloseFigure();
            }
            return path;
        }

        private static GraphicsPath GenerateCapsule(
            this Graphics graphics,
            RectangleF baseRect)
        {
            float diameter;
            RectangleF arc;
            GraphicsPath path = new GraphicsPath();
            try
            {
                if (baseRect.Width > baseRect.Height)
                {
                    diameter = baseRect.Height;
                    SizeF sizeF = new SizeF(diameter, diameter);
                    arc = new RectangleF(baseRect.Location, sizeF);
                    path.AddArc(arc, 90, 180);
                    arc.X = baseRect.Right - diameter;
                    path.AddArc(arc, 270, 180);
                }
                else if (baseRect.Width < baseRect.Height)
                {
                    diameter = baseRect.Width;
                    SizeF sizeF = new SizeF(diameter, diameter);
                    arc = new RectangleF(baseRect.Location, sizeF);
                    path.AddArc(arc, 180, 180);
                    arc.Y = baseRect.Bottom - diameter;
                    path.AddArc(arc, 0, 180);
                }
                else path.AddEllipse(baseRect);
            }
            catch { path.AddEllipse(baseRect); }
            finally { path.CloseFigure(); }
            return path;
        }

        /// <summary>
        /// Draws a rounded rectangle specified by a pair of coordinates, a width, a height and the radius
        /// for the arcs that make the rounded edges.
        /// </summary>
        /// <param name="brush">System.Drawing.Pen that determines the color, width and style of the rectangle.</param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to draw.</param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to draw.</param>
        /// <param name="width">Width of the rectangle to draw.</param>
        /// <param name="height">Height of the rectangle to draw.</param>
        /// <param name="radius">The radius of the arc used for the rounded edges.</param>
        public static void DrawRoundedRectangle(
            this Graphics graphics,
            Pen pen,
            float x,
            float y,
            float width,
            float height,
            float radius)
        {
            RectangleF rectangle = new RectangleF(x, y, width, height);
            using GraphicsPath path = graphics.GenerateRoundedRectangle(rectangle, radius);
            SmoothingMode old = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawPath(pen, path);
            graphics.SmoothingMode = old;
        }

        /// <summary>
        /// Draws a rounded rectangle specified by a pair of coordinates, a width, a height and the radius
        /// for the arcs that make the rounded edges.
        /// </summary>
        /// <param name="brush">System.Drawing.Pen that determines the color, width and style of the rectangle.</param>
        /// <param name="x">The x-coordinate of the upper-left corner of the rectangle to draw.</param>
        /// <param name="y">The y-coordinate of the upper-left corner of the rectangle to draw.</param>
        /// <param name="width">Width of the rectangle to draw.</param>
        /// <param name="height">Height of the rectangle to draw.</param>
        /// <param name="radius">The radius of the arc used for the rounded edges.</param>

        public static void DrawRoundedRectangle(
            this Graphics graphics,
            Pen pen,
            int x,
            int y,
            int width,
            int height,
            int radius)
        {
            graphics.DrawRoundedRectangle(
                pen,
                Convert.ToSingle(x),
                Convert.ToSingle(y),
                Convert.ToSingle(width),
                Convert.ToSingle(height),
                Convert.ToSingle(radius));
        }
    }

    public class CustomGroupBox : GroupBox
    {
        private Color _borderColor = Color.Black;
        private int _borderWidth = 2;
        private int _borderRadius = 5;
        private int _textIndent = 10;

        public CustomGroupBox() : base()
        {
            InitializeComponent();
            this.Paint += this.BorderedGroupBox_Paint;
        }

        public CustomGroupBox(int width, float radius, Color color) : base()
        {
            this._borderWidth = Math.Max(1, width);
            this._borderColor = color;
            this._borderRadius = (int)Math.Max(0, radius);
            InitializeComponent();
            this.Paint += this.BorderedGroupBox_Paint;
        }

        public Color BorderColor
        {
            get => this._borderColor;
            set
            {
                this._borderColor = value;
                DrawGroupBox();
            }
        }

        public int BorderWidth
        {
            get => this._borderWidth;
            set
            {
                if (value > 0)
                {
                    this._borderWidth = Math.Min(value, 10);
                    DrawGroupBox();
                }
            }
        }

        public int BorderRadius
        {
            get => this._borderRadius;
            set
            {   // Setting a radius of 0 produces square corners...
                if (value >= 0)
                {
                    this._borderRadius = value;
                    this.DrawGroupBox();
                }
            }
        }

        public int LabelIndent
        {
            get => this._textIndent;
            set
            {
                this._textIndent = value;
                this.DrawGroupBox();
            }
        }

        private void BorderedGroupBox_Paint(object sender, PaintEventArgs e) =>
            DrawGroupBox(e.Graphics);

        private void DrawGroupBox() =>
            this.DrawGroupBox(this.CreateGraphics());

        private void DrawGroupBox(Graphics g)
        {
            using Brush textBrush = new SolidBrush(this.ForeColor);
            SizeF strSize = g.MeasureString(this.Text, this.Font);

            using Brush borderBrush = new SolidBrush(this.BorderColor);
            using Pen borderPen = new Pen(borderBrush, (float)this._borderWidth);
            Rectangle rect = new Rectangle(this.ClientRectangle.X,
                                            this.ClientRectangle.Y + (int)(strSize.Height / 2),
                                            this.ClientRectangle.Width - 1,
                                            this.ClientRectangle.Height - (int)(strSize.Height / 2) - 1);

            using Brush labelBrush = new SolidBrush(this.BackColor);

            // Clear text and border
            g.Clear(this.BackColor);

            // Drawing Border (added "Fix" from Jim Fell, Oct 6, '18)
            int rectX = (0 == this._borderWidth % 2) ? rect.X + this._borderWidth / 2 : rect.X + 1 + this._borderWidth / 2;
            int rectHeight = (0 == this._borderWidth % 2) ? rect.Height - this._borderWidth / 2 : rect.Height - 1 - this._borderWidth / 2;
            // NOTE DIFFERENCE: rectX vs rect.X and rectHeight vs rect.Height
            g.DrawRoundedRectangle(borderPen, rectX, rect.Y, rect.Width, rectHeight, (float)this._borderRadius);

            // Draw text
            if (this.Text.Length > 0)
            {
                // Do some work to ensure we don't put the label outside
                // of the box, regardless of what value is assigned to the Indent:
                int width = (int)rect.Width, posX;
                posX = (this._textIndent < 0) ? Math.Max(0 - width, this._textIndent) : Math.Min(width, this._textIndent);
                posX = (posX < 0) ? rect.Width + posX - (int)strSize.Width : posX;
                g.FillRectangle(labelBrush, posX, 0, strSize.Width, strSize.Height);
                g.DrawString(this.Text, this.Font, textBrush, posX, 0);
            }
        }

        #region Component Designer generated code
        /// <summary>Required designer variable.</summary>
#pragma warning disable CA1859 // Use concrete types when possible for improved performance
#pragma warning disable CA1805 // Do not initialize unnecessarily
        private System.ComponentModel.IContainer components = null;
#pragma warning restore CA1805 // Do not initialize unnecessarily
#pragma warning restore CA1859 // Use concrete types when possible for improved performance

        /// <summary>Clean up any resources being used.</summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();

            base.Dispose(disposing);
        }

        /// <summary>Required method for Designer support - Don't modify!</summary>
        private void InitializeComponent() => components = new System.ComponentModel.Container();
        #endregion
    }
}

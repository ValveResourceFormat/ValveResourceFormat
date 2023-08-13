using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GUI.Controls;

// https://github.com/ahmetkakici/ProPictureBox
// MIT License, Copyright (c) 2014 Ahmet Kakıcı

class ProPictureBox : PictureBox
{

    readonly struct ProTransformation
    {
        public Point Translation { get { return _translation; } }
        public double Scale { get { return _scale; } }
        private readonly Point _translation;
        private readonly double _scale;

        public ProTransformation(Point translation, double scale)
        {
            _translation = translation;
            _scale = scale;
        }

        public Point ConvertToIm(Point p)
        {
            return new Point((int)(p.X * _scale + _translation.X), (int)(p.Y * _scale + _translation.Y));
        }

        public Size ConvertToIm(Size p)
        {
            return new Size((int)(p.Width * _scale), (int)(p.Height * _scale));
        }

        public Rectangle ConvertToIm(Rectangle r)
        {
            return new Rectangle(ConvertToIm(r.Location), ConvertToIm(r.Size));
        }

        public Point ConvertToPb(Point p)
        {
            return new Point((int)((p.X - _translation.X) / _scale), (int)((p.Y - _translation.Y) / _scale));
        }

        public Size ConvertToPb(Size p)
        {
            return new Size((int)(p.Width / _scale), (int)(p.Height / _scale));
        }

        public Rectangle ConvertToPb(Rectangle r)
        {
            return new Rectangle(ConvertToPb(r.Location), ConvertToPb(r.Size));
        }

        public ProTransformation SetTranslate(Point p)
        {
            return new ProTransformation(p, _scale);
        }

        public ProTransformation AddTranslate(Point p)
        {
            return SetTranslate(new Point(p.X + _translation.X, p.Y + _translation.Y));
        }

        public ProTransformation SetScale(double scale)
        {
            return new ProTransformation(_translation, scale);
        }
    }

    private Point? _clickedPoint;
    private ProTransformation _transformation;
    private ProTransformation Transformation
    {
        set
        {
            _transformation = FixTranslation(value);
            Invalidate();
        }
        get
        {
            return _transformation;
        }
    }

    public ProPictureBox()
    {
        _transformation = new ProTransformation(new Point(0, 0), 1.0f);
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
    }

    private ProTransformation FixTranslation(ProTransformation value)
    {
        var maxScale = Math.Max((double)Image.Width / ClientRectangle.Width, (double)Image.Height / ClientRectangle.Height);
        if (value.Scale > maxScale)
        {
            value = value.SetScale(maxScale);
        }

        var rectSize = value.ConvertToIm(ClientRectangle.Size);
        var max = new Size(Image.Width - rectSize.Width, Image.Height - rectSize.Height);

        value = value.SetTranslate((new Point(Math.Min(value.Translation.X, max.Width), Math.Min(value.Translation.Y, max.Height))));
        if (value.Translation.X < 0 || value.Translation.Y < 0)
        {
            value = value.SetTranslate(new Point(Math.Max(value.Translation.X, 0), Math.Max(value.Translation.Y, 0)));
        }
        return value;
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        var transformation = _transformation;
        var pos1 = transformation.ConvertToIm(e.Location);
        var scale = Transformation.Scale;

        if (e.Delta > 0)
        {
            scale /= 1.25f;
        }
        else
        {
            scale *= 1.25f;
        }

        if (scale < 0.05f)
        {
            scale = 0.05f;
        }

        transformation = transformation.SetScale(scale);

        var pos2 = transformation.ConvertToIm(e.Location);
        transformation = transformation.AddTranslate(pos1 - (Size)pos2);
        Transformation = transformation;
    }

    private void OnMouseUp(object sender, MouseEventArgs mouseEventArgs)
    {
        _clickedPoint = null;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_clickedPoint == null)
        {
            return;
        }

        var p = _transformation.ConvertToIm((Size)e.Location);
        Transformation = _transformation.SetTranslate(_clickedPoint.Value - p);
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        Focus();
        _clickedPoint = _transformation.ConvertToIm(e.Location);
    }

    protected override void OnPaint(PaintEventArgs pe)
    {
        var image = Image;

        if (Image is null || pe is null)
        {
            return;
        }

        var imRect = Transformation.ConvertToIm(ClientRectangle);

        pe.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        pe.Graphics.DrawImage(image, ClientRectangle, imRect, GraphicsUnit.Pixel);
    }
}

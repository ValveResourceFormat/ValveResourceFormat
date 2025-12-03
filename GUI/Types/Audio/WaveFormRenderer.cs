using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using NAudio.Wave;

namespace GUI.Types.Audio;

// Based on https://github.com/naudio/NAudio.WaveFormRenderer (MIT License - Copyright (c) 2021 NAudio)

internal class WaveFormRenderer
{
    private ISampleProvider Provider;
    private float[] ReadBuffer;

    public int Width { get; set; } = 800;
    public int TopHeight { get; set; } = 50;
    public int BottomHeight { get; set; } = 50;
    public int PixelsPerPeak { get; set; } = 4;
    public virtual Pen TopSpacerPen { get; set; }
    public virtual Pen BottomSpacerPen { get; set; }

    public Pen TopPeakPen
    {
        get
        {
            field ??= CreateGradientPen(TopHeight, Color.Yellow, Color.Green);
            return field;
        }
        set { field = value; }
    }


    public Pen BottomPeakPen
    {
        get
        {
            field ??= CreateGradientPen(TopHeight, Color.Red, Color.Blue);
            return field;
        }
        set { field = value; }
    }

    private static Pen CreateBottomPen(int topHeight, int bottomHeight)
    {
        var bottomGradient = new LinearGradientBrush(new Point(0, topHeight), new Point(0, topHeight + bottomHeight),
            Color.FromArgb(16, 16, 16), Color.FromArgb(150, 150, 150));
        var colorBlend = new ColorBlend(3);
        colorBlend.Colors[0] = Color.FromArgb(16, 16, 16);
        colorBlend.Colors[1] = Color.FromArgb(142, 142, 142);
        colorBlend.Colors[2] = Color.FromArgb(150, 150, 150);
        colorBlend.Positions[0] = 0;
        colorBlend.Positions[1] = 0.1f;
        colorBlend.Positions[2] = 1.0f;
        bottomGradient.InterpolationColors = colorBlend;
        return new Pen(bottomGradient);
    }

    protected static Pen CreateGradientPen(int height, Color startColor, Color endColor)
    {
        var brush = new LinearGradientBrush(new Point(0, 0), new Point(0, height), startColor, endColor);
        return new Pen(brush);
    }

    public Image Render(WaveStream waveStream)
    {
        var bytesPerSample = waveStream.WaveFormat.BitsPerSample / 8;
        var samples = waveStream.Length / (bytesPerSample);
        var samplesPerPixel = (int)(samples / Width);

        Provider = waveStream.ToSampleProvider();
        var samplesPerPeak = samplesPerPixel * PixelsPerPeak;
        samplesPerPeak -= samplesPerPeak % waveStream.WaveFormat.BlockAlign;
        ReadBuffer = new float[samplesPerPeak];

        var b = new Bitmap(Width, TopHeight + BottomHeight);
        b.MakeTransparent();

        using var g = Graphics.FromImage(b);

        var midPoint = TopHeight;

        var x = 0;
        var currentPeak = GetNextPeak();

        while (x < Width)
        {
            var nextPeak = GetNextPeak();

            for (var n = 0; n < PixelsPerPeak; n++)
            {
                var lineHeight = TopHeight * currentPeak.Max;
                g.DrawLine(TopPeakPen, x, midPoint, x, midPoint - lineHeight);
                lineHeight = BottomHeight * currentPeak.Min;
                g.DrawLine(BottomPeakPen, x, midPoint, x, midPoint - lineHeight);
                x++;
            }

            currentPeak = nextPeak;
        }

        return b;
    }

    public (float Min, float Max) GetNextPeak()
    {
        var samplesRead = Provider.Read(ReadBuffer, 0, ReadBuffer.Length);
        var max = (samplesRead == 0) ? 0 : ReadBuffer.Take(samplesRead).Max();
        var min = (samplesRead == 0) ? 0 : ReadBuffer.Take(samplesRead).Min();
        return (min, max);
    }
}

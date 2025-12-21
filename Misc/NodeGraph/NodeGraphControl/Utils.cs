using System;
using System.Drawing;

namespace NodeGraphControl
{
    public static class Utils
    {
        public static float Clamp(float min, float max, float value)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;

            return value;
        }

        public static int Clamp(int min, int max, int value)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;

            return value;
        }

        public static double Distance(PointF p1, PointF p2)
        {
            return Distance(p1.X, p1.Y, p2.X, p2.Y);
        }

        public static double Distance(float x1, float y1, float x2, float y2)
        {
            double xDelta = x1 - x2;
            double yDelta = y1 - y2;

            return Math.Sqrt(Math.Pow(xDelta, 2) + Math.Pow(yDelta, 2));
        }
    }
}

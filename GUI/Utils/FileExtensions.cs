using System;

namespace GUI.Utils
{
    internal static class FileExtensions
    {
        // http://stackoverflow.com/a/4975942/272647
        public static string ToFileSizeString(this uint byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            string result;

            if (byteCount == 0)
            {
                result = string.Format("{0} {1}", byteCount, suf[0]);
            }
            else
            {
                var absoluteByteCount = Math.Abs(byteCount);
                var place = Convert.ToInt32(Math.Floor(Math.Log(absoluteByteCount, 1024)));
                var num = Math.Round(absoluteByteCount / Math.Pow(1024, place), 1);
                result = string.Format("{0} {1}", Math.Sign(byteCount) * num, suf[place]);
            }

            return result;
        }
    }
}

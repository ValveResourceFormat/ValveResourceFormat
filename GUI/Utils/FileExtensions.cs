using System;

namespace GUI.Utils
{
    public static class FileExtensions
    {
        /// <summary>
        /// http://stackoverflow.com/a/4975942/272647
        /// </summary>
        /// <returns></returns>
        public static string ToFileSizeString(this uint byteCount)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB", "PB", "EB" }; //Longs run out around EB
            string result = String.Empty;

            if (byteCount == 0)
            {
                result = String.Format("{0} {1}", byteCount, suf[0]);
            }
            else
            {
                long absoluteByteCount = Math.Abs(byteCount);
                int place = Convert.ToInt32(Math.Floor(Math.Log(absoluteByteCount, 1024)));
                double num = Math.Round(absoluteByteCount / Math.Pow(1024, place), 1);
                result = String.Format("{0} {1}", (Math.Sign(byteCount) * num).ToString(), suf[place]);
            }

            return result;
        }
    }
}
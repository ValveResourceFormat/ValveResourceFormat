using System;
using ValveResourceFormat.IO;

namespace Decompiler
{
    public class ConsoleProgressReporter : IProgressReporter
    {
        public void SetProgress(string progress)
        {
            Console.WriteLine($"--- {progress}");
        }
    }
}

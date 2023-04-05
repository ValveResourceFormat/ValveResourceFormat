using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GUI.Controls;
using OpenTK;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    internal class RenderLoopThread
    {
        private const int TicksPerMillisecond = 10_000;
        private const long TicksPerSecond = TicksPerMillisecond * 1_000;

        private static class WinApi
        {
            [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            public static extern uint TimeBeginPeriod(uint uMilliseconds);

            [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            public static extern uint TimeEndPeriod(uint uMilliseconds);
        }

        private static int instances;
        private static Thread loopThread;
        private static GLControl currentGLControl;

        public static void RegisterInstance()
        {
            if (instances++ == 0)
            {
                Start();
            }

#if DEBUG
            Console.WriteLine($"Registered GL instance, current count: {instances}");
#endif
        }

        public static void UnregisterInstance()
        {
            if (--instances == 0)
            {
                Stop();
            }

#if DEBUG
            Console.WriteLine($"Unregistered GL instance, current count: {instances}");
#endif
        }

        public static void SetCurrentGLControl(GLControl glControl)
        {
            if (currentGLControl == null)
            {
                _ = WinApi.TimeBeginPeriod(1);
            }

            Interlocked.Exchange(ref currentGLControl, glControl);
        }

        public static void UnsetCurrentGLControl(GLControl glControl)
        {
            Interlocked.CompareExchange(ref currentGLControl, null, glControl);

            if (currentGLControl == null)
            {
                _ = WinApi.TimeEndPeriod(1);
            }
        }

        private static void Start()
        {
            loopThread = new Thread(RenderLoop)
            {
                Name = nameof(RenderLoopThread)
            };
            loopThread.Start();
        }

        public static void Stop()
        {
            loopThread = null;
        }

        private static void RenderLoop()
        {
#if DEBUG
            Console.WriteLine("RenderLoop thread started");
#endif

            var desiredInterval = TicksPerSecond / 120;

            while (instances > 0)
            {
                var nextFrame = Stopwatch.GetTimestamp() + desiredInterval;
                var control = currentGLControl;

                if (control == null)
                {
                    // If there is no control currently visible, sleep for 100ms
                    Thread.Sleep(100);
                    continue;
                }

                if (!control.Visible)
                {
                    // Work around the issue that VisibleChanged is not raised when control becomes invisible
                    UnsetCurrentGLControl(control);
                    continue;
                }

                // We're relying on the fact that Invalidate() will block for the duration of Draw() call
                try
                {
                    control.Invoke(control.Invalidate);
                }
                catch (ObjectDisposedException)
                {
                    // Due to wonky Invoke, checking IsDisposed is not enough
                }

                var currentTime = Stopwatch.GetTimestamp();
                var sleep = Math.Max(1, (int)(nextFrame - currentTime) / TicksPerMillisecond);

                Thread.Sleep(sleep);
            }

#if DEBUG
            Console.WriteLine("RenderLoop thread quit");
#endif
        }
    }
}

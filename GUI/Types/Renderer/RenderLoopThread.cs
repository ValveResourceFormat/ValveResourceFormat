using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK;

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
        private static GLViewerControl currentGLControl;

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

        public static void SetCurrentGLControl(GLViewerControl glControl)
        {
            if (currentGLControl == null)
            {
                _ = WinApi.TimeBeginPeriod(1);

#if DEBUG
                Console.WriteLine("Called TimeBeginPeriod");
#endif
            }

            Interlocked.Exchange(ref currentGLControl, glControl);
        }

        public static void UnsetCurrentGLControl(GLViewerControl glControl)
        {
            Interlocked.CompareExchange(ref currentGLControl, null, glControl);

            if (currentGLControl == null)
            {
                _ = WinApi.TimeEndPeriod(1);

#if DEBUG
                Console.WriteLine("Called TimeEndPeriod");
#endif
            }
        }

        public static void UnsetIfClosingParentOfCurrentGLControl(Control parentControl)
        {
            var glControl = currentGLControl;

            if (parentControl.Contains(glControl))
            {
                UnsetCurrentGLControl(glControl);
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

            var lastUpdate = Stopwatch.GetTimestamp();

            while (instances > 0)
            {
                var currentTime = Stopwatch.GetTimestamp();
                var elapsed = currentTime - lastUpdate;
                var control = currentGLControl;
                lastUpdate = currentTime;

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

                var desiredInterval = TicksPerSecond / Settings.Config.MaxFPS;
                var nextFrame = currentTime + desiredInterval;

                try
                {
                    control.Invoke(control.Draw, currentTime, elapsed);
                }
                catch (ObjectDisposedException)
                {
                    // Due to wonky Invoke, checking IsDisposed is not enough
                    UnsetCurrentGLControl(control);
                    continue;
                }

                currentTime = Stopwatch.GetTimestamp();
                var sleep = Math.Max(1, (int)(nextFrame - currentTime) / TicksPerMillisecond);

                Thread.Sleep(sleep);
            }

#if DEBUG
            Console.WriteLine("RenderLoop thread quit");
#endif
        }
    }
}

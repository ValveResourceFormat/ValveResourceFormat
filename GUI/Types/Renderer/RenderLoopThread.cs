using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    partial class RenderLoopThread
    {
        //private const int TicksPerMillisecond = 10_000;
        //private const long TicksPerSecond = TicksPerMillisecond * 1_000;

        private static int instances;
        private static Thread? loopThread;
        private static GLViewerControl? currentGLControl;
        private static readonly ManualResetEventSlim renderSignal = new(initialState: true);

        public static void Initialize(Form form)
        {
            form.Activated += OnAppActivated;

#if DEBUG
            CodeHotReloadService.CodeHotReloaded += OnAppActivated;
#endif
        }

        private static void OnAppActivated(object? sender, EventArgs e)
        {
            if (currentGLControl != null)
            {
                renderSignal.Set();
            }
        }

        public static void RegisterInstance()
        {
            if (Interlocked.Increment(ref instances) == 1)
            {
                Start();
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Registered GL instance, current count: {instances}");
#endif
        }

        public static void UnregisterInstance()
        {
            if (Interlocked.Decrement(ref instances) == 0)
            {
                renderSignal.Set();
                loopThread = null; // The thread should quit on its own
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), $"Unregistered GL instance, current count: {instances}");
#endif
        }

        public static void SetCurrentGLControl(GLViewerControl glControl)
        {
            var originalGlControl = Interlocked.Exchange(ref currentGLControl, glControl);

            /*
            if (originalGlControl == null)
            {
                _ = PInvoke.timeBeginPeriod(1);

#if DEBUG
                Log.Debug(nameof(RenderLoop), "Called TimeBeginPeriod");
#endif
            }
            */

            renderSignal.Set();
        }

        public static void UnsetCurrentGLControl(GLViewerControl glControl)
        {
            Interlocked.CompareExchange(ref currentGLControl, null, glControl);

            if (currentGLControl == null)
            {
                renderSignal.Reset();

                /*
                _ = PInvoke.timeEndPeriod(1);

#if DEBUG
                Log.Debug(nameof(RenderLoop), "Called TimeEndPeriod");
#endif
                */
            }
        }

        public static void UnsetIfClosingParentOfCurrentGLControl(Control parentControl)
        {
            var glControl = currentGLControl;

            if (glControl != null && parentControl.Contains(glControl.GLControl))
            {
                UnsetCurrentGLControl(glControl);
            }
        }

        private static void Start()
        {
            loopThread = new Thread(RenderLoop)
            {
                Name = nameof(RenderLoop)
            };
            loopThread.Start();
        }

        private static void RenderLoop()
        {
#if DEBUG
            Log.Debug(nameof(RenderLoop), "Thread started");
#endif

            while (instances > 0)
            {
                var control = currentGLControl;

                if (control == null)
                {
                    renderSignal.Wait();
                    continue;
                }

                if (control.GLControl is not { } glControl || !glControl.Visible)
                {
                    // Work around the issue that VisibleChanged is not raised when control becomes invisible
                    UnsetCurrentGLControl(control);
                    continue;
                }

                var currentTime = Stopwatch.GetTimestamp();

                var isPaused = !renderSignal.IsSet;

                if (!isPaused && Form.ActiveForm == null)
                {
                    isPaused = true;
                    renderSignal.Reset();
                }

                control.Draw(currentTime, isPaused);

                if (!renderSignal.IsSet)
                {
                    if (!isPaused)
                    {
                        control.Draw(currentTime, isPaused: true);
                    }

                    renderSignal.Wait();
                    continue;
                }

                /*
                var desiredInterval = TicksPerSecond / 144; // todo: max fps
                var nextFrame = currentTime + desiredInterval;
                currentTime = Stopwatch.GetTimestamp();
                var sleep = Math.Max(1, (int)(nextFrame - currentTime) / TicksPerMillisecond);

                Thread.Sleep(sleep);
                */
            }

#if DEBUG
            Log.Debug(nameof(RenderLoop), "Thread quit");
#endif
        }
    }
}

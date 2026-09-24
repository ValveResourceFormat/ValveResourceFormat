using System.Threading;
using System.Windows.Forms;

namespace GUI.Types.GLViewers
{
    partial class RenderLoopThread
    {
        //private const int TicksPerMillisecond = 10_000;
        //private const long TicksPerSecond = TicksPerMillisecond * 1_000;

        private static int threadHash;
        private static int instances;
        private static Thread? loopThread;
        private static GLBaseControl? currentGLControl;
        private static readonly ManualResetEventSlim renderSignal = new(initialState: true);

        public static void Initialize(Form form)
        {
            form.Activated += OnAppActivated;

#if DEBUG
            GUI.Utils.CodeHotReloadService.CodeHotReloaded += OnAppActivated;
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
                renderSignal.Reset();
                Start();
            }

#if DEBUG
            GUI.Utils.Log.Debug(nameof(RenderLoop), $"Registered GL instance, current count: {instances}");
#endif
        }

        public static void UnregisterInstance()
        {
            if (Interlocked.Decrement(ref instances) == 0)
            {
                Interlocked.Increment(ref threadHash);
                renderSignal.Set();
                loopThread = null; // The thread should quit on its own

                var detached = Interlocked.Exchange(ref currentGLControl, null);
                detached?.OnDetachedFromRenderLoop();
            }

#if DEBUG
            GUI.Utils.Log.Debug(nameof(RenderLoop), $"Unregistered GL instance, current count: {instances}");
#endif
        }

        public static bool IsCurrentGLControl(GLBaseControl glControl) => currentGLControl == glControl;

        public static void SetCurrentGLControl(GLBaseControl glControl)
        {
            var originalGlControl = Interlocked.Exchange(ref currentGLControl, glControl);

            if (loopThread == null)
            {
                Start();
            }

            if (originalGlControl != null && originalGlControl != glControl)
            {
                originalGlControl.OnDetachedFromRenderLoop();
            }

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

        public static void UnsetCurrentGLControl(GLBaseControl glControl)
        {
            Interlocked.CompareExchange(ref currentGLControl, null, glControl);

            glControl.OnDetachedFromRenderLoop();

            // With no instances left the loop has been told to quit.
            if (currentGLControl == null && Volatile.Read(ref instances) > 0)
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
                Name = nameof(RenderLoop),
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            loopThread.Start();
        }

        private static void RenderLoop()
        {
            var localHash = threadHash;

#if DEBUG
            GUI.Utils.Log.Debug(nameof(RenderLoop), $"Thread started (#{localHash})");
#endif

            while (threadHash == localHash)
            {
                if (currentGLControl == null)
                {
                    renderSignal.Wait();
                    continue;
                }

                if (DrawCurrentControl() is not { } presented)
                {
                    continue;
                }

                if (!renderSignal.IsSet)
                {
                    if (threadHash != localHash)
                    {
                        break;
                    }

                    renderSignal.Wait();
                    continue;
                }

                if (!presented)
                {
                    Thread.Sleep(1);
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
            GUI.Utils.Log.Debug(nameof(RenderLoop), $"Thread quit (#{localHash})");
#endif
        }

        /// <summary>
        /// Draws one frame of the current control, returning whether it was presented, or null when
        /// there was nothing to draw. The control is only referenced for the duration of this call.
        /// </summary>
        private static bool? DrawCurrentControl()
        {
            var control = currentGLControl;

            if (control == null || control.TryPrewarm())
            {
                return null;
            }

            if (control.GLControl is not { } glControl || !glControl.Visible)
            {
                // Work around the issue that VisibleChanged is not raised when control becomes invisible
                UnsetCurrentGLControl(control);
                return null;
            }

            var isPaused = !renderSignal.IsSet;

            if (!isPaused && Form.ActiveForm == null)
            {
                isPaused = true;
                renderSignal.Reset();
            }

            return control.Draw(isPaused);
        }
    }
}

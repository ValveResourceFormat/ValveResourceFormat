#if DEBUG
using System.Threading;
using System.Threading.Tasks;

namespace GUI.Automation;

/// <summary>
/// Replaces the wall clock frame time that drives the scene simulation, so that entities, particles,
/// animation and shader time can be held still or advanced by exact amounts. How fast frames are
/// drawn then no longer changes what they show, which is what makes two captures comparable.
/// </summary>
/// <remarks>
/// Only the simulation is affected. The camera and input keep real time, so a paused scene can still
/// be looked around. Auto exposure adapts with the simulation, so it holds while paused too.
/// </remarks>
internal static class AutomationClock
{
    private static readonly Lock Sync = new();

    private static float stepRemaining;
    private static float stepInterval;
    private static TaskCompletionSource? stepDone;

    public static bool IsPaused { get; private set; }

    public static bool IsStepping => stepDone != null;

    /// <summary>
    /// Called by the viewer each frame, on the render thread. Returns whether automation controls
    /// the clock, in which case <paramref name="timestep"/> has been replaced.
    /// </summary>
    public static bool Advance(ref float timestep)
    {
        TaskCompletionSource? finished = null;

        if (!Automation.IsEnabled)
        {
            return false;
        }

        using (Sync.EnterScope())
        {
            if (!IsPaused)
            {
                return false;
            }

            timestep = MathF.Min(stepInterval, stepRemaining);
            stepRemaining -= timestep;

            // Float leftovers smaller than any useful step would cost a whole extra frame
            if (stepDone != null && stepRemaining < 1e-5f)
            {
                stepRemaining = 0f;
                (finished, stepDone) = (stepDone, null);
            }
        }

        finished?.TrySetResult();
        return true;
    }

    /// <summary>Holds the simulation still, or goes back to real time, abandoning a step that is still running.</summary>
    public static void SetPaused(bool paused)
    {
        using (Sync.EnterScope())
        {
            IsPaused = paused;
        }

        Abandon();
    }

    /// <summary>
    /// Pauses, then advances <paramref name="seconds"/> of simulation in frames of
    /// <paramref name="interval"/>. Completes once the last of them has been simulated, or is
    /// cancelled when another step, a resume or <see cref="Abandon"/> replaces it.
    /// </summary>
    public static Task Step(float seconds, float interval)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource? replaced;

        using (Sync.EnterScope())
        {
            IsPaused = true;
            stepRemaining = seconds;
            stepInterval = interval;
            (replaced, stepDone) = (stepDone, done);
        }

        replaced?.TrySetCanceled();
        return done.Task;
    }

    /// <summary>Stops a step that is running where it got to, staying paused.</summary>
    public static void Abandon()
    {
        TaskCompletionSource? abandoned;

        using (Sync.EnterScope())
        {
            stepRemaining = 0f;
            (abandoned, stepDone) = (stepDone, null);
        }

        abandoned?.TrySetCanceled();
    }
}
#endif

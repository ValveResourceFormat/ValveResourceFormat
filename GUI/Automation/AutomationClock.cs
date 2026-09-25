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
/// be looked around. Auto exposure adapts with the simulation, so it holds while paused too, and the
/// dither pattern holds still so that identical frames are identical to the pixel.
/// </remarks>
internal static class AutomationClock
{
    /// <summary>The entity system's tick, which keeps each simulated frame to exactly one tick.</summary>
    public const float DefaultStepInterval = 1f / 64f;

    private static readonly Lock Sync = new();

    private static bool paused;
    private static float stepRemaining;
    private static float stepInterval = DefaultStepInterval;
    private static int stepFrames;
    private static TaskCompletionSource<int>? stepDone;

    public static bool IsPaused
    {
        get
        {
            using var _ = Sync.EnterScope();
            return paused;
        }
    }

    /// <summary>
    /// Called by the viewer each frame, on the render thread. Returns whether automation controls
    /// the clock, in which case <paramref name="timestep"/> has been replaced.
    /// </summary>
    public static bool Advance(ref float timestep)
    {
        TaskCompletionSource<int>? finished = null;
        var frames = 0;

        using (Sync.EnterScope())
        {
            if (!paused)
            {
                return false;
            }

            if (stepRemaining <= 0f)
            {
                timestep = 0f;
                return true;
            }

            timestep = MathF.Min(stepInterval, stepRemaining);
            stepRemaining -= timestep;
            stepFrames++;

            // Float leftovers smaller than any useful step would cost a whole extra frame.
            if (stepRemaining < 1e-5f)
            {
                stepRemaining = 0f;
                finished = stepDone;
                frames = stepFrames;
                stepDone = null;
            }
        }

        finished?.TrySetResult(frames);
        return true;
    }

    public static void Pause()
    {
        using var _ = Sync.EnterScope();
        paused = true;
    }

    /// <summary>Goes back to real time, abandoning a step that is still running.</summary>
    public static void Resume()
    {
        TaskCompletionSource<int>? abandoned;

        using (Sync.EnterScope())
        {
            paused = false;
            stepRemaining = 0f;
            abandoned = stepDone;
            stepDone = null;
        }

        abandoned?.TrySetCanceled();
    }

    /// <summary>
    /// Pauses, then advances <paramref name="seconds"/> of simulation in frames of
    /// <paramref name="interval"/>. Completes with the number of frames it took once the last of them
    /// has been simulated. The caller keeps the render loop going until then.
    /// </summary>
    public static Task<int> Step(float seconds, float interval)
    {
        TaskCompletionSource<int>? replaced;
        var done = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (Sync.EnterScope())
        {
            paused = true;
            stepRemaining = seconds;
            stepInterval = interval;
            stepFrames = 0;
            replaced = stepDone;
            stepDone = done;
        }

        replaced?.TrySetCanceled();

        return done.Task;
    }
}
#endif

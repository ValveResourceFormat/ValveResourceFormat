#if DEBUG
using System.Threading;

namespace GUI.Automation;

/// <summary>
/// Replaces the wall clock frame time that drives the scene simulation. Entities, particles,
/// animation and shader time can then stop or advance by exact amounts. The frame rate then does not
/// change what the frames show, so two captures are comparable.
/// </summary>
/// <remarks>
/// This affects only the simulation. The camera and input keep real time, so the camera can still
/// move in a paused scene. Auto exposure adapts with the simulation, so it also holds while paused.
/// </remarks>
internal static class AutomationClock
{
    private static readonly Lock Sync = new();

    private static volatile bool Paused;
    private static float StepRemaining;
    private static float StepInterval;

    public static bool IsPaused => Paused;

    /// <summary>Whether a step has simulation time left to advance.</summary>
    public static bool IsStepping => StepRemaining > 0f;

    /// <summary>How many more frames the running step advances over.</summary>
    public static int StepFramesLeft
    {
        get
        {
            using var _ = Sync.EnterScope();
            return (int)MathF.Ceiling(StepRemaining / StepInterval);
        }
    }

    /// <summary>
    /// Called by the viewer each frame, on the render thread. Returns whether automation controls
    /// the clock, in which case it replaced <paramref name="timestep"/>.
    /// </summary>
    public static bool Advance(ref float timestep)
    {
        // Running in real time is the usual case, which needs no lock
        if (!Automation.IsEnabled || !Paused)
        {
            return false;
        }

        using var _ = Sync.EnterScope();

        // Unpaused since the check above
        if (!Paused)
        {
            return false;
        }

        timestep = MathF.Min(StepInterval, StepRemaining);
        StepRemaining -= timestep;

        // Float leftovers smaller than any useful step would cost a whole extra frame
        if (StepRemaining < 1e-5f)
        {
            StepRemaining = 0f;
        }

        return true;
    }

    /// <summary>Holds the simulation still, or goes back to real time, ending a step where it got to.</summary>
    public static void SetPaused(bool paused)
    {
        using var _ = Sync.EnterScope();

        Paused = paused;
        StepRemaining = 0f;
    }

    /// <summary>
    /// Pauses, then advances <paramref name="seconds"/> of simulation over the frames that follow, in
    /// frames of <paramref name="interval"/>.
    /// </summary>
    public static void Step(float seconds, float interval)
    {
        using var _ = Sync.EnterScope();

        Paused = true;
        StepRemaining = seconds;
        StepInterval = interval;
    }
}
#endif

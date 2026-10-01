using System.Threading;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Editor;

/// <summary>Whether the world is frozen for inspecting and editing, or running.</summary>
public enum EditorMode
{
    /// <summary>The world is paused: entity logic does not run, and selection and tools are available.</summary>
    Viewer,

    /// <summary>The world runs: entity logic, inputs and outputs tick, and the player can be walked.</summary>
    Playing,
}

/// <summary>A change to <see cref="EditorState"/> asked for from outside the render thread.</summary>
public enum EditorRequest
{
    /// <summary>Start the world running.</summary>
    Play,

    /// <summary>Freeze the world, leaving walk mode.</summary>
    Pause,

    /// <summary>Play when paused, pause when playing.</summary>
    TogglePlay,

    /// <summary>Advance a paused world by one tick.</summary>
    Step,

    /// <summary>Walk as the player, which plays the world, or stop walking.</summary>
    ToggleWalk,
}

/// <summary>
/// The editor's mode, which everything else follows. Walking as the player needs the world running, so
/// walking plays and pausing stops walking. Requests can come from any thread and are applied by
/// <see cref="Update"/> on the render thread, where the entity world and the camera are owned.
/// </summary>
public sealed class EditorState
{
    private readonly EntitySystem entitySystem;
    private readonly UserInput input;

    private readonly Lock requestLock = new();
    private readonly List<EditorRequest> pendingRequests = [];

    private TrackedKeys previousKeys;
    private bool escapeFreedMouse;
    private bool roundStarted;

    /// <summary>Gets the current mode.</summary>
    public EditorMode Mode { get; private set; } = EditorMode.Viewer;

    /// <summary>Gets whether the camera is walking as the player.</summary>
    public bool IsWalking => input.WalkMode;

    /// <summary>Raised on the render thread when <see cref="Mode"/> changes.</summary>
    public event Action<EditorMode>? ModeChanged;

    /// <summary>Starts paused, with the entity world frozen until played.</summary>
    /// <param name="entitySystem">The entity world the mode runs and freezes.</param>
    /// <param name="input">The camera input, whose walk mode the state drives.</param>
    public EditorState(EntitySystem entitySystem, UserInput input)
    {
        this.entitySystem = entitySystem;
        this.input = input;

        entitySystem.Enabled = false;
    }

    /// <summary>Queues a change for the next <see cref="Update"/>.</summary>
    /// <param name="request">The change.</param>
    public void Request(EditorRequest request)
    {
        using var _ = requestLock.EnterScope();
        pendingRequests.Add(request);
    }

    /// <summary>
    /// Handles the keys that change the mode: X walks or stops walking, and escape first frees the
    /// mouse while walking, then on a second press stops walking and pauses. Call on the render thread
    /// with this frame's keys, before the input reads them.
    /// </summary>
    /// <param name="keys">The keys held this frame.</param>
    public void HandleKeys(TrackedKeys keys)
    {
        var pressed = keys & ~previousKeys;
        previousKeys = keys;

        if ((pressed & TrackedKeys.MouseLeftOrRight) != 0)
        {
            escapeFreedMouse = false;
        }

        if ((pressed & TrackedKeys.X) != 0)
        {
            Apply(EditorRequest.ToggleWalk);
        }
        else if (IsWalking && (pressed & TrackedKeys.Escape) != 0)
        {
            if (escapeFreedMouse)
            {
                escapeFreedMouse = false;
                Apply(EditorRequest.Pause);
            }
            else
            {
                escapeFreedMouse = true;
            }
        }
    }

    /// <summary>Applies the queued requests. Call on the render thread, before the entity world updates.</summary>
    public void Update()
    {
        EditorRequest[] requests;

        using (requestLock.EnterScope())
        {
            if (pendingRequests.Count == 0)
            {
                return;
            }

            requests = [.. pendingRequests];
            pendingRequests.Clear();
        }

        foreach (var request in requests)
        {
            Apply(request);
        }
    }

    private void Apply(EditorRequest request)
    {
        switch (request)
        {
            case EditorRequest.Play:
                SetMode(EditorMode.Playing);
                break;

            case EditorRequest.Pause:
                input.SetWalkMode(false);
                SetMode(EditorMode.Viewer);
                break;

            case EditorRequest.TogglePlay:
                Apply(Mode == EditorMode.Playing ? EditorRequest.Pause : EditorRequest.Play);
                break;

            case EditorRequest.Step:
                if (Mode == EditorMode.Viewer)
                {
                    StartRoundOnce();
                    entitySystem.Step();
                }

                break;

            case EditorRequest.ToggleWalk:
                if (IsWalking)
                {
                    input.SetWalkMode(false);
                }
                else
                {
                    SetMode(EditorMode.Playing);
                    input.SetWalkMode(true);
                }

                break;
        }
    }

    private void SetMode(EditorMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        entitySystem.Enabled = mode == EditorMode.Playing;

        if (mode == EditorMode.Playing)
        {
            StartRoundOnce();
        }

        ModeChanged?.Invoke(mode);
    }

    private void StartRoundOnce()
    {
        if (!roundStarted)
        {
            roundStarted = true;
            entitySystem.StartRound();
        }
    }
}

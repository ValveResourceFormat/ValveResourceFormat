using System.Threading;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Input;

namespace ValveResourceFormat.Editor;

/// <summary>What the world is being used for.</summary>
public enum EditorMode
{
    /// <summary>
    /// The tool mode for compiled assets: the world is frozen and rendered as the engine would, with aids
    /// for viewing and inspecting it, such as selection and entity details.
    /// </summary>
    Viewer,

    /// <summary>The world runs as in game: entity logic, inputs and outputs tick, and the player can be walked.</summary>
    Game,
}

/// <summary>A change to <see cref="EditorState"/> asked for from outside the render thread.</summary>
public enum EditorRequest
{
    /// <summary>Start running the world.</summary>
    EnterGame,

    /// <summary>Freeze the world where it is and return to viewing it, leaving walk mode.</summary>
    EnterViewer,

    /// <summary>Enter game mode from the viewer, or return to the viewer from game mode.</summary>
    ToggleGame,

    /// <summary>Advance the frozen world by one tick, in viewer mode.</summary>
    Step,

    /// <summary>Walk as the player, which enters game mode, or stop walking.</summary>
    ToggleWalk,
}

/// <summary>
/// The editor's mode, which everything else follows. Walking as the player needs the world running, so
/// walking enters game mode and returning to the viewer stops walking. Requests can come from any thread
/// and are applied by <see cref="Update"/> on the render thread, where the entity world and the camera are
/// owned.
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

    /// <summary>Starts in viewer mode, with the entity world frozen until game mode is entered.</summary>
    /// <param name="entitySystem">The entity world game mode runs.</param>
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
    /// mouse while walking, then on a second press returns to the viewer. Call on the render thread
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
                Apply(EditorRequest.EnterViewer);
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
            case EditorRequest.EnterGame:
                SetMode(EditorMode.Game);
                break;

            case EditorRequest.EnterViewer:
                input.SetWalkMode(false);
                SetMode(EditorMode.Viewer);
                break;

            case EditorRequest.ToggleGame:
                Apply(Mode == EditorMode.Game ? EditorRequest.EnterViewer : EditorRequest.EnterGame);
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
                    SetMode(EditorMode.Game);
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
        entitySystem.Enabled = mode == EditorMode.Game;

        if (mode == EditorMode.Game)
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

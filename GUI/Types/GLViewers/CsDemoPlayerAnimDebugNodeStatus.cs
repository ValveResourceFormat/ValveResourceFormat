namespace GUI.Types.GLViewers;

/// <summary>Per-player model/animation warnings for the anim debug overlay.</summary>
public readonly record struct CsDemoPlayerAnimDebugNodeStatus(
    bool MissingModel,
    bool FallbackAnim,
    bool MissingSkeleton,
    string? ClipLabel);

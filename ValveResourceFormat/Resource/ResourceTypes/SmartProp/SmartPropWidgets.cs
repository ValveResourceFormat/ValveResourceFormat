namespace ValveResourceFormat.ResourceTypes.SmartProps
{
    /// <summary>
    /// A visual editing handle emitted while evaluating a smart prop element's modifier
    /// chain. Widgets capture the active coordinate frame at the exact point in the
    /// modifier list where they are defined, positioned in world space.
    /// </summary>
    /// <param name="ElementId">Element the widget belongs to.</param>
    /// <param name="WorldMatrix">World transform of the element at the widget's chain position.</param>
    /// <param name="Position">World space anchor point of the widget.</param>
    /// <param name="PitchYawRoll">World space rotation of the widget's frame, in degrees.</param>
    /// <param name="Name">Display name, empty when the source field is unset.</param>
    public abstract record SmartPropWidget(
        int ElementId,
        Matrix4x4 WorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        string Name);

    /// <summary>
    /// A locator marker: three coordinate axis arms at an offset from the element origin,
    /// scaled by a display factor clamped to at least 0.01.
    /// </summary>
    public sealed record SmartPropLocatorWidget(
        int ElementId,
        Matrix4x4 WorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        string Name,
        Vector3 Offset,
        float DisplayScale) : SmartPropWidget(ElementId, WorldMatrix, Position, PitchYawRoll, Name);

    /// <summary>
    /// A rotation ring widget spinning about a world space axis at the element origin.
    /// Carries the ring radius (at least 1), the initial angle indicator in degrees and a
    /// display color with components in 0 to 1.
    /// </summary>
    public sealed record SmartPropRotatorWidget(
        int ElementId,
        Matrix4x4 WorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        string Name,
        Vector3 Offset,
        Vector3 Axis,
        float Radius,
        float Angle,
        Vector3 Color,
        string OutputVariable = "",
        float? MinAngle = null,
        float? MaxAngle = null,
        float SnappingIncrement = 0f) : SmartPropWidget(ElementId, WorldMatrix, Position, PitchYawRoll, Name);

    /// <summary>Which of a sizer's six handles have output variables attached.</summary>
    public readonly record struct SmartPropSizerHandles(
        bool MinX,
        bool MaxX,
        bool MinY,
        bool MaxY,
        bool MinZ,
        bool MaxZ);

    /// <summary>Which of a sizer's axes are active at all.</summary>
    public readonly record struct SmartPropSizerAxes(bool X, bool Y, bool Z);

    /// <summary>Optional authored edit limits for each sizer axis.</summary>
    public readonly record struct SmartPropSizerConstraints(
        float? MinX,
        float? MaxX,
        float? MinY,
        float? MaxY,
        float? MinZ,
        float? MaxZ);

    /// <summary>
    /// A sizer widget: a wireframe box around the element with draggable bounds handles.
    /// Carries the initial min and max bounds, the per handle output variable presence
    /// and the per axis activity flags.
    /// </summary>
    public sealed record SmartPropSizerWidget(
        int ElementId,
        Matrix4x4 WorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        string Name,
        Vector3 MinBounds,
        Vector3 MaxBounds,
        SmartPropSizerHandles Handles,
        SmartPropSizerAxes ActiveAxes,
        string MinXVariable = "",
        string MaxXVariable = "",
        string MinYVariable = "",
        string MaxYVariable = "",
        string MinZVariable = "",
        string MaxZVariable = "",
        SmartPropSizerConstraints Constraints = default) : SmartPropWidget(ElementId, WorldMatrix, Position, PitchYawRoll, Name);

    /// <summary>
    /// The choice handle of a PickOne element: a small marker where the picked option is
    /// displayed in the editor. Carries the handle size (at least 1), a display color
    /// with components in 0 to 1, and the marker shape (SQUARE, DIAMOND or CIRCLE).
    /// </summary>
    public sealed record SmartPropPickOneHandleWidget(
        int ElementId,
        Matrix4x4 WorldMatrix,
        Vector3 Position,
        Vector3 PitchYawRoll,
        string Name,
        Vector3 Offset,
        float Size,
        Vector3 Color,
        string Shape) : SmartPropWidget(ElementId, WorldMatrix, Position, PitchYawRoll, Name);
}

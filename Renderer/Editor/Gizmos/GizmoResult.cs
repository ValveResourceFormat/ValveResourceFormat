namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// What a drag of a gizmo has done since it started, which Hammer hands the objects it moves on every update.
/// Each object goes back to where it was when the drag started and takes all of it again.
/// </summary>
public sealed class GizmoResult
{
    /// <summary>Gets or sets the part being dragged.</summary>
    public GizmoPart Part { get; set; } = GizmoPart.None;

    /// <summary>Gets or sets the move or turn since the drag started, in the world.</summary>
    public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;

    /// <summary>Gets or sets how far the drag has turned, in degrees.</summary>
    public float Angle { get; set; }

    /// <summary>Gets or sets the axis turned around, in the space of <see cref="Frame"/>.</summary>
    public Vector3 Axis { get; set; }

    /// <summary>Gets or sets what the drag turns and scales about.</summary>
    public Vector3 Pivot { get; set; }

    /// <summary>Gets or sets the axes of the gizmo, its rows the X, Y and Z axes.</summary>
    public Matrix4x4 Frame { get; set; } = Matrix4x4.Identity;

    /// <summary>Gets or sets where the first selected object is and how it is turned, without its scale.</summary>
    public Matrix4x4 Orientation { get; set; } = Matrix4x4.Identity;

    /// <summary>Gets or sets how much the drag scales along each axis of <see cref="Frame"/>.</summary>
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>Gets or sets where the dragged handle was from the pivot when the drag started, in the space of <see cref="Frame"/>.</summary>
    public Vector3 ScaleStart { get; set; }

    /// <summary>Gets or sets <see cref="ScaleStart"/> scaled by <see cref="Scale"/>.</summary>
    public Vector3 ScaleCurrent { get; set; }

    /// <summary>Gets or sets what the drag is.</summary>
    public GizmoResultFlags Flags { get; set; }

    internal void Reset()
    {
        Part = GizmoPart.None;
        Transform = Matrix4x4.Identity;
        Angle = 0f;
        Axis = Vector3.Zero;
        Scale = Vector3.One;
        ScaleStart = Vector3.Zero;
        ScaleCurrent = Vector3.Zero;
        Flags = GizmoResultFlags.None;
    }
}

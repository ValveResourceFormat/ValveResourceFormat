namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// Something a gizmo can move, turn or scale. A drag starts a transform, each update puts it back where it was when
/// the transform started and applies everything the drag has done since, and the end keeps it or puts it back.
/// </summary>
public interface IGizmoTransformable
{
    /// <summary>Gets where it is, which the pivot starts at when it is the first selected.</summary>
    Vector3 Origin { get; }

    /// <summary>Gets where it is and how it is turned, without its scale, which local space follows.</summary>
    Matrix4x4 RigidTransform { get; }

    /// <summary>Gets its bounds in a space, as tight as what it is made of is in that space.</summary>
    /// <param name="worldToSpace">The transform from the world into the space.</param>
    AABB GetBounds(in Matrix4x4 worldToSpace);

    /// <summary>Gets which ways it can be moved.</summary>
    GizmoTransformModes SupportedTransformModes { get; }

    /// <summary>Gets what it belongs to that is moved with it, or <see langword="null"/>.</summary>
    IGizmoTransformable? Parent { get; }

    /// <summary>Remembers where it is, which every update of the transform starts from.</summary>
    void BeginTransform();

    /// <summary>Puts it where a drag has taken it since the transform started.</summary>
    /// <param name="result">What the drag has done.</param>
    void ApplyGizmoTransform(GizmoResult result);

    /// <summary>Ends the transform, keeping where it was taken or putting it back.</summary>
    /// <param name="commit">Whether it stays where it was taken, which also writes it into the map.</param>
    void EndTransform(bool commit);
}

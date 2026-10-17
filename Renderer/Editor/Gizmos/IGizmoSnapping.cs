namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>What a snapped point may move along.</summary>
public enum GizmoSnapConstraint
{
    /// <summary>Anywhere.</summary>
    None,

    /// <summary>Only along a direction.</summary>
    Axis,

    /// <summary>Only within the plane across a direction.</summary>
    Plane,
}

/// <summary>How the drags of a gizmo snap, which Hammer's tools answer for the gizmos they show.</summary>
public interface IGizmoSnapping
{
    /// <summary>Gets the size of the grid.</summary>
    float GridSize { get; }

    /// <summary>Gets whether drags snap.</summary>
    bool IsSnappingEnabled { get; }

    /// <summary>Gets whether turns snap.</summary>
    bool IsRotationSnappingEnabled { get; }

    /// <summary>Snaps an angle of a turn.</summary>
    /// <param name="degrees">The angle.</param>
    /// <returns>The snapped angle.</returns>
    float SnapAngle(float degrees);

    /// <summary>Gets the smallest a scaled size may become: a step of the grid while snapping, a fraction of a unit otherwise.</summary>
    /// <param name="size">The size.</param>
    /// <returns>The size, made no smaller than the smallest.</returns>
    float ClampMinimumSize(float size);

    /// <summary>Snaps a point to the grid, keeping the part of the move the constraint does not allow.</summary>
    /// <param name="position">The point, snapped in place.</param>
    /// <param name="constraint">What the snap may move the point along.</param>
    /// <param name="direction">The direction of the constraint.</param>
    /// <returns>Whether the point was snapped to the grid.</returns>
    bool SnapPosition(ref Vector3 position, GizmoSnapConstraint constraint, Vector3 direction);
}

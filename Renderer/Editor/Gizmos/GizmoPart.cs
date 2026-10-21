namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>
/// The parts of a gizmo, numbered as Hammer numbers them for every gizmo, which is the order its pick objects
/// are drawn in.
/// </summary>
public enum GizmoPart
{
    /// <summary>No part.</summary>
    None = -1,

    /// <summary>The center of the translate gizmo, which moves freely.</summary>
    Center = 0,

    /// <summary>The center of the scale gizmo, which scales along every axis at once.</summary>
    UniformScale = 1,

    /// <summary>The sphere inside the rings of the rotate gizmo, which turns freely.</summary>
    Trackball = 2,

    /// <summary>The X axis, the ring around it, or the face of a box towards it.</summary>
    AxisX = 3,

    /// <summary>The Y axis, the ring around it, or the face of a box towards it.</summary>
    AxisY = 4,

    /// <summary>The Z axis, the ring around it, or the face of a box towards it.</summary>
    AxisZ = 5,

    /// <summary>The plane of the X and Y axes.</summary>
    PlaneXY = 6,

    /// <summary>The plane of the Y and Z axes.</summary>
    PlaneYZ = 7,

    /// <summary>The plane of the X and Z axes.</summary>
    PlaneXZ = 8,

    /// <summary>The face of a box away from the X axis.</summary>
    FaceNegativeX = 9,

    /// <summary>The face of a box away from the Y axis.</summary>
    FaceNegativeY = 10,

    /// <summary>The face of a box away from the Z axis.</summary>
    FaceNegativeZ = 11,
}

/// <summary>Which ways an object can be moved by a gizmo, as Hammer asks every object it would move.</summary>
[Flags]
public enum GizmoTransformModes
{
    /// <summary>Not at all.</summary>
    None = 0,

    /// <summary>Moved.</summary>
    Translate = 0x1,

    /// <summary>Turned.</summary>
    Rotate = 0x2,

    /// <summary>Scaled.</summary>
    Scale = 0x4,

    /// <summary>Scaled by the faces of its bounds, which also needs <see cref="Scale"/>.</summary>
    BoxScale = 0x8,

    /// <summary>Every way.</summary>
    All = Translate | Rotate | Scale | BoxScale,
}

/// <summary>What the selection is to a gizmo, which some gizmos read.</summary>
public enum GizmoSelectionKind
{
    /// <summary>One object, or none.</summary>
    Single,

    /// <summary>More than one object.</summary>
    Multiple,
}

/// <summary>What a drag of a gizmo did, which says how the objects it moves take its result.</summary>
[Flags]
public enum GizmoResultFlags
{
    /// <summary>A move.</summary>
    None = 0,

    /// <summary>Each object takes the result in its own space, about its own origin.</summary>
    Local = 0x8,

    /// <summary>A scale.</summary>
    Scale = 0x10,

    /// <summary>A scale along every axis at once.</summary>
    UniformScale = 0x20,

    /// <summary>A turn.</summary>
    Rotate = 0x40,
}

using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Editor.Gizmos;

/// <summary>Where a node of a map is, how it is turned, and how it is scaled along its own axes.</summary>
/// <param name="Origin">Where it is.</param>
/// <param name="Angles">How it is turned: pitch, yaw and roll in degrees.</param>
/// <param name="Scales">How it is scaled along its own axes.</param>
public readonly record struct MapPlacement(Vector3 Origin, Vector3 Angles, Vector3 Scales)
{
    /// <summary>Gets the transform from the space of the node to the world, without its scale.</summary>
    public Matrix4x4 RigidTransform => EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin);

    /// <summary>Gets the transform from the space of the node to the world.</summary>
    public Matrix4x4 Transform => Matrix4x4.CreateScale(Scales) * RigidTransform;
}

/// <summary>
/// How each kind of map node takes the result of a gizmo drag, as Hammer's nodes do: moved, turned about the pivot
/// or in place, and scaled either into their scales or, for meshes, into their vertices.
/// </summary>
public static class GizmoTransforms
{
    /// <summary>Gets where an entity is after a drag that started with it at a placement.</summary>
    /// <param name="placement">Where the entity was when the drag started.</param>
    /// <param name="result">What the drag has done.</param>
    /// <param name="isPath">Whether the entity is a path or a node of one, which scale about the pivot in the frame of the gizmo.</param>
    /// <param name="isSolid">Whether the entity is made of meshes, which scale themselves and leave the entity where it is.</param>
    /// <returns>The placement.</returns>
    public static MapPlacement TransformEntity(MapPlacement placement, GizmoResult result, bool isPath, bool isSolid)
    {
        ArgumentNullException.ThrowIfNull(result);

        var isLocal = (result.Flags & GizmoResultFlags.Local) != 0;

        if ((result.Flags & GizmoResultFlags.Scale) != 0)
        {
            if (isSolid)
            {
                return placement;
            }

            if (!isLocal && !isPath && (result.Flags & GizmoResultFlags.UniformScale) == 0)
            {
                return placement with
                {
                    Origin = Vector3.Transform(placement.Origin, ScaleAboutPointInFrame(result.Scale, result.Frame, result.Pivot)),
                    Scales = placement.Scales * GetNearestAxisScale(placement, result),
                };
            }

            var scale = isLocal
                ? Matrix4x4.CreateScale(result.Scale)
                : ScaleAboutPointInFrame(result.Scale, result.Frame, result.Pivot);

            // What the scale stretches each axis of the entity by, so it is kept along them and never shears
            var axes = isLocal ? Matrix4x4.Identity : placement.RigidTransform;
            var factors = new Vector3(
                Vector3.TransformNormal(GetRow(axes, 0), scale).Length(),
                Vector3.TransformNormal(GetRow(axes, 1), scale).Length(),
                Vector3.TransformNormal(GetRow(axes, 2), scale).Length());

            return placement with
            {
                Origin = isLocal ? placement.Origin : Vector3.Transform(placement.Origin, scale),
                Scales = placement.Scales * factors,
            };
        }

        if ((result.Flags & GizmoResultFlags.Rotate) != 0)
        {
            var rotation = GetRotation(result, isLocal);

            return placement with
            {
                Origin = isLocal ? placement.Origin : Vector3.Transform(placement.Origin, rotation),
                Angles = RotateAngles(placement.Angles, rotation, isLocal),
            };
        }

        return placement with { Origin = Vector3.Transform(placement.Origin, result.Transform) };
    }

    /// <summary>
    /// Gets where a mesh is after a drag that started with it at a placement, and what its vertices are transformed
    /// by when the drag scales it, which also keeps it from storing a scale.
    /// </summary>
    /// <param name="placement">Where the mesh was when the drag started.</param>
    /// <param name="result">What the drag has done.</param>
    /// <returns>The placement, and the transform of the vertices in the space of the mesh, or <see langword="null"/> when they stay.</returns>
    public static (MapPlacement Placement, Matrix4x4? VertexTransform) TransformMesh(MapPlacement placement, GizmoResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var isLocal = (result.Flags & GizmoResultFlags.Local) != 0;

        if ((result.Flags & GizmoResultFlags.Rotate) != 0)
        {
            var rotation = GetRotation(result, isLocal);

            return (placement with
            {
                Origin = isLocal ? placement.Origin : Vector3.Transform(placement.Origin, rotation),
                Angles = RotateAngles(placement.Angles, rotation, isLocal),
            }, null);
        }

        if ((result.Flags & GizmoResultFlags.Scale) == 0)
        {
            return (placement with { Origin = Vector3.Transform(placement.Origin, result.Transform) }, null);
        }

        var oldToWorld = placement.RigidTransform;
        Matrix4x4 scale;

        if (isLocal)
        {
            // A scale about the pivot in the space of the mesh, though it is then taken as one in the world
            var pivot = Matrix4x4.Invert(oldToWorld, out var worldToOld) ? Vector3.Transform(result.Pivot, worldToOld) : result.Pivot;
            scale = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateScale(result.Scale) * Matrix4x4.CreateTranslation(pivot);
        }
        else
        {
            scale = ScaleAboutPointInFrame(result.Scale, result.Frame, result.Pivot);
        }

        var moved = placement with { Origin = isLocal ? placement.Origin : Vector3.Transform(placement.Origin, scale) };

        if (!Matrix4x4.Invert(moved.RigidTransform, out var worldToNew))
        {
            return (moved, null);
        }

        var vertexTransform = oldToWorld * scale * worldToNew;

        if (isLocal)
        {
            vertexTransform.Translation = Vector3.Zero;
        }

        return (moved, vertexTransform);
    }

    /// <summary>
    /// Gets a scale about a point along the axes of a frame: into the frame, scaled about the point there, and back.
    /// </summary>
    /// <param name="scale">How much to scale along each axis of the frame.</param>
    /// <param name="frame">The frame, its rows the axes.</param>
    /// <param name="pivot">The point, in the world.</param>
    public static Matrix4x4 ScaleAboutPointInFrame(Vector3 scale, Matrix4x4 frame, Vector3 pivot)
    {
        if (!Matrix4x4.Invert(frame, out var toFrame))
        {
            return Matrix4x4.Identity;
        }

        var framePivot = Vector3.Transform(pivot, toFrame);

        return toFrame
            * Matrix4x4.CreateTranslation(-framePivot)
            * Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateTranslation(framePivot)
            * frame;
    }

    /// <summary>
    /// Gets the angles of something turned by a rotation, in the world or in its own space. Only the turn of the
    /// rotation is taken, through angles, so a scale in it is dropped.
    /// </summary>
    /// <param name="angles">How it was turned: pitch, yaw and roll in degrees.</param>
    /// <param name="rotation">The rotation.</param>
    /// <param name="isLocal">Whether the rotation is in its own space.</param>
    public static Vector3 RotateAngles(Vector3 angles, Matrix4x4 rotation, bool isLocal)
    {
        var turn = EntityTransformHelper.EulerAnglesToRotationMatrix(ToAngles(rotation));
        var current = EntityTransformHelper.EulerAnglesToRotationMatrix(angles);

        return ToAngles(isLocal ? turn * current : current * turn);
    }

    // The turn of a drag about its axis, about the pivot in the world or in place in the space of each object
    private static Matrix4x4 GetRotation(GizmoResult result, bool isLocal)
    {
        var axis = isLocal ? result.Axis : Vector3.TransformNormal(result.Axis, result.Frame);
        var rotation = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(axis), float.DegreesToRadians(result.Angle));

        return isLocal
            ? rotation
            : Matrix4x4.CreateTranslation(-result.Pivot) * rotation * Matrix4x4.CreateTranslation(result.Pivot);
    }

    private static Vector3 ToAngles(Matrix4x4 rotation)
    {
        if (!Matrix4x4.Decompose(rotation, out _, out var turn, out _))
        {
            return Vector3.Zero;
        }

        return EntityTransformHelper.ToEulerAngles(turn);
    }

    /// <summary>
    /// Gets how much a scale along the axes of the gizmo stretches each axis of an entity: each axis of the gizmo
    /// scales the one axis of the entity nearest it. Hammer instead scales every axis of the entity the dragged axis
    /// has a part along, so dragging one axis of the gizmo scales two of an entity turned by other than a quarter turn.
    /// </summary>
    private static Vector3 GetNearestAxisScale(MapPlacement placement, GizmoResult result)
    {
        var turn = placement.RigidTransform;
        var factors = Vector3.One;

        for (var gizmoAxis = 0; gizmoAxis < 3; gizmoAxis++)
        {
            if (result.Scale[gizmoAxis] == 1f)
            {
                continue;
            }

            var direction = GetRow(result.Frame, gizmoAxis);
            var nearest = 0;

            for (var axis = 1; axis < 3; axis++)
            {
                if (MathF.Abs(Vector3.Dot(GetRow(turn, axis), direction)) > MathF.Abs(Vector3.Dot(GetRow(turn, nearest), direction)))
                {
                    nearest = axis;
                }
            }

            factors[nearest] = result.Scale[gizmoAxis];
        }

        return factors;
    }

    private static Vector3 GetRow(in Matrix4x4 matrix, int row) => row switch
    {
        0 => new(matrix.M11, matrix.M12, matrix.M13),
        1 => new(matrix.M21, matrix.M22, matrix.M23),
        _ => new(matrix.M31, matrix.M32, matrix.M33),
    };
}

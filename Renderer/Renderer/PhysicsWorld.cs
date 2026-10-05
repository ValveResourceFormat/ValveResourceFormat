namespace ValveResourceFormat.Renderer;

/// <summary>
/// The static collision of one world group: the world physics of every map loaded into it, each placed
/// where its spawn group put it.
/// </summary>
/// <remarks>
/// Queries are moved into each shape's local space, so a swept box stays axis-aligned to the shape rather
/// than to the world. That is exact for a shape that is only moved, as nearly every spawn group is, and
/// only approximate for one that is also rotated.
/// </remarks>
public sealed class PhysicsWorld
{
    private readonly record struct Body(Scene Owner, Rubikon Shape, Matrix4x4 Transform, Matrix4x4 InverseTransform, bool IsIdentity)
    {
        public Vector3 ToLocal(Vector3 point) => IsIdentity ? point : Vector3.Transform(point, InverseTransform);

        // Distance survives the round trip because the transform is rigid
        public Rubikon.TraceResult ToWorld(Rubikon.TraceResult result)
        {
            if (!IsIdentity)
            {
                result.HitPosition = Vector3.Transform(result.HitPosition, Transform);
                result.ContactPoint = Vector3.Transform(result.ContactPoint, Transform);
                result.HitNormal = Vector3.Normalize(Vector3.TransformNormal(result.HitNormal, Transform));
            }

            return result;
        }
    }

    private readonly List<Body> bodies = [];

    /// <summary>Gets whether any collision has been added.</summary>
    public bool IsEmpty => bodies.Count == 0;

    /// <summary>Adds a shape, placed by a rigid transform.</summary>
    /// <param name="owner">The scene of the spawn group the shape belongs to, for <see cref="Remove"/>.</param>
    /// <param name="shape">The collision shape, in its own local space.</param>
    /// <param name="transform">The rigid transform placing it in the world.</param>
    public void Add(Scene owner, Rubikon shape, Matrix4x4 transform)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(shape);

        if (!Matrix4x4.Invert(transform, out var inverse))
        {
            inverse = Matrix4x4.Identity;
            transform = Matrix4x4.Identity;
        }

        bodies.Add(new Body(owner, shape, transform, inverse, transform.IsIdentity));
    }

    /// <summary>Removes every shape added for <paramref name="owner"/>.</summary>
    public void Remove(Scene owner) => bodies.RemoveAll(body => body.Owner == owner);

    /// <summary>Removes every shape.</summary>
    public void Clear() => bodies.Clear();

    /// <inheritdoc cref="Rubikon.TraceRay"/>
    public Rubikon.TraceResult TraceRay(Vector3 from, Vector3 to, string collisionName)
    {
        if (Rubikon.IsInvalidRay(from, to))
        {
            return new Rubikon.TraceResult { IsValid = false };
        }

        var closest = new Rubikon.TraceResult();

        foreach (var body in bodies)
        {
            closest.MinimizeWith(body.ToWorld(body.Shape.TraceRay(body.ToLocal(from), body.ToLocal(to), collisionName)));
        }

        return closest;
    }

    /// <inheritdoc cref="Rubikon.TraceAABB(Vector3, Vector3, Vector3, string, bool, bool)"/>
    public Rubikon.TraceResult TraceAABB(Vector3 from, Vector3 to, Vector3 halfExtents, string collisionName, bool detectStartSolid = false, bool computeContactPoint = false)
    {
        if (Rubikon.IsInvalidRay(from, to))
        {
            return new Rubikon.TraceResult { IsValid = false };
        }

        var closest = new Rubikon.TraceResult();

        foreach (var body in bodies)
        {
            var result = body.Shape.TraceAABB(body.ToLocal(from), body.ToLocal(to), halfExtents, collisionName, detectStartSolid, computeContactPoint);

            // A start-solid overlap outranks a touch at the same distance, as it does within one shape
            if (!result.Hit || result.Distance > closest.Distance
                || (result.Distance == closest.Distance && (closest.StartSolid || !result.StartSolid)))
            {
                continue;
            }

            closest = body.ToWorld(result);

            if (closest.StopsScanning(detectStartSolid))
            {
                break;
            }
        }

        return closest;
    }

    /// <inheritdoc cref="Rubikon.IntersectsAABB"/>
    public bool IntersectsAABB(Vector3 center, Vector3 halfExtents, string collisionName)
    {
        foreach (var body in bodies)
        {
            if (body.Shape.IntersectsAABB(body.ToLocal(center), halfExtents, collisionName))
            {
                return true;
            }
        }

        return false;
    }
}

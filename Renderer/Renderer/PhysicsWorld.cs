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

    /// <summary>Gets whether there is nothing to collide with.</summary>
    public bool IsEmpty => bodies.Count == 0 && GroundPlane is null;

    /// <summary>
    /// Gets or sets the height of an infinite horizontal plane that is solid below, traced on top of
    /// the shapes as untagged geometry, or null for none.
    /// </summary>
    public float? GroundPlane { get; set; }

    private float? GetGroundPlane(string collisionName)
        => Rubikon.SkipsCollision(collisionName, [], []) ? null : GroundPlane;

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

        // A ray from under the plane passes through, as it does through the back of a triangle
        if (GetGroundPlane(collisionName) is { } height && from.Z >= height)
        {
            closest.MinimizeWith(TracePlane(from, to, Vector3.Zero, Vector3.UnitZ, height));
        }

        return closest;
    }

    /// <summary>Traces a ray, giving the result as where along it the hit is.</summary>
    /// <param name="from">Start of the ray.</param>
    /// <param name="to">End of the ray.</param>
    /// <param name="collisionName">Collision interaction name used to filter shapes.</param>
    /// <param name="position">The nearest hit point, or <paramref name="to"/> on a miss.</param>
    /// <param name="normal">The surface normal at the hit point, or zero on a miss.</param>
    /// <param name="fraction">How far along the ray the hit is, from 0 at its start to 1 at its end, which it is on a miss.</param>
    /// <returns>Whether the ray hit anything.</returns>
    public bool TraceRay(Vector3 from, Vector3 to, string collisionName, out Vector3 position, out Vector3 normal, out float fraction)
    {
        var result = TraceRay(from, to, collisionName);

        if (!result.Hit)
        {
            position = to;
            normal = Vector3.Zero;
            fraction = 1f;
            return false;
        }

        position = result.HitPosition;
        normal = result.HitNormal;
        fraction = result.Distance / Vector3.Distance(from, to);
        return true;
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

        if (GetGroundPlane(collisionName) is { } height)
        {
            closest.MinimizeWith(TracePlane(from, to, halfExtents, Vector3.UnitZ, height, detectStartSolid));
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

        return GetGroundPlane(collisionName) is { } height && center.Z - halfExtents.Z < height;
    }

    /// <summary>
    /// Sweeps a box against a half-space that is solid where the dot product of
    /// <paramref name="normal"/> and a point is at most <paramref name="planeOffset"/>.
    /// </summary>
    internal static Rubikon.TraceResult TracePlane(Vector3 from, Vector3 to, Vector3 halfExtents, Vector3 normal, float planeOffset, bool detectStartSolid = false)
    {
        // Extent of the box toward the plane along the normal (support half-width)
        var extent = MathF.Abs(normal.X) * halfExtents.X + MathF.Abs(normal.Y) * halfExtents.Y + MathF.Abs(normal.Z) * halfExtents.Z;

        // Signed gap of the box's nearest face to the plane at each end (>0 outside the solid)
        var gapFrom = Vector3.Dot(normal, from) - planeOffset - extent;
        var gapTo = Vector3.Dot(normal, to) - planeOffset - extent;

        if (gapFrom < 0f)
        {
            return new Rubikon.TraceResult(true, from, normal, 0f, -1) { StartSolid = detectStartSolid };
        }

        var closing = gapFrom - gapTo; // positive when the sweep moves toward the plane

        if (closing <= 0f)
        {
            return new Rubikon.TraceResult(); // moving away or parallel while outside
        }

        var fraction = gapFrom / closing;

        if (fraction > 1f)
        {
            return new Rubikon.TraceResult(); // the sweep ends before reaching the plane
        }

        var hitPosition = Vector3.Lerp(from, to, fraction);
        return new Rubikon.TraceResult(true, hitPosition, normal, Vector3.Distance(from, hitPosition), -1);
    }
}

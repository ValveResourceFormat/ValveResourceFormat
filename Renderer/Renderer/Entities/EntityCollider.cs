using System.Linq;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A brush entity's collision shape: the <see cref="Rubikon"/> built from its own model, plus the
/// rigid transform placing it in the world. Sweeps and overlap tests are answered in the shape's
/// local space and mapped back out, so the shape follows the entity as it moves without rebuilding.
/// </summary>
/// <remarks>
/// The player hull stays axis-aligned in the shape's local frame rather than being re-fitted to a
/// world-axis-aligned box, matching how the engine traces against rotated brush models. Under
/// rotation this is the same approximation Source makes.
/// </remarks>
public sealed class EntityCollider
{
    private Matrix4x4 transform = Matrix4x4.Identity;
    private Matrix4x4 inverseTransform = Matrix4x4.Identity;

    /// <summary>Gets the collision shape, in the entity's local space.</summary>
    public Rubikon Shape { get; }

    /// <summary>Gets the shape's bounds in local space.</summary>
    public AABB LocalBounds { get; }

    /// <summary>Gets the shape's bounds in world space, refreshed whenever <see cref="Transform"/> changes.</summary>
    public AABB WorldBounds { get; private set; }

    /// <summary>Gets a value indicating whether the shape has any geometry to trace against.</summary>
    public bool IsEmpty { get; }

    /// <summary>Gets or sets the rigid transform from the shape's local space into the world.</summary>
    public Matrix4x4 Transform
    {
        get => transform;
        set
        {
            transform = value;

            if (!Matrix4x4.Invert(value, out inverseTransform))
            {
                inverseTransform = Matrix4x4.Identity;
            }

            WorldBounds = LocalBounds.Transform(value);
        }
    }

    /// <summary>
    /// Builds a collider from a brush entity's compiled physics.
    /// </summary>
    /// <param name="physics">The entity model's physics aggregate.</param>
    public EntityCollider(PhysAggregateData physics)
    {
        Shape = new Rubikon(physics);
        LocalBounds = ComputeBounds(Shape, out var isEmpty);
        IsEmpty = isEmpty;
        Transform = Matrix4x4.Identity;
    }

    private static AABB ComputeBounds(Rubikon shape, out bool isEmpty)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        isEmpty = true;

        foreach (var hull in shape.Hulls)
        {
            min = Vector3.Min(min, hull.Min);
            max = Vector3.Max(max, hull.Max);
            isEmpty = false;
        }

        foreach (var mesh in shape.Meshes)
        {
            foreach (var vertex in mesh.VertexPositions)
            {
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
                isEmpty = false;
            }
        }

        return isEmpty ? new AABB(Vector3.Zero, Vector3.Zero) : new AABB(min, max);
    }

    /// <summary>
    /// Sweeps an axis-aligned box through this shape.
    /// </summary>
    /// <param name="from">Sweep start, the box centre in world space.</param>
    /// <param name="to">Sweep end in world space.</param>
    /// <param name="halfExtents">Half-extents of the swept box.</param>
    /// <param name="detectStartSolid">Whether an overlap at <paramref name="from"/> reports as start-solid.</param>
    /// <returns>The hit, in world space.</returns>
    public Rubikon.TraceResult TraceAABB(Vector3 from, Vector3 to, Vector3 halfExtents, bool detectStartSolid = false)
    {
        if (IsEmpty)
        {
            return new Rubikon.TraceResult();
        }

        var localFrom = Vector3.Transform(from, inverseTransform);
        var localTo = Vector3.Transform(to, inverseTransform);

        var result = Shape.TraceAABB(localFrom, localTo, halfExtents, "player", detectStartSolid);

        if (!result.Hit)
        {
            return result;
        }

        result.HitPosition = Vector3.Transform(result.HitPosition, transform);
        result.ContactPoint = Vector3.Transform(result.ContactPoint, transform);
        result.HitNormal = Vector3.Normalize(Vector3.TransformNormal(result.HitNormal, transform));

        // Distance survives the round trip because the transform is rigid
        return result;
    }

    /// <summary>
    /// Traces a ray against this shape. Used by the player's reach test, which needs a point
    /// query rather than the swept hull movement uses.
    /// </summary>
    /// <param name="from">Ray start in world space.</param>
    /// <param name="to">Ray end in world space.</param>
    /// <returns>The hit, in world space.</returns>
    public Rubikon.TraceResult TraceRay(Vector3 from, Vector3 to)
    {
        if (IsEmpty || !MightHit(from, to, Vector3.Zero))
        {
            return new Rubikon.TraceResult();
        }

        var result = Shape.TraceRay(Vector3.Transform(from, inverseTransform), Vector3.Transform(to, inverseTransform));

        if (!result.Hit)
        {
            return result;
        }

        result.HitPosition = Vector3.Transform(result.HitPosition, transform);
        result.HitNormal = Vector3.Normalize(Vector3.TransformNormal(result.HitNormal, transform));

        return result;
    }

    /// <summary>
    /// Tests a resting axis-aligned box against this shape's solid volume, containment included.
    /// </summary>
    public bool OverlapsVolume(Vector3 center, Vector3 halfExtents)
    {
        if (IsEmpty || !WorldBounds.Intersects(new AABB(center - halfExtents, center + halfExtents)))
        {
            return false;
        }

        return Shape.IntersectsOrContainsAABB(Vector3.Transform(center, inverseTransform), halfExtents, "player");
    }

    /// <summary>
    /// Tests whether a world-space point is inside this shape's solid volume.
    /// </summary>
    public bool ContainsPoint(Vector3 point)
    {
        if (IsEmpty || !WorldBounds.Contains(point))
        {
            return false;
        }

        return Shape.ContainsPoint(Vector3.Transform(point, inverseTransform));
    }

    /// <summary>
    /// Tests whether a world-space swept box could possibly reach this collider, so callers can skip
    /// the transform and descent entirely.
    /// </summary>
    /// <param name="from">Sweep start.</param>
    /// <param name="to">Sweep end.</param>
    /// <param name="halfExtents">Half-extents of the swept box.</param>
    /// <returns><see langword="true"/> when the sweep's bounds overlap <see cref="WorldBounds"/>.</returns>
    public bool MightHit(Vector3 from, Vector3 to, Vector3 halfExtents)
    {
        if (IsEmpty)
        {
            return false;
        }

        var sweep = new AABB(Vector3.Min(from, to) - halfExtents, Vector3.Max(from, to) + halfExtents);
        return WorldBounds.Intersects(sweep);
    }

    /// <summary>
    /// Loads the physics of a brush entity model, following the model's referenced physics when it
    /// carries none inline.
    /// </summary>
    /// <param name="model">The entity's model, already loaded.</param>
    /// <param name="fileLoader">Loader used for the referenced physics file.</param>
    /// <returns>The physics aggregate, or <see langword="null"/> when the model has none.</returns>
    public static PhysAggregateData? LoadPhysics(Model? model, IO.IFileLoader fileLoader)
    {
        if (model == null)
        {
            return null;
        }

        var physics = model.GetEmbeddedPhys();

        if (physics != null)
        {
            return physics;
        }

        var referenced = model.GetReferencedPhysNames().FirstOrDefault();

        return referenced == null
            ? null
            : fileLoader?.LoadFileCompiled(referenced)?.DataBlock as PhysAggregateData;
    }
}

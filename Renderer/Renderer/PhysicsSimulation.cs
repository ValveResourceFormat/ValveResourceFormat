using Box3D;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// The scene's rigid body world, built on Box3D. The static world geometry goes in once as a compound
/// shape, physics props add themselves as dynamic bodies, and <see cref="EntitySystem"/> steps the
/// simulation on its fixed tick. <see cref="Rubikon"/> stays the tracer the player moves against;
/// this world is what makes props fall, tumble and get pushed around.
/// </summary>
public sealed class PhysicsSimulation : IDisposable
{
    /// <summary>Downward acceleration in units per second squared, matching the player's <c>sv_gravity</c>.</summary>
    public const float GravityValue = 800f;

    /// <summary>
    /// Source units per meter. The solver's tolerances (contact slop, sleep thresholds, speed caps) are
    /// authored in meters, so the library is told the scale once and everything else stays in map units.
    /// </summary>
    public const float UnitsPerMeter = 39.3701f;

    /// <summary>Collision category of the immovable world geometry.</summary>
    public const ulong StaticCategory = 1;

    /// <summary>Collision category of simulated props.</summary>
    public const ulong PropCategory = 2;

    /// <summary>Collision category of the player's pushing hull.</summary>
    public const ulong PlayerCategory = 4;

    /// <summary>Solver sub-steps per tick, Box3D's recommended default.</summary>
    private const int SubStepCount = 4;

    /// <summary>
    /// The impact speed the surface table's thresholds and the sound events' volume curves are
    /// normalized against: a hit at this speed is a full-strength impact.
    /// </summary>
    private const float ImpactSpeedReference = 500f;

    /// <summary>Gets the underlying Box3D world.</summary>
    public PhysicsWorld World { get; }

    // Hulls handed to the compound builder and meshes attached to bodies are borrowed by the native
    // side rather than copied, so they must stay alive until the world is destroyed
    private readonly List<IDisposable> borrowedGeometry = [];

    // Bodies carry only a ulong, so entity lookup for raycast hits goes through this table
    private readonly Dictionary<ulong, BaseEntity> bodyOwners = [];
    private ulong nextBodyId;

    // The game's surface table, or null when the game offers none; every shape then simulates and
    // sounds like the fallback surface
    private readonly SurfaceProperties? surfaces;

    // Sound events already queued for decoding, so a map of a hundred crates caches each once
    private readonly HashSet<string> cachedImpactSounds = [];

    static PhysicsSimulation()
    {
        // Must happen before anything else in the library: the native defaults are derived from the
        // length unit on first use, and a world created first would bake meter-scale tolerances
        Box3D.Native.B3.b3SetLengthUnitsPerMeter(UnitsPerMeter);
    }

    /// <summary>
    /// Creates an empty world with Source gravity. Geometry arrives via
    /// <see cref="AddStaticGeometry"/> and <see cref="CreatePropBody"/>.
    /// </summary>
    /// <param name="fileLoader">
    /// Loader for the game's surface property table, which is where friction, density and impact
    /// sounds come from. Without one, everything simulates as the default surface, silently.
    /// </param>
    public PhysicsSimulation(IO.IFileLoader? fileLoader = null)
    {
        World = new PhysicsWorld(WorldSettings.Default with
        {
            Gravity = new Vector3(0f, 0f, -GravityValue),
            MaximumLinearSpeed = 3500f, // sv_maxvelocity
        });

        if (fileLoader != null)
        {
            surfaces = SurfaceProperties.Load(fileLoader);
        }
    }

    /// <summary>
    /// Advances the simulation by one entity tick.
    /// </summary>
    public void Step(float tickInterval)
    {
        World.Step(tickInterval, SubStepCount);
        PlayImpactSounds();
    }

    /// <summary>
    /// Plays this step's impacts, the way the game keys them off the surface table: the moving
    /// shape's surface picks the event family (a crate sounds like a crate whatever it lands on),
    /// and the approach speed against the surface's own threshold picks soft or hard.
    /// </summary>
    private void PlayImpactSounds()
    {
        if (surfaces == null)
        {
            return;
        }

        foreach (var hit in World.Events.ContactHits)
        {
            var materialId = hit.ShapeA.IsValid && hit.ShapeA.Body.Type == BodyType.Dynamic
                ? hit.UserMaterialIdA
                : hit.UserMaterialIdB;

            var surface = surfaces.Find((uint)materialId);
            var strength = hit.ApproachSpeed / ImpactSpeedReference;

            var soundEvent = strength >= surface.ImpactHardThreshold
                ? surface.ImpactHard ?? surface.ImpactSoft
                : surface.ImpactSoft ?? surface.ImpactHard;

            if (soundEvent == null)
            {
                continue;
            }

            Sound.Play(soundEvent, hit.Point, volume: Math.Clamp(strength, 0.2f, 1f));
        }
    }

    /// <summary>
    /// The shape properties a surface hash dictates: the table's friction, elasticity and density,
    /// and the hash itself riding along as the material id so a contact can find the surface again.
    /// </summary>
    private ShapeDefinition MakeShapeDefinition(uint surfaceHash, ulong categories, bool hitEvents)
    {
        var surface = (surfaces?.Find(surfaceHash)) ?? SurfaceProperties.Fallback;

        if (hitEvents)
        {
            CacheImpactSounds(surface);
        }

        return ShapeDefinition.Default with
        {
            Density = surface.Density,
            Material = PhysicsMaterial.Default with
            {
                Friction = surface.Friction,
                Restitution = surface.Elasticity,
                UserMaterialId = surfaceHash,
            },
            Filter = new CollisionFilter(categories, ulong.MaxValue, 0),
            EnableHitEvents = hitEvents,
        };
    }

    /// <summary>
    /// The surface a shape descriptor names, as the hash the table is keyed by. Old assets carry
    /// no surface list, in which case everything is the fallback.
    /// </summary>
    private static uint GetSurfaceHash(PhysAggregateData phys, int surfacePropertyIndex)
    {
        var hashes = phys.SurfacePropertyHashes;

        return surfacePropertyIndex >= 0 && surfacePropertyIndex < hashes.Length
            ? hashes[surfacePropertyIndex]
            : 0;
    }

    private void CacheImpactSounds(SurfaceProperties.Surface surface)
    {
        if (surface.ImpactSoft != null && cachedImpactSounds.Add(surface.ImpactSoft))
        {
            Sound.Cache(surface.ImpactSoft);
        }

        if (surface.ImpactHard != null && cachedImpactSounds.Add(surface.ImpactHard))
        {
            Sound.Cache(surface.ImpactHard);
        }
    }

    /// <summary>
    /// Bakes a physics aggregate into one static compound body: every solid hull and mesh, in world
    /// space. Clip geometry is left out, because clips block players and NPCs rather than physics.
    /// </summary>
    /// <param name="phys">The world's compiled physics.</param>
    public void AddStaticGeometry(PhysAggregateData phys)
    {
        if (phys.Parts.Length == 0)
        {
            return;
        }

        var builder = new CompoundBuilder();
        var bindPose = phys.BindPose;

        for (var p = 0; p < phys.Parts.Length; p++)
        {
            var shape = phys.Parts[p].Shape;
            var pose = bindPose.Length > p ? bindPose[p] : Matrix4x4.Identity;

            foreach (var sphere in shape.Spheres)
            {
                if (SkipsPropCollision(phys, sphere.CollisionAttributeIndex))
                {
                    continue;
                }

                builder.AddSphere(new Sphere(Vector3.Transform(sphere.Shape.Center, pose), sphere.Shape.Radius),
                    MakeMaterial(GetSurfaceHash(phys, sphere.SurfacePropertyIndex)));
            }

            foreach (var capsule in shape.Capsules)
            {
                if (SkipsPropCollision(phys, capsule.CollisionAttributeIndex))
                {
                    continue;
                }

                var center = capsule.Shape.Center;
                builder.AddCapsule(new Capsule(
                    Vector3.Transform(center[0], pose),
                    Vector3.Transform(center[1], pose),
                    capsule.Shape.Radius),
                    MakeMaterial(GetSurfaceHash(phys, capsule.SurfacePropertyIndex)));
            }

            foreach (var hullDesc in shape.Hulls)
            {
                if (SkipsPropCollision(phys, hullDesc.CollisionAttributeIndex))
                {
                    continue;
                }

                if (BuildHull(hullDesc.Shape.GetVertexPositions(), pose) is { } hull)
                {
                    borrowedGeometry.Add(hull);
                    builder.AddHull(hull, Vector3.Zero, null,
                        MakeMaterial(GetSurfaceHash(phys, hullDesc.SurfacePropertyIndex)));
                }
            }

            foreach (var meshDesc in shape.Meshes)
            {
                if (SkipsPropCollision(phys, meshDesc.CollisionAttributeIndex))
                {
                    continue;
                }

                var mesh = BuildMesh(meshDesc.Shape.GetVertices(), meshDesc.Shape.GetTriangles(), pose);

                if (mesh != null)
                {
                    borrowedGeometry.Add(mesh);
                    builder.AddMesh(mesh, Vector3.Zero, null, null,
                        MakeMaterial(GetSurfaceHash(phys, meshDesc.SurfacePropertyIndex)));
                }
            }
        }

        if (builder.ChildCount == 0)
        {
            return;
        }

        var compound = builder.Build();
        borrowedGeometry.Add(compound);

        var body = World.CreateStaticBody(Vector3.Zero, null);
        body.AddCompound(compound, ShapeDefinition.Default with
        {
            Filter = new CollisionFilter(StaticCategory, ulong.MaxValue, 0),
        });
    }

    /// <summary>
    /// The contact material a surface hash dictates, for the compound's per-child materials.
    /// </summary>
    private PhysicsMaterial MakeMaterial(uint surfaceHash)
    {
        var surface = (surfaces?.Find(surfaceHash)) ?? SurfaceProperties.Fallback;

        return PhysicsMaterial.Default with
        {
            Friction = surface.Friction,
            Restitution = surface.Elasticity,
            UserMaterialId = surfaceHash,
        };
    }

    /// <summary>
    /// Creates the rigid body for a physics prop from its compiled physics: every sphere, capsule
    /// and convex hull, in the prop's local space. A soccer ball is a single sphere, a crate a
    /// single hull, and both must come through. Falls back to a box over the bounds when the
    /// aggregate offers nothing usable.
    /// </summary>
    /// <param name="phys">The prop model's physics aggregate.</param>
    /// <param name="localBounds">Bounds of its traced shape, for the fallback box.</param>
    /// <param name="origin">Spawn position in the world.</param>
    /// <param name="rotation">Spawn orientation.</param>
    /// <param name="motionEnabled">Whether the body simulates; a motion-disabled prop stands as a static obstacle.</param>
    /// <param name="startAsleep">Whether the body waits for a touch before simulating.</param>
    /// <param name="owner">The entity the body reports as, from <see cref="GetOwner"/>.</param>
    /// <returns>The created body, or <see langword="null"/> when there was nothing to build one from.</returns>
    public Body? CreatePropBody(PhysAggregateData phys, AABB localBounds, Vector3 origin, Quaternion rotation,
        bool motionEnabled, bool startAsleep, BaseEntity owner)
    {
        var definition = motionEnabled
            ? BodyDefinition.Dynamic(origin, rotation) with { StartAwake = !startAsleep }
            : BodyDefinition.Static(origin, rotation);

        var body = World.CreateBody(definition);
        var bindPose = phys.BindPose;

        for (var p = 0; p < phys.Parts.Length; p++)
        {
            var shape = phys.Parts[p].Shape;
            var pose = bindPose.Length > p ? bindPose[p] : Matrix4x4.Identity;

            foreach (var sphere in shape.Spheres)
            {
                body.AddSphere(new Sphere(Vector3.Transform(sphere.Shape.Center, pose), sphere.Shape.Radius),
                    MakeShapeDefinition(GetSurfaceHash(phys, sphere.SurfacePropertyIndex), PropCategory, hitEvents: true));
            }

            foreach (var capsule in shape.Capsules)
            {
                var center = capsule.Shape.Center;
                body.AddCapsule(new Capsule(
                    Vector3.Transform(center[0], pose),
                    Vector3.Transform(center[1], pose),
                    capsule.Shape.Radius),
                    MakeShapeDefinition(GetSurfaceHash(phys, capsule.SurfacePropertyIndex), PropCategory, hitEvents: true));
            }

            foreach (var hullDesc in shape.Hulls)
            {
                if (BuildHull(hullDesc.Shape.GetVertexPositions(), pose) is { } hull)
                {
                    // Copied on attach, unlike the compound path, so nothing to keep alive
                    using (hull)
                    {
                        body.AddHull(hull,
                            MakeShapeDefinition(GetSurfaceHash(phys, hullDesc.SurfacePropertyIndex), PropCategory, hitEvents: true));
                    }
                }
            }

            // Meshes are left out on purpose: dynamic bodies cannot carry triangle meshes, and a
            // mesh-collision prop is covered by the fallback box below
        }

        if (body.ShapeCount == 0)
        {
            if (localBounds.Size == Vector3.Zero)
            {
                body.Destroy();
                return null;
            }

            body.AddBox(new Box3D.Box(localBounds.Size * 0.5f, localBounds.Center),
                MakeShapeDefinition(0, PropCategory, hitEvents: true));
        }

        Register(body, owner);
        return body;
    }

    /// <summary>
    /// Creates the kinematic body that stands where the player stands, so walking into a prop shoves
    /// it aside. It collides with props alone: the world cannot move it, and the player's own movement
    /// already handles walls.
    /// </summary>
    /// <param name="feetPosition">The player's feet, which is the body origin.</param>
    /// <param name="hullHalfExtents">Half-extents of the player's collision hull.</param>
    /// <returns>The created body.</returns>
    public Body CreatePlayerBody(Vector3 feetPosition, Vector3 hullHalfExtents)
    {
        var body = World.CreateKinematicBody(feetPosition, null);

        body.AddBox(new Box3D.Box(hullHalfExtents, new Vector3(0f, 0f, hullHalfExtents.Z)), ShapeDefinition.Default with
        {
            Filter = new CollisionFilter(PlayerCategory, PropCategory, 0),
        });

        return body;
    }

    /// <summary>
    /// Links a body to the entity it simulates, for <see cref="GetOwner"/> to find again.
    /// </summary>
    public void Register(Body body, BaseEntity owner)
    {
        body.UserData = ++nextBodyId;
        bodyOwners[body.UserData] = owner;
    }

    /// <summary>
    /// Unlinks a body from its entity. Call before destroying a registered body.
    /// </summary>
    public void Forget(Body body)
    {
        bodyOwners.Remove(body.UserData);
    }

    /// <summary>
    /// Finds the entity a body simulates, or <see langword="null"/> for one that stands for no
    /// entity, such as the static world.
    /// </summary>
    public BaseEntity? GetOwner(Body body)
        => bodyOwners.TryGetValue(body.UserData, out var owner) ? owner : null;

    private static ConvexHull? BuildHull(ReadOnlySpan<Vector3> points, Matrix4x4 pose)
    {
        if (points.Length < 4)
        {
            return null;
        }

        var transformed = points;

        if (!pose.IsIdentity)
        {
            var buffer = new Vector3[points.Length];

            for (var i = 0; i < points.Length; i++)
            {
                buffer[i] = Vector3.Transform(points[i], pose);
            }

            transformed = buffer;
        }

        try
        {
            // Zero keeps the engine's own vertex budget; oversized hulls are merged down
            return ConvexHull.FromPoints(transformed, 0);
        }
        catch (ArgumentException)
        {
            // Degenerate (flat or tiny) input the quickhull cannot enclose; skip the hull rather
            // than lose the whole aggregate
            return null;
        }
    }

    private static CollisionMesh? BuildMesh(ReadOnlySpan<Vector3> vertices,
        ReadOnlySpan<ResourceTypes.RubikonPhysics.Shapes.Mesh.Triangle> triangles, Matrix4x4 pose)
    {
        if (triangles.Length == 0)
        {
            return null;
        }

        var positions = new Vector3[vertices.Length];

        for (var i = 0; i < vertices.Length; i++)
        {
            positions[i] = pose.IsIdentity ? vertices[i] : Vector3.Transform(vertices[i], pose);
        }

        var indices = new int[triangles.Length * 3];

        for (var i = 0; i < triangles.Length; i++)
        {
            // Source triangles wind counter-clockwise seen from the solid side, which is the
            // winding the mesh builder expects
            indices[i * 3 + 0] = triangles[i].X;
            indices[i * 3 + 1] = triangles[i].Y;
            indices[i * 3 + 2] = triangles[i].Z;
        }

        return CollisionMesh.FromTriangles(positions, indices, null, default);
    }

    /// <summary>
    /// Whether props pass through geometry with these collision attributes. Clips exist to steer
    /// players, NPCs and grenades; a ladder is a climbing volume. None of them stop a thrown crate.
    /// </summary>
    private static bool SkipsPropCollision(PhysAggregateData phys, int collisionAttributeIndex)
    {
        var attributes = phys.CollisionAttributes[collisionAttributeIndex];

        var interactAs = attributes.GetArray<string>("m_InteractAsStrings")
            ?? attributes.GetArray<string>("m_PhysicsTagStrings")
            ?? [];

        foreach (var tag in interactAs)
        {
            if (tag is "playerclip" or "npcclip" or "csgo_grenadeclip" or "ladder")
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The world goes first: the geometry below is borrowed by shapes inside it, and must
        // outlive them
        World.Dispose();

        foreach (var geometry in borrowedGeometry)
        {
            geometry.Dispose();
        }

        borrowedGeometry.Clear();
        bodyOwners.Clear();
    }
}

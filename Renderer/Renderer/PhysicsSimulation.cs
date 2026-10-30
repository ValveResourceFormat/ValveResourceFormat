using System.Linq;
using System.Threading.Tasks;
using Box3D;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ConvexHull = Box3D.ConvexHull;
using RnShape = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shape;

namespace ValveResourceFormat.Renderer;

/// <summary>Box3D rigid body world for props, ragdolls and movers.</summary>
public sealed class PhysicsSimulation : IDisposable
{
    /// <summary>Downward acceleration in units per second squared, matching <c>sv_gravity</c>.</summary>
    public const float GravityValue = 800f;

    /// <summary>Source units per meter, the length scale of solver tolerances.</summary>
    public const float UnitsPerMeter = 39.3701f;

    /// <summary>Collision category of the immovable world geometry.</summary>
    public const ulong StaticCategory = 1;

    /// <summary>Collision category of simulated props.</summary>
    public const ulong PropCategory = 2;

    /// <summary>Collision category of the player pushing hull.</summary>
    public const ulong PlayerCategory = 4;

    /// <summary>Collision category of kinematic doors and func movers.</summary>
    public const ulong MoverCategory = 8;

    /// <summary>Collision category of debris, which collides with neither the player nor other debris.</summary>
    public const ulong DebrisCategory = 16;

    /// <summary>
    /// Collision category of non-debris ragdoll parts. They skip the player hull, whose overlap
    /// resolves out the bottom face and pushes a lying part through the one-sided world mesh.
    /// </summary>
    public const ulong RagdollCategory = 32;

    /// <summary>What a shape of the given collision category collides with.</summary>
    public static ulong CollidesWith(ulong category)
        => category == DebrisCategory ? ~(PlayerCategory | DebrisCategory) : ulong.MaxValue;

    /// <summary>Sets whether all shapes of a body collide with the given categories.</summary>
    public static void SetCollidesWith(Body body, ulong categories, bool collide)
    {
        Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
        var count = body.GetShapes(shapes);

        for (var i = 0; i < count; i++)
        {
            var filter = shapes[i].Filter;
            var collidesWith = collide ? filter.CollidesWith | categories : filter.CollidesWith & ~categories;
            shapes[i].SetFilter(new CollisionFilter(filter.Categories, collidesWith, filter.Group), recomputeContacts: true);
        }
    }

    // About 240 solver slices per second; coarser slices leave ragdoll heaps jittering instead of sleeping
    private static int SubStepsFor(float timeStep)
        => Math.Clamp((int)MathF.Ceiling(timeStep * 240f), 2, 8);

    /// <summary>Gets the underlying Box3D world.</summary>
    public Box3D.PhysicsWorld World { get; }

    /// <summary>Gets a shapeless static body for joints attached to the world.</summary>
    public Body WorldAnchor
    {
        get
        {
            if (!hasWorldAnchor)
            {
                worldAnchor = World.CreateBody(BodyDefinition.Static(Vector3.Zero, Quaternion.Identity));
                hasWorldAnchor = true;
            }

            return worldAnchor;
        }
    }

    private Body worldAnchor;
    private bool hasWorldAnchor;

    // Borrowed rather than copied by the native side, must outlive the world
    private readonly List<IDisposable> borrowedGeometry = [];

    private readonly List<Task<StaticGeometry?>> pendingStaticGeometry = [];

    private sealed record StaticGeometry(CompoundGeometry Compound, List<IDisposable> Borrowed, Matrix4x4 Placement);

    private readonly Dictionary<ulong, BaseEntity> bodyOwners = [];
    private ulong nextBodyId;

    // Solver bounds include a broadphase margin that changes as the body moves
    private readonly Dictionary<ulong, BodyGeometry> bodyGeometry = [];

    private sealed class BodyGeometry
    {
        public BoundingBox? Bounds { get; private set; }
        public List<Vector3> HullPoints { get; } = [];
        public List<Capsule> RoundShapes { get; } = [];

        public void Grow(BoundingBox other)
            => Bounds = Bounds is { } current
                ? new BoundingBox(Vector3.Min(current.Min, other.Min), Vector3.Max(current.Max, other.Max))
                : other;
    }

    private readonly SurfaceProperties? surfaces;
    private readonly PhysicsImpactSounds? impactSounds;

    static PhysicsSimulation()
    {
        // Native defaults are derived from the length unit on first use
        Box3D.Native.B3.b3SetLengthUnitsPerMeter(UnitsPerMeter);
    }

    /// <summary>Creates an empty world with Source gravity.</summary>
    /// <param name="fileLoader">Loader for the surface property table; without one all shapes use the fallback surface and make no sound.</param>
    public PhysicsSimulation(IO.IFileLoader? fileLoader = null)
    {
        World = new Box3D.PhysicsWorld(WorldSettings.Default with
        {
            Gravity = new Vector3(0f, 0f, -GravityValue),
            MaximumLinearSpeed = 3500f, // sv_maxvelocity
        });

        if (fileLoader != null)
        {
            surfaces = SurfaceProperties.Load(fileLoader);
        }

        if (surfaces != null)
        {
            impactSounds = new PhysicsImpactSounds(surfaces);
        }
    }

    /// <summary>Advances the simulation by one step and plays impact sounds.</summary>
    public void Step(float tickInterval)
    {
        if (!AttachStaticGeometry())
        {
            return;
        }

        World.Step(tickInterval, SubStepsFor(tickInterval));
        impactSounds?.Play(World.Events.ContactHits, tickInterval);
    }

    /// <summary>Applies an impulse at the first hit along a ray and returns the entity struck, if any.</summary>
    /// <param name="from">Ray start.</param>
    /// <param name="direction">Normalized ray direction.</param>
    /// <param name="distance">Ray length.</param>
    /// <param name="impulse">Impulse along the ray, applied at the hit point.</param>
    public BaseEntity? ApplyImpactImpulse(Vector3 from, Vector3 direction, float distance, float impulse)
    {
        // Not as the player, or debris would be skipped
        var hit = World.RaycastClosest(from, direction * distance,
            new QueryFilter(~PlayerCategory, StaticCategory | PropCategory | DebrisCategory | RagdollCategory | MoverCategory));

        if (!hit.Hit || !hit.Shape.IsValid)
        {
            return null;
        }

        var body = hit.Shape.Body;

        if (body.Type != BodyType.Dynamic)
        {
            return GetOwner(body);
        }

        body.ApplyImpulse(direction * impulse, hit.Point, wake: true);
        return GetOwner(body);
    }

    /// <summary>Shortens a hold distance so the thinnest side of a held body stays out of the world and movers.</summary>
    public float ClearReach(Vector3 from, Vector3 direction, float reach, Body body)
    {
        if (!bodyGeometry.TryGetValue(body.UserData, out var geometry) || geometry.Bounds is not { } bounds)
        {
            return reach;
        }

        var extents = bounds.Extents;
        return ShortenReach(from, direction, reach, MathF.Min(extents.X, MathF.Min(extents.Y, extents.Z)));
    }

    /// <summary>Shortens a hold distance by sweeping the shapes of a held body from the eyes until they touch the world or movers.</summary>
    /// <param name="from">Ray start.</param>
    /// <param name="direction">Normalized ray direction.</param>
    /// <param name="reach">Desired hold distance.</param>
    /// <param name="body">Held body.</param>
    /// <param name="normal">Normal of the surface the ray meets, or zero.</param>
    public float TouchReach(Vector3 from, Vector3 direction, float reach, Body body, out Vector3 normal)
    {
        normal = Vector3.Zero;

        if (reach <= MinClearReach || !bodyGeometry.TryGetValue(body.UserData, out var geometry) || geometry.Bounds is not { } bounds)
        {
            return reach;
        }

        var filter = new QueryFilter(PlayerCategory, StaticCategory | MoverCategory);
        var rotation = body.Rotation;
        var origin = from - Vector3.Transform(body.LocalCenterOfMass, rotation);
        var travel = direction * reach;
        var fraction = 1f;

        foreach (var point in geometry.HullPoints)
        {
            var hit = World.RaycastClosest(origin + Vector3.Transform(point, rotation), travel, filter);

            if (hit.Hit)
            {
                fraction = MathF.Min(fraction, hit.Fraction);
            }
        }

        foreach (var round in geometry.RoundShapes)
        {
            var turned = new Capsule(Vector3.Transform(round.Start, rotation), Vector3.Transform(round.End, rotation), round.Radius);
            fraction = MathF.Min(fraction, World.CastCapsule(turned, origin, travel, filter));
        }

        var surface = World.RaycastClosest(from, direction * (reach + bounds.Extents.Length()), filter);

        if (surface.Hit)
        {
            normal = surface.Normal;
        }

        if (fraction >= 1f)
        {
            return reach;
        }

        return MathF.Max((fraction * reach) - TouchMargin, MinClearReach);
    }

    // Keeps a held body out of constant contact
    private const float TouchMargin = 0.5f;

    private float ShortenReach(Vector3 from, Vector3 direction, float reach, float extent)
    {
        var hit = World.RaycastClosest(from, direction * (reach + extent),
            new QueryFilter(PlayerCategory, StaticCategory | MoverCategory));

        if (!hit.Hit)
        {
            return reach;
        }

        return MathF.Min(reach, MathF.Max(Vector3.Distance(from, hit.Point) - extent, MinClearReach));
    }

    // Never hold a body inside the face of the carrier
    private const float MinClearReach = 16f;

    // Table densities are kg/m^3, shape volumes are in cubic units
    private const float DensityScale = 1f / (UnitsPerMeter * UnitsPerMeter * UnitsPerMeter);

    // Table elasticities near 1 are not restitution coefficients, and the solver takes the larger
    // side of a contact; e/(1+e) keeps the order but tames them, the cap is for metal_bouncy (1000)
    private static float ToRestitution(float elasticity)
        => MathF.Min(elasticity / (1f + elasticity), 0.9f);

    private ShapeDefinition MakeShapeDefinition(uint surfaceHash, ulong categories)
    {
        var surface = (surfaces?.Find(surfaceHash)) ?? SurfaceProperties.Fallback;
        var simulated = (categories & (PropCategory | DebrisCategory | RagdollCategory)) != 0;

        if (simulated)
        {
            impactSounds?.Prepare(surfaceHash);
        }

        return ShapeDefinition.Default with
        {
            Density = surface.Density * DensityScale,
            Material = PhysicsMaterial.Default with
            {
                Friction = surface.Friction,
                Restitution = ToRestitution(surface.Elasticity),
                UserMaterialId = surfaceHash,
            },
            Filter = new CollisionFilter(categories, CollidesWith(categories), 0),
            EnableHitEvents = simulated,
        };
    }

    /// <summary>Finds the break sound of the first surface in the collision that has one.</summary>
    public string? FindBreakSound(PhysAggregateData phys)
    {
        if (surfaces == null)
        {
            return null;
        }

        foreach (var part in phys.Parts)
        {
            var shape = part.Shape;
            var indices = shape.GetAllHulls().Select(h => h.SurfacePropertyIndex)
                .Concat(shape.GetAllSpheres().Select(s => s.SurfacePropertyIndex))
                .Concat(shape.GetAllCapsules().Select(c => c.SurfacePropertyIndex))
                .Concat(shape.GetAllMeshes().Select(m => m.SurfacePropertyIndex));

            foreach (var index in indices)
            {
                if (surfaces.Find(GetSurfaceHash(phys, index)).BreakSound is { } sound)
                {
                    return sound;
                }
            }
        }

        return null;
    }

    // Old assets have no surface list
    private static uint GetSurfaceHash(PhysAggregateData phys, int surfacePropertyIndex)
    {
        var hashes = phys.SurfacePropertyHashes;

        return surfacePropertyIndex >= 0 && surfacePropertyIndex < hashes.Length
            ? hashes[surfacePropertyIndex]
            : 0;
    }

    /// <summary>Builds a physics aggregate into a static compound body in the background, leaving out clips.</summary>
    /// <param name="phys">Compiled world physics.</param>
    /// <param name="placement">Rigid transform of the spawn group.</param>
    public void AddStaticGeometry(PhysAggregateData phys, Matrix4x4 placement)
    {
        if (phys.Parts.Length == 0)
        {
            return;
        }

        pendingStaticGeometry.Add(Task.Run(() => BuildStaticGeometry(phys, placement)));
    }

    // Runs off the stepping thread, must not touch the world
    private StaticGeometry? BuildStaticGeometry(PhysAggregateData phys, Matrix4x4 placement)
    {
        var builder = new CompoundBuilder();
        var borrowed = new List<IDisposable>();
        var bindPose = phys.BindPose;

        for (var p = 0; p < phys.Parts.Length; p++)
        {
            var shape = phys.Parts[p].Shape;
            var pose = bindPose.Length > p ? bindPose[p] : Matrix4x4.Identity;

            foreach (var sphere in shape.GetAllSpheres())
            {
                if (SkipsPropCollision(phys, sphere.CollisionAttributeIndex))
                {
                    continue;
                }

                builder.AddSphere(new Sphere(Vector3.Transform(sphere.Shape.Center, pose), sphere.Shape.Radius),
                    MakeMaterial(GetSurfaceHash(phys, sphere.SurfacePropertyIndex)));
            }

            foreach (var capsule in shape.GetAllCapsules())
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

            foreach (var hullDesc in shape.GetAllHulls())
            {
                if (SkipsPropCollision(phys, hullDesc.CollisionAttributeIndex))
                {
                    continue;
                }

                if (BuildHull(hullDesc.Shape.GetVertexPositions(), pose) is { } hull)
                {
                    borrowed.Add(hull);
                    builder.AddHull(hull, Vector3.Zero, null,
                        MakeMaterial(GetSurfaceHash(phys, hullDesc.SurfacePropertyIndex)));
                }
            }

            foreach (var meshDesc in shape.GetAllMeshes())
            {
                if (SkipsPropCollision(phys, meshDesc.CollisionAttributeIndex))
                {
                    continue;
                }

                var mesh = BuildMesh(meshDesc.Shape.GetVertices(), meshDesc.Shape.GetTriangles(), pose);

                if (mesh != null)
                {
                    borrowed.Add(mesh);
                    builder.AddMesh(mesh, Vector3.Zero, null, null,
                        MakeMaterial(GetSurfaceHash(phys, meshDesc.SurfacePropertyIndex)));
                }
            }
        }

        if (builder.ChildCount == 0)
        {
            foreach (var geometry in borrowed)
            {
                geometry.Dispose();
            }

            return null;
        }

        return new StaticGeometry(builder.Build(), borrowed, placement);
    }

    /// <summary>Waits for pending static geometry and attaches it.</summary>
    public void FinishStaticGeometry()
    {
        Task.WaitAll([.. pendingStaticGeometry]);
        AttachStaticGeometry();
    }

    // The world does not step until this returns true, or props would fall through missing floors
    private bool AttachStaticGeometry()
    {
        for (var i = 0; i < pendingStaticGeometry.Count; i++)
        {
            var pending = pendingStaticGeometry[i];

            if (!pending.IsCompleted)
            {
                continue;
            }

            pendingStaticGeometry.RemoveAt(i--);

            if (pending.Result is not { } geometry)
            {
                continue;
            }

            borrowedGeometry.AddRange(geometry.Borrowed);
            borrowedGeometry.Add(geometry.Compound);

            var body = World.CreateStaticBody(geometry.Placement.Translation, Quaternion.CreateFromRotationMatrix(geometry.Placement));
            body.AddCompound(geometry.Compound, ShapeDefinition.Default with
            {
                Filter = new CollisionFilter(StaticCategory, ulong.MaxValue, 0),
            });
        }

        return pendingStaticGeometry.Count == 0;
    }

    private PhysicsMaterial MakeMaterial(uint surfaceHash)
    {
        var surface = (surfaces?.Find(surfaceHash)) ?? SurfaceProperties.Fallback;

        return PhysicsMaterial.Default with
        {
            Friction = surface.Friction,
            Restitution = ToRestitution(surface.Elasticity),
            UserMaterialId = surfaceHash,
        };
    }

    /// <summary>Creates a prop body from its spheres, capsules and hulls, or a box over the bounds when there are none.</summary>
    /// <param name="phys">Physics aggregate of the model.</param>
    /// <param name="localBounds">Bounds for the fallback box.</param>
    /// <param name="origin">Spawn position.</param>
    /// <param name="rotation">Spawn orientation.</param>
    /// <param name="motionEnabled">False makes the body static.</param>
    /// <param name="startAsleep">Whether the body starts asleep.</param>
    /// <param name="owner">Entity returned by <see cref="GetOwner"/>.</param>
    public Body? CreatePropBody(PhysAggregateData phys, AABB localBounds, Vector3 origin, Quaternion rotation,
        bool motionEnabled, bool startAsleep, BaseEntity owner)
    {
        var definition = motionEnabled
            ? BodyDefinition.Dynamic(origin, rotation) with { StartAwake = !startAsleep }
            : BodyDefinition.Static(origin, rotation);

        // There is no air drag, so undamped props would swing on hinges forever
        if (motionEnabled)
        {
            var linearDamping = phys.Parts.Length > 0 ? phys.Parts[0].LinearDamping : 0f;
            var angularDamping = phys.Parts.Length > 0 ? phys.Parts[0].AngularDamping : 0f;

            definition = definition with
            {
                LinearDamping = linearDamping > 0f ? linearDamping : FallbackPropLinearDamping,
                AngularDamping = angularDamping > 0f ? angularDamping : FallbackPropAngularDamping,
            };
        }

        var body = World.CreateBody(definition);
        var geometry = AddAggregateShapes(body, phys, PropCategory);

        if (body.ShapeCount == 0)
        {
            if (localBounds.Size == Vector3.Zero)
            {
                body.Destroy();
                return null;
            }

            body.AddBox(new Box3D.Box(localBounds.Size * 0.5f, localBounds.Center),
                MakeShapeDefinition(0, PropCategory));
            geometry.Grow(new BoundingBox(localBounds.Min, localBounds.Max));

            for (var corner = 0; corner < 8; corner++)
            {
                geometry.HullPoints.Add(new Vector3(
                    (corner & 1) != 0 ? localBounds.Max.X : localBounds.Min.X,
                    (corner & 2) != 0 ? localBounds.Max.Y : localBounds.Min.Y,
                    (corner & 4) != 0 ? localBounds.Max.Z : localBounds.Min.Z));
            }
        }

        Register(body, owner);
        bodyGeometry[body.UserData] = geometry;
        return body;
    }

    /// <summary>Creates the body of one ragdoll part with its authored mass.</summary>
    /// <param name="phys">Physics aggregate of the ragdoll model.</param>
    /// <param name="partIndex">Part to build.</param>
    /// <param name="position">World position of the part.</param>
    /// <param name="rotation">World orientation of the part.</param>
    /// <param name="isDebris">Whether to use <see cref="DebrisCategory"/>.</param>
    /// <param name="owner">Entity returned by <see cref="GetOwner"/>.</param>
    public Body? CreateRagdollBody(PhysAggregateData phys, int partIndex, Vector3 position, Quaternion rotation, bool isDebris, BaseEntity owner)
    {
        var part = phys.Parts[partIndex];

        // Joint friction does not resist rigid whole-body rotation, a held ragdoll needs damping to stop spinning
        var angularDamping = part.AngularDamping > 0f ? part.AngularDamping : FallbackAngularDamping;

        var body = World.CreateBody(BodyDefinition.Dynamic(position, rotation) with
        {
            LinearDamping = part.LinearDamping,
            AngularDamping = angularDamping,
        });

        var geometry = new BodyGeometry();
        AddPartShapes(body, phys, part.Shape, Matrix4x4.Identity, isDebris ? DebrisCategory : RagdollCategory, geometry);

        if (body.ShapeCount == 0)
        {
            body.Destroy();
            return null;
        }

        var authoredMass = phys.Parts[partIndex].Mass;

        if (authoredMass > 0f && body.Mass > 0f)
        {
            var massScale = authoredMass / body.Mass;
            Span<Shape> shapes = stackalloc Shape[body.ShapeCount];
            var count = body.GetShapes(shapes);

            for (var i = 0; i < count; i++)
            {
                shapes[i].SetDensity(shapes[i].Density * massScale, updateBodyMass: true);
            }
        }

        Register(body, owner);
        bodyGeometry[body.UserData] = geometry;
        return body;
    }

    // Linear damping too, since a hinged body swings its mass center more than it spins
    private const float FallbackPropLinearDamping = 0.1f;
    private const float FallbackPropAngularDamping = 0.5f;

    // Typical authored limb values, for ragdolls authored without any
    private const float FallbackAngularDamping = 2f;
    private const float FallbackJointFriction = 0.3f;

    private const float TunnelRescueMargin = 0.25f;

    /// <summary>Moves a body back in front of a static surface its center crossed since <paramref name="previousPosition"/>.</summary>
    public void CatchTunneledBody(Body body, Vector3 previousPosition)
    {
        var delta = body.Position - previousPosition;

        if (delta.LengthSquared() < 0.01f)
        {
            return;
        }

        // Contact push-out can eject squeezed parts through the one-sided world mesh. Backfaces
        // are not hit, letting bodies climb back out of geometry
        var hit = World.RaycastClosest(previousPosition, delta,
            new QueryFilter(RagdollCategory, StaticCategory));

        if (!hit.Hit || Vector3.Dot(hit.Normal, delta) >= 0f)
        {
            return;
        }

        body.SetTransform(hit.Point + hit.Normal * TunnelRescueMargin, null);

        var velocity = body.LinearVelocity;
        var into = Vector3.Dot(velocity, hit.Normal);

        if (into < 0f)
        {
            body.LinearVelocity = velocity - hit.Normal * into;
        }
    }

    /// <summary>Creates a ragdoll joint, or <see langword="null"/> for an unsupported type.</summary>
    public Joint? CreateRagdollJoint(in ResourceTypes.RubikonPhysics.Joint joint, Body body1, Body body2)
    {
        // Default constraint stiffness, softer lets limbs stretch under load
        var baseDefinition = JointDefinition.Connect(body1, body2,
            new JointFrame(joint.Frame1.Position, joint.Frame1.Rotation),
            new JointFrame(joint.Frame2.Position, joint.Frame2.Rotation))
            with
        { CollideConnected = joint.EnableCollision };

        var frictionAmount = joint.HasFriction && joint.Friction > 0f ? joint.Friction : FallbackJointFriction;

        switch (joint.Type)
        {
            case ResourceTypes.RubikonPhysics.JointType.Revolute:
            {
                var definition = RevoluteJointDefinition.Default with
                {
                    Base = baseDefinition,
                    LimitsEnabled = joint.EnableTwistLimit,
                    LowerAngle = joint.TwistLimit.Min,
                    UpperAngle = joint.TwistLimit.Max,
                    MotorEnabled = true,
                    MotorSpeed = 0f,
                    MaxMotorTorque = frictionAmount * RagdollFrictionTorque,
                };

                return World.CreateRevoluteJoint(definition).AsJoint;
            }

            case ResourceTypes.RubikonPhysics.JointType.Spherical:
            case ResourceTypes.RubikonPhysics.JointType.Conical:
            {
                var definition = SphericalJointDefinition.Default with
                {
                    Base = baseDefinition,
                    ConeLimitEnabled = joint.EnableSwingLimit,
                    ConeAngle = joint.SwingLimit.Max,
                    TwistLimitEnabled = joint.EnableTwistLimit,
                    LowerTwistAngle = joint.TwistLimit.Min,
                    UpperTwistAngle = joint.TwistLimit.Max,
                    MotorEnabled = true,
                    MotorVelocity = Vector3.Zero,
                    MaxMotorTorque = frictionAmount * RagdollFrictionTorque,
                };

                return World.CreateSphericalJoint(definition).AsJoint;
            }

            case ResourceTypes.RubikonPhysics.JointType.Weld:
                return World.CreateWeldJoint(WeldJointDefinition.Default with { Base = baseDefinition }).AsJoint;

            default:
                return null;
        }
    }

    // Authored joint friction (0.15 to 0.7) to motor torque against kilogram limb masses
    private const float RagdollFrictionTorque = 100f;

    /// <summary>Creates a kinematic body for a door or func mover that pushes props.</summary>
    public Body? CreateMoverBody(PhysAggregateData phys, Vector3 origin, Quaternion rotation)
    {
        var body = World.CreateKinematicBody(origin, rotation);

        AddAggregateShapes(body, phys, MoverCategory);

        if (body.ShapeCount == 0)
        {
            body.Destroy();
            return null;
        }

        return body;
    }

    /// <summary>Creates a kinematic body from one part of a model, in its own space, that pushes props as the part moves.</summary>
    public Body? CreateMoverPartBody(PhysAggregateData phys, int partIndex, Vector3 origin, Quaternion rotation)
    {
        var body = World.CreateKinematicBody(origin, rotation);

        AddPartShapes(body, phys, phys.Parts[partIndex].Shape, Matrix4x4.Identity, MoverCategory, new BodyGeometry());

        if (body.ShapeCount == 0)
        {
            body.Destroy();
            return null;
        }

        return body;
    }

    // Meshes are skipped, dynamic bodies cannot carry them and movers are hull-compiled
    private BodyGeometry AddAggregateShapes(Body body, PhysAggregateData phys, ulong categories)
    {
        var bindPose = phys.BindPose;
        var geometry = new BodyGeometry();

        for (var p = 0; p < phys.Parts.Length; p++)
        {
            var pose = bindPose.Length > p ? bindPose[p] : Matrix4x4.Identity;
            AddPartShapes(body, phys, phys.Parts[p].Shape, pose, categories, geometry);
        }

        return geometry;
    }

    private void AddPartShapes(Body body, PhysAggregateData phys, RnShape shape, Matrix4x4 pose, ulong categories, BodyGeometry geometry)
    {
        foreach (var sphere in shape.GetAllSpheres())
        {
            var center = Vector3.Transform(sphere.Shape.Center, pose);
            var radius = new Vector3(sphere.Shape.Radius);

            body.AddSphere(new Sphere(center, sphere.Shape.Radius),
                MakeShapeDefinition(GetSurfaceHash(phys, sphere.SurfacePropertyIndex), categories));
            geometry.Grow(new BoundingBox(center - radius, center + radius));
            geometry.RoundShapes.Add(new Capsule(center, center, sphere.Shape.Radius));
        }

        foreach (var capsule in shape.GetAllCapsules())
        {
            var center0 = Vector3.Transform(capsule.Shape.Center[0], pose);
            var center1 = Vector3.Transform(capsule.Shape.Center[1], pose);
            var radius = new Vector3(capsule.Shape.Radius);

            body.AddCapsule(new Capsule(center0, center1, capsule.Shape.Radius),
                MakeShapeDefinition(GetSurfaceHash(phys, capsule.SurfacePropertyIndex), categories));
            geometry.Grow(new BoundingBox(Vector3.Min(center0, center1) - radius, Vector3.Max(center0, center1) + radius));
            geometry.RoundShapes.Add(new Capsule(center0, center1, capsule.Shape.Radius));
        }

        foreach (var hullDesc in shape.GetAllHulls())
        {
            var points = hullDesc.Shape.GetVertexPositions();

            if (BuildHull(points, pose) is { } hull)
            {
                // Copied on attach, unlike compound hulls
                using (hull)
                {
                    body.AddHull(hull,
                        MakeShapeDefinition(GetSurfaceHash(phys, hullDesc.SurfacePropertyIndex), categories));
                    geometry.Grow(hull.Bounds);
                }

                foreach (var point in points)
                {
                    geometry.HullPoints.Add(Vector3.Transform(point, pose));
                }
            }
        }
    }

    /// <summary>Creates a kinematic player hull body that collides only with props.</summary>
    /// <param name="feetPosition">Body origin at the feet.</param>
    /// <param name="hullHalfExtents">Half-extents of the player hull.</param>
    public Body CreatePlayerBody(Vector3 feetPosition, Vector3 hullHalfExtents)
    {
        var body = World.CreateKinematicBody(feetPosition, null);

        body.AddBox(new Box3D.Box(hullHalfExtents, new Vector3(0f, 0f, hullHalfExtents.Z)), ShapeDefinition.Default with
        {
            Filter = new CollisionFilter(PlayerCategory, PropCategory, 0),
        });

        return body;
    }

    /// <summary>Links a body to an entity for <see cref="GetOwner"/>.</summary>
    public void Register(Body body, BaseEntity owner)
    {
        body.UserData = ++nextBodyId;
        bodyOwners[body.UserData] = owner;
    }

    /// <summary>Unlinks a registered body before it is destroyed.</summary>
    public void Forget(Body body)
    {
        bodyOwners.Remove(body.UserData);
        bodyGeometry.Remove(body.UserData);
        impactSounds?.Forget(body.UserData);
    }

    /// <summary>Finds the entity of a body, or <see langword="null"/> for the world.</summary>
    public BaseEntity? GetOwner(Body body)
        => bodyOwners.TryGetValue(body.UserData, out var owner) ? owner : null;

    // 3V-6 edges at 40 vertices is 114, inside the 128 edge budget
    private const int ReducedHullVertexBudget = 40;

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
            // Zero keeps the default vertex budget
            return ConvexHull.FromPoints(transformed, 0);
        }
        catch (ArgumentException)
        {
            // Quickhull splits coplanar n-gons, a rebuilt rounded hull can exceed the edge budget
            try
            {
                return ConvexHull.FromPoints(transformed, ReducedHullVertexBudget);
            }
            catch (ArgumentException)
            {
                // Degenerate (flat or tiny) input
                return null;
            }
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
            indices[i * 3 + 0] = triangles[i].X;
            indices[i * 3 + 1] = triangles[i].Y;
            indices[i * 3 + 2] = triangles[i].Z;
        }

        return CollisionMesh.FromTriangles(positions, indices, null, default);
    }

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
        // Geometry still being built is owned by nothing yet
        foreach (var pending in pendingStaticGeometry)
        {
            if (pending.Result is { } geometry)
            {
                geometry.Compound.Dispose();

                foreach (var borrowed in geometry.Borrowed)
                {
                    borrowed.Dispose();
                }
            }
        }

        pendingStaticGeometry.Clear();

        World.Dispose();

        foreach (var geometry in borrowedGeometry)
        {
            geometry.Dispose();
        }

        borrowedGeometry.Clear();
        bodyOwners.Clear();
    }
}

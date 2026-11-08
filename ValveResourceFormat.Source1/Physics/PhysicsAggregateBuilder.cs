using System.Linq;
using System.Runtime.InteropServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.IO.KVHelpers;
using RnMesh = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace ValveResourceFormat.Source1.Physics;

/// <summary>
/// Builds Rubikon physics, as a compiled Source 2 aggregate stores it, from convex point sets and triangle meshes,
/// each tagged with a collision group, the layers it interacts as, and a surface property.
/// </summary>
internal sealed class PhysicsAggregateBuilder
{
    // Hull vertices, half edges and faces are addressed by bytes
    private const int MaxHullElements = 255;

    // How many times a polyhedron too large for one hull may be halved
    private const int MaxSplitDepth = 6;

    private const int MaxTrianglesPerLeaf = 8;

    // Points closer than this to a plane lie on it, in inches
    private const float PlaneEpsilon = 0.02f;

    // Adjacent triangles whose normals are closer than this cosine, about half a degree, can share a face
    private const float CoplanarCosine = 0.99996f;

    private readonly KVObject collisionAttributes = KVObject.Array();
    private readonly Dictionary<string, int> collisionAttributeIndices = [];
    private readonly KVObject surfacePropertyHashes = KVObject.Array();
    private readonly Dictionary<string, int> surfacePropertyIndices = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Part> parts = [];
    private readonly KVObject joints = KVObject.Array();

    private sealed class Part(string? boneName, Matrix4x4 bindPose, float mass, float inertiaScale, float linearDamping, float angularDamping)
    {
        public string? BoneName { get; } = boneName;
        public Matrix4x4 BindPose { get; } = bindPose;
        public float Mass { get; } = mass;
        public float InertiaScale { get; } = inertiaScale;
        public float LinearDamping { get; } = linearDamping;
        public float AngularDamping { get; } = angularDamping;
        public KVObject Hulls { get; } = KVObject.Array();
        public KVObject Meshes { get; } = KVObject.Array();
        public HashSet<int> UsedCollisionAttributes { get; } = [];
    }

    // Shapes go to the last part, and to a part of no bone when none was started
    private Part CurrentPart
    {
        get
        {
            if (parts.Count == 0)
            {
                parts.Add(new Part(null, Matrix4x4.Identity, 0f, 1f, 0f, 0f));
            }

            return parts[^1];
        }
    }

    /// <summary>Gets whether no shape was added.</summary>
    public bool IsEmpty => parts.TrueForAll(static part => part.Hulls.Count == 0 && part.Meshes.Count == 0);

    /// <summary>Starts a part that follows a bone, which the shapes added next belong to.</summary>
    /// <param name="boneName">The name of the bone.</param>
    /// <param name="bindPose">Takes the points of the shapes to the space of the model.</param>
    /// <param name="mass">The mass of the part in kilograms.</param>
    /// <param name="inertiaScale">Multiplies the rotational inertia derived from the shapes.</param>
    /// <param name="linearDamping">How fast the part loses linear velocity.</param>
    /// <param name="angularDamping">How fast the part loses angular velocity.</param>
    /// <returns>The index of the part.</returns>
    public int BeginPart(string boneName, Matrix4x4 bindPose, float mass, float inertiaScale = 1f, float linearDamping = 0f, float angularDamping = 0f)
    {
        parts.Add(new Part(boneName, bindPose, mass, inertiaScale, linearDamping, angularDamping));

        return parts.Count - 1;
    }

    /// <summary>Adds a joint between two parts, whose frames line up when the child part is in the middle of its swing.</summary>
    /// <param name="type">The kind of joint.</param>
    /// <param name="parentPart">The index of the part the joint hangs from.</param>
    /// <param name="childPart">The index of the part the joint moves.</param>
    /// <param name="jointToParent">Takes the frame of the joint to the space of the parent part.</param>
    /// <param name="jointToChild">Takes the frame of the joint to the space of the child part.</param>
    /// <param name="twistMin">The turn in radians of the child about the Z axis of the joint at one end of its range.</param>
    /// <param name="twistMax">The turn in radians of the child about the Z axis of the joint at the other end of its range.</param>
    /// <param name="swingLimit">How far in radians the Z axis of the child can lean away from the one of the parent.</param>
    public void AddJoint(JointType type, int parentPart, int childPart, Matrix4x4 jointToParent, Matrix4x4 jointToChild, float twistMin, float twistMax, float swingLimit)
    {
        var parentFrame = CreateFrame(jointToParent);
        var childFrame = CreateFrame(jointToChild);

        var joint = KVObject.Collection();
        joint.Add("m_nType", (int)type);
        joint.Add("m_nBody1", parentPart);
        joint.Add("m_nBody2", childPart);
        joint.Add("m_nFlags", 0);
        joint.Add("m_Frame1", parentFrame);
        joint.Add("m_Frame2", childFrame);
        joint.Add("m_bEnableCollision", false);
        joint.Add("m_bEnableLinearLimit", false);
        joint.Add("m_LinearLimit", CreateRange(0f, 0f));
        joint.Add("m_bEnableLinearMotor", false);
        joint.Add("m_vLinearTargetVelocity", ToKVArray(Vector3.Zero));
        joint.Add("m_flMaxForce", 0f);
        joint.Add("m_bEnableSwingLimit", type == JointType.Conical);
        joint.Add("m_SwingLimit", CreateRange(0f, swingLimit));
        joint.Add("m_bEnableTwistLimit", type != JointType.Weld);
        joint.Add("m_TwistLimit", CreateRange(twistMin, twistMax));
        joint.Add("m_bEnableAngularMotor", false);
        joint.Add("m_vAngularTargetVelocity", ToKVArray(Vector3.Zero));
        joint.Add("m_flMaxTorque", 0f);
        joint.Add("m_flLinearFrequency", 0f);
        joint.Add("m_flLinearDampingRatio", 0f);
        joint.Add("m_flAngularFrequency", 0f);
        joint.Add("m_flAngularDampingRatio", 0f);
        joint.Add("m_flFriction", 0f);
        joints.Add(joint);
    }

    // A joint frame is a position, a scale and a rotation
    private static KVObject CreateFrame(Matrix4x4 transform)
    {
        var position = transform.Translation;
        var rotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(transform));

        var frame = KVObject.Array();
        frame.Add(position.X);
        frame.Add(position.Y);
        frame.Add(position.Z);
        frame.Add(1f);
        frame.Add(rotation.X);
        frame.Add(rotation.Y);
        frame.Add(rotation.Z);
        frame.Add(rotation.W);

        return frame;
    }

    private static KVObject CreateRange(float min, float max)
    {
        var range = KVObject.Collection();
        range.Add("m_flMin", min);
        range.Add("m_flMax", max);

        return range;
    }

    /// <summary>Gets the number of convex point sets that did not make a closed hull and were left out.</summary>
    public int SkippedHullCount { get; private set; }

    /// <summary>Gets the index of the collision attributes of a group and its layers, adding them when new.</summary>
    /// <param name="collisionGroup">The collision group, such as <c>default</c> or <c>conditionallysolid</c>.</param>
    /// <param name="interactAs">The layers the shapes interact as.</param>
    public int GetCollisionAttributes(string collisionGroup, IReadOnlyList<string> interactAs)
    {
        var key = $"{collisionGroup}:{string.Join(',', interactAs)}";

        if (collisionAttributeIndices.TryGetValue(key, out var index))
        {
            return index;
        }

        var layers = KVObject.Array();

        foreach (var layer in interactAs)
        {
            layers.Add(layer);
        }

        var attributes = KVObject.Collection();
        attributes.Add("m_CollisionGroupString", collisionGroup);
        attributes.Add("m_InteractAsStrings", layers);
        attributes.Add("m_InteractWithStrings", KVObject.Array());
        attributes.Add("m_InteractExcludeStrings", KVObject.Array());

        index = collisionAttributes.Count;
        collisionAttributes.Add(attributes);
        collisionAttributeIndices.Add(key, index);

        return index;
    }

    /// <summary>Gets the index of a surface property, adding it when new.</summary>
    /// <param name="name">The name of the surface property.</param>
    public int GetSurfaceProperty(string name)
    {
        if (surfacePropertyIndices.TryGetValue(name, out var index))
        {
            return index;
        }

        index = surfacePropertyHashes.Count;
        surfacePropertyHashes.Add(StringToken.Store(name));
        surfacePropertyIndices.Add(name, index);

        return index;
    }

    /// <summary>Adds a convex polyhedron given as a closed triangle surface.</summary>
    /// <param name="points">The corners of the polyhedron.</param>
    /// <param name="triangles">Three indices into <paramref name="points"/> per triangle, whose planes are the face planes.</param>
    /// <param name="collisionAttributeIndex">The collision attributes, see <see cref="GetCollisionAttributes"/>.</param>
    /// <param name="surfacePropertyIndex">The surface property, see <see cref="GetSurfaceProperty"/>.</param>
    /// <returns>Whether the triangles made a closed hull, which is left out otherwise.</returns>
    /// <remarks>A polyhedron with more corners, edges or faces than a hull addresses is cut into several hulls.</remarks>
    public bool AddHull(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles, int collisionAttributeIndex, int surfacePropertyIndex)
    {
        return AddHull(points, triangles, collisionAttributeIndex, surfacePropertyIndex, MaxSplitDepth);
    }

    private bool AddHull(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles, int collisionAttributeIndex, int surfacePropertyIndex, int splitsLeft)
    {
        var hull = CreateHull(points, triangles, out var exceedsLimits);

        if (hull != null)
        {
            var part = CurrentPart;
            part.Hulls.Add(CreateDescriptor(part, "m_Hull", hull, collisionAttributeIndex, surfacePropertyIndex));
            return true;
        }

        if (exceedsLimits && splitsLeft > 0 && ConvexSplitter.TrySplit(points, triangles, out var lower, out var upper))
        {
            var isLowerAdded = AddHull(lower.Points, lower.Triangles, collisionAttributeIndex, surfacePropertyIndex, splitsLeft - 1);
            var isUpperAdded = AddHull(upper.Points, upper.Triangles, collisionAttributeIndex, surfacePropertyIndex, splitsLeft - 1);

            return isLowerAdded && isUpperAdded;
        }

        SkippedHullCount++;
        return false;
    }

    /// <summary>Adds a triangle mesh.</summary>
    /// <param name="vertices">The vertex positions.</param>
    /// <param name="indices">Three vertex indices per triangle.</param>
    /// <param name="surfaceProperties">The surface property of each triangle, see <see cref="GetSurfaceProperty"/>.</param>
    /// <param name="collisionAttributeIndex">The collision attributes, see <see cref="GetCollisionAttributes"/>.</param>
    public void AddMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, IReadOnlyList<int> surfaceProperties, int collisionAttributeIndex)
    {
        if (indices.Count < 3)
        {
            return;
        }

        var mesh = CreateMesh(vertices, indices, surfaceProperties);
        var part = CurrentPart;
        part.Meshes.Add(CreateDescriptor(part, "m_Mesh", mesh, collisionAttributeIndex, surfaceProperties[0]));
    }

    /// <summary>Creates the aggregate of every shape added, with a part for each bone started and otherwise one static part.</summary>
    /// <param name="resource">The resource the aggregate is a block of.</param>
    public PhysAggregateData Build(Resource resource)
        => new(BlockType.DATA) { Resource = resource, Data = BuildData() };

    /// <summary>Creates the data of the aggregate of every shape added.</summary>
    public KVObject BuildData()
    {
        var partData = KVObject.Array();
        var bindPose = KVObject.Array();
        var boneNames = KVObject.Array();
        var boneParents = KVObject.Array();

        foreach (var part in parts)
        {
            var attributeIndices = KVObject.Array();

            foreach (var index in part.UsedCollisionAttributes.Order())
            {
                attributeIndices.Add(index);
            }

            var shape = KVObject.Collection();
            shape.Add("m_spheres", KVObject.Array());
            shape.Add("m_capsules", KVObject.Array());
            shape.Add("m_hulls", part.Hulls);
            shape.Add("m_meshes", part.Meshes);
            shape.Add("m_CollisionAttributeIndices", attributeIndices);

            var partObject = KVObject.Collection();
            partObject.Add("m_nFlags", 0);
            partObject.Add("m_flMass", part.Mass);
            partObject.Add("m_rnShape", shape);
            partObject.Add("m_nCollisionAttributeIndex", 0);
            partObject.Add("m_flInertiaScale", part.InertiaScale);
            partObject.Add("m_flLinearDamping", part.LinearDamping);
            partObject.Add("m_flAngularDamping", part.AngularDamping);
            partData.Add(partObject);

            if (part.BoneName == null)
            {
                continue;
            }

            bindPose.Add(ToKVArray(part.BindPose));

            // Each part points at the entry of its own bone in the names
            boneParents.Add(boneNames.Count);
            boneNames.Add(part.BoneName);
        }

        var data = KVObject.Collection();
        data.Add("m_nFlags", 0);
        data.Add("m_bindPose", bindPose);
        data.Add("m_parts", partData);
        data.Add("m_joints", joints);
        data.Add("m_boneNames", boneNames);
        data.Add("m_boneParents", boneParents);
        data.Add("m_surfacePropertyHashes", surfacePropertyHashes);
        data.Add("m_collisionAttributes", collisionAttributes);

        return data;
    }

    /// <summary>Creates a physics resource of every shape added.</summary>
    public Resource BuildResource()
    {
        var resource = new Resource(ResourceType.PhysicsCollisionMesh);
        resource.AddBlock(Build(resource));

        return resource;
    }

    private static KVObject CreateDescriptor(Part part, string shapeName, KVObject shape, int collisionAttributeIndex, int surfacePropertyIndex)
    {
        part.UsedCollisionAttributes.Add(collisionAttributeIndex);

        var descriptor = KVObject.Collection();
        descriptor.Add("m_nCollisionAttributeIndex", collisionAttributeIndex);
        descriptor.Add("m_nSurfacePropertyIndex", surfacePropertyIndex);
        descriptor.Add("m_UserFriendlyName", string.Empty);
        descriptor.Add(shapeName, shape);

        return descriptor;
    }

    /// <summary>
    /// Builds the hull of a closed convex triangle surface: adjacent triangles in one plane merge into a face, and the
    /// faces that share an edge are linked by twin half edges.
    /// </summary>
    internal static KVObject? CreateHull(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles, out bool exceedsLimits)
    {
        exceedsLimits = false;

        if (points.Count > MaxHullElements)
        {
            exceedsLimits = true;
            return null;
        }

        if (points.Count < 4 || triangles.Count < 12)
        {
            return null;
        }

        var corners = GetOutwardTriangles(points, triangles);
        var triangleFaces = MergeCoplanarTriangles(points, corners, out var faceCount);

        // Each face is bounded by the edges of its triangles that no other triangle of it shares, as a map from the
        // start of each edge to its end
        var triangleEdges = new Dictionary<(int From, int To), int>();

        for (var i = 0; i < corners.Length; i++)
        {
            var from = corners[i];
            var to = corners[i % 3 == 2 ? i - 2 : i + 1];

            if (!triangleEdges.TryAdd((from, to), triangleFaces[i / 3]))
            {
                return null;
            }
        }

        var boundaries = new Dictionary<int, int>[faceCount];

        for (var face = 0; face < faceCount; face++)
        {
            boundaries[face] = [];
        }

        foreach (var ((from, to), face) in triangleEdges)
        {
            if (!triangleEdges.TryGetValue((to, from), out var otherFace))
            {
                return null;
            }

            if (otherFace != face && !boundaries[face].TryAdd(from, to))
            {
                return null;
            }
        }

        // A point on fewer than three faces lies inside an edge and is not a corner
        var vertexFaceCounts = new int[points.Count];

        foreach (var boundary in boundaries)
        {
            foreach (var from in boundary.Keys)
            {
                vertexFaceCounts[from]++;
            }
        }

        var vertexIndices = new int[points.Count];
        var vertices = new List<Vector3>();

        for (var i = 0; i < points.Count; i++)
        {
            vertexIndices[i] = -1;

            if (vertexFaceCounts[i] >= 3)
            {
                vertexIndices[i] = vertices.Count;
                vertices.Add(points[i]);
            }
        }

        var loops = new List<List<int>>();

        foreach (var boundary in boundaries)
        {
            var loop = GetFaceLoop(boundary, vertexIndices);

            if (loop == null)
            {
                return null;
            }

            loops.Add(loop);
        }

        var planes = GetFacePlanes(points, corners, triangleFaces, faceCount, vertices, loops);
        var edges = CreateHalfEdges(loops, out var faceEdges);

        if (planes == null || edges == null)
        {
            return null;
        }

        if (vertices.Count > MaxHullElements || edges.Count > MaxHullElements || loops.Count > MaxHullElements)
        {
            exceedsLimits = true;
            return null;
        }

        return CreateHullData(vertices, edges, faceEdges, planes, loops);
    }

    // The triangle corners, wound counterclockwise as seen from outside
    internal static int[] GetOutwardTriangles(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles)
    {
        var corners = new int[triangles.Count - (triangles.Count % 3)];
        var center = Vector3.Zero;

        foreach (var point in points)
        {
            center += point;
        }

        center /= points.Count;

        var volume = 0f;

        for (var i = 0; i < corners.Length; i += 3)
        {
            var first = points[triangles[i]] - center;
            volume += Vector3.Dot(first, Vector3.Cross(points[triangles[i + 1]] - center, points[triangles[i + 2]] - center));
        }

        var isInward = volume < 0f;

        for (var i = 0; i < corners.Length; i += 3)
        {
            corners[i] = triangles[i];
            corners[i + 1] = isInward ? triangles[i + 2] : triangles[i + 1];
            corners[i + 2] = isInward ? triangles[i + 1] : triangles[i + 2];
        }

        return corners;
    }

    // Groups the triangles that share an edge and lie in one plane, returning the face of each triangle
    private static int[] MergeCoplanarTriangles(IReadOnlyList<Vector3> points, int[] corners, out int faceCount)
    {
        var triangleCount = corners.Length / 3;
        var normals = new Vector3[triangleCount];
        var edgeTriangles = new Dictionary<(int From, int To), int>();
        var groups = new int[triangleCount];

        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            normals[triangle] = Vector3.Normalize(GetTriangleCross(points, corners, triangle));
            groups[triangle] = triangle;

            for (var corner = 0; corner < 3; corner++)
            {
                var from = corners[(triangle * 3) + corner];
                var to = corners[(triangle * 3) + ((corner + 1) % 3)];
                edgeTriangles[(from, to)] = triangle;
            }
        }

        foreach (var ((from, to), triangle) in edgeTriangles)
        {
            if (!edgeTriangles.TryGetValue((to, from), out var neighbour))
            {
                continue;
            }

            var isParallel = Vector3.Dot(normals[triangle], normals[neighbour]) > CoplanarCosine;

            // The corner of the neighbour that is not on the shared edge must lie in the plane of the triangle
            var neighbourCorner = corners[neighbour * 3] + corners[(neighbour * 3) + 1] + corners[(neighbour * 3) + 2] - from - to;
            var distance = Vector3.Dot(normals[triangle], points[neighbourCorner] - points[from]);

            if (isParallel && MathF.Abs(distance) <= PlaneEpsilon)
            {
                groups[FindGroup(groups, triangle)] = FindGroup(groups, neighbour);
            }
        }

        var faceIndices = new Dictionary<int, int>();
        var triangleFaces = new int[triangleCount];

        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var group = FindGroup(groups, triangle);

            if (!faceIndices.TryGetValue(group, out var face))
            {
                face = faceIndices.Count;
                faceIndices.Add(group, face);
            }

            triangleFaces[triangle] = face;
        }

        faceCount = faceIndices.Count;
        return triangleFaces;
    }

    private static int FindGroup(int[] groups, int triangle)
    {
        while (groups[triangle] != triangle)
        {
            groups[triangle] = groups[groups[triangle]];
            triangle = groups[triangle];
        }

        return triangle;
    }

    private static Vector3 GetTriangleCross(IReadOnlyList<Vector3> points, int[] corners, int triangle)
    {
        return MathUtils.TriangleCross(points[corners[triangle * 3]], points[corners[(triangle * 3) + 1]], points[corners[(triangle * 3) + 2]]);
    }

    // Walks the boundary of a face once around, keeping its corners, or returns null when it is not one loop
    private static List<int>? GetFaceLoop(Dictionary<int, int> boundary, int[] vertexIndices)
    {
        var loop = new List<int>();
        var start = -1;

        foreach (var from in boundary.Keys)
        {
            if (vertexIndices[from] >= 0)
            {
                start = from;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var current = start;
        var length = 0;

        do
        {
            if (length == boundary.Count || !boundary.TryGetValue(current, out var next))
            {
                return null;
            }

            if (vertexIndices[current] >= 0)
            {
                loop.Add(vertexIndices[current]);
            }

            current = next;
            length++;
        }
        while (current != start);

        // Every boundary edge must be on the loop, or the face has a hole or is split
        if (length != boundary.Count || loop.Count < 3)
        {
            return null;
        }

        return loop;
    }

    // The plane of each face, with the area weighted normal of its triangles, through the average of its corners
    private static List<Vector4>? GetFacePlanes(IReadOnlyList<Vector3> points, int[] corners, int[] triangleFaces, int faceCount,
        List<Vector3> vertices, List<List<int>> loops)
    {
        var normals = new Vector3[faceCount];

        for (var triangle = 0; triangle < triangleFaces.Length; triangle++)
        {
            normals[triangleFaces[triangle]] += GetTriangleCross(points, corners, triangle);
        }

        var planes = new List<Vector4>(faceCount);

        for (var face = 0; face < faceCount; face++)
        {
            // The normal is weighted by area, so a small triangle of a rounded shape still has a plane
            if (normals[face].LengthSquared() == 0f)
            {
                return null;
            }

            var normal = Vector3.Normalize(normals[face]);
            var distance = 0f;

            foreach (var corner in loops[face])
            {
                distance += Vector3.Dot(normal, vertices[corner]);
            }

            planes.Add(new Vector4(normal, distance / loops[face].Count));
        }

        return planes;
    }

    // Half edges as Rubikon stores them, four bytes each: next, twin, origin and face, every edge followed by its twin
    private static List<byte[]>? CreateHalfEdges(List<List<int>> loops, out int[] faceEdges)
    {
        var edges = new List<byte[]>();
        var edgeByVertices = new Dictionary<(int From, int To), int>();
        var assigned = new List<bool>();
        faceEdges = new int[loops.Count];

        for (var face = 0; face < loops.Count; face++)
        {
            var loop = loops[face];
            var loopEdges = new int[loop.Count];

            for (var i = 0; i < loop.Count; i++)
            {
                var from = loop[i];
                var to = loop[(i + 1) % loop.Count];

                if (!edgeByVertices.TryGetValue((from, to), out var edge))
                {
                    edge = edges.Count;
                    edges.Add([0, (byte)(edge + 1), (byte)from, 0]);
                    edges.Add([0, (byte)edge, (byte)to, 0]);
                    assigned.Add(false);
                    assigned.Add(false);
                    edgeByVertices.Add((from, to), edge);
                    edgeByVertices.Add((to, from), edge + 1);
                }

                if (assigned[edge])
                {
                    return null;
                }

                assigned[edge] = true;
                edges[edge][3] = (byte)face;
                loopEdges[i] = edge;
            }

            for (var i = 0; i < loopEdges.Length; i++)
            {
                edges[loopEdges[i]][0] = (byte)loopEdges[(i + 1) % loopEdges.Length];
            }

            faceEdges[face] = loopEdges[0];
        }

        // Every half edge must have a face, or the hull is not closed
        foreach (var isAssigned in assigned)
        {
            if (!isAssigned)
            {
                return null;
            }
        }

        return edges;
    }

    private static KVObject CreateHullData(List<Vector3> vertices, List<byte[]> edges, int[] faceEdges, List<Vector4> planes, List<List<int>> loops)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }

        // Volume, centroid and area from a fan of tetrahedra over each face, apexed at a point inside
        var inside = (min + max) * 0.5f;
        var volume = 0f;
        var surfaceArea = 0f;
        var weightedCentroid = Vector3.Zero;

        foreach (var loop in loops)
        {
            var first = vertices[loop[0]];

            for (var i = 1; i < loop.Count - 1; i++)
            {
                var second = vertices[loop[i]];
                var third = vertices[loop[i + 1]];
                var cross = Vector3.Cross(second - first, third - first);
                var tetrahedronVolume = Vector3.Dot(first - inside, cross) / 6f;

                surfaceArea += cross.Length() * 0.5f;
                volume += tetrahedronVolume;
                weightedCentroid += tetrahedronVolume * (inside + first + second + third) / 4f;
            }
        }

        var centroid = volume > 0f ? weightedCentroid / volume : inside;
        var maxRadius = 0f;

        foreach (var vertex in vertices)
        {
            maxRadius = MathF.Max(maxRadius, Vector3.Distance(vertex, centroid));
        }

        var vertexIndices = new byte[vertices.Count];

        for (var i = 0; i < vertexIndices.Length; i++)
        {
            vertexIndices[i] = (byte)i;
        }

        var edgeBytes = new byte[edges.Count * 4];

        for (var i = 0; i < edges.Count; i++)
        {
            edges[i].CopyTo(edgeBytes, i * 4);
        }

        var faceBytes = new byte[faceEdges.Length];

        for (var i = 0; i < faceEdges.Length; i++)
        {
            faceBytes[i] = (byte)faceEdges[i];
        }

        var bounds = KVObject.Collection();
        bounds.Add("m_vMinBounds", ToKVArray(min));
        bounds.Add("m_vMaxBounds", ToKVArray(max));

        var hull = KVObject.Collection();
        hull.Add("m_vCentroid", ToKVArray(centroid));
        hull.Add("m_flMaxAngularRadius", maxRadius);
        hull.Add("m_vOrthographicAreas", ToKVArray(Vector3.Zero));
        hull.Add("m_flVolume", volume);
        hull.Add("m_flSurfaceArea", surfaceArea);
        hull.Add("m_nFlags", 0);
        hull.Add("m_Bounds", bounds);
        hull.Add("m_VertexPositions", KVObject.Blob(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(vertices)).ToArray()));
        hull.Add("m_Vertices", KVObject.Blob(vertexIndices));
        hull.Add("m_Edges", KVObject.Blob(edgeBytes));
        hull.Add("m_Faces", KVObject.Blob(faceBytes));
        hull.Add("m_Planes", KVObject.Blob(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(planes)).ToArray()));

        return hull;
    }

    private static KVObject CreateMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices, IReadOnlyList<int> surfaceProperties)
    {
        var triangleCount = indices.Count / 3;
        var order = new int[triangleCount];
        var centers = new Vector3[triangleCount];

        for (var i = 0; i < triangleCount; i++)
        {
            order[i] = i;
            centers[i] = (vertices[indices[i * 3]] + vertices[indices[(i * 3) + 1]] + vertices[indices[(i * 3) + 2]]) / 3f;
        }

        var nodes = new List<RnMesh.Node>();
        AddMeshNode(nodes, vertices, indices, centers, order, 0, triangleCount);

        // The tree addresses triangles in the order its leaves were built
        var triangles = new int[triangleCount * 3];
        var materials = KVObject.Array();
        var hasMixedMaterials = false;

        for (var i = 0; i < triangleCount; i++)
        {
            triangles[i * 3] = indices[order[i] * 3];
            triangles[(i * 3) + 1] = indices[(order[i] * 3) + 1];
            triangles[(i * 3) + 2] = indices[(order[i] * 3) + 2];
            hasMixedMaterials |= surfaceProperties[i] != surfaceProperties[0];
        }

        // A mesh of one surface property leaves it to its descriptor
        if (hasMixedMaterials)
        {
            foreach (var triangle in order)
            {
                materials.Add(surfaceProperties[triangle]);
            }
        }

        var vertexArray = vertices.ToArray();

        var mesh = KVObject.Collection();
        mesh.Add("m_vMin", ToKVArray(nodes[0].Min));
        mesh.Add("m_vMax", ToKVArray(nodes[0].Max));
        mesh.Add("m_Materials", materials);
        mesh.Add("m_vOrthographicAreas", ToKVArray(Vector3.Zero));
        mesh.Add("m_nFlags", 0);
        mesh.Add("m_nDebugFlags", 0);
        mesh.Add("m_Vertices", KVObject.Blob(MemoryMarshal.AsBytes(vertexArray.AsSpan()).ToArray()));
        mesh.Add("m_Triangles", KVObject.Blob(MemoryMarshal.AsBytes(triangles.AsSpan()).ToArray()));
        mesh.Add("m_Nodes", KVObject.Blob(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(nodes)).ToArray()));

        return mesh;
    }

    // Adds the subtree of triangles [start, start + count) depth first: a split node is followed by its left child,
    // and stores how far after it its right child is
    private static void AddMeshNode(List<RnMesh.Node> nodes, IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices,
        Vector3[] centers, int[] order, int start, int count)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        for (var i = start; i < start + count; i++)
        {
            for (var corner = 0; corner < 3; corner++)
            {
                var vertex = vertices[indices[(order[i] * 3) + corner]];
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }
        }

        var nodeIndex = nodes.Count;

        if (count <= MaxTrianglesPerLeaf)
        {
            nodes.Add(new RnMesh.Node(min, max, RnMesh.NodeType.Leaf, (uint)count, (uint)start));
            return;
        }

        var extent = max - min;
        var axis = RnMesh.NodeType.SplitX;

        if (extent.Y > extent.X && extent.Y >= extent.Z)
        {
            axis = RnMesh.NodeType.SplitY;
        }
        else if (extent.Z > extent.X && extent.Z > extent.Y)
        {
            axis = RnMesh.NodeType.SplitZ;
        }

        var axisIndex = (int)axis;
        Array.Sort(order, start, count, Comparer<int>.Create((a, b) => centers[a][axisIndex].CompareTo(centers[b][axisIndex])));

        nodes.Add(default);

        var leftCount = count / 2;
        AddMeshNode(nodes, vertices, indices, centers, order, start, leftCount);

        var rightIndex = nodes.Count;
        AddMeshNode(nodes, vertices, indices, centers, order, start + leftCount, count - leftCount);

        nodes[nodeIndex] = new RnMesh.Node(min, max, axis, (uint)(rightIndex - nodeIndex), 0);
    }
}

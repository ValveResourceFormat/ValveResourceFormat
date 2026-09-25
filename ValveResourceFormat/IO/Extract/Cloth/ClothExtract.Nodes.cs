using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// The name the document references a control node by: the exported <c>$cloth_m{N}p{L}</c> name for a proxy vertex,
    /// the element name for a free <c>ClothNode</c>, and the control name for anything else.
    /// </summary>
    private static string? AuthoredNodeName(FeModel feModel, int node, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        if (node < 0 || node >= feModel.CtrlNames.Length)
        {
            return null;
        }

        var name = feModel.CtrlNames[node];
        if (name.StartsWith("$cloth_m", StringComparison.Ordinal))
        {
            return proxyNodeNames?.GetValueOrDefault(node);
        }

        return name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal)
            ? name[FeModel.FreeClothNodePrefix.Length..]
            : name;
    }

    /// <summary>
    /// Declares every control node no other construct covers as a <c>ClothNode</c> or a one-joint <c>ClothChain</c>, then
    /// the rods between declared nodes (and from them to <paramref name="chainJoints"/>) as springs. Returns the number of
    /// nodes declared.
    /// </summary>
    internal static int AddFreeClothNodesAndSprings(KVObject clothChildren, KVObject softbodyChildren,
        FeModel feModel, HashSet<int> coveredNodes, Func<string, bool> emitBareStatic,
        HashSet<string> clothBones, Func<int, bool, KVObject>? folderFor = null, bool hasOtherChains = false,
        Func<string, bool>? bareStaticReparented = null, HashSet<(int, int)>? alreadyEmitted = null,
        HashSet<int>? chainJoints = null)
    {
        var names = feModel.CtrlNames;

        var anchorOf = BuildCtrlAnchorMap(feModel);

        var nodeByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var node = 0; node < names.Length; node++)
        {
            nodeByName.TryAdd(names[node], node);
        }

        var jiggleNodes = feModel.JiggleBones.Select(static j => j.Node).ToHashSet();
        var shapeParentBones = CollisionShapeParentBones(feModel);

        var rodTouched = new HashSet<int>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA != rod.NodeB)
            {
                rodTouched.Add(rod.NodeA);
                rodTouched.Add(rod.NodeB);
            }
        }

        var springName = new Dictionary<int, string>();
        var declared = new HashSet<int>();
        var emitted = 0;

        KVObject FolderOf(int node)
            => folderFor is not null ? folderFor(node, !rodTouched.Contains(node)) : clothChildren;

        for (var node = 0; node < names.Length; node++)
        {
            var name = names[node];
            if (coveredNodes.Contains(node) || jiggleNodes.Contains(node) || shapeParentBones.Contains(name))
            {
                continue;
            }

            if (name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal))
            {
                var elementName = name[FeModel.FreeClothNodePrefix.Length..];
                if (!TryResolveClothNodeAnchor(feModel, anchorOf, node, out var rootBone, out var origin, out var angles))
                {
                    continue;
                }

                FolderOf(node).Add(MakeClothNode(feModel, rootBone, node,
                    isStaticNode: feModel.IsStatic(node), elementName: elementName, origin: origin, angles: angles));
                springName[node] = elementName;
                declared.Add(node);
                clothBones.Add(rootBone);
                emitted++;

                if (nodeByName.TryGetValue(rootBone, out var rootNode))
                {
                    springName.TryAdd(rootNode, rootBone);
                }
            }
            else if (!feModel.IsGeneratedNodeName(name))
            {
                var isStatic = feModel.IsStatic(node);
                var bareStatic = isStatic && !rodTouched.Contains(node);
                if (!isStatic || !bareStatic || emitBareStatic(name))
                {
                    var loneNode = LoneNodeIsJointChain(feModel, node, bareStatic, bareStaticReparented?.Invoke(name) ?? false)
                        && !(StrayRecordOnlyAClothNodeStates(feModel, node) && bareStaticReparented?.Invoke(name) == false);
                    (loneNode ? clothChildren : FolderOf(node)).Add(loneNode
                        ? MakeLoneJointChain(feModel, name, node, hasOtherChains)
                        : MakeClothNode(feModel, name, node, isStaticNode: isStatic));
                    springName[node] = name;
                    declared.Add(node);
                    clothBones.Add(name);
                    emitted++;
                }
            }
        }

        if (springName.Count > 0)
        {
            AddFreeClothSprings(softbodyChildren, feModel, springName, declared, alreadyEmitted, chainJoints);
        }

        return emitted;
    }

    /// <summary>
    /// Declares the rods between the nodes <paramref name="springName"/> names, and from a declared node to one of
    /// <paramref name="chainJoints"/>, skipping the pairs in <paramref name="alreadyEmitted"/>.
    /// </summary>
    private static void AddFreeClothSprings(KVObject softbodyChildren, FeModel feModel, Dictionary<int, string> springName,
        HashSet<int> declared, HashSet<(int, int)>? alreadyEmitted, HashSet<int>? chainJoints)
    {
        var names = feModel.CtrlNames;

        bool IsEndpoint(int node, int other) => springName.ContainsKey(node)
            || (chainJoints is not null && chainJoints.Contains(node) && declared.Contains(other));

        // A pair whose rods are identical copies is one spring with extra_iterations; any other pair keeps a spring per rod.
        var rodsByEdge = new Dictionary<(int, int), List<FeModel.Rod>>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA == rod.NodeB || !IsEndpoint(rod.NodeA, rod.NodeB) || !IsEndpoint(rod.NodeB, rod.NodeA))
            {
                continue;
            }

            GetOrAdd(rodsByEdge, RodPair(rod)).Add(rod);
        }

        foreach (var (edge, rods) in rodsByEdge)
        {
            if (alreadyEmitted is not null && alreadyEmitted.Contains(edge))
            {
                continue;
            }

            var name0 = springName.GetValueOrDefault(edge.Item1) ?? names[edge.Item1];
            var name1 = springName.GetValueOrDefault(edge.Item2) ?? names[edge.Item2];

            // A spring keeps its source element's corner order, which a spring reaching a chain joint may reverse.
            if ((!declared.Contains(edge.Item1) || !declared.Contains(edge.Item2))
                && Array.IndexOf(feModel.SourceSprings, (edge.Item2, edge.Item1)) >= 0
                && Array.IndexOf(feModel.SourceSprings, (edge.Item1, edge.Item2)) < 0)
            {
                (name0, name1) = (name1, name0);
            }

            var first = rods[0];

            if (IsUnrecordedJointTie(feModel, edge, rods, springName))
            {
                var memberStiffness = MathF.Sqrt(first.RelaxationFactor);
                softbodyChildren.Add(MakeClothSelfCollisionCluster($"cluster_{edge.Item1}_{edge.Item2}", [name0, name1],
                    first.MaxDist / 2f, first.MaxDist / 2f, [memberStiffness, memberStiffness]));
                continue;
            }

            var allIdentical = rods.TrueForAll(rod => rod.MinDist == first.MinDist
                && rod.MaxDist == first.MaxDist && rod.RelaxationFactor == first.RelaxationFactor);

            if (rods.Count > 1 && allIdentical)
            {
                softbodyChildren.Add(MakeClothSpring($"rod_{edge.Item1}_{edge.Item2}", name0, name1,
                    first.MinDist, first.MaxDist, first.RelaxationFactor, extraIterations: rods.Count - 1));
                continue;
            }

            for (var copy = 0; copy < rods.Count; copy++)
            {
                var rod = rods[copy];
                var springLabel = copy == 0 ? $"rod_{edge.Item1}_{edge.Item2}" : $"rod_{edge.Item1}_{edge.Item2}_{copy}";
                softbodyChildren.Add(MakeClothSpring(springLabel, name0, name1, rod.MinDist, rod.MaxDist,
                    rod.RelaxationFactor));
            }
        }
    }

    /// <summary>
    /// Whether the single rigid rod on <paramref name="edge"/> between a declared node and a chain joint has no two-corner
    /// source element in the original, so it is declared as a two-member <c>ClothSelfCollisionCluster</c>.
    /// </summary>
    internal static bool IsUnrecordedJointTie(FeModel feModel, (int A, int B) edge, List<FeModel.Rod> rods,
        Dictionary<int, string> springName)
    {
        if (springName.ContainsKey(edge.A) && springName.ContainsKey(edge.B))
        {
            return false;
        }

        if (rods.Count != 1 || HasSourceSpring(feModel, edge.A, edge.B))
        {
            return false;
        }

        var rod = rods[0];
        return MathF.Abs(rod.MinDist - rod.MaxDist) <= 1e-4f * MathF.Max(1f, MathF.Abs(rod.MaxDist));
    }

    /// <summary>Builds the test for whether a bone has an ancestor that is a cloth control node.</summary>
    private Func<string, bool> ClothControlAncestorTest(FeModel feModel)
    {
        var (controlNames, boneByName) = ClothControlLookups(feModel);
        return name =>
        {
            if (boneByName is null || !boneByName.TryGetValue(name, out var bone))
            {
                return false;
            }

            for (var ancestor = bone.Parent; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (controlNames.Contains(ancestor.Name))
                {
                    return true;
                }
            }

            return false;
        };
    }

    /// <summary>Builds the test for whether a bone's parent bone is a cloth control node.</summary>
    private Func<string, bool> ClothControlParentTest(FeModel feModel)
    {
        var (controlNames, boneByName) = ClothControlLookups(feModel);
        return name => boneByName is not null && boneByName.TryGetValue(name, out var bone)
            && bone.Parent is not null && controlNames.Contains(bone.Parent.Name);
    }

    private (HashSet<string> ControlNames, Dictionary<string, Bone>? BoneByName) ClothControlLookups(FeModel feModel)
        => (new HashSet<string>(feModel.CtrlNames, StringComparer.Ordinal),
            model?.Skeleton.Bones.ToDictionary(static b => b.Name, StringComparer.Ordinal));

    /// <summary>
    /// Whether a node's stray radius record can only be stated by a <c>ClothNode</c>: a chain joint's stretchiness at or
    /// above <see cref="ChainStrayStretchinessLimit"/> cancels the radius.
    /// </summary>
    internal static bool StrayRecordOnlyAClothNodeStates(FeModel feModel, int node)
        => feModel.GetStrayRadius(node) > 0f && feModel.GetStrayStretchiness(node) >= ChainStrayStretchinessLimit;

    private const float ChainStrayStretchinessLimit = 0.99999988f;

    private static bool LoneClothNodeIsOriginalRoot(FeModel feModel, int node)
        => feModel.HasCompiledSkelParents
            && node < feModel.SkelParents.Length && feModel.SkelParents[node] < 0;

    /// <summary>
    /// Whether a lone real cloth node is re-declared as a one-joint <c>ClothChain</c> rather than a <c>ClothNode</c>: an
    /// <c>m_SkelParents</c> root that is dynamic, a bare static node a <c>ClothNode</c> would re-parent, or locked to its
    /// goal.
    /// </summary>
    internal static bool LoneNodeIsJointChain(FeModel feModel, int node, bool bareStatic, bool bareStaticReparented)
        => LoneClothNodeIsOriginalRoot(feModel, node)
            && (!feModel.IsStatic(node) || (bareStatic && bareStaticReparented) || feModel.IsLockedToGoal(node));

    private static KVObject MakeLoneJointChain(FeModel feModel, string name, int node, bool hasOtherChains)
    {
        var chain = new FeModel.BoneChain { RootBone = name };
        chain.Joints.Add(new FeModel.BoneChainJoint
        {
            Node = node,
            Name = name,
            ParentNode = -1,
            InvMass = node < feModel.NodeInvMasses.Length ? feModel.NodeInvMasses[node] : 0f,
        });
        return MakeClothChainNode(feModel, chain, hasOtherChains);
    }

    /// <summary>
    /// The <c>transform_alignment</c> of a <c>ClothNode</c> on an <c>m_Ropes</c> run: 2 for an element node or a based
    /// node, 1 otherwise, and 0 off the ropes.
    /// </summary>
    internal static int RopeClothNodeAlignment(FeModel feModel, int node, bool isElement, bool hasBasis)
    {
        if (!feModel.IsRopeNode(node))
        {
            return 0;
        }

        return isElement || hasBasis ? 2 : 1;
    }

    /// <summary>The number of distinct nodes <paramref name="node"/> shares an <c>m_Rods</c> record with.</summary>
    internal static int RodNeighbourCount(FeModel feModel, int node)
    {
        var neighbours = new HashSet<int>();
        foreach (var rod in feModel.Rods)
        {
            if (rod.NodeA == node && rod.NodeB != node)
            {
                neighbours.Add(rod.NodeB);
            }
            else if (rod.NodeB == node && rod.NodeA != node)
            {
                neighbours.Add(rod.NodeA);
            }
        }

        return neighbours.Count;
    }

    internal static KVObject MakeClothNode(FeModel feModel, string boneName, int node, bool isStaticNode = false,
        string? elementName = null, Vector3 origin = default, Vector3 angles = default,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var integrator = feModel.GetIntegrator(node);
        var goalStrength = feModel.GoalStrengthPaint(integrator.ForceAttraction);
        var goalDamping = feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction);
        var strayRadius = feModel.GetStrayRadius(node);

        var hasBasis = feModel.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode)
        {
            if (!hasBasis || basisNode < 0 || basisNode >= feModel.CtrlNames.Length)
            {
                return string.Empty;
            }

            return AuthoredNodeName(feModel, basisNode, proxyNodeNames) ?? string.Empty;
        }

        // A basis preset is written only where the default alignment would drop the original's basis.
        var preset = elementName is not null || (isStaticNode && !feModel.AllowsRotation(node)) || RodNeighbourCount(feModel, node) < 2
            ? feModel.ClothNodeBasisPreset(node)
            : null;
        var references = preset?.References ?? basis;

        var collisionMask = feModel.GetNodeCollisionMask(node);

        return BuildClothNode(new ClothNodeFields
        {
            Name = elementName ?? boneName,
            Origin = origin,
            Angles = angles,
            RootBone = boneName,
            HasStrayRadius = strayRadius > 0f,
            HasWorldCollision = feModel.IsWorldCollisionNode(node),
            CollisionMask = collisionMask,
            TransformAlignment = preset?.TransformAlignment ?? RopeClothNodeAlignment(feModel, node, elementName is not null, hasBasis),
            NodeBaseY1 = BasisName(references.NodeY1),
            NodeBaseX1 = BasisName(references.NodeX1),
            NodeBaseY0 = BasisName(references.NodeY0),
            NodeBaseX0 = BasisName(references.NodeX0),
            LockTranslation = feModel.LocksTranslation(node),
            GravityZ = integrator.Gravity / FeModel.ClothSourceBaseGravity,
            GoalStrength = goalStrength,
            GoalDamping = goalDamping,
            Mass = feModel.RecoverMassMultiplier(node) ?? 1.0f,
            Friction = feModel.GetNodeFriction(node),
            StrayRadius = strayRadius,
            StrayRadiusRelaxationFactor = feModel.GetStrayRelaxationFactor(node),
            CollisionRadius = feModel.GetCollisionRadius(node),
            IsStaticNode = isStaticNode,
            AllowRotation = feModel.AllowsRotation(node),
            SuperDamping = Math.Clamp(integrator.PointDamping / FeModel.ClothDragPointDampingScale, 0f, 1f),
        });
    }

    /// <summary>The keys of a declared <c>ClothNode</c>, each defaulting to its neutral value.</summary>
    private readonly record struct ClothNodeFields()
    {
        public required string Name { get; init; }
        public required string RootBone { get; init; }
        public Vector3 Origin { get; init; }
        public Vector3 Angles { get; init; }
        public bool HasStrayRadius { get; init; }
        public bool HasWorldCollision { get; init; }
        public int CollisionMask { get; init; }
        public int TransformAlignment { get; init; }
        public string NodeBaseY1 { get; init; } = string.Empty;
        public string NodeBaseX1 { get; init; } = string.Empty;
        public string NodeBaseY0 { get; init; } = string.Empty;
        public string NodeBaseX0 { get; init; } = string.Empty;
        public bool LockTranslation { get; init; }
        public float GravityZ { get; init; } = 1.0f;
        public float GoalStrength { get; init; }
        public float GoalDamping { get; init; }
        public float Mass { get; init; } = 1.0f;
        public float Friction { get; init; }
        public float StrayRadius { get; init; }
        public float StrayRadiusRelaxationFactor { get; init; } = 1.0f;
        public float CollisionRadius { get; init; }
        public bool IsStaticNode { get; init; }
        public bool AllowRotation { get; init; }
        public float? SuperDamping { get; init; }
    }

    /// <summary>A <c>ClothNode</c> with every key of <paramref name="fields"/>, in the order the editor writes them.</summary>
    private static KVObject BuildClothNode(ClothNodeFields fields)
    {
        var node = MakeNode("ClothNode",
            ("name", fields.Name),
            ("origin", ToKVArray(fields.Origin)),
            ("angles", ToKVArray(fields.Angles)),
            ("cloth_node_root_bone", fields.RootBone),
            ("has_stray_radius", fields.HasStrayRadius),
            ("has_world_collision", fields.HasWorldCollision));
        AddCollisionLayerFlags(node, "cloth_collision_layer", ClothNodeLayerMask(fields.CollisionMask));
        node.Add("transform_alignment", fields.TransformAlignment);
        node.Add("node_base_y1", fields.NodeBaseY1);
        node.Add("node_base_x1", fields.NodeBaseX1);
        node.Add("node_base_y0", fields.NodeBaseY0);
        node.Add("node_base_x0", fields.NodeBaseX0);
        node.Add("lock_translation", fields.LockTranslation);
        node.Add("gravity_z", fields.GravityZ);
        node.Add("goal_strength", fields.GoalStrength);
        node.Add("goal_damping", fields.GoalDamping);
        node.Add("mass", fields.Mass);
        node.Add("friction", fields.Friction);
        node.Add("stray_radius", fields.StrayRadius);
        node.Add("stray_radius_relaxation_factor", fields.StrayRadiusRelaxationFactor);
        node.Add("collision_radius", fields.CollisionRadius);
        node.Add("is_static_node", fields.IsStaticNode);
        node.Add("allow_rotation", fields.AllowRotation);
        if (fields.SuperDamping is { } superDamping)
        {
            node.Add("super_damping", superDamping);
        }

        return node;
    }

    /// <summary>
    /// The layer mask a <c>ClothNode</c> declares for a compiled <paramref name="mask"/>: all four layers stand for the
    /// default mask, which is also what a mask outside 0..14 falls back to.
    /// </summary>
    private static int ClothNodeLayerMask(int mask) => mask is >= 0 and <= 14 ? mask : 0xF;

    /// <summary>
    /// The name a <c>ClothTri</c> or <c>ClothQuad</c> corner references a control node by: the element name for a free
    /// <c>ClothNode</c> and the control name otherwise.
    /// </summary>
    private static string ClothFaceCornerName(FeModel feModel, int node)
    {
        var name = feModel.CtrlNames[node];
        return name.StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal)
            ? name[FeModel.FreeClothNodePrefix.Length..]
            : name;
    }

    /// <summary>
    /// Declares a <c>ClothStiffHinge</c> for every compiled bend whose three nodes are free cloth nodes, recovering
    /// <c>max_angle</c> from <c>9 * height^2 = |b1|^2 + |b2|^2 - 2 |b1| |b2| cos(max_angle)</c>.
    /// </summary>
    internal static void AddClothStiffHinges(KVObject softbodyChildren, FeModel feModel)
    {
        bool IsFreeNode(int node) => node >= 0 && node < feModel.CtrlNames.Length && node < feModel.InitPosePositions.Length
            && feModel.CtrlNames[node].StartsWith(FeModel.FreeClothNodePrefix, StringComparison.Ordinal);

        foreach (var bend in feModel.KelagerBends)
        {
            if (!IsFreeNode(bend.MidNode) || !IsFreeNode(bend.End0) || !IsFreeNode(bend.End1))
            {
                continue;
            }

            var hinge = feModel.InitPosePositions[bend.MidNode];
            var base1 = (feModel.InitPosePositions[bend.End0] - hinge).Length();
            var base2 = (feModel.InitPosePositions[bend.End1] - hinge).Length();
            if (base1 <= 0f || base2 <= 0f)
            {
                continue;
            }

            var cosine = ((base1 * base1) + (base2 * base2) - (9f * bend.Height * bend.Height)) / (2f * base1 * base2);
            softbodyChildren.Add(MakeNode("ClothStiffHinge",
                ("cloth_node_0", ClothFaceCornerName(feModel, bend.MidNode)),
                ("cloth_node_1", ClothFaceCornerName(feModel, bend.End0)),
                ("cloth_node_2", ClothFaceCornerName(feModel, bend.End1)),
                ("max_angle", float.RadiansToDegrees(MathF.Acos(Math.Clamp(cosine, -1f, 1f))))));
        }
    }

    /// <summary>
    /// The <c>ClothTri</c> or <c>ClothQuad</c> element over one compiled face, with repeated corners collapsed, or null
    /// when fewer than three remain.
    /// </summary>
    private static KVObject? MakeClothFace(FeModel feModel, int[] face)
    {
        var corners = new List<int>(4);
        foreach (var corner in face)
        {
            if (!corners.Contains(corner))
            {
                corners.Add(corner);
            }
        }

        if (corners.Count is not (3 or 4))
        {
            return null;
        }

        var node = MakeNode(corners.Count == 4 ? "ClothQuad" : "ClothTri");
        for (var i = 0; i < corners.Count; i++)
        {
            node.Add("cloth_node_" + i.ToString(CultureInfo.InvariantCulture),
                ClothFaceCornerName(feModel, corners[i]));
        }

        return node;
    }

    /// <summary>Declares every face the original built from <c>ClothTri</c> and <c>ClothQuad</c> elements.</summary>
    private static void AddClothFaces(KVObject clothChildren, FeModel feModel)
    {
        foreach (var face in feModel.GetAuthoredElementFaces())
        {
            if (MakeClothFace(feModel, face) is { } element)
            {
                clothChildren.Add(element);
            }
        }
    }

    private static Dictionary<int, FeModel.CtrlOffset> BuildCtrlAnchorMap(FeModel feModel)
    {
        var anchorOf = new Dictionary<int, FeModel.CtrlOffset>();
        foreach (var offset in feModel.CtrlOffsets)
        {
            anchorOf[offset.CtrlChild] = offset;
        }

        return anchorOf;
    }

    /// <summary>
    /// Resolves the root bone, bone-local origin and angles a free <c>$cloth_node_</c> control node is re-authored at,
    /// from its <c>m_CtrlOffsets</c> entry or else its skeleton parent. False where the root is a generated node.
    /// </summary>
    internal static bool TryResolveClothNodeAnchor(FeModel feModel, Dictionary<int, FeModel.CtrlOffset> anchorOf,
        int node, [NotNullWhen(true)] out string? rootBone, out Vector3 origin,
        out Vector3 angles)
    {
        var names = feModel.CtrlNames;
        rootBone = null;
        origin = default;
        angles = default;
        var parent = -1;

        if (anchorOf.TryGetValue(node, out var anchor)
            && anchor.CtrlParent >= 0 && anchor.CtrlParent < names.Length)
        {
            parent = anchor.CtrlParent;
            rootBone = names[parent];
            origin = anchor.Offset;
        }
        else if (node < feModel.SkelParents.Length
            && feModel.SkelParents[node] >= 0 && feModel.SkelParents[node] < names.Length)
        {
            parent = feModel.SkelParents[node];
            rootBone = names[parent];
            if (node < feModel.InitPosePositions.Length && parent < feModel.InitPosePositions.Length
                && parent < feModel.InitPoseRotations.Length)
            {
                origin = Vector3.Transform(
                    feModel.InitPosePositions[node] - feModel.InitPosePositions[parent],
                    Quaternion.Conjugate(feModel.InitPoseRotations[parent]));
            }
        }

        if (parent >= 0 && node < feModel.InitPoseRotations.Length && parent < feModel.InitPoseRotations.Length)
        {
            var local = Quaternion.Conjugate(feModel.InitPoseRotations[parent]) * feModel.InitPoseRotations[node];
            if (2f * MathF.Atan2(new Vector3(local.X, local.Y, local.Z).Length(), MathF.Abs(local.W)) > ClothNodeRotationTolerance)
            {
                angles = EntityTransformHelper.ToEulerAngles(local);
            }
        }

        if (rootBone is not null && angles == Vector3.Zero && origin.Length() < ClothNodeMergeRadius)
        {
            // Push a node the compiler would merge into its root bone just outside the merge radius.
            var direction = origin == Vector3.Zero ? Vector3.One : origin;
            origin = Vector3.Normalize(direction) * (ClothNodeMergeRadius * 1.25f);
        }

        return rootBone is not null && !FeModel.IsProxyNodeName(rootBone);
    }

    // Bone-local distance under which the compiler merges a free ClothNode into its root bone's control node.
    private const float ClothNodeMergeRadius = 1e-3f;

    // Radians of rest rotation under which a free ClothNode counts as unrotated.
    private const float ClothNodeRotationTolerance = 1e-4f;
}

using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // A chain joint's stretchiness at or above this cancels its stray radius
    private const float ChainStrayStretchinessLimit = 0.99999988f;

    // Bone-local distance under which the compiler merges a free ClothNode into its root bone's control node
    private const float ClothNodeMergeRadius = 1e-3f;

    // Multiple of ClothNodeMergeRadius a node too close to its root bone is pushed out to
    private const float ClothNodeMergeClearance = 1.25f;

    // Radians of rest rotation under which a free ClothNode counts as unrotated
    private const float ClothNodeRotationTolerance = 1e-4f;

    private (HashSet<string> ControlNames, Dictionary<string, Bone>? BoneByName)? clothControlLookups;

    /// <summary>
    /// The name the document references a control node by: the exported <c>$cloth_m{N}p{L}</c> name for a proxy vertex,
    /// the element name for a free <c>ClothNode</c>, and the control name for anything else.
    /// </summary>
    private static string? AuthoredNodeName(ClothReconstruction cloth, int node, IReadOnlyDictionary<int, string>? proxyNodeNames)
    {
        if (node < 0 || node >= cloth.Fe.CtrlNames.Length)
        {
            return null;
        }

        return cloth.Fe.CtrlNames[node].StartsWith(ClothReconstruction.ProxyNamePrefix, StringComparison.Ordinal)
            ? proxyNodeNames?.GetValueOrDefault(node)
            : ClothFaceCornerName(cloth, node);
    }

    /// <summary>
    /// Declares every control node no other construct covers as a <c>ClothNode</c> or a one-joint <c>ClothChain</c>, then
    /// the rods between declared nodes (and from them to <paramref name="chainJoints"/>) as springs. Returns the number of
    /// nodes declared.
    /// </summary>
    internal static int AddFreeClothNodesAndSprings(KVObject clothChildren, KVObject softbodyChildren,
        ClothReconstruction cloth, HashSet<int> coveredNodes, Func<string, bool> emitBareStatic,
        HashSet<string> clothBones, Func<int, bool, KVObject>? folderFor = null,
        Func<string, bool>? bareStaticReparented = null, HashSet<(int, int)>? alreadyEmitted = null,
        HashSet<int>? chainJoints = null)
    {
        var names = cloth.Fe.CtrlNames;

        var anchorOf = BuildCtrlAnchorMap(cloth);

        var nodeByName = LookupsOf(cloth).NodeByName;
        var shapeParentBones = cloth.CollisionShapes.ParentBones;
        var rodTouched = LookupsOf(cloth).RodNeighbourCounts;

        var springName = new Dictionary<int, string>();
        var declared = new HashSet<int>();
        var emitted = 0;

        KVObject FolderOf(int node)
            => folderFor is not null ? folderFor(node, !rodTouched.ContainsKey(node)) : clothChildren;

        for (var node = 0; node < names.Length; node++)
        {
            var name = names[node];
            if (coveredNodes.Contains(node) || IsDeclaredByItsJiggleBone(cloth, node) || shapeParentBones.Contains(name))
            {
                continue;
            }

            if (cloth.IsFreeClothNode(node))
            {
                var elementName = ClothFaceCornerName(cloth, node);
                if (!TryResolveClothNodeAnchor(cloth, anchorOf, node, out var anchor))
                {
                    continue;
                }

                var rootBone = anchor.RootBone;
                FolderOf(node).Add(MakeClothNode(cloth, rootBone, node,
                    isStaticNode: cloth.Fe.IsStatic(node), elementName: elementName, origin: anchor.Origin, angles: anchor.Angles));
                springName[node] = elementName;
                declared.Add(node);
                clothBones.Add(rootBone);
                emitted++;

                if (nodeByName.TryGetValue(rootBone, out var rootNode))
                {
                    springName.TryAdd(rootNode, rootBone);
                }
            }
            else if (!cloth.IsGeneratedNodeName(name))
            {
                var isStatic = cloth.Fe.IsStatic(node);
                var bareStatic = isStatic && !rodTouched.ContainsKey(node);
                if (!bareStatic || emitBareStatic(name))
                {
                    var reparented = bareStaticReparented?.Invoke(name);
                    var loneNode = LoneNodeIsJointChain(cloth, node, bareStatic, reparented ?? false)
                        && !(StrayRecordOnlyAClothNodeStates(cloth, node) && reparented == false);
                    if (loneNode)
                    {
                        clothChildren.Add(MakeLoneJointChain(cloth, name, node));
                    }
                    else
                    {
                        FolderOf(node).Add(MakeClothNode(cloth, name, node, isStaticNode: isStatic));
                    }

                    springName[node] = name;
                    declared.Add(node);
                    clothBones.Add(name);
                    emitted++;
                }
            }
        }

        if (springName.Count > 0)
        {
            AddFreeClothSprings(softbodyChildren, cloth, springName, declared, alreadyEmitted, chainJoints);
        }

        return emitted;
    }

    /// <summary>
    /// Declares the rods between the nodes <paramref name="springName"/> names, and from a declared node to one of
    /// <paramref name="chainJoints"/>, skipping the pairs in <paramref name="alreadyEmitted"/>.
    /// </summary>
    private static void AddFreeClothSprings(KVObject softbodyChildren, ClothReconstruction cloth, Dictionary<int, string> springName,
        HashSet<int> declared, HashSet<(int, int)>? alreadyEmitted, HashSet<int>? chainJoints)
    {
        var names = cloth.Fe.CtrlNames;

        bool IsEndpoint(int node, int other) => springName.ContainsKey(node)
            || (chainJoints is not null && chainJoints.Contains(node) && declared.Contains(other));

        foreach (var (edge, rods) in cloth.RodsByPair)
        {
            if (!IsEndpoint(edge.Item1, edge.Item2) || !IsEndpoint(edge.Item2, edge.Item1)
                || (alreadyEmitted is not null && alreadyEmitted.Contains(edge)))
            {
                continue;
            }

            var name0 = springName.GetValueOrDefault(edge.Item1) ?? names[edge.Item1];
            var name1 = springName.GetValueOrDefault(edge.Item2) ?? names[edge.Item2];

            if ((!declared.Contains(edge.Item1) || !declared.Contains(edge.Item2))
                && cloth.HasDirectedSourceSpring(edge.Item2, edge.Item1)
                && !cloth.HasDirectedSourceSpring(edge.Item1, edge.Item2))
            {
                (name0, name1) = (name1, name0);
            }

            if (IsUnrecordedJointTie(cloth, edge, rods, springName))
            {
                var first = rods[0];
                var memberStiffness = MathF.Sqrt(first.RelaxationFactor);
                softbodyChildren.Add(MakeClothSelfCollisionCluster($"cluster_{edge.Item1}_{edge.Item2}", [name0, name1],
                    first.MaxDist / 2f, first.MaxDist / 2f, [memberStiffness, memberStiffness]));
                continue;
            }

            AddPairSprings(softbodyChildren, $"rod_{edge.Item1}_{edge.Item2}", name0, name1, rods);
        }
    }

    /// <summary>
    /// Whether the single rigid rod on <paramref name="edge"/> between a declared node and a chain joint has no two-corner
    /// source element in the original, so it is declared as a two-member <c>ClothSelfCollisionCluster</c>.
    /// </summary>
    private static bool IsUnrecordedJointTie(ClothReconstruction cloth, (int A, int B) edge, List<FeModel.Rod> rods,
        Dictionary<int, string> springName)
    {
        if (springName.ContainsKey(edge.A) && springName.ContainsKey(edge.B))
        {
            return false;
        }

        return rods.Count == 1 && !cloth.IsSourceSpring(edge.A, edge.B) && !rods[0].IsBanded;
    }

    /// <summary>Builds the test for whether a bone has an ancestor that is a cloth control node.</summary>
    private Func<string, bool> ClothControlAncestorTest(ClothReconstruction cloth)
    {
        var (controlNames, boneByName) = ClothControlLookups(cloth);
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
    private Func<string, bool> ClothControlParentTest(ClothReconstruction cloth)
    {
        var (controlNames, boneByName) = ClothControlLookups(cloth);
        return name => boneByName is not null && boneByName.TryGetValue(name, out var bone)
            && bone.Parent is not null && controlNames.Contains(bone.Parent.Name);
    }

    private (HashSet<string> ControlNames, Dictionary<string, Bone>? BoneByName) ClothControlLookups(ClothReconstruction cloth)
        => clothControlLookups ??= BuildClothControlLookups(cloth);

    private (HashSet<string> ControlNames, Dictionary<string, Bone>? BoneByName) BuildClothControlLookups(ClothReconstruction cloth)
    {
        Dictionary<string, Bone>? boneByName = null;
        if (model is not null)
        {
            boneByName = new Dictionary<string, Bone>(model.Skeleton.Bones.Length, StringComparer.Ordinal);
            foreach (var bone in model.Skeleton.Bones)
            {
                boneByName.TryAdd(bone.Name, bone);
            }
        }

        return (new HashSet<string>(cloth.Fe.CtrlNames, StringComparer.Ordinal), boneByName);
    }

    /// <summary>
    /// Whether a node's stray radius can only be stated by a <c>ClothNode</c>, because its stretchiness would cancel the
    /// radius on a chain joint.
    /// </summary>
    private static bool StrayRecordOnlyAClothNodeStates(ClothReconstruction cloth, int node)
        => cloth.Fe.GetStrayRadius(node) > 0f && cloth.GetStrayStretchiness(node) >= ChainStrayStretchinessLimit;

    private static bool LoneClothNodeIsOriginalRoot(ClothReconstruction cloth, int node)
        => cloth.HasCompiledSkelParents
            && node < cloth.SkelParents.Length && cloth.SkelParents[node] < 0;

    /// <summary>
    /// Whether a lone real cloth node is re-declared as a one-joint <c>ClothChain</c> rather than a <c>ClothNode</c>: an
    /// <c>m_SkelParents</c> root that is dynamic, a bare static node a <c>ClothNode</c> would re-parent, or locked to its
    /// goal.
    /// </summary>
    internal static bool LoneNodeIsJointChain(ClothReconstruction cloth, int node, bool bareStatic, bool bareStaticReparented)
        => LoneClothNodeIsOriginalRoot(cloth, node)
            && (!cloth.Fe.IsStatic(node) || (bareStatic && bareStaticReparented) || cloth.Fe.IsLockedToGoal(node));

    private static KVObject MakeLoneJointChain(ClothReconstruction cloth, string name, int node)
    {
        var chain = new BoneChain { RootBone = name };
        chain.Joints.Add(new BoneChainJoint
        {
            Node = node,
            Name = name,
            ParentNode = -1,
            InvMass = node < cloth.Fe.NodeInvMasses.Length ? cloth.Fe.NodeInvMasses[node] : 0f,
        });
        return MakeClothChainNode(cloth, chain);
    }

    /// <summary>
    /// The <c>transform_alignment</c> of a <c>ClothNode</c> on an <c>m_Ropes</c> run: 2 for an element node or a based
    /// node, 1 otherwise, and 0 off the ropes.
    /// </summary>
    private static int RopeClothNodeAlignment(ClothReconstruction cloth, int node, bool isElement, bool hasBasis)
    {
        if (!cloth.IsRopeNode(node))
        {
            return 0;
        }

        return isElement || hasBasis ? 2 : 1;
    }

    /// <summary>The number of distinct nodes <paramref name="node"/> shares an <c>m_Rods</c> record with.</summary>
    internal static int RodNeighbourCount(ClothReconstruction cloth, int node)
        => LookupsOf(cloth).RodNeighbourCounts.GetValueOrDefault(node);

    /// <summary>
    /// A <c>ClothNode</c> on <paramref name="boneName"/> declaring control node <paramref name="node"/> with the values it
    /// compiled with, under <paramref name="elementName"/> where it is a free cloth node.
    /// </summary>
    internal static KVObject MakeClothNode(ClothReconstruction cloth, string boneName, int node, bool isStaticNode = false,
        string? elementName = null, Vector3 origin = default, Vector3 angles = default,
        IReadOnlyDictionary<int, string>? proxyNodeNames = null)
    {
        var paint = NodePaint.Of(cloth, node);
        var strayRadius = cloth.Fe.GetStrayRadius(node);

        var hasBasis = cloth.Fe.NodeBases.TryGetValue(node, out var basis);
        string BasisName(int basisNode)
            => hasBasis ? AuthoredNodeName(cloth, basisNode, proxyNodeNames) ?? string.Empty : string.Empty;

        var preset = elementName is not null || (isStaticNode && !cloth.Fe.AllowsRotation(node)) || RodNeighbourCount(cloth, node) < 2
            ? cloth.ClothNodeBasisPreset(node)
            : null;
        var references = preset?.References ?? basis;

        var collisionMask = cloth.Fe.GetNodeCollisionMask(node);

        return BuildClothNode(new ClothNodeFields
        {
            Name = elementName ?? boneName,
            Origin = origin,
            Angles = angles,
            RootBone = boneName,
            HasStrayRadius = strayRadius > 0f,
            HasWorldCollision = cloth.Fe.IsWorldCollisionNode(node),
            CollisionMask = collisionMask,
            TransformAlignment = preset?.TransformAlignment ?? RopeClothNodeAlignment(cloth, node, elementName is not null, hasBasis),
            NodeBaseY1 = BasisName(references.NodeY1),
            NodeBaseX1 = BasisName(references.NodeX1),
            NodeBaseY0 = BasisName(references.NodeY0),
            NodeBaseX0 = BasisName(references.NodeX0),
            LockTranslation = cloth.LocksTranslation(node),
            GravityZ = paint.GravityZ,
            GoalStrength = paint.GoalStrength,
            GoalDamping = paint.GoalDamping,
            Mass = cloth.RecoverMassMultiplier(node) ?? 1.0f,
            Friction = cloth.Fe.GetNodeFriction(node),
            StrayRadius = strayRadius,
            StrayRadiusRelaxationFactor = cloth.GetStrayRelaxationFactor(node),
            CollisionRadius = cloth.Fe.GetCollisionRadius(node),
            IsStaticNode = isStaticNode,
            AllowRotation = cloth.Fe.AllowsRotation(node),
            SuperDamping = paint.Drag,
        });
    }

    /// <summary>
    /// The paint a control node's compiled integrator states: its goal strength and damping, its gravity scale and its drag.
    /// </summary>
    private readonly record struct NodePaint(float GoalStrength, float GoalDamping, float GravityZ, float Drag)
    {
        public static NodePaint Of(ClothReconstruction cloth, int node)
        {
            var integrator = cloth.Fe.GetIntegrator(node);
            return new NodePaint(cloth.GoalStrengthPaint(integrator.ForceAttraction),
                cloth.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction),
                integrator.Gravity / ClothReconstruction.ClothSourceBaseGravity,
                MathUtils.Saturate(integrator.PointDamping / ClothReconstruction.ClothDragPointDampingScale));
        }
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
    private static int ClothNodeLayerMask(int mask) => mask is >= 0 and <= 14 ? mask : ClothAllCollisionLayers;

    /// <summary>
    /// The name a <c>ClothTri</c> or <c>ClothQuad</c> corner references a control node by: the element name for a free
    /// <c>ClothNode</c> and the control name otherwise.
    /// </summary>
    private static string ClothFaceCornerName(ClothReconstruction cloth, int node)
    {
        var name = cloth.Fe.CtrlNames[node];
        return name.StartsWith(ClothReconstruction.FreeClothNodePrefix, StringComparison.Ordinal)
            ? name[ClothReconstruction.FreeClothNodePrefix.Length..]
            : name;
    }

    /// <summary>
    /// Declares a <c>ClothStiffHinge</c> for every compiled bend whose three nodes are free cloth nodes, recovering
    /// <c>max_angle</c> from <c>9 * height^2 = |b1|^2 + |b2|^2 - 2 |b1| |b2| cos(max_angle)</c>.
    /// </summary>
    internal static void AddClothStiffHinges(KVObject softbodyChildren, ClothReconstruction cloth)
    {
        bool IsFreeNode(int node) => cloth.IsFreeClothNode(node) && node < cloth.Fe.InitPosePositions.Length;

        foreach (var bend in cloth.Fe.KelagerBends)
        {
            if (!IsFreeNode(bend.MidNode) || !IsFreeNode(bend.End0) || !IsFreeNode(bend.End1))
            {
                continue;
            }

            var hinge = cloth.Fe.InitPosePositions[bend.MidNode];
            var base1 = (cloth.Fe.InitPosePositions[bend.End0] - hinge).Length();
            var base2 = (cloth.Fe.InitPosePositions[bend.End1] - hinge).Length();
            if (base1 <= 0f || base2 <= 0f)
            {
                continue;
            }

            var cosine = ((base1 * base1) + (base2 * base2) - (9f * bend.Height * bend.Height)) / (2f * base1 * base2);
            softbodyChildren.Add(MakeNode("ClothStiffHinge",
                ("cloth_node_0", ClothFaceCornerName(cloth, bend.MidNode)),
                ("cloth_node_1", ClothFaceCornerName(cloth, bend.End0)),
                ("cloth_node_2", ClothFaceCornerName(cloth, bend.End1)),
                ("max_angle", float.RadiansToDegrees(MathUtils.SafeAcos(cosine)))));
        }
    }

    /// <summary>
    /// The <c>ClothTri</c> or <c>ClothQuad</c> element over one compiled face, with repeated corners collapsed, or null
    /// when fewer than three remain.
    /// </summary>
    private static KVObject? MakeClothFace(ClothReconstruction cloth, int[] face)
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
                ClothFaceCornerName(cloth, corners[i]));
        }

        return node;
    }

    /// <summary>Declares every face the original built from <c>ClothTri</c> and <c>ClothQuad</c> elements.</summary>
    private static void AddClothFaces(KVObject clothChildren, ClothReconstruction cloth)
    {
        foreach (var face in cloth.GetAuthoredElementFaces())
        {
            if (MakeClothFace(cloth, face) is { } element)
            {
                clothChildren.Add(element);
            }
        }
    }

    private static Dictionary<int, FeModel.CtrlOffset> BuildCtrlAnchorMap(ClothReconstruction cloth)
    {
        var anchorOf = new Dictionary<int, FeModel.CtrlOffset>();
        foreach (var offset in cloth.Fe.CtrlOffsets)
        {
            anchorOf[offset.CtrlChild] = offset;
        }

        return anchorOf;
    }

    /// <summary>
    /// Resolves where a free <c>$cloth_node_</c> control node is re-authored, from its <c>m_CtrlOffsets</c> entry or else
    /// its skeleton parent. False where it has no root or the root is a generated node.
    /// </summary>
    internal static bool TryResolveClothNodeAnchor(ClothReconstruction cloth, Dictionary<int, FeModel.CtrlOffset> anchorOf,
        int node, out ClothNodeAnchor resolved)
    {
        var names = cloth.Fe.CtrlNames;
        string? rootBone = null;
        var origin = Vector3.Zero;
        var angles = Vector3.Zero;
        var parent = -1;

        if (anchorOf.TryGetValue(node, out var anchor)
            && anchor.CtrlParent >= 0 && anchor.CtrlParent < names.Length)
        {
            parent = anchor.CtrlParent;
            rootBone = names[parent];
            origin = anchor.Offset;
        }
        else if (node < cloth.SkelParents.Length
            && cloth.SkelParents[node] >= 0 && cloth.SkelParents[node] < names.Length)
        {
            parent = cloth.SkelParents[node];
            rootBone = names[parent];
            if (node < cloth.Fe.InitPosePositions.Length && parent < cloth.Fe.InitPosePositions.Length)
            {
                origin = ClothBoneLocalPose(cloth, node, parent).Origin;
            }
        }

        if (parent >= 0 && node < cloth.Fe.InitPoseRotations.Length && parent < cloth.Fe.InitPoseRotations.Length)
        {
            var local = Quaternion.Conjugate(cloth.Fe.InitPoseRotations[parent]) * cloth.Fe.InitPoseRotations[node];
            if (2f * MathF.Atan2(new Vector3(local.X, local.Y, local.Z).Length(), MathF.Abs(local.W)) > ClothNodeRotationTolerance)
            {
                angles = EntityTransformHelper.ToEulerAngles(local);
            }
        }

        if (rootBone is not null && angles == Vector3.Zero && origin.Length() < ClothNodeMergeRadius)
        {
            var direction = origin == Vector3.Zero ? Vector3.One : origin;
            origin = Vector3.Normalize(direction) * (ClothNodeMergeRadius * ClothNodeMergeClearance);
        }

        resolved = new ClothNodeAnchor(rootBone ?? string.Empty, origin, angles);
        return rootBone is not null && !FeModel.IsProxyNodeName(rootBone);
    }
}

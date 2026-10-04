using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    private const float ClothExtrudeTwistBase = 90f;

    private const float ClothExtrudeTwistAttrDefault = 0f;

    // Every twist_relax above zero compiles a static root's twist entries alike, so the top of the range stands for it.
    private const float ClothStaticRootTwistRelax = 1f;

    /// <summary>The <c>extrude_twist</c> a joint row states for a ring rolled <paramref name="measuredTwist"/> degrees.</summary>
    internal static float ClothExtrudeTwistKey(float measuredTwist) => ClothExtrudeTwistBase - measuredTwist;

    /// <summary>
    /// Declares an algorithm-0 <c>ClothRigidCloudCluster</c> behind every chain lock that
    /// <see cref="ClothReconstruction.IsRigidCloudClusterLock"/> attributes to one.
    /// </summary>
    private static void AddClothRigidCloudClusterLocks(KVObject softbodyChildren, ClothReconstruction cloth,
        IEnumerable<BoneChain> chains)
    {
        foreach (var chain in chains.Where(chain => ClothReconstruction.IsRigidCloudClusterLock(cloth, chain)))
        {
            foreach (var (joint, _) in ClothReconstruction.LockedJointsWithChildren(cloth, chain))
            {
                softbodyChildren.Add(MakeClothRigidCloudCluster(joint.Name, RigidCloudClusterMembers(chain, joint)));
            }
        }
    }

    /// <summary>
    /// The members of the <c>ClothRigidCloudCluster</c> locking <paramref name="joint"/>: its chain descendants a generation
    /// at a time until there are two, plus the joint itself where its subtree has fewer.
    /// </summary>
    internal static List<string> RigidCloudClusterMembers(BoneChain chain, BoneChainJoint joint)
    {
        List<string> members = [];
        List<int> generation = [joint.Node];
        while (members.Count < 2 && generation.Count > 0)
        {
            List<BoneChainJoint> next = [.. chain.Joints.Where(child => generation.Contains(child.ParentNode))];
            members.AddRange(next.Select(static child => child.Name));
            generation = [.. next.Select(static child => child.Node)];
        }

        if (members.Count < 2)
        {
            members.Insert(0, joint.Name);
        }

        return members;
    }

    /// <summary>An algorithm-0 <c>ClothRigidCloudCluster</c> locking <paramref name="parentNode"/> at the default stiffness.</summary>
    internal static KVObject MakeClothRigidCloudCluster(string parentNode, IEnumerable<string> members)
    {
        var joints = KVObject.Array();
        foreach (var member in members)
        {
            var joint = KVObject.Collection();
            joint.Add("joint_name", member);
            joint.Add("stiffness", 1f);
            joints.Add(joint);
        }

        var attrs = KVObject.Collection();
        AddColumn(attrs, "joint_name", "Joint Name", true, 0).Add("default", string.Empty);
        AddColumn(attrs, "stiffness", "Stiffness", true, 1).Add("default", 1f);

        return MakeNode("ClothRigidCloudCluster",
            ("name", parentNode + "_rigid_cloud"),
            ("algorithm", 0),
            ("parent_node", parentNode),
            ("chain", MakeChainData(joints, attrs, version: 0)));
    }

    /// <summary>
    /// Where a chain joint's basis references a <c>$cloth_node_</c>, declares every dynamic chain joint as a static
    /// <c>ClothNode</c> in node order, the based ones carrying their alignment-3 preset.
    /// </summary>
    internal static IEnumerable<KVObject> ChainJointClothNodes(ClothReconstruction cloth, IReadOnlyList<BoneChain> chains)
    {
        var joints = ChainJointNodes(chains);
        var names = cloth.Fe.CtrlName;

        var presets = new Dictionary<int, FeModelIndex.NodeBasis>();
        foreach (var node in joints)
        {
            if (cloth.ClothNodeBasisPreset(node) is (3, var references)
                && (cloth.IsFreeClothNode(references.NodeX1) || cloth.IsFreeClothNode(references.NodeY1)))
            {
                presets[node] = references;
            }
        }

        if (presets.Count == 0)
        {
            yield break;
        }

        foreach (var node in joints.Where(node => !cloth.Index.IsStatic(node)).Order())
        {
            if (!presets.TryGetValue(node, out var references))
            {
                yield return MakeStaticClothNode(names[node], names[node]);
                continue;
            }

            yield return MakeNode("ClothNode",
                ("name", names[node]),
                ("cloth_node_root_bone", names[node]),
                ("transform_alignment", 3),
                ("node_base_x1", AuthoredNodeName(cloth, references.NodeX1, null) ?? string.Empty),
                ("node_base_y1", AuthoredNodeName(cloth, references.NodeY1, null) ?? string.Empty),
                ("is_static_node", true));
        }
    }

    /// <summary>
    /// Declares <paramref name="chain"/>, walked in <paramref name="walk"/> order where given, then its restatement and
    /// second declarations.
    /// </summary>
    private static void AddClothChainDeclarations(KVObject children, ClothReconstruction cloth, BoneChain chain,
        IReadOnlyList<BoneChainJoint>? walk = null)
    {
        children.Add(MakeClothChainNode(cloth, chain, walk));
        if (MakeClothChainRestatement(cloth, chain) is { } restated)
        {
            children.Add(restated);
        }

        foreach (var second in MakeClothChainSecondDeclarations(cloth, chain))
        {
            children.Add(second);
        }
    }

    private static KVObject MakeClothChainNode(ClothReconstruction cloth, BoneChain chain,
        IReadOnlyList<BoneChainJoint>? walk = null)
    {
        var softHinge = cloth.HasChainRods(chain) && !cloth.HasRigidHingeLink(chain);
        var version = cloth.ChainVersionOf(chain);
        var chainMass = cloth.RecoverChainMassDefault(chain);

        var joints = KVObject.Array();
        foreach (var joint in walk ?? chain.Joints)
        {
            var jointNode = MakeClothJoint(cloth, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge, version,
                chain: chain, chainMass: chainMass);

            var childSibling = joint.ChildSiblingSpring > 0f
                ? joint.ChildSiblingSpring
                : cloth.SpringsHingeChildren(chain, joint.Node) ? 1.0f : 0f;
            if (childSibling > 0f)
            {
                jointNode.Add("child_sibling_spring", childSibling);
            }

            joints.Add(jointNode);
        }

        var chainNode = MakeNode("ClothChain",
            ("name", chain.RootBone + chain.DeclarationSuffix),
            ("root_bone", chain.RootBone),
            ("chain", MakeChainData(joints, MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius, chainMass), version)));

        var hinges = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            if (cloth.RigidHingeJoints.TryGetValue(joint.Node, out var hingeVector))
            {
                hinges.Add(MakeNode("ClothChainHinge",
                    ("constrained_bone", joint.Name),
                    ("hinge_vector", ToKVArray(hingeVector)),
                    ("soft_hinge_link", false),
                    ("limits_enabled", false)));
            }
        }

        if (hinges.Count > 0)
        {
            chainNode.Add("children", hinges);
        }

        return chainNode;
    }

    /// <summary>
    /// The plain second declaration of the joints marked <see cref="BoneChainJoint.Restated"/>, read off the joint nodes,
    /// or null when there are none.
    /// </summary>
    private static KVObject? MakeClothChainRestatement(ClothReconstruction cloth, BoneChain chain)
    {
        var restated = chain.Joints.FindAll(static joint => joint.Restated);
        if (restated.Count == 0)
        {
            return null;
        }

        var members = restated.Select(static joint => joint.Node).ToHashSet();
        var joints = KVObject.Array();
        string? rootBone = null;
        foreach (var joint in restated)
        {
            var kv = KVObject.Collection();
            kv.Add("joint_name", joint.Name);

            var parented = members.Contains(joint.ParentNode);
            if (parented && joint.ParentName is { } parentName)
            {
                kv.Add("joint_parent", parentName);
            }
            else
            {
                rootBone ??= joint.Name;
            }

            kv.Add("simulate", joint.Simulated);

            var paint = NodePaint.Of(cloth, joint.Node);
            kv.Add("goal_strength", paint.GoalStrength);
            kv.Add("goal_damping", paint.GoalDamping);
            kv.Add("gravity_z", paint.GravityZ);

            if (joint.Simulated)
            {
                kv.Add("collision_radius", cloth.Index.GetCollisionRadius(joint.Node));
            }

            if (parented
                && LookupsOf(cloth).FirstRodByPair.TryGetValue(ClothReconstruction.UnorderedPair(joint.Node, joint.ParentNode), out var rod)
                && rod.RelaxationFactor != 1f)
            {
                kv.Add("stretch_spring", rod.RelaxationFactor);
            }

            joints.Add(kv);
        }

        rootBone ??= restated[0].Name;
        return MakeNode("ClothChain",
            ("name", rootBone + "_restated"),
            ("root_bone", rootBone),
            ("chain", MakeChainData(joints, MakeClothChainAttrs())));
    }

    /// <summary>
    /// The second declaration of every sub-chain with a <see cref="BoneChainJoint.SecondDeclarationRoot"/>. It carries the
    /// members' whole attribute set except <c>stiff_hinge</c> and <c>child_sibling_spring</c>, which each declaration writes
    /// again.
    /// </summary>
    private static List<KVObject> MakeClothChainSecondDeclarations(ClothReconstruction cloth, BoneChain chain)
    {
        var runs = new List<string>();
        foreach (var joint in chain.Joints)
        {
            if (joint.SecondDeclarationRoot is { } root && !runs.Contains(root))
            {
                runs.Add(root);
            }
        }

        if (runs.Count == 0)
        {
            return [];
        }

        var version = cloth.ChainVersionOf(chain);
        var declarations = new List<KVObject>(runs.Count);
        foreach (var rootBone in runs)
        {
            var joints = KVObject.Array();
            foreach (var joint in chain.Joints)
            {
                if (string.Equals(joint.SecondDeclarationRoot, rootBone, StringComparison.OrdinalIgnoreCase))
                {
                    joints.Add(MakeClothJoint(cloth, joint, chainExtrudes: false, softHinge: false, version,
                        chain, secondDeclaration: true));
                }
            }

            declarations.Add(MakeNode("ClothChain",
                ("name", rootBone + "_second"),
                ("root_bone", rootBone),
                ("chain", MakeChainData(joints, MakeClothChainAttrs(), version))));
        }

        return declarations;
    }

    /// <summary>
    /// The <c>ClothChain</c> joint row declaring <paramref name="joint"/>: its parent, simulation, twist, goal, extrude,
    /// span, collision and selection keys.
    /// </summary>
    internal static KVObject MakeClothJoint(ClothReconstruction cloth, BoneChainJoint joint, bool chainExtrudes = false,
        bool softHinge = false, int chainVersion = 2, BoneChain? chain = null,
        bool secondDeclaration = false, float chainMass = 1f)
    {
        var kv = KVObject.Collection();
        kv.Add("joint_name", joint.Name);

        var secondRoot = joint.SecondDeclarationRoot;
        if (joint.ParentName is not null
            && !(secondDeclaration && string.Equals(joint.Name, secondRoot, StringComparison.OrdinalIgnoreCase)))
        {
            kv.Add("joint_parent", joint.ParentName);
        }

        var valueNode = joint.ValueNode >= 0 ? joint.ValueNode
            : joint.Restated && joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        var paint = NodePaint.Of(cloth, valueNode);

        var twistRelax = cloth.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode);

        var pinnedSimulatedRoot = (joint.IsRoot && !joint.Simulated && twistRelax > 0f) || joint.SpringsWithSiblings;

        var firstOfTwo = secondRoot is not null && !secondDeclaration
            && !string.Equals(joint.Name, secondRoot, StringComparison.OrdinalIgnoreCase);
        kv.Add("simulate", !firstOfTwo && (joint.Simulated || pinnedSimulatedRoot));

        if (secondRoot is not null)
        {
            twistRelax = cloth.TwistRelaxDeclaredAt(joint.Node, joint.ParentNode,
                secondDeclaration ? 1 : 0) ?? 0f;
        }

        if (twistRelax == 0f && !joint.Simulated && !secondDeclaration
            && (cloth.HasRelaxlessTwistLink(joint.Node) || cloth.OrientsRelaxlessTwist(joint.Node)))
        {
            twistRelax = ClothStaticRootTwistRelax;
        }

        if (joint.Node < cloth.Fe.StaticNodes)
        {
            kv.Add("allow_rotation", cloth.Index.AllowsRotation(joint.Node));
        }

        if (cloth.LocksTranslation(joint.Node, chainVersion, chain) || pinnedSimulatedRoot)
        {
            kv.Add("lock_translation", true);
        }

        kv.Add("goal_strength", paint.GoalStrength);
        kv.Add("goal_damping", paint.GoalDamping);

        if (paint.Drag != 0f)
        {
            kv.Add("drag", paint.Drag);
        }

        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", NodePaint.Of(cloth, gravityNode).GravityZ);

        kv.Add("twist_relax", twistRelax);

        kv.Add("world_collision", cloth.Index.IsWorldCollisionNode(joint.Node));

        var collisionMask = cloth.Index.GetNodeCollisionMask(joint.Node);
        if (collisionMask is >= 0 and < ClothAllCollisionLayers)
        {
            AddCollisionLayerFlags(kv, "collision_layer_", collisionMask);
        }

        var (worldFriction, groundFriction) = cloth.Index.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", cloth.Index.GetCollisionRadius(valueNode));

        var strayNode = joint.ValueNode >= 0
            ? joint.ValueNode
            : cloth.StrayRadiusNode(joint.Node, joint.Name);
        kv.Add("stray_radius", cloth.Index.GetStrayRadius(strayNode));
        kv.Add("stray_radius_stretchiness", cloth.GetStrayStretchiness(strayNode));
        kv.Add("friction", cloth.Index.GetNodeFriction(joint.Node));

        if (cloth.RecoverJointMass(joint.Node, chainMass) is { } massMultiplier)
        {
            kv.Add("mass", massMultiplier);
        }

        if ((cloth.GetVertexMapNames(joint.Node)
            ?? (joint.ProxyNode >= 0 ? cloth.GetVertexMapNames(joint.ProxyNode) : null))
            is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        var hinge = cloth.GetChainHinge(joint.Name, joint.Node);
        AddJointExtrude(kv, cloth, joint, chainExtrudes, hinge);
        AddJointSpans(kv, cloth, joint, secondDeclaration);

        if (hinge is { } chainHinge)
        {
            kv.Add("hinge_constraint_vector_worldspace", ToKVArray(chainHinge.Vector));
            kv.Add("hinge_constraint_soft", softHinge);
            kv.Add("hinge_constraint_limit_cw", chainHinge.LimitCw);
            kv.Add("hinge_constraint_limit_ccw", chainHinge.LimitCcw);
        }

        return kv;
    }

    /// <summary>Adds a joint row's ring keys and its end effector.</summary>
    private static void AddJointExtrude(KVObject kv, ClothReconstruction cloth, BoneChainJoint joint, bool chainExtrudes,
        ChainHinge? hinge)
    {
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistKey(joint.ExtrudeTwist) + joint.ExtrudeTwistTieNudge);

                if (joint.ForwardAxis != 'x')
                {
                    kv.Add("extrude_forward_axis", joint.ForwardAxis.ToString());
                }
            }
        }

        if (joint.EndEffector != 0f && (hinge is null || cloth.ProxyCountOf(joint.Node) > 2))
        {
            kv.Add("end_effector", joint.EndEffector);
        }
    }

    /// <summary>Adds a joint row's span stiffnesses, antishrink, stiff hinge and motion bias.</summary>
    private static void AddJointSpans(KVObject kv, ClothReconstruction cloth, BoneChainJoint joint, bool secondDeclaration)
    {
        if (joint.StretchStiffness != 1.0f)
        {
            kv.Add("stretch_spring", joint.StretchStiffness);
        }

        if (joint.AnimatedLength)
        {
            kv.Add("animated_length", true);
        }

        kv.Add("bend_spring", joint.BendStiffness);
        kv.Add("torsion_spring", joint.TorsionStiffness);
        kv.Add("extra_iterations", joint.ExtraIterations);
        kv.Add("suspender", joint.Suspender);

        if (joint.Antishrink != 1.0f)
        {
            kv.Add("antishrink", joint.Antishrink);
        }

        if (cloth.GetStiffHinge(joint.Node) is { } stiffHinge)
        {
            if (cloth.GetStiffHinge(joint.Node, secondDeclaration ? 1 : 0) is { } declared)
            {
                kv.Add("stiff_hinge", declared.Stiffness);
                kv.Add("stiff_hinge_angle", declared.Angle);
            }

            var stiffBias = stiffHinge.MotionBias != 0f ? stiffHinge.MotionBias : cloth.GetMotionBias(joint) ?? 0f;
            if (stiffBias != 0f)
            {
                kv.Add("motion_bias", stiffBias);
            }
        }
        else if (cloth.GetMotionBias(joint) is { } motionBias)
        {
            kv.Add("motion_bias", motionBias);
        }
    }

    /// <summary>The <c>attrs</c> table of a <c>ClothChain</c>: the column schema and the default of every joint key.</summary>
    internal static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f, float mass = 1f)
    {
        var attrs = KVObject.Collection();

        KVObject Column(string key, string display, bool show, int uiOrder, KVObject def, KVObject? min = null,
            KVObject? max = null)
        {
            var attr = AddColumn(attrs, key, display, show, uiOrder);
            attr.Add("default", def);
            if (min is not null)
            {
                attr.Add("min", min);
            }

            if (max is not null)
            {
                attr.Add("max", max);
            }

            return attr;
        }

        Column("joint_name", "Joint Name", true, 1, string.Empty).Add("lock", true);
        Column("joint_parent", "Parent Joint", false, 2, string.Empty);
        Column("simulate", "Simulate", true, 3, true);
        Column("allow_rotation", "Allow Rotation", false, 4, true);
        Column("stretch_spring", "Stretch Stiffness", false, 5, 1.0f, 0.0f, 1.0f);
        Column("child_sibling_spring", "Spring Between Children", false, 6, 0.0f, 0.0f, 1.0f);
        Column("bend_spring", "Bend Stiffness", false, 7, 1.0f, 0.0f, 1.0f);
        Column("torsion_spring", "Torsion Stiffness", false, 8, 0.0f, 0.0f, 1.0f);
        Column("explicit_length", "Explicit Length", false, 9, 0.0f, 0.0f);
        Column("world_collision", "World Ground Collision", true, 10, false);
        Column("animated_length", "Animated Length", false, 11, false);
        Column("goal_strength", "Goal Strength", true, 12, 0.0f, 0.0f, 1.0f);
        Column("goal_damping", "Goal Damping", true, 13, 0.0f, 0.0f, 1.0f);
        Column("drag", "Extra Drag", false, 14, 0.0f, 0.0f, 1.0f);
        Column("mass", "Mass", false, 15, mass, 0.0f);
        Column("gravity_z", "Gravity", true, 16, 1.0f);
        Column("collision_radius", "Collision Radius", true, 17, 0.0f, 0.0f);
        Column("lock_translation", "Lock Translation", false, 18, false);
        Column("suspender", "Suspender Spring", false, 19, 0.0f);
        Column("antishrink", "Antishrink Strength", false, 20, 1.0f, 0.0f, 1.0f);
        Column("stray_radius", "Stray Radius", true, 21, 0.0f, 0.0f);
        Column("stray_radius_stretchiness", "Stray Radius Stretchiness", false, 22, 0.0f, 0.0f);
        Column("friction", "Friction", false, 23, 0.0f, 0.0f, 1.0f);
        Column("vertex_map", "Vertex Map", false, 24, string.Empty).Add("verify", "vertex_map");
        Column("end_effector", "End Effector", false, 25, 0.0f).Add("lock_default_value", true);
        Column("stiff_hinge", "Stiff Hinge", true, 26, 0.0f, 0.0f, 1.0f).Add("lock_root2", true);
        Column("stiff_hinge_angle", "Stiff Hinge Angle", true, 27, 0.0f, 0.0f, 180.0f).Add("lock_root2", true);
        Column("motion_bias", "Motion Bias", true, 28, 0.0f, -1.0f, 1.0f).Add("lock_root", true);
        Column("extra_iterations", "Extra Iterations", true, 29, 0, 0, 1000);
        Column("twist_relax", "Twist Relax", true, 30, 0.0f, 0.0f, 1.0f);
        Column("extrude_sides", "Extrude Sides", false, 31, extrudeSides, 0, 4);
        Column("extrude_radius", "Extrude Radius", false, 32, extrudeSides >= 1 ? extrudeRadius : 5.0f, 0.0f);
        Column("extrude_twist", "Extrude Twist", false, 33, ClothExtrudeTwistAttrDefault);
        Column("extrude_forward_axis", "Extrude Forward Axis", false, 34, string.Empty).Add("verify", "extrude_forward_axis");
        Column("world_friction", "Ground Softness (\"world friction\" in Source1)", false, 35, 0.0f, 0.0f, 1.0f);
        Column("ground_friction", "Ground Friction", false, 36, 0.0f, 0.0f, 1.0f);
        Column("stray_box", "Stray Box", false, 37, string.Empty).Add("verify", "stray_box");
        Column("collision_layer_0", "Collision Layer 0", false, 38, true);
        Column("collision_layer_1", "Collision Layer 1", false, 39, true);
        Column("collision_layer_2", "Collision Layer 2", false, 40, true);
        Column("collision_layer_3", "Collision Layer 3", false, 41, true);

        return attrs;
    }
}

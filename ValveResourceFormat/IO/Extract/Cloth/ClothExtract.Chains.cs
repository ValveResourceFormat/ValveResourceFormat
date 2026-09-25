using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    private const float ClothExtrudeTwistBase = 90f;

    /// <summary>The <c>extrude_twist</c> a joint row states for a ring rolled <paramref name="measuredTwist"/> degrees.</summary>
    internal static float ClothExtrudeTwistKey(float measuredTwist) => ClothExtrudeTwistBase - measuredTwist;

    /// <summary>The <c>extrude_twist</c> default of a chain's <c>attrs</c> table, the key's schema default.</summary>
    private const float ClothExtrudeTwistAttrDefault = 0f;

    // Every twist_relax above zero compiles a static root's twist entries alike, so the top of the range stands for it.
    private const float ClothStaticRootTwistRelax = 1f;

    /// <summary>
    /// Declares an algorithm-0 <c>ClothRigidCloudCluster</c> behind every chain lock <see cref="IsRigidCloudClusterLock"/>
    /// attributes to one.
    /// </summary>
    internal static void AddClothRigidCloudClusterLocks(KVObject softbodyChildren, FeModel feModel,
        IEnumerable<FeModel.BoneChain> chains)
    {
        foreach (var chain in chains.Where(chain => IsRigidCloudClusterLock(feModel, chain)))
        {
            foreach (var (joint, _) in LockedJointsWithChildren(feModel, chain))
            {
                softbodyChildren.Add(MakeClothRigidCloudCluster(joint.Name, RigidCloudClusterMembers(chain, joint)));
            }
        }
    }

    /// <summary>
    /// The members of the <c>ClothRigidCloudCluster</c> locking <paramref name="joint"/>: its chain descendants a generation
    /// at a time until there are two, completed by the joint itself where its subtree has fewer.
    /// </summary>
    internal static List<string> RigidCloudClusterMembers(FeModel.BoneChain chain, FeModel.BoneChainJoint joint)
    {
        List<string> members = [];
        List<int> generation = [joint.Node];
        while (members.Count < 2 && generation.Count > 0)
        {
            List<FeModel.BoneChainJoint> next = [.. chain.Joints.Where(child => generation.Contains(child.ParentNode))];
            members.AddRange(next.Select(static child => child.Name));
            generation = [.. next.Select(static child => child.Node)];
        }

        if (members.Count < 2)
        {
            members.Insert(0, joint.Name);
        }

        return members;
    }

    /// <summary>
    /// An algorithm-0 <c>ClothRigidCloudCluster</c> locking <paramref name="parentNode"/>, with <paramref name="members"/> as
    /// its joints at the default stiffness.
    /// </summary>
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

        var jointName = KVObject.Collection();
        jointName.Add("display", "Joint Name");
        jointName.Add("show", true);
        jointName.Add("ui_order", 0);
        jointName.Add("default", string.Empty);
        var stiffness = KVObject.Collection();
        stiffness.Add("display", "Stiffness");
        stiffness.Add("show", true);
        stiffness.Add("ui_order", 1);
        stiffness.Add("default", 1f);
        var attrs = KVObject.Collection();
        attrs.Add("joint_name", jointName);
        attrs.Add("stiffness", stiffness);

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", attrs);
        chainData.Add("selection", KVObject.Array());
        chainData.Add("version", 0);

        return MakeNode("ClothRigidCloudCluster",
            ("name", parentNode + "_rigid_cloud"),
            ("algorithm", 0),
            ("parent_node", parentNode),
            ("chain", chainData));
    }

    /// <summary>
    /// Where a chain joint's basis names a <c>$cloth_node_</c> reference, declares every dynamic chain joint as a static
    /// <c>ClothNode</c> in node order, the based ones carrying their alignment-3 preset.
    /// </summary>
    internal static IEnumerable<KVObject> ChainJointClothNodes(FeModel feModel, IReadOnlyList<FeModel.BoneChain> chains)
    {
        var joints = chains.SelectMany(static chain => chain.Joints).Select(static joint => joint.Node).ToHashSet();
        var names = feModel.CtrlNames;
        bool NamesClothNode(int node) => node >= 0 && node < names.Length && names[node].StartsWith("$cloth_node_", StringComparison.Ordinal);

        var presets = new Dictionary<int, FeModel.NodeBasis>();
        foreach (var node in joints)
        {
            if (feModel.ClothNodeBasisPreset(node) is (3, var references)
                && (NamesClothNode(references.NodeX1) || NamesClothNode(references.NodeY1)))
            {
                presets[node] = references;
            }
        }

        if (presets.Count == 0)
        {
            yield break;
        }

        foreach (var node in joints.Where(node => !feModel.IsStatic(node)).Order())
        {
            if (!presets.TryGetValue(node, out var references))
            {
                yield return MakeNode("ClothNode", ("name", names[node]), ("cloth_node_root_bone", names[node]), ("is_static_node", true));
                continue;
            }

            yield return MakeNode("ClothNode",
                ("name", names[node]),
                ("cloth_node_root_bone", names[node]),
                ("transform_alignment", 3),
                ("node_base_x1", ResolveAntiTunnelNodeName(feModel, references.NodeX1, null) ?? string.Empty),
                ("node_base_y1", ResolveAntiTunnelNodeName(feModel, references.NodeY1, null) ?? string.Empty),
                ("is_static_node", true));
        }
    }

    private static KVObject MakeClothChainNode(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains,
        IReadOnlyList<FeModel.BoneChainJoint>? walk = null, HashSet<string>? relandedJoints = null)
    {
        // A hinged chain that still carries rods was authored with a soft hinge link.
        var softHinge = feModel.HasChainRods(chain) && !feModel.HasRigidHingeLink(chain);

        var version = ClothChainVersion(feModel, chain, hasOtherChains);

        var chainMass = feModel.RecoverChainMassDefault(chain);

        var joints = KVObject.Array();
        foreach (var joint in walk ?? chain.Joints)
        {
            var jointNode = MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge, version,
                rollTies: relandedJoints?.Contains(joint.Name) != true, chain: chain, chainMass: chainMass);

            var childSibling = joint.ChildSiblingSpring > 0f
                ? joint.ChildSiblingSpring
                : feModel.SpringsHingeChildren(chain, joint.Node) ? 1.0f : 0f;
            if (childSibling > 0f)
            {
                jointNode.Add("child_sibling_spring", childSibling);
            }

            joints.Add(jointNode);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius, chain.ExtrudeTwist,
            chainMass));
        chainData.Add("selection", KVObject.Array());

        chainData.Add("version", version);

        var chainNode = MakeNode("ClothChain",
            ("name", chain.RootBone + chain.DeclarationSuffix),
            ("root_bone", chain.RootBone),
            ("chain", chainData));

        var hinges = KVObject.Array();
        foreach (var joint in chain.Joints)
        {
            if (feModel.RigidHingeJoints.TryGetValue(joint.Node, out var hingeVector))
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
    /// The plain second declaration of the joints <c>BuildBoneChains</c> marked restated, read off the joint nodes, or null
    /// when there are none.
    /// </summary>
    private static KVObject? MakeClothChainRestatement(FeModel feModel, FeModel.BoneChain chain)
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

            var integrator = feModel.GetIntegrator(joint.Node);
            kv.Add("goal_strength", feModel.GoalStrengthPaint(integrator.ForceAttraction));
            kv.Add("goal_damping", feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction));
            kv.Add("gravity_z", integrator.Gravity / ClothSourceBaseGravity);

            if (joint.Simulated)
            {
                kv.Add("collision_radius", feModel.GetCollisionRadius(joint.Node));
            }

            if (parented)
            {
                foreach (var rod in feModel.Rods)
                {
                    if ((rod.NodeA == joint.Node && rod.NodeB == joint.ParentNode)
                        || (rod.NodeA == joint.ParentNode && rod.NodeB == joint.Node))
                    {
                        if (rod.RelaxationFactor != 1f)
                        {
                            kv.Add("stretch_spring", rod.RelaxationFactor);
                        }

                        break;
                    }
                }
            }

            joints.Add(kv);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs());
        chainData.Add("selection", KVObject.Array());

        rootBone ??= restated[0].Name;
        return MakeNode("ClothChain",
            ("name", rootBone + "_restated"),
            ("root_bone", rootBone),
            ("chain", chainData));
    }

    /// <summary>
    /// The second declaration of every sub-chain <c>BuildBoneChains</c> marked. It carries the members' whole attribute set
    /// except <c>stiff_hinge</c> and <c>child_sibling_spring</c>, which each declaration writes again.
    /// </summary>
    private static List<KVObject> MakeClothChainSecondDeclarations(FeModel feModel, FeModel.BoneChain chain, int version)
    {
        var runs = new List<string>();
        foreach (var joint in chain.Joints)
        {
            if (joint.SecondDeclarationRoot is { } root && !runs.Contains(root))
            {
                runs.Add(root);
            }
        }

        var declarations = new List<KVObject>(runs.Count);
        foreach (var rootBone in runs)
        {
            var joints = KVObject.Array();
            foreach (var joint in chain.Joints)
            {
                if (string.Equals(joint.SecondDeclarationRoot, rootBone, StringComparison.OrdinalIgnoreCase))
                {
                    joints.Add(MakeClothJoint(feModel, joint, chainExtrudes: false, softHinge: false, version,
                        rollTies: true, chain, secondDeclaration: true));
                }
            }

            var chainData = KVObject.Collection();
            chainData.Add("joints", joints);
            chainData.Add("attrs", MakeClothChainAttrs());
            chainData.Add("selection", KVObject.Array());
            chainData.Add("version", version);

            declarations.Add(MakeNode("ClothChain",
                ("name", rootBone + "_second"),
                ("root_bone", rootBone),
                ("chain", chainData)));
        }

        return declarations;
    }

    internal static KVObject MakeClothJoint(FeModel feModel, FeModel.BoneChainJoint joint, bool chainExtrudes = false,
        bool softHinge = false, int chainVersion = 2, bool rollTies = true, FeModel.BoneChain? chain = null,
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

        // A joint declared twice keeps the second declaration's values on its node and the first's on its ring;
        // ValueNode names the ring this declaration extruded where each extruded one.
        var valueNode = joint.ValueNode >= 0 ? joint.ValueNode
            : joint.Restated && joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        var integrator = feModel.GetIntegrator(valueNode);
        var goalStrength = feModel.GoalStrengthPaint(integrator.ForceAttraction);

        var twistRelax = feModel.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode);

        // A root with a non-zero twist entry of its own was authored simulated and pinned by lock_translation.
        var pinnedSimulatedRoot = (joint.IsRoot && !joint.Simulated && twistRelax > 0f) || joint.SpringsWithSiblings;

        // Of a joint two chains declare, only the second declaration simulates.
        var firstOfTwo = secondRoot is not null && !secondDeclaration
            && !string.Equals(joint.Name, secondRoot, StringComparison.OrdinalIgnoreCase);
        kv.Add("simulate", !firstOfTwo && (joint.Simulated || pinnedSimulatedRoot));

        // Each declaration of a doubled run states the twist of its own rank.
        if (secondRoot is not null)
        {
            twistRelax = feModel.TwistRelaxDeclaredAt(joint.Node, joint.ParentNode,
                secondDeclaration ? 1 : 0) ?? 0f;
        }

        // A static joint's twist_relax survives only as the twist link it made.
        if (twistRelax == 0f && !joint.Simulated && !secondDeclaration
            && (feModel.HasRelaxlessTwistLink(joint.Node) || feModel.OrientsRelaxlessTwist(joint.Node)))
        {
            twistRelax = ClothStaticRootTwistRelax;
        }

        if (joint.Node < feModel.StaticNodeCount)
        {
            kv.Add("allow_rotation", feModel.AllowsRotation(joint.Node));
        }

        if (feModel.LocksTranslation(joint.Node, chainVersion, chain) || pinnedSimulatedRoot)
        {
            kv.Add("lock_translation", true);
        }

        kv.Add("goal_strength", goalStrength);
        kv.Add("goal_damping", feModel.GoalDampingPaint(integrator.ForceAttraction, integrator.VertexAttraction));

        var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);
        if (drag != 0f)
        {
            kv.Add("drag", drag);
        }

        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", feModel.GetIntegrator(gravityNode).Gravity / ClothSourceBaseGravity);

        kv.Add("twist_relax", twistRelax);

        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));

        // The attrs default spells out mask 15; a mask above the four bits cannot be stated on a chain.
        var collisionMask = feModel.GetNodeCollisionMask(joint.Node);
        if (collisionMask is >= 0 and < 0xF)
        {
            kv.Add("collision_layer_0", (collisionMask & 1) != 0);
            kv.Add("collision_layer_1", (collisionMask & 2) != 0);
            kv.Add("collision_layer_2", (collisionMask & 4) != 0);
            kv.Add("collision_layer_3", (collisionMask & 8) != 0);
        }

        var (worldFriction, groundFriction) = feModel.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", feModel.GetCollisionRadius(valueNode));

        var strayNode = joint.ValueNode >= 0
            ? joint.ValueNode
            : feModel.StrayRadiusNode(joint.Node, joint.Name);
        kv.Add("stray_radius", feModel.GetStrayRadius(strayNode));
        kv.Add("stray_radius_stretchiness", feModel.GetStrayStretchiness(strayNode));
        kv.Add("friction", feModel.GetNodeFriction(joint.Node));

        if (feModel.RecoverJointMass(joint.Node, chainMass) is { } massMultiplier)
        {
            kv.Add("mass", massMultiplier);
        }

        // A joint outside every selection states the selections its proxies are in.
        if ((feModel.GetVertexMapNames(joint.Node)
            ?? (joint.ProxyNode >= 0 ? feModel.GetVertexMapNames(joint.ProxyNode) : null))
            is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        var hinge = feModel.GetChainHinge(joint.Name, joint.Node);

        // An extruding chain states every joint's own ring, including an explicit 0 width.
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistKey(joint.ExtrudeTwist) + (rollTies ? joint.ExtrudeTwistTieNudge : 0f));

                if (joint.ForwardAxis != 'x')
                {
                    kv.Add("extrude_forward_axis", joint.ForwardAxis.ToString());
                }
            }
        }

        // A hinged joint with only the hinge's two proxies has no second ring to recover.
        if (joint.EndEffector != 0f && (hinge is null || feModel.ProxyCountOf(joint.Node) > 2))
        {
            kv.Add("end_effector", joint.EndEffector);
        }

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

        // The bend is written once per declaration, so each declaration states the bend of its own rank.
        if (feModel.GetStiffHinge(joint.Node) is { } stiffHinge)
        {
            if (feModel.GetStiffHinge(joint.Node, secondDeclaration ? 1 : 0) is { } declared)
            {
                kv.Add("stiff_hinge", declared.Stiffness);
                kv.Add("stiff_hinge_angle", declared.Angle);
            }

            var stiffBias = stiffHinge.MotionBias != 0f ? stiffHinge.MotionBias : feModel.GetMotionBias(joint) ?? 0f;
            if (stiffBias != 0f)
            {
                kv.Add("motion_bias", stiffBias);
            }
        }
        else if (feModel.GetMotionBias(joint) is { } motionBias)
        {
            kv.Add("motion_bias", motionBias);
        }

        if (hinge is { } chainHinge)
        {
            kv.Add("hinge_constraint_vector_worldspace", ToKVArray(chainHinge.Vector));
            kv.Add("hinge_constraint_soft", softHinge);
            kv.Add("hinge_constraint_limit_cw", chainHinge.LimitCw);
            kv.Add("hinge_constraint_limit_ccw", chainHinge.LimitCcw);
        }

        return kv;
    }

    /// <summary>The <c>attrs</c> table of a <c>ClothChain</c>: the column schema and the default of every joint key.</summary>
    internal static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f,
        float extrudeTwist = 0f, float mass = 1f)
    {
        var attrs = KVObject.Collection();

        KVObject AddAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = KVObject.Collection();
            attr.Add("display", display);
            attr.Add("show", show);
            attr.Add("ui_order", uiOrder);
            attrs.Add(key, attr);
            return attr;
        }

        KVObject FloatAttr(string key, string display, bool show, int uiOrder, float def, float? min = null, float? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject IntAttr(string key, string display, bool show, int uiOrder, int def, int? min = null, int? max = null)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            if (min.HasValue) { attr.Add("min", min.Value); }
            if (max.HasValue) { attr.Add("max", max.Value); }
            return attr;
        }

        KVObject BoolAttr(string key, string display, bool show, int uiOrder, bool def)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", def);
            return attr;
        }

        KVObject StringAttr(string key, string display, bool show, int uiOrder)
        {
            var attr = AddAttr(key, display, show, uiOrder);
            attr.Add("default", "");
            return attr;
        }

        StringAttr("joint_name", "Joint Name", true, 1).Add("lock", true);
        StringAttr("joint_parent", "Parent Joint", false, 2);
        BoolAttr("simulate", "Simulate", true, 3, true);
        BoolAttr("allow_rotation", "Allow Rotation", false, 4, true);
        FloatAttr("stretch_spring", "Stretch Stiffness", false, 5, 1.0f, 0.0f, 1.0f);
        FloatAttr("child_sibling_spring", "Spring Between Children", false, 6, 0.0f, 0.0f, 1.0f);
        FloatAttr("bend_spring", "Bend Stiffness", false, 7, 1.0f, 0.0f, 1.0f);
        FloatAttr("torsion_spring", "Torsion Stiffness", false, 8, 0.0f, 0.0f, 1.0f);
        FloatAttr("explicit_length", "Explicit Length", false, 9, 0.0f, 0.0f);
        BoolAttr("world_collision", "World Ground Collision", true, 10, false);
        BoolAttr("animated_length", "Animated Length", false, 11, false);
        FloatAttr("goal_strength", "Goal Strength", true, 12, 0.0f, 0.0f, 1.0f);
        FloatAttr("goal_damping", "Goal Damping", true, 13, 0.0f, 0.0f, 1.0f);
        FloatAttr("drag", "Extra Drag", false, 14, 0.0f, 0.0f, 1.0f);
        FloatAttr("mass", "Mass", false, 15, mass, 0.0f);
        FloatAttr("gravity_z", "Gravity", true, 16, 1.0f);
        FloatAttr("collision_radius", "Collision Radius", true, 17, 0.0f, 0.0f);
        BoolAttr("lock_translation", "Lock Translation", false, 18, false);
        FloatAttr("suspender", "Suspender Spring", false, 19, 0.0f);
        FloatAttr("antishrink", "Antishrink Strength", false, 20, 1.0f, 0.0f, 1.0f);
        FloatAttr("stray_radius", "Stray Radius", true, 21, 0.0f, 0.0f);
        FloatAttr("stray_radius_stretchiness", "Stray Radius Stretchiness", false, 22, 0.0f, 0.0f);
        FloatAttr("friction", "Friction", false, 23, 0.0f, 0.0f, 1.0f);
        StringAttr("vertex_map", "Vertex Map", false, 24).Add("verify", "vertex_map");
        FloatAttr("end_effector", "End Effector", false, 25, 0.0f).Add("lock_default_value", true);
        FloatAttr("stiff_hinge", "Stiff Hinge", true, 26, 0.0f, 0.0f, 1.0f).Add("lock_root2", true);
        FloatAttr("stiff_hinge_angle", "Stiff Hinge Angle", true, 27, 0.0f, 0.0f, 180.0f).Add("lock_root2", true);
        FloatAttr("motion_bias", "Motion Bias", true, 28, 0.0f, -1.0f, 1.0f).Add("lock_root", true);
        IntAttr("extra_iterations", "Extra Iterations", true, 29, 0, 0, 1000);
        FloatAttr("twist_relax", "Twist Relax", true, 30, 0.0f, 0.0f, 1.0f);
        IntAttr("extrude_sides", "Extrude Sides", false, 31, extrudeSides, 0, 4);
        FloatAttr("extrude_radius", "Extrude Radius", false, 32, extrudeSides >= 1 ? extrudeRadius : 5.0f, 0.0f);
        FloatAttr("extrude_twist", "Extrude Twist", false, 33, ClothExtrudeTwistAttrDefault);
        StringAttr("extrude_forward_axis", "Extrude Forward Axis", false, 34).Add("verify", "extrude_forward_axis");
        FloatAttr("world_friction", "Ground Softness (\"world friction\" in Source1)", false, 35, 0.0f, 0.0f, 1.0f);
        FloatAttr("ground_friction", "Ground Friction", false, 36, 0.0f, 0.0f, 1.0f);
        StringAttr("stray_box", "Stray Box", false, 37).Add("verify", "stray_box");
        BoolAttr("collision_layer_0", "Collision Layer 0", false, 38, true);
        BoolAttr("collision_layer_1", "Collision Layer 1", false, 39, true);
        BoolAttr("collision_layer_2", "Collision Layer 2", false, 40, true);
        BoolAttr("collision_layer_3", "Collision Layer 3", false, 41, true);

        return attrs;
    }
}

using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // An unrolled proxy ring sits on the joint frame's +Y, so an authored twist counts down from 90 degrees.
    const float ClothExtrudeTwistBase = 90f;

    static KVObject MakeClothChainNode(FeModel feModel, FeModel.BoneChain chain, bool hasOtherChains,
        IReadOnlyList<FeModel.BoneChainJoint>? walk = null)
    {
        // A rigid hinge takes the chain's rod network over, so a hinged chain that still carries rods was
        // authored with a soft link instead.
        var softHinge = feModel.HasChainRods(chain);

        var joints = KVObject.Array();
        foreach (var joint in walk ?? chain.Joints)
        {
            var jointNode = MakeClothJoint(feModel, joint, chainExtrudes: chain.ExtrudeSides >= 1, softHinge);
            if (feModel.SpringsHingeChildren(chain, joint.Node))
            {
                jointNode.Add("child_sibling_spring", 1.0f);
            }

            joints.Add(jointNode);
        }

        var chainData = KVObject.Collection();
        chainData.Add("joints", joints);
        chainData.Add("attrs", MakeClothChainAttrs(chain.ExtrudeSides, chain.ExtrudeRadius, chain.ExtrudeTwist));
        chainData.Add("selection", KVObject.Array());

        // The two chain formats are not interchangeable: format 1 registers a non-simulated joint that has
        // no parent to be offset from into m_LockToGoal, format 2 leaves it out. Both are in live use, so
        // the original's own m_LockToGoal membership is what says which one a chain was authored in.
        // A rotation-locked root carries a second, sharper signal: format 1 suppresses that root's
        // m_NodeBases entry and format 2 keeps it, so the original's own m_NodeBases decides those chains.
        // The node-base signal reads an ABSENT root entry as format 1, which is equally what an anchor bone
        // several sub-chains were merged under looks like: it roots no chain in the original, so nothing
        // ever gave it a base. Format 1 also locks every non-simulated, rotation-free joint of an extruding
        // chain to its goal, so an original that locks none of them rules format 1 out directly.
        var root = chain.Joints.Count > 0 ? chain.Joints[0] : null;
        var lockedInOriginal = chain.Joints.Exists(joint => feModel.IsLockedToGoal(joint.Node));
        var locksJoints = chain.ExtrudeSides >= 1
            && chain.Joints.Exists(joint => !joint.Simulated && feModel.AllowsRotation(joint.Node));

        // A chain of one joint compiles only at version 0, but the access violation it avoids only
        // happens when the model carries a second chain; a model whose only chain has one joint
        // compiles fine at version >= 1 and keeps the node-base-driven choice below.
        chainData.Add("version", chain.Joints.Count == 1 && hasOtherChains
            ? 0
            : root is not null && !feModel.AllowsRotation(root.Node)
                && (lockedInOriginal || !locksJoints)
            ? (feModel.NodeBases.ContainsKey(root.Node) ? 2 : 1)
            : (lockedInOriginal ? 1 : 2));

        var chainNode = MakeNode("ClothChain",
            ("name", chain.RootBone + chain.DeclarationSuffix),
            ("root_bone", chain.RootBone),
            ("chain", chainData));

        // A rigid ClothChainHinge is a child node of the chain, constraining one joint by name.
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
    /// The plain second declaration of the joints <c>BuildBoneChains</c> marked restated, or null when
    /// the chain has none. Emitted right after the extruding chain, so the compiler re-registers those
    /// joint nodes with these values and adds the plain parent rod, as the source's own second
    /// declaration did. Every value is read from the joint node itself, which is where the second
    /// declaration left it.
    /// </summary>
    static KVObject? MakeClothChainRestatement(FeModel feModel, FeModel.BoneChain chain)
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
            kv.Add("goal_strength", FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction));
            kv.Add("goal_damping", FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction));
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

    static KVObject MakeClothJoint(FeModel feModel, FeModel.BoneChainJoint joint, bool chainExtrudes = false,
        bool softHinge = false)
    {
        var kv = KVObject.Collection();
        kv.Add("joint_name", joint.Name);

        if (joint.ParentName is not null)
        {
            kv.Add("joint_parent", joint.ParentName);
        }

        // The compiler CUBES the joint goal_strength into flAnimationForceAttraction, the same way it
        // treats the painted cloth_goal_strength_v2 on a proxy mesh, so the emitted value is the cube root
        // of the recovered attraction.
        //
        // It is recovered regardless of joint.Simulated: a chain ROOT is routinely authored
        // `simulate = false` with a nonzero goal_strength, so gating on the flag would zero goal_strength
        // on every chain root.
        //
        // A joint the source declared twice keeps the second declaration's values on its own node;
        // the first declaration's values survive on the ring it extruded (MakeClothChainRestatement
        // emits the second declaration from the node). Where the two declarations each extruded a ring
        // of their own, BoneChainJoint.ValueNode names this declaration's.
        var valueNode = joint.ValueNode >= 0 ? joint.ValueNode
            : joint.Restated && joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        var integrator = feModel.GetIntegrator(valueNode);
        var goalStrength = FeModel.GoalStrengthFromAttraction(integrator.ForceAttraction);

        var twistRelax = feModel.GetAuthoredTwistRelax(joint.Node, joint.ParentNode, joint.ProxyNode);

        // The compiler scales a twist entry by the ORIENT joint's own twist_relax only where that
        // joint simulates, and writes a flat 0.0 where it merely allows rotation. So a chain root
        // the original gives a non-zero entry of its own was authored as a SIMULATED joint, and it
        // is pinned into the static block by lock_translation rather than by simulate = false.
        var pinnedSimulatedRoot = joint.IsRoot && !joint.Simulated && twistRelax > 0f;
        kv.Add("simulate", joint.Simulated || pinnedSimulatedRoot);

        // Only a static node carries a rotation lock.
        if (joint.Node < feModel.StaticNodeCount)
        {
            kv.Add("allow_rotation", feModel.AllowsRotation(joint.Node));
        }

        if (feModel.LocksTranslation(joint.Node) || pinnedSimulatedRoot)
        {
            kv.Add("lock_translation", true);
        }

        kv.Add("goal_strength", goalStrength);
        kv.Add("goal_damping", FeModel.GoalDampingFromAttraction(integrator.ForceAttraction, integrator.VertexAttraction));

        // The same flPointDamping channel the proxy sheets carry as cloth_drag.
        var drag = Math.Clamp(integrator.PointDamping / ClothDragPointDampingScale, 0f, 1f);
        if (drag != 0f)
        {
            kv.Add("drag", drag);
        }

        var gravityNode = joint.ProxyNode >= 0 ? joint.ProxyNode : joint.Node;
        kv.Add("gravity_z", feModel.GetIntegrator(gravityNode).Gravity / ClothSourceBaseGravity);

        // A non-zero twist_relax, stiff_hinge or motion_bias makes the compiler build a Twist or
        // KelagerBend constraint network in place of the plain ropes a chain otherwise compiles to, so
        // each is recovered per joint, magnitude included, from the original's own m_Twists participation
        // (FeModel.GetAuthoredTwistRelax) rather than defaulted.
        kv.Add("twist_relax", twistRelax);

        // World collision membership and radius (m_WorldCollisionNodes / m_NodeCollisionRadii).
        kv.Add("world_collision", feModel.IsWorldCollisionNode(joint.Node));

        var (worldFriction, groundFriction) = feModel.GetWorldFriction(joint.Node);
        kv.Add("world_friction", worldFriction);
        kv.Add("ground_friction", groundFriction);
        kv.Add("collision_radius", feModel.GetCollisionRadius(valueNode));

        // Stray radius (m_AnimStrayRadii): the max distance the node may stray from its animated position.
        // A joint whose own node is pinned records it on its ring alone, which is also the only place a
        // shared joint's second declaration keeps its own.
        var strayNode = joint.ValueNode >= 0 ? joint.ValueNode : joint.Node;
        kv.Add("stray_radius", feModel.GetStrayRadius(strayNode));
        kv.Add("stray_radius_stretchiness", feModel.GetStrayStretchiness(strayNode));
        kv.Add("friction", feModel.GetNodeFriction(joint.Node));

        if (feModel.RecoverJointMassMultiplier(joint.Node) is { } massMultiplier)
        {
            kv.Add("mass", massMultiplier);
        }

        // The named vertex selections this joint belongs to, comma separated. Naming them here is what
        // puts the joint and the proxies extruded from it back into the selections cloth effects target.
        // A joint that does not simulate stays out of the selection itself while its proxies join it, so
        // when the joint's own node belongs to none the proxies it extruded carry the membership.
        if ((feModel.GetVertexMapNames(joint.Node)
            ?? (joint.ProxyNode >= 0 ? feModel.GetVertexMapNames(joint.ProxyNode) : null))
            is { } vertexMaps)
        {
            kv.Add("vertex_map", vertexMaps);
        }

        // The hinge constraint the ClothChainHinge node writes onto the joint it constrains. It both
        // orients that joint's proxy ring and adds the compiler's own static anchor node, so a joint that
        // shipped one loses a control node without it - and a joint that did not gains one.
        var hinge = feModel.GetChainHinge(joint.Name, joint.Node);

        // Per-joint extrude width. The chain-level extrude_sides (MakeClothChainAttrs) is one uniform
        // value, so it cannot reproduce a ribbon whose END-CAP joint fans wider than its body; overriding
        // it per joint recovers that fan. A chain that extrudes at all emits every joint's own width,
        // including an explicit 0 for a joint that carries no proxies, which would otherwise inherit the
        // chain-level default. A chain that does not extrude emits nothing.
        if (chainExtrudes)
        {
            kv.Add("extrude_sides", joint.ExtrudeSides);

            // Ring geometry varies along a chain, so the chain-level defaults only fit one joint. Emit each
            // joint's own measured ring instead.
            if (joint.ExtrudeSides > 0)
            {
                kv.Add("extrude_radius", joint.ExtrudeRadius);
                kv.Add("extrude_twist", ClothExtrudeTwistBase - joint.ExtrudeTwist + joint.ExtrudeTwistTieNudge);

                // 'x' is the compiler's own default and needs no explicit key.
                if (joint.ForwardAxis != 'x')
                {
                    kv.Add("extrude_forward_axis", joint.ForwardAxis.ToString());
                }
            }

        }

        // A tip that fans into two rows is a second ring this far along the joint's forward axis, not
        // one ring of twice the width - the wider ring puts every proxy somewhere else entirely. A
        // hinged joint that carries only the hinge's own two proxies has no second ring to recover:
        // that pair straddles the hinge axis, which reads as two rings a ring apart. Emitted outside the
        // extrude block: a joint whose only generated node is the "$cc<bone>_Ctr" centre has an
        // end_effector but no ring at all, so its chain never extrudes.
        if (joint.EndEffector != 0f && (hinge is null || feModel.ProxyCountOf(joint.Node) > 2))
        {
            kv.Add("end_effector", joint.EndEffector);
        }

        // Each of the three sliders lands verbatim on the flRelaxationFactor of the rod it generates, so
        // they carry the recovered per-joint stiffness rather than a 1.0/0.0 on-off (see
        // FeModel.BuildBoneChains). Zero still means "no rod at all" on the bend and torsion spans.
        // 1.0 is stretch_spring's own attr default and needs no explicit key.
        if (joint.StretchStiffness != 1.0f)
        {
            kv.Add("stretch_spring", joint.StretchStiffness);
        }

        kv.Add("bend_spring", joint.BendStiffness);
        kv.Add("torsion_spring", joint.TorsionStiffness);
        kv.Add("extra_iterations", joint.ExtraIterations);
        kv.Add("suspender", joint.Suspender);

        // A stiff hinge compiles to a three-node bend rather than a rod, so it is recovered from the bend
        // centred on this joint (see FeModel.GetStiffHinge).
        if (feModel.GetStiffHinge(joint.Node) is { } stiffHinge)
        {
            kv.Add("stiff_hinge", stiffHinge.Stiffness);
            kv.Add("stiff_hinge_angle", stiffHinge.Angle);

            if (stiffHinge.MotionBias != 0f)
            {
                kv.Add("motion_bias", stiffHinge.MotionBias);
            }
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

    // The cloth-chain joint datatable schema: per-column UI metadata and defaults, matching the editable
    // ModelDoc source the tools produce. The compiler takes the "default" value of any joint field the
    // joint rows above do not write.
    static KVObject MakeClothChainAttrs(int extrudeSides = 0, float extrudeRadius = 0f, float extrudeTwist = 0f)
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

        // The complete version-2 attr set. An incomplete v1-era key list makes the v2 joint grid ignore
        // the table and fall back to default columns. Attrs with values recovered from the compiled
        // FeModel are shown; the rest keep stock visibility.
        StringAttr("joint_name", "Joint Name", true, 1).Add("lock", true);
        StringAttr("joint_parent", "Parent Joint", false, 2);
        BoolAttr("simulate", "Simulate", true, 3, true);
        BoolAttr("allow_rotation", "Allow Rotation", false, 4, true);
        // The display names match the ClothChainAttrEditor schema and are ModelDoc UI labels only.
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
        FloatAttr("mass", "Mass", false, 15, 1.0f, 0.0f);
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
        // Recovered per chain from the compiled $cc proxy width (see FeModel.BuildBoneChains): a 2-wide
        // strip or N-sided tube regenerates its proxies only when the ClothChain re-declares the extrude.
        // extrudeSides 0 keeps the stock default, a plain rope.
        IntAttr("extrude_sides", "Extrude Sides", false, 31, extrudeSides, 0, 4);
        FloatAttr("extrude_radius", "Extrude Radius", false, 32, extrudeSides >= 1 ? extrudeRadius : 5.0f, 0.0f);
        FloatAttr("extrude_twist", "Extrude Twist", false, 33, extrudeSides >= 1 ? extrudeTwist : 0.0f);
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

    bool EmitChainClothPhase(FeModel feModel, List<FeModel.BoneChain> boneChains, KVObject rootChildren)
    {
        // Phase 1 fallback (no recoverable sheet): bone-chain cloth, plus a GENERATED sheet grid
        // over each group of neighbouring chains (skirts/capes). The grid mirrors hand-authored
        // item proxies: with back_solve_joints=false the chains keep simulating the bones while
        // the sheet simulates the surface between them and drives the render mesh directly.
        var (softbody, softbodyChildren) = MakeListNode("Softbody");
        AddSoftbodyAttributes(softbody, feModel);
        softbodyChildren.Add(MakeClothParams(feModel,
            generatesBendRods: feModel.HasChainStiffnessRods(boneChains),
            generatesBendOnlyRods: feModel.HasChainBendOnlyRods(boneChains),
            addCurvature: feModel.ChainRingCurvature));
        var (clothFolder, clothFolderChildren) = MakeListNode("Folder");
        clothFolder.Add("name", "cloth");
        softbodyChildren.Add(clothFolder);

        var hasOtherChains = boneChains.Count > 1;

        // The compiled node order is (block, constraint rank, creation index), so inside a band it IS the
        // order the control nodes were created in. A chain creates each joint immediately followed by its
        // own ring nodes, so a joint the band order separates from its rings was created before the chain
        // ran - by an earlier declaration of the same bone name, which the chain then reuses.
        var declarationPlan = TryPlanClothChainDeclarations(feModel, boneChains,
            ClothControlParentTest(feModel));
        var declaredChains = declarationPlan?.Chains ?? boneChains;
        foreach (var (name, node) in declarationPlan?.PreDeclared ?? [])
        {
            clothFolderChildren.Add(MakeClothChainJointDeclaration(feModel, name, node));
        }

        foreach (var boneChain in declaredChains)
        {
            var walk = declarationPlan is not null
                && declarationPlan.Walk.TryGetValue(boneChain, out var found)
                ? found
                : null;
            clothFolderChildren.Add(MakeClothChainNode(feModel, boneChain, hasOtherChains, walk));
            if (MakeClothChainRestatement(feModel, boneChain) is { } restated)
            {
                clothFolderChildren.Add(restated);
            }
        }

        foreach (var clothGrid in ClothChainGridsToExtract)
        {
            // The grid ships DISABLED: the chains alone reproduce the original physics, and with
            // drive_meshes the sheet would fight the chain-driven skinning of the same region.
            // It is a ready-made starting sheet the author can enable/retarget in ModelDoc
            // (like hand-authored cape proxies that drive otherwise boneless render regions).
            var gridNode = MakeClothProxyMeshFile(clothGrid.Name, clothGrid.FileName, backSolveJoints: false, driveMeshes: true);
            gridNode.Add("disabled", true);
            clothFolderChildren.Add(gridNode);
        }

        AddClothFaces(clothFolderChildren, feModel);
        var sourceSprings = AddClothSourceSprings(softbodyChildren, feModel, boneChains);
        AddClothChainSurplusRods(softbodyChildren, feModel, boneChains);

        var chainCoveredNodes = boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Node)
            .ToHashSet();
        var clothBones = ClothBoneNames(feModel);
        clothBones.UnionWith(boneChains.SelectMany(static chain => chain.Joints)
            .Select(static joint => joint.Name));
        // A static control node no chain, shape or jiggle bone claims is recreated by nothing else in
        // this phase, so it is declared as a bare ClothNode wherever the compiled skeleton records the
        // bone as a cloth control node - the same evidence the proxy-sheet phase reads.
        var clothControlBones = model?.Skeleton.Bones
            .Where(static b => b.IsClothControlNode)
            .Select(static b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chainSurface = feModel.Quads.Length > 0 || feModel.Tris.Length > 0;
        AddFreeClothNodesAndSprings(clothFolderChildren, softbodyChildren, feModel, chainCoveredNodes,
            name => chainSurface || (clothControlBones?.Contains(name) ?? false),
            clothBones, ClothVertexMapFolders(feModel, clothFolderChildren), hasOtherChains: true,
            ClothControlAncestorTest(feModel), sourceSprings);

        AddClothFollowBones(softbodyChildren, feModel, clothBones);
        AddClothCollisionShapes(softbodyChildren, feModel);
        AddClothEffects(softbodyChildren, feModel, AvailableVertexMaps(feModel, boneChains));
        rootChildren.Add(softbody);
        AddClothAntiTunnelProbes(rootChildren, feModel, proxyNodeNames: null);
        return true;
    }
}

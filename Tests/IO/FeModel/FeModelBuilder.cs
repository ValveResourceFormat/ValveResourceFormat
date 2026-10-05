using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>
    /// Builds an <see cref="FeModel"/> from typed records, so a reconstruction rule can be exercised on inputs whose
    /// expected result is computed by hand. Every set member is written as its serialized <c>m_pFeModel</c> field.
    /// </summary>
    internal sealed record FeModelBuilder
    {
        public string[] Names { get; init; } = [];

        /// <summary>Gets <c>m_nNodeCount</c>, the length of <see cref="Names"/> unless set.</summary>
        public int? NodeCount { get; init; }

        public int StaticNodes { get; init; }

        public int? RotLockStaticNodes { get; init; }

        public int? FirstPositionDrivenNode { get; init; }

        /// <summary>Gets <c>m_NodeInvMasses</c>, 0 on the static nodes and 1 on the rest unless set.</summary>
        public float[]? InvMasses { get; init; }

        public int[]? Parents { get; init; }

        /// <summary>Gets the <c>m_InitPose</c> positions, each with no rotation, unless <see cref="Poses"/> is set.</summary>
        public Vector3[]? Positions { get; init; }

        public FeTransform[]? Poses { get; init; }

        public uint? StaticNodeFlags { get; init; }

        public uint? DynamicNodeFlags { get; init; }

        public float? LocalForce { get; init; }

        public float? DefaultSurfaceStretch { get; init; }

        public float? DefaultThreadStretch { get; init; }

        public float? DefaultGravityScale { get; init; }

        public int? ExtraIterations { get; init; }

        public int? ExtraGoalIterations { get; init; }

        public FeRodConstraint[]? Rods { get; init; }

        public FeQuad[]? Quads { get; init; }

        public FeTri[]? Tris { get; init; }

        public FeTwistConstraint[]? Twists { get; init; }

        public FeHingeLimit[]? HingeLimits { get; init; }

        public FeNodeBase[]? NodeBases { get; init; }

        public FeCtrlOffset[]? CtrlOffsets { get; init; }

        public FeCtrlOsOffset[]? CtrlOsOffsets { get; init; }

        public FeCtrlOffset[]? LockToParent { get; init; }

        public FeCtrlSoftOffset[]? CtrlSoftOffsets { get; init; }

        public FeNodeReverseOffset[]? ReverseOffsets { get; init; }

        public FeFitMatrix[]? FitMatrices { get; init; }

        public FeFitWeight[]? FitWeights { get; init; }

        public FeFollowNode[]? FollowNodes { get; init; }

        public FeNodeIntegrator[]? NodeIntegrator { get; init; }

        public FeKelagerBend[]? KelagerBends { get; init; }

        public FeAxialEdgeBend[]? AxialEdges { get; init; }

        public FeAnimStrayRadius[]? AnimStrayRadii { get; init; }

        public FeNodeWindBase[]? DynNodeWindBases { get; init; }

        public FeBoneMergeLink[]? BoneMergeLinks { get; init; }

        public FeSimdRodConstraintAnim[]? SimdRodsAnim { get; init; }

        public FeCollisionPlane[]? CollisionPlanes { get; init; }

        public FeTaperedCapsuleRigid[]? TaperedCapsuleRigids { get; init; }

        public FeSphereRigid[]? SphereRigids { get; init; }

        public FeBoxRigid[]? BoxRigids { get; init; }

        public FeRigidColliderIndices[]? RigidColliderPriorities { get; init; }

        public FeAntiTunnelProbe[]? AntiTunnelProbes { get; init; }

        public int[]? AntiTunnelTargetNodes { get; init; }

        public uint[]? AntiTunnelBytecode { get; init; }

        public FeEffectDesc[]? Effects { get; init; }

        public FeIndexedJiggleBone[]? JiggleBones { get; init; }

        public FeVertexMapDesc[]? VertexMaps { get; init; }

        public byte[]? VertexMapValues { get; init; }

        public uint[]? VertexSetNames { get; init; }

        public byte[]? DynNodeVertexSet { get; init; }

        /// <summary>Gets <c>m_Ropes</c>; <c>m_nRopeCount</c> is <see cref="RopeCount"/>.</summary>
        public int[]? Ropes { get; init; }

        public int? RopeCount { get; init; }

        public int[]? SourceElems { get; init; }

        public int[]? LockToGoal { get; init; }

        public int[]? WorldCollisionNodes { get; init; }

        public int[]? TreeCollisionMasks { get; init; }

        public float[]? NodeCollisionRadii { get; init; }

        public float[]? LocalForces { get; init; }

        public float[]? LocalRotations { get; init; }

        public float[]? LegacyStretchForce { get; init; }

        public FeModel Build() => new(ToKV());

        public ClothReconstruction Reconstruct() => new(Build());

        /// <summary>The <c>m_pFeModel</c> object this builder describes.</summary>
        public KVObject ToKV()
        {
            var kv = KVObject.Collection();
            kv["m_CtrlName"] = KVObject.Array(Names.Select(static name => new KVObject(name)));
            kv["m_nNodeCount"] = NodeCount ?? Names.Length;
            kv["m_nStaticNodes"] = StaticNodes;
            kv["m_NodeInvMasses"] = Floats(InvMasses ?? [.. Names.Select((_, node) => node < StaticNodes ? 0f : 1f)]);

            Set(kv, "m_nRotLockStaticNodes", RotLockStaticNodes);
            Set(kv, "m_nFirstPositionDrivenNode", FirstPositionDrivenNode);
            Set(kv, "m_nStaticNodeFlags", StaticNodeFlags);
            Set(kv, "m_nDynamicNodeFlags", DynamicNodeFlags);
            Set(kv, "m_flLocalForce", LocalForce);
            Set(kv, "m_flDefaultSurfaceStretch", DefaultSurfaceStretch);
            Set(kv, "m_flDefaultThreadStretch", DefaultThreadStretch);
            Set(kv, "m_flDefaultGravityScale", DefaultGravityScale);
            Set(kv, "m_nExtraIterations", ExtraIterations);
            Set(kv, "m_nExtraGoalIterations", ExtraGoalIterations);
            Set(kv, "m_nRopeCount", RopeCount);

            var poses = Poses ?? Positions?.Select(static position => new FeTransform(position, 1f, Quaternion.Identity)).ToArray();
            Set(kv, "m_InitPose", poses, Transform);
            Set(kv, "m_SkelParents", Parents, Ints);
            Set(kv, "m_Ropes", Ropes, Ints);
            Set(kv, "m_SourceElems", SourceElems, Ints);
            Set(kv, "m_LockToGoal", LockToGoal, Ints);
            Set(kv, "m_WorldCollisionNodes", WorldCollisionNodes, Ints);
            Set(kv, "m_TreeCollisionMasks", TreeCollisionMasks, Ints);
            Set(kv, "m_AntiTunnelTargetNodes", AntiTunnelTargetNodes, Ints);
            Set(kv, "m_AntiTunnelBytecode", AntiTunnelBytecode, UInts);
            Set(kv, "m_VertexSetNames", VertexSetNames, UInts);
            Set(kv, "m_VertexMapValues", VertexMapValues, Bytes);
            Set(kv, "m_DynNodeVertexSet", DynNodeVertexSet, Bytes);
            Set(kv, "m_NodeCollisionRadii", NodeCollisionRadii, Floats);
            Set(kv, "m_LocalForce", LocalForces, Floats);
            Set(kv, "m_LocalRotation", LocalRotations, Floats);
            Set(kv, "m_LegacyStretchForce", LegacyStretchForce, Floats);

            SetArray(kv, "m_Rods", Rods, static rod => Object(
                ("nNode", Ints(rod.Nodes)), ("flMaxDist", rod.MaxDist), ("flMinDist", rod.MinDist),
                ("flWeight0", rod.Weight0), ("flRelaxationFactor", rod.RelaxationFactor)));
            SetArray(kv, "m_Quads", Quads, static quad => Object(
                ("nNode", Ints(quad.Nodes)), ("flSlack", quad.Slack),
                ("vShape", KVObject.Array(quad.Shapes.Select(static shape => Floats([shape.X, shape.Y, shape.Z, shape.W]))))));
            SetArray(kv, "m_Tris", Tris, static tri => Object(
                ("nNode", Ints(tri.Nodes)), ("w1", tri.W1), ("w2", tri.W2), ("v1x", tri.V1x), ("v2", Floats([tri.V2.X, tri.V2.Y]))));
            SetArray(kv, "m_Twists", Twists, static twist => Object(
                ("nNodeOrient", twist.NodeOrient), ("nNodeEnd", twist.NodeEnd),
                ("flTwistRelax", twist.TwistRelax), ("flSwingRelax", twist.SwingRelax)));
            SetArray(kv, "m_HingeLimits", HingeLimits, static hinge => Object(
                ("nNode", Ints(hinge.Nodes)), ("nFlags", hinge.Flags), ("flWeight4", hinge.Weight4), ("flWeight5", hinge.Weight5),
                ("flAngleCenter", hinge.AngleCenter), ("flAngleExtents", hinge.AngleExtents)));
            SetArray(kv, "m_NodeBases", NodeBases, static basis => Object(
                ("nNode", basis.Node), ("nDummy", Ints(basis.Dummy)), ("nNodeX0", basis.NodeX0), ("nNodeX1", basis.NodeX1),
                ("nNodeY0", basis.NodeY0), ("nNodeY1", basis.NodeY1), ("qAdjust", Quat(basis.Adjust))));
            SetArray(kv, "m_CtrlOffsets", CtrlOffsets, CtrlOffset);
            SetArray(kv, "m_LockToParent", LockToParent, CtrlOffset);
            SetArray(kv, "m_CtrlOsOffsets", CtrlOsOffsets, static offset => Object(
                ("nCtrlParent", offset.CtrlParent), ("nCtrlChild", offset.CtrlChild)));
            SetArray(kv, "m_CtrlSoftOffsets", CtrlSoftOffsets, static offset => Object(
                ("nCtrlParent", offset.CtrlParent), ("nCtrlChild", offset.CtrlChild), ("vOffset", Vector(offset.Offset)),
                ("flAlpha", offset.Alpha)));
            SetArray(kv, "m_ReverseOffsets", ReverseOffsets, static offset => Object(
                ("vOffset", Vector(offset.Offset)), ("nBoneCtrl", offset.BoneCtrl), ("nTargetNode", offset.TargetNode)));
            SetArray(kv, "m_FitMatrices", FitMatrices, static fit =>
            {
                var o = Object(("bone", Transform(fit.Bone)), ("vCenter", Vector(fit.Center)), ("nEnd", fit.End), ("nNode", fit.Node),
                    ("nBeginDynamic", fit.BeginDynamic));
                Set(o, "nCtrl", fit.Ctrl);
                return o;
            });
            SetArray(kv, "m_FitWeights", FitWeights, static weight => Object(
                ("flWeight", weight.Weight), ("nNode", weight.Node), ("nDummy", weight.Dummy)));
            SetArray(kv, "m_FollowNodes", FollowNodes, static follow => Object(
                ("nParentNode", follow.ParentNode), ("nChildNode", follow.ChildNode), ("flWeight", follow.Weight)));
            SetArray(kv, "m_NodeIntegrator", NodeIntegrator, static integrator => Object(
                ("flPointDamping", integrator.PointDamping), ("flAnimationForceAttraction", integrator.AnimationForceAttraction),
                ("flAnimationVertexAttraction", integrator.AnimationVertexAttraction), ("flGravity", integrator.Gravity)));
            SetArray(kv, "m_KelagerBends", KelagerBends, static bend => Object(
                ("flWeight", Floats(bend.Weights)), ("flHeight0", bend.Height0), ("nNode", Ints(bend.Nodes)), ("nReserved", bend.Reserved)));
            SetArray(kv, "m_AxialEdges", AxialEdges, static edge => Object(
                ("te", edge.Te), ("tv", edge.Tv), ("flDist", edge.Dist), ("flWeight", Floats(edge.Weights)), ("nNode", Ints(edge.Nodes))));
            SetArray(kv, "m_AnimStrayRadii", AnimStrayRadii, static stray => Object(
                ("nNode", Ints(stray.Nodes)), ("flMaxDist", stray.MaxDist), ("flRelaxationFactor", stray.RelaxationFactor)));
            SetArray(kv, "m_DynNodeWindBases", DynNodeWindBases, static wind => Object(
                ("nNodeX0", wind.NodeX0), ("nNodeX1", wind.NodeX1), ("nNodeY0", wind.NodeY0), ("nNodeY1", wind.NodeY1)));
            SetArray(kv, "m_BoneMergeLinks", BoneMergeLinks, static link => Object(
                ("m_nParentHash", link.ParentHash), ("m_nChildNode", link.ChildNode)));
            SetArray(kv, "m_SimdRodsAnim", SimdRodsAnim, static rods => Object(
                ("nNode", KVObject.Array(rods.Nodes.Select(static row => Ints(row)))), ("f4Weight0", Floats(rods.Weight0)),
                ("f4RelaxationFactor", Floats(rods.RelaxationFactor))));
            SetArray(kv, "m_CollisionPlanes", CollisionPlanes, static plane => Object(
                ("nCtrlParent", plane.CtrlParent), ("nChildNode", plane.ChildNode),
                ("m_Plane", Object(("m_vNormal", Vector(plane.Plane.Normal)), ("m_flOffset", plane.Plane.Offset))),
                ("flStrength", plane.Strength), ("flStickiness", plane.Stickiness)));
            SetArray(kv, "m_TaperedCapsuleRigids", TaperedCapsuleRigids, static capsule => Rigid(
                Object(("vSphere", KVObject.Array(capsule.Spheres.Select(static sphere => Floats([sphere.X, sphere.Y, sphere.Z, sphere.W]))))),
                capsule.Node, capsule.CollisionMask, capsule.VertexMapIndex, capsule.Flags, capsule.Stickiness));
            SetArray(kv, "m_SphereRigids", SphereRigids, static sphere =>
            {
                var o = KVObject.Collection();
                if (sphere.Sphere is { } s)
                {
                    o["vSphere"] = Floats([s.X, s.Y, s.Z, s.W]);
                }

                return Rigid(o, sphere.Node, sphere.CollisionMask, sphere.VertexMapIndex, sphere.Flags, sphere.Stickiness);
            });
            SetArray(kv, "m_BoxRigids", BoxRigids, static box =>
            {
                var o = KVObject.Collection();
                if (box.Frame2 is { } frame)
                {
                    o["tmFrame2"] = Transform(frame);
                }

                if (box.Size is { } size)
                {
                    o["vSize"] = Vector(size);
                }

                return Rigid(o, box.Node, box.CollisionMask, box.VertexMapIndex, box.Flags, box.Stickiness);
            });
            SetArray(kv, "m_RigidColliderPriorities", RigidColliderPriorities, static row =>
            {
                var o = Object(("m_nTaperedCapsuleRigidIndex", row.TaperedCapsuleRigidIndex), ("m_nSphereRigidIndex", row.SphereRigidIndex),
                    ("m_nBoxRigidIndex", row.BoxRigidIndex), ("m_nSDFRigidIndex", row.SDFRigidIndex),
                    ("m_nCollisionPlaneIndex", row.CollisionPlaneIndex));
                Set(o, "m_nCollisionSphereIndex", row.CollisionSphereIndex, Ints);
                return o;
            });
            SetArray(kv, "m_AntiTunnelProbes", AntiTunnelProbes, static probe => Object(
                ("flWeight", probe.Weight), ("nFlags", probe.Flags), ("nProbeNode", probe.ProbeNode), ("nCount", probe.Count),
                ("nBegin", probe.Begin), ("flActivationDistance", probe.ActivationDistance), ("flCurvatureRadius", probe.CurvatureRadius),
                ("flBias", probe.Bias)));
            SetArray(kv, "m_Effects", Effects, static effect =>
            {
                var o = Object(("sName", effect.Name), ("nNameHash", effect.NameHash), ("nType", effect.Type));
                if (effect.Params is { } parameters)
                {
                    o["m_Params"] = parameters;
                }

                return o;
            });
            SetArray(kv, "m_JiggleBones", JiggleBones, static jiggle => Object(
                ("m_nNode", unchecked((uint)jiggle.Node)), ("m_nJiggleParent", unchecked((uint)jiggle.JiggleParent)),
                ("m_jiggleBone", JiggleBone(jiggle.JiggleBone))));
            SetArray(kv, "m_VertexMaps", VertexMaps, static map => Object(
                ("sName", map.Name), ("nNameHash", map.NameHash), ("nColor", map.Color), ("nFlags", map.Flags),
                ("nVertexBase", map.VertexBase), ("nVertexCount", map.VertexCount), ("nMapOffset", map.MapOffset),
                ("nNodeListOffset", map.NodeListOffset), ("vCenterOfMass", Vector(map.CenterOfMass)),
                ("flVolumetricSolveStrength", map.VolumetricSolveStrength), ("nScaleSourceNode", map.ScaleSourceNode),
                ("nNodeListCount", map.NodeListCount)));

            return kv;
        }

        /// <summary>A rest transform at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) with no rotation.</summary>
        public static FeTransform Pose(float x, float y, float z) => new(new Vector3(x, y, z), 1f, Quaternion.Identity);

        /// <summary>A rest transform at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) turned by the given quaternion.</summary>
        public static FeTransform Pose(float x, float y, float z, float qx, float qy, float qz, float qw)
            => new(new Vector3(x, y, z), 1f, new Quaternion(qx, qy, qz, qw));

        /// <summary>A rod free to move between <paramref name="min"/> and <paramref name="max"/>.</summary>
        public static FeRodConstraint Rod(int a, int b, float min, float max, float weight0 = 0.5f, float relaxation = 1f)
            => new([a, b], max, min, weight0, relaxation);

        /// <summary>A rigid rod, whose minimum equals its maximum.</summary>
        public static FeRodConstraint RigidRod(int a, int b, float length, float relaxation = 1f, float weight0 = 0.5f)
            => new([a, b], length, length, weight0, relaxation);

        public static FeQuad Quad(int a, int b, int c, int d, float slack = 0f, Vector4[]? shapes = null) => new([a, b, c, d], slack, shapes ?? []);

        public static FeTri Tri(int a, int b, int c) => new([a, b, c], 0f, 0f, 0f, default);

        public static FeTwistConstraint Twist(int orient, int end, float twistRelax, float swingRelax) => new(orient, end, twistRelax, swingRelax);

        /// <summary>A node basis with no adjustment, which reads as a zero quaternion.</summary>
        public static FeNodeBase NodeBase(int node, int x0, int x1, int y0, int y1) => new(node, [], x0, x1, y0, y1, default);

        public static FeCtrlOffset Offset(int parent, int child, float x, float y, float z) => new(new Vector3(x, y, z), parent, child);

        public static FeNodeReverseOffset ReverseOffset(int bone, int target, float x, float y, float z) => new(new Vector3(x, y, z), bone, target);

        public static FeFitWeight FitWeight(int node, float weight = 1f) => new(weight, node, 0);

        /// <summary>A fit over the weights before <paramref name="end"/>, with an identity bone and no <c>nCtrl</c>.</summary>
        public static FeFitMatrix FitMatrix(int node, int end, int beginDynamic, FeTransform? bone = null, Vector3 center = default)
            => new(bone ?? FeTransform.Identity, center, end, node, beginDynamic, null);

        public static FeKelagerBend KelagerBend(int bent, int a, int b, float height0, float[] weights) => new(weights, height0, [bent, a, b], 0);

        /// <summary>A vertex map over <paramref name="count"/> nodes from <paramref name="vertexBase"/>, its weights at <paramref name="offset"/>.</summary>
        public static FeVertexMapDesc VertexMap(string name, uint hash, int offset, int vertexBase, int count, float volumetric = 0f,
            int scaleSourceNode = -1)
            => new(name, hash, 0, 0, vertexBase, count, offset, 0, Vector3.Zero, volumetric, scaleSourceNode, 0);

        public static FeEffectDesc Effect(string name, uint hash, int type, KVObject? parameters = null) => new(name, hash, type, parameters);

        private static KVObject CtrlOffset(FeCtrlOffset offset) => Object(
            ("vOffset", Vector(offset.Offset)), ("nCtrlParent", offset.CtrlParent), ("nCtrlChild", offset.CtrlChild));

        private static KVObject Rigid(KVObject o, int node, int collisionMask, int vertexMapIndex, int flags, float? stickiness)
        {
            o["nNode"] = node;
            o["nCollisionMask"] = collisionMask;
            o["nVertexMapIndex"] = vertexMapIndex;
            o["nFlags"] = flags;
            Set(o, "flStickiness", stickiness);
            return o;
        }

        private static KVObject JiggleBone(FeJiggleBone bone) => Object(
            ("m_nFlags", bone.Flags), ("m_flLength", bone.Length), ("m_flTipMass", bone.TipMass),
            ("m_flYawStiffness", bone.YawStiffness), ("m_flYawDamping", bone.YawDamping),
            ("m_flPitchStiffness", bone.PitchStiffness), ("m_flPitchDamping", bone.PitchDamping),
            ("m_flAlongStiffness", bone.AlongStiffness), ("m_flAlongDamping", bone.AlongDamping), ("m_flAngleLimit", bone.AngleLimit),
            ("m_flMinYaw", bone.MinYaw), ("m_flMaxYaw", bone.MaxYaw), ("m_flYawFriction", bone.YawFriction), ("m_flYawBounce", bone.YawBounce),
            ("m_flMinPitch", bone.MinPitch), ("m_flMaxPitch", bone.MaxPitch), ("m_flPitchFriction", bone.PitchFriction),
            ("m_flPitchBounce", bone.PitchBounce), ("m_flBaseMass", bone.BaseMass), ("m_flBaseStiffness", bone.BaseStiffness),
            ("m_flBaseDamping", bone.BaseDamping), ("m_flBaseMinLeft", bone.BaseMinLeft), ("m_flBaseMaxLeft", bone.BaseMaxLeft),
            ("m_flBaseLeftFriction", bone.BaseLeftFriction), ("m_flBaseMinUp", bone.BaseMinUp), ("m_flBaseMaxUp", bone.BaseMaxUp),
            ("m_flBaseUpFriction", bone.BaseUpFriction), ("m_flBaseMinForward", bone.BaseMinForward),
            ("m_flBaseMaxForward", bone.BaseMaxForward), ("m_flBaseForwardFriction", bone.BaseForwardFriction),
            ("m_flRadius0", bone.Radius0), ("m_flRadius1", bone.Radius1), ("m_vPoint0", Vector(bone.Point0)), ("m_vPoint1", Vector(bone.Point1)),
            ("m_nCollisionMask", bone.CollisionMask));

        /// <summary>A KV3 object holding <paramref name="fields"/> in order.</summary>
        public static KVObject Object(params (string Key, KVObject Value)[] fields)
        {
            var o = KVObject.Collection();
            foreach (var (key, value) in fields)
            {
                o[key] = value;
            }

            return o;
        }

        private static void Set<T>(KVObject kv, string key, T? value)
            where T : struct
        {
            if (value is { } v)
            {
                kv[key] = v switch
                {
                    int i => i,
                    uint u => u,
                    float f => f,
                    _ => throw new ArgumentException(key),
                };
            }
        }

        private static void Set<T>(KVObject kv, string key, T? value, Func<T, KVObject> write)
            where T : class
        {
            if (value is not null)
            {
                kv[key] = write(value);
            }
        }

        private static void SetArray<T>(KVObject kv, string key, T[]? values, Func<T, KVObject> write)
        {
            if (values is not null)
            {
                kv[key] = KVObject.Array(values.Select(write));
            }
        }

        private static KVObject Transform(FeTransform transform) => Floats([
            transform.Position.X, transform.Position.Y, transform.Position.Z, transform.Scale,
            transform.Orientation.X, transform.Orientation.Y, transform.Orientation.Z, transform.Orientation.W]);

        private static KVObject Transform(FeTransform[] transforms) => KVObject.Array(transforms.Select(static t => Transform(t)));

        private static KVObject Vector(Vector3 v) => Floats([v.X, v.Y, v.Z]);

        private static KVObject Quat(Quaternion q) => Floats([q.X, q.Y, q.Z, q.W]);

        public static KVObject Floats(params float[] values) => KVObject.Array(values.Select(static value => new KVObject(value)));

        public static KVObject Ints(params int[] values) => KVObject.Array(values.Select(static value => new KVObject(value)));

        private static KVObject UInts(uint[] values) => KVObject.Array(values.Select(static value => new KVObject(value)));

        private static KVObject Bytes(byte[] values) => KVObject.Array(values.Select(static value => new KVObject((int)value)));
    }
}

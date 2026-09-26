using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>A single structural rod (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance (<c>flMinDist</c>).</param>
        /// <param name="MaxDist">Maximum allowed distance (<c>flMaxDist</c>).</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction (<c>flWeight0</c>).</param>
        /// <param name="RelaxationFactor">Relaxation factor (<c>flRelaxationFactor</c>).</param>
        public readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor);

        /// <summary>
        /// One rod of <c>m_SimdRodsAnim</c>: its two nodes in lane order and the <c>f4Weight0</c> lane value, which is
        /// the share of <paramref name="NodeA"/> exactly as <see cref="Rod.Weight0"/> is on a rod kept in <c>m_Rods</c>.
        /// </summary>
        /// <param name="NodeA">First node of the lane.</param>
        /// <param name="NodeB">Second node of the lane.</param>
        /// <param name="Weight0">Blend weight of <paramref name="NodeA"/>.</param>
        public readonly record struct AnimRod(int NodeA, int NodeB, float Weight0);

        /// <summary>A single node's explicit orientation basis (from <c>m_NodeBases</c>).</summary>
        /// <param name="NodeX0">Control-node index defining the local X axis' first endpoint.</param>
        /// <param name="NodeX1">Control-node index defining the local X axis' second endpoint.</param>
        /// <param name="NodeY0">Control-node index defining the local Y axis' first endpoint.</param>
        /// <param name="NodeY1">Control-node index defining the local Y axis' second endpoint.</param>
        public readonly record struct NodeBasis(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>
        /// A single node's solver integrator parameters (from <c>m_NodeIntegrator</c>).
        /// </summary>
        /// <param name="PointDamping">Velocity damping (<c>flPointDamping</c>).</param>
        /// <param name="ForceAttraction">Goal/force attraction toward the animated pose (<c>flAnimationForceAttraction</c>).</param>
        /// <param name="VertexAttraction">Per-vertex attraction toward the animated pose (<c>flAnimationVertexAttraction</c>).</param>
        /// <param name="Gravity">Gravity acceleration applied to the node (<c>flGravity</c>).</param>
        public readonly record struct NodeIntegrator(float PointDamping, float ForceAttraction, float VertexAttraction, float Gravity);

        /// <summary>A single twist constraint record (from <c>m_Twists</c>).</summary>
        /// <param name="Orient">The node whose frame the twist is measured in.</param>
        /// <param name="End">The node the twist is measured toward.</param>
        /// <param name="TwistRelax">The record's <c>flTwistRelax</c>.</param>
        /// <param name="SwingRelax">The record's <c>flSwingRelax</c>.</param>
        public readonly record struct TwistRecord(int Orient, int End, float TwistRelax, float SwingRelax);

        /// <summary>A three-node bend constraint (from <c>m_KelagerBends</c>).</summary>
        /// <param name="MidNode">The bent node, whose joint carries the authored stiff hinge.</param>
        /// <param name="End0">The first node the bend measures against.</param>
        /// <param name="End1">The second node the bend measures against.</param>
        /// <param name="MidWeight">Solver share of <paramref name="MidNode"/>.</param>
        /// <param name="End0Weight">Solver share of <paramref name="End0"/>.</param>
        /// <param name="End1Weight">Solver share of <paramref name="End1"/>.</param>
        /// <param name="Height">Distance from the bent node to the triple's centroid the bend allows.</param>
        public readonly record struct KelagerBend(int MidNode, int End0, int End1,
            float MidWeight, float End0Weight, float End1Weight, float Height);

        /// <summary>A named vertex selection, used to target cloth effects and joint vertex maps.</summary>
        /// <param name="Name">The authored selection name.</param>
        /// <param name="NameHash">The hash the compiler keys the selection by.</param>
        /// <param name="VertexBase">The first control node the selection covers.</param>
        /// <param name="VertexCount">How many consecutive control nodes it covers.</param>
        /// <param name="CenterOfMass">The selection's centre of mass.</param>
        /// <param name="Weights">Membership weight of each covered node, 0..1, indexed from <paramref name="VertexBase"/>.</param>
        /// <param name="VolumetricSolveStrength">How strongly the selection is solved as a volume.</param>
        /// <param name="ScaleSourceNode">The control node whose scale the selection follows, or -1.</param>
        public readonly record struct VertexMap(string Name, uint NameHash, int VertexBase, int VertexCount,
            Vector3 CenterOfMass, float[] Weights, float VolumetricSolveStrength = 0f, int ScaleSourceNode = -1)
        {
            /// <summary>Gets how strongly <paramref name="node"/> belongs to this selection, 0 when it does not.</summary>
            public float WeightOf(int node)
            {
                var index = node - VertexBase;
                return index >= 0 && index < Weights.Length ? Weights[index] : 0f;
            }
        }

        /// <summary>An anti-tunnelling probe (from <c>m_AntiTunnelProbes</c>).</summary>
        public readonly record struct AntiTunnelProbe(float Weight, uint Flags, int ProbeNode, int Count, int Begin,
            float ActivationDistance, float CurvatureRadius, float Bias);

        /// <summary>A dynamic-to-kinematic node link (from <c>m_DynKinLinks</c>).</summary>
        public readonly record struct DynKinLink(int Parent, int Child);

        /// <summary>A collision plane (from <c>m_CollisionPlanes</c>).</summary>
        public readonly record struct CollisionPlane(int CtrlParent, int ChildNode, Vector3 PlaneNormal,
            float PlaneOffset, float Stickiness, float Strength);

        /// <summary>A signed-distance-field collision volume (from <c>m_SDFRigids</c>).</summary>
        public readonly record struct SDFRigid(Vector3 LocalMin, Vector3 LocalMax, float Bounciness, int Node,
            int CollisionMask, int VertexMapIndex, uint Flags, float[] Distances, int Width, int Height, int Depth);

        /// <summary>
        /// A named cloth effect (from <c>m_Effects</c>). <see cref="Params"/> is the unparsed per-type parameter block.
        /// </summary>
        public readonly record struct Effect(string Name, uint NameHash, int Type, KVObject Params);

        /// <summary>A deprecated morph layer (from <c>m_MorphLayers</c>).</summary>
        public readonly record struct MorphLayer(string Name, uint NameHash, int[] Nodes, Vector3[] InitPos,
            float[] Gravity, float[] GoalStrength, float[] GoalDamping, uint Flags);

        /// <summary>A self-collision layer (from <c>m_SelfCollisionLayers</c>).</summary>
        public readonly record struct SelfCollisionLayer(string Name, int[] Nodes, float ParentReaction,
            uint Flags, uint[] EndIndices);

        /// <summary>A node stray-box constraint (from <c>m_NodeStrayBoxes</c>).</summary>
        public readonly record struct NodeStrayBox(Vector3 Min, Vector3 Max, uint Flags, int NodeA, int NodeB);

        /// <summary>A tapered-capsule stretch constraint (from <c>m_TaperedCapsuleStretches</c>).</summary>
        public readonly record struct TaperedCapsuleStretch(int NodeA, int NodeB, int CollisionMask,
            float RadiusA, float RadiusB);

        /// <summary>A per-pair spring constraint (from <c>m_SpringIntegrator</c>).</summary>
        public readonly record struct SpringIntegrator(int NodeA, int NodeB, float RestLength,
            float SpringConstant, float SpringDamping, float NodeWeight0);

        /// <summary>
        /// One row of <c>m_RigidColliderPriorities</c>: the index at which a priority group starts in each
        /// rigid-collider array. Row <c>g</c> holds group <c>g</c>'s first index in every array and the last
        /// row holds each array's element count, so group <c>g</c> owns <c>[row[g], row[g + 1])</c>.
        /// </summary>
        public readonly record struct RigidColliderIndices(int TaperedCapsuleRigidIndex, int SphereRigidIndex,
            int BoxRigidIndex, int SDFRigidIndex, int CollisionPlaneIndex);

        /// <summary>A jiggle bone's physical parameters (from <c>CFeJiggleBone</c>).</summary>
        public readonly record struct JiggleBone(
            uint Flags, float Length, float TipMass,
            float YawStiffness, float YawDamping, float PitchStiffness, float PitchDamping,
            float AlongStiffness, float AlongDamping, float AngleLimit,
            float MinYaw, float MaxYaw, float YawFriction, float YawBounce,
            float MinPitch, float MaxPitch, float PitchFriction, float PitchBounce,
            float BaseMass, float BaseStiffness, float BaseDamping,
            float BaseMinLeft, float BaseMaxLeft, float BaseLeftFriction,
            float BaseMinUp, float BaseMaxUp, float BaseUpFriction,
            float BaseMinForward, float BaseMaxForward, float BaseForwardFriction,
            float Radius0, float Radius1, Vector3 Point0, Vector3 Point1, int CollisionMask);

        /// <summary>A jiggle bone keyed to its control node (from <c>m_JiggleBones</c>).</summary>
        public readonly record struct IndexedJiggleBone(int Node, int JiggleParent, JiggleBone Bone);

        /// <summary>A bone-merge link (from <c>m_BoneMergeLinks</c>).</summary>
        public readonly record struct BoneMergeLink(uint ParentHash, int ChildNode);

        /// <summary>A node locked to its parent's offset transform (from <c>m_LockToParent</c>).</summary>
        public readonly record struct LockToParentLink(Vector3 Offset, int CtrlParent, int CtrlChild);

        /// <summary>A strip's column pairing (from <c>m_CtrlOsOffsets</c>).</summary>
        public readonly record struct CtrlOsOffset(int CtrlParent, int CtrlChild);

        /// <summary>A generated node's bone-local anchor offset (from <c>m_CtrlOffsets</c>).</summary>
        public readonly record struct CtrlOffset(Vector3 Offset, int CtrlParent, int CtrlChild);
    }
}

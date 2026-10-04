using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>A distance constraint between two control nodes (from <c>m_Rods</c>).</summary>
        /// <param name="NodeA">First endpoint control-node index.</param>
        /// <param name="NodeB">Second endpoint control-node index.</param>
        /// <param name="MinDist">Minimum allowed distance.</param>
        /// <param name="MaxDist">Maximum allowed distance.</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction.</param>
        /// <param name="RelaxationFactor">Relaxation factor.</param>
        public readonly record struct Rod(int NodeA, int NodeB, float MinDist, float MaxDist, float Weight0, float RelaxationFactor)
        {
            /// <summary>Gets whether the rod's minimum and maximum distance differ by more than a relative 1e-4.</summary>
            internal bool IsBanded => MathF.Abs(MinDist - MaxDist) > 1e-4f * MathF.Max(1f, MathF.Abs(MaxDist));

            /// <summary>Gets whether <paramref name="length"/> matches the rest distance <paramref name="rest"/>.</summary>
            internal static bool IsAtRestLength(float length, float rest) => MathF.Abs(length - rest) <= MathF.Max(1e-3f, 1e-4f * rest);
        }

        /// <summary>A rod of <c>m_SimdRodsAnim</c>.</summary>
        /// <param name="NodeA">First node of the lane.</param>
        /// <param name="NodeB">Second node of the lane.</param>
        /// <param name="Weight0">Share of <paramref name="NodeA"/> in the correction, as in <see cref="Rod.Weight0"/>.</param>
        public readonly record struct AnimRod(int NodeA, int NodeB, float Weight0);

        /// <summary>A node's explicit orientation basis (from <c>m_NodeBases</c>).</summary>
        /// <param name="NodeX0">First control node of the local X axis.</param>
        /// <param name="NodeX1">Second control node of the local X axis.</param>
        /// <param name="NodeY0">First control node of the local Y axis.</param>
        /// <param name="NodeY1">Second control node of the local Y axis.</param>
        public readonly record struct NodeBasis(int NodeX0, int NodeX1, int NodeY0, int NodeY1);

        /// <summary>A node's solver integrator parameters (from <c>m_NodeIntegrator</c>).</summary>
        /// <param name="PointDamping">Velocity damping.</param>
        /// <param name="ForceAttraction">Force attraction toward the animated pose.</param>
        /// <param name="VertexAttraction">Position attraction toward the animated pose.</param>
        /// <param name="Gravity">Gravity applied to the node.</param>
        public readonly record struct NodeIntegrator(float PointDamping, float ForceAttraction, float VertexAttraction, float Gravity);

        /// <summary>A twist constraint (from <c>m_Twists</c>).</summary>
        /// <param name="Orient">The node whose frame the twist is measured in.</param>
        /// <param name="End">The node the twist is measured toward.</param>
        /// <param name="TwistRelax">Twist relaxation factor.</param>
        /// <param name="SwingRelax">Swing relaxation factor.</param>
        public readonly record struct TwistRecord(int Orient, int End, float TwistRelax, float SwingRelax);

        /// <summary>A three-node bend constraint (from <c>m_KelagerBends</c>).</summary>
        /// <param name="MidNode">The bent node.</param>
        /// <param name="End0">The first node the bend measures against.</param>
        /// <param name="End1">The second node the bend measures against.</param>
        /// <param name="MidWeight">Solver share of <paramref name="MidNode"/>.</param>
        /// <param name="End0Weight">Solver share of <paramref name="End0"/>.</param>
        /// <param name="End1Weight">Solver share of <paramref name="End1"/>.</param>
        /// <param name="Height">Allowed distance from the bent node to the centroid of the three nodes.</param>
        public readonly record struct KelagerBend(int MidNode, int End0, int End1,
            float MidWeight, float End0Weight, float End1Weight, float Height);

        /// <summary>A named vertex selection, used to target cloth effects and joint vertex maps.</summary>
        /// <param name="Name">The selection name.</param>
        /// <param name="NameHash">The selection name hash.</param>
        /// <param name="VertexBase">The first control node the selection covers.</param>
        /// <param name="VertexCount">How many consecutive control nodes it covers.</param>
        /// <param name="CenterOfMass">The selection's center of mass.</param>
        /// <param name="Weights">Membership weight of each covered node, 0 to 1, indexed from <paramref name="VertexBase"/>.</param>
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

        /// <summary>
        /// A collision plane (from <c>m_CollisionPlanes</c>). <see cref="Stickiness"/> only exists in older files,
        /// newer ones carry <see cref="Strength"/> instead.
        /// </summary>
        public readonly record struct CollisionPlane(int CtrlParent, int ChildNode, Vector3 PlaneNormal,
            float PlaneOffset, float Stickiness, float Strength);

        /// <summary>
        /// A named cloth effect (from <c>m_Effects</c>). <see cref="Params"/> is the raw per-type parameter block,
        /// or null when absent.
        /// </summary>
        public readonly record struct Effect(string Name, uint NameHash, int Type, KVObject? Params);

        /// <summary>A deprecated morph layer (from <c>m_MorphLayers</c>).</summary>
        public readonly record struct MorphLayer(string Name, uint NameHash, int[] Nodes, Vector3[] InitPos,
            float[] Gravity, float[] GoalStrength, float[] GoalDamping, uint Flags);

        /// <summary>
        /// One row of <c>m_RigidColliderPriorities</c>: the index at which a priority group starts in each
        /// rigid-collider array. The last row holds each array's element count, so group <c>g</c> owns
        /// <c>[row[g], row[g + 1])</c>.
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

        /// <summary>
        /// A jiggle bone keyed to its control node (from <c>m_JiggleBones</c>). Both indices are stored unsigned,
        /// so the all-ones "none" value reads as -1.
        /// </summary>
        public readonly record struct IndexedJiggleBone(int Node, int JiggleParent, JiggleBone Bone);

        /// <summary>A bone-merge link (from <c>m_BoneMergeLinks</c>).</summary>
        public readonly record struct BoneMergeLink(uint ParentHash, int ChildNode);

        /// <summary>A node locked to its parent at a fixed offset (from <c>m_LockToParent</c>).</summary>
        public readonly record struct LockToParentLink(Vector3 Offset, int CtrlParent, int CtrlChild);

        /// <summary>A strip's column pairing (from <c>m_CtrlOsOffsets</c>).</summary>
        public readonly record struct CtrlOsOffset(int CtrlParent, int CtrlChild);

        /// <summary>A generated node's bone-local anchor offset (from <c>m_CtrlOffsets</c>).</summary>
        public readonly record struct CtrlOffset(Vector3 Offset, int CtrlParent, int CtrlChild);
    }
}

using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// A single joint within a reconstructed bone chain.
        /// </summary>
        public sealed class BoneChainJoint
        {
            /// <summary>Gets the control-node index of this joint.</summary>
            public int Node { get; init; }
            /// <summary>Gets the bone name of this joint.</summary>
            public required string Name { get; init; }
            /// <summary>Gets the control-node index of the chain parent, or -1 if this is the chain root.</summary>
            public int ParentNode { get; init; }
            /// <summary>Gets the bone name of the chain parent, or null if this is the chain root.</summary>
            public string? ParentName { get; init; }
            /// <summary>Gets the inverse mass for this node (0 = static anchor).</summary>
            public float InvMass { get; set; }
            /// <summary>
            /// Gets the node whose compiled per-node values describe THIS declaration of the joint, or
            /// -1 to read them from the joint's own node. A bone two chains declare re-registers its own
            /// node once, so only the ring each declaration extruded still carries that declaration's
            /// goal, damping, collision radius, stray radius and simulate flag.
            /// </summary>
            public int ValueNode { get; set; } = -1;
            /// <summary>
            /// Gets the number of auto-generated <c>$cc</c> proxy nodes the compiler placed on THIS joint
            /// (its local ribbon width). Usually equal to the chain's <see cref="BoneChain.ExtrudeSides"/>,
            /// but an end-cap joint can fan wider. Used to override the chain-level extrude per joint so an
            /// end-cap fan is not lost to the uniform chain width.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>
            /// Gets one of the <c>$cc</c> proxy nodes generated from this joint, or -1 when it has none.
            /// A joint's own node is position-driven and compiles with no gravity, so the authored
            /// <c>gravity_z</c> survives only on its proxies.
            /// </summary>
            public int ProxyNode { get; set; } = -1;
            /// <summary>
            /// Gets the authored <c>extra_iterations</c> of this joint, recovered from how many copies of
            /// its parent span the compiler emitted (one per iteration, so copies minus one).
            /// </summary>
            public int ExtraIterations { get; set; }
            /// <summary>
            /// Gets the authored <c>suspender</c> of this joint: a single companion rod between this
            /// joint's own ring and its CHAIN ROOT's ring that the compiler adds, carrying this value as
            /// its own <c>flRelaxationFactor</c>. Zero when the joint carries none. Told apart from
            /// <see cref="ExtraIterations"/> by <c>RootSuspenderValue</c> in <c>BuildBoneChains</c>.
            /// </summary>
            public float Suspender { get; set; }
            /// <summary>
            /// Gets whether a rod spans this joint and its grandparent, i.e. whether the source authored a
            /// non-zero <c>bend_spring</c> here.
            /// </summary>
            public bool BendSpring { get; set; }
            /// <summary>
            /// Gets whether a rod spans this joint and its great-grandparent, i.e. whether the source
            /// authored a non-zero <c>torsion_spring</c> here.
            /// </summary>
            public bool TorsionSpring { get; set; }
            /// <summary>
            /// Gets the authored <c>stretch_spring</c> of this joint: the <c>flRelaxationFactor</c> the
            /// compiler wrote on the span between this joint and its chain parent.
            /// </summary>
            public float StretchStiffness { get; set; } = 1f;
            /// <summary>
            /// Gets the authored <c>bend_spring</c> of this joint: the <c>flRelaxationFactor</c> on the
            /// span to its grandparent. Zero when <see cref="BendSpring"/> is false, which is what keeps
            /// the compiler from generating that rod at all.
            /// </summary>
            public float BendStiffness { get; set; }
            /// <summary>
            /// Gets the authored <c>torsion_spring</c> of this joint: the <c>flRelaxationFactor</c> on the
            /// span to its great-grandparent. Zero when <see cref="TorsionSpring"/> is false.
            /// </summary>
            public float TorsionStiffness { get; set; }
            /// <summary>Gets the distance from this joint to its own proxy ring.</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll (degrees) added on top of <see cref="ExtrudeTwist"/> to settle the node-base
            /// axis scan of a joint whose scan is a numerical tie. Zero for every other joint.
            /// </summary>
            public float ExtrudeTwistTieNudge { get; set; }
            /// <summary>
            /// Gets the roll (degrees) of this joint's proxy ring about the forward axis, measured in the
            /// joint's rest frame. The ring's unrolled direction is the frame's +Y, so the value authored
            /// as <c>extrude_twist</c> is this angle's complement.
            /// </summary>
            public float ExtrudeTwist { get; set; }
            /// <summary>
            /// Gets the forward distance to a second proxy ring around this joint, which is what the
            /// authored <c>end_effector</c> produces (a tip that fans into two rows rather than one wider
            /// ring). Zero when the joint carries a single ring.
            /// </summary>
            public float EndEffector { get; set; }
            /// <summary>
            /// Gets the forward axis the compiler used to orient this joint's proxy ring (<c>'x'</c>,
            /// <c>'y'</c> or <c>'z'</c>), detected from the ring's own plane normal expressed in the
            /// joint's rest frame. <c>'x'</c> is the default and needs no explicit
            /// <c>extrude_forward_axis</c> authoring.
            /// </summary>
            public char ForwardAxis { get; set; } = 'x';
            /// <summary>
            /// Gets whether the source declared this joint a second time, in a plain chain after the
            /// extruding one. The plain declaration re-registers the joint node with its own values
            /// and ties it to its parent with a rod of its own, so the node and the ring extruded
            /// from it carry two different sets of values.
            /// </summary>
            public bool Restated { get; set; }
            /// <summary>Gets a value indicating whether this joint is simulated (invMass &gt; 0).</summary>
            public bool Simulated => InvMass > 0f;
            /// <summary>Gets a value indicating whether this joint is the chain root.</summary>
            public bool IsRoot => ParentNode < 0;
        }

        /// <summary>
        /// A reconstructed bone chain: a static anchor bone plus all of its simulated descendants.
        /// </summary>
        public sealed class BoneChain
        {
            /// <summary>Gets the anchor (root) bone name.</summary>
            public required string RootBone { get; init; }
            /// <summary>
            /// Gets the suffix that tells this chain apart from another declaration over the same root
            /// bone, empty for a bone only one chain declares.
            /// </summary>
            public string DeclarationSuffix { get; set; } = string.Empty;
            /// <summary>Gets the joints, root first, in pre-order (a parent always precedes its children).</summary>
            public List<BoneChainJoint> Joints { get; } = [];
            /// <summary>
            /// Gets the ribbon width the compiler baked as auto-generated <c>$cc</c> proxy nodes per joint:
            /// 0/1 = a plain 1-wide rope (no extrude), 2+ = an extruded strip or tube. Drives the
            /// ClothChain's <c>extrude_sides</c> so the recompile regenerates the same proxy count.
            /// </summary>
            public int ExtrudeSides { get; set; }
            /// <summary>Gets the mean distance from a joint bone to its <c>$cc</c> proxy nodes (the extrude half-width).</summary>
            public float ExtrudeRadius { get; set; }
            /// <summary>
            /// Gets the roll (degrees) applied to the extruded proxy ring about the chain's forward axis.
            /// Recovered from where the compiler actually placed the <c>$cc</c> proxies in the joint's rest
            /// frame; a chain whose ring is not rolled recovers 0.
            /// </summary>
            public float ExtrudeTwist { get; set; }
        }

        // One reconstructed ClothChain before its joints are walked: which bone roots it, which children
        // of a given node it keeps, and which of that node's rings belongs to it.
        sealed class ChainSpec
        {
            public int Root { get; init; }
            public bool RinglessRoot { get; init; }
            /// <summary>The children each listed node keeps in this chain; a node absent keeps all of them.</summary>
            public Dictionary<int, HashSet<int>>? ChildrenOf { get; set; }
            /// <summary>The ring each listed node extruded in THIS declaration; empty for a ringless one.</summary>
            public Dictionary<int, List<int>>? RingOf { get; set; }
            public string Suffix { get; set; } = string.Empty;
        }

        // extrude_forward_axis selector quaternions: a +90-degree rotation about local Z for 'y' (maps +X
        // to +Y), a -90-degree rotation about local Y for 'z' (maps +X to +Z). 'x' uses
        // Quaternion.Identity. Composed with a joint's own rest rotation (ringFrame = jointRot *
        // axisSelect), this re-labels which local axis is "forward".
        static readonly Quaternion ExtrudeAxisSelectY = new(0f, 0f, 0.70710677f, 0.70710677f);
        static readonly Quaternion ExtrudeAxisSelectZ = new(0f, -0.70710677f, 0f, 0.70710677f);

        static Quaternion ExtrudeAxisSelectQuaternion(char axis) => axis switch
        {
            'y' => ExtrudeAxisSelectY,
            'z' => ExtrudeAxisSelectZ,
            _ => Quaternion.Identity,
        };
    }
}

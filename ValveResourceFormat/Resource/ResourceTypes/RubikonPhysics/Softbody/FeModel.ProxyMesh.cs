using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// The auto-generated cloth proxy mesh (the cloth "sheet"), reconstructed from the FeModel surface
        /// topology. Vertices are the control nodes referenced by <see cref="Quads"/>/<see cref="Tris"/>
        /// (i.e. the <c>$cloth_*</c> proxy nodes); the real bone-chain nodes are intentionally excluded so a
        /// proxy-mesh recompile and a <c>ClothChain</c> recompile do not drive the same nodes twice.
        /// </summary>
        public sealed class ProxyMesh
        {
            /// <summary>Gets the original FeModel control-node index of each proxy vertex.</summary>
            public required int[] NodeIndices { get; init; }
            /// <summary>Gets the model-space rest position of each proxy vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the per-vertex <c>cloth_enable</c> flag (1 = simulated, 0 = pinned anchor).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>
            /// Gets the per-vertex goal/force attraction toward the animated pose (recovered from the node
            /// integrator's <c>flAnimationForceAttraction</c>, clamped to the 0..1 paint range). Higher = the
            /// cloth hugs the body more tightly. Emitted as the modern <c>cloth_goal_strength_v2$0</c> paint.
            /// </summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>
            /// Gets the per-vertex goal damping paint, recovered by inverting the vertex-attraction
            /// response (<see cref="GoalDampingFromAttraction"/>). Legacy models with out-of-range
            /// attraction clamp to the strongest reproducible damping.
            /// </summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>
            /// Gets the per-vertex <c>cloth_animation_force_attract</c> paint: the node integrator's
            /// <c>flAnimationForceAttraction</c> at 1/30 scale, for the vertices
            /// <c>RawGoalPaintNodes</c> keeps on the raw integrator, and 0 for the rest. Empty when
            /// no vertex of the sheet needs it.
            /// </summary>
            public float[] AnimationForceAttract { get; init; } = [];
            /// <summary>
            /// Gets the per-vertex <c>cloth_animation_attract</c> paint: the node integrator's
            /// <c>flAnimationVertexAttraction</c> at 1/30 scale, on the same vertices as
            /// <see cref="AnimationForceAttract"/>.
            /// </summary>
            public float[] AnimationAttract { get; init; } = [];
            /// <summary>Gets the per-vertex self-collision radius (recovered from <c>m_NodeCollisionRadii</c>).</summary>
            public required float[] CollisionRadius { get; init; }
            /// <summary>Gets the per-vertex friction (recovered from <c>m_DynNodeFriction</c>), 0..1 paint range.</summary>
            public required float[] Friction { get; init; }
            /// <summary>Gets the per-vertex air drag (recovered from the FeModel air-drag scalar), 0..1 paint range.</summary>
            public required float[] Drag { get; init; }
            /// <summary>Gets the per-vertex ground-collision weight (recovered where available), 0..1 paint range.</summary>
            public required float[] GroundCollision { get; init; }
            /// <summary>Gets the per-vertex ground friction of a world-colliding node (recovered from <c>m_WorldCollisionParams</c>), 0..1 paint range.</summary>
            public required float[] GroundFriction { get; init; }
            /// <summary>
            /// Gets the per-vertex gravity (the integrator's <c>flGravity</c>, verbatim - the
            /// <c>cloth_gravity$0</c> paint compiles into <c>flGravity</c> with no scaling, unlike the
            /// /360 <c>gravity_z</c> KV field on ClothNode/ClothChain joints). Without the stream the
            /// compiler defaults every vertex to 360, silently discarding authored per-vertex variation.
            /// </summary>
            public required float[] Gravity { get; init; }
            /// <summary>
            /// Gets the RAW compiled <c>flAnimationVertexAttraction</c> per vertex. The
            /// goal_strength/goal_damping pair caps it at 1.0; legacy-era compiles above that value are
            /// re-authored through <see cref="AnimationAttract"/> instead.
            /// </summary>
            public required float[] VertexAttraction { get; init; }
            /// <summary>
            /// Gets the skeleton bone influences of each proxy vertex. Pinned anchors carry a single
            /// weight-1 influence on their anchor bone. Simulated vertices are SMOOTHLY weighted across the
            /// nearest joints of the anchor's chain: the compiler back-solves a bone with a proper fit
            /// matrix only when enough weighted vertices reference it - hard single-bone skinning degrades
            /// every chain joint to a point-driven rope with a much denser rod network.
            /// </summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>
            /// Gets the faces (proxy-vertex index quads and triangles) covering the sheet, preserving the
            /// original quad/tri split. Triangulating the quads instead makes the compiler re-derive a much
            /// denser quad/rod network and the recompiled cloth turns rigid.
            /// </summary>
            public required List<int[]> Faces { get; init; }
            /// <summary>
            /// Gets the named vertex selections covering this sheet, as a per-vertex membership weight
            /// each. Painted back onto the sheet as one <c>cloth_vertex_set_&lt;name&gt;</c> stream per
            /// selection, which is how a proxy vertex joins one.
            /// </summary>
            public (string Name, float[] Weights)[] VertexMaps { get; init; } = [];
            /// <summary>
            /// Gets, per vertex, whether the compiled sheet drives it through distance constraints alone -
            /// the importer turned every authored element it belongs to into rods (<c>m_SourceElems</c>)
            /// instead of keeping it as a solve element (<c>m_Quads</c>/<c>m_Tris</c>). Empty on a sheet
            /// with no such region. Painted back as <c>cloth_make_rods</c>, which makes the compiler split
            /// the same sheet the same way; the sheet's own gradient paint is not recoverable and does not
            /// need to be.
            /// </summary>
            public float[] RodsDriven { get; init; } = [];
            /// <summary>Gets the number of simulated (cloth_enable == 1) vertices.</summary>
            public int SimulatedCount { get; init; }
            /// <summary>Gets the number of pinned (cloth_enable == 0) vertices.</summary>
            public int PinnedCount { get; init; }
            /// <summary>
            /// Gets whether the cloth importer is expected to silently PRUNE one or more of this synthesised
            /// island's vertices, which would make any explicit <c>ClothSpring</c> (m_Rods) referencing a
            /// pruned vertex a hard "Cannot find node $cloth_mXpY" compile failure. Two importer behaviours
            /// cause this (see <c>ComputeDropRisk</c>): (1) a pinned vertex whose face-neighbours are
            /// ALL pinned (a fully-static mesh region the solver discards), and (2) a near-coincident vertex
            /// pair the importer welds. When true, <c>AddClothProxySprings</c> skips this island's explicit
            /// rods and lets the compiler auto-derive them from the surface instead (guaranteed to compile,
            /// at the cost of exact rod topology for this one island). False for cleanly-triangulated
            /// islands, which keep their exact reconstructed rods.
            /// </summary>
            public bool IsDropRisk { get; init; }
            /// <summary>
            /// Gets whether <see cref="Faces"/> came from the authored <c>m_SourceElems</c> topology rather
            /// than a synthesised triangulation. The compiler rebuilds the shipped rods from such a sheet on
            /// its own, so it is exported without rod-suppressing paints and without explicit springs.
            /// </summary>
            public bool UsesAuthoredFaces { get; init; }
            /// <summary>
            /// Gets whether no vertex of this sheet is driven by a real skeleton bone. Hand-authored
            /// proxies of this kind ship unskinned, and the compiler answers by generating its own static
            /// root node plus one <c>m_CtrlOffsets</c> entry per vertex. Skinning such a sheet to the
            /// synthetic per-vertex bones instead binds each node directly and loses both.
            /// </summary>
            public bool IsFreeFloating { get; init; }
        }

        // The original "$cloth_m<N>" mesh index the compiler already assigned to a proxy vertex set (the
        // smallest parsed index among its nodes - every node of one physical proxy carries the same
        // index by construction, so this is exact, not a heuristic), or -1 if none of its nodes carry
        // that name (a rods-only "$cc" panel).
        int ProxyMeshOriginalIndex(ProxyMesh mesh) => mesh.NodeIndices
            .Select(node => ParseProxyMeshIndex(CtrlNames[node]))
            .Where(m => m >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        // Per-node cloth paint values recovered from the FeModel solver data (goal attraction, damping,
        // collision/friction/drag, gravity, and skin influences), shared by every proxy-mesh
        // reconstruction path - quad/tri-driven (BuildProxyMesh) and rod-only (BuildProxyMeshFromNodeSet).
        readonly record struct ProxyVertexData(
            bool IsSim,
            float GoalStrength,
            float GoalDamping,
            float AnimationForceAttract,
            float AnimationAttract,
            float CollisionRadius,
            float Friction,
            float Drag,
            float Gravity,
            float VertexAttraction,
            float GroundCollision,
            float GroundFriction,
            (string Bone, float Weight)[] SkinInfluences);

        // Extracts the mesh index the compiler already encodes in an auto-generated proxy control-node
        // name ("$cloth_m3p12" -> 3), or -1 if the name does not follow that convention.
        static int ParseProxyMeshIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return -1;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return -1;
            }

            return int.TryParse(name.AsSpan(Prefix.Length, pIndex - Prefix.Length), out var index) ? index : -1;
        }

        /// <summary>
        /// Whether the original compiled <paramref name="node"/> as a vertex of an authored cloth SHEET.
        /// Anything a reconstructed proxy mesh covers that this rejects is a stand-in the export builds
        /// over bone or free-<c>ClothNode</c> controls, so per-vertex sheet data recovered for it belongs
        /// to a different construct.
        /// </summary>
        public bool IsProxyMeshNode(int node)
            => node >= 0 && node < CtrlNames.Length && ParseProxyMeshIndex(CtrlNames[node]) >= 0;

        // Extracts the AUTHORED local vertex index from an auto-generated proxy control-node name
        // ("$cloth_m3p12" -> 12) - the compiler assigns p{N} as the vertex's position in the authored
        // DMX's own position array, so the original author's vertex ORDER survives compilation inside the
        // node names. int.MaxValue for non-proxy names.
        static int ParseProxyVertexIndex(string name)
        {
            const string Prefix = "$cloth_m";
            if (!name.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return int.MaxValue;
            }

            var pIndex = name.IndexOf('p', Prefix.Length);
            if (pIndex < 0)
            {
                return int.MaxValue;
            }

            return int.TryParse(name.AsSpan(pIndex + 1), out var index) ? index : int.MaxValue;
        }
    }
}

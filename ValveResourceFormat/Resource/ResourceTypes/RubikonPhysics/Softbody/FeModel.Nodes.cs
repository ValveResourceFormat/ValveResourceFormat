using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Gets the <c>transform_alignment</c> and <c>node_base</c> references that compile to the node's
        /// <c>m_NodeBases</c> entry, or null when it has none. Alignment 3 returns X0 and Y0 as -1.
        /// </summary>
        internal (int TransformAlignment, NodeBasis References)? ClothNodeBasisPreset(int node)
        {
            if (!NodeBases.TryGetValue(node, out var basis))
            {
                return null;
            }

            if (basis.NodeX0 == node && (basis.NodeY0 == node || basis.NodeY1 == node))
            {
                return (3, new NodeBasis(-1, basis.NodeX1, -1, basis.NodeY0 == node ? basis.NodeY1 : basis.NodeY0));
            }

            return (4, basis);
        }

        private const float TwistRelaxToParentFactor = 0.618f;

        private const float TwistRelaxToChildFactor = 0.382f;

        /// <summary>
        /// Gets whether a twist link naming <paramref name="node"/> carries a zero <c>flTwistRelax</c> in both directions.
        /// </summary>
        internal bool HasRelaxlessTwistLink(int node) => relaxlessTwistNodes.Contains(node);

        /// <summary>Gets whether <paramref name="node"/> orients a twist entry with a zero <c>flTwistRelax</c>.</summary>
        internal bool OrientsRelaxlessTwist(int node) => relaxlessTwistOrients.Contains(node);

        private readonly HashSet<int> relaxlessTwistNodes = [];

        private readonly HashSet<int> relaxlessTwistOrients = [];

        /// <summary>
        /// Recovers the joint's authored <c>twist_relax</c> from its entry toward its ring node
        /// <paramref name="proxyNode"/>, else toward <paramref name="parent"/>, else from any entry it orients.
        /// </summary>
        internal float GetAuthoredTwistRelax(int node, int parent, int proxyNode)
        {
            if (proxyNode >= 0 && TwistRelaxByLink.TryGetValue((node, proxyNode), out var toRing))
            {
                return toRing / TwistRelaxToChildFactor;
            }

            if (parent >= 0 && TwistRelaxByLink.TryGetValue((node, parent), out var toParent))
            {
                return toParent / TwistRelaxToParentFactor;
            }

            return twistOrientFallback.TryGetValue(node, out var toAnyChild)
                ? toAnyChild / TwistRelaxToChildFactor
                : 0f;
        }

        /// <summary>
        /// The <c>twist_relax</c> the declaration at <paramref name="rank"/> stated toward the joint's
        /// own parent, or null where the pair carries no entry at that rank.
        /// </summary>
        internal float? TwistRelaxDeclaredAt(int node, int parent, int rank)
            => parent >= 0 && TwistRelaxCopies.TryGetValue((node, parent), out var copies)
                && rank >= 0 && rank < copies.Count
                ? copies[rank] / TwistRelaxToParentFactor
                : null;

        private readonly Dictionary<int, float> twistOrientFallback = [];

        /// <summary>
        /// Gets whether every simulated node collides with the world, which is how the source's
        /// force-world-collision-on-all-nodes switch shows up (the switch itself leaves no flag bit).
        /// </summary>
        internal bool ForcesWorldCollisionOnAllNodes
            => NodeCount > StaticNodeCount && WorldCollisionNodes.Count == NodeCount - StaticNodeCount;

        /// <summary>
        /// Gets the ground friction shared by the world-colliding nodes, which is what the source authored
        /// as the cloth's default. Zero when the model has no world collision params.
        /// </summary>
        internal float DefaultGroundFriction => WorldCollisionFriction.Count > 0
            ? WorldCollisionFriction.Values.GroupBy(static f => f.Ground).OrderByDescending(static g => g.Count()).First().Key
            : 0f;

        /// <summary>
        /// Gets the scale every compiled <c>flRelaxationFactor</c> of <c>m_AnimStrayRadii</c> carries:
        /// <c>exp(-m_flDefaultThreadStretch)</c>, and 1 for a model that authors no thread stretch.
        /// </summary>
        private float StrayRelaxationScale => DefaultThreadStretch <= 0f ? 1f : MathF.Exp(-DefaultThreadStretch);

        /// <summary>
        /// Gets the authored relaxation factor of <paramref name="node"/>'s stray radius, or 1 for a node with none.
        /// </summary>
        internal float GetStrayRelaxationFactor(int node)
            => AnimStrayRadii.TryGetValue(node, out var stray)
                ? Math.Clamp(stray.RelaxationFactor / StrayRelaxationScale, 0f, 1f)
                : 1f;

        /// <summary>Gets the authored stray-radius stretchiness of <paramref name="node"/>, 0 for a node with none.</summary>
        internal float GetStrayStretchiness(int node)
            => AnimStrayRadii.ContainsKey(node) ? 1f - GetStrayRelaxationFactor(node) : 0f;

        /// <summary>
        /// Gets the node a chain joint's stray radius is recorded on: the joint node itself, else the first of its
        /// extruded proxies that carries one.
        /// </summary>
        internal int StrayRadiusNode(int node, string jointName)
        {
            if (AnimStrayRadii.ContainsKey(node))
            {
                return node;
            }

            for (var proxy = 0; proxy < CtrlNames.Length; proxy++)
            {
                if (AnimStrayRadii.ContainsKey(proxy) && IsChainProxyOf(CtrlNames[proxy], jointName))
                {
                    return proxy;
                }
            }

            return node;
        }

        /// <summary>
        /// Gets whether <paramref name="ctrlName"/> is <c>$cc&lt;joint&gt;_Ctr</c> or <c>$cc&lt;joint&gt;_&lt;index&gt;</c>.
        /// </summary>
        private static bool IsChainProxyOf(string ctrlName, string jointName)
        {
            if (!ctrlName.StartsWith("$cc", StringComparison.Ordinal))
            {
                return false;
            }

            var body = ctrlName.AsSpan(3);
            if (!body.StartsWith(jointName, StringComparison.Ordinal) || body.Length <= jointName.Length + 1
                || body[jointName.Length] != '_')
            {
                return false;
            }

            var suffix = body[(jointName.Length + 1)..];
            if (suffix.SequenceEqual("Ctr"))
            {
                return true;
            }

            foreach (var c in suffix)
            {
                if (!char.IsAsciiDigit(c))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Gets whether the cloth was authored as ModelDoc's <c>ImportedCloth</c> node: it carries a field only an imported
        /// node row writes and none of the ring, sheet or fit data other constructs produce.
        /// </summary>
        internal bool IsImportedCloth
            => (CtrlOsOffsets.Length > 0 || HasImportedNodeFields)
                && CtrlOffsets.Length == 0
                && Quads.Length == 0 && Tris.Length == 0
                && FitMatrixNodes.Count == 0
                && CtrlNames.Length > 0
                && !Array.Exists(CtrlNames, IsCompilerGeneratedNodeName);

        /// <summary>
        /// Gets both columns of every <c>m_CtrlOsOffsets</c> pair on a model without a surface that is not
        /// <see cref="IsImportedCloth"/> as a whole.
        /// </summary>
        internal IReadOnlySet<int> ImportedStripNodes => importedStripNodes ??= BuildImportedStripNodes();

        private HashSet<int>? importedStripNodes;

        private HashSet<int> BuildImportedStripNodes()
        {
            var strip = new HashSet<int>();
            if (CtrlOsOffsets.Length == 0 || IsImportedCloth || Quads.Length > 0 || Tris.Length > 0)
            {
                return strip;
            }

            foreach (var pair in CtrlOsOffsets)
            {
                if (pair.CtrlParent >= 0 && pair.CtrlParent < CtrlNames.Length && pair.CtrlChild >= 0 && pair.CtrlChild < CtrlNames.Length
                    && !IsCompilerGeneratedNodeName(CtrlNames[pair.CtrlParent]) && !IsCompilerGeneratedNodeName(CtrlNames[pair.CtrlChild]))
                {
                    strip.Add(pair.CtrlParent);
                    strip.Add(pair.CtrlChild);
                }
            }

            return strip;
        }

        private bool HasImportedNodeFields
        {
            get
            {
                var flags = StaticNodeFlags | DynamicNodeFlags;
                return ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) != 0 && (flags & NodeFlagGoalAttraction) == 0)
                    || FollowNodeLinks.Count > 0
                    || Array.Exists(LegacyStretchForce, static force => force != 0f);
            }
        }

        private static bool IsCompilerGeneratedNodeName(string? name)
            => string.IsNullOrEmpty(name)
                || name.StartsWith("$cc", StringComparison.Ordinal)
                || name.StartsWith("$cloth_m", StringComparison.Ordinal)
                || name.StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                || name.StartsWith("$cloth_root", StringComparison.Ordinal)
                || name.StartsWith("$ha_", StringComparison.Ordinal);

        /// <summary>
        /// Gets whether a node lies on an <c>m_Ropes</c> run of two or more nodes. The rope pass never starts or keeps a
        /// run on a node whose class byte is 1, which is what a <c>ClothNode</c> at the default alignment compiles to.
        /// </summary>
        internal bool IsRopeNode(int node)
        {
            if (ropeNodes is null)
            {
                ropeNodes = [];
                foreach (var run in RopeRuns)
                {
                    if (run.Length >= 2)
                    {
                        ropeNodes.UnionWith(run);
                    }
                }
            }

            return ropeNodes.Contains(node);
        }

        private HashSet<int>? ropeNodes;
    }
}

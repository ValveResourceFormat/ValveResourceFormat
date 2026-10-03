using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>
        /// Gets the <c>transform_alignment</c> and <c>node_base</c> references that compile to the node's
        /// <c>m_NodeBases</c> entry, or null when it has none. Alignment 3 returns X0 and Y0 as -1.
        /// </summary>
        internal (int TransformAlignment, NodeBasis References)? ClothNodeBasisPreset(int node)
        {
            if (!Fe.NodeBases.TryGetValue(node, out var basis))
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
            => Fe.NodeCount > Fe.StaticNodeCount && Fe.WorldCollisionNodes.Count == Fe.NodeCount - Fe.StaticNodeCount;

        /// <summary>
        /// Gets the ground friction shared by the world-colliding nodes, which is what the source authored
        /// as the cloth's default. Zero when the model has no world collision params.
        /// </summary>
        internal float DefaultGroundFriction => Fe.WorldCollisionFriction.Count > 0
            ? Fe.WorldCollisionFriction.Values.GroupBy(static f => f.Ground).OrderByDescending(static g => g.Count()).First().Key
            : 0f;

        /// <summary>
        /// Gets the scale every compiled <c>flRelaxationFactor</c> of <c>m_AnimStrayRadii</c> carries:
        /// <c>exp(-m_flDefaultThreadStretch)</c>, and 1 for a model that authors no thread stretch.
        /// </summary>
        private float StrayRelaxationScale => Fe.DefaultThreadStretch <= 0f ? 1f : MathF.Exp(-Fe.DefaultThreadStretch);

        /// <summary>
        /// Gets the authored relaxation factor of <paramref name="node"/>'s stray radius, or 1 for a node with none.
        /// </summary>
        internal float GetStrayRelaxationFactor(int node)
            => Fe.AnimStrayRadii.TryGetValue(node, out var stray)
                ? MathUtils.Saturate(stray.RelaxationFactor / StrayRelaxationScale)
                : 1f;

        /// <summary>Gets the authored stray-radius stretchiness of <paramref name="node"/>, 0 for a node with none.</summary>
        internal float GetStrayStretchiness(int node)
            => Fe.AnimStrayRadii.ContainsKey(node) ? 1f - GetStrayRelaxationFactor(node) : 0f;

        /// <summary>
        /// Gets the node a chain joint's stray radius is recorded on: the joint node itself, else the first of its
        /// extruded proxies that carries one.
        /// </summary>
        internal int StrayRadiusNode(int node, string jointName)
        {
            if (Fe.AnimStrayRadii.ContainsKey(node))
            {
                return node;
            }

            return StrayChainProxies.GetValueOrDefault(jointName, node);
        }

        /// <summary>
        /// Gets the first node with a stray radius named <c>$cc&lt;joint&gt;_Ctr</c> or <c>$cc&lt;joint&gt;_&lt;index&gt;</c>, keyed by
        /// the joint's name.
        /// </summary>
        private Dictionary<string, int> StrayChainProxies => strayChainProxies ??= BuildStrayChainProxies();

        private Dictionary<string, int>? strayChainProxies;

        private Dictionary<string, int> BuildStrayChainProxies()
        {
            var proxies = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var proxy = 0; proxy < Fe.CtrlNames.Length; proxy++)
            {
                var name = Fe.CtrlNames[proxy];
                if (!Fe.AnimStrayRadii.ContainsKey(proxy) || !name.StartsWith(RingNodePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var split = name.LastIndexOf('_');
                var suffix = name.AsSpan(split + 1);
                if (split < RingNodePrefix.Length || suffix.IsEmpty || (!suffix.SequenceEqual("Ctr") && !IsAsciiDigits(suffix)))
                {
                    continue;
                }

                proxies.TryAdd(name[RingNodePrefix.Length..split], proxy);
            }

            return proxies;

            static bool IsAsciiDigits(ReadOnlySpan<char> text)
            {
                foreach (var c in text)
                {
                    if (!char.IsAsciiDigit(c))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Gets whether the cloth was authored as ModelDoc's <c>ImportedCloth</c> node: it carries a field only an imported
        /// node row writes and none of the ring, sheet or fit data other constructs produce.
        /// </summary>
        internal bool IsImportedCloth
            => isImportedCloth ??= (Fe.CtrlOsOffsets.Length > 0 || HasImportedNodeFields)
                && Fe.CtrlOffsets.Length == 0
                && Fe.Quads.Length == 0 && Fe.Tris.Length == 0
                && Fe.FitMatrixNodes.Count == 0
                && Fe.CtrlNames.Length > 0
                && !Array.Exists(Fe.CtrlNames, IsCompilerGeneratedNodeName);

        private bool? isImportedCloth;

        /// <summary>
        /// Gets both columns of every <c>m_CtrlOsOffsets</c> pair on a model without a surface that is not
        /// <see cref="IsImportedCloth"/> as a whole.
        /// </summary>
        internal IReadOnlySet<int> ImportedStripNodes => importedStripNodes ??= BuildImportedStripNodes();

        private HashSet<int>? importedStripNodes;

        private HashSet<int> BuildImportedStripNodes()
        {
            var strip = new HashSet<int>();
            if (Fe.CtrlOsOffsets.Length == 0 || IsImportedCloth || Fe.Quads.Length > 0 || Fe.Tris.Length > 0)
            {
                return strip;
            }

            foreach (var pair in Fe.CtrlOsOffsets)
            {
                if (pair.CtrlParent >= 0 && pair.CtrlParent < Fe.CtrlNames.Length && pair.CtrlChild >= 0 && pair.CtrlChild < Fe.CtrlNames.Length
                    && !IsCompilerGeneratedNodeName(Fe.CtrlNames[pair.CtrlParent]) && !IsCompilerGeneratedNodeName(Fe.CtrlNames[pair.CtrlChild]))
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
                var flags = Fe.StaticNodeFlags | Fe.DynamicNodeFlags;
                return ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) != 0 && (flags & NodeFlagGoalAttraction) == 0)
                    || Fe.FollowNodeLinks.Count > 0
                    || Array.Exists(Fe.LegacyStretchForce, static force => force != 0f);
            }
        }

        /// <summary>The prefix of a ring node the compiler generates around a chain joint.</summary>
        private const string RingNodePrefix = "$cc";

        private static bool IsCompilerGeneratedNodeName(string? name)
            => string.IsNullOrEmpty(name)
                || name.StartsWith(RingNodePrefix, StringComparison.Ordinal)
                || name.StartsWith(ProxyNamePrefix, StringComparison.Ordinal)
                || name.StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                || name.StartsWith(ClothRootNodeName, StringComparison.Ordinal)
                || name.StartsWith("$ha_", StringComparison.Ordinal);

        /// <summary>
        /// Gets whether a node lies on an <c>m_Ropes</c> run of two or more nodes. The rope pass never starts or keeps a
        /// run on a node whose class byte is 1, which is what a <c>ClothNode</c> at the default alignment compiles to.
        /// </summary>
        internal bool IsRopeNode(int node)
            => (ropeNodes ??= [.. RopeRuns.Where(static run => run.Length >= 2).SelectMany(static run => run)]).Contains(node);

        private HashSet<int>? ropeNodes;
    }
}

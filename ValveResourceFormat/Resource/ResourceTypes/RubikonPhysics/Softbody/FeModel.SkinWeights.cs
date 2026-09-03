using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {

        // Reads an array of cloth faces (m_Quads/m_Tris), returning each face's nNode index list.
        static int[][] ReadNodeIndexArray(KVObject data, string key, int expectedLength)
        {
            var arr = data.GetArray(key);
            if (arr is null)
            {
                return [];
            }

            var faces = new List<int[]>(arr.Count);
            foreach (var face in arr)
            {
                var nodes = face.GetIntegerArray("nNode");
                if (nodes.Length >= expectedLength)
                {
                    faces.Add(nodes.Take(expectedLength).Select(static v => (int)v).ToArray());
                }
            }

            return [.. faces];
        }

        // m_SourceElems is the authored proxy mesh's own element list: four counts lead, one per element
        // arity, and the elements themselves follow grouped by arity - first the single corners, then the
        // pairs, the triangles and the quads, each a run of control-node indices in cyclic winding order.
        // Only arity three and up describe a face. Unlike m_Quads/m_Tris it survives even when the compiler
        // collapses the whole surface into rods, which is the only record of the authored topology for such
        // models.
        static (int[][] Faces, (int, int)[] Springs) ReadSourceElems(KVObject data)
        {
            if (!data.ContainsKey("m_SourceElems") || !data.IsNotBlobType("m_SourceElems"))
            {
                return ([], []);
            }

            var elems = data.GetIntegerArray("m_SourceElems");
            if (elems.Length < SourceElemArities)
            {
                return ([], []);
            }

            var counted = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                var count = elems[arity - 1];
                if (count < 0 || count > elems.Length)
                {
                    return ([], []);
                }

                counted += arity * (int)count;
            }

            // The counts have to account for the array exactly, or this is not the layout being read.
            if (counted != elems.Length)
            {
                return ([], []);
            }

            var faces = new List<int[]>();
            var springs = new List<(int, int)>();
            var read = SourceElemArities;
            for (var arity = 1; arity <= SourceElemArities; arity++)
            {
                for (var remaining = (int)elems[arity - 1]; remaining > 0; remaining--, read += arity)
                {
                    if (arity == 2)
                    {
                        var a = (int)elems[read];
                        var b = (int)elems[read + 1];
                        if (a != b)
                        {
                            springs.Add((a, b));
                        }

                        continue;
                    }

                    if (arity < 3)
                    {
                        continue;
                    }

                    var corners = new List<int>(arity);
                    for (var c = 0; c < arity; c++)
                    {
                        var node = (int)elems[read + c];
                        if (!corners.Contains(node))
                        {
                            corners.Add(node);
                        }
                    }

                    if (corners.Count >= 3)
                    {
                        faces.Add([.. corners]);
                    }
                }
            }

            return ([.. faces], [.. springs]);
        }

        const int SourceElemArities = 4;

        /// <summary>
        /// Gets the authored proxy-mesh faces recovered from <c>m_SourceElems</c>, as control-node index
        /// lists in winding order (four corners for a quad, three for a triangle).
        /// </summary>
        public int[][] SourceFaces { get; } = [];

        // A source element identified by its corner set alone. A chain surface is recorded once per
        // winding with its corners rotated freely, so membership is the only stable part of it.
        static string SurfaceElementKey(IEnumerable<int> corners)
        {
            var sorted = corners.ToArray();
            Array.Sort(sorted);
            return string.Join(',', sorted);
        }

        /// <summary>
        /// Gets the authored two-corner elements of <c>m_SourceElems</c>: the edges the source declared as
        /// explicit springs rather than as part of a face. Each is one authored <c>ClothSpring</c>, and each
        /// contributes its own rod on top of whatever the surface and the chains generate.
        /// </summary>
        public (int, int)[] SourceSprings { get; } = [];

        /// <summary>
        /// Gets whether the compiler anchored this cloth to a static root node of its own making, which is
        /// what it does for a proxy mesh that arrives with no skinning. Its absence means every sheet was
        /// skinned, so exporting one unskinned would add a node the original never had.
        /// </summary>
        public bool HasGeneratedClothRoot => Array.Exists(CtrlNames, static n => n == ClothRootNodeName);

        const string ClothRootNodeName = "$cloth_root";

        /// <summary>
        /// Returns the node pairs the compiler regenerates as <c>m_Rods</c> from <paramref name="faces"/>:
        /// every face edge plus every face diagonal, deduplicated.
        /// </summary>
        public static HashSet<(int, int)> DeriveRodsFromFaces(IEnumerable<int[]> faces)
        {
            var derived = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    for (var b = a + 1; b < face.Length; b++)
                    {
                        var (x, y) = face[a] < face[b] ? (face[a], face[b]) : (face[b], face[a]);
                        derived.Add((x, y));
                    }
                }
            }

            return derived;
        }

        /// <summary>
        /// Gets a value indicating whether this FeModel carries any control nodes.
        /// </summary>
        public bool HasData => CtrlNames.Length > 0;

        /// <summary>
        /// Gets a value indicating whether <c>m_SkelParents</c> was present in the compiled data. False on
        /// old-era compiles (and rope cloth), where <see cref="SkelParents"/> is synthesized from
        /// <c>m_Ropes</c>/<c>m_FollowNodes</c> or the skeleton instead.
        /// </summary>
        public bool HasCompiledSkelParents { get; }

        /// <summary>
        /// Returns whether a control-node name is an auto-generated cloth proxy node (not a real skeleton bone).
        /// </summary>
        public static bool IsProxyNodeName(string? name)
            => string.IsNullOrEmpty(name) || name.StartsWith('$');

        /// <summary>
        /// The prefix the compiler gives a control node it created for an authored free-standing
        /// <c>ClothNode</c> element (the element name follows). Distinct from the sheet-vertex
        /// (<c>$cloth_m&lt;N&gt;p&lt;M&gt;</c>) and chain-extrude (<c>$cc&lt;joint&gt;_&lt;n&gt;</c>) families.
        /// </summary>
        public const string FreeClothNodePrefix = "$cloth_node_";

        /// <summary>
        /// Gets or sets the names of the skeleton's real bones. Cloth extrusion does not always mark what it
        /// generates with the <c>$</c> prefix - a two-column strip names its second column after the bone it
        /// widens - so without the skeleton to compare against, a generated node is indistinguishable from a
        /// real one and gets authored as a chain joint the compiler then cannot resolve.
        /// </summary>
        public IReadOnlySet<string>? SkeletonBoneNames { get; set; }

        /// <summary>
        /// Gets or sets the control nodes of <see cref="GetCulledBoneCtrls"/> - bone ctrls the compiled
        /// skeleton culled - captured before their re-declared names are folded into
        /// <see cref="SkeletonBoneNames"/>.
        /// </summary>
        public IReadOnlySet<int>? CulledBoneCtrlNodes { get; set; }

        /// <summary>
        /// Gets or sets each skeleton bone's parent bone name. Used to orient chain links recovered from
        /// the rod mesh on compiles that ship no <c>m_SkelParents</c>: the rod evidence alone cannot tell
        /// parent from child on a strap anchored at both ends.
        /// </summary>
        public IReadOnlyDictionary<string, string?>? SkeletonBoneParents { get; set; }

        /// <summary>
        /// Rebuilds <see cref="SkelParents"/> from the model's own bone hierarchy, for cloth that ships
        /// neither <c>m_SkelParents</c> nor the <c>m_Ropes</c>/<c>m_FollowNodes</c> trail
        /// <see cref="BuildRopeParents"/> reads. A control node takes the nearest ancestor bone that is
        /// itself a control node. Does nothing once either of those two sources has produced a hierarchy.
        /// </summary>
        public void SetSkeletonParents(IReadOnlyDictionary<string, string?> boneParents)
        {
            if (SkelParents.Length > 0 || CtrlNames.Length == 0 || NodeCount <= 0)
            {
                return;
            }

            // Only for cloth built purely out of real bones. Once the compiler has generated nodes of its
            // own, they carry the hierarchy the skeleton cannot express, and imposing the bone tree on top
            // re-parents the surrounding network instead of completing it.
            foreach (var name in CtrlNames)
            {
                if (IsProxyNodeName(name))
                {
                    return;
                }
            }

            var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var node = 0; node < CtrlNames.Length && node < NodeCount; node++)
            {
                nodeByName.TryAdd(CtrlNames[node], node);
            }

            var parents = new int[NodeCount];
            Array.Fill(parents, -1);
            var parented = false;

            foreach (var (name, node) in nodeByName)
            {
                var ancestor = boneParents.GetValueOrDefault(name);
                while (ancestor is not null)
                {
                    if (nodeByName.TryGetValue(ancestor, out var parentNode) && parentNode != node)
                    {
                        parents[node] = parentNode;
                        parented = true;
                        break;
                    }

                    ancestor = boneParents.GetValueOrDefault(ancestor);
                }
            }

            if (parented)
            {
                SkelParents = parents;
            }
        }

        /// <summary>
        /// Returns whether a control node is generated by the cloth compiler rather than being a skeleton
        /// bone the chain can name as a joint.
        /// </summary>
        public bool IsGeneratedNodeName(string? name)
            => IsProxyNodeName(name)
                || (SkeletonBoneNames is not null && !SkeletonBoneNames.Contains(name!));

        /// <summary>
        /// Gets the control nodes that were authored as skeleton bones but culled from the compiled
        /// skeleton (unskinned cloth-only bones). A cloth construct can only reference a bone the
        /// document skeleton contains, so the export has to re-declare these as Bone nodes. Generated ring/strip members are excluded: they are the
        /// compiler's own extrude output (a CtrlOffsets child) or a strip's paired second column (a
        /// CtrlOsOffsets child), and re-declaring one collides with its regeneration.
        /// </summary>
        public List<(int Node, string Name)> GetCulledBoneCtrls()
        {
            var result = new List<(int Node, string Name)>();
            if (SkeletonBoneNames is null)
            {
                return result;
            }

            var generatedChildren = new HashSet<int>();
            foreach (var offset in CtrlOffsets)
            {
                generatedChildren.Add(offset.CtrlChild);
            }

            foreach (var pair in CtrlOsOffsets)
            {
                generatedChildren.Add(pair.CtrlChild);
            }

            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var name = CtrlNames[node];
                if (IsProxyNodeName(name) || SkeletonBoneNames.Contains(name)
                    || generatedChildren.Contains(node) || node >= InitPosePositions.Length)
                {
                    continue;
                }

                result.Add((node, name));
            }

            return result;
        }

        /// <summary>
        /// Whether the cloth drives any REAL (non auto-generated proxy) skeleton bone: at least one
        /// position-driven control node (index &gt;= <see cref="FirstPositionDrivenNode"/>) carries a real
        /// bone name. Those bones are back-solved from the simulated proxy nodes, whether the mechanism is
        /// <c>m_FitMatrices</c> or <c>m_CtrlOffsets</c> alone with no fit matrices at all. It is the signal
        /// that a reconstructed proxy mesh emits <c>back_solve_joints = true</c>, and it is a superset of
        /// <see cref="FitMatrixNodes"/> being non-empty.
        /// </summary>
        public bool DrivesRealBones
        {
            get
            {
                for (var i = FirstPositionDrivenNode; i < CtrlNames.Length; i++)
                {
                    if (!IsProxyNodeName(CtrlNames[i]))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Returns whether the node at <paramref name="node"/> is a static (pinned, invMass == 0) anchor.
        /// </summary>
        public bool IsStatic(int node)
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] == 0f;

        /// <summary>
        /// Walks the skeleton-parent chain from <paramref name="node"/> up to the first real (non
        /// auto-generated cloth proxy) control-node name. This is the skeleton bone that an auto-generated
        /// proxy node is anchored/skinned to.
        /// </summary>
        public string? ResolveSkinBone(int node)
        {
            var index = ResolveSkinBoneNode(node);
            return index >= 0 ? CtrlNames[index] : null;
        }

        // Same walk as ResolveSkinBone, returning the control-node index of the bone instead of its name.
        int ResolveSkinBoneNode(int node)
        {
            var p = node >= 0 && node < SkelParents.Length ? SkelParents[node] : -1;
            var guard = 0;
            while (p >= 0 && p < CtrlNames.Length && guard++ < 256)
            {
                if (!IsProxyNodeName(CtrlNames[p]))
                {
                    return p;
                }

                p = p < SkelParents.Length ? SkelParents[p] : -1;
            }

            return -1;
        }
    }
}

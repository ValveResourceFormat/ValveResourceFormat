using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>
        /// Marks the joints a second <c>ClothChain</c> re-declares, and the bone that chain is rooted at.
        /// </summary>
        /// <param name="chains">The reconstructed chains, edited in place.</param>
        private void MarkSecondDeclarations(List<BoneChain> chains)
        {
            foreach (var chain in chains)
            {
                if (chain.ExtrudeSides >= 1)
                {
                    continue;
                }

                var byNode = new Dictionary<int, BoneChainJoint>();
                var children = new Dictionary<int, List<BoneChainJoint>>();
                foreach (var joint in chain.Joints)
                {
                    byNode[joint.Node] = joint;
                    if (joint.ParentNode >= 0)
                    {
                        var siblings = GetOrAdd(children, joint.ParentNode);
                        siblings.Add(joint);
                    }
                }

                var roots = new List<BoneChainJoint>();
                foreach (var joint in chain.Joints)
                {
                    if (!joint.Simulated || joint.ParentNode < 0
                        || !TwistRelaxCopies.TryGetValue((joint.Node, joint.ParentNode), out var toParent)
                        || toParent.Count != 1 || toParent[0] <= 0f
                        || !children.TryGetValue(joint.Node, out var kids)
                        || !kids.Exists(kid => TwistRelaxCopies.TryGetValue((joint.Node, kid.Node), out var toChild)
                            && toChild.Count == 1 && toChild[0] == 0f))
                    {
                        continue;
                    }

                    var root = joint;
                    while (root.Simulated && root.ParentNode >= 0 && byNode.TryGetValue(root.ParentNode, out var above))
                    {
                        root = above;
                    }

                    if (!root.Simulated && !roots.Contains(root))
                    {
                        roots.Add(root);
                    }
                }

                foreach (var root in roots)
                {
                    var pending = new Queue<BoneChainJoint>();
                    pending.Enqueue(root);
                    while (pending.Count > 0)
                    {
                        var joint = pending.Dequeue();
                        joint.SecondDeclarationRoot = root.Name;
                        if (children.TryGetValue(joint.Node, out var kids))
                        {
                            foreach (var kid in kids)
                            {
                                pending.Enqueue(kid);
                            }
                        }
                    }
                }

                MarkVoicedSecondDeclarations(chain, byNode);
            }
        }

        /// <summary>
        /// Marks the runs whose second declaration stated its own <c>twist_relax</c>, whose pairs carry two twist copies.
        /// </summary>
        private void MarkVoicedSecondDeclarations(BoneChain chain, Dictionary<int, BoneChainJoint> byNode)
        {
            var doubled = new HashSet<int>();
            foreach (var (link, copies) in TwistRelaxCopies)
            {
                if (copies.Count == 2)
                {
                    doubled.Add(link.Orient);
                    doubled.Add(link.End);
                }
            }

            if (doubled.Count == 0)
            {
                return;
            }

            var members = chain.Joints.FindAll(joint => joint.SecondDeclarationRoot is null
                && doubled.Contains(joint.Node));
            var memberNodes = members.Select(static joint => joint.Node).ToHashSet();

            foreach (var root in members)
            {
                if (memberNodes.Contains(root.ParentNode) || root.Simulated)
                {
                    continue;
                }

                var run = new List<BoneChainJoint>();
                var pending = new Queue<BoneChainJoint>();
                pending.Enqueue(root);
                while (pending.Count > 0)
                {
                    var joint = pending.Dequeue();
                    run.Add(joint);
                    foreach (var kid in members)
                    {
                        if (kid.ParentNode == joint.Node)
                        {
                            pending.Enqueue(kid);
                        }
                    }
                }

                if (!run.Exists(static joint => joint.Simulated) || !FirstDeclarationIsStatic(run, byNode))
                {
                    continue;
                }

                foreach (var joint in run)
                {
                    joint.SecondDeclarationRoot = root.Name;
                }
            }
        }

        /// <summary>
        /// Gets whether the first of a doubled run's declarations left it unsimulated: a child-ward copy of 0 at rank 0.
        /// </summary>
        private bool FirstDeclarationIsStatic(List<BoneChainJoint> run, Dictionary<int, BoneChainJoint> byNode)
        {
            foreach (var joint in run)
            {
                if (!joint.Simulated || !byNode.ContainsKey(joint.ParentNode))
                {
                    continue;
                }

                foreach (var kid in run)
                {
                    if (kid.ParentNode == joint.Node
                        && TwistRelaxCopies.TryGetValue((joint.Node, kid.Node), out var toChild)
                        && toChild.Count == 2)
                    {
                        return toChild[0] == 0f;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the bones a chain declares only to spring its siblings together, as the last chain build found them. Such a
        /// bone anchors no chain of its own, so a cloth node parented to it still needs its own static declaration.
        /// </summary>
        internal IReadOnlySet<string> SiblingSpringHubs { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Gathers the chains of a ringless sibling group under the bone that parents them, marks the hub as springing its
        /// children together, and returns the hubs.
        /// </summary>
        /// <param name="chains">The reconstructed chains, edited in place.</param>
        private HashSet<string> MergeSiblingHubs(List<BoneChain> chains)
        {
            var siblingSpringHubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (SkeletonBoneParents is null)
            {
                return siblingSpringHubs;
            }

            var nodeOf = new Dictionary<string, int>(Fe.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < Fe.CtrlNames.Length; i++)
            {
                nodeOf.TryAdd(Fe.CtrlNames[i], i);
            }

            var groups = new Dictionary<string, List<BoneChain>>(StringComparer.OrdinalIgnoreCase);
            foreach (var chain in chains)
            {
                if (chain.ExtrudeSides >= 1 || chain.Joints.Exists(static joint => joint.RingNodes.Count > 0))
                {
                    continue;
                }

                if (chain.Joints.Find(static joint => joint.IsRoot) is not { Simulated: false } root
                    || !Fe.IsLockedToGoal(root.Node))
                {
                    continue;
                }

                if (SkeletonBoneParents.GetValueOrDefault(root.Name) is not { } hub
                    || !nodeOf.TryGetValue(hub, out var hubNode)
                    || chain.Joints.Exists(joint => joint.Node == hubNode))
                {
                    continue;
                }

                var members = GetOrAdd(groups, hub);
                members.Add(chain);
            }

            foreach (var (hub, members) in groups)
            {
                if (members.Count < 2)
                {
                    continue;
                }

                var hubNode = nodeOf[hub];
                var host = chains.Find(chain => chain.Joints.Exists(joint => joint.Node == hubNode));
                if (host is null)
                {
                    host = members[0];
                    host.RootBone = hub;
                    host.Joints.Insert(0, new BoneChainJoint
                    {
                        Node = hubNode,
                        Name = Fe.CtrlNames[hubNode],
                        ParentNode = -1,
                        InvMass = InverseMassOf(hubNode),
                    });
                }

                host.Joints.Find(joint => joint.Node == hubNode)!.ChildSiblingSpring = 1f;
                siblingSpringHubs.Add(Fe.CtrlNames[hubNode]);
                var hostNodes = host.Joints.Select(static joint => joint.Node).ToHashSet();

                foreach (var member in members)
                {
                    foreach (var joint in member.Joints)
                    {
                        if (joint.IsRoot && joint.Node != hubNode)
                        {
                            joint.ParentNode = hubNode;
                            joint.ParentName = hub;
                            joint.SpringsWithSiblings = true;
                        }

                        if (member != host && hostNodes.Add(joint.Node))
                        {
                            host.Joints.Add(joint);
                        }
                    }

                    if (member != host)
                    {
                        chains.Remove(member);
                    }
                }
            }

            return siblingSpringHubs;
        }

        /// <summary>
        /// The root bones of <paramref name="merged"/> whose one reconstructed declaration was compiled as two,
        /// each mapped to the children the second, ringless declaration keeps.
        /// </summary>
        private Dictionary<int, HashSet<int>> VersionSplitRoots(List<BoneChain> merged, Func<BoneChain, int> chainVersion)
        {
            var splits = new Dictionary<int, HashSet<int>>();
            foreach (var chain in merged)
            {
                if (chain.Joints.Count < 3 || chain.Joints[0].RingNodes.Count == 0)
                {
                    continue;
                }

                var root = chain.Joints[0].Node;
                var ringEnd = chain.Joints[0].RingNodes.Max();
                if (ringEnd >= Fe.StaticNodeCount)
                {
                    continue;
                }

                var staged = new List<int>();
                var unstaged = new List<int>();
                (int AfterRing, int Any) stagedFirst = (int.MaxValue, int.MaxValue);
                (int AfterRing, int Any) unstagedFirst = (int.MaxValue, int.MaxValue);
                var sharesRingBlock = false;
                var readable = true;
                foreach (var kid in chain.Joints)
                {
                    if (kid.ParentNode != root)
                    {
                        continue;
                    }

                    var subtree = new List<BoneChainJoint> { kid };
                    var members = new HashSet<int> { kid.Node };
                    foreach (var joint in chain.Joints)
                    {
                        if (joint != kid && members.Contains(joint.ParentNode))
                        {
                            members.Add(joint.Node);
                            subtree.Add(joint);
                        }
                    }

                    var rings = subtree.SelectMany(static joint => joint.RingNodes).Where(node => node < Fe.StaticNodeCount).ToList();
                    var ringBlock = rings.FindAll(node => Fe.AllowsRotation(node) == Fe.AllowsRotation(ringEnd));
                    sharesRingBlock |= ringBlock.Count > 0;
                    var first = (AfterRing: ringBlock.Where(node => node > ringEnd).DefaultIfEmpty(int.MaxValue).Min(),
                        Any: rings.DefaultIfEmpty(int.MaxValue).Min());

                    switch (ThinJointStagingOf(subtree))
                    {
                        case ThinJointStaging.Staged:
                            staged.Add(kid.Node);
                            stagedFirst = (Math.Min(stagedFirst.AfterRing, first.AfterRing), Math.Min(stagedFirst.Any, first.Any));
                            break;
                        case ThinJointStaging.Unstaged:
                            unstaged.Add(kid.Node);
                            unstagedFirst = (Math.Min(unstagedFirst.AfterRing, first.AfterRing), Math.Min(unstagedFirst.Any, first.Any));
                            break;
                        default:
                            readable = false;
                            break;
                    }
                }

                var (stagedAt, unstagedAt) = sharesRingBlock
                    ? (stagedFirst.AfterRing, unstagedFirst.AfterRing)
                    : (stagedFirst.Any, unstagedFirst.Any);

                if (!readable || staged.Count == 0 || unstaged.Count == 0 || stagedAt == unstagedAt
                    || chainVersion(chain) != 1)
                {
                    continue;
                }

                splits[root] = [.. stagedAt < unstagedAt ? unstaged : staged];
            }

            return splits;
        }

        private int NumberedRingCount(int jointNode)
            => ProxyRingOf(jointNode).Count(node => RingSuffixIndex(Fe.CtrlNames[node]) >= 0);
    }
}

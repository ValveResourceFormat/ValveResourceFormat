using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// Renames the joints of a cloth DMX to the control-node spelling of their bone, which the compiler registers the
    /// control node under.
    /// </summary>
    private static void RespellJointsAsClothControlNodes(DmeModel dmeModel, FeModel? feModel)
    {
        if (feModel is null || feModel.CtrlNames.Length == 0)
        {
            return;
        }

        var clothSpelling = new Dictionary<string, string>(feModel.CtrlNames.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var ctrlName in feModel.CtrlNames)
        {
            clothSpelling.TryAdd(ctrlName, ctrlName);
        }

        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint
                && clothSpelling.TryGetValue(joint.Name, out var spelling) && spelling != joint.Name)
            {
                joint.Name = spelling;
                joint.Transform.Name = spelling;
            }
        }
    }

    /// <summary>
    /// Adds the <see cref="CulledBones"/> to a cloth DMX's joint list at their rest transforms and registers them in
    /// <paramref name="boneIndexByName"/>, then nests the joints under their compiled parents.
    /// </summary>
    private void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName)
    {
        if (physAggregateData?.FeModel is { } feModel)
        {
            AppendCulledClothBoneJoints(dmeModel, boneIndexByName, feModel, CulledBones);
            NestProxyJointsUnderCompiledParents(dmeModel, feModel);
        }
    }

    /// <summary>
    /// Moves every joint of a cloth DMX whose compiled parent is another joint of the same DMX, but neither an ancestor nor
    /// a descendant, under that parent, keeping its model-space transform.
    /// </summary>
    internal static void NestProxyJointsUnderCompiledParents(DmeModel dmeModel, FeModel feModel)
    {
        if (!feModel.HasCompiledSkelParents)
        {
            return;
        }

        var nodeByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < feModel.CtrlNames.Length; node++)
        {
            nodeByName.TryAdd(feModel.CtrlNames[node], node);
        }

        var jointByName = new Dictionary<string, DmeJoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint)
            {
                jointByName.TryAdd(joint.Name, joint);
            }
        }

        var parentOf = new Dictionary<DmeJoint, object>();
        var pending = new Stack<object>();
        pending.Push(dmeModel);
        while (pending.Count > 0)
        {
            var dag = pending.Pop();
            var children = dag is DmeModel model ? model.Children : ((DmeDag)dag).Children;
            foreach (var child in children)
            {
                if (child is DmeJoint childJoint && parentOf.TryAdd(childJoint, dag))
                {
                    pending.Push(childJoint);
                }
            }
        }

        bool IsAncestor(DmeJoint candidate, DmeJoint joint)
        {
            for (var at = parentOf.GetValueOrDefault(joint); at is DmeJoint up; at = parentOf.GetValueOrDefault(up))
            {
                if (up == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        var moves = new List<(DmeJoint Joint, DmeJoint Parent)>();
        foreach (var (name, joint) in jointByName)
        {
            if (!nodeByName.TryGetValue(name, out var node) || node >= feModel.SkelParents.Length)
            {
                continue;
            }

            var parent = feModel.SkelParents[node];
            if (parent < 0 || parent >= feModel.CtrlNames.Length
                || !jointByName.TryGetValue(feModel.CtrlNames[parent], out var parentJoint)
                || parentJoint == joint || !parentOf.ContainsKey(joint) || !parentOf.ContainsKey(parentJoint)
                || IsAncestor(parentJoint, joint) || IsAncestor(joint, parentJoint))
            {
                continue;
            }

            moves.Add((joint, parentJoint));
        }

        if (moves.Count == 0)
        {
            return;
        }

        var world = DmeJointWorldTransforms(dmeModel);
        foreach (var (joint, parentJoint) in moves)
        {
            if (IsAncestor(joint, parentJoint))
            {
                continue;
            }

            var oldParent = parentOf[joint];
            var siblings = oldParent is DmeModel model ? model.Children : ((DmeDag)oldParent).Children;
            siblings.Remove(joint);

            var (position, rotation) = world[joint];
            var (parentPosition, parentRotation) = world[parentJoint];
            var inverse = Quaternion.Conjugate(parentRotation);
            joint.Transform.Position = Vector3.Transform(position - parentPosition, inverse);
            joint.Transform.Orientation = Quaternion.Normalize(inverse * rotation);
            parentJoint.Children.Add(joint);
            parentOf[joint] = parentJoint;
        }
    }

    /// <summary>
    /// Appends <paramref name="culledClothBones"/> as joints, nested under a joint of their compiled parent where the DMX
    /// has one, and registers them in <paramref name="boneIndexByName"/>.
    /// </summary>
    internal static void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName,
        FeModel feModel, IEnumerable<(int Node, string Name)> culledClothBones)
    {
        var appended = new List<(int Node, DmeJoint Joint)>();
        foreach (var (node, culledName) in culledClothBones)
        {
            if (node >= feModel.InitPosePositions.Length || boneIndexByName.ContainsKey(culledName))
            {
                continue;
            }

            var joint = new DmeJoint { Name = culledName };
            joint.Transform.Name = culledName;
            joint.Transform.Position = feModel.InitPosePositions[node];
            joint.Transform.Orientation = node < feModel.InitPoseRotations.Length
                ? feModel.InitPoseRotations[node]
                : Quaternion.Identity;
            boneIndexByName[culledName] = dmeModel.JointList.Count;
            dmeModel.JointList.Add(joint);
            appended.Add((node, joint));
        }

        if (appended.Count == 0)
        {
            return;
        }

        var world = feModel.HasCompiledSkelParents ? DmeJointWorldTransforms(dmeModel) : [];
        var jointByName = new Dictionary<string, DmeJoint>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in dmeModel.JointList)
        {
            if (element is DmeJoint joint)
            {
                jointByName.TryAdd(joint.Name, joint);
            }
        }

        foreach (var (_, joint) in appended)
        {
            world[joint] = (joint.Transform.Position, joint.Transform.Orientation);
        }

        foreach (var (node, joint) in appended)
        {
            var parent = feModel.HasCompiledSkelParents && node < feModel.SkelParents.Length ? feModel.SkelParents[node] : -1;
            if (parent < 0 || parent >= feModel.CtrlNames.Length
                || !jointByName.TryGetValue(feModel.CtrlNames[parent], out var parentJoint)
                || parentJoint == joint || !world.TryGetValue(parentJoint, out var parentWorld))
            {
                dmeModel.Children.Add(joint);
                continue;
            }

            var inverse = Quaternion.Conjugate(parentWorld.Rotation);
            joint.Transform.Position = Vector3.Transform(world[joint].Position - parentWorld.Position, inverse);
            joint.Transform.Orientation = Quaternion.Normalize(inverse * world[joint].Rotation);
            parentJoint.Children.Add(joint);
        }
    }

    /// <summary>The model-space transform of every joint reachable from the DAG roots of <paramref name="dmeModel"/>.</summary>
    private static Dictionary<DmeJoint, (Vector3 Position, Quaternion Rotation)> DmeJointWorldTransforms(DmeModel dmeModel)
    {
        var world = new Dictionary<DmeJoint, (Vector3 Position, Quaternion Rotation)>();
        var pending = new Stack<(DmeDag Dag, Vector3 Position, Quaternion Rotation)>();
        foreach (var child in dmeModel.Children)
        {
            if (child is DmeJoint joint)
            {
                pending.Push((joint, Vector3.Zero, Quaternion.Identity));
            }
        }

        while (pending.Count > 0)
        {
            var (dag, parentPosition, parentRotation) = pending.Pop();
            var position = parentPosition + Vector3.Transform(dag.Transform.Position, parentRotation);
            var rotation = Quaternion.Normalize(parentRotation * dag.Transform.Orientation);
            if (dag is not DmeJoint joint || !world.TryAdd(joint, (position, rotation)))
            {
                continue;
            }

            foreach (var child in dag.Children)
            {
                if (child is DmeJoint childJoint)
                {
                    pending.Push((childJoint, position, rotation));
                }
            }
        }

        return world;
    }
}

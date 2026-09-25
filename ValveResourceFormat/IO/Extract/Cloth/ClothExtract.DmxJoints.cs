using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    // A compiled model can carry two spellings of one bone: m_modelSkeleton's m_boneName and, for cloth
    // control nodes, the FeModel's m_CtrlName. Both are authored, and the compiler records each verbatim
    // because every bone lookup it does is case-insensitive. This export has one name per bone, so a bone
    // the compiler registers as a control node through a blend INDEX rather than a KV name string comes
    // back under the skeleton's spelling instead of the cloth data's.
    //
    // Re-spelling the joints of THIS sheet alone leaves everything else in place: the compiler still binds
    // each joint to the same bone case-insensitively, the model skeleton and every other DMX keep the
    // spelling they were compiled with, and the control node lands under the cloth data's name.
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
    /// Adds the culled cloth bones the vmdl re-declares (<see cref="AddCulledClothBones"/>) to a cloth
    /// DMX's joint list at their control node's rest transform, and registers them in
    /// <paramref name="boneIndexByName"/> so the sheet's skin weights can reference them.
    /// </summary>
    /// <remarks>
    /// The compiler parents a proxy joint's node to the nearest DAG ancestor joint of the same file that
    /// has a node, so a culled bone whose compiled parent is a joint of this DMX is nested under that
    /// joint; the rest stay root joints.
    /// </remarks>
    private void AppendCulledClothBoneJoints(DmeModel dmeModel, Dictionary<string, int> boneIndexByName)
    {
        if (physAggregateData?.FeModel is { } feModel)
        {
            AppendCulledClothBoneJoints(dmeModel, boneIndexByName, feModel, CulledBones);
            NestProxyJointsUnderCompiledParents(dmeModel, feModel);
        }
    }

    /// <summary>
    /// Moves every joint of a cloth DMX whose control node has a compiled parent that is another joint of the same
    /// DMX, but not one of its DAG ancestors, under that parent's joint at the local transform that keeps its
    /// model-space transform. A joint whose compiled parent sits in its own subtree is left where it is.
    /// </summary>
    /// <remarks>
    /// The compiler parents a proxy joint's node to the nearest DAG ancestor joint of the same file that has a node,
    /// so a joint the skeleton hangs elsewhere compiles with the wrong parent or none.
    /// </remarks>
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

    /// <inheritdoc cref="AppendCulledClothBoneJoints(DmeModel, Dictionary{string, int})"/>
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

    /// <summary>
    /// The model-space transform of every joint reachable from <paramref name="dmeModel"/>'s DAG roots,
    /// composed from the joints' local transforms.
    /// </summary>
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

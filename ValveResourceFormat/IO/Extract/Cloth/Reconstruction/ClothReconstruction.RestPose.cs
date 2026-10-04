using System.Collections.ObjectModel;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothReconstruction
{
    /// <summary>
    /// How far a control node's recorded rest position may sit from its bone's compiled bind pose and still correct it.
    /// </summary>
    private const float ClothRestBoneTolerance = 1.0f;

    /// <summary>
    /// How far bones beyond <see cref="ClothRestBoneTolerance"/> may stray from one shared offset or uniform scale of
    /// their compiled positions.
    /// </summary>
    private const float ClothRestBoneRigidSpread = 1e-2f;

    /// <summary>
    /// cos of half of 0.3 degrees: a recorded rest rotation further than this from the bind rotation turns the bone.
    /// </summary>
    private const float ClothProxyRestRotationTurn = 0.99999657f;

    /// <summary>
    /// Re-derives each bone's parent-space position from the cloth rest pose, root first: a control node's bone moves
    /// onto its recorded position when that is within <see cref="ClothRestBoneTolerance"/> of the compiled pose, and
    /// every other bone keeps its compiled offset from its corrected parent. Also fills the proxy dictionaries, and
    /// returns the <see cref="ChainExtrudeOrigins"/> or null.
    /// </summary>
    private Dictionary<string, Vector3>? BuildClothRestBonePositions(Skeleton skeleton)
    {
        var targets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        var rotationTargets = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < Fe.CtrlNames.Length; node++)
        {
            var name = Fe.CtrlNames[node];
            if (string.IsNullOrEmpty(name) || IsGeneratedNodeName(name))
            {
                continue;
            }

            if (node < Fe.InitPosePositions.Length)
            {
                targets.TryAdd(name, Fe.InitPosePositions[node]);
            }

            if (node < Fe.InitPoseRotations.Length)
            {
                rotationTargets.TryAdd(name, Fe.InitPoseRotations[node]);
            }
        }

        if (targets.Count == 0)
        {
            return null;
        }

        var maxApart = 0f;
        var maxApartUncapped = 0f;
        var farBones = new List<(string Name, Vector3 Compiled, Vector3 Target)>();
        void Measure(Bone bone, Vector3 compiledParent, Quaternion parentRotation)
        {
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);
            var rotation = parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                maxApartUncapped = MathF.Max(maxApartUncapped, apart);
                if (apart <= ClothRestBoneTolerance)
                {
                    maxApart = MathF.Max(maxApart, apart);
                }
                else
                {
                    farBones.Add((bone.Name, compiled, target));
                }
            }

            foreach (var child in bone.Children)
            {
                Measure(child, compiled, rotation);
            }
        }

        foreach (var root in skeleton.Roots)
        {
            Measure(root, Vector3.Zero, Quaternion.Identity);
        }

        var farOffsetsAreRigid = farBones.Count > 1;
        foreach (var (_, compiled, target) in farBones)
        {
            farOffsetsAreRigid &= Vector3.Distance(target - compiled, farBones[0].Target - farBones[0].Compiled)
                <= ClothRestBoneRigidSpread;
        }

        var compiledSquared = farBones.Sum(static bone => Vector3.Dot(bone.Compiled, bone.Compiled));
        var farScale = compiledSquared > 0f
            ? farBones.Sum(static bone => Vector3.Dot(bone.Target, bone.Compiled)) / compiledSquared
            : 1f;
        var farOffsetsAreScaled = farBones.Count > 1
            && farBones.TrueForAll(bone => Vector3.Distance(bone.Target, bone.Compiled * farScale) <= ClothRestBoneRigidSpread);

        Dictionary<string, Vector3>? origins = null;
        if (farOffsetsAreScaled && !farOffsetsAreRigid)
        {
            origins = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, compiled, _) in farBones)
            {
                origins.TryAdd(name, compiled);
            }
        }

        var turned = ProxyRestRotations(skeleton.Roots, rotationTargets, RestPose.ProxyBoneRotations);

        if (maxApart > 0f)
        {
            ProxyRestPositions(skeleton.Roots, targets, ReadOnlyDictionary<string, Quaternion>.Empty, RestPose.BonePositions,
                ClothRestBoneTolerance);
        }

        if (maxApartUncapped > 0f || turned.Count > 0)
        {
            ProxyRestPositions(skeleton.Roots, targets, turned, RestPose.ProxyBonePositions);
        }

        return origins;
    }

    /// <summary>
    /// The parent-local positions that put every bone with a recorded rest position on it, root first, while every other
    /// bone keeps its compiled offset composed through the <paramref name="turned"/> world rotations. Only positions that
    /// change are written to <paramref name="into"/>. Targets further than <paramref name="maxApart"/> from the compiled
    /// position are ignored.
    /// </summary>
    internal static void ProxyRestPositions(IEnumerable<Bone> roots, IReadOnlyDictionary<string, Vector3> targets,
        IReadOnlyDictionary<string, Quaternion> turned, Dictionary<string, Vector3> into, float maxApart = float.PositiveInfinity)
    {
        void Walk(Bone bone, Vector3 parentPosition, Quaternion parentRotation, Vector3 compiledParent,
            Quaternion compiledParentRotation)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, compiledParentRotation);
            var rotation = turned.TryGetValue(bone.Name, out var turnedRotation) ? turnedRotation : parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart > 0f && apart <= maxApart)
                {
                    world = target;
                }
            }

            var local = Vector3.Transform(world - parentPosition, Quaternion.Conjugate(parentRotation));
            if (local != bone.Position)
            {
                into[bone.Name] = local;
            }

            foreach (var child in bone.Children)
            {
                Walk(child, world, rotation, compiled, compiledParentRotation * bone.Angle);
            }
        }

        foreach (var root in roots)
        {
            Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity);
        }
    }

    /// <summary>
    /// The parent-local rotations that turn every bone with a recorded rest rotation onto it, root first, while every other
    /// bone keeps its compiled world rotation. Only a turned bone and its children are written to <paramref name="into"/>;
    /// the returned map holds their world rotations.
    /// </summary>
    internal static Dictionary<string, Quaternion> ProxyRestRotations(IEnumerable<Bone> roots,
        IReadOnlyDictionary<string, Quaternion> targets, Dictionary<string, Quaternion> into)
    {
        var turned = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);

        void Turn(Bone bone, Quaternion compiledParent, Quaternion parentRotation, bool parentTurned)
        {
            var compiled = compiledParent * bone.Angle;
            var world = compiled;
            var isTurned = targets.TryGetValue(bone.Name, out var target)
                && MathF.Abs(Quaternion.Dot(Quaternion.Normalize(compiled), Quaternion.Normalize(target))) < ClothProxyRestRotationTurn;
            if (isTurned)
            {
                world = target;
            }

            if (isTurned || parentTurned)
            {
                into[bone.Name] = Quaternion.Normalize(Quaternion.Conjugate(parentRotation) * world);
                turned[bone.Name] = world;
            }

            foreach (var child in bone.Children)
            {
                Turn(child, compiled, world, isTurned);
            }
        }

        foreach (var root in roots)
        {
            Turn(root, Quaternion.Identity, Quaternion.Identity, parentTurned: false);
        }

        return turned;
    }
}

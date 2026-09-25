using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

internal sealed partial class ClothExtract
{
    /// <summary>
    /// How far a control node's recorded rest position may sit from its bone's compiled bind pose and still correct it.
    /// </summary>
    private const float ClothRestBoneTolerance = 1.0f;

    /// <summary>
    /// How far far control bones may sit from one uniform scale of their
    /// compiled positions and still read as a scaled skeleton.
    /// </summary>
    private const float ClothRestBoneRigidSpread = 1e-2f;

    /// <summary>
    /// Re-derives each bone's parent-space position from the cloth rest pose, root first: a control node's bone is moved
    /// onto its recorded position, judged against the compiled pose, and every bone keeps its compiled offset from its
    /// corrected parent. Also fills the proxy dictionaries and <see cref="FeModel.ChainExtrudeOrigins"/>.
    /// </summary>
    private void BuildClothRestBonePositions(FeModel feModel)
    {
        Debug.Assert(model is not null, "model required for cloth rest bones");

        var targets = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < feModel.CtrlNames.Length && node < feModel.InitPosePositions.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (!string.IsNullOrEmpty(name) && !feModel.IsGeneratedNodeName(name))
            {
                targets.TryAdd(name, feModel.InitPosePositions[node]);
            }
        }

        if (targets.Count == 0)
        {
            return;
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
                maxApartUncapped = Math.Max(maxApartUncapped, apart);
                if (apart <= ClothRestBoneTolerance)
                {
                    maxApart = Math.Max(maxApart, apart);
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

        foreach (var root in model.Skeleton.Roots)
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

        if (farOffsetsAreScaled && !farOffsetsAreRigid)
        {
            var origins = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, compiled, _) in farBones)
            {
                origins.TryAdd(name, compiled);
            }

            feModel.ChainExtrudeOrigins = origins;
        }

        var rotationTargets = new Dictionary<string, Quaternion>(StringComparer.OrdinalIgnoreCase);
        for (var node = 0; node < feModel.CtrlNames.Length && node < feModel.InitPoseRotations.Length; node++)
        {
            var name = feModel.CtrlNames[node];
            if (!string.IsNullOrEmpty(name) && !feModel.IsGeneratedNodeName(name))
            {
                rotationTargets.TryAdd(name, feModel.InitPoseRotations[node]);
            }
        }

        var turned = ProxyRestRotations(model.Skeleton.Roots, rotationTargets, ProxyRestBoneRotations);

        void Walk(Bone bone, Vector3 parentPosition, Vector3 compiledParent, Quaternion parentRotation)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, parentRotation);

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart > 0f && apart <= ClothRestBoneTolerance)
                {
                    world = target;
                }
            }

            var local = Vector3.Transform(world - parentPosition, Quaternion.Conjugate(parentRotation));
            if (local != bone.Position)
            {
                RestBonePositions[bone.Name] = local;
            }

            foreach (var child in bone.Children)
            {
                Walk(child, world, compiled, parentRotation * bone.Angle);
            }
        }

        if (maxApart > 0f)
        {
            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Vector3.Zero, Quaternion.Identity);
            }
        }

        if (maxApartUncapped > 0f || turned.Count > 0)
        {
            ProxyRestPositions(model.Skeleton.Roots, targets, turned, ProxyRestBonePositions);
        }
    }

    /// <summary>
    /// The parent-local positions that put every bone with a recorded rest position on it, root first, while every other
    /// bone keeps its compiled offset composed through the <paramref name="turned"/> world rotations. Only positions that
    /// change are written to <paramref name="into"/>.
    /// </summary>
    internal static void ProxyRestPositions(IEnumerable<Bone> roots, IReadOnlyDictionary<string, Vector3> targets,
        IReadOnlyDictionary<string, Quaternion> turned, Dictionary<string, Vector3> into)
    {
        void Walk(Bone bone, Vector3 parentPosition, Quaternion parentRotation, Vector3 compiledParent,
            Quaternion compiledParentRotation)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, compiledParentRotation);
            var rotation = turned.TryGetValue(bone.Name, out var turnedRotation) ? turnedRotation : parentRotation * bone.Angle;

            if (targets.TryGetValue(bone.Name, out var target) && Vector3.Distance(compiled, target) > 0f)
            {
                world = target;
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

    /// <summary>
    /// cos of half of 0.3 degrees: a recorded rest rotation further than this from the bind rotation turns the bone.
    /// </summary>
    private const float ClothProxyRestRotationTurn = 0.99999657f;
}

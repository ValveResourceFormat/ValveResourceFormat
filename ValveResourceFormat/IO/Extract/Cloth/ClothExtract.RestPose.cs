using System.Diagnostics;
using System.Globalization;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
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

    /// <summary>Six-decimal grid steps searched on each side of a joint's real-valued origin or angles, per component.</summary>
    private const int ClothChainOriginSearchSteps = 12;

    /// <summary>Degrees a landed angle may sit from the printed one; a rotation needing more keeps the printed angles.</summary>
    private const float ClothChainAngleSlack = 1e-3f;

    /// <summary>The compiler's degrees-to-half-angle factor, <c>f32(pi / 360)</c>.</summary>
    private const float CompilerHalfDegreesToRadians = (float)(Math.PI * 2 / 360.0 * 0.5);

    /// <summary>A world transform as the compiler's chain rest pose carries it.</summary>
    internal readonly record struct CompilerTransform(Vector3 Position, float Scale, Quaternion Rotation);

    /// <summary>
    /// Re-solves the Bone origin and angles of every ClothChain joint against the compiler's chain rest pose, top-down (see
    /// <see cref="ComposeChainBone"/>).
    /// </summary>
    private void BuildClothChainBoneOrigins(FeModel feModel)
    {
        Debug.Assert(model is not null, "model required for cloth chain bones");

        var targets = new Dictionary<string, (Vector3 Position, Quaternion Rotation)>(StringComparer.OrdinalIgnoreCase);
        foreach (var chain in feModel.BuildBoneChains())
        {
            foreach (var joint in chain.Joints)
            {
                if (joint.Node >= 0 && joint.Node < feModel.InitPosePositions.Length
                    && joint.Node < feModel.InitPoseRotations.Length)
                {
                    targets.TryAdd(joint.Name, (feModel.InitPosePositions[joint.Node], feModel.InitPoseRotations[joint.Node]));
                }
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        void Walk(Bone bone, CompilerTransform? parent)
        {
            if (ModelExtract.IsCompilerOwnedClothBone(bone))
            {
                foreach (var child in bone.Children)
                {
                    Walk(child, parent);
                }

                return;
            }

            var isJoint = targets.TryGetValue(bone.Name, out var target);
            var world = ComposeChainBone(parent, ModelExtract.BonePosition(bone, RestBonePositions),
                EntityTransformHelper.ToEulerAngles(bone.Angle), isJoint ? target.Position : null,
                isJoint ? target.Rotation : null, out var landed, out var landedAngles);
            if (landed is { } origin)
            {
                ChainBoneOrigins[bone.Name] = origin;
                RelandedJoints.Add(bone.Name);
            }

            if (landedAngles is { } angles)
            {
                ChainBoneAngles[bone.Name] = angles;
                RelandedJoints.Add(bone.Name);
            }

            foreach (var child in bone.Children)
            {
                Walk(child, world);
            }
        }

        foreach (var root in model.Skeleton.Roots)
        {
            Walk(root, null);
        }
    }

    /// <summary>
    /// One Bone of the compiler's ClothChain rest pose: its printed <paramref name="origin"/> and <paramref name="angles"/>
    /// composed onto <paramref name="parent"/>. For a chain joint, <paramref name="landedAngles"/> and
    /// <paramref name="landedOrigin"/> are the six-decimal values that land it on its target rotation and position, null
    /// where it already lands or no grid value does.
    /// </summary>
    internal static CompilerTransform ComposeChainBone(CompilerTransform? parent, Vector3 origin, Vector3 angles,
        Vector3? targetPosition, Quaternion? targetRotation, out Vector3? landedOrigin, out Vector3? landedAngles)
    {
        var printedAngles = CompilerTextFloat(angles);
        var rotation = CompilerAngleQuaternion(printedAngles);
        landedOrigin = null;
        landedAngles = null;

        if (parent is { } rotated && targetRotation is { } wanted
            && !SameRotationBits(CompilerComposeRotation(rotated, rotation), wanted)
            && TryLandChainAngles(rotated, wanted, out var solved)
            && Math.Abs(solved.X - printedAngles.X) <= ClothChainAngleSlack
            && Math.Abs(solved.Y - printedAngles.Y) <= ClothChainAngleSlack
            && Math.Abs(solved.Z - printedAngles.Z) <= ClothChainAngleSlack)
        {
            landedAngles = solved;
            rotation = CompilerAngleQuaternion(solved);
        }

        var world = CompilerCompose(parent, CompilerTextFloat(origin), rotation);

        if (parent is { } resolvedParent && targetPosition is { } position && world.Position != position
            && Vector3.Distance(world.Position, position) <= ClothRestBoneTolerance
            && TryLandChainOrigin(resolvedParent, position, out var landed))
        {
            landedOrigin = landed;
            world = CompilerCompose(parent, landed, rotation);
        }

        return world;
    }

    private static bool SameRotationBits(Quaternion a, Quaternion b)
        => BitConverter.SingleToUInt32Bits(a.X) == BitConverter.SingleToUInt32Bits(b.X)
            && BitConverter.SingleToUInt32Bits(a.Y) == BitConverter.SingleToUInt32Bits(b.Y)
            && BitConverter.SingleToUInt32Bits(a.Z) == BitConverter.SingleToUInt32Bits(b.Z)
            && BitConverter.SingleToUInt32Bits(a.W) == BitConverter.SingleToUInt32Bits(b.W);

    /// <summary>
    /// Finds the six-decimal (pitch, yaw, roll) nearest the real-valued local rotation whose rotation composed onto
    /// <paramref name="parent"/> is exactly <paramref name="target"/>, and which the six-decimal print keeps.
    /// </summary>
    internal static bool TryLandChainAngles(CompilerTransform parent, Quaternion target, out Vector3 angles)
    {
        double ux = -parent.Rotation.X, uy = -parent.Rotation.Y, uz = -parent.Rotation.Z, uw = parent.Rotation.W;
        double tx = target.X, ty = target.Y, tz = target.Z, tw = target.W;
        var lx = uw * tx + ux * tw + uy * tz - uz * ty;
        var ly = uw * ty - ux * tz + uy * tw + uz * tx;
        var lz = uw * tz + ux * ty - uy * tx + uz * tw;
        var lw = uw * tw - ux * tx - uy * ty - uz * tz;
        var m00 = 1 - 2 * (ly * ly + lz * lz);
        var m10 = 2 * (lx * ly + lz * lw);
        var m20 = 2 * (lx * lz - ly * lw);
        var m21 = 2 * (ly * lz + lx * lw);
        var m22 = 1 - 2 * (lx * lx + ly * ly);
        var (pitches, pitchSin, pitchCos) = AngleCandidates(double.RadiansToDegrees(Math.Asin(Math.Clamp(-m20, -1, 1))));
        var (yaws, yawSin, yawCos) = AngleCandidates(double.RadiansToDegrees(Math.Atan2(m10, m00)));
        var (rolls, rollSin, rollCos) = AngleCandidates(double.RadiansToDegrees(Math.Atan2(m21, m22)));

        angles = default;
        var bestDistance = int.MaxValue;
        for (var ip = 0; ip < pitches.Length; ip++)
        {
            for (var iy = 0; iy < yaws.Length; iy++)
            {
                for (var ir = 0; ir < rolls.Length; ir++)
                {
                    var distance = Math.Abs(ip - ClothChainOriginSearchSteps) + Math.Abs(iy - ClothChainOriginSearchSteps)
                        + Math.Abs(ir - ClothChainOriginSearchSteps);
                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    var local = CompilerHalfAngleQuaternion(pitchSin[ip], pitchCos[ip], yawSin[iy], yawCos[iy], rollSin[ir],
                        rollCos[ir]);
                    var candidate = new Vector3(pitches[ip], yaws[iy], rolls[ir]);
                    if (SameRotationBits(CompilerComposeRotation(parent, local), target)
                        && CompilerTextFloat(candidate) == candidate)
                    {
                        angles = candidate;
                        bestDistance = distance;
                    }
                }
            }
        }

        return bestDistance != int.MaxValue;

        static (float[] Values, float[] Sin, float[] Cos) AngleCandidates(double centre)
        {
            var values = GridCandidates(centre);
            var sin = new float[values.Length];
            var cos = new float[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                var half = values[i] * CompilerHalfDegreesToRadians;
                sin[i] = (float)Math.Sin(half);
                cos[i] = (float)Math.Cos(half);
            }

            return (values, sin, cos);
        }
    }

    /// <summary>The float32 the compiler parses back from a value the document prints with six decimals.</summary>
    internal static float CompilerTextFloat(float value)
        => (float)double.Parse(((double)value).ToString("F6", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <inheritdoc cref="CompilerTextFloat(float)"/>
    internal static Vector3 CompilerTextFloat(Vector3 value)
        => new(CompilerTextFloat(value.X), CompilerTextFloat(value.Y), CompilerTextFloat(value.Z));

    /// <summary>
    /// The compiler's AngleQuaternion over (pitch, yaw, roll) degrees: float32 half angles,
    /// double sine and cosine cast back to float32, in its product order.
    /// </summary>
    internal static Quaternion CompilerAngleQuaternion(Vector3 degrees)
    {
        var halfPitch = degrees.X * CompilerHalfDegreesToRadians;
        var halfYaw = degrees.Y * CompilerHalfDegreesToRadians;
        var halfRoll = degrees.Z * CompilerHalfDegreesToRadians;
        return CompilerHalfAngleQuaternion((float)Math.Sin(halfPitch), (float)Math.Cos(halfPitch), (float)Math.Sin(halfYaw),
            (float)Math.Cos(halfYaw), (float)Math.Sin(halfRoll), (float)Math.Cos(halfRoll));
    }

    /// <summary>The compiler's AngleQuaternion product over the sines and cosines of the three half angles.</summary>
    private static Quaternion CompilerHalfAngleQuaternion(float sp, float cp, float sy, float cy, float sr, float cr)
    {
        var srXcp = sr * cp;
        var crXsp = cr * sp;
        var crXcp = cr * cp;
        var srXsp = sr * sp;
        return new Quaternion(srXcp * cy - crXsp * sy, crXsp * cy + srXcp * sy, crXcp * sy - srXsp * cy,
            crXcp * cy + srXsp * sy);
    }

    /// <summary>
    /// The compiler's CTransform concat of <paramref name="parent"/> with a Bone's origin at unit
    /// scale and its rotation, or the Bone's own transform at a root: the origin rotated in its operation order, and a
    /// hemisphere-aligned Hamilton product renormalized in dot-product order.
    /// </summary>
    internal static CompilerTransform CompilerCompose(CompilerTransform? parent, Vector3 origin, Quaternion rotation)
    {
        if (parent is not { } p)
        {
            return new CompilerTransform(origin, 1f, rotation);
        }

        return new CompilerTransform(CompilerComposePosition(p, origin), p.Scale, CompilerComposeRotation(p, rotation));
    }

    private static Quaternion CompilerComposeRotation(CompilerTransform parent, Quaternion rotation)
    {
        var q = parent.Rotation;
        var l = rotation;
        var plusX = q.X + l.X;
        var plusY = q.Y + l.Y;
        var plusZ = q.Z + l.Z;
        var plusW = q.W + l.W;
        var minusX = q.X - l.X;
        var minusY = q.Y - l.Y;
        var minusZ = q.Z - l.Z;
        var minusW = q.W - l.W;
        if ((plusX * plusX + plusY * plusY) + (plusZ * plusZ + plusW * plusW)
            < (minusX * minusX + minusY * minusY) + (minusZ * minusZ + minusW * minusW))
        {
            l = new Quaternion(0f - l.X, 0f - l.Y, 0f - l.Z, 0f - l.W);
        }

        var x = ((q.W * l.X + q.X * l.W) + q.Y * l.Z) + q.Z * -l.Y;
        var y = ((q.W * l.Y + q.X * -l.Z) + q.Y * l.W) + q.Z * l.X;
        var z = ((q.W * l.Z + q.X * l.Y) + q.Y * -l.X) + q.Z * l.W;
        var w = ((q.W * l.W + q.X * -l.X) + q.Y * -l.Y) + q.Z * -l.Z;
        var norm = MathF.Sqrt((x * x + y * y) + (z * z + w * w));
        return norm == 0f ? Quaternion.Identity : new Quaternion(x / norm, y / norm, z / norm, w / norm);
    }

    private static Vector3 CompilerComposePosition(CompilerTransform parent, Vector3 origin)
    {
        var q = parent.Rotation;
        var v0 = origin.Z * q.Y - origin.Y * q.Z;
        var v1 = origin.X * q.Z - origin.Z * q.X;
        var v2 = origin.Y * q.X - origin.X * q.Y;
        var t0 = v0 + v0;
        var t1 = v1 + v1;
        var t2 = v2 + v2;
        return new Vector3(
            ((t2 * q.Y - t1 * q.Z) + (q.W * t0 + origin.X)) * parent.Scale + parent.Position.X,
            ((t0 * q.Z - t2 * q.X) + (q.W * t1 + origin.Y)) * parent.Scale + parent.Position.Y,
            ((t1 * q.X - t0 * q.Y) + (q.W * t2 + origin.Z)) * parent.Scale + parent.Position.Z);
    }

    /// <summary>
    /// Finds the six-decimal origin nearest the real-valued solution whose position composed onto
    /// <paramref name="parent"/> is exactly <paramref name="target"/>, and which the six-decimal print keeps.
    /// </summary>
    internal static bool TryLandChainOrigin(CompilerTransform parent, Vector3 target, out Vector3 origin)
    {
        double qx = parent.Rotation.X, qy = parent.Rotation.Y, qz = parent.Rotation.Z, qw = parent.Rotation.W;
        var dx = (target.X - (double)parent.Position.X) / parent.Scale;
        var dy = (target.Y - (double)parent.Position.Y) / parent.Scale;
        var dz = (target.Z - (double)parent.Position.Z) / parent.Scale;
        var tx = 2 * (qz * dy - qy * dz);
        var ty = 2 * (qx * dz - qz * dx);
        var tz = 2 * (qy * dx - qx * dy);
        float[] xs = GridCandidates(dx + qw * tx + (qz * ty - qy * tz));
        float[] ys = GridCandidates(dy + qw * ty + (qx * tz - qz * tx));
        float[] zs = GridCandidates(dz + qw * tz + (qy * tx - qx * ty));

        origin = default;
        var bestDistance = int.MaxValue;
        for (var ix = 0; ix < xs.Length; ix++)
        {
            for (var iy = 0; iy < ys.Length; iy++)
            {
                for (var iz = 0; iz < zs.Length; iz++)
                {
                    var candidate = new Vector3(xs[ix], ys[iy], zs[iz]);
                    var distance = Math.Abs(ix - ClothChainOriginSearchSteps) + Math.Abs(iy - ClothChainOriginSearchSteps)
                        + Math.Abs(iz - ClothChainOriginSearchSteps);
                    if (distance < bestDistance && CompilerComposePosition(parent, candidate) == target
                        && CompilerTextFloat(candidate) == candidate)
                    {
                        origin = candidate;
                        bestDistance = distance;
                    }
                }
            }
        }

        return bestDistance != int.MaxValue;
    }

    /// <summary>The six-decimal values within <see cref="ClothChainOriginSearchSteps"/> grid steps of <paramref name="centre"/>.</summary>
    private static float[] GridCandidates(double centre)
    {
        var middle = (long)Math.Round(centre * 1e6);
        var values = new float[2 * ClothChainOriginSearchSteps + 1];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (float)((middle + i - ClothChainOriginSearchSteps) / 1e6);
        }

        return values;
    }
}

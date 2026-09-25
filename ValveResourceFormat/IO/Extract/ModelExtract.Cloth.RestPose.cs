using System.Diagnostics;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    // How far a control node's recorded rest position may sit from the same bone's compiled bind pose and
    // still be read as the same pose at better precision. Past a whole unit the node sits somewhere else
    // entirely and that bone keeps its compiled transform.
    const float ClothRestBoneTolerance = 1.0f;

    // And how far it has to sit before the disagreement is worth acting on: a control node whose bone
    // already accumulates to its recorded position exactly keeps its compiled transform.
    const float ClothRestBoneFloor = 0f;

    // How far apart two control bones' corrections may sit and still be read as ONE pose difference.
    // A proxy mesh authored in a different pose moves as a unit, and it takes at least two bones
    // agreeing to witness that; one bone on its own is an isolated disagreement, not a pose, and the
    // exporter does not guess at it.
    const float ClothRestBoneRigidSpread = 1e-2f;

    // The correction runs per MODEL when any bone disagrees at all. Once enabled, every bone past the
    // per-bone floor moves together: derived rest shapes span bones on both sides of any per-bone cut,
    // so a partial correction leaves them mixed.
    const float ClothRestBoneModelGate = 0f;

    // The gate and floor of the proxy dictionary alone, the one the cloth import reads.
    const float ClothProxyRestBoneModelGate = 0f;
    const float ClothProxyRestBoneFloor = 0f;

    // Re-derives each bone's parent-space position from the cloth rest pose, root first: a bone the
    // FeModel registers as a control node is put back on its recorded world position, and every bone under
    // it keeps its compiled offset from that corrected parent, so a correction propagates down the
    // hierarchy exactly as the authored transform chain would. Whether a bone qualifies is judged on the
    // COMPILED pose, not the corrected one - the disagreement accumulates down a chain, and measuring
    // against an already-corrected parent would only ever see one link's worth of it.
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

        var turned = ProxyRestRotations(model.Skeleton.Roots, rotationTargets, ClothProxyRestBoneRotations);

        void Walk(Bone bone, Vector3 parentPosition, Quaternion parentRotation, Vector3 compiledParent,
            Quaternion compiledParentRotation, Dictionary<string, Vector3> into, float tolerance, float floor,
            bool proxy)
        {
            var world = parentPosition + Vector3.Transform(bone.Position, parentRotation);
            var compiled = compiledParent + Vector3.Transform(bone.Position, compiledParentRotation);
            var compiledRotation = compiledParentRotation * bone.Angle;
            var rotation = proxy && turned.TryGetValue(bone.Name, out var turnedRotation)
                ? turnedRotation
                : parentRotation * (proxy && ClothProxyRestBoneRotations.TryGetValue(bone.Name, out var local0) ? local0 : bone.Angle);

            if (targets.TryGetValue(bone.Name, out var target))
            {
                var apart = Vector3.Distance(compiled, target);
                if (apart > floor && apart <= tolerance)
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
                Walk(child, world, rotation, compiled, compiledRotation, into, tolerance, floor, proxy);
            }
        }

        if (maxApart > ClothRestBoneModelGate)
        {
            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity,
                    ClothRestBonePositions, ClothRestBoneTolerance, ClothRestBoneFloor, proxy: false);
            }
        }

        var farOffsetsMoveTogether = farOffsetsAreRigid || farOffsetsAreScaled;
        var proxyTolerance = farOffsetsMoveTogether ? float.MaxValue : ClothRestBoneTolerance;
        var proxyPositions = maxApart > ClothProxyRestBoneModelGate
            || (farOffsetsMoveTogether && maxApartUncapped > ClothProxyRestBoneModelGate);
        if (proxyPositions || turned.Count > 0)
        {
            foreach (var root in model.Skeleton.Roots)
            {
                Walk(root, Vector3.Zero, Quaternion.Identity, Vector3.Zero, Quaternion.Identity,
                    ClothProxyRestBonePositions, proxyPositions ? proxyTolerance : -1f, ClothProxyRestBoneFloor, proxy: true);
            }
        }
    }

    /// <summary>
    /// The parent-local rotations that turn every bone with a recorded cloth rest rotation onto it, root first, while
    /// every other bone keeps its compiled world rotation. Only a turned bone and the children of one are written to
    /// <paramref name="into"/>; the returned map holds their world rotations.
    /// </summary>
    /// <remarks>
    /// The proxy's joint rotations reach the cloth import the same way its positions do.
    /// </remarks>
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

    // cos of half of one degree. A proxy-sheet original's recorded rest rotation sits within 0.3 degrees of its bind
    // rotation on nearly every control bone, the compiler's own drift, and a turned bone sits a degree or more away.
    const float ClothProxyRestRotationTurn = 0.99996192f;

    /// <summary>
    /// Gets the Bone <c>origin</c> of each ClothChain joint re-solved so that the compiler's own chain rest pose puts
    /// the joint on its recorded <c>m_InitPose</c> position bit for bit. Only the document skeleton reads these; mesh
    /// joints keep <see cref="ClothRestBonePositions"/>.
    /// </summary>
    public Dictionary<string, Vector3> ClothChainBoneOrigins { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the Bone <c>angles</c> of each ClothChain joint re-solved so that the compiler's chain rest pose gives the joint
    /// its recorded <c>m_InitPose</c> rotation bit for bit. Only the document skeleton reads these.
    /// </summary>
    public Dictionary<string, Vector3> ClothChainBoneAngles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the ClothChain joints whose Bone origin or angles were re-solved onto the compiler's chain rest pose. Their rings
    /// are rebuilt from the recorded transform instead of a drifted one, so they are written without the node-base tie roll
    /// (<see cref="FeModel.BoneChainJoint.ExtrudeTwistTieNudge"/>) that was chosen against the drift.
    /// </summary>
    HashSet<string> ClothChainRelandedJoints { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Six-decimal grid steps searched on each side of a joint's real-valued origin or angles, per component.</summary>
    const int ClothChainOriginSearchSteps = 12;

    /// <summary>Degrees a landed angle may sit from the printed one; a rotation needing more keeps the printed angles.</summary>
    const float ClothChainAngleSlack = 1e-3f;

    /// <summary>The compiler's degrees-to-half-angle factor, <c>f32(pi / 360)</c> (0x3C0EFA35).</summary>
    const float CompilerHalfDegreesToRadians = (float)(Math.PI * 2 / 360.0 * 0.5);

    /// <summary>A world transform as the compiler's chain rest pose carries it.</summary>
    internal readonly record struct CompilerTransform(Vector3 Position, float Scale, Quaternion Rotation);

    /// <summary>
    /// Re-solves the Bone origin of every ClothChain joint against the compiler's chain rest pose, top-down over the
    /// hierarchy the document declares (see <see cref="ComposeChainBone"/>).
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
            if (IsCompilerOwnedClothBone(bone))
            {
                foreach (var child in bone.Children)
                {
                    Walk(child, parent);
                }

                return;
            }

            var isJoint = targets.TryGetValue(bone.Name, out var target);
            var world = ComposeChainBone(parent, BonePosition(bone, ClothRestBonePositions),
                EntityTransformHelper.ToEulerAngles(bone.Angle), isJoint ? target.Position : null,
                isJoint ? target.Rotation : null, out var landed, out var landedAngles);
            if (landed is { } origin)
            {
                ClothChainBoneOrigins[bone.Name] = origin;
                ClothChainRelandedJoints.Add(bone.Name);
            }

            if (landedAngles is { } angles)
            {
                ClothChainBoneAngles[bone.Name] = angles;
                ClothChainRelandedJoints.Add(bone.Name);
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
    /// One Bone of the compiler's ClothChain rest pose: its printed
    /// <paramref name="origin"/> and <paramref name="angles"/> read back as float32, composed onto
    /// <paramref name="parent"/>. For a chain joint, <paramref name="landedAngles"/> is the six-decimal angles within
    /// <see cref="ClothChainAngleSlack"/> of the printed ones that give it <paramref name="targetRotation"/> exactly, and
    /// then <paramref name="landedOrigin"/> the six-decimal origin that puts it on <paramref name="targetPosition"/>, where
    /// that position is within <see cref="ClothRestBoneTolerance"/>. Either stays null where the joint already lands or no
    /// grid value does, and the returned transform is composed from what was landed.
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

    static bool SameRotationBits(Quaternion a, Quaternion b)
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

                    var sp = pitchSin[ip];
                    var cp = pitchCos[ip];
                    var sy = yawSin[iy];
                    var cy = yawCos[iy];
                    var sr = rollSin[ir];
                    var cr = rollCos[ir];
                    var srXcp = sr * cp;
                    var crXsp = cr * sp;
                    var crXcp = cr * cp;
                    var srXsp = sr * sp;
                    var local = new Quaternion(srXcp * cy - crXsp * sy, crXsp * cy + srXcp * sy, crXcp * sy - srXsp * cy,
                        crXcp * cy + srXsp * sy);
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
            var middle = (long)Math.Round(centre * 1e6);
            var values = new float[2 * ClothChainOriginSearchSteps + 1];
            var sin = new float[values.Length];
            var cos = new float[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = (float)((middle + i - ClothChainOriginSearchSteps) / 1e6);
                var half = values[i] * CompilerHalfDegreesToRadians;
                sin[i] = (float)Math.Sin(half);
                cos[i] = (float)Math.Cos(half);
            }

            return (values, sin, cos);
        }
    }

    /// <summary>The float32 the compiler parses back from a value the document prints with six decimals.</summary>
    internal static float CompilerTextFloat(float value)
        => (float)double.Parse(((double)value).ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.CultureInfo.InvariantCulture);

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
        var sp = (float)Math.Sin(halfPitch);
        var cp = (float)Math.Cos(halfPitch);
        var sy = (float)Math.Sin(halfYaw);
        var cy = (float)Math.Cos(halfYaw);
        var sr = (float)Math.Sin(halfRoll);
        var cr = (float)Math.Cos(halfRoll);
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

    static Quaternion CompilerComposeRotation(CompilerTransform parent, Quaternion rotation)
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

    static Vector3 CompilerComposePosition(CompilerTransform parent, Vector3 origin)
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

        static float[] GridCandidates(double centre)
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

    /// <summary>
    /// Gets the rest-pose bone positions written into the cloth PROXY mesh only. The cloth import
    /// takes the transforms it records in <c>m_InitPose</c> from the proxy mesh file's own joint
    /// list, so a model authored with a proxy posed differently from the render mesh is reproduced
    /// by correcting that joint list alone. Unlike <see cref="ClothRestBonePositions"/> this one is
    /// not capped at <see cref="ClothRestBoneTolerance"/>, because nothing the render mesh is
    /// skinned to moves with it.
    /// </summary>
    public Dictionary<string, Vector3> ClothProxyRestBonePositions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the rest-pose bone rotations, parent-local, written into the cloth PROXY mesh only, beside
    /// <see cref="ClothProxyRestBonePositions"/>.
    /// </summary>
    public Dictionary<string, Quaternion> ClothProxyRestBoneRotations { get; } = new(StringComparer.OrdinalIgnoreCase);

    static Dictionary<int, FeModel.CtrlOffset> BuildCtrlAnchorMap(FeModel feModel)
    {
        var anchorOf = new Dictionary<int, FeModel.CtrlOffset>();
        foreach (var offset in feModel.CtrlOffsets)
        {
            anchorOf[offset.CtrlChild] = offset;
        }

        return anchorOf;
    }

    // The bone a "$cloth_node_<name>" ctrl hangs off, plus the bone-local origin and angles to re-author it at: the
    // m_CtrlOffsets entry the compiler wrote for it, or the skeleton parent when the model carries no such
    // entry, and the node's rest rotation relative to that bone, which the compiler composes as the bone's
    // rotation times the ClothNode's own. A node anchored to another generated node has no authorable root bone.
    internal static bool TryResolveClothNodeAnchor(FeModel feModel, Dictionary<int, FeModel.CtrlOffset> anchorOf,
        int node, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? rootBone, out Vector3 origin,
        out Vector3 angles)
    {
        var names = feModel.CtrlNames;
        rootBone = null;
        origin = default;
        angles = default;
        var parent = -1;

        if (anchorOf.TryGetValue(node, out var anchor)
            && anchor.CtrlParent >= 0 && anchor.CtrlParent < names.Length)
        {
            parent = anchor.CtrlParent;
            rootBone = names[parent];
            origin = anchor.Offset;
        }
        else if (node < feModel.SkelParents.Length
            && feModel.SkelParents[node] >= 0 && feModel.SkelParents[node] < names.Length)
        {
            parent = feModel.SkelParents[node];
            rootBone = names[parent];
            if (node < feModel.InitPosePositions.Length && parent < feModel.InitPosePositions.Length
                && parent < feModel.InitPoseRotations.Length)
            {
                origin = Vector3.Transform(
                    feModel.InitPosePositions[node] - feModel.InitPosePositions[parent],
                    Quaternion.Conjugate(feModel.InitPoseRotations[parent]));
            }
        }

        if (parent >= 0 && node < feModel.InitPoseRotations.Length && parent < feModel.InitPoseRotations.Length)
        {
            var local = Quaternion.Conjugate(feModel.InitPoseRotations[parent]) * feModel.InitPoseRotations[node];
            if (2f * MathF.Atan2(new Vector3(local.X, local.Y, local.Z).Length(), MathF.Abs(local.W)) > ClothNodeRotationTolerance)
            {
                angles = EntityTransformHelper.ToEulerAngles(local);
            }
        }

        if (rootBone is not null && angles == Vector3.Zero && origin.Length() < ClothNodeMergeRadius)
        {
            // The compiler folds a free ClothNode into its root bone's own ctrl when the authored origin
            // is within ClothNodeMergeRadius of the bone and it carries no rotation of its own, which loses
            // the node the original still carries its "$cloth_node_" ctrl for. Push it just outside,
            // keeping its direction where it has one.
            var direction = origin == Vector3.Zero ? Vector3.One : origin;
            origin = Vector3.Normalize(direction) * (ClothNodeMergeRadius * 1.25f);
        }

        return rootBone is not null && !FeModel.IsProxyNodeName(rootBone);
    }

    // Bone-local euclidean distance under which the compiler merges a free ClothNode into its root bone's
    // control node instead of giving it one of its own. A node at exactly this distance keeps its own.
    const float ClothNodeMergeRadius = 1e-3f;

    // Radians of rest rotation relative to the root bone under which a free ClothNode counts as unrotated.
    const float ClothNodeRotationTolerance = 1e-4f;
}

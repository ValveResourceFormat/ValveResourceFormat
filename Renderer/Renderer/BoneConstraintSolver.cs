using System.Buffers.Binary;
using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.ResourceTypes.ModelFlex;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Evaluates a model's bone constraints on its model space pose, in compiled order.
    /// </summary>
    public sealed class BoneConstraintSolver
    {
        private readonly Constraint[] constraints;

        /// <summary>
        /// Builds the constraints of a model. Constraints whose bones, attachments or flex controllers are missing are skipped.
        /// </summary>
        public BoneConstraintSolver(Model model)
            : this(BoneConstraintBase.ReadList(model), model.Skeleton, model.FlexControllers, model.Attachments)
        {
        }

        private BoneConstraintSolver(BoneConstraintBase[] definitions, Skeleton skeleton, FlexController[] flexControllers,
            IReadOnlyDictionary<string, Attachment> attachments)
        {
            var context = new BuildContext(skeleton, flexControllers, attachments);
            var built = new List<Constraint>();

            foreach (var definition in definitions)
            {
                var constraint = definition switch
                {
                    TiltTwistConstraint c => (Constraint?)TiltTwist.Create(c, context),
                    TwistConstraint c => Twist.Create(c, context),
                    AimConstraint c => Aim.Create(c, context),
                    OrientConstraint c => Orient.Create(c, context),
                    PointConstraint c => Point.Create(c, context),
                    ParentConstraint c => Parent.Create(c, context),
                    MorphConstraint c => Morph.Create(c, context),
                    PoseSpaceBoneConstraint c => PoseSpaceBone.Create(c, context),
                    PoseSpaceMorphConstraint c => PoseSpaceMorph.Create(c, context),
                    DotToMorphConstraint c => DotToMorph.Create(c, context),
                    RbfConstraint c => Rbf.Create(c, context),
                    _ => null,
                };

                if (constraint != null)
                {
                    built.Add(constraint);
                    WritesMorphs |= constraint.WritesMorphs;
                }
            }

            constraints = [.. built];
        }

        /// <summary>Gets whether any constraint writes flex controller values.</summary>
        public bool WritesMorphs { get; }

        /// <summary>Gets the number of constraints that resolved against the model.</summary>
        public int Count => constraints.Length;

        /// <summary>
        /// Evaluates every constraint on the model space bone matrices, reading and writing flex controller values in <paramref name="morphs"/>.
        /// </summary>
        public void Evaluate(Matrix4x4[] pose, Span<float> morphs)
        {
            foreach (var constraint in constraints)
            {
                constraint.Evaluate(pose, morphs);
            }
        }

        private sealed class BuildContext(Skeleton skeleton, FlexController[] flexControllers, IReadOnlyDictionary<string, Attachment> attachments)
        {
            public Skeleton Skeleton { get; } = skeleton;

            private readonly Dictionary<uint, Attachment> attachmentsByHash = attachments.Values
                .DistinctBy(a => StringToken.Get(a.Name))
                .ToDictionary(a => StringToken.Get(a.Name));

            private readonly Dictionary<Bone, int[]> descendants = [];

            public int ParentOf(int bone) => bone >= 0 && Skeleton.Bones[bone].Parent is { } parent ? parent.Index : -1;

            public int FindMorph(string name) => Array.FindIndex(flexControllers, c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

            public FlexController? GetMorph(int index) => index >= 0 ? flexControllers[index] : null;

            public Attachment? FindAttachment(uint hash) => attachmentsByHash.GetValueOrDefault(hash);

            public Attachment? FindAttachment(string name) => attachments.GetValueOrDefault(name);

            public int[] DescendantsOf(int boneIndex)
            {
                var bone = Skeleton.Bones[boneIndex];
                if (!descendants.TryGetValue(bone, out var list))
                {
                    var collected = new List<int>();
                    var stack = new Stack<Bone>(bone.Children);
                    while (stack.TryPop(out var child))
                    {
                        collected.Add(child.Index);
                        foreach (var grandchild in child.Children)
                        {
                            stack.Push(grandchild);
                        }
                    }

                    list = [.. collected.Order()];
                    descendants[bone] = list;
                }

                return list;
            }

            public Constrained? ResolveConstrainedBone(ConstrainedBone constrained)
            {
                var bone = Skeleton.GetBoneIndex(constrained.BoneHash);
                return bone < 0 ? null : new Constrained(bone, ParentOf(bone), constrained.Weight, DescendantsOf(bone), constrained);
            }

            public Constrained[]? ResolveBones(uint[] hashes)
                => ResolveConstrainedBones([.. hashes.Select(hash => new ConstrainedBone { BoneHash = hash, Weight = 1f })]);

            public Constrained[]? ResolveConstrainedBones(BaseConstraint constraint) => ResolveConstrainedBones(constraint.ConstrainedBones);

            private Constrained[]? ResolveConstrainedBones(ConstrainedBone[] definitions)
            {
                var constrainedBones = new Constrained[definitions.Length];
                for (var i = 0; i < constrainedBones.Length; i++)
                {
                    if (ResolveConstrainedBone(definitions[i]) is not { } constrained)
                    {
                        return null;
                    }

                    constrainedBones[i] = constrained;
                }

                return constrainedBones;
            }

            public Target? ResolveTarget(ConstraintTarget target)
            {
                var offset = new FrameBone(target.PositionOffset, 1f, target.Offset);

                if (!target.IsAttachment)
                {
                    var bone = Skeleton.GetBoneIndex(target.BoneHash);
                    return bone < 0 ? null : new Target(IsApproxIdentity(offset) ? TargetKind.Bone : TargetKind.BoneWithOffset, bone, offset, null, target);
                }

                if (FindAttachment(target.BoneHash) is not { Length: > 0 } attachment)
                {
                    return null;
                }

                if (attachment.Length > 1)
                {
                    return new Target(TargetKind.Attachment, -1, offset, attachment, target);
                }

                var influence = attachment[0];
                offset = new FrameBone(influence.Offset, 1f, influence.Rotation).Concat(offset);

                var influenceBone = Skeleton.GetBoneIndex(influence.Name);
                if (influenceBone < 0)
                {
                    return new Target(TargetKind.Fixed, -1, offset, null, target);
                }

                return new Target(IsApproxIdentity(offset) ? TargetKind.Bone : TargetKind.BoneWithOffset, influenceBone, offset, null, target);
            }

            public Target[]? ResolveTargets(BaseConstraint constraint)
            {
                var targets = new Target[constraint.Targets.Length];
                for (var i = 0; i < targets.Length; i++)
                {
                    if (ResolveTarget(constraint.Targets[i]) is not { } target)
                    {
                        return null;
                    }

                    targets[i] = target;
                }

                return targets;
            }
        }

        private sealed record Constrained(int Bone, int Parent, float Weight, int[] Descendants, ConstrainedBone Definition);

        private enum TargetKind
        {
            BoneWithOffset,
            Bone,
            Attachment,
            Fixed,
        }

        private sealed record Target(TargetKind Kind, int Bone, FrameBone Offset, Attachment? Attachment, ConstraintTarget Definition);

        private abstract class Constraint
        {
            public virtual bool WritesMorphs => false;

            public abstract void Evaluate(Matrix4x4[] pose, Span<float> morphs);
        }

        private abstract class TargetConstraint(Constrained[] constrainedBones, Target[] targets, Skeleton skeleton) : Constraint
        {
            protected Constrained[] ConstrainedBones { get; } = constrainedBones;
            protected Target[] Targets { get; } = targets;

            protected FrameBone GetTarget(Target target, Matrix4x4[] pose) => target.Kind switch
            {
                TargetKind.BoneWithOffset => FrameBone.FromMatrix(pose[target.Bone]).Concat(target.Offset),
                TargetKind.Bone => FrameBone.FromMatrix(pose[target.Bone]),
                TargetKind.Attachment => FrameBone.FromMatrix(ModelSceneNode.GetAttachmentLocalTransform(target.Attachment!, skeleton, pose)).Concat(target.Offset),
                _ => target.Offset,
            };

            protected (Vector3 Position, float TotalWeight) BlendPositions(int count, Matrix4x4[] pose)
            {
                var position = Vector3.Zero;
                var totalWeight = 0f;

                for (var i = 0; i < count; i++)
                {
                    var target = Targets[i];
                    var targetPosition = target.Kind switch
                    {
                        TargetKind.BoneWithOffset => FrameBone.FromMatrix(pose[target.Bone]).Concat(target.Offset).Position,
                        TargetKind.Bone => pose[target.Bone].Translation,
                        // Source 2 adds the offset without rotating it into the attachment.
                        TargetKind.Attachment => ModelSceneNode.GetAttachmentLocalTransform(target.Attachment!, skeleton, pose).Translation + target.Definition.PositionOffset,
                        _ => target.Offset.Position,
                    };

                    position += targetPosition * target.Definition.Weight;
                    totalWeight += target.Definition.Weight;
                }

                if (totalWeight != 0f)
                {
                    position /= totalWeight;
                }

                return (position, totalWeight);
            }

            protected (Quaternion Rotation, float TotalWeight) BlendRotations(Matrix4x4[] pose, Span<Quaternion> rotations, Span<float> weights)
            {
                var totalWeight = 0f;

                for (var i = 0; i < Targets.Length; i++)
                {
                    rotations[i] = GetTarget(Targets[i], pose).Angle;
                    weights[i] = Targets[i].Definition.Weight;
                    totalWeight += weights[i];
                }

                return (WeightedAverage(rotations, weights), totalWeight);
            }
        }

        private sealed class TiltTwist(Constrained constrained, int targetBone, int targetParent, Quaternion inverseOffset, int targetAxis, int constrainedAxis) : Constraint
        {
            private bool hasPreviousAngle;
            private float previousAngle;

            public static TiltTwist? Create(TiltTwistConstraint definition, BuildContext context)
            {
                if (definition.ConstrainedBones.Length == 0 || definition.Targets.Length == 0
                    || context.ResolveConstrainedBone(definition.ConstrainedBones[0]) is not { } constrained)
                {
                    return null;
                }

                var targetBone = context.Skeleton.GetBoneIndex(definition.Targets[0].BoneHash);
                if (targetBone < 0)
                {
                    return null;
                }

                return new TiltTwist(constrained, targetBone, context.ParentOf(targetBone), Quaternion.Inverse(definition.Targets[0].Offset),
                    Math.Clamp(definition.TargetAxis, 0, 2), Math.Clamp(definition.ConstrainedAxis, 0, 2));
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var rotation = FrameBone.FromMatrix(pose[targetBone]).Angle;
                if (targetParent >= 0)
                {
                    rotation = Quaternion.Inverse(FrameBone.FromMatrix(pose[targetParent]).Angle) * rotation;
                }

                var angle = TwistAngle(inverseOffset * rotation, targetAxis) * constrained.Weight;

                // Unwrap against the last frame so the bone does not flip when the angle crosses +-pi.
                if (hasPreviousAngle && previousAngle != angle)
                {
                    var previousWrapped = MathUtils.Wrap(previousAngle, -MathF.Tau, MathF.Tau);
                    var wrapped = MathUtils.Wrap(angle, -MathF.Tau, MathF.Tau);
                    var delta = wrapped - previousWrapped;

                    if (MathF.Abs(delta) >= MathF.PI)
                    {
                        delta += wrapped > previousWrapped ? -MathF.Tau : MathF.Tau;
                    }

                    angle = previousAngle + delta;
                }

                hasPreviousAngle = true;
                previousAngle = angle;

                var axis = Vector3.Zero;
                axis[constrainedAxis] = 1f;
                SetLocalRotation(pose, constrained, Quaternion.CreateFromAxisAngle(axis, angle));
            }

            // The twist of the rotation around the axis; ignores the tilt of the axis itself.
            private static float TwistAngle(Quaternion rotation, int axis)
            {
                rotation = rotation == default ? Quaternion.Identity : Quaternion.Normalize(rotation);

                Span<Vector3> basis = [Vector3.Transform(Vector3.UnitX, rotation), Vector3.Transform(Vector3.UnitY, rotation), Vector3.Transform(Vector3.UnitZ, rotation)];
                var axis1 = (axis + 1) % 3;
                var axis2 = (axis + 2) % 3;
                const float Epsilon = 0.0001f;

                var diagonal = Math.Clamp(MathF.Floor(basis[axis][axis] * 1e7f + 0.5f) / 1e7f, -1f, 1f);
                var tilt = diagonal < 0f ? MathF.Acos(diagonal + 1f) : MathF.Acos(1f - diagonal);

                if (MathF.Abs(tilt - MathF.PI / 2f) > Epsilon)
                {
                    var a = basis[axis][axis1];
                    var b = basis[axis][axis2];
                    var invLength = 1f / MathF.Max(MathF.Sqrt(a * a + b * b), 1.17549435e-38f);
                    a *= invLength;
                    b *= invLength;

                    var m1 = basis[axis1][axis];
                    var m2 = basis[axis2][axis];
                    return MathF.Atan2(m2 * a - m1 * b, -m1 * a - m2 * b);
                }

                var diagonal1 = basis[axis1][axis1];
                if (MathF.Abs(diagonal1 - 1f) <= Epsilon)
                {
                    return 0f;
                }

                return MathF.Atan2(basis[axis1][axis2], diagonal1);
            }
        }

        // Measures the twist of the second target against its parent bone; the first target is not read.
        private sealed class Twist(TwistConstraint definition, Constrained[] constrainedBones, int childBone, int parentBone, int grandparentBone) : Constraint
        {
            private readonly Quaternion[] results = new Quaternion[constrainedBones.Length];

            public static Twist? Create(TwistConstraint definition, BuildContext context)
            {
                if (context.ResolveConstrainedBones(definition) is not { } constrainedBones)
                {
                    return null;
                }

                if (definition.Targets.Length < 2)
                {
                    return new Twist(definition, constrainedBones, -1, -1, -1);
                }

                var childBone = context.Skeleton.GetBoneIndex(definition.Targets[1].BoneHash);
                var parentBone = context.ParentOf(childBone);
                return childBone < 0 || parentBone < 0 ? null : new Twist(definition, constrainedBones, childBone, parentBone, context.ParentOf(parentBone));
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var parentRotation = Quaternion.Identity;
                var child = FrameBone.Identity;

                if (childBone >= 0)
                {
                    var parent = FrameBone.FromMatrix(pose[parentBone]);
                    child = parent.Inverse().Concat(FrameBone.FromMatrix(pose[childBone]));
                    parentRotation = grandparentBone >= 0
                        ? FrameBone.FromMatrix(pose[grandparentBone]).Inverse().Concat(parent).Angle
                        : parent.Angle;
                }

                var baseInverse = Quaternion.Inverse(definition.Inverse ? definition.ParentBindRotation : definition.ChildBindRotation);
                Solve(parentRotation, child, baseInverse);

                for (var i = 0; i < constrainedBones.Length; i++)
                {
                    SetLocalRotation(pose, constrainedBones[i], results[i]);
                }
            }

            private void Solve(Quaternion parentRotation, FrameBone child, Quaternion baseInverse)
            {
                for (var i = 0; i < constrainedBones.Length; i++)
                {
                    results[i] = constrainedBones[i].Definition.BaseOrientation;
                }

                var up = definition.UpVector;
                var childDirection = child.Position;
                if (childDirection.LengthSquared() < 1.42109e-12f)
                {
                    return;
                }

                childDirection = Vector3.Normalize(childDirection);

                Vector3 rotatedUp, twistAxis;
                if (definition.Inverse)
                {
                    var rotation = baseInverse * Align(baseInverse, parentRotation);
                    rotatedUp = Vector3.Transform(up, rotation);
                    twistAxis = Vector3.Transform(childDirection, rotation);
                }
                else
                {
                    var rotation = baseInverse * Align(baseInverse, child.Angle);
                    rotatedUp = Vector3.Transform(up, rotation);
                    twistAxis = Vector3.Transform(childDirection, baseInverse);
                }

                const float ParallelEpsilon = 1.19209e-6f;
                if (1f - MathF.Abs(Vector3.Dot(up, twistAxis)) < ParallelEpsilon
                    || 1f - MathF.Abs(Vector3.Dot(rotatedUp, twistAxis)) < ParallelEpsilon)
                {
                    return;
                }

                var from = Vector3.Normalize(MathUtils.ProjectOntoPlane(up, twistAxis));
                var to = Vector3.Normalize(MathUtils.ProjectOntoPlane(rotatedUp, twistAxis));
                var cosine = Vector3.Dot(from, to);

                // A twist near 0 or 180 degrees leaves the base orientations as they are.
                if (MathF.Abs(MathF.Abs(cosine) - 1f) <= 0.001f)
                {
                    return;
                }

                var angle = MathUtils.SafeAcos(cosine);
                if (Vector3.Dot(Vector3.Normalize(Vector3.Cross(from, to)), twistAxis) < 0f)
                {
                    angle = -angle;
                }

                var twist = Quaternion.CreateFromAxisAngle(childDirection, angle);

                for (var i = 0; i < constrainedBones.Length; i++)
                {
                    var weight = constrainedBones[i].Weight;
                    var scaled = Quaternion.Slerp(Quaternion.Identity, twist, definition.Inverse ? weight - 1f : weight);
                    results[i] = scaled * Align(scaled, constrainedBones[i].Definition.BaseOrientation);
                }
            }
        }

        private sealed class Aim(AimConstraint definition, Constrained[] constrainedBones, Target[] targets, Skeleton skeleton)
            : TargetConstraint(constrainedBones, targets, skeleton)
        {
            public static Aim? Create(AimConstraint definition, BuildContext context)
                => context.ResolveConstrainedBones(definition) is { } constrainedBones && context.ResolveTargets(definition) is { } targets
                    ? new Aim(definition, constrainedBones, targets, context.Skeleton)
                    : null;

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var upTarget = FrameBone.Identity;
                var upType = definition.UpType;

                // Up types 0 and 2 read the last target; 1 and 3 still leave it out of the aim when there is more than one.
                if (upType is 0 or 2)
                {
                    if (Targets.Length <= 1)
                    {
                        return;
                    }

                    upTarget = GetTarget(Targets[^1], pose);
                }
                else if (Targets.Length == 0)
                {
                    return;
                }

                var (targetPosition, totalWeight) = BlendPositions(Targets.Length > 1 ? Targets.Length - 1 : Targets.Length, pose);

                foreach (var constrained in ConstrainedBones)
                {
                    var weight = totalWeight * constrained.Weight;
                    if (MathF.Abs(weight) <= 0.0001f)
                    {
                        continue;
                    }

                    var current = FrameBone.FromMatrix(pose[constrained.Bone]);
                    var parent = constrained.Parent >= 0 ? FrameBone.FromMatrix(pose[constrained.Parent]) : FrameBone.Identity;
                    var parentInverse = parent.Inverse();

                    // Without a parent Source 2 reads the position from past the end of its identity transform.
                    var localPosition = parentInverse.TransformPoint(current.Position);

                    var up = upType switch
                    {
                        1 => definition.UpVector,
                        3 => Vector3.Transform(definition.UpVector, parent.Angle),
                        2 => MathUtils.SafeNormalize(upTarget.Position - parent.TransformPoint(localPosition)),
                        _ => Vector3.Transform(definition.UpVector, upTarget.Angle),
                    };

                    up = Vector3.Transform(up, Quaternion.Conjugate(parent.Angle));
                    var direction = MathUtils.SafeNormalize(parentInverse.TransformPoint(targetPosition) - localPosition);

                    var look = LookRotation(direction, up);
                    var local = look * Align(look, definition.AimOffset);
                    var rotation = parent.Angle * Align(parent.Angle, local);

                    current.Angle = MathF.Abs(weight - 1f) <= 0.0001f ? rotation : Quaternion.Slerp(current.Angle, rotation, weight);
                    WriteBack(pose, constrained, current);
                }
            }

            // +Y points along the direction, +Z towards up.
            private static Quaternion LookRotation(Vector3 forward, Vector3 up)
            {
                forward = MathUtils.SafeNormalize(forward);
                up = MathUtils.SafeNormalize(MathUtils.ProjectOntoPlane(up, forward));
                var side = MathUtils.SafeNormalize(Vector3.Cross(forward, up));

                var basis = new Matrix4x4(
                    side.X, side.Y, side.Z, 0f,
                    forward.X, forward.Y, forward.Z, 0f,
                    up.X, up.Y, up.Z, 0f,
                    0f, 0f, 0f, 1f);
                return Quaternion.CreateFromRotationMatrix(basis);
            }
        }

        private sealed class Orient(Constrained[] constrainedBones, Target[] targets, Skeleton skeleton) : TargetConstraint(constrainedBones, targets, skeleton)
        {
            public static Orient? Create(OrientConstraint definition, BuildContext context)
                => context.ResolveConstrainedBones(definition) is { } constrainedBones && context.ResolveTargets(definition) is { } targets
                    ? new Orient(constrainedBones, targets, context.Skeleton)
                    : null;

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                Span<Quaternion> rotations = stackalloc Quaternion[Targets.Length];
                Span<float> weights = stackalloc float[Targets.Length];
                var (rotation, totalWeight) = BlendRotations(pose, rotations, weights);

                foreach (var constrained in ConstrainedBones)
                {
                    var weight = totalWeight * constrained.Weight;
                    if (MathF.Abs(weight) <= 0.0001f)
                    {
                        continue;
                    }

                    var current = FrameBone.FromMatrix(pose[constrained.Bone]);
                    current.Angle = MathF.Abs(weight - 1f) <= 0.0001f ? rotation : Quaternion.Slerp(current.Angle, rotation, weight);
                    WriteBack(pose, constrained, current);
                }
            }
        }

        private sealed class Point(Constrained[] constrainedBones, Target[] targets, Skeleton skeleton) : TargetConstraint(constrainedBones, targets, skeleton)
        {
            public static Point? Create(PointConstraint definition, BuildContext context)
                => context.ResolveConstrainedBones(definition) is { } constrainedBones && context.ResolveTargets(definition) is { } targets
                    ? new Point(constrainedBones, targets, context.Skeleton)
                    : null;

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var (position, totalWeight) = BlendPositions(Targets.Length, pose);

                foreach (var constrained in ConstrainedBones)
                {
                    var weight = totalWeight * constrained.Weight;
                    if (MathF.Abs(weight) <= 0.0001f)
                    {
                        continue;
                    }

                    var current = FrameBone.FromMatrix(pose[constrained.Bone]);
                    current.Position = MathF.Abs(weight - 1f) <= 0.0001f ? position : Vector3.Lerp(current.Position, position, weight);
                    WriteBack(pose, constrained, current);
                }
            }
        }

        private sealed class Parent(Constrained[] constrainedBones, Target[] targets, Skeleton skeleton) : TargetConstraint(constrainedBones, targets, skeleton)
        {
            public static Parent? Create(ParentConstraint definition, BuildContext context)
                => context.ResolveConstrainedBones(definition) is { } constrainedBones && context.ResolveTargets(definition) is { } targets
                    ? new Parent(constrainedBones, targets, context.Skeleton)
                    : null;

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                Span<Quaternion> rotations = stackalloc Quaternion[Targets.Length];
                Span<float> weights = stackalloc float[Targets.Length];
                var position = Vector4.Zero;
                var totalWeight = 0f;

                for (var i = 0; i < Targets.Length; i++)
                {
                    var target = GetTarget(Targets[i], pose);
                    rotations[i] = target.Angle;
                    weights[i] = Targets[i].Definition.Weight;
                    position += new Vector4(target.Position, target.Scale) * weights[i];
                    totalWeight += weights[i];
                }

                if (totalWeight > 0f)
                {
                    position /= totalWeight;
                }

                var blended = new FrameBone(position, WeightedAverage(rotations, weights));

                foreach (var constrained in ConstrainedBones)
                {
                    var weight = totalWeight * constrained.Weight;
                    if (MathF.Abs(weight) <= 0.0001f)
                    {
                        continue;
                    }

                    if (MathF.Abs(weight - 1f) <= 0.0001f)
                    {
                        WriteBack(pose, constrained, blended);
                        continue;
                    }

                    // Source 2 blends by the bone's own weight alone here, not by the target weight sum.
                    var current = FrameBone.FromMatrix(pose[constrained.Bone]);
                    var t = constrained.Weight;
                    current.Position = Vector3.Lerp(current.Position, blended.Position, t);
                    current.Scale = float.Lerp(current.Scale, blended.Scale, t);
                    current.Angle = Quaternion.Normalize(Quaternion.Lerp(current.Angle, Align(current.Angle, blended.Angle), t));
                    WriteBack(pose, constrained, current);
                }
            }
        }

        private sealed class Morph(MorphConstraint definition, Constrained[] constrainedBones, int morph, float morphMin, float morphMax) : Constraint
        {
            public static Morph? Create(MorphConstraint definition, BuildContext context)
            {
                if (context.ResolveConstrainedBones(definition) is not { } constrainedBones || context.ResolveTargets(definition) is null)
                {
                    return null;
                }

                var morph = context.FindMorph(definition.TargetMorph);
                return context.GetMorph(morph) is { } controller
                    ? new Morph(definition, constrainedBones, morph, controller.Min, controller.Max)
                    : null;
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                if (morph >= morphs.Length)
                {
                    return;
                }

                var value = morphs[morph];
                var amount = morphMin == morphMax
                    ? (value - morphMax >= 0f ? definition.Max : definition.Min)
                    : definition.Min + (definition.Max - definition.Min) * (value - morphMin) / (morphMax - morphMin);

                var channel = definition.Channel;
                if (channel is < 0 or > 5)
                {
                    return;
                }

                foreach (var constrained in constrainedBones)
                {
                    var weight = constrained.Weight;
                    if (MathF.Abs(weight) <= 0.0001f)
                    {
                        continue;
                    }

                    var parent = constrained.Parent >= 0 ? FrameBone.FromMatrix(pose[constrained.Parent]) : FrameBone.Identity;
                    var local = parent.Inverse().Concat(FrameBone.FromMatrix(pose[constrained.Bone]));

                    if (channel < 3)
                    {
                        var position = constrained.Definition.BasePosition;
                        position[channel] += amount * weight;
                        local.Position = position;
                    }
                    else
                    {
                        var axis = Vector3.Zero;
                        axis[channel - 3] = 1f;
                        var baseRotation = constrained.Definition.BaseOrientation;
                        var rotated = Quaternion.CreateFromAxisAngle(axis, float.DegreesToRadians(amount));
                        rotated = baseRotation * Align(baseRotation, rotated);
                        local.Angle = MathF.Abs(weight - 1f) <= 0.0001f ? rotated : Quaternion.Slerp(baseRotation, rotated, weight);
                    }

                    WriteBack(pose, constrained, parent.Concat(local));
                }
            }
        }

        private sealed class PoseSpaceBone(Constrained[] constrainedBones, int bone, int parent, Vector3 offset, RadialBasis basis, PoseSpaceBoneConstraint definition) : Constraint
        {
            private readonly float[] weights = new float[basis.SampleCount];

            public static PoseSpaceBone? Create(PoseSpaceBoneConstraint definition, BuildContext context)
            {
                if (definition.Targets.Length != 1 || !definition.Targets[0].IsAttachment
                    || definition.ConstrainedBones.Length == 0 || definition.Inputs.Length == 0
                    || context.ResolveConstrainedBones(definition) is not { } constrainedBones
                    || context.ResolveTarget(definition.Targets[0]) is not { Kind: TargetKind.Bone or TargetKind.BoneWithOffset } target
                    || definition.Inputs.Any(input => input.Outputs.Length < constrainedBones.Length))
                {
                    return null;
                }

                var parent = context.ParentOf(target.Bone);
                if (parent < 0)
                {
                    return null;
                }

                // Duplicate samples are dropped, which leaves the sample count short of the inputs and disables the constraint.
                if (RadialBasis.Create(definition.Inputs.Select(i => i.Value), definition.Kernel, definition.Falloff) is not { } basis
                    || basis.SampleCount != definition.Inputs.Length)
                {
                    return null;
                }

                return new PoseSpaceBone(constrainedBones, target.Bone, parent, target.Offset.Position, basis, definition);
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var local = FrameBone.FromMatrix(pose[parent]).Inverse().Concat(FrameBone.FromMatrix(pose[bone]));
                basis.Evaluate(local.TransformPoint(offset), weights);

                var inputs = definition.Inputs;
                Span<Quaternion> rotations = stackalloc Quaternion[inputs.Length];

                for (var s = 0; s < constrainedBones.Length; s++)
                {
                    // Positions use the raw basis weights; only the rotation average normalizes them.
                    var position = Vector3.Zero;
                    for (var i = 0; i < inputs.Length; i++)
                    {
                        position += inputs[i].Outputs[s].Position * weights[i];
                        rotations[i] = inputs[i].Outputs[s].Rotation;
                    }

                    var newLocal = new FrameBone(position, 1f, WeightedAverage(rotations, weights));
                    var constrained = constrainedBones[s];
                    WriteBack(pose, constrained, constrained.Parent >= 0 ? FrameBone.FromMatrix(pose[constrained.Parent]).Concat(newLocal) : newLocal);
                }
            }
        }

        private sealed class PoseSpaceMorph(PoseSpaceMorphConstraint definition, int bone, int parent, Vector3 offset, int[] outputs, RadialBasis basis) : Constraint
        {
            private readonly float[] weights = new float[basis.SampleCount];

            public override bool WritesMorphs => true;

            public static PoseSpaceMorph? Create(PoseSpaceMorphConstraint definition, BuildContext context)
            {
                var bone = context.Skeleton.GetBoneIndex(definition.BoneName);
                var parent = context.ParentOf(bone);
                var outputs = definition.OutputMorphs.Select(context.FindMorph).ToArray();

                // Source 2 rejects bone index 0 as well as a missing bone.
                if (definition.Inputs.Length == 0 || outputs.Length == 0 || bone <= 0 || parent < 0
                    || context.FindAttachment(definition.AttachmentName) is not { Length: 1 } attachment
                    || outputs.Any(o => o < 0)
                    || RadialBasis.Create(definition.Inputs.Select(i => i.Value), definition.Kernel, definition.Falloff) is not { } basis
                    || basis.SampleCount != definition.Inputs.Length)
                {
                    return null;
                }

                return new PoseSpaceMorph(definition, bone, parent, attachment[0].Offset, outputs, basis);
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                var local = FrameBone.FromMatrix(pose[parent]).Inverse().Concat(FrameBone.FromMatrix(pose[bone]));
                basis.Evaluate(local.TransformPoint(offset), weights);

                var inputs = definition.Inputs;
                for (var j = 0; j < outputs.Length; j++)
                {
                    if (outputs[j] >= morphs.Length)
                    {
                        continue;
                    }

                    var value = 0f;
                    for (var i = 0; i < inputs.Length; i++)
                    {
                        value += j < inputs[i].Weights.Length ? inputs[i].Weights[j] * weights[i] : 0f;
                    }

                    morphs[outputs[j]] = definition.Clamp ? MathUtils.Saturate(value) : value;
                }
            }
        }

        private sealed class DotToMorph(DotToMorphConstraint definition, int bone, int target, int morph) : Constraint
        {
            public override bool WritesMorphs => true;

            public static DotToMorph? Create(DotToMorphConstraint definition, BuildContext context)
            {
                var bone = context.Skeleton.GetBoneIndex(definition.BoneName);
                var target = context.Skeleton.GetBoneIndex(definition.TargetBoneName);
                var morph = context.FindMorph(definition.MorphChannelName);
                return bone >= 0 && target >= 0 && morph >= 0 ? new DotToMorph(definition, bone, target, morph) : null;
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                if (morph >= morphs.Length)
                {
                    return;
                }

                var boneRotation = FrameBone.FromMatrix(pose[bone]).Angle;
                var direction = MathUtils.SafeNormalize(pose[target].Translation - pose[bone].Translation);

                var angle = float.RadiansToDegrees(MathUtils.AngleBetween(direction, Vector3.Transform(Vector3.UnitZ, boneRotation)));

                var value = MathUtils.RemapValClamped(angle, definition.InputMin, definition.InputMax, definition.OutputMin, definition.OutputMax);

                // Fades back to the minimum as the target swings onto the bone's Y axis.
                value += MathF.Abs(Vector3.Dot(direction, Vector3.Transform(Vector3.UnitY, boneRotation))) * (definition.OutputMin - value);
                morphs[morph] = value;
            }
        }

        // Untested: no shipped CS2 model uses it, so the blob layout is only known from the reader.
        private sealed class Rbf(Constrained[] inputs, Constrained[] outputs, RbfParameters parameters) : Constraint
        {
            private readonly (Vector3 Position, Quaternion Rotation)[] inputPose = new (Vector3, Quaternion)[inputs.Length];
            private readonly float[] distances = new float[parameters.PoseCount];

            public static Rbf? Create(RbfConstraint definition, BuildContext context)
            {
                if (definition.InputBoneHashes.Length == 0 || definition.OutputBoneHashes.Length == 0
                    || RbfParameters.Read(definition.Parameters) is not { } parameters
                    || parameters.InputCount != definition.InputBoneHashes.Length
                    || parameters.OutputCount != definition.OutputBoneHashes.Length)
                {
                    return null;
                }

                return context.ResolveBones(definition.InputBoneHashes) is { } inputs && context.ResolveBones(definition.OutputBoneHashes) is { } outputs
                    ? new Rbf(inputs, outputs, parameters)
                    : null;
            }

            public override void Evaluate(Matrix4x4[] pose, Span<float> morphs)
            {
                for (var i = 0; i < inputs.Length; i++)
                {
                    var input = inputs[i];
                    var local = FrameBone.FromMatrix(pose[input.Bone]);
                    if (input.Parent >= 0)
                    {
                        local = FrameBone.FromMatrix(pose[input.Parent]).Inverse().Concat(local);
                    }

                    inputPose[i] = (local.Position, local.Angle);
                }

                parameters.Evaluate(inputPose, distances);

                for (var o = 0; o < outputs.Length; o++)
                {
                    var output = outputs[o];
                    var local = parameters.GetOutput(o, distances);
                    WriteBack(pose, output, output.Parent >= 0 ? FrameBone.FromMatrix(pose[output.Parent]).Concat(local) : local);
                }
            }
        }

        // The m_rbfParameters blob: each input bone's local transform per pose, and per output bone
        // a 7 channel (position, quaternion) weight matrix over the summed pose distances.
        private sealed class RbfParameters
        {
            private float translationThreshold;
            private float rotationThreshold;
            private int inputCount;
            private float[][] translations = [];
            private float[][] rotations = [];
            private float[] translationScales = [];
            private float[] rotationScales = [];
            private float[] inputScales = [];
            private Vector3[] basePositions = [];
            private float[][] outputWeights = [];

            public int PoseCount { get; private set; }
            public int InputCount => inputCount;
            public int OutputCount => basePositions.Length;

            public static RbfParameters? Read(byte[] blob)
            {
                if (blob.Length < 0x20)
                {
                    return null;
                }

                var span = blob.AsSpan();
                var poses = BinaryPrimitives.ReadInt32LittleEndian(span[0x14..]);
                var inputs = BinaryPrimitives.ReadInt32LittleEndian(span[0x18..]);
                var outputs = BinaryPrimitives.ReadInt32LittleEndian(span[0x1C..]);

                if (poses <= 0 || inputs <= 0 || outputs <= 0
                    || 0x20L + 4L * ((long)inputs * (7L * poses + 3) + (long)outputs * (3 + 7L * poses)) > blob.Length)
                {
                    return null;
                }

                var offset = 0x20;
                float[] ReadFloats(int count)
                {
                    var values = MemoryMarshal.Cast<byte, float>(blob.AsSpan(offset, count * 4)).ToArray();
                    offset += count * 4;
                    return values;
                }

                var parameters = new RbfParameters
                {
                    translationThreshold = BinaryPrimitives.ReadSingleLittleEndian(span[0x04..]),
                    rotationThreshold = BinaryPrimitives.ReadSingleLittleEndian(span[0x08..]),
                    PoseCount = poses,
                    inputCount = inputs,
                };

                parameters.translations = [.. Enumerable.Range(0, inputs).Select(_ => ReadFloats(3 * poses))];
                parameters.rotations = [.. Enumerable.Range(0, inputs).Select(_ => ReadFloats(4 * poses))];
                parameters.translationScales = ReadFloats(inputs);
                parameters.rotationScales = ReadFloats(inputs);
                parameters.inputScales = ReadFloats(inputs);
                parameters.basePositions = [.. Enumerable.Range(0, outputs).Select(_ => ReadFloats(3)).Select(v => new Vector3(v[0], v[1], v[2]))];
                parameters.outputWeights = [.. Enumerable.Range(0, outputs).Select(_ => ReadFloats(7 * poses))];
                return parameters;
            }

            // Sums every input's scaled translation and rotation distance to each pose.
            public void Evaluate(ReadOnlySpan<(Vector3 Position, Quaternion Rotation)> inputPose, Span<float> distances)
            {
                distances.Clear();

                if (PoseCount <= 1)
                {
                    distances[0] = 1f;
                    return;
                }

                for (var i = 0; i < inputCount; i++)
                {
                    if (inputScales[i] == 0f)
                    {
                        continue;
                    }

                    for (var k = 0; k < PoseCount; k++)
                    {
                        var distance = 0f;

                        if (translationScales[i] > 0f)
                        {
                            var t = translations[i];
                            var d = Vector3.Distance(inputPose[i].Position, new Vector3(t[k * 3], t[k * 3 + 1], t[k * 3 + 2]));
                            distance += (d <= translationThreshold ? 0f : d) / translationScales[i];
                        }

                        if (rotationScales[i] > 0f)
                        {
                            var r = rotations[i];
                            var c = Quaternion.Dot(new Quaternion(r[k * 4], r[k * 4 + 1], r[k * 4 + 2], r[k * 4 + 3]), inputPose[i].Rotation);
                            var cosine = 2f * c * c - 1f;
                            var a = MathUtils.SafeAcos(cosine);
                            distance += (a <= rotationThreshold ? 0f : a) / rotationScales[i];
                        }

                        distances[k] += distance / inputScales[i];
                    }
                }
            }

            public FrameBone GetOutput(int output, ReadOnlySpan<float> distances)
            {
                Span<float> channels = stackalloc float[7];
                var weights = outputWeights[output];

                for (var c = 0; c < 7; c++)
                {
                    for (var k = 0; k < PoseCount; k++)
                    {
                        channels[c] += weights[c * PoseCount + k] * distances[k];
                    }
                }

                var rotation = new Quaternion(channels[3], channels[4], channels[5], channels[6]);
                return new FrameBone(basePositions[output] + new Vector3(channels[0], channels[1], channels[2]), 1f, rotation == default ? rotation : Quaternion.Normalize(rotation));
            }
        }

        // Radial basis interpolation over sample positions, with the kernel matrix inverted up front.
        private sealed class RadialBasis
        {
            private readonly Vector3[] samples;
            private readonly double[] distances;
            private readonly double[,] inverse;
            private readonly RbfKernel kernel;
            private readonly float scale;

            private RadialBasis(Vector3[] samples, double[,] inverse, RbfKernel kernel, float scale)
            {
                this.samples = samples;
                this.inverse = inverse;
                distances = new double[samples.Length];
                this.kernel = kernel;
                this.scale = scale;
            }

            public int SampleCount => samples.Length;

            public static RadialBasis? Create(IEnumerable<Vector3> values, RbfKernel kernel, float falloff)
            {
                var samples = values.Distinct().ToArray();
                if (samples.Length == 0)
                {
                    return null;
                }

                kernel = kernel is >= RbfKernel.Multiquadric and <= RbfKernel.ThinPlate ? kernel : RbfKernel.Multiquadric;
                var scale = falloff > 0f ? 1f / falloff : 1f;

                var n = samples.Length;
                var matrix = new double[n, n];
                for (var i = 0; i < n; i++)
                {
                    // Thin plate is NaN at r=0, which poisons the solve.
                    matrix[i, i] = Kernel(kernel, 0f, scale);
                    for (var j = i + 1; j < n; j++)
                    {
                        matrix[i, j] = matrix[j, i] = Kernel(kernel, Vector3.Distance(samples[i], samples[j]), scale);
                    }
                }

                return Invert(matrix) is { } inverse ? new RadialBasis(samples, inverse, kernel, scale) : null;
            }

            public void Evaluate(Vector3 position, Span<float> weights)
            {
                var n = samples.Length;

                for (var k = 0; k < n; k++)
                {
                    distances[k] = Kernel(kernel, Vector3.Distance(position, samples[k]), scale);
                }

                for (var i = 0; i < n; i++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < n; k++)
                    {
                        sum += inverse[i, k] * distances[k];
                    }

                    weights[i] = (float)sum;
                }
            }

            private static float Kernel(RbfKernel kernel, float r, float scale) => kernel switch
            {
                RbfKernel.Multiquadric => MathF.Sqrt(1f + (r * scale) * (r * scale)),
                RbfKernel.InverseMultiquadric => 1f / MathF.Sqrt(1f + (r * scale) * (r * scale)),
                RbfKernel.Gaussian => MathF.Exp(-(r * scale) * (r * scale)),
                RbfKernel.Linear => r,
                RbfKernel.Cubic => r * r * r,
                RbfKernel.Quintic => r * r * r * r * r,
                _ => r * r * MathF.Log(r),
            };

            // LU with partial pivoting, failing on an exactly zero pivot.
            private static double[,]? Invert(double[,] matrix)
            {
                var n = matrix.GetLength(0);
                var lu = (double[,])matrix.Clone();
                var pivots = Enumerable.Range(0, n).ToArray();

                for (var column = 0; column < n; column++)
                {
                    var pivot = column;
                    for (var row = column + 1; row < n; row++)
                    {
                        if (Math.Abs(lu[row, column]) > Math.Abs(lu[pivot, column]))
                        {
                            pivot = row;
                        }
                    }

                    if (pivot != column)
                    {
                        for (var k = 0; k < n; k++)
                        {
                            (lu[pivot, k], lu[column, k]) = (lu[column, k], lu[pivot, k]);
                        }

                        (pivots[pivot], pivots[column]) = (pivots[column], pivots[pivot]);
                    }

                    if (lu[column, column] == 0.0)
                    {
                        return null;
                    }

                    for (var row = column + 1; row < n; row++)
                    {
                        lu[row, column] /= lu[column, column];
                        for (var k = column + 1; k < n; k++)
                        {
                            lu[row, k] -= lu[row, column] * lu[column, k];
                        }
                    }
                }

                var inverse = new double[n, n];
                for (var column = 0; column < n; column++)
                {
                    for (var row = 0; row < n; row++)
                    {
                        var sum = pivots[row] == column ? 1.0 : 0.0;
                        for (var k = 0; k < row; k++)
                        {
                            sum -= lu[row, k] * inverse[k, column];
                        }

                        inverse[row, column] = sum;
                    }

                    for (var row = n - 1; row >= 0; row--)
                    {
                        var sum = inverse[row, column];
                        for (var k = row + 1; k < n; k++)
                        {
                            sum -= lu[row, k] * inverse[k, column];
                        }

                        inverse[row, column] = sum / lu[row, row];
                    }
                }

                return inverse;
            }
        }

        private static void SetLocalRotation(Matrix4x4[] pose, Constrained constrained, Quaternion localRotation)
        {
            var current = FrameBone.FromMatrix(pose[constrained.Bone]);

            if (constrained.Parent < 0)
            {
                current.Angle = localRotation;
                WriteBack(pose, constrained, current);
                return;
            }

            var parent = FrameBone.FromMatrix(pose[constrained.Parent]);
            var local = parent.Inverse().Concat(current);
            local.Angle = localRotation;
            WriteBack(pose, constrained, parent.Concat(local));
        }

        // Moves the bone and carries its descendants along.
        private static void WriteBack(Matrix4x4[] pose, Constrained constrained, FrameBone model)
        {
            var previous = pose[constrained.Bone];
            var next = model.ToMatrix();
            pose[constrained.Bone] = next;

            if (constrained.Descendants.Length == 0)
            {
                return;
            }

            // Source 2 leaves the descendants in place when the change looks like identity, which tolerates ~11 degrees of rotation.
            var delta = model.Concat(FrameBone.FromMatrix(previous).Inverse());
            if (IsApproxIdentity(delta) || !Matrix4x4.Invert(previous, out var previousInverse))
            {
                return;
            }

            var deltaMatrix = previousInverse * next;
            foreach (var descendant in constrained.Descendants)
            {
                pose[descendant] *= deltaMatrix;
            }
        }

        // CTransform equality against identity: 1e-4 on position and scale, 0.1 on each quaternion component.
        private static bool IsApproxIdentity(FrameBone transform)
        {
            const float PositionEpsilon = 0.0001f;
            const float RotationEpsilon = 0.1f;

            if (MathF.Abs(transform.Position.X) > PositionEpsilon || MathF.Abs(transform.Position.Y) > PositionEpsilon
                || MathF.Abs(transform.Position.Z) > PositionEpsilon || MathF.Abs(transform.Scale - 1f) > PositionEpsilon)
            {
                return false;
            }

            var q = transform.Angle;
            var matches = MathF.Abs(q.X) <= RotationEpsilon && MathF.Abs(q.Y) <= RotationEpsilon && MathF.Abs(q.Z) <= RotationEpsilon;
            return matches && (MathF.Abs(q.W - 1f) <= RotationEpsilon || MathF.Abs(q.W + 1f) <= RotationEpsilon);
        }

        private static Quaternion Align(Quaternion reference, Quaternion q) => Quaternion.Dot(reference, q) < 0f ? -q : q;

        // Log-space weighted average around the first rotation.
        private static Quaternion WeightedAverage(ReadOnlySpan<Quaternion> rotations, ReadOnlySpan<float> weights)
        {
            if (rotations.Length == 1)
            {
                return rotations[0];
            }

            var totalWeight = 0f;
            foreach (var weight in weights)
            {
                totalWeight += weight;
            }

            var inverseTotal = totalWeight > 0f ? 1f / totalWeight : 1f;

            // Log average depends on the first rotation's sign, which a matrix pose does not keep.
            var sum = Vector4.Zero;

            for (var i = 0; i < rotations.Length; i++)
            {
                var q = Align(rotations[0], rotations[i]);
                var length = new Vector3(q.X, q.Y, q.Z).Length();
                var angleScale = length >= 1e-5f ? MathF.Atan2(length, q.W) / length : 0f;
                sum += new Vector4(q.X * angleScale, q.Y * angleScale, q.Z * angleScale, 0.5f * MathF.Log(q.LengthSquared())) * (weights[i] * inverseTotal);
            }

            var angle = sum.AsVector3().Length();
            var magnitude = MathF.Exp(sum.W);
            var sineScale = angle >= 1e-5f ? MathF.Sin(angle) * magnitude / angle : 0f;
            return new Quaternion(sum.X * sineScale, sum.Y * sineScale, sum.Z * sineScale, MathF.Cos(angle) * magnitude);
        }
    }
}

using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Simulates the jiggle bones of a model as springs in world space after the animation has posed it.
    /// A bone points along its local X axis, with yaw about its Z axis and pitch about its Y axis.
    /// A bone lags behind its animated pose, and its descendants follow it.
    /// </summary>
    public sealed class JiggleBoneSolver
    {
        // Longer steps come from pauses and seeks, after which the bones start over from the animated pose
        private const float MaxTimeStep = 1f / 20f;
        private const float MinimumLengthSquared = 1e-12f;
        private const float ResetDot = -0.9f;

        private readonly Entry[] jiggleBones;
        private readonly int[][] descendants;
        private readonly State[] states;
        private readonly Matrix4x4[] animated;
        private readonly Matrix4x4?[] inverseAnimated;
        private readonly Collider[] colliders;
        private readonly Collider[] worldColliders;

        private readonly record struct Entry(int Bone, int Parent, FeModel.FeJiggleBone Jiggle);

        private readonly record struct Candidate(int SourceIndex, int ParentSourceIndex, int Bone, FeModel.FeJiggleBone Jiggle);

        // A sphere is a capsule whose two ends are the same sphere
        private readonly record struct Collider(int Bone, Vector4 Start, Vector4 End, int CollisionMask);

        private readonly record struct SimulationContext(Collider[] WorldColliders, float DeltaTime);

        private readonly struct Goal
        {
            public Goal(Matrix4x4 matrix, float sign, float length)
            {
                Sign = sign;
                Position = matrix.Translation;
                Forward = sign * new Vector3(matrix.M11, matrix.M12, matrix.M13);
                Left = sign * new Vector3(matrix.M21, matrix.M22, matrix.M23);
                Up = sign * new Vector3(matrix.M31, matrix.M32, matrix.M33);
                Tip = Position + (length * Forward);
            }

            public float Sign { get; }
            public Vector3 Position { get; }
            public Vector3 Forward { get; }
            public Vector3 Left { get; }
            public Vector3 Up { get; }
            public Vector3 Tip { get; }
        }

        private struct State
        {
            public bool IsInitialized;
            public Vector3 BasePosition;
            public Vector3 BaseLastPosition;
            public Vector3 BaseVelocity;
            public Vector3 BaseAcceleration;
            public Vector3 TipPosition;
            public Vector3 TipVelocity;
            public Vector3 TipAcceleration;
        }

        /// <summary>
        /// Builds the simulation of the jiggle bones in the soft body of a model. Nodes that are not bones of its skeleton are skipped.
        /// </summary>
        /// <param name="model">The model whose jiggle bones are simulated.</param>
        /// <returns>The simulation, or <see langword="null"/> when the model has no jiggle bones.</returns>
        public static JiggleBoneSolver? Create(Model model)
        {
            var feModel = model.GetEmbeddedPhys()?.FeModel;

            if (feModel is null || feModel.JiggleBones.Length == 0)
            {
                return null;
            }

            var skeleton = model.Skeleton;
            var nodeBones = GetNodeBones(skeleton, feModel);
            var entries = GetEntries(feModel.JiggleBones, nodeBones);

            if (entries.Length == 0)
            {
                return null;
            }

            var colliders = HasCollision(entries) ? GetColliders(feModel, nodeBones) : [];

            return new JiggleBoneSolver(skeleton, entries, colliders);
        }

        private JiggleBoneSolver(Skeleton skeleton, Entry[] entries, Collider[] colliders)
        {
            jiggleBones = entries;
            this.colliders = colliders;
            worldColliders = new Collider[colliders.Length];
            states = new State[entries.Length];
            animated = new Matrix4x4[entries.Length];
            inverseAnimated = new Matrix4x4?[entries.Length];
            descendants = new int[entries.Length][];

            for (var i = 0; i < entries.Length; i++)
            {
                descendants[i] = BoneConstraintSolver.GetDescendants(skeleton.Bones[entries[i].Bone]);
            }
        }

        /// <summary>
        /// Moves the jiggle bones and their descendants in a pose that holds the animated transforms.
        /// </summary>
        /// <param name="pose">Model space transforms of the bones.</param>
        /// <param name="modelToWorld">The transform of the model, which gives gravity its direction and the model's own movement its effect.</param>
        /// <param name="timeStep">Seconds since the last evaluation, above zero.</param>
        public void Evaluate(Matrix4x4[] pose, Matrix4x4 modelToWorld, float timeStep)
        {
            if (timeStep > MaxTimeStep)
            {
                Array.Clear(states);
                return;
            }

            if (!Matrix4x4.Decompose(modelToWorld, out _, out var rotation, out var translation))
            {
                return;
            }

            var frame = Matrix4x4.CreateFromQuaternion(rotation);
            frame.Translation = translation;

            if (!Matrix4x4.Invert(frame, out var inverseFrame))
            {
                return;
            }

            for (var i = 0; i < jiggleBones.Length; i++)
            {
                animated[i] = pose[jiggleBones[i].Bone];
                inverseAnimated[i] = Matrix4x4.Invert(animated[i], out var inverse) ? inverse : null;
            }

            var context = new SimulationContext(worldColliders, timeStep);

            for (var i = 0; i < jiggleBones.Length; i++)
            {
                var (bone, _, jiggle) = jiggleBones[i];
                var current = pose[bone];

                if (!Matrix4x4.Invert(current, out var inverseCurrent))
                {
                    continue;
                }

                // A collider can follow a jiggle bone that moved earlier in this loop
                if ((jiggle.BoneFlags & FeJiggleBoneFlags.Collision) != 0)
                {
                    UpdateWorldColliders(pose, frame);
                }

                var goalModel = GetGoal(i, pose);
                var simulated = Simulate(jiggle, ref states[i], goalModel * frame, in context);
                var simulatedModel = simulated * inverseFrame;
                var delta = inverseCurrent * simulatedModel;

                pose[bone] = simulatedModel;

                foreach (var descendant in descendants[i])
                {
                    pose[descendant] *= delta;
                }
            }
        }

        private void UpdateWorldColliders(Matrix4x4[] pose, Matrix4x4 modelToWorld)
        {
            for (var i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                var colliderToWorld = pose[collider.Bone] * modelToWorld;
                var start = Vector3.Transform(collider.Start.AsVector3(), colliderToWorld);
                var end = Vector3.Transform(collider.End.AsVector3(), colliderToWorld);

                worldColliders[i] = collider with
                {
                    Start = new Vector4(start, collider.Start.W),
                    End = new Vector4(end, collider.End.W),
                };
            }
        }

        private Matrix4x4 GetGoal(int index, Matrix4x4[] pose)
        {
            var (bone, parent, jiggle) = jiggleBones[index];

            if ((jiggle.BoneFlags & FeJiggleBoneFlags.GoalFromSimulatedPose) != 0)
            {
                return pose[bone];
            }

            if (parent < 0)
            {
                return animated[index];
            }

            var inverseAnimatedParent = inverseAnimated[parent];

            if (inverseAnimatedParent == null)
            {
                return animated[index];
            }

            var simulatedParent = pose[jiggleBones[parent].Bone];
            return animated[index] * inverseAnimatedParent.Value * simulatedParent;
        }

        private static Matrix4x4 Simulate(FeModel.FeJiggleBone jiggle, ref State state, Matrix4x4 goalMatrix, in SimulationContext context)
        {
            var flags = jiggle.BoneFlags;
            var sign = (flags & FeJiggleBoneFlags.InvertAxes) != 0 ? -1f : 1f;
            var goal = new Goal(goalMatrix, sign, jiggle.Length);

            if (!state.IsInitialized || IsFlipped(state, goal))
            {
                state = new State
                {
                    IsInitialized = true,
                    BasePosition = goal.Position,
                    BaseLastPosition = goal.Position,
                    TipPosition = goal.Tip,
                };
            }

            var direction = goal.Forward;
            var simulatesTip = (flags & (FeJiggleBoneFlags.Flexible | FeJiggleBoneFlags.Rigid)) != 0;

            if (simulatesTip)
            {
                direction = SimulateTip(jiggle, ref state, in goal, in context);
            }

            var result = CreateFrame(sign * direction, in goal);

            if ((flags & FeJiggleBoneFlags.BaseSpring) != 0)
            {
                result.Translation = SimulateBase(jiggle, ref state, in goal, in context);
            }

            return result;
        }

        // A tip that has flipped to the far side of its bone starts over from the goal
        private static bool IsFlipped(State state, in Goal goal)
        {
            var currentDirection = MathUtils.SafeNormalize(state.TipPosition - goal.Position, goal.Forward, MinimumLengthSquared);
            return Vector3.Dot(currentDirection, goal.Forward) < ResetDot;
        }

        private static Matrix4x4 CreateFrame(Vector3 forward, in Goal goal)
        {
            var goalUp = goal.Sign * goal.Up;
            var goalLeft = goal.Sign * goal.Left;
            var left = MathUtils.SafeNormalize(Vector3.Cross(goalUp, forward), goalLeft, MinimumLengthSquared);
            var up = Vector3.Cross(forward, left);

            var frame = MathUtils.CreateBasis(forward, left, up);
            frame.Translation = goal.Position;
            return frame;
        }

        /// <summary>Moves the tip and returns its direction from the goal position.</summary>
        private static Vector3 SimulateTip(FeModel.FeJiggleBone jiggle, ref State state, in Goal goal, in SimulationContext context)
        {
            var flags = jiggle.BoneFlags;
            var deltaTime = context.DeltaTime;

            var acceleration = state.TipAcceleration + new Vector3(0f, 0f, -jiggle.TipMass);
            state.TipAcceleration = Vector3.Zero;

            if ((flags & FeJiggleBoneFlags.Flexible) != 0)
            {
                var error = goal.Tip - state.TipPosition;
                var yawAcceleration = (jiggle.YawStiffness * Vector3.Dot(goal.Left, error)) - (jiggle.YawDamping * Vector3.Dot(goal.Left, state.TipVelocity));
                var pitchAcceleration = (jiggle.PitchStiffness * Vector3.Dot(goal.Up, error)) - (jiggle.PitchDamping * Vector3.Dot(goal.Up, state.TipVelocity));

                acceleration += (yawAcceleration * goal.Left) + (pitchAcceleration * goal.Up);

                if ((flags & FeJiggleBoneFlags.LengthConstraint) == 0)
                {
                    var alongAcceleration = (jiggle.AlongStiffness * Vector3.Dot(goal.Forward, error)) - (jiggle.AlongDamping * Vector3.Dot(goal.Forward, state.TipVelocity));
                    acceleration += alongAcceleration * goal.Forward;
                }
            }

            state.TipVelocity += acceleration * deltaTime;
            state.TipPosition += state.TipVelocity * deltaTime;

            if ((flags & FeJiggleBoneFlags.Collision) != 0)
            {
                Collide(jiggle, ref state, in goal, in context);
            }

            if ((flags & FeJiggleBoneFlags.YawConstraint) != 0)
            {
                ApplyAngleRange(ref state, goal.Position, goal.Forward, goal.Left, goal.Up,
                    jiggle.MinYaw, jiggle.MaxYaw, jiggle.YawFriction, jiggle.YawBounce);
            }

            if ((flags & FeJiggleBoneFlags.PitchConstraint) != 0)
            {
                ApplyAngleRange(ref state, goal.Position, goal.Forward, goal.Up, goal.Left,
                    jiggle.MinPitch, jiggle.MaxPitch, jiggle.PitchFriction, jiggle.PitchBounce);
            }

            var direction = MathUtils.SafeNormalize(state.TipPosition - goal.Position, goal.Forward, MinimumLengthSquared);

            if ((flags & FeJiggleBoneFlags.AngleConstraint) != 0)
            {
                var dot = Vector3.Dot(direction, goal.Forward);
                var angle = MathUtils.SafeAcos(dot);

                // The angle of a tip behind the bone counts on from a half turn
                if (dot < 0f)
                {
                    angle = MathF.Tau - angle;
                }

                if (angle > jiggle.AngleLimit)
                {
                    // The chord between two directions that are the limit angle apart on a circle of the bone's length
                    var maxDistance = 2f * jiggle.Length * MathF.Sin(jiggle.AngleLimit * 0.5f);
                    var awayFromGoal = MathUtils.SafeNormalize(state.TipPosition - goal.Tip, Vector3.Zero, MinimumLengthSquared);

                    state.TipPosition = goal.Tip + (maxDistance * awayFromGoal);
                    direction = MathUtils.SafeNormalize(state.TipPosition - goal.Position, goal.Forward, MinimumLengthSquared);
                }
            }

            if ((flags & FeJiggleBoneFlags.LengthConstraint) != 0)
            {
                state.TipPosition = goal.Position + (jiggle.Length * direction);
                state.TipVelocity = MathUtils.ProjectOntoPlane(state.TipVelocity, direction);
            }

            return direction;
        }

        /// <summary>
        /// Keeps the angle of the tip toward <paramref name="side"/> within a range. At a limit the tip slides onto the limit
        /// plane, which holds <paramref name="axis"/>, friction slows its motion along that plane and its velocity off the plane bounces.
        /// </summary>
        private static void ApplyAngleRange(ref State state, Vector3 goalPosition, Vector3 forward, Vector3 side, Vector3 axis,
            float min, float max, float friction, float bounce)
        {
            var along = state.TipPosition - goalPosition;
            var angle = MathF.Atan2(Vector3.Dot(side, along), Vector3.Dot(forward, along));

            if (!TryClamp(ref angle, min, max))
            {
                return;
            }

            var (sin, cos) = MathF.SinCos(angle);
            var limitForward = (cos * forward) + (sin * side);
            var limitNormal = (cos * side) - (sin * forward);

            state.TipPosition = goalPosition + (Vector3.Dot(axis, along) * axis) + (Vector3.Dot(limitForward, along) * limitForward);

            var normalVelocity = Vector3.Dot(limitNormal, state.TipVelocity);
            var planeVelocity = (Vector3.Dot(axis, state.TipVelocity) * axis) + (Vector3.Dot(limitForward, state.TipVelocity) * limitForward);

            state.TipAcceleration -= friction * planeVelocity;
            state.TipVelocity = planeVelocity - (bounce * normalVelocity * limitNormal);
        }

        private static Vector3 SimulateBase(FeModel.FeJiggleBone jiggle, ref State state, in Goal goal, in SimulationContext context)
        {
            var deltaTime = context.DeltaTime;
            var acceleration = state.BaseAcceleration + new Vector3(0f, 0f, -jiggle.BaseMass);
            var error = goal.Position - state.BasePosition;

            acceleration += (jiggle.BaseStiffness * error) - (jiggle.BaseDamping * state.BaseVelocity);

            state.BaseVelocity += acceleration * deltaTime;
            state.BasePosition += state.BaseVelocity * deltaTime;
            state.BaseAcceleration = Vector3.Zero;

            var offset = state.BasePosition - goal.Position;
            var local = new Vector3(Vector3.Dot(goal.Left, offset), Vector3.Dot(goal.Up, offset), Vector3.Dot(goal.Forward, offset));
            var localVelocity = new Vector3(Vector3.Dot(goal.Left, state.BaseVelocity), Vector3.Dot(goal.Up, state.BaseVelocity), Vector3.Dot(goal.Forward, state.BaseVelocity));

            if (TryClamp(ref local.X, jiggle.BaseMinLeft, jiggle.BaseMaxLeft))
            {
                state.BaseAcceleration -= jiggle.BaseLeftFriction * ((localVelocity.Y * goal.Up) + (localVelocity.Z * goal.Forward));
            }

            if (TryClamp(ref local.Y, jiggle.BaseMinUp, jiggle.BaseMaxUp))
            {
                state.BaseAcceleration -= jiggle.BaseUpFriction * ((localVelocity.X * goal.Left) + (localVelocity.Z * goal.Forward));
            }

            if (TryClamp(ref local.Z, jiggle.BaseMinForward, jiggle.BaseMaxForward))
            {
                state.BaseAcceleration -= jiggle.BaseForwardFriction * ((localVelocity.X * goal.Left) + (localVelocity.Y * goal.Up));
            }

            state.BasePosition = goal.Position + (local.X * goal.Left) + (local.Y * goal.Up) + (local.Z * goal.Forward);
            state.BaseVelocity = (state.BasePosition - state.BaseLastPosition) / deltaTime;
            state.BaseLastPosition = state.BasePosition;

            return state.BasePosition;
        }

        /// <summary>
        /// Pushes the tip out of the collision shapes of the soft body that overlap the capsule of the bone. The capsule
        /// stands in the bone frame of the tip before any push, and a push moves the tip by its depth scaled by how
        /// much farther the tip is from the bone origin than the contact. Afterwards the tip velocity loses its part
        /// along the total push.
        /// </summary>
        private static void Collide(FeModel.FeJiggleBone jiggle, ref State state, in Goal goal, in SimulationContext context)
        {
            var filtersByMask = (jiggle.BoneFlags & FeJiggleBoneFlags.CollisionMask) != 0;
            var direction = MathUtils.SafeNormalize(state.TipPosition - goal.Position, goal.Forward, MinimumLengthSquared);
            var bone = CreateFrame(goal.Sign * direction, in goal);
            var capsuleStart = Vector3.Transform(jiggle.Point0, bone);
            var capsuleEnd = Vector3.Transform(jiggle.Point1, bone);
            var tipBefore = state.TipPosition;

            foreach (var collider in context.WorldColliders)
            {
                if (filtersByMask && (collider.CollisionMask & jiggle.CollisionMask) == 0)
                {
                    continue;
                }

                var colliderStart = collider.Start.AsVector3();
                var colliderEnd = collider.End.AsVector3();

                var (capsuleT, colliderT) = GetClosestParameters(capsuleStart, capsuleEnd, colliderStart, colliderEnd);
                var capsulePoint = Vector3.Lerp(capsuleStart, capsuleEnd, capsuleT);
                var colliderPoint = Vector3.Lerp(colliderStart, colliderEnd, colliderT);
                var capsuleRadius = float.Lerp(jiggle.Radius0, jiggle.Radius1, capsuleT);
                var colliderRadius = float.Lerp(collider.Start.W, collider.End.W, colliderT);

                // A contact with a collider that already holds the start of the capsule is left to the other bones
                var startReach = colliderRadius + jiggle.Radius0;

                if (startReach * startReach >= Vector3.DistanceSquared(colliderPoint, capsuleStart))
                {
                    continue;
                }

                var separation = capsulePoint - colliderPoint;
                var distanceSquared = separation.LengthSquared();
                var radiusSum = capsuleRadius + colliderRadius;

                if (radiusSum * radiusSum <= distanceSquared || distanceSquared < MinimumLengthSquared)
                {
                    continue;
                }

                var distance = MathF.Sqrt(distanceSquared);
                var leverArm = float.Lerp(jiggle.Point0.X, jiggle.Point1.X, capsuleT);

                if (leverArm == 0f)
                {
                    continue;
                }

                var displacement = separation / distance * (radiusSum - distance);

                state.TipPosition += displacement * (jiggle.Length / leverArm);
            }

            var pushed = state.TipPosition - tipBefore;

            if (pushed != Vector3.Zero)
            {
                state.TipVelocity -= Vector3.Dot(state.TipVelocity, pushed) * pushed;
            }
        }

        /// <summary>
        /// Returns the parameter along <paramref name="p0"/>-<paramref name="p1"/> of the point closest to the line through
        /// <paramref name="q0"/>-<paramref name="q1"/>, and the parameter along the second segment of the point closest to it.
        /// </summary>
        private static (float S, float T) GetClosestParameters(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
        {
            var d1 = q1 - q0;
            var d2 = p1 - p0;
            var plane = Vector3.Cross(d1, d2);
            var toward = Vector3.Cross(d1, plane);
            var denominator = Vector3.Dot(d2, toward);
            var s = 0f;

            if (MathF.Abs(denominator) > MinimumLengthSquared)
            {
                s = MathUtils.Saturate(Vector3.Dot(q0 - p0, toward) / denominator);
            }

            var colliderLengthSquared = d1.LengthSquared();
            var t = 0f;

            if (colliderLengthSquared > MinimumLengthSquared)
            {
                var point = p0 + (s * d2);
                t = MathUtils.Saturate(Vector3.Dot(point - q0, d1) / colliderLengthSquared);
            }

            return (s, t);
        }

        private static int[] GetNodeBones(Skeleton skeleton, FeModel feModel)
        {
            var nodeNames = feModel.CtrlName;
            var nodeBones = new int[nodeNames.Length];

            for (var i = 0; i < nodeNames.Length; i++)
            {
                nodeBones[i] = skeleton.GetBoneIndex(nodeNames[i]);
            }

            return nodeBones;
        }

        private static Entry[] GetEntries(FeModel.FeIndexedJiggleBone[] source, int[] nodeBones)
        {
            var candidates = new List<Candidate>();

            for (var i = 0; i < source.Length; i++)
            {
                var indexed = source[i];

                if ((uint)indexed.Node >= (uint)nodeBones.Length || nodeBones[indexed.Node] < 0)
                {
                    continue;
                }

                candidates.Add(new Candidate(i, indexed.JiggleParent, nodeBones[indexed.Node], indexed.JiggleBone));
            }

            candidates.Sort(static (a, b) => a.Bone.CompareTo(b.Bone));

            var sortedIndices = new Dictionary<int, int>(candidates.Count);

            for (var i = 0; i < candidates.Count; i++)
            {
                sortedIndices[candidates[i].SourceIndex] = i;
            }

            var entries = new Entry[candidates.Count];

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                var parent = sortedIndices.GetValueOrDefault(candidate.ParentSourceIndex, -1);

                // A parent is simulated before its children, so one that sorts after the bone is ignored
                if (parent >= i)
                {
                    parent = -1;
                }

                entries[i] = new Entry(candidate.Bone, parent, candidate.Jiggle);
            }

            return entries;
        }

        private static bool HasCollision(Entry[] entries)
        {
            foreach (var entry in entries)
            {
                if ((entry.Jiggle.BoneFlags & FeJiggleBoneFlags.Collision) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Collider[] GetColliders(FeModel feModel, int[] nodeBones)
        {
            var list = new List<Collider>();

            foreach (var sphere in feModel.SphereRigids)
            {
                if (sphere.Sphere is { } shape && TryGetBone(nodeBones, sphere.Node, out var bone))
                {
                    list.Add(new Collider(bone, shape, shape, sphere.CollisionMask));
                }
            }

            foreach (var capsule in feModel.TaperedCapsuleRigids)
            {
                if (capsule.Spheres.Length >= 2 && TryGetBone(nodeBones, capsule.Node, out var bone))
                {
                    list.Add(new Collider(bone, capsule.Spheres[0], capsule.Spheres[1], capsule.CollisionMask));
                }
            }

            return [.. list];
        }

        private static bool TryGetBone(int[] nodeBones, int node, out int bone)
        {
            bone = -1;

            if ((uint)node >= (uint)nodeBones.Length)
            {
                return false;
            }

            bone = nodeBones[node];
            return bone >= 0;
        }

        private static bool TryClamp(ref float value, float min, float max)
        {
            if (value < min)
            {
                value = min;
                return true;
            }

            if (value > max)
            {
                value = max;
                return true;
            }

            return false;
        }
    }
}

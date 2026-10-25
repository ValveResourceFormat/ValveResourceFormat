using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// The hitboxes, or the bones, of the model a control point carries, refreshed at most once per
    /// system time and keeping the boxes of the refresh before, so functions can tell how the model
    /// moved. Each system holds one per control point, and the first function to read it at a given
    /// time decides whether it holds hitboxes or bones and which hitbox set. Without hitboxes, a
    /// system that falls back to render bounds reads one box around the model's bounds, or an empty
    /// box at the world origin when the control point carries no model.
    /// </summary>
    internal sealed class HitBoxCache
    {
        private const string DefaultHitboxSet = "default";
        private const float BoneBoxExtent = 8f;

        private HitBox[] current = [];
        private HitBox[] previous = [];
        private Matrix4x4[] pose = [];

        private Model? inverseBindPoseModel;
        private Matrix4x4[] inverseBindPose = [];

        private float boundsTime = float.NaN;
        private AABB bounds;

        /// <summary>How many boxes the last refresh found.</summary>
        public int Count { get; private set; }

        /// <summary>How many boxes the refresh before found.</summary>
        public int PreviousCount { get; private set; }

        /// <summary>The system time of the last refresh, or -1 before the first.</summary>
        public float UpdateTime { get; private set; } = -1f;

        /// <summary>The system time of the refresh before, or -1 when there was none.</summary>
        public float PreviousUpdateTime { get; private set; } = -1f;

        /// <summary>The sum of every box's <see cref="HitBox.Volume"/>.</summary>
        public float TotalVolume { get; private set; }

        /// <summary>The object the boxes were read from, or null when the control point carries none.</summary>
        public IParticleModelObject? Object { get; private set; }

        /// <summary>The boxes of the last refresh.</summary>
        public ReadOnlySpan<HitBox> Current => current.AsSpan(0, Count);

        /// <summary>The boxes of the refresh before.</summary>
        public ReadOnlySpan<HitBox> Previous => previous.AsSpan(0, PreviousCount);

        /// <summary>Whether there are boxes and the refresh before found as many, so the two can be paired.</summary>
        public bool IsStable => Count != 0 && Count == PreviousCount;

        /// <summary>
        /// Refreshes the cache of control point <paramref name="cp"/> unless it was already refreshed at
        /// the system's current time, and returns it. Null for a control point outside the valid range.
        /// </summary>
        /// <param name="systemState">The system whose control point to read.</param>
        /// <param name="cp">The control point.</param>
        /// <param name="hitboxSetName">The hitbox set to read, falling back to <c>default</c>.</param>
        /// <param name="useBones">Read one box per bone instead of the hitboxes.</param>
        /// <param name="computeVolumes">Give each hitbox its volume as its weight.</param>
        public static HitBoxCache? Refresh(ParticleSystemState systemState, int cp, string hitboxSetName, bool useBones, bool computeVolumes = false)
        {
            if ((uint)cp > ParticleSystemState.MaxControlPoint)
            {
                return null;
            }

            var controlPoint = systemState.GetControlPoint(cp);
            var cache = controlPoint.HitBoxes;

            if (cache.UpdateTime != systemState.Age)
            {
                cache.Update(systemState, controlPoint.ResolveModelObject(), hitboxSetName, useBones, computeVolumes);
            }

            return cache;
        }

        private void Update(ParticleSystemState systemState, IParticleModelObject? modelObject, string hitboxSetName, bool useBones, bool computeVolumes)
        {
            PreviousCount = Count;
            PreviousUpdateTime = UpdateTime;
            UpdateTime = systemState.Age;
            TotalVolume = 0f;
            (current, previous) = (previous, current);
            Object = modelObject;
            Count = 0;

            if (useBones)
            {
                if (modelObject != null)
                {
                    ReadBones(modelObject);
                }

                return;
            }

            if (modelObject != null)
            {
                ReadHitBoxes(modelObject, hitboxSetName, computeVolumes);
            }

            if (Count == 0 && systemState.Data is { HitboxesFallBackToRenderBounds: true } simulation)
            {
                var (transform, modelBounds) = modelObject == null
                    ? (Matrix4x4.Identity, new AABB(Vector3.Zero, Vector3.Zero))
                    : (modelObject.Transform, ModelElements.Bounds(modelObject.Model, simulation.FileLoader));
                var volume = computeVolumes ? Volume(modelBounds.Size) : 1f;

                EnsureCapacity(1);
                current[0] = HitBox.FromTransform(transform, modelBounds.Min, modelBounds.Max, volume);
                Count = 1;
                TotalVolume = volume;
            }
        }

        private void ReadBones(IParticleModelObject modelObject)
        {
            var bones = modelObject.Model.Skeleton.Bones;
            var transform = modelObject.Transform;
            var extent = new Vector3(BoneBoxExtent);

            if (bones.Length == 0)
            {
                EnsureCapacity(1);
                current[0] = HitBox.FromTransform(transform, -extent, extent, 1f);
                Count = 1;
                TotalVolume = 1f;
                return;
            }

            EnsureCapacity(bones.Length);
            var boneTransforms = Pose(modelObject);

            for (var i = 0; i < bones.Length; i++)
            {
                current[i] = HitBox.FromTransform(boneTransforms[i] * transform, -extent, extent, 1f);
            }

            Count = bones.Length;
            TotalVolume = Count;
        }

        private void ReadHitBoxes(IParticleModelObject modelObject, string hitboxSetName, bool computeVolumes)
        {
            var model = modelObject.Model;

            if (FindHitboxSet(model, hitboxSetName) is not { Length: > 0 } hitboxes)
            {
                return;
            }

            EnsureCapacity(hitboxes.Length);

            var skeleton = model.Skeleton;
            var transform = modelObject.Transform;
            var boneTransforms = skeleton.Bones.Length > 0 ? Pose(modelObject) : [];

            for (var i = 0; i < hitboxes.Length; i++)
            {
                var hitbox = hitboxes[i];
                var bone = skeleton.GetBoneIndex(hitbox.BoneName);
                var world = bone >= 0 && bone < boneTransforms.Length ? boneTransforms[bone] * transform : transform;
                var (mins, maxs) = ShapeBounds(hitbox);
                var volume = computeVolumes ? Volume(maxs - mins) : 1f;

                current[i] = HitBox.FromTransform(world, mins, maxs, volume);
                TotalVolume += volume;
            }

            Count = hitboxes.Length;
        }

        private static Hitbox[]? FindHitboxSet(Model model, string name)
        {
            Hitbox[]? fallback = null;

            foreach (var (setName, hitboxes) in model.HitboxSets)
            {
                if (string.Equals(setName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return hitboxes;
                }

                if (fallback == null && string.Equals(setName, DefaultHitboxSet, StringComparison.OrdinalIgnoreCase))
                {
                    fallback = hitboxes;
                }
            }

            return fallback;
        }

        private static (Vector3 Mins, Vector3 Maxs) ShapeBounds(Hitbox hitbox)
        {
            var radius = new Vector3(hitbox.ShapeRadius);

            return hitbox.ShapeType switch
            {
                Hitbox.HitboxShape.Sphere => (hitbox.MinBounds - radius, hitbox.MinBounds + radius),
                Hitbox.HitboxShape.Capsule => (Vector3.Min(hitbox.MinBounds, hitbox.MaxBounds) - radius, Vector3.Max(hitbox.MinBounds, hitbox.MaxBounds) + radius),
                _ => (hitbox.MinBounds, hitbox.MaxBounds),
            };
        }

        private static float Volume(Vector3 size) => size.X * size.Y * size.Z;

        private ReadOnlySpan<Matrix4x4> Pose(IParticleModelObject modelObject)
        {
            var boneCount = modelObject.Model.Skeleton.Bones.Length;

            if (pose.Length < boneCount)
            {
                pose = new Matrix4x4[boneCount];
            }

            var span = pose.AsSpan(0, boneCount);
            modelObject.GetBonePose(span);
            return span;
        }

        private void EnsureCapacity(int count)
        {
            if (current.Length < count)
            {
                Array.Resize(ref current, count);
            }

            if (previous.Length < count)
            {
                Array.Resize(ref previous, count);
            }
        }

        /// <summary>
        /// Picks a random point inside the boxes. Each of <see cref="HitBoxSampling.Tries"/> plus one
        /// candidates draws a box and a position in it, and with more than one try the candidate
        /// furthest along the direction bias from <paramref name="origin"/>, give or take a random
        /// amount, wins. Without boxes the point is <paramref name="origin"/> in box 0.
        /// </summary>
        /// <param name="systemState">The system whose random sequence the draws take.</param>
        /// <param name="origin">The point the direction bias is measured from.</param>
        /// <param name="sampling">How to pick.</param>
        /// <param name="haltonIndex">The next index of the even distribution sequence, advanced per candidate.</param>
        /// <returns>The point in the world, its fraction of the box's extent per axis, and the box.</returns>
        public (Vector3 Position, Vector3 Fraction, int Index) RandomPoint(ParticleSystemState systemState, Vector3 origin, in HitBoxSampling sampling, ref int haltonIndex)
        {
            if (Count == 0)
            {
                return (origin, Vector3.Zero, 0);
            }

            var random = systemState.Random;
            var (low, high) = HitBox.ScaledFractions(sampling.Scale);
            var bias = sampling.DirectionBias;
            var tries = sampling.Tries;

            if (HasDirectionBias(bias))
            {
                tries = Math.Max(tries, 5);
            }

            var first = sampling.DesiredHitbox >= 0 ? Math.Min(sampling.DesiredHitbox, Count - 1) : 0;
            var range = sampling.DesiredHitbox >= 0 ? 1 : Count;

            var best = -1e20f;
            (Vector3 Position, Vector3 Fraction, int Index) result = (origin, Vector3.Zero, 0);

            for (var candidate = 0; candidate <= Math.Max(tries, 0); candidate++)
            {
                var index = first + (int)(range * random.Next());

                if (sampling.WeightByVolume)
                {
                    index = PickByVolume(random.Next() * TotalVolume);
                }

                Vector3 fraction;

                if (sampling.EvenDistribution)
                {
                    var sample = (random.Seed + haltonIndex++) & 0x7FFFFFFF;
                    var halton = new Vector3(MathUtils.RadicalInverse(sample, 2), MathUtils.RadicalInverse(sample, 3), MathUtils.RadicalInverse(sample, 5));
                    fraction = MathUtils.Lerp(low, high, halton);
                }
                else
                {
                    fraction = random.NextBetweenPerComponentSeededTwice(low, high);
                }

                if (sampling.ShellSize != 1f)
                {
                    var axis = Math.Min((int)(random.Next() * 3f), 2);
                    var sign = (int)(random.Next() * 2f) == 1 ? 1f : -1f;
                    var depth = sampling.Scale[axis] * sampling.ShellSize * 0.5f * random.Next();
                    var start = sign >= 0f ? low[axis] : high[axis];
                    fraction[axis] = (depth * sign) + start;
                }

                var position = current[index].PointAt(fraction);
                var score = tries > 1 ? Vector3.Dot(position - origin, bias) + (random.Next() * 72f) : float.MaxValue;

                if (score > best)
                {
                    best = score;
                    result = (position, fraction, index);
                }
            }

            return result;
        }

        private int PickByVolume(float remaining)
        {
            for (var i = 0; i < Count; i++)
            {
                remaining -= current[i].Volume;

                if (remaining <= 0f)
                {
                    return i;
                }
            }

            return Count - 1;
        }

        /// <summary>Whether a direction bias is large enough for the picks to score their candidates along it.</summary>
        public static bool HasDirectionBias(Vector3 bias)
            => MathF.Abs(bias.X) >= 1e-4f || MathF.Abs(bias.Y) >= 1e-4f || MathF.Abs(bias.Z) >= 1e-4f;

        /// <summary>
        /// Whether <paramref name="point"/> lies inside any box, or with <paramref name="boundBox"/>
        /// inside the world aligned box around all of them.
        /// </summary>
        public bool Contains(Vector3 point, bool boundBox)
        {
            if (boundsTime != UpdateTime)
            {
                boundsTime = UpdateTime;
                bounds = new AABB(new Vector3(float.MaxValue), new Vector3(float.MinValue));

                foreach (ref readonly var box in Current)
                {
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var local = new Vector3((corner & 1) != 0 ? box.Maxs.X : box.Mins.X, (corner & 2) != 0 ? box.Maxs.Y : box.Mins.Y, (corner & 4) != 0 ? box.Maxs.Z : box.Mins.Z);
                        bounds = bounds.Encapsulate(box.TransformPoint(local));
                    }
                }
            }

            var inBounds = bounds.Contains(point);

            if (boundBox || !inBounds)
            {
                return inBounds;
            }

            foreach (ref readonly var box in Current)
            {
                if (new AABB(box.Mins, box.Maxs).Contains(box.InverseTransformPoint(point)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The box nearest to <paramref name="point"/>, or only <paramref name="desiredHitbox"/> when it
        /// is not negative, and a place in it as a fraction of the box's extent per axis.
        /// </summary>
        /// <remarks>
        /// Without <paramref name="closestPoint"/> the nearest box is the one whose origin is nearest,
        /// and every box that comes nearer than the ones before draws a random place in the part of
        /// it <paramref name="scale"/> covers. With it, the point is carried into each box's space
        /// and measured against the closest point of the box shaped as <paramref name="testType"/>
        /// says, and that closest point, scaled about the box centre by <paramref name="scale"/>, is
        /// the place. A box with no extent is passed over. Without boxes the answer is box 0 at
        /// fraction 0.
        /// </remarks>
        public (Vector3 Fraction, int Index) Closest(ParticleSystemState systemState, Vector3 point, int desiredHitbox, Vector3 scale,
            bool closestPoint, ClosestPointTestType testType, float hybridRatio)
        {
            if (Count == 0)
            {
                return (Vector3.Zero, 0);
            }

            var first = desiredHitbox >= 0 ? Math.Min(desiredHitbox, Count - 1) : 0;
            var last = desiredHitbox >= 0 ? first : Count - 1;
            var (low, high) = HitBox.ScaledFractions(scale);
            var random = systemState.Random;

            var bestDistance = float.MaxValue;
            var result = (Fraction: Vector3.Zero, Index: 0);

            for (var i = first; i <= last; i++)
            {
                ref readonly var box = ref current[i];

                if (!closestPoint)
                {
                    var distance = Vector3.DistanceSquared(point, box.Position);

                    if (distance >= bestDistance)
                    {
                        continue;
                    }

                    bestDistance = distance;
                    result = (random.NextBetweenPerComponent(low, high), i);
                    continue;
                }

                var local = box.InverseTransformPoint(point);
                var nearest = testType switch
                {
                    ClosestPointTestType.PARTICLE_CLOSEST_TYPE_BOX => Vector3.Clamp(local, box.Mins, box.Maxs),
                    ClosestPointTestType.PARTICLE_CLOSEST_TYPE_CAPSULE => ClosestPointOnCapsule(box.Mins, box.Maxs, local),
                    _ => MathUtils.Lerp(ClosestPointOnCapsule(box.Mins, box.Maxs, local), Vector3.Clamp(local, box.Mins, box.Maxs), hybridRatio),
                };
                var nearestDistance = Vector3.DistanceSquared(local, nearest);
                var size = box.Maxs - box.Mins;

                if (nearestDistance >= bestDistance || size == Vector3.Zero)
                {
                    continue;
                }

                var fraction = new Vector3(
                    size.X != 0f ? (nearest.X - box.Mins.X) / size.X : 0.5f,
                    size.Y != 0f ? (nearest.Y - box.Mins.Y) / size.Y : 0.5f,
                    size.Z != 0f ? (nearest.Z - box.Mins.Z) / size.Z : 0.5f);

                bestDistance = nearestDistance;
                result = (((fraction - new Vector3(0.5f)) * scale) + new Vector3(0.5f), i);
            }

            return result;
        }

        private static Vector3 ClosestPointOnCapsule(Vector3 mins, Vector3 maxs, Vector3 point)
        {
            var size = maxs - mins;
            var radius = (size.X + size.Y + size.Z) * 0.25f;
            var axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.X && size.Y >= size.Z ? 1 : 2;
            var offset = Vector3.Zero;
            offset[axis] = (size[axis] * 0.5f) - radius;
            var center = (mins + maxs) * 0.5f;
            var start = center + offset;
            var segment = center - offset - start;

            var along = Vector3.Dot(point - start, segment);
            var lengthSquared = segment.LengthSquared();
            var onSegment = along <= 0f ? start
                : along <= lengthSquared ? start + (segment * (along / lengthSquared))
                : start + segment;

            var distance = Vector3.Distance(point, onSegment);

            return radius + ParticleMath.FloatEpsilon < distance
                ? onSegment + ((point - onSegment) * (radius / distance))
                : point;
        }

        /// <summary>
        /// The velocity of box <paramref name="index"/> between the refresh before and the last one, or
        /// null when the box was not there both times or no time passed between them.
        /// </summary>
        public Vector3? BoxVelocity(int index)
        {
            var interval = UpdateTime - PreviousUpdateTime;

            if ((uint)index >= (uint)Count || (uint)index >= (uint)PreviousCount || PreviousUpdateTime == -1f || interval <= 0f)
            {
                return null;
            }

            return (current[index].Position - previous[index].Position) / interval;
        }

        /// <summary>
        /// The inverse of every bone's bind pose in model space, for the model the boxes were read
        /// from. Empty when there is no model.
        /// </summary>
        public ReadOnlySpan<Matrix4x4> InverseBindPose
        {
            get
            {
                var model = Object?.Model;

                if (model == null)
                {
                    return [];
                }

                if (inverseBindPoseModel != model)
                {
                    inverseBindPoseModel = model;
                    inverseBindPose = BindPoseModelObject.GetInverseBindPose(model);
                }

                return inverseBindPose;
            }
        }

        /// <summary>
        /// The linear blend skinning transform of a particle bound to up to four bones of the boxes
        /// read as bones: each valid bone's bind pose inverse followed by its box transform, weighted
        /// by <paramref name="weights"/> and the remainder of 1 for the fourth. A bone is valid when it
        /// is not 0xFFFF and both this and, with <paramref name="computePrevious"/>, the refresh before
        /// hold it. A single valid bone is taken whole.
        /// </summary>
        /// <param name="boneIndices">Four 16-bit bone indices from the low bits up.</param>
        /// <param name="weights">The weights of the first three bones.</param>
        /// <param name="computePrevious">Also build the transform of the refresh before.</param>
        /// <param name="skin">The transform at the last refresh.</param>
        /// <param name="previousSkin">The transform at the refresh before, identity unless asked for.</param>
        /// <returns>Whether any bone was valid.</returns>
        public bool ComputeSkinningMatrix(ulong boneIndices, Vector3 weights, bool computePrevious, out Matrix4x4 skin, out Matrix4x4 previousSkin)
        {
            skin = Matrix4x4.Identity;
            previousSkin = Matrix4x4.Identity;

            var inverseBind = InverseBindPose;
            var lastWeight = MathUtils.Saturate(1f - weights.X - weights.Y - weights.Z);
            Span<float> allWeights = [weights.X, weights.Y, weights.Z, lastWeight];
            Span<int> valid = stackalloc int[PackedBones.Count];
            Span<float> validWeights = stackalloc float[PackedBones.Count];
            var validCount = 0;

            for (var k = 0; k < PackedBones.Count; k++)
            {
                var bone = PackedBones.Get(boneIndices, k);

                if (bone == PackedBones.None || bone >= Count || (computePrevious && bone >= PreviousCount) || bone >= inverseBind.Length)
                {
                    continue;
                }

                valid[validCount] = bone;
                validWeights[validCount] = allWeights[k];
                validCount++;
            }

            if (validCount == 0)
            {
                return false;
            }

            skin = Blend(current, inverseBind, valid[..validCount], validWeights[..validCount]);

            if (computePrevious)
            {
                previousSkin = Blend(previous, inverseBind, valid[..validCount], validWeights[..validCount]);
            }

            return true;
        }

        private static Matrix4x4 Blend(HitBox[] boxes, ReadOnlySpan<Matrix4x4> inverseBind, ReadOnlySpan<int> bones, ReadOnlySpan<float> weights)
        {
            if (bones.Length == 1)
            {
                return inverseBind[bones[0]] * boxes[bones[0]].Matrix;
            }

            var result = new Matrix4x4();

            for (var i = 0; i < bones.Length; i++)
            {
                result += inverseBind[bones[i]] * boxes[bones[i]].Matrix * weights[i];
            }

            result.M14 = 0f;
            result.M24 = 0f;
            result.M34 = 0f;
            result.M44 = 1f;
            return result;
        }
    }
}

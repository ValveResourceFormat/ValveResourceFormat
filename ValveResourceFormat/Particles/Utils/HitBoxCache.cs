using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// One box of a <see cref="HitBoxCache"/>: a model hitbox, or a bone, placed in the world.
    /// </summary>
    /// <param name="Mins">The box's low corner, in the space of its transform.</param>
    /// <param name="Maxs">The box's high corner, in the space of its transform.</param>
    /// <param name="Orientation">The rotation of the transform.</param>
    /// <param name="Position">The world position of the transform.</param>
    /// <param name="Scale">The uniform scale of the transform.</param>
    /// <param name="Volume">The box's volume when the cache was asked to weigh boxes by volume, 1 otherwise.</param>
    internal readonly record struct HitBox(Vector3 Mins, Vector3 Maxs, Quaternion Orientation, Vector3 Position, float Scale, float Volume)
    {
        /// <summary>Carries a point from the box's space into the world.</summary>
        public Vector3 TransformPoint(Vector3 local) => (Vector3.Transform(local, Orientation) * Scale) + Position;

        /// <summary>
        /// The world position of the point at <paramref name="fraction"/> of the box's extent per axis,
        /// from its low corner at 0 to its high corner at 1.
        /// </summary>
        public Vector3 PointAt(Vector3 fraction) => TransformPoint(((Maxs - Mins) * fraction) + Mins);

        /// <summary>Carries a world point into the box's space.</summary>
        public Vector3 InverseTransformPoint(Vector3 world)
            => Vector3.Transform((world - Position) / Scale, Quaternion.Conjugate(Orientation));

        /// <summary>The transform as a matrix, scale included.</summary>
        public Matrix4x4 Matrix
        {
            get
            {
                var matrix = Matrix4x4.CreateFromQuaternion(Orientation) * Scale;
                matrix.M44 = 1f;
                matrix.Translation = Position;
                return matrix;
            }
        }

        /// <summary>Splits a transform into the rotation, position and largest axis scale a box keeps.</summary>
        public static HitBox FromTransform(Matrix4x4 transform, Vector3 mins, Vector3 maxs, float volume)
        {
            var scale = transform.MaxAxisScale();
            var rotation = transform;
            rotation.Translation = Vector3.Zero;

            if (scale > 0f)
            {
                rotation = NormalizeRows(rotation);
            }

            var orientation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
            return new HitBox(mins, maxs, orientation, transform.Translation, scale, volume);
        }

        private static Matrix4x4 NormalizeRows(Matrix4x4 m)
        {
            var x = MathUtils.SafeNormalize(m.GetRow(0).AsVector3());
            var y = MathUtils.SafeNormalize(m.GetRow(1).AsVector3());
            var z = MathUtils.SafeNormalize(m.GetRow(2).AsVector3());

            return new Matrix4x4(
                x.X, x.Y, x.Z, 0f,
                y.X, y.Y, y.Z, 0f,
                z.X, z.Y, z.Z, 0f,
                0f, 0f, 0f, 1f);
        }
    }

    /// <summary>
    /// The hitboxes, or the bones, of the model a control point carries, refreshed at most once per
    /// system time and keeping the boxes of the refresh before, so functions can tell how the model
    /// moved. Each system holds one per control point, and the first function to read it at a given
    /// time decides whether it holds hitboxes or bones and which hitbox set.
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
        private Vector3 boundsMin;
        private Vector3 boundsMax;

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
            var fallbackToRenderBounds = systemState.Data?.Definition.GetBooleanProperty("m_bShouldHitboxesFallbackToRenderBounds") ?? false;
            cache.Update(systemState.Age, controlPoint.ResolveModelObject(), hitboxSetName, useBones, computeVolumes, fallbackToRenderBounds);
            return cache;
        }

        private void Update(float time, IParticleModelObject? modelObject, string hitboxSetName, bool useBones, bool computeVolumes, bool fallbackToRenderBounds)
        {
            if (UpdateTime == time)
            {
                return;
            }

            PreviousCount = Count;
            PreviousUpdateTime = UpdateTime;
            UpdateTime = time;
            TotalVolume = 0f;
            (current, previous) = (previous, current);
            Object = modelObject;
            Count = 0;

            if (modelObject == null)
            {
                return;
            }

            if (useBones)
            {
                ReadBones(modelObject);
            }
            else
            {
                ReadHitBoxes(modelObject, hitboxSetName, computeVolumes);
            }

            if (Count == 0 && fallbackToRenderBounds)
            {
                var (mins, maxs) = ModelElements.Bounds(modelObject.Model);
                var size = maxs - mins;
                var volume = computeVolumes ? size.X * size.Y * size.Z : 1f;

                EnsureCapacity(1);
                current[0] = HitBox.FromTransform(modelObject.Transform, mins, maxs, volume);
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
                var size = maxs - mins;
                var volume = computeVolumes ? size.X * size.Y * size.Z : 1f;

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
            var high = (sampling.Scale * 0.5f) + new Vector3(0.5f);
            var low = new Vector3(0.5f) - (sampling.Scale * 0.5f);
            var bias = sampling.DirectionBias;
            var tries = sampling.Tries;

            if (MathF.Abs(bias.X) >= 1e-4f || MathF.Abs(bias.Y) >= 1e-4f || MathF.Abs(bias.Z) >= 1e-4f)
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
                    fraction = low + ((high - low) * new Vector3(Halton(sample, 2), Halton(sample, 3), Halton(sample, 5)));
                }
                else
                {
                    fraction = new Vector3(random.NextDoublySeeded(), random.NextDoublySeeded(), random.NextDoublySeeded());
                    fraction = ((high - low) * fraction) + low;
                }

                if (sampling.ShellSize != 1f)
                {
                    var axis = Math.Min((int)(random.Next() * 3f), 2);
                    var sign = (int)(random.Next() * 2f) == 1 ? 1f : -1f;
                    var depth = sampling.Scale.GetComponent(axis) * sampling.ShellSize * 0.5f * random.Next();
                    var start = sign >= 0f ? low.GetComponent(axis) : high.GetComponent(axis);
                    fraction = SetComponent(fraction, axis, (depth * sign) + start);
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

        private static Vector3 SetComponent(Vector3 vector, int component, float value) => component switch
        {
            0 => vector with { X = value },
            1 => vector with { Y = value },
            _ => vector with { Z = value },
        };

        /// <summary>The radical inverse of <paramref name="index"/> in <paramref name="radix"/>, one coordinate of a Halton sequence.</summary>
        private static float Halton(int index, int radix)
        {
            var result = 0f;
            var digitWeight = 1f / radix;

            while (index > 0)
            {
                result += index % radix * digitWeight;
                index /= radix;
                digitWeight /= radix;
            }

            return result;
        }

        /// <summary>
        /// Whether <paramref name="point"/> lies inside any box, or with <paramref name="boundBox"/>
        /// inside the world aligned box around all of them.
        /// </summary>
        public bool Contains(Vector3 point, bool boundBox)
        {
            if (boundsTime != UpdateTime)
            {
                boundsTime = UpdateTime;
                boundsMin = new Vector3(float.MaxValue);
                boundsMax = new Vector3(float.MinValue);

                foreach (var box in Current)
                {
                    for (var corner = 0; corner < 8; corner++)
                    {
                        var local = new Vector3((corner & 1) != 0 ? box.Maxs.X : box.Mins.X, (corner & 2) != 0 ? box.Maxs.Y : box.Mins.Y, (corner & 4) != 0 ? box.Maxs.Z : box.Mins.Z);
                        var world = box.TransformPoint(local);
                        boundsMin = Vector3.Min(boundsMin, world);
                        boundsMax = Vector3.Max(boundsMax, world);
                    }
                }
            }

            var inBounds = point.X >= boundsMin.X && point.Y >= boundsMin.Y && point.Z >= boundsMin.Z
                && point.X <= boundsMax.X && point.Y <= boundsMax.Y && point.Z <= boundsMax.Z;

            if (boundBox || !inBounds)
            {
                return inBounds;
            }

            foreach (var box in Current)
            {
                var local = box.InverseTransformPoint(point);

                if (local.X >= box.Mins.X && local.Y >= box.Mins.Y && local.Z >= box.Mins.Z
                    && local.X <= box.Maxs.X && local.Y <= box.Maxs.Y && local.Z <= box.Maxs.Z)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The box nearest to <paramref name="point"/>, or only <paramref name="desiredHitbox"/> when it
        /// is not negative, and where in it the point lies as a fraction of the box's extent per axis.
        /// With <paramref name="closestPoint"/> the fraction is held to the part of the box
        /// <paramref name="scale"/> covers, which puts it on the closest point of that part. Without
        /// boxes the answer is box 0 at fraction 0.
        /// </summary>
        public (Vector3 Fraction, int Index) Closest(Vector3 point, int desiredHitbox, Vector3 scale, bool closestPoint)
        {
            if (Count == 0)
            {
                return (Vector3.Zero, 0);
            }

            var first = desiredHitbox >= 0 ? Math.Min(desiredHitbox, Count - 1) : 0;
            var last = desiredHitbox >= 0 ? first : Count - 1;
            var high = (scale * 0.5f) + new Vector3(0.5f);
            var low = Vector3.One - high;

            var bestDistance = float.MaxValue;
            var result = (Fraction: Vector3.Zero, Index: first);

            for (var i = first; i <= last; i++)
            {
                var box = current[i];
                var local = box.InverseTransformPoint(point);
                var size = box.Maxs - box.Mins;
                var fraction = new Vector3(
                    size.X != 0f ? (local.X - box.Mins.X) / size.X : 0.5f,
                    size.Y != 0f ? (local.Y - box.Mins.Y) / size.Y : 0.5f,
                    size.Z != 0f ? (local.Z - box.Mins.Z) / size.Z : 0.5f);
                var clamped = Vector3.Clamp(fraction, low, high);
                var distance = Vector3.DistanceSquared(box.PointAt(clamped), point);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    result = (closestPoint ? clamped : fraction, i);
                }
            }

            return result;
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
                    inverseBindPose = new Matrix4x4[model.Skeleton.Bones.Length];
                    ParticleModelObject.GetBindPose(model, inverseBindPose);

                    for (var i = 0; i < inverseBindPose.Length; i++)
                    {
                        Matrix4x4.Invert(inverseBindPose[i], out inverseBindPose[i]);
                    }
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
            var lastWeight = Math.Clamp(1f - weights.X - weights.Y - weights.Z, 0f, 1f);
            Span<float> allWeights = [weights.X, weights.Y, weights.Z, lastWeight];
            Span<int> valid = stackalloc int[4];
            Span<float> validWeights = stackalloc float[4];
            var validCount = 0;

            for (var k = 0; k < 4; k++)
            {
                var bone = (int)((boneIndices >> (16 * k)) & 0xFFFF);

                if (bone == 0xFFFF || bone >= Count || (computePrevious && bone >= PreviousCount) || bone >= inverseBind.Length)
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

    /// <summary>How <see cref="HitBoxCache.RandomPoint"/> picks a point.</summary>
    /// <param name="Tries">How many candidates past the first to draw.</param>
    /// <param name="DesiredHitbox">The only box to pick, or -1 for any.</param>
    /// <param name="Scale">The part of each box's extent to pick from, centred on the box, per axis.</param>
    /// <param name="DirectionBias">The direction from the origin candidates are scored along.</param>
    /// <param name="ShellSize">Below 1, pushes the point onto a face, keeping this fraction of the scaled extent.</param>
    /// <param name="EvenDistribution">Spread the positions within a box along a Halton sequence instead of at random.</param>
    /// <param name="WeightByVolume">Pick boxes in proportion to their volume.</param>
    internal readonly record struct HitBoxSampling(int Tries, int DesiredHitbox, Vector3 Scale, Vector3 DirectionBias, float ShellSize, bool EvenDistribution, bool WeightByVolume);
}

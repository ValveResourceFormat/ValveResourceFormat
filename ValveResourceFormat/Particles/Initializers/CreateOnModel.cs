using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places new particles at random points inside the hitboxes, or the bones, of the model its model
    /// input reads, recording the box and the point's place in it for the functions that keep
    /// particles on the model. Particles can inherit the velocity of their box.
    /// </summary>
    /// <remarks>
    /// With <c>m_bUseMesh</c> particles are placed on the triangles of the model's render meshes
    /// instead, posed by its bones and pushed along the triangle normal by the shell size minus 1.
    /// The triangle is picked by area among 16 random candidates. A model whose skinned meshes
    /// carry no bone remap table falls back to the hitboxes.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateOnModel">C_INIT_CreateOnModel</seealso>
    class CreateOnModel : ParticleFunctionInitializer
    {
        private readonly int modelControlPoint;
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly int forceInModel;
        private readonly bool scaleToVolume;
        private readonly bool evenDistribution;
        private readonly INumberProvider desiredHitbox = new LiteralNumberProvider(-1f);
        private readonly int hitboxValueFromControlPoint = -1;
        private readonly IVectorProvider hitBoxScale = new LiteralVectorProvider(Vector3.One);
        private readonly float boneVelocity;
        private readonly float maxBoneVelocity;
        private readonly IVectorProvider directionBias = new LiteralVectorProvider(Vector3.Zero);
        private readonly string hitboxSetName = "default";
        private readonly bool localCoords;
        private readonly bool useBones;
        private readonly INumberProvider shellSize = new LiteralNumberProvider(1f);
        private readonly bool useMesh;

        private const int MeshCandidates = 16;

        private int meshRandomCounter;
        private Matrix4x4[] meshSkin = [];
        private Matrix4x4 meshRigid;
        private float meshSkinTime = float.NaN;

        public CreateOnModel(ParticleDefinitionParser parse) : base(parse)
        {
            modelControlPoint = ModelInput.ControlPoint(parse);
            transformInput = parse.TransformInput("m_transformInput", transformInput);
            forceInModel = parse.Int32("m_nForceInModel", forceInModel);
            scaleToVolume = parse.Boolean("m_bScaleToVolume", scaleToVolume);
            evenDistribution = parse.Boolean("m_bEvenDistribution", evenDistribution);
            desiredHitbox = parse.CollectionNumberProvider("m_nDesiredHitbox", desiredHitbox);
            hitboxValueFromControlPoint = parse.Int32("m_nHitboxValueFromControlPointIndex", hitboxValueFromControlPoint);
            hitBoxScale = parse.CollectionVectorProvider("m_vecHitBoxScale", hitBoxScale);
            boneVelocity = parse.Float("m_flBoneVelocity", boneVelocity);
            maxBoneVelocity = parse.Float("m_flMaxBoneVelocity", maxBoneVelocity);
            directionBias = parse.CollectionVectorProvider("m_vecDirectionBias", directionBias);
            hitboxSetName = parse.String("m_HitboxSetName", hitboxSetName);
            localCoords = parse.Boolean("m_bLocalCoords", localCoords);
            useBones = parse.Boolean("m_bUseBones", useBones);
            shellSize = parse.CollectionNumberProvider("m_flShellSize", shellSize);
            useMesh = parse.Boolean("m_bUseMesh", useMesh);
        }

        public override void Reset()
        {
            meshRandomCounter = 0;
            meshSkinTime = float.NaN;
        }

        public override ulong WrittenFields
            => ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.CreationTime.Mask()
                | ParticleField.HitboxIndex.Mask() | ParticleField.HitboxOffsetPosition.Mask()
                | (useMesh ? ParticleField.Normal.Mask() | ParticleField.BoneIndices.Mask() | ParticleField.BoneWeights.Mask() : 0UL);

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var shell = shellSize.NextNumber(particleSystemState);
            var desired = hitboxValueFromControlPoint >= 0
                ? (int)particleSystemState.GetControlPoint(hitboxValueFromControlPoint).Position.X
                : (int)desiredHitbox.NextNumber(particleSystemState);
            var scale = hitBoxScale.NextVector(particleSystemState);
            var directionBiasValue = directionBias.NextVector(particleSystemState);

            var creationTimes = run.ReadFloats(ParticleField.CreationTime);
            var positions = run.Vectors(ParticleField.Position);
            var previous = SpawnPreviousPositions(run, particleSystemState);
            var hitboxIndices = UnwrittenIntegers(run, particleSystemState, ParticleField.HitboxIndex);
            var hitboxOffsets = UnwrittenVectors(run, particleSystemState, ParticleField.HitboxOffsetPosition);
            var normals = useMesh ? UnwrittenVectors(run, particleSystemState, ParticleField.Normal) : default;
            var boneIndices = useMesh && IsUnwritten(particleSystemState, ParticleField.BoneIndices) ? run.BoneIndices : default;
            var boneWeights = useMesh ? UnwrittenVectors(run, particleSystemState, ParticleField.BoneWeights) : default;

            var haltonIndex = particleSystemState.SpawnIndex(run.Start);

            for (var i = 0; i < run.Length; i++)
            {
                if (useMesh && TryPickOnMesh(particleSystemState, shell, out var meshPoint, out var meshNormal, out var meshBones))
                {
                    positions[i] = meshPoint;

                    if (!previous.IsEmpty)
                    {
                        previous[i] = meshPoint;
                    }

                    if (!hitboxOffsets.IsEmpty)
                    {
                        hitboxOffsets[i] = meshPoint;
                    }

                    if (!normals.IsEmpty)
                    {
                        normals[i] = meshNormal;
                    }

                    if (!hitboxIndices.IsEmpty)
                    {
                        hitboxIndices[i] = 0;
                    }

                    if (!boneIndices.IsEmpty)
                    {
                        boneIndices[i] = meshBones;
                    }

                    if (!boneWeights.IsEmpty)
                    {
                        boneWeights[i] = Vector3.UnitX;
                    }

                    continue;
                }

                var transform = transformInput.NextTransformAtTime(particleSystemState, creationTimes[i]);
                var bias = directionBiasValue;

                if (localCoords)
                {
                    bias = Vector3.TransformNormal(bias, transform);
                }

                if (modelControlPoint < 0 || HitBoxCache.Refresh(particleSystemState, modelControlPoint, hitboxSetName, useBones, scaleToVolume && !useBones) is not { } cache)
                {
                    continue;
                }

                var sampling = new HitBoxSampling(forceInModel, desired, scale, bias, shell, evenDistribution, scaleToVolume && !useBones);
                var (position, fraction, index) = cache.RandomPoint(particleSystemState, transform.Translation, sampling, ref haltonIndex);

                positions[i] = position;

                if (!hitboxIndices.IsEmpty)
                {
                    hitboxIndices[i] = index;
                }

                if (!hitboxOffsets.IsEmpty)
                {
                    hitboxOffsets[i] = fraction;
                }

                if (previous.IsEmpty)
                {
                    continue;
                }

                previous[i] = position;

                if (boneVelocity != 0f && cache.BoxVelocity(index) is { } velocity)
                {
                    velocity *= boneVelocity;

                    if (maxBoneVelocity > 0f)
                    {
                        velocity = MathUtils.ClampLength(velocity, maxBoneVelocity);
                    }

                    previous[i] -= velocity * particleSystemState.PreviousStep;
                }
            }
        }

        private bool TryPickOnMesh(ParticleSystemState particleSystemState, float shell, out Vector3 point, out Vector3 normal, out ulong bones)
        {
            point = Vector3.Zero;
            normal = Vector3.Zero;
            bones = 0;

            if (modelControlPoint < 0
                || particleSystemState.GetControlPoint(modelControlPoint).ResolveModelObject() is not { } modelObject
                || MeshTriangles.Get(modelObject.Model, particleSystemState.Data?.FileLoader) is not { TriangleCount: > 0 } mesh)
            {
                return false;
            }

            Span<int> candidates = stackalloc int[MeshCandidates];
            var totalArea = 0f;

            for (var i = 0; i < MeshCandidates; i++)
            {
                candidates[i] = Math.Min((int)(NextMeshRandom(particleSystemState) * mesh.TriangleCount), mesh.TriangleCount - 1);
                totalArea += mesh.Area(candidates[i]);
            }

            var pick = Math.Min(NextMeshRandom(particleSystemState) * totalArea, totalArea);
            var triangle = candidates[0];

            foreach (var candidate in candidates)
            {
                var area = mesh.Area(candidate);

                if (pick <= area)
                {
                    triangle = candidate;
                    break;
                }

                pick -= area;
            }

            PoseSkin(particleSystemState, modelObject, mesh);

            if (!TrySkin(mesh.Corner(triangle, 0), out var v0)
                || !TrySkin(mesh.Corner(triangle, 1), out var v1)
                || !TrySkin(mesh.Corner(triangle, 2), out var v2))
            {
                return false;
            }

            var u = NextMeshRandom(particleSystemState);
            var v = NextMeshRandom(particleSystemState);

            if (u + v > 1f)
            {
                (u, v) = (1f - u, 1f - v);
            }

            normal = MathUtils.SafeNormalize(MathUtils.TriangleCross(v0, v1, v2));
            point = (v1 * u) + (v0 * (1f - u - v)) + (v2 * v) + ((shell - 1f) * normal);
            bones = mesh.Corner(triangle, 0).Bones;

            return true;
        }

        private float NextMeshRandom(ParticleSystemState particleSystemState)
            => ParticleRandom.ForSample(particleSystemState.Random.Seed + meshRandomCounter++);

        /// <summary>
        /// Works out, once per step, every bone's skinning transform into the world and the transform
        /// a rigid vertex follows: the first bone's pose, or the model's placement when it has no bones.
        /// </summary>
        private void PoseSkin(ParticleSystemState particleSystemState, IParticleModelObject modelObject, MeshTriangles mesh)
        {
            var boneCount = mesh.InverseBindPose.Length;

            if (meshSkinTime == particleSystemState.Age && meshSkin.Length == boneCount)
            {
                return;
            }

            meshSkinTime = particleSystemState.Age;

            if (meshSkin.Length != boneCount)
            {
                meshSkin = new Matrix4x4[boneCount];
            }

            modelObject.GetBonePose(meshSkin);
            meshRigid = boneCount > 0 ? meshSkin[0] * modelObject.Transform : modelObject.Transform;

            for (var i = 0; i < boneCount; i++)
            {
                meshSkin[i] = mesh.InverseBindPose[i] * meshSkin[i] * modelObject.Transform;
            }
        }

        /// <summary>
        /// Moves a vertex into the world by its bones, failing for a skinned vertex whose bones were
        /// not mapped to the model's.
        /// </summary>
        private bool TrySkin(MeshTriangles.Vertex vertex, out Vector3 world)
        {
            if (vertex.Weights.X <= 0f)
            {
                world = Vector3.Transform(vertex.Position, meshRigid);
                return true;
            }

            world = Vector3.Zero;

            if (!vertex.Remapped)
            {
                return false;
            }

            for (var k = 0; k < PackedBones.Count; k++)
            {
                var bone = PackedBones.Get(vertex.Bones, k);
                var weight = vertex.Weights[k];

                if (weight > 0f && bone < meshSkin.Length)
                {
                    world += Vector3.Transform(vertex.Position, meshSkin[bone]) * weight;
                }
            }

            return true;
        }
    }
}

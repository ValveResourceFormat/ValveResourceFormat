using System.Runtime.InteropServices;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places particles on a phyllotaxis (sunflower) spiral around a control point, each one a fixed
    /// angle further round and further out than the last, and sets their radius to grow along the
    /// spiral. The spiral lies in the world XY plane unless local coordinates turn it into the control
    /// point's frame.
    /// </summary>
    /// <remarks>
    /// The spiral is sized for the particles placed so far plus those in the emission, so each
    /// emission stretches it; an emission of 16 or more particles is sized in up to three runs, as
    /// <see cref="ParticleFunctionInitializer.SplitIntoCalls"/> splits it. Nothing is placed while
    /// either radius per point is 0. The point count keeps running across restarts.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreatePhyllotaxis">C_INIT_CreatePhyllotaxis</seealso>
    class CreatePhyllotaxis : ParticleFunctionInitializer
    {
        private readonly int controlPointNumber;
        private readonly int scaleCP = -1;
        private readonly int component;
        private readonly float radCentCore;
        private readonly float radPerPoint = 1f;
        private readonly float radPerPointTo = -1f;
        private readonly float pointAngle = 137.508f;
        private readonly float sizeOverall = 60f;
        private readonly float radBias = 0.5f;
        private readonly float minRad = 0.25f;
        private readonly float distBias = 0.5f;
        private readonly bool useLocalCoords;
        private readonly bool useWithContEmit;
        private readonly bool useOrigRadius = true;

        private int pointIndex;

        public CreatePhyllotaxis(ParticleDefinitionParser parse) : base(parse)
        {
            controlPointNumber = parse.Int32("m_nControlPointNumber", controlPointNumber);
            scaleCP = parse.Int32("m_nScaleCP", scaleCP);
            component = parse.Int32("m_nComponent", component);
            radCentCore = parse.Float("m_fRadCentCore", radCentCore);
            radPerPoint = parse.Float("m_fRadPerPoint", radPerPoint);
            radPerPointTo = parse.Float("m_fRadPerPointTo", radPerPointTo);
            pointAngle = parse.Float("m_fpointAngle", pointAngle);
            sizeOverall = parse.Float("m_fsizeOverall", sizeOverall);
            radBias = parse.Float("m_fRadBias", radBias);
            minRad = parse.Float("m_fMinRad", minRad);
            distBias = parse.Float("m_fDistBias", distBias);
            useLocalCoords = parse.Boolean("m_bUseLocalCoords", useLocalCoords);
            useWithContEmit = parse.Boolean("m_bUseWithContEmit", useWithContEmit);
            useOrigRadius = parse.Boolean("m_bUseOrigRadius", useOrigRadius);
        }

        public override void Reset()
        {
            pointIndex = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override ulong WrittenFields => FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.Radius);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            Initialize(MemoryMarshal.CreateSpan(ref particle, 1), particles, particleSystemState);
            return particle;
        }

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            InitializeCall(batch[head], particleSystemState);
            InitializeCall(batch[blocks], particleSystemState);
            InitializeCall(batch[tail], particleSystemState);
        }

        private void InitializeCall(Span<Particle> batch, ParticleSystemState particleSystemState)
        {
            if (batch.IsEmpty || radPerPoint == 0f || radPerPointTo == 0f)
            {
                return;
            }

            var controlPoint = particleSystemState.GetControlPoint(controlPointNumber);
            float totalPoints = pointIndex + batch.Length;

            var scale = 1f;
            var radiusScale = 1f;

            if (scaleCP != -1 && component != -1)
            {
                scale = component > 2 ? 0f : particleSystemState.GetControlPoint(scaleCP).Position.GetComponent(component);
                radiusScale = (MathUtils.Saturate((scale - 100f) / 400f) * 4f) + 1f;
            }

            var radiusPerPoint = MathF.Abs(radPerPoint);
            var radiusTo = radPerPointTo != -1f ? MathF.Abs(radPerPointTo) : 1f;
            var size = MathF.Abs(sizeOverall) * scale;
            var rotation = controlPoint.GetRotationMatrix();

            foreach (ref var particle in batch)
            {
                float n = pointIndex;
                var fraction = n / totalPoints;
                var bias = radBias == 0.5f ? 1f : ParticleMath.BiasClamped(fraction, radBias);

                float radius;
                float distance;

                if (sizeOverall == -1f || useWithContEmit)
                {
                    radius = MathF.Sqrt(n * MathF.Tau);
                    distance = n;
                }
                else
                {
                    if (radPerPointTo == -1f)
                    {
                        radius = (MathF.Sqrt(n * MathF.Tau * bias * 0.1f) * radiusScale * radiusPerPoint) + minRad;
                    }
                    else
                    {
                        var growth = ((radiusTo - minRad) * (bias * n) / totalPoints) + minRad;
                        radius = growth * radiusScale * radiusPerPoint;
                    }

                    distance = (ParticleMath.BiasClamped(MathF.Sqrt(fraction), distBias) * size) + radCentCore;
                }

                var angle = float.DegreesToRadians(n * pointAngle);
                var offset = new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);

                if (useLocalCoords)
                {
                    offset = Vector3.TransformNormal(offset, rotation);
                }

                particle.Position = controlPoint.Position + offset;
                particle.PositionPrevious = particle.Position;
                particle.Radius = useOrigRadius ? radius * particle.Radius : radius;

                pointIndex++;
            }
        }
    }
}

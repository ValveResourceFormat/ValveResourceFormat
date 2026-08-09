namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Rotates each particle's normal by however much a control point's frame rotated since the previous
    /// frame, so normals stay locked to that frame as the control point turns. A particle younger than
    /// one step only receives the fraction of the rotation covering the part of the step it existed for.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_NormalLock">C_OP_NormalLock</seealso>
    class NormalLock : ParticleFunctionOperator
    {
        private readonly int controlPoint;
        private readonly ControlPointTransformProvider transform;

        private Matrix4x4 previousTransform = Matrix4x4.Identity;
        private bool hasPrevious;

        public override void Reset()
        {
            previousTransform = Matrix4x4.Identity;
            hasPrevious = false;
        }

        public NormalLock(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
            transform = new ControlPointTransformProvider(controlPoint, true);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var current = transform.NextTransform(ref Particle.Default, particleSystemState);

            if (!hasPrevious)
            {
                var controlPointState = particleSystemState.GetControlPoint(controlPoint);
                previousTransform = controlPointState.PreviousStepTime > 0f
                    ? Matrix4x4.CreateFromQuaternion(controlPointState.GetPreviousRotation())
                        * Matrix4x4.CreateTranslation(controlPointState.PositionPrevious)
                    : current;
                hasPrevious = true;
            }

            var delta = Matrix4x4.Identity;

            if (Matrix4x4.Invert(previousTransform, out var previousInverse))
            {
                delta = previousInverse * current;
            }

            foreach (ref var particle in particles.Current)
            {
                var ratio = frameTime > 0f
                    ? MathUtils.Saturate(MathF.Min(particle.Age, frameTime) / frameTime)
                    : 1f;

                var rotated = Vector3.TransformNormal(particle.Normal, delta);

                particle.Normal = Vector3.Lerp(particle.Normal, rotated, ratio);
            }

            previousTransform = current;
        }
    }
}

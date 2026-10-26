namespace ValveResourceFormat.Particles.Operators
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

        public NormalLock(ParticleDefinitionParser parse) : base(parse)
        {
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            transform = new ControlPointTransformProvider(controlPoint, true);
        }

        public override void Reset()
        {
            previousTransform = Matrix4x4.Identity;
            hasPrevious = false;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Normal.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var currentTransform = transform.NextTransform(particleSystemState);

            if (!hasPrevious)
            {
                var controlPointState = particleSystemState.GetControlPoint(controlPoint);
                previousTransform = controlPointState.PreviousStepTime > 0f
                    ? Matrix4x4.CreateFromQuaternion(controlPointState.GetPreviousRotation())
                        * Matrix4x4.CreateTranslation(controlPointState.PositionPrevious)
                    : currentTransform;
                hasPrevious = true;
            }

            var delta = Matrix4x4.Identity;

            if (Matrix4x4.Invert(previousTransform, out var previousInverse))
            {
                delta = previousInverse * currentTransform;
            }

            var current = particles.Current;
            var normals = current.Vectors(ParticleField.Normal);
            var ages = current.ReadAges();

            for (var i = 0; i < normals.Length; i++)
            {
                var ratio = MathF.Min(ages[i], frameTime) / frameTime;

                var rotated = Vector3.TransformNormal(normals[i], delta);

                normals[i] = Vector3.Lerp(normals[i], rotated, ratio);
            }

            previousTransform = currentTransform;
        }
    }
}

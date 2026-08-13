namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Writes an angle derived from a transform input's orientation, offset by
    /// <c>m_flOffsetRot</c> degrees, into a rotation field at spawn, in radians. The angle depends
    /// only on the transform, so every particle spawned in a frame receives the same value.
    /// </summary>
    /// <remarks>
    /// From behavior version 7 the value is the offset minus the Euler component selected by
    /// <c>m_nComponent</c>; below 7 the component is ignored and the forward vector's atan2 yaw is
    /// added to the offset instead, shifted by a further pi below version 4.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RemapInitialTransformDirectionToRotation">C_INIT_RemapInitialTransformDirectionToRotation</seealso>
    class RemapInitialTransformDirectionToRotation : ParticleFunctionInitializer
    {
        private readonly ITransformProvider transformInput = new ControlPointTransformProvider();
        private readonly ParticleField fieldOutput = ParticleField.Yaw;
        private readonly float offsetRot;
        private readonly int component = 1;
        private readonly bool useEulerComponent;
        private readonly bool addPi;

        public RemapInitialTransformDirectionToRotation(ParticleDefinitionParser parse) : base(parse)
        {
            transformInput = parse.TransformInput("m_TransformInput", transformInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            offsetRot = parse.Float("m_flOffsetRot", offsetRot);
            component = parse.Int32("m_nComponent", component);
            useEulerComponent = parse.BehaviorVersion >= 7;
            addPi = parse.BehaviorVersion < 4;
        }

        public override ulong WrittenFields => FieldMask(fieldOutput);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var rotation = Quaternion.CreateFromRotationMatrix(transformInput.NextTransform(particleSystemState));
            var offset = float.DegreesToRadians(offsetRot);

            float value;
            if (useEulerComponent)
            {
                var angles = EntityTransformHelper.ToEulerAngles(rotation);
                value = offset - float.DegreesToRadians(angles.GetComponent(component));
            }
            else
            {
                var forward = Vector3.Transform(Vector3.UnitX, rotation);
                value = offset + MathF.Atan2(forward.Y, forward.X) + (addPi ? MathF.PI : 0f);
            }

            particle.SetScalar(fieldOutput, value);

            return particle;
        }
    }
}

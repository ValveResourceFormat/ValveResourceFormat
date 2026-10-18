namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Where a particle function's model input takes its model from.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleModelType_t">ParticleModelType_t</seealso>
    public enum ParticleModelType
    {
        /// <summary>No model.</summary>
        PM_TYPE_INVALID = 0,
        /// <summary>A model named value.</summary>
        PM_TYPE_NAMED_VALUE_MODEL = 1,
        /// <summary>The model of the entity an entity handle named value holds.</summary>
        PM_TYPE_NAMED_VALUE_EHANDLE = 2,
        /// <summary>The model bound to a control point.</summary>
        PM_TYPE_CONTROL_POINT = 3,
    }
}

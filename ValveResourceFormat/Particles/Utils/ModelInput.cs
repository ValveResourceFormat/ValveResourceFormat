using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Utils
{
    /// <summary>
    /// A function's <c>m_modelInput</c>: where it takes the model it works on from. Only the model a
    /// control point carries is supported.
    /// </summary>
    internal static class ModelInput
    {
        /// <summary>
        /// The control point the model input reads, or -1 when it reads a named value or nothing. A
        /// missing input reads control point 0.
        /// </summary>
        public static int ControlPoint(ParticleDefinitionParser parse, string key = "m_modelInput")
        {
            if (parse.Data.GetSubCollection(key) is not { IsCollection: true } data)
            {
                return 0;
            }

            var input = parse.Nested(data);

            return input.Enum("m_nType", ParticleModelType.PM_TYPE_CONTROL_POINT) == ParticleModelType.PM_TYPE_CONTROL_POINT
                ? input.ControlPoint("m_nControlPoint")
                : -1;
        }
    }
}

namespace ValveResourceFormat.ResourceTypes.Choreo.Data
{
    public class ChoreoFlexSample
    {
        private static string[] Interpolators = [
            "default",
            "catmullrom_normalize_x",
            "easein",
            "easeout",
            "easeinout",
            "bspline",
            "linear_interp",
            "kochanek",
            "kochanek_early",
            "kochanek_late",
            "simple_cubic",
            "catmullrom",
            "catmullrom_normalize",
            "catmullrom_tangent",
            "exponential_decay",
            "hold"
        ];

        public float Time { get; private set; }
        public float Value { get; private set; }
        public byte FromCurveIndex { get; private set; }
        public byte ToCurveIndex { get; private set; }
        public string FromCurve
        {
            get
            {
                return Interpolators[FromCurveIndex];
            }
        }
        public string ToCurve
        {
            get
            {
                return Interpolators[ToCurveIndex];
            }
        }
        public ChoreoFlexSample(float time, float value, byte fromCurveIndex, byte toCurveIndex)
        {
            Time = time;
            Value = value;
            FromCurveIndex = fromCurveIndex;
            ToCurveIndex = toCurveIndex;
        }
    }
}

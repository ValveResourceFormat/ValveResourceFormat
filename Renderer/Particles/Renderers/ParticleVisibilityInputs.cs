using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Scales a renderer's alpha and radius for a whole draw by how the camera sees a control point:
    /// its distance from the camera and the angle between them, each remapped to 0 to 1 and multiplied
    /// together, then taken from the minimum to the maximum scale.
    /// </summary>
    /// <remarks>
    /// There is no occlusion query, so the control point always counts as fully in view.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/CParticleVisibilityInputs">CParticleVisibilityInputs</seealso>
    internal readonly struct ParticleVisibilityInputs
    {
        private readonly int controlPoint = -1;
        private readonly float proxyRadius = 1f;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float distanceInputMin;
        private readonly float distanceInputMax;
        private readonly float dotInputMin;
        private readonly float dotInputMax;
        private readonly bool dotControlPointAngles = true;
        private readonly bool dotCameraAngles;
        private readonly float alphaScaleMin;
        private readonly float alphaScaleMax = 1f;
        private readonly float radiusScaleMin = 1f;
        private readonly float radiusScaleMax = 1f;
        private readonly float radiusScaleFovBase;

        public ParticleVisibilityInputs()
        {
        }

        public ParticleVisibilityInputs(ParticleDefinitionParser parse)
        {
            CameraBias = parse.Float("m_flCameraBias", CameraBias);
            controlPoint = parse.Int32("m_nCPin", controlPoint);
            proxyRadius = parse.Float("m_flProxyRadius", proxyRadius);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            distanceInputMin = parse.Float("m_flDistanceInputMin", distanceInputMin);
            distanceInputMax = parse.Float("m_flDistanceInputMax", distanceInputMax);
            dotInputMin = parse.Float("m_flDotInputMin", dotInputMin);
            dotInputMax = parse.Float("m_flDotInputMax", dotInputMax);
            dotControlPointAngles = parse.Boolean("m_bDotCPAngles", dotControlPointAngles);
            dotCameraAngles = parse.Boolean("m_bDotCameraAngles", dotCameraAngles);
            alphaScaleMin = parse.Float("m_flAlphaScaleMin", alphaScaleMin);
            alphaScaleMax = parse.Float("m_flAlphaScaleMax", alphaScaleMax);
            radiusScaleMin = parse.Float("m_flRadiusScaleMin", radiusScaleMin);
            radiusScaleMax = parse.Float("m_flRadiusScaleMax", radiusScaleMax);
            radiusScaleFovBase = parse.Float("m_flRadiusScaleFOVBase", radiusScaleFovBase);
        }

        /// <summary>How far each particle is drawn moved toward the camera, in world units.</summary>
        public float CameraBias { get; }

        /// <summary>
        /// The alpha and radius scales for a draw seen from <paramref name="camera"/>. Both are 1 when
        /// neither a control point nor a radius FOV base is set.
        /// </summary>
        public (float Alpha, float Radius) Evaluate(ParticleSystemState systemState, Camera camera)
        {
            if (controlPoint < 0 && radiusScaleFovBase <= 0f)
            {
                return (1f, 1f);
            }

            var visibility = 1f;

            if (controlPoint >= 0)
            {
                var point = systemState.GetControlPoint(controlPoint);
                var toPoint = point.Position - camera.Location;

                // The game multiplies the query result by the proxy radius remapped over the input
                // range, not by the remapped result; the result itself counts as fully visible here
                if (inputMin != inputMax)
                {
                    visibility = RemapClamped(proxyRadius, inputMin, inputMax);
                }

                if (dotInputMin != dotInputMax)
                {
                    var from = dotCameraAngles ? camera.Forward : MathUtils.SafeNormalize(toPoint);
                    var to = dotControlPointAngles ? point.Forward : MathUtils.SafeNormalize(-toPoint);

                    visibility *= RemapClamped(Vector3.Dot(from, to), dotInputMin, dotInputMax);
                }

                if (distanceInputMin != distanceInputMax)
                {
                    visibility *= RemapClamped(toPoint.Length(), distanceInputMin, distanceInputMax);
                }
            }

            var alpha = MathUtils.Lerp(alphaScaleMin, alphaScaleMax, visibility);
            var radius = MathUtils.Lerp(radiusScaleMin, radiusScaleMax, visibility);

            return (alpha, radius);
        }

        private static float RemapClamped(float value, float inputMin, float inputMax)
            => MathUtils.RemapValClamped(value, inputMin, inputMax, 0f, 1f);
    }
}

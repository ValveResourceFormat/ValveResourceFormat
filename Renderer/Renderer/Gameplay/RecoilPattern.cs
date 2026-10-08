namespace ValveResourceFormat.Renderer.Gameplay;

// The angle and magnitude of the kick for each shot in a spray, generated from a weapon's recoil keys
// in weapons.vdata.
internal sealed class RecoilPattern
{
    // Shots per mode; the shot index wraps past it
    public const int Length = 64;

    private const int ModeCount = 2;
    private const float FullAutoSmoothing = 0.55f;
    private const int SuppressionShots = 4;
    private const float SuppressionFactor = 0.75f;

    private readonly (float Angle, float Magnitude)[] table = new (float, float)[ModeCount * Length];

    // Takes m_nRecoilSeed, m_bIsFullAuto, and the two modes of m_flRecoilAngle, m_flRecoilAngleVariance,
    // m_flRecoilMagnitude and m_flRecoilMagnitudeVariance
    public RecoilPattern(int seed, bool fullAuto, float[] angle, float[] angleVariance, float[] magnitude, float[] magnitudeVariance)
    {
        var random = new UniformRandomStream();

        for (var mode = 0; mode < ModeCount; mode++)
        {
            random.SetSeed(seed);

            var shotAngle = 0f;
            var shotMagnitude = 0f;

            for (var shot = 0; shot < Length; shot++)
            {
                var newAngle = angle[mode] + random.RandomFloat(-angleVariance[mode], angleVariance[mode]);
                var newMagnitude = magnitude[mode] + random.RandomFloat(-magnitudeVariance[mode], magnitudeVariance[mode]);

                // Automatic fire drifts from one kick to the next rather than jumping around
                if (fullAuto && shot > 0)
                {
                    shotAngle += (newAngle - shotAngle) * FullAutoSmoothing;
                    shotMagnitude += (newMagnitude - shotMagnitude) * FullAutoSmoothing;
                }
                else
                {
                    shotAngle = newAngle;
                    shotMagnitude = newMagnitude;
                }

                // and its first few kicks ramp up to full strength
                if (fullAuto && shot < SuppressionShots)
                {
                    shotMagnitude *= float.Lerp(SuppressionFactor, 1f, shot / (float)SuppressionShots);
                }

                table[mode * Length + shot] = (shotAngle, shotMagnitude);
            }
        }
    }

    // The kick for a shot at the weapon's recoil index
    public (float Angle, float Magnitude) this[int mode, int shot] => table[mode * Length + (shot & (Length - 1))];
}

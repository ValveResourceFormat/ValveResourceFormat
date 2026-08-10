namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// The listener's state for one <see cref="AudioMixer.Update"/>, threaded down the sound event tree to
/// the spatializers. Everything time-based in there (occlusion smoothing, doppler) has to run off the
/// real elapsed time rather than per-update steps, or it changes character with the frame rate.
/// </summary>
/// <param name="Position">World position of the listener.</param>
/// <param name="Forward">Unit vector the listener faces.</param>
/// <param name="Right">Unit vector towards the listener's right ear.</param>
/// <param name="Up">Unit vector out of the top of the listener's head, which elevation is measured against.</param>
/// <param name="Velocity">How fast the listener is moving, in units per second.</param>
/// <param name="DeltaTime">Real time elapsed since the previous update, in seconds. Zero when there was no previous update.</param>
/// <param name="PanSharpness">How hard sounds pull towards the near ear (see <see cref="SoundEventPlayer.PositionalSharpness"/>).</param>
/// <param name="SpectralCueStrength">How strongly above/below and front/behind are voiced (see <see cref="SoundEventPlayer.SpectralCueStrength"/>).</param>
/// <param name="SampleRate">Mixer output rate, which the cue filters are tuned against.</param>
public readonly record struct ListenerState(Vector3 Position, Vector3 Forward, Vector3 Right, Vector3 Up, Vector3 Velocity,
    float DeltaTime, float PanSharpness = 1f, float SpectralCueStrength = 0f, int SampleRate = 48000);

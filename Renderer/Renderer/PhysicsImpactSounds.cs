using Box3D;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Plays the knocks of physics contacts: each simulated body that strikes something sounds like
/// its own surface, soft or hard by how fast it hit. A pile of props makes contacts every step, so
/// most hits are never heard; this decides which ones are.
/// </summary>
internal sealed class PhysicsImpactSounds(SurfaceProperties surfaces)
{
    // Slower hits are silent. The volume grows with the square of the speed up to full at
    // FullVolumeSpeed, so the knocks of something settling are faint and a throw is loud
    private const float MinImpactSpeed = 70f;
    private const float FullVolumeSpeed = 320f;
    private const float MissingHardSoundBoost = 1.2f;

    // A body is heard only after this long out of touch. Every contact restarts the wait, heard or
    // not: a rattling stack or a carried prop pressed into a wall hits again every step and stays
    // silent, while a drop, a throw and each bounce after it are heard.
    private const float QuietPeriod = 0.1f;

    // How many impact sounds may start at once, and how many a second after that. A blast
    // scattering a room of props plays its loudest few, not one per prop.
    private const float BurstBudget = 6f;
    private const float BudgetPerSecond = 12f;

    private readonly record struct Impact(string Sound, Vector3 Position, float Volume);

    private readonly Dictionary<ulong, float> lastContactTimes = [];
    private readonly Dictionary<ulong, Impact> stepImpacts = [];
    private readonly List<Impact> heard = [];
    private readonly HashSet<string> playedThisStep = [];
    private readonly HashSet<string> cachedSounds = [];
    private float time;
    private float budget = BurstBudget;

    /// <summary>
    /// Queues a surface's impact sounds for decoding, so the first hit does not wait for them.
    /// </summary>
    public void Prepare(uint surfaceHash)
    {
        var surface = surfaces.Find(surfaceHash);

        foreach (var sound in (ReadOnlySpan<string?>)[surface.ImpactSoftSound, surface.ImpactHardSound])
        {
            if (sound != null && cachedSounds.Add(sound))
            {
                Sound.Cache(sound);
            }
        }
    }

    /// <summary>Drops what is kept about a body that is being destroyed.</summary>
    public void Forget(ulong bodyId) => lastContactTimes.Remove(bodyId);

    /// <summary>Plays the impacts of the step just taken.</summary>
    /// <param name="hits">The step's contact hits.</param>
    /// <param name="timeStep">How long the step was.</param>
    public void Play(WorldEvents.ContactHitEventList hits, float timeStep)
    {
        time += timeStep;
        budget = MathF.Min(BurstBudget, budget + (timeStep * BudgetPerSecond));

        stepImpacts.Clear();

        foreach (var hit in hits)
        {
            // The player's pushing hull shoves props along rather than striking them
            if (!hit.ShapeA.IsValid || !hit.ShapeB.IsValid
                || ((hit.ShapeA.Filter.Categories | hit.ShapeB.Filter.Categories) & PhysicsSimulation.PlayerCategory) != 0)
            {
                continue;
            }

            Note(hit.ShapeA.Body, (uint)hit.UserMaterialIdA, hit.Point, hit.ApproachSpeed);
            Note(hit.ShapeB.Body, (uint)hit.UserMaterialIdB, hit.Point, hit.ApproachSpeed);
        }

        heard.Clear();

        foreach (var (bodyId, impact) in stepImpacts)
        {
            var wasQuiet = !lastContactTimes.TryGetValue(bodyId, out var lastContact) || time - lastContact >= QuietPeriod;
            lastContactTimes[bodyId] = time;

            if (wasQuiet && impact.Volume > 0f)
            {
                heard.Add(impact);
            }
        }

        if (heard.Count == 0)
        {
            return;
        }

        // Loudest first, and one of each sound: several crates landing at once are one knock
        heard.Sort(static (a, b) => b.Volume.CompareTo(a.Volume));
        playedThisStep.Clear();

        foreach (var impact in heard)
        {
            if (budget < 1f)
            {
                break;
            }

            if (!playedThisStep.Add(impact.Sound))
            {
                continue;
            }

            budget -= 1f;
            Sound.Play(impact.Sound, impact.Position, volumeScale: impact.Volume);
        }
    }

    // A body's loudest hit of the step. Only a simulated body sounds: the world and the movers are
    // what gets struck.
    private void Note(Body body, uint surfaceHash, Vector3 position, float speed)
    {
        if (body.Type != BodyType.Dynamic || body.UserData == 0)
        {
            return;
        }

        var impact = default(Impact);

        if (speed >= MinImpactSpeed)
        {
            var surface = surfaces.Find(surfaceHash);
            var volume = MathF.Min(1f, speed * speed / (FullVolumeSpeed * FullVolumeSpeed));
            var sound = volume >= surface.ImpactHardThreshold
                ? surface.ImpactHardSound ?? surface.ImpactSoftSound
                : surface.ImpactSoftSound ?? surface.ImpactHardSound;

            // Some surfaces name a hard sound that does not exist; their soft one stands in, louder
            if (sound != null && sound == surface.ImpactHardSound && surface.ImpactSoftSound != null && !Sound.Exists(sound))
            {
                sound = surface.ImpactSoftSound;
                volume *= MissingHardSoundBoost;
            }

            if (sound != null)
            {
                impact = new Impact(sound, position, volume);
            }
        }

        // Too slow to hear still counts as a contact, which is what keeps the quiet period honest
        if (!stepImpacts.TryGetValue(body.UserData, out var loudest) || impact.Volume > loudest.Volume)
        {
            stepImpacts[body.UserData] = impact;
        }
    }
}

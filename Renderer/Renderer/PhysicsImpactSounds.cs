using Box3D;

namespace ValveResourceFormat.Renderer;

internal sealed class PhysicsImpactSounds(SurfaceProperties surfaces)
{
    private const float MinImpactSpeed = 70f;
    private const float FullVolumeSpeed = 320f;
    private const float MissingHardSoundBoost = 1.2f;

    // Bodies in constant contact, like a rattling stack or a prop pressed into a wall, stay silent
    private const float QuietPeriod = 0.1f;

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

    public void Forget(ulong bodyId) => lastContactTimes.Remove(bodyId);

    public void Play(WorldEvents.ContactHitEventList hits, float timeStep)
    {
        time += timeStep;
        budget = MathF.Min(BurstBudget, budget + (timeStep * BudgetPerSecond));

        stepImpacts.Clear();

        foreach (var hit in hits)
        {
            // The player hull pushes props rather than striking them
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

    // Static and kinematic bodies are struck, not heard
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

            // Some surfaces name a hard sound that does not exist
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

        // Silent hits still restart the quiet period
        if (!stepImpacts.TryGetValue(body.UserData, out var loudest) || impact.Volume > loudest.Volume)
        {
            stepImpacts[body.UserData] = impact;
        }
    }
}

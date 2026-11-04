using System.IO;
using System.Linq;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_choreographed_scene</c>. Plays a choreographed scene from its <c>vcdlist</c> on <c>Start</c>: its speech
/// plays at the actors, and its triggers fire <c>OnTrigger1</c> to <c>OnTrigger16</c> on time. Not played: animation,
/// look at, face, gestures and every other actor performance, and interrupts.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CSceneEntity">CSceneEntity</seealso>
public sealed class LogicChoreographedScene : BaseEntity
{
    private readonly record struct Cue(float Time, ChoreoEventType Type, string Parameter, string Actor);

    /// <summary>Gets whether the scene is playing, paused or not.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Gets whether the scene is paused by the <c>Pause</c> input.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Gets the seconds the scene has played.</summary>
    public float SceneTime { get; private set; }

    private readonly List<Cue> cues = [];
    private readonly List<SoundHandle> playingSounds = [];
    private float duration;
    private int nextCue;
    private float lastThinkTime;
    private BaseEntity? activator;

    /// <summary>Initializes a <c>logic_choreographed_scene</c> from its keyvalues.</summary>
    public LogicChoreographedScene(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        if (IsPlaying || !TryLoadScene())
        {
            return;
        }

        activator = data.Activator;
        IsPlaying = true;
        IsPaused = false;
        SceneTime = 0f;
        nextCue = 0;
        lastThinkTime = EntitySystem.CurrentTime;

        EntitySystem.TriggerOutput(this, "OnStart", activator);
        Advance();
    }

    [EntityInput("Pause")]
    private void InputPause(EntityInputData data)
    {
        if (!IsPlaying || IsPaused)
        {
            return;
        }

        Advance();
        IsPaused = true;
        EntitySystem.TriggerOutput(this, "OnPaused", activator);
    }

    [EntityInput("Resume")]
    private void InputResume(EntityInputData data)
    {
        if (!IsPlaying || !IsPaused)
        {
            return;
        }

        IsPaused = false;
        lastThinkTime = EntitySystem.CurrentTime;
        EntitySystem.TriggerOutput(this, "OnResumed", activator);
        SetNextThink(EntitySystem.CurrentTime);
    }

    [EntityInput("Cancel")]
    private void InputCancel(EntityInputData data) => Cancel();

    // Interrupts are not played, so the next one is now
    [EntityInput("CancelAtNextInterrupt")]
    private void InputCancelAtNextInterrupt(EntityInputData data) => Cancel();

    /// <inheritdoc/>
    public override void Think()
    {
        if (IsPlaying && !IsPaused)
        {
            Advance();
        }
    }

    /// <inheritdoc/>
    protected override void OnRemove()
    {
        base.OnRemove();
        StopSounds();
    }

    private void Cancel()
    {
        if (!IsPlaying)
        {
            return;
        }

        IsPlaying = false;
        StopSounds();
        SetNextThink(-1f);
        EntitySystem.TriggerOutput(this, "OnCanceled", activator);
    }

    // Runs every cue that is due, and finishes the scene once its time is up
    private void Advance()
    {
        var now = EntitySystem.CurrentTime;
        SceneTime += now - lastThinkTime;
        lastThinkTime = now;

        while (nextCue < cues.Count && cues[nextCue].Time <= SceneTime)
        {
            Run(cues[nextCue++]);
        }

        if (SceneTime >= duration && nextCue >= cues.Count)
        {
            IsPlaying = false;
            SetNextThink(-1f);
            EntitySystem.TriggerOutput(this, "OnCompletion", activator);
            return;
        }

        SetNextThink(now);
    }

    private void Run(Cue cue)
    {
        switch (cue.Type)
        {
            case ChoreoEventType.FireTrigger:
                EntitySystem.TriggerOutput(this, $"OnTrigger{cue.Parameter}", activator);
                break;

            case ChoreoEventType.Speak:
                // The player hears its own lines in their head, anything else speaks from where its actor is
                var actor = ResolveActor(cue.Actor);
                Vector3? position = actor is PlayerEntity ? null : (actor ?? this).WorldOrigin;
                playingSounds.Add(Sound.Play(cue.Parameter, position));
                break;
        }
    }

    // Actors name an entity, the player, or one of the target keyvalues
    private BaseEntity? ResolveActor(string name)
    {
        if (name.Equals("!player", StringComparison.OrdinalIgnoreCase))
        {
            return EntitySystem.Player;
        }

        if (name.StartsWith("!target", StringComparison.OrdinalIgnoreCase))
        {
            name = KeyValues.GetStringProperty($"target{name[7..]}") ?? string.Empty;
        }

        return string.IsNullOrEmpty(name) ? null : EntitySystem.FindByTargetName(name);
    }

    private void StopSounds()
    {
        foreach (var sound in playingSounds)
        {
            sound.Stop();
        }

        playingSounds.Clear();
    }

    // Scenes are compiled into a list per folder, act1/refuge/x.vcd into scenes/act1_refuge.vcdlist
    private bool TryLoadScene()
    {
        if (cues.Count > 0)
        {
            return true;
        }

        var sceneName = (KeyValues.GetStringProperty("scenefile") ?? string.Empty).Replace('\\', '/').ToLowerInvariant();

        if (sceneName.StartsWith("scenes/", StringComparison.Ordinal))
        {
            sceneName = sceneName["scenes/".Length..];
        }

        var folder = Path.GetDirectoryName(sceneName)?.Replace('\\', '_').Replace('/', '_');
        var listName = $"scenes/{(string.IsNullOrEmpty(folder) ? "_root" : folder)}.vcdlist";

        if (EntitySystem.FileLoader.LoadFileCompiled(listName)?.DataBlock is not ChoreoSceneFileData list
            || list.Scenes.FirstOrDefault(scene => sceneName.Equals(scene.Name, StringComparison.OrdinalIgnoreCase)) is not { } scene)
        {
            EntitySystem.Logger.LogUniqueWarning("Choreographed scene {Scene} was not found in {List}", sceneName, listName);
            return false;
        }

        foreach (var actor in scene.Actors)
        {
            foreach (var channel in actor.Channels)
            {
                foreach (var choreoEvent in channel.Events)
                {
                    if (choreoEvent.Type is ChoreoEventType.FireTrigger or ChoreoEventType.Speak)
                    {
                        cues.Add(new Cue(choreoEvent.StartTime + (choreoEvent.Type == ChoreoEventType.Speak ? choreoEvent.SoundStartDelay : 0f),
                            choreoEvent.Type, choreoEvent.Param1, actor.Name));
                    }
                }
            }
        }

        cues.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        duration = scene.Duration / 1000f;

        return true;
    }
}

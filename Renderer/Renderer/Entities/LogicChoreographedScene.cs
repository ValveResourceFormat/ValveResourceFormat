using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_choreographed_scene</c>. Keeps the timing of a choreographed scene without its actors performing:
/// <c>OnStart</c> when it starts, <c>OnTrigger1</c> to <c>OnTrigger16</c> at its fire trigger events, and
/// <c>OnCompletion</c> at its first stop point or once its last event has ended.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CSceneEntity">CSceneEntity</seealso>
public sealed partial class LogicChoreographedScene : BaseEntity
{
    private const int TriggerOutputCount = 16;

    // Fire trigger events by output number, and stop points as 0
    private readonly List<(float Time, int Output)> timedEvents = [];
    private int nextTimedEvent;
    private float sceneEnd;

    // Scene time as of resumeTime, which runs on from there unless paused
    private float sceneTime;
    private float resumeTime;
    private bool isPlaying;
    private bool isPaused;
    private bool hasCompleted;

    /// <summary>Initializes a <c>logic_choreographed_scene</c> from its keyvalues.</summary>
    public LogicChoreographedScene(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>Fires the events that are due, and finishes the scene once it has run past its end.</summary>
    public override void Think()
    {
        if (!isPlaying || isPaused)
        {
            return;
        }

        var now = GetSceneTime();

        while (nextTimedEvent < timedEvents.Count && timedEvents[nextTimedEvent].Time <= now)
        {
            var output = timedEvents[nextTimedEvent].Output;
            nextTimedEvent++;

            if (output == 0)
            {
                Complete();
            }
            else
            {
                EntitySystem.TriggerOutput(this, "OnTrigger" + output.ToString(CultureInfo.InvariantCulture), this);
            }
        }

        if (now > sceneEnd)
        {
            isPlaying = false;
            Complete();
            return;
        }

        // The scene is done on the first think after its end, never on it
        var nextSceneTime = sceneEnd + EntitySystem.TickInterval;

        if (nextTimedEvent < timedEvents.Count)
        {
            nextSceneTime = MathF.Min(nextSceneTime, timedEvents[nextTimedEvent].Time);
        }

        SetNextThink(EntitySystem.CurrentTime + nextSceneTime - now);
    }

    [EntityInput("Start")]
    private void InputStart(EntityInputData data)
    {
        if (isPlaying)
        {
            return;
        }

        var sceneFile = KeyValues.GetStringProperty("scenefile");

        if (string.IsNullOrEmpty(sceneFile) || FindScene(sceneFile) is not { } scene)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' has no scene \"{Scene}\" in the scene lists", Classname, TargetName, sceneFile);
            return;
        }

        timedEvents.Clear();
        nextTimedEvent = 0;
        sceneTime = float.MaxValue;
        sceneEnd = float.MinValue;

        foreach (var choreoEvent in scene.Events)
        {
            AddEvent(choreoEvent);
        }

        foreach (var actor in scene.Actors)
        {
            foreach (var channel in actor.Channels)
            {
                foreach (var choreoEvent in channel.Events)
                {
                    AddEvent(choreoEvent);
                }
            }
        }

        // A scene without events ends on its first think
        if (sceneTime > sceneEnd)
        {
            sceneTime = 0f;
            sceneEnd = 0f;
        }

        timedEvents.Sort(static (a, b) => a.Time.CompareTo(b.Time));

        isPlaying = true;
        isPaused = false;
        hasCompleted = false;
        resumeTime = EntitySystem.CurrentTime;

        EntitySystem.TriggerOutput(this, "OnStart", this);

        Think();
    }

    [EntityInput("Cancel")]
    private void InputCancel(EntityInputData data)
    {
        if (!isPlaying)
        {
            return;
        }

        isPlaying = false;
        isPaused = false;
        SetNextThink(-1f);

        EntitySystem.TriggerOutput(this, "OnCanceled", this);
    }

    [EntityInput("Pause")]
    private void InputPause(EntityInputData data)
    {
        if (!isPlaying || isPaused)
        {
            return;
        }

        sceneTime = GetSceneTime();
        isPaused = true;
        SetNextThink(-1f);

        EntitySystem.TriggerOutput(this, "OnPaused", this);
    }

    [EntityInput("Resume")]
    private void InputResume(EntityInputData data)
    {
        if (!isPlaying || !isPaused)
        {
            return;
        }

        isPaused = false;
        resumeTime = EntitySystem.CurrentTime;

        EntitySystem.TriggerOutput(this, "OnResumed", this);

        Think();
    }

    private float GetSceneTime() => isPaused ? sceneTime : sceneTime + EntitySystem.CurrentTime - resumeTime;

    // A stop point completes the scene early. Reaching the end of the scene then does not complete it again
    private void Complete()
    {
        if (hasCompleted)
        {
            return;
        }

        hasCompleted = true;
        EntitySystem.TriggerOutput(this, "OnCompletion", this);
    }

    private void AddEvent(ChoreoEvent choreoEvent)
    {
        // Scene time starts at the first event; one without an end time ends where it starts
        sceneTime = MathF.Min(sceneTime, choreoEvent.StartTime);
        sceneEnd = MathF.Max(sceneEnd, MathF.Max(choreoEvent.StartTime, choreoEvent.EndTime));

        if (choreoEvent.Type == ChoreoEventType.StopPoint)
        {
            timedEvents.Add((choreoEvent.StartTime, 0));
        }
        else if (choreoEvent.Type == ChoreoEventType.FireTrigger
            && ValueParsing.TryParseInt(choreoEvent.Param1, out var output)
            && output is >= 1 and <= TriggerOutputCount)
        {
            timedEvents.Add((choreoEvent.StartTime, output));
        }
    }

    // Scenes are compiled into the lists of scenes/scenes.vrman, where a path matches ignoring case, slash direction and extension
    private ChoreoScene? FindScene(string sceneFile)
    {
        if (EntitySystem.FileLoader.LoadFileCompiled("scenes/scenes.vrman")?.DataBlock is not ResourceManifest manifest)
        {
            return null;
        }

        var sceneKey = GetSceneKey(sceneFile);

        foreach (var resources in manifest.Resources)
        {
            foreach (var listName in resources)
            {
                if (EntitySystem.FileLoader.LoadFileCompiled(listName)?.DataBlock is not ChoreoSceneFileData sceneList)
                {
                    continue;
                }

                foreach (var scene in sceneList.Scenes)
                {
                    if (GetSceneKey("scenes/" + scene.Name) == sceneKey)
                    {
                        return scene;
                    }
                }
            }
        }

        return null;
    }

    private static string GetSceneKey(string path)
        => Path.ChangeExtension(path.Replace('\\', '/'), null).ToLowerInvariant();
}

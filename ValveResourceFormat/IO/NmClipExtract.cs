using System.IO;
using System.Linq;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO;

/// <summary>
/// Extracts Source 2 animation clips to editable format.
/// </summary>
public class NmClipExtract
{
    private readonly Resource resource;
    private readonly AnimationClip clip;
    private readonly IFileLoader fileLoader;
    /// <summary>
    /// Initializes a new instance of the <see cref="NmClipExtract"/> class.
    /// </summary>
    public NmClipExtract(Resource resource, IFileLoader fileLoader)
    {
        this.resource = resource;
        clip = resource.DataBlock as AnimationClip
            ?? throw new InvalidDataException($"Resource DataBlock is not an {nameof(AnimationClip)}.");
        this.fileLoader = fileLoader;
    }

    /// <summary>
    /// Converts the animation clip to a content file.
    /// </summary>
    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile();

        var kv = new KVObject(null);
        var sourceFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        kv.Add("m_sourceFilename", sourceFileName);
        kv.Add("m_animationSkeletonName", clip.SkeletonName);
        // TODO: figure out additive type.
        var isAdditive = clip.Data.GetProperty<bool>("m_bIsAdditive");
        if (isAdditive)
        {
            kv.Add("m_additiveType", "RelativeToFrame");
            kv.Add("m_additiveBaseFilename", "");
            kv.Add("m_additiveBaseFrame", "FirstFrame");
            kv.Add("m_nAdditiveBaseFrameIdx", 0L);
        }

        var animation = new ResourceTypes.ModelAnimation.Animation(clip);
        var skeletonResource = fileLoader.LoadFileCompiled(clip.SkeletonName);
        if (skeletonResource != null)
        {
            var skeleton = ResourceTypes.ModelAnimation.Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);
            var modelSpaceSamplingChain = clip.Data.GetArray("m_modelSpaceSamplingChain");
            // The array below indexes into the bone sampling chain, which in turn indexes into the skeleton bones.
            var modelSpaceBoneSamplingIndices = clip.Data.GetIntegerArray("m_modelSpaceBoneSamplingIndices");

            var bonesToSampleInModelSpace = KVObject.Array("m_bonesToSampleInModelSpace");
            foreach (var chainIdx in modelSpaceBoneSamplingIndices)
            {
                if (chainIdx < 0 || chainIdx >= modelSpaceSamplingChain!.Length)
                {
                    throw new InvalidDataException($"Model space sampling chain index {chainIdx} is out of bounds (0..{modelSpaceSamplingChain!.Length - 1}).");
                }
                var boneIdx = modelSpaceSamplingChain[chainIdx]!.GetInt32Property("m_nBoneIdx");
                bonesToSampleInModelSpace.Add((KVValue)skeleton.Bones[boneIdx].Name);
            }
            kv.Add("m_bonesToSampleInModelSpace", bonesToSampleInModelSpace.Value);

            contentFile.AddSubFile(Path.GetFileName(sourceFileName) ?? "animation.dmx", () =>
            {
                return ModelExtract.ToDmxAnim(skeleton, [], animation);
            });
        }
        var events = clip.Data.GetArray("m_events")!;
        var docEventTracks = KVObject.Array("m_eventTracks");
        foreach (var ev in events!)
        {
            var docEventTrack = BuildDocEventBasedOnEventClass(ev, ev.GetStringProperty("_class"));
            var startTimeObj = ev.GetSubCollection("m_flStartTime");
            var startTimeSeconds = startTimeObj?.GetFloatProperty("m_flValue") ?? 0f;
            var durationObj = ev.GetSubCollection("m_flDuration");
            var durationSeconds = durationObj?.GetFloatProperty("m_flValue") ?? 0f;
            var eventList = docEventTrack!.GetArray("m_events")!.First();
            // Doc file event time stamps are given in frames they can be technically floats, but based on recompilation tests
            // these seem inconsistent, unless they're floored to int, then it matches up.
            eventList.Add("m_flStartTime", Math.Floor(startTimeSeconds * animation.FrameCount));
            eventList.Add("m_flDuration", Math.Floor(durationSeconds * animation.FrameCount));
            docEventTracks.Add(docEventTrack.Value);
        }
        kv.Add("m_eventTracks", docEventTracks.Value);
        contentFile.Data = Encoding.UTF8.GetBytes(new KV3File(kv).ToString());
        return contentFile;
    }

    // Returns a full event track.
    private static KVObject BuildDocEventBasedOnEventClass(KVObject kvCompiledEvent, string className)
    {
        // From testing one event track in doc seems to correspond to one event in compiled asset
        // even though m_events is an array inside each track.
        var kvDocEventTrack = new KVObject(null);
        var kvDocEvent = new KVObject("m_event");

        kvDocEventTrack.Add("m_type", "Duration"); // Doesn't seem to matter?
        kvDocEventTrack.Add("m_bIsSyncTrack", kvCompiledEvent.ContainsKey("m_syncID"));

        // Example: CNmIDEvent maps to CNmClipDocEvent_ID.
        var eventName = className["CNm".Length..^"Event".Length];

        const string EntityAttribute = "EntityAttribute";
        if (eventName is "EntityAttributeInt" or "EntityAttributeFloat")
        {
            var attributeType = eventName[EntityAttribute.Length..];
            eventName = EntityAttribute;
            kvDocEvent.Add("m_nValueType", $"EVENT_ENTITY_ATTR_TYPE_{attributeType.ToUpperInvariant()}");
        }

        var docEventClass = $"CNmClipDocEvent_{eventName}";

        kvDocEventTrack.Add("m_eventClassName", docEventClass);
        kvDocEvent.Add("_class", docEventClass);

        foreach (var child in kvCompiledEvent.Children)
        {
            var key = child.Name;
            var value = child.Value;

            // These were already handled and shouldn't be copied over.
            if (key is "_class" or "m_flStartTimeSeconds" or "m_flDurationSeconds")
            {
                continue;
            }

            var newKey = (eventName, key) switch
            {
                ("Particle", "m_hParticleSystem") => "m_particleSystem",
                ("Legacy", "m_animEventClassName") => "m_eventClass",
                ("Transition", "m_ID") => "m_optionalID",
                _ => key,
            };

            kvDocEvent.Add(newKey, value);
        }

        var eventsArray = KVObject.Array("m_events");
        eventsArray.Add(kvDocEvent.Value);
        kvDocEventTrack.Add("m_events", eventsArray.Value);
        return kvDocEventTrack;
    }
}

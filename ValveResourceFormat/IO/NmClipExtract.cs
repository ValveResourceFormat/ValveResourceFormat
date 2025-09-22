using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.IO;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.ModelAnimation2;

public class NmClipExtract
{
    private readonly Resource resource;
    private readonly AnimationClip clip;
    private readonly IFileLoader fileLoader;
    public NmClipExtract(Resource resource, IFileLoader fileLoader)
    {
        this.resource = resource;
        clip = resource.DataBlock as AnimationClip
            ?? throw new InvalidDataException($"Resource DataBlock is not an {nameof(AnimationClip)}.");
        this.fileLoader = fileLoader;
    }

    public ContentFile ToContentFile()
    {
        var contentFile = new ContentFile();

        var kv = new KVObject(null);
        var sourceFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        kv.AddProperty("m_sourceFilename", sourceFileName);
        kv.AddProperty("m_animationSkeletonName", clip.SkeletonName);
        // TODO: figure out additive type.

        var animation = new ModelAnimation.Animation(clip);
        var skeletonResource = fileLoader.LoadFileCompiled(clip.SkeletonName);
        if (skeletonResource != null)
        {
            var skeleton = ModelAnimation.Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);
            var modelSpaceSamplingChain = clip.Data.GetArray<KVObject>("m_modelSpaceSamplingChain");
            // The array below indexes into the bone sampling chain, which in turn indexes into the skeleton bones.
            var modelSpaceBoneSamplingIndices = clip.Data.GetIntegerArray("m_modelSpaceBoneSamplingIndices");

            var bonesToSampleInModelSpace = new KVObject("m_bonesToSampleInModelSpace", true, modelSpaceBoneSamplingIndices.Length);
            foreach (var chainIdx in modelSpaceBoneSamplingIndices)
            {
                if (chainIdx < 0 || chainIdx >= modelSpaceSamplingChain.Length)
                {
                    throw new InvalidDataException($"Model space sampling chain index {chainIdx} is out of bounds (0..{modelSpaceSamplingChain.Length - 1}).");
                }
                var boneIdx = modelSpaceSamplingChain[chainIdx].GetInt32Property("m_nBoneIdx");
                bonesToSampleInModelSpace.AddItem(skeleton.Bones[boneIdx].Name);
            }
            kv.AddProperty("m_bonesToSampleInModelSpace", bonesToSampleInModelSpace);

            contentFile.AddSubFile(sourceFileName, () =>
            {
                return ModelExtract.ToDmxAnim(skeleton, [], animation);
            });
        }
        var events = clip.Data.GetArray<KVObject>("m_events");
        var docEventTracks = new KVObject("m_eventTracks", true, events.Length);
        foreach (var ev in events)
        {
            var docEventTrack = BuildDocEventBasedOnEventClass(ev, ev.GetStringProperty("_class"));
            var startTimeSeconds = ev.GetFloatProperty("m_flStartTimeSeconds");
            var durationSeconds = ev.GetFloatProperty("m_flDurationSeconds");
            var eventList = docEventTrack.GetArray<KVObject>("m_events").First();
            // Doc file event time stamps are given in frames they can be technically floats, but based on recompilation tests
            // these seem inconsistent, unless they're floored to int, then it matches up.
            eventList.AddProperty("m_flStartTime", Math.Floor(startTimeSeconds * animation.Fps));
            eventList.AddProperty("m_flDuration", Math.Floor(durationSeconds * animation.Fps));
            docEventTracks.AddItem(docEventTrack);
        }
        kv.AddProperty("m_eventTracks", docEventTracks);
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

        kvDocEventTrack.AddProperty("m_type", "Duration"); // Doesn't seem to matter?
        kvDocEventTrack.AddProperty("m_bIsSyncTrack", kvCompiledEvent.ContainsKey("m_syncID"));

        // Example: CNmIDEvent maps to CNmClipDocEvent_ID.
        var eventName = className["CNm".Length..^"Event".Length];

        const string EntityAttribute = "EntityAttribute";
        if (eventName is "EntityAttributeInt" or "EntityAttributeFloat")
        {
            eventName = EntityAttribute;
            var attributeType = eventName[EntityAttribute.Length..];
            kvDocEvent.AddProperty("m_nValueType", $"EVENT_ENTITY_ATTR_TYPE_{attributeType.ToUpperInvariant()}");
        }

        var docEventClass = $"CNmClipDocEvent_{eventName}";

        kvDocEventTrack.AddProperty("m_eventClassName", docEventClass);
        kvDocEvent.AddProperty("_class", docEventClass);

        foreach (var (key, value) in kvCompiledEvent)
        {
            // These were already handled and shouldn't be copied over.
            if (key is "m_flStartTimeSeconds" or "m_flDurationSeconds")
            {
                continue;
            }

            var (newKey, newValue) = (eventName, key) switch
            {
                ("Particle", "m_hParticleSystem") => ("m_particleSystem", value),
                ("Legacy", "m_animEventClassName") => ("m_eventClass", value),
                ("Transition", "m_ID") => ("m_optionalID", value),
                _ => (key, value),
            };

            kvDocEvent.AddProperty(newKey, newValue);
        }

        var eventsArray = new KVObject("m_events", true, 1);
        eventsArray.AddItem(kvDocEvent);
        kvDocEventTrack.AddProperty("m_events", eventsArray);
        return kvDocEventTrack;
    }
}

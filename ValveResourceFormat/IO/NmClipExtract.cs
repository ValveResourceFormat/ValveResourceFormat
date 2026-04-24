using System.Diagnostics;
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

        var kv = KVObject.Collection();
        var sourceFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        Debug.Assert(sourceFileName != null);
        kv.Add("m_sourceFilename", sourceFileName);
        kv.Add("m_animationSkeletonName", clip.SkeletonName);
        // TODO: figure out additive type.
        var isAdditive = clip.Data.Root.GetBooleanProperty("m_bIsAdditive");
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
            var modelSpaceSamplingChain = clip.Data.Root.GetArray("m_modelSpaceSamplingChain");
            // The array below indexes into the bone sampling chain, which in turn indexes into the skeleton bones.
            var modelSpaceBoneSamplingIndices = clip.Data.Root.GetIntegerArray("m_modelSpaceBoneSamplingIndices");

            var bonesToSampleInModelSpace = KVObject.Array();
            foreach (var chainIdx in modelSpaceBoneSamplingIndices)
            {
                if (chainIdx < 0 || chainIdx >= modelSpaceSamplingChain!.Count)
                {
                    throw new InvalidDataException($"Model space sampling chain index {chainIdx} is out of bounds (0..{modelSpaceSamplingChain!.Count - 1}).");
                }
                var boneIdx = modelSpaceSamplingChain[(int)chainIdx]!.GetInt32Property("m_nBoneIdx");
                bonesToSampleInModelSpace.Add(skeleton.Bones[boneIdx].Name);
            }
            kv.Add("m_bonesToSampleInModelSpace", bonesToSampleInModelSpace);

            contentFile.AddSubFile(Path.GetFileName(sourceFileName), () =>
            {
                return ModelExtract.ToDmxAnim(skeleton, [], animation, nmSkelAxisFixup: true);
            });
        }
        var events = clip.Data.Root.GetArray("m_events")!;
        var docEventTracks = KVObject.Array();
        foreach (var ev in events!)
        {
            var docEventTrack = BuildDocEventBasedOnEventClass(ev, ev.GetStringProperty("_class"));
            var startTimeObj = ev.GetSubCollection("m_flStartTime");
            var startTimeSeconds = startTimeObj?.GetFloatProperty("m_flValue") ?? 0f;
            var durationObj = ev.GetSubCollection("m_flDuration");
            var durationSeconds = durationObj?.GetFloatProperty("m_flValue") ?? 0f;
            var eventList = docEventTrack!.GetArray("m_events")![0];
            // Doc file event time stamps are given in frames they can be technically floats, but based on recompilation tests
            // these seem inconsistent, unless they're floored to int, then it matches up.
            eventList["m_flStartTime"] = Math.Floor(startTimeSeconds * animation.FrameCount);
            eventList["m_flDuration"] = Math.Floor(durationSeconds * animation.FrameCount);
            docEventTracks.Add(docEventTrack);
        }
        kv.Add("m_eventTracks", docEventTracks);
        contentFile.Data = Encoding.UTF8.GetBytes(kv.ToKV3String());
        return contentFile;
    }

    // Returns a full event track.
    private static KVObject BuildDocEventBasedOnEventClass(KVObject kvCompiledEvent, string className)
    {
        // From testing one event track in doc seems to correspond to one event in compiled asset
        // even though m_events is an array inside each track.
        var kvDocEventTrack = KVObject.Collection();
        var kvDocEvent = KVObject.Collection();

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

        foreach (var (key, value) in kvCompiledEvent.Children)
        {
            if (key is "_class")
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

        var eventsArray = KVObject.Array();
        eventsArray.Add(kvDocEvent);
        kvDocEventTrack.Add("m_events", eventsArray);
        return kvDocEventTrack;
    }
}

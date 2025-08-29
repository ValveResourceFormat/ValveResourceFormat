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
            ?? throw new InvalidDataException("Resource DataBlock is not an AnimationClip.");
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
            // Doc file event time stamps are given in frames they can be technically floats, but based on recompilation tests
            // these seem inconsistent, unless they're floored to int, then it matches up.
            docEventTrack.GetArray<KVObject>("m_events").First().AddProperty("m_flStartTime", Math.Floor(startTimeSeconds * animation.Fps));
            docEventTrack.GetArray<KVObject>("m_events").First().AddProperty("m_flDuration", Math.Floor(durationSeconds * animation.Fps));
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
        kvDocEventTrack.AddProperty("m_type", "Duration");
        kvDocEventTrack.AddProperty("m_bIsSyncTrack", kvCompiledEvent.ContainsKey("m_syncID"));
        switch (className)
        {
            case "CNmIDEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_ID");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_ID");
                    kvDocEvent.AddProperty("m_ID", kvCompiledEvent.GetStringProperty("m_ID"));
                    kvDocEvent.AddProperty("m_secondaryID", kvCompiledEvent.GetStringProperty("m_secondaryID"));
                    break;
                }
            case "CNmEntityAttributeIntEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_EntityAttribute");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_EntityAttribute");
                    kvDocEvent.AddProperty("m_attributeName", kvCompiledEvent.GetStringProperty("m_attributeName"));
                    kvDocEvent.AddProperty("m_nValueType", "EVENT_ENTITY_ATTR_TYPE_INT");
                    kvDocEvent.AddProperty("m_nIntValue", kvCompiledEvent.GetInt32Property("m_nIntValue"));
                    break;
                }
            case "CNmEntityAttributeFloatEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_EntityAttribute");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_EntityAttribute");
                    kvDocEvent.AddProperty("m_attributeName", kvCompiledEvent.GetStringProperty("m_attributeName"));
                    kvDocEvent.AddProperty("m_nValueType", "EVENT_ENTITY_ATTR_TYPE_FLOAT");
                    kvDocEvent.AddProperty("m_FloatValue", kvCompiledEvent.GetProperty<object>("m_FloatValue"));
                    break;
                }

            case "CNmFloatCurveEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_FloatCurve");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_FloatCurve");
                    kvDocEvent.AddProperty("m_curve", kvCompiledEvent.GetProperty<object>("m_curve"));
                    kvDocEvent.AddProperty("m_ID", kvCompiledEvent.GetStringProperty("m_ID"));
                    break;
                }
            case "CNmFootEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_Foot");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_Foot");
                    kvDocEvent.AddProperty("m_phase", kvCompiledEvent.GetProperty<object>("m_phase"));
                    break;
                }
            case "CNmFrameSnapEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_FrameSnap");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_FrameSnap");
                    kvDocEvent.AddProperty("m_frameSnapMode", kvCompiledEvent.GetProperty<object>("m_frameSnapMode"));
                    break;
                }
            case "CNmLegacyEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_Legacy");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_Legacy");
                    kvDocEvent.AddProperty("m_eventClass", kvCompiledEvent.GetStringProperty("m_animEventClassName"));
                    kvDocEvent.AddProperty("m_KV", kvCompiledEvent.GetProperty<KVObject>("m_KV"));
                    break;
                }
            case "CNmMaterialAttributeEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_MaterialAttribute");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_MaterialAttribute");
                    kvDocEvent.AddProperty("m_attributeName", kvCompiledEvent.GetStringProperty("m_attributeName"));
                    kvDocEvent.AddProperty("m_x", kvCompiledEvent.GetProperty<object>("m_x"));
                    kvDocEvent.AddProperty("m_y", kvCompiledEvent.GetProperty<object>("m_y"));
                    kvDocEvent.AddProperty("m_z", kvCompiledEvent.GetProperty<object>("m_z"));
                    kvDocEvent.AddProperty("m_w", kvCompiledEvent.GetProperty<object>("m_w"));
                    break;
                }
            case "CNmOrientationWarpEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_OrientationWarp");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_OrientationWarp");
                    break;
                }
            case "CNmParticleEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_Particle");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_Particle");
                    kvDocEvent.AddProperty("m_relevance", kvCompiledEvent.GetStringProperty("m_relevance"));
                    kvDocEvent.AddProperty("m_type", kvCompiledEvent.GetStringProperty("m_type"));
                    kvDocEvent.AddProperty("m_particleSystem", kvCompiledEvent.GetStringProperty("m_hParticleSystem"));
                    kvDocEvent.AddProperty("m_bDetachFromOwner", kvCompiledEvent.GetProperty<bool>("m_bDetachFromOwner"));
                    kvDocEvent.AddProperty("m_bStopImmediately", kvCompiledEvent.GetProperty<bool>("m_bStopImmediately"));
                    kvDocEvent.AddProperty("m_bPlayEndCap", kvCompiledEvent.GetProperty<bool>("m_bPlayEndCap"));
                    kvDocEvent.AddProperty("m_attachmentPoint0", kvCompiledEvent.GetStringProperty("m_attachmentPoint0"));
                    kvDocEvent.AddProperty("m_attachmentType0", kvCompiledEvent.GetStringProperty("m_attachmentType0"));
                    kvDocEvent.AddProperty("m_attachmentPoint1", kvCompiledEvent.GetStringProperty("m_attachmentPoint1"));
                    kvDocEvent.AddProperty("m_attachmentType1", kvCompiledEvent.GetStringProperty("m_attachmentType1"));
                    kvDocEvent.AddProperty("m_config", kvCompiledEvent.GetStringProperty("m_config"));
                    kvDocEvent.AddProperty("m_effectForConfig", kvCompiledEvent.GetStringProperty("m_effectForConfig"));
                    kvDocEvent.AddProperty("m_tags", kvCompiledEvent.GetStringProperty("m_tags"));
                    break;
                }
            case "CNmRootMotionEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_RootMotion");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_RootMotion");
                    kvDocEvent.AddProperty("m_flBlendTimeSeconds", kvCompiledEvent.GetFloatProperty("m_flBlendTimeSeconds"));
                    break;
                }
            case "CNmSoundEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_Sound");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_Sound");
                    kvDocEvent.AddProperty("m_relevance", kvCompiledEvent.GetStringProperty("m_relevance"));
                    kvDocEvent.AddProperty("m_type", kvCompiledEvent.GetStringProperty("m_type"));
                    kvDocEvent.AddProperty("m_bContinuePlayingSoundAtDurationEnd", kvCompiledEvent.GetProperty<bool>("m_bContinuePlayingSoundAtDurationEnd"));
                    kvDocEvent.AddProperty("m_flDurationInterruptionThreshold", kvCompiledEvent.GetFloatProperty("m_flDurationInterruptionThreshold"));
                    kvDocEvent.AddProperty("m_name", kvCompiledEvent.GetStringProperty("m_name"));
                    kvDocEvent.AddProperty("m_position", kvCompiledEvent.GetStringProperty("m_position"));
                    kvDocEvent.AddProperty("m_attachmentName", kvCompiledEvent.GetStringProperty("m_attachmentName"));
                    kvDocEvent.AddProperty("m_tags", kvCompiledEvent.GetStringProperty("m_tags"));
                    break;
                }
            case "CNmTargetWarpEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_TargetWarp");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_TargetWarp");
                    kvDocEvent.AddProperty("m_rule", kvCompiledEvent.GetStringProperty("m_rule"));
                    kvDocEvent.AddProperty("m_algorithm", kvCompiledEvent.GetStringProperty("m_algorithm"));
                    break;
                }
            case "CNmTransitionEvent":
                {
                    kvDocEventTrack.AddProperty("m_eventClassName", "CNmClipDocEvent_Transition");
                    kvDocEvent.AddProperty("_class", "CNmClipDocEvent_Transition");
                    kvDocEvent.AddProperty("m_rule", kvCompiledEvent.GetStringProperty("m_rule"));
                    kvDocEvent.AddProperty("m_optionalID", kvCompiledEvent.GetStringProperty("m_ID"));
                    break;
                }
        }
        var arr = new KVObject("m_events", true, 1);
        arr.AddItem(kvDocEvent);
        kvDocEventTrack.AddProperty("m_events", arr);
        return kvDocEventTrack;
    }
}

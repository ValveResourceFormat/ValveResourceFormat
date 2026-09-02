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

        var skeletonSourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skeleton = LoadSkeleton(clip.SkeletonName, skeletonSourceFiles);
        var skeletonSourcesKnown = skeleton != null;

        // Secondary animations (e.g. the weapon of a viewmodel clip) share the DMX; the compiler
        // pulls each declared skeleton's tracks out of it by bone name.
        var secondaryAnimations = new List<(ResourceTypes.ModelAnimation.Skeleton Skeleton, ResourceTypes.ModelAnimation.Animation Animation)>();
        foreach (var secAnim in clip.SecondaryAnimations)
        {
            if (LoadSkeleton(secAnim.SkeletonName, skeletonSourceFiles) is { } secSkeleton)
            {
                secondaryAnimations.Add((secSkeleton, new ResourceTypes.ModelAnimation.ClipAnimation(secAnim)));
            }
            else
            {
                skeletonSourcesKnown = false;
            }
        }

        var dmxFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        Debug.Assert(dmxFileName != null);

        var (sourceFileName, additiveBaseFileName) = FindAuthoredSourceFiles(skeletonSourceFiles, skeletonSourcesKnown);
        kv.Add("m_sourceFilename", sourceFileName ?? NormalizeContentPath(dmxFileName));
        kv.Add("m_animationSkeletonName", clip.SkeletonName);

        if (clip.IsAdditive)
        {
            AddAdditiveProperties(kv, additiveBaseFileName);
        }

        var animation = new ResourceTypes.ModelAnimation.ClipAnimation(clip);
        if (skeleton != null)
        {
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

            contentFile.AddSubFile(
                Path.GetFileName(dmxFileName),
                () => ModelExtract.ToDmxAnim(skeleton, [], animation, secondaryAnimations, nmSkelAxisFixup: true)
            );
        }

        if (clip.SecondaryAnimations.Length > 0)
        {
            var secondarySkeletonNames = KVObject.Array();
            foreach (var secAnim in clip.SecondaryAnimations)
            {
                secondarySkeletonNames.Add(secAnim.SkeletonName);
            }

            kv.Add("m_secondaryAnimationSkeletonNames", secondarySkeletonNames);
        }

        var syncEventIds = new HashSet<string>();
        var syncTrack = clip.Data.Root.GetSubCollection("m_syncTrack");
        if (syncTrack != null)
        {
            var syncEvents = syncTrack.GetArray("m_syncEvents");
            if (syncEvents != null)
            {
                foreach (var syncEv in syncEvents)
                {
                    var syncId = syncEv.GetStringProperty("m_ID", string.Empty);
                    if (!string.IsNullOrEmpty(syncId))
                    {
                        syncEventIds.Add(syncId);
                    }
                }
            }
        }

        var frameIntervalCount = Math.Max(0, animation.FrameCount - 1);
        var events = clip.Data.Root.GetArray("m_events")!;
        var docEventTracks = KVObject.Array();
        foreach (var ev in events!)
        {
            var eventSyncId = ev.GetStringProperty("m_syncID", ev.GetStringProperty("m_ID", string.Empty));
            var isSyncTrack = syncEventIds.Contains(eventSyncId);

            var docEventTrack = BuildDocEventBasedOnEventClass(ev, ev.GetStringProperty("_class"), isSyncTrack);
            var startTimeObj = ev.GetSubCollection("m_flStartTime");
            var startTimeFraction = startTimeObj?.GetFloatProperty("m_flValue") ?? 0f;
            var durationObj = ev.GetSubCollection("m_flDuration");
            var durationFraction = durationObj?.GetFloatProperty("m_flValue") ?? 0f;
            var eventList = docEventTrack!.GetArray("m_events")![0];
            // Compiled event times are fractions of the clip, denominated in its frame intervals
            // (FrameCount - 1); doc files give them in frames. The product is exact on shipped data.
            eventList["m_flStartTime"] = Math.Round(startTimeFraction * frameIntervalCount, MidpointRounding.AwayFromZero);
            eventList["m_flDuration"] = Math.Round(durationFraction * frameIntervalCount, MidpointRounding.AwayFromZero);
            docEventTracks.Add(docEventTrack);
        }

        foreach (var curve in clip.FloatCurves)
        {
            docEventTracks.Add(BuildFloatCurveDocEventTrack(curve, frameIntervalCount));
        }

        kv.Add("m_eventTracks", docEventTracks);
        contentFile.Data = Encoding.UTF8.GetBytes(kv.ToKV3String());
        return contentFile;
    }

    /// <summary>
    /// Loads a skeleton, collecting the content files it was compiled from so that they can be told
    /// apart from the clip's own source animation in the input dependency list.
    /// </summary>
    private ResourceTypes.ModelAnimation.Skeleton? LoadSkeleton(string skeletonName, HashSet<string> skeletonSourceFiles)
    {
        using var skeletonResource = fileLoader.LoadFileCompiled(skeletonName);

        if (skeletonResource?.DataBlock is not BinaryKV3 skeletonData)
        {
            return null;
        }

        if (skeletonResource.EditInfo != null)
        {
            foreach (var dependency in skeletonResource.EditInfo.InputDependencies)
            {
                skeletonSourceFiles.Add(NormalizeContentPath(dependency.ContentRelativeFilename));
            }
        }

        return ResourceTypes.ModelAnimation.Skeleton.FromSkeletonData(skeletonData.Data);
    }

    /// <summary>
    /// Recovers the animation the clip was authored from, and for an additive clip generated against
    /// another animation, that animation. Neither is stored in the compiled clip, but the compiler
    /// records both as input dependencies, alongside the clip document itself and the source files of
    /// every skeleton the clip references.
    /// </summary>
    /// <param name="skeletonSourceFiles">The content files every referenced skeleton was compiled from.</param>
    /// <param name="skeletonSourcesKnown">
    /// Whether every skeleton the clip references was loaded. When one was not, its own source files are
    /// missing from <paramref name="skeletonSourceFiles"/> and are indistinguishable from an additive base.
    /// </param>
    private (string? SourceFileName, string? AdditiveBaseFileName) FindAuthoredSourceFiles(HashSet<string> skeletonSourceFiles, bool skeletonSourcesKnown)
    {
        if (resource.EditInfo == null)
        {
            return (null, null);
        }

        var candidates = new List<string>();

        foreach (var dependency in resource.EditInfo.InputDependencies)
        {
            var file = NormalizeContentPath(dependency.ContentRelativeFilename);

            if (file.EndsWith(".vnmclip", StringComparison.OrdinalIgnoreCase) || skeletonSourceFiles.Contains(file))
            {
                continue;
            }

            candidates.Add(file);
        }

        if (candidates.Count == 1 && skeletonSourcesKnown)
        {
            return (candidates[0], null);
        }

        // An additive generated against another animation lists that animation too; the clip's own
        // source is the one named after it.
        var clipName = Path.GetFileNameWithoutExtension(clip.Name);
        string? sourceFileName = null;

        foreach (var candidate in candidates)
        {
            if (!Path.GetFileNameWithoutExtension(candidate).Equals(clipName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sourceFileName != null)
            {
                return (null, null);
            }

            sourceFileName = candidate;
        }

        if (sourceFileName == null || candidates.Count != 2 || !skeletonSourcesKnown)
        {
            return (sourceFileName, null);
        }

        var additiveBaseFileName = candidates[0] == sourceFileName ? candidates[1] : candidates[0];
        return (sourceFileName, additiveBaseFileName);
    }

    private void AddAdditiveProperties(KVObject kv, string? additiveBaseFileName)
    {
        if (additiveBaseFileName != null)
        {
            kv.Add("m_additiveType", "RelativeToAnimation");
            kv.Add("m_additiveBaseFilename", additiveBaseFileName);
            kv.Add("m_additiveBaseFrame", "FirstFrame");
            // A negative index takes every frame relative to the matching frame of the base animation.
            kv.Add("m_nAdditiveBaseFrameIdx", -1L);
            return;
        }

        kv.Add("m_additiveType", "RelativeToFrame");
        kv.Add("m_additiveBaseFilename", "");
        kv.Add("m_additiveBaseFrame", "UserSpecifiedFrame");
        kv.Add("m_nAdditiveBaseFrameIdx", (long)FindAdditiveBaseFrame());
    }

    /// <summary>
    /// Finds the frame the additive was generated relative to: subtracting it left that frame as the
    /// identity transform on every bone. Falls back to the first frame for a clip that holds no such
    /// frame, which is the case when the additive was generated against a separate animation, or
    /// against a frame that the clip's own frame range does not cover.
    /// </summary>
    private int FindAdditiveBaseFrame()
    {
        // Between the largest deviation measured on a base frame (9e-3, quantization) and the
        // smallest measured on a frame that is not one (4e-2).
        const float IdentityTolerance = 0.02f;

        var bones = new ResourceTypes.ModelAnimation.FrameBone[clip.TrackCompressionSettings.Length];
        var baseFrame = 0;
        var smallestDeviation = float.MaxValue;

        for (var frameIndex = 0; frameIndex < clip.NumFrames; frameIndex++)
        {
            clip.ReadFrame(frameIndex, bones);

            var deviation = 0f;

            foreach (var bone in bones)
            {
                var rotation = bone.Angle;
                var rotationDeviation = MathF.Max(
                    MathF.Abs(MathF.Abs(rotation.W) - 1f),
                    new Vector3(rotation.X, rotation.Y, rotation.Z).Length()
                );

                deviation = MathF.Max(deviation, MathF.Max(bone.Position.Length(), rotationDeviation));
            }

            if (deviation < smallestDeviation)
            {
                smallestDeviation = deviation;
                baseFrame = frameIndex;
            }
        }

        return smallestDeviation <= IdentityTolerance ? baseFrame : 0;
    }

    private static string NormalizeContentPath(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Rebuilds the doc event track a compiled float curve came from. The compiler bakes a
    /// <c>CNmClipDocEvent_FloatCurve</c> (a name plus a piecewise curve) into per-frame samples and
    /// stores no event for it, so the curve is reconstructed as a linear knot per frame.
    /// </summary>
    private static KVObject BuildFloatCurveDocEventTrack(AnimationFloatCurve curve, int frameIntervalCount)
    {
        var kvDocEventTrack = KVObject.Collection();
        kvDocEventTrack.Add("m_type", "Duration");
        kvDocEventTrack.Add("m_bIsSyncTrack", false);
        kvDocEventTrack.Add("m_eventClassName", "CNmClipDocEvent_FloatCurve");

        var spline = KVObject.Array();
        var tangents = KVObject.Array();

        // Curve x is the normalized position in the clip, matching the domain convention of
        // curves Valve ships (the compiler copies the curve verbatim into the compiled event).
        for (var f = 0; f < curve.Values.Length; f++)
        {
            var knot = KVObject.Collection();
            knot.Add("x", frameIntervalCount > 0 ? (double)f / frameIntervalCount : 0d);
            knot.Add("y", (double)curve.Values[f]);
            knot.Add("m_flSlopeIncoming", 0d);
            knot.Add("m_flSlopeOutgoing", 0d);
            spline.Add(knot);

            var tangent = KVObject.Collection();
            tangent.Add("m_nIncomingTangent", "CURVE_TANGENT_LINEAR");
            tangent.Add("m_nOutgoingTangent", "CURVE_TANGENT_LINEAR");
            tangents.Add(tangent);
        }

        var kvCurve = KVObject.Collection();
        kvCurve.Add("m_spline", spline);
        kvCurve.Add("m_tangents", tangents);
        var domainMins = KVObject.Array();
        domainMins.Add(0d);
        domainMins.Add(0d);
        kvCurve.Add("m_vDomainMins", domainMins);
        var domainMaxs = KVObject.Array();
        domainMaxs.Add(1d);
        domainMaxs.Add(1d);
        kvCurve.Add("m_vDomainMaxs", domainMaxs);

        var kvDocEvent = KVObject.Collection();
        kvDocEvent.Add("_class", "CNmClipDocEvent_FloatCurve");
        kvDocEvent.Add("m_ID", curve.Name);
        kvDocEvent.Add("m_flStartTime", 0d);
        kvDocEvent.Add("m_flDuration", (double)frameIntervalCount);
        kvDocEvent.Add("m_curve", kvCurve);

        var eventsArray = KVObject.Array();
        eventsArray.Add(kvDocEvent);
        kvDocEventTrack.Add("m_events", eventsArray);
        return kvDocEventTrack;
    }

    // Returns a full event track.
    private static KVObject BuildDocEventBasedOnEventClass(KVObject kvCompiledEvent, string className, bool isSyncTrack)
    {
        // From testing one event track in doc seems to correspond to one event in compiled asset
        // even though m_events is an array inside each track.
        var kvDocEventTrack = KVObject.Collection();
        var kvDocEvent = KVObject.Collection();

        kvDocEventTrack.Add("m_type", "Duration"); // Doesn't seem to matter?
        kvDocEventTrack.Add("m_bIsSyncTrack", isSyncTrack);

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
            // Doc events carry no sync id; the compiler derives the compiled one from the event itself.
            if (key is "_class" or "m_syncID")
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

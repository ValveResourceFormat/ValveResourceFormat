using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private ClipSources? sources;

    /// <summary>
    /// The first frame of the shared source animation this clip was cut from, when another clip
    /// writes that animation.
    /// </summary>
    private int? sharedSourceStartFrame;

    /// <summary>
    /// Whether this clip writes its frames to a file of its own, because another clip compiled from
    /// the same source animation writes that file and this clip's frames are not part of it.
    /// </summary>
    private bool separateFromSharedSource;

    private sealed record ClipSources(
        ResourceTypes.ModelAnimation.Skeleton? Skeleton,
        List<(ResourceTypes.ModelAnimation.Skeleton Skeleton, ResourceTypes.ModelAnimation.AnimationBase Animation)> SecondaryAnimations,
        string? DocumentFileName,
        string? SourceFileName);

    private ClipSources Sources => sources ??= LoadSources();

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

        var (skeleton, secondaryAnimations, documentFileName, sourceFileName) = Sources;

        var dmxFileName = Path.ChangeExtension(resource.FileName, ".dmx");
        Debug.Assert(dmxFileName != null);
        var dmxSubFileName = Path.GetFileName(dmxFileName);

        if (documentFileName != null && sourceFileName != null && !separateFromSharedSource)
        {
            dmxSubFileName = ContentFile.GetSubFileName(documentFileName, sourceFileName);
        }
        else
        {
            sourceFileName = NormalizeContentPath(dmxFileName);
        }

        kv.Add("_class", "CNmClipDocument");
        kv.Add("m_nVersion", 1L);
        kv.Add("m_sourceFilename", sourceFileName);
        kv.Add("m_animationSkeletonName", clip.SkeletonName);

        if (sharedSourceStartFrame is { } startFrame)
        {
            kv.Add("m_nStartFrame", (long)startFrame);
            kv.Add("m_nEndFrame", (long)(startFrame + clip.NumFrames - 1));
        }

        var composeAdditive = clip.IsAdditive && AddAdditiveProperties(kv);

        var animation = new ResourceTypes.ModelAnimation.ClipAnimation(clip);
        if (skeleton != null)
        {
            var bonesToSampleInModelSpace = KVObject.Array();
            // These index into the sampling chain, which in turn indexes into the skeleton bones.
            foreach (var chainIndex in clip.ModelSpaceBoneSamplingIndices)
            {
                if (chainIndex < 0 || chainIndex >= clip.ModelSpaceSamplingChain.Length)
                {
                    throw new InvalidDataException($"Model space sampling chain index {chainIndex} is out of bounds (0..{clip.ModelSpaceSamplingChain.Length - 1}).");
                }

                bonesToSampleInModelSpace.Add(skeleton.Bones[clip.ModelSpaceSamplingChain[chainIndex].BoneIndex].Name);
            }
            kv.Add("m_bonesToSampleInModelSpace", bonesToSampleInModelSpace);

            if (sharedSourceStartFrame == null)
            {
                contentFile.AddSubFile(
                    dmxSubFileName,
                    () => ModelExtract.ToDmxAnim(skeleton, [], animation, secondaryAnimations, nmSkelAxisFixup: true, keepClothProxyBones: true, composeAdditive)
                );
            }
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

        var syncEventIds = clip.SyncTrack.SyncEvents.Select(static e => e.ID.Name).Where(static id => !string.IsNullOrEmpty(id)).ToHashSet();

        var frameIntervalCount = Math.Max(0, animation.FrameCount - 1);
        var docEventTracks = KVObject.Array();
        // The exported document mirrors the compiled event data key for key
        foreach (var ev in ((KVObject)clip.Data).GetArray("m_events") ?? [])
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
        contentFile.Data = kv.ToKV3Bytes();
        return contentFile;
    }

    /// <summary>
    /// Points clips that were cut from the same source animation at one file. The longest clip of
    /// each source writes it, and every clip whose frames are a run of that clip's frames reads its
    /// range from it. A clip that is no such run writes its frames to a file of its own, so that no
    /// two clips write the same file.
    /// </summary>
    internal static void ShareSourceAnimations(IEnumerable<NmClipExtract> extracts)
    {
        var clipsBySource = new Dictionary<string, List<NmClipExtract>>(StringComparer.OrdinalIgnoreCase);

        foreach (var extract in extracts)
        {
            var sources = extract.Sources;
            var sourceFileName = sources.SourceFileName;

            if (sources.DocumentFileName == null || sourceFileName == null)
            {
                continue;
            }

            if (!clipsBySource.TryGetValue(sourceFileName, out var clips))
            {
                clips = [];
                clipsBySource.Add(sourceFileName, clips);
            }

            clips.Add(extract);
        }

        foreach (var clips in clipsBySource.Values)
        {
            if (clips.Count < 2)
            {
                continue;
            }

            var longest = clips.MaxBy(static extract => extract.clip.NumFrames)!;

            foreach (var extract in clips)
            {
                if (extract == longest)
                {
                    continue;
                }

                var startFrame = FindFrameRun(longest.clip, extract.clip);

                if (startFrame >= 0)
                {
                    extract.sharedSourceStartFrame = startFrame;
                }
                else
                {
                    extract.separateFromSharedSource = true;
                }
            }
        }
    }

    /// <summary>
    /// Finds where the frames of a clip run in another, matching every track of the primary and
    /// secondary animations.
    /// </summary>
    /// <returns>The frame of <paramref name="clip"/> the first frame of <paramref name="run"/> matches, or -1.</returns>
    private static int FindFrameRun(AnimationClip clip, AnimationClip run)
    {
        if (!HasSameLayout(clip, run))
        {
            return -1;
        }

        for (var startFrame = 0; startFrame + run.NumFrames <= clip.NumFrames; startFrame++)
        {
            if (IsFrameRun(clip, run, startFrame) && IsSecondaryFrameRun(clip, run, startFrame))
            {
                return startFrame;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether two clips can share a source animation: neither is additive, which is generated against
    /// frames of the whole source before the range is cut, they play at one frame rate, which would
    /// otherwise need the duration overridden, and their animations drive the same tracks.
    /// </summary>
    private static bool HasSameLayout(AnimationClip clip, AnimationClip other)
    {
        if (clip.IsAdditive || other.IsAdditive || !HasSameFrameRate(clip, other) || !HasSameTracks(clip, other))
        {
            return false;
        }

        if (other.SecondaryAnimations.Length != clip.SecondaryAnimations.Length)
        {
            return false;
        }

        for (var i = 0; i < clip.SecondaryAnimations.Length; i++)
        {
            if (!HasSameTracks(clip.SecondaryAnimations[i], other.SecondaryAnimations[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSecondaryFrameRun(AnimationClip clip, AnimationClip run, int startFrame)
    {
        for (var i = 0; i < clip.SecondaryAnimations.Length; i++)
        {
            if (!IsFrameRun(clip.SecondaryAnimations[i], run.SecondaryAnimations[i], startFrame))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSameFrameRate(AnimationClip clip, AnimationClip other)
    {
        // Durations are stored rounded, which leaves the frame rates of one source a hair apart.
        const float FrameRateTolerance = 1e-4f;

        if (clip.NumFrames < 2 || other.NumFrames < 2)
        {
            return false;
        }

        var frameRate = (clip.NumFrames - 1) / clip.Duration;
        var otherFrameRate = (other.NumFrames - 1) / other.Duration;

        return MathF.Abs(frameRate - otherFrameRate) <= FrameRateTolerance * frameRate;
    }

    private static bool HasSameTracks(AnimationClip clip, AnimationClip other)
        => clip.SkeletonName.Equals(other.SkeletonName, StringComparison.OrdinalIgnoreCase)
            && clip.TrackCompressionSettings.Length == other.TrackCompressionSettings.Length;

    private static bool IsFrameRun(AnimationClip clip, AnimationClip run, int startFrame)
    {
        // A secondary animation can be shorter than the primary one.
        if (startFrame + run.NumFrames > clip.NumFrames)
        {
            return false;
        }

        var trackCount = clip.TrackCompressionSettings.Length;
        var clipBones = new ResourceTypes.ModelAnimation.FrameBone[trackCount];
        var runBones = new ResourceTypes.ModelAnimation.FrameBone[trackCount];

        for (var frameIndex = 0; frameIndex < run.NumFrames; frameIndex++)
        {
            clip.ReadFrame(startFrame + frameIndex, clipBones);
            run.ReadFrame(frameIndex, runBones);

            if (!clipBones.AsSpan().SequenceEqual(runBones))
            {
                return false;
            }
        }

        return true;
    }

    private ClipSources LoadSources()
    {
        var skeletonSourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skeleton = LoadSkeleton(clip.SkeletonName, skeletonSourceFiles);
        var skeletonSourcesKnown = skeleton != null;

        // Secondary animations (e.g. the weapon of a viewmodel clip) share the DMX; the compiler
        // pulls each declared skeleton's tracks out of it by bone name.
        var secondaryAnimations = new List<(ResourceTypes.ModelAnimation.Skeleton Skeleton, ResourceTypes.ModelAnimation.AnimationBase Animation)>();
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

        var (documentFileName, sourceFileName) = FindAuthoredSourceFiles(skeletonSourceFiles, skeletonSourcesKnown);
        return new ClipSources(skeleton, secondaryAnimations, documentFileName, sourceFileName);
    }

    /// <summary>
    /// Loads a skeleton, collecting the content files it was compiled from to tell them apart from the
    /// clip's own source animation in the input dependency list.
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
    /// Recovers the clip document and the animation the clip was authored from. Both are input
    /// dependencies of the compiled clip, alongside the source files of every skeleton it references
    /// and, for an additive generated against another animation, that animation.
    /// </summary>
    /// <param name="skeletonSourceFiles">The content files every referenced skeleton was compiled from.</param>
    /// <param name="skeletonSourcesKnown">
    /// Whether every skeleton the clip references was loaded. A skeleton that was not leaves its own
    /// source files out of <paramref name="skeletonSourceFiles"/>.
    /// </param>
    private (string? DocumentFileName, string? SourceFileName) FindAuthoredSourceFiles(HashSet<string> skeletonSourceFiles, bool skeletonSourcesKnown)
    {
        if (resource.EditInfo == null)
        {
            return (null, null);
        }

        string? documentFileName = null;
        var candidates = new List<string>();

        foreach (var dependency in resource.EditInfo.InputDependencies)
        {
            var file = NormalizeContentPath(dependency.ContentRelativeFilename);

            if (file.EndsWith(".vnmclip", StringComparison.OrdinalIgnoreCase))
            {
                documentFileName = file;
                continue;
            }

            if (skeletonSourceFiles.Contains(file))
            {
                continue;
            }

            candidates.Add(file);
        }

        if (candidates.Count == 1 && skeletonSourcesKnown)
        {
            return (documentFileName, candidates[0]);
        }

        // An additive lists its base animation too; the clip's own source is the one named after it.
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
                return (documentFileName, null);
            }

            sourceFileName = candidate;
        }

        return (documentFileName, sourceFileName);
    }

    /// <summary>
    /// Writes how the additive is generated. A clip that holds a frame left as the identity transform
    /// was generated relative to that frame, and is written as is. Any other is written composed over
    /// the reference pose of its skeleton and generated relative to it, which gives back the same
    /// offsets whatever animation it was generated against.
    /// </summary>
    /// <returns>Whether the frames are to be composed over the reference pose.</returns>
    private bool AddAdditiveProperties(KVObject kv)
    {
        var baseFrame = FindAdditiveBaseFrame();

        if (baseFrame < 0)
        {
            kv.Add("m_additiveType", "RelativeToSkeleton");
            return true;
        }

        kv.Add("m_additiveType", "RelativeToFrame");
        kv.Add("m_additiveBaseFilename", "");
        kv.Add("m_additiveBaseFrame", "UserSpecifiedFrame");
        kv.Add("m_nAdditiveBaseFrameIdx", (long)baseFrame);
        return false;
    }

    /// <summary>
    /// Finds the frame the additive was generated relative to, the one left as the identity transform on
    /// every bone.
    /// </summary>
    /// <returns>The frame, or -1 for a clip that holds no such frame.</returns>
    private int FindAdditiveBaseFrame()
    {
        // Between the largest deviation measured on a base frame and the smallest on any other.
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

                deviation = MathUtils.Max(deviation, bone.Position.Length(), rotationDeviation);
                deviation = MathF.Max(deviation, MathF.Abs(bone.Scale));
            }

            if (deviation < smallestDeviation)
            {
                smallestDeviation = deviation;
                baseFrame = frameIndex;
            }
        }

        return smallestDeviation <= IdentityTolerance ? baseFrame : -1;
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

        // Curve x is the normalized position in the clip, matching shipped curves (the compiler
        // copies the curve verbatim into the compiled event).
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

using System.Globalization;
using System.Linq;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Plays a choreography scene on a model: the flex animation tracks, gestures and sequences of one actor,
    /// the speech of every actor, and the loops of the scene.
    /// </summary>
    /// <remarks>
    /// Look at, face and animation graph events drive parameters of graphs that are not evaluated here, and move to,
    /// trigger, script and the like need a game world. Flex values do not fade in over the first updates of an event
    /// nor decay after it, as how far those go depends on the update rate of the game.
    /// </remarks>
    public sealed class ChoreoScenePlayer
    {
        private const string NullEventName = "NULL";
        private const byte InfiniteLoopCount = byte.MaxValue;

        private readonly ChoreoScene scene;
        private readonly ModelSceneNode node;
        private readonly ChoreoEvent[] flexEvents;
        private readonly ChoreoEvent[] animationEvents;
        private readonly ChoreoEvent[] speakEvents;
        private readonly ChoreoEvent[] loopEvents;
        private readonly int[] loopsLeft;
        private readonly Dictionary<string, int> controllerIndices = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<AnimationBase> playingAnimations = [];
        private readonly HashSet<AnimationBase> activeAnimations = [];
        private readonly float[] flexValues;
        private readonly float[] flexWeights;
        private readonly float[] visemeValues;
        private readonly ChoreoLipSync? lipSync;
        private readonly HashSet<ChoreoEvent> actorSpeakEvents = [];
        private readonly List<Speech> speeches = [];

        private sealed class Speech(ChoreoEvent speakEvent, Sentence sentence, float duration)
        {
            public ChoreoEvent SpeakEvent { get; } = speakEvent;
            public Sentence Sentence { get; } = sentence;
            public float Duration { get; } = duration;
            public float Elapsed { get; set; }
        }

        // Where speech was last checked up to. Events before the start of a scene play when it starts.
        private float spokenTime = float.NegativeInfinity;

        /// <summary>Gets the length of the scene in seconds, which is where its last event ends.</summary>
        public float Duration { get; }

        /// <summary>Gets the time in the scene in seconds.</summary>
        public float Time { get; private set; }

        /// <summary>Gets or sets whether <see cref="Update"/> advances the time.</summary>
        public bool IsPlaying { get; set; }

        /// <summary>Gets whether the scene animates flex controllers of the model.</summary>
        public bool HasFlexEvents => flexEvents.Length > 0 || (lipSync != null && actorSpeakEvents.Count > 0);

        /// <summary>Raised when playback reaches a speak event, whose <see cref="ChoreoEvent.Param1"/> names the sound event.</summary>
        public event Action<ChoreoEvent>? Spoke;

        /// <summary>Raised when playback reaches the end of a speak event that cuts its sound off there.</summary>
        public event Action<ChoreoEvent>? SpeechCut;

        /// <summary>Creates a player for an actor of a scene.</summary>
        /// <param name="scene">The scene.</param>
        /// <param name="actor">The actor of the scene that the model plays.</param>
        /// <param name="node">The model that plays the events.</param>
        /// <param name="phonemes">The flex controller values of each phoneme, without which the mouth does not follow speech.</param>
        public ChoreoScenePlayer(ChoreoScene scene, ChoreoActor actor, ModelSceneNode node, FlexSceneFile.FlexSceneFile? phonemes = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(node);

            this.scene = scene;
            this.node = node;

            var flex = new List<ChoreoEvent>();
            var animation = new List<ChoreoEvent>();
            var speak = new List<ChoreoEvent>();
            var loops = new List<ChoreoEvent>();
            var duration = 0f;

            foreach (var choreoEvent in scene.Events)
            {
                if (!IsPlayable(choreoEvent))
                {
                    continue;
                }

                duration = MathF.Max(duration, GetEndTime(choreoEvent));

                if (choreoEvent.Type == ChoreoEventType.Loop)
                {
                    loops.Add(choreoEvent);
                }
            }

            foreach (var sceneActor in scene.Actors)
            {
                if (!sceneActor.IsActive)
                {
                    continue;
                }

                foreach (var channel in sceneActor.Channels)
                {
                    if (!channel.IsActive)
                    {
                        continue;
                    }

                    foreach (var choreoEvent in channel.Events)
                    {
                        if (!IsPlayable(choreoEvent))
                        {
                            continue;
                        }

                        duration = MathF.Max(duration, GetEndTime(choreoEvent));

                        if (choreoEvent.Type == ChoreoEventType.Speak)
                        {
                            speak.Add(choreoEvent);

                            if (sceneActor == actor)
                            {
                                actorSpeakEvents.Add(choreoEvent);
                            }
                        }
                        else if (sceneActor == actor && choreoEvent.Type == ChoreoEventType.FlexAnimation)
                        {
                            flex.Add(choreoEvent);
                        }
                        else if (sceneActor == actor && choreoEvent.Type is ChoreoEventType.Gesture or ChoreoEventType.Sequence)
                        {
                            animation.Add(choreoEvent);
                        }
                    }
                }
            }

            // Later events blend over earlier ones
            flexEvents = [.. flex.OrderBy(static choreoEvent => choreoEvent.StartTime)];
            animationEvents = [.. animation];
            speakEvents = [.. speak];
            loopEvents = [.. loops];
            loopsLeft = new int[loopEvents.Length];
            Duration = duration;

            ResetLoops();

            var controllers = node.Model.FlexControllers;
            for (var i = 0; i < controllers.Length; i++)
            {
                controllerIndices.TryAdd(controllers[i].Name, i);
            }

            flexValues = new float[controllers.Length];
            flexWeights = new float[controllers.Length];
            visemeValues = new float[controllers.Length];

            if (phonemes != null)
            {
                lipSync = new ChoreoLipSync(phonemes, controllerIndices);
            }
        }

        /// <summary>Makes the mouth follow a sound started for a speak event, from now on.</summary>
        /// <param name="speakEvent">The speak event. Only those of the actor of the model move its mouth.</param>
        /// <param name="sentence">The sentence of the sound.</param>
        /// <param name="duration">The length of the sound in seconds.</param>
        public void AddSpeech(ChoreoEvent speakEvent, Sentence sentence, float duration)
        {
            ArgumentNullException.ThrowIfNull(speakEvent);
            ArgumentNullException.ThrowIfNull(sentence);

            if (lipSync == null || !actorSpeakEvents.Contains(speakEvent))
            {
                return;
            }

            foreach (var tag in sentence.RunTimePhonemes)
            {
                duration = MathF.Max(duration, tag.EndTime);
            }

            speeches.Add(new Speech(speakEvent, sentence, duration));
        }

        /// <summary>Advances the scene while it plays and applies it to the model.</summary>
        /// <param name="timestep">The time that passed in seconds.</param>
        public void Update(float timestep)
        {
            if (!IsPlaying)
            {
                return;
            }

            var time = Time + timestep;

            foreach (var speech in speeches)
            {
                speech.Elapsed += timestep;
            }

            speeches.RemoveAll(static speech => speech.Elapsed >= speech.Duration);

            for (var i = 0; i < loopEvents.Length; i++)
            {
                var loop = loopEvents[i];

                if (loopsLeft[i] == 0 || loop.StartTime <= Time || loop.StartTime > time)
                {
                    continue;
                }

                if (!float.TryParse(loop.Param1, NumberStyles.Float, CultureInfo.InvariantCulture, out var target))
                {
                    continue;
                }

                if (loop.LoopCount != InfiniteLoopCount)
                {
                    loopsLeft[i]--;
                }

                RaiseSpeakEvents(loop.StartTime);
                spokenTime = target;
                time = target + (time - loop.StartTime);
                break;
            }

            Time = time;

            if (Time >= Duration)
            {
                Time = Duration;
                IsPlaying = false;
            }

            RaiseSpeakEvents(Time);
            Apply();
        }

        /// <summary>Moves to a time in the scene and applies it to the model. Only the start of the scene plays what starts there.</summary>
        /// <param name="time">The time in seconds.</param>
        public void Seek(float time)
        {
            Time = Math.Clamp(time, 0f, Duration);
            spokenTime = Time > 0f ? Time : float.NegativeInfinity;

            speeches.Clear();
            ResetLoops();
            Apply();
        }

        /// <summary>Removes the scene from the model.</summary>
        public void Stop()
        {
            IsPlaying = false;
            speeches.Clear();

            foreach (var animation in playingAnimations)
            {
                node.SetOverlayAnimation(animation, 0f, 0f);
            }

            playingAnimations.Clear();
            node.SetFlexControllerLayer([], null);
        }

        private static bool IsPlayable(ChoreoEvent choreoEvent)
        {
            return choreoEvent.Flags.HasFlag(ChoreoFlags.IsActive)
                && !choreoEvent.Name.Equals(NullEventName, StringComparison.OrdinalIgnoreCase);
        }

        // An event without an end time is -1 there, and happens at its start
        private static float GetEndTime(ChoreoEvent choreoEvent) => MathF.Max(choreoEvent.StartTime, choreoEvent.EndTime);

        private void ResetLoops()
        {
            for (var i = 0; i < loopEvents.Length; i++)
            {
                loopsLeft[i] = loopEvents[i].LoopCount;
            }
        }

        private void RaiseSpeakEvents(float time)
        {
            foreach (var speakEvent in speakEvents)
            {
                if (speakEvent.StartTime > spokenTime && speakEvent.StartTime <= time)
                {
                    Spoke?.Invoke(speakEvent);
                }

                if (speakEvent.EndTime > spokenTime && speakEvent.EndTime <= time && speakEvent.SpeakFlags.HasFlag(ChoreoSpeakFlags.HardStopSpeakEvent))
                {
                    CutSpeech(speakEvent);
                }
            }

            spokenTime = time;
        }

        private void CutSpeech(ChoreoEvent speakEvent)
        {
            speeches.RemoveAll(speech => speech.SpeakEvent == speakEvent);
            SpeechCut?.Invoke(speakEvent);
        }

        private void Apply()
        {
            ApplyFlex();
            ApplyAnimations();
        }

        private void ApplyFlex()
        {
            if (!HasFlexEvents)
            {
                return;
            }

            Array.Clear(flexValues);
            Array.Clear(flexWeights);
            Array.Clear(visemeValues);

            foreach (var flexEvent in flexEvents)
            {
                if (!IsActive(flexEvent))
                {
                    continue;
                }

                var weight = GetEventWeight(flexEvent);
                if (weight <= 0f)
                {
                    continue;
                }

                foreach (var track in flexEvent.EventFlex.Tracks)
                {
                    ApplyTrack(flexEvent, track, weight);
                }
            }

            foreach (var speech in speeches)
            {
                lipSync?.AddVisemes(speech.Sentence, speech.Elapsed, speech.Duration, visemeValues);
            }

            node.SetFlexControllerLayer(flexValues, flexWeights, visemeValues);
        }

        private void ApplyTrack(ChoreoEvent flexEvent, ChoreoFlexAnimationTrack track, float weight)
        {
            if (!track.TrackFlags.HasFlag(ChoreoTrackFlags.Enabled))
            {
                return;
            }

            var elapsed = Time - flexEvent.StartTime;
            var duration = flexEvent.EndTime - flexEvent.StartTime;

            var range = track.MaxRange - track.MinRange;
            float intensity;

            if (range != 0f)
            {
                intensity = ChoreoCurveEvaluator.Evaluate(track.Ramp, elapsed, duration, -track.MinRange / range);
                intensity = (intensity * range) + track.MinRange;
            }
            else
            {
                intensity = ChoreoCurveEvaluator.Evaluate(track.Ramp, elapsed, duration, 0f);
            }

            if (!track.TrackFlags.HasFlag(ChoreoTrackFlags.Combo) || track.ComboRamp == null)
            {
                BlendController(track.Name, intensity, weight);
                return;
            }

            // The balance of a combo track moves the value between its right and left controllers
            var balance = ChoreoCurveEvaluator.Evaluate(track.ComboRamp, elapsed, duration, 0.5f);
            var rightScale = balance > 0.5f ? (1f - balance) / 0.5f : 1f;
            var leftScale = balance < 0.5f ? balance / 0.5f : 1f;

            BlendController($"right_{track.Name}", intensity * rightScale, weight);
            BlendController($"left_{track.Name}", intensity * leftScale, weight);
        }

        // Each event lerps the controller towards its value. The layer holds what those lerps add up to,
        // as a value and the weight it has over what is under the scene.
        private void BlendController(string name, float value, float weight)
        {
            if (!controllerIndices.TryGetValue(name, out var index))
            {
                return;
            }

            var previousWeight = flexWeights[index];
            var newWeight = 1f - ((1f - previousWeight) * (1f - weight));

            flexValues[index] = ((flexValues[index] * previousWeight * (1f - weight)) + (value * weight)) / newWeight;
            flexWeights[index] = newWeight;
        }

        private void ApplyAnimations()
        {
            activeAnimations.Clear();

            foreach (var animationEvent in animationEvents)
            {
                if (!IsActive(animationEvent) || !node.Animations.TryGetValue(animationEvent.Param1, out var animation))
                {
                    continue;
                }

                activeAnimations.Add(animation);
                playingAnimations.Add(animation);

                node.SetOverlayAnimation(animation, GetAnimationTime(animationEvent, animation), GetEventWeight(animationEvent));
            }

            foreach (var animation in playingAnimations)
            {
                if (!activeAnimations.Contains(animation))
                {
                    node.SetOverlayAnimation(animation, 0f, 0f);
                }
            }

            playingAnimations.IntersectWith(activeAnimations);
        }

        // A gesture is fitted to its event, a sequence plays at its own speed from the start of the event
        private float GetAnimationTime(ChoreoEvent animationEvent, AnimationBase animation)
        {
            var elapsed = Time - animationEvent.StartTime;

            if (animationEvent.Type == ChoreoEventType.Gesture)
            {
                var duration = animationEvent.EndTime - animationEvent.StartTime;
                var fraction = duration > 0f ? Math.Clamp(elapsed / duration, 0f, 1f) : 1f;

                return ChoreoCurveEvaluator.GetGestureCycle(animationEvent, fraction) * animation.Duration;
            }

            if (animation is SequenceAnimation { IsLooping: true } && animation.Duration > 0f)
            {
                return elapsed % animation.Duration;
            }

            return MathF.Min(elapsed, animation.Duration);
        }

        private bool IsActive(ChoreoEvent choreoEvent) => Time >= choreoEvent.StartTime && Time <= choreoEvent.EndTime;

        // The ramps of the scene and of the event scale how much the event shows
        private float GetEventWeight(ChoreoEvent choreoEvent)
        {
            var sceneWeight = ChoreoCurveEvaluator.Evaluate(scene.Ramp, Time, Duration, 0f);
            var eventWeight = ChoreoCurveEvaluator.Evaluate(choreoEvent.Ramp, Time - choreoEvent.StartTime, choreoEvent.EndTime - choreoEvent.StartTime, 0f);

            return sceneWeight * eventWeight;
        }
    }
}

using System.IO;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.ResourceTypes.Choreo.Enums;

namespace ValveResourceFormat.Renderer.Choreo
{
    /// <summary>
    /// Plays the events of one actor of a choreography scene on a model: flex animation tracks, gestures, sequences and speech.
    /// Expression, look at, face, move to and the other events are not played.
    /// </summary>
    public sealed class ChoreoScenePlayer
    {
        private const string NullGestureName = "NULL";

        private readonly ChoreoScene scene;
        private readonly ModelSceneNode node;
        private readonly ChoreoEvent[] flexEvents;
        private readonly ChoreoEvent[] animationEvents;
        private readonly ChoreoEvent[] speakEvents;
        private readonly Dictionary<string, int> controllerIndices = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> playingAnimations = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> activeAnimations = new(StringComparer.OrdinalIgnoreCase);
        private readonly float[] flexValues;
        private readonly float[] flexWeights;

        /// <summary>Gets the length of the scene in seconds.</summary>
        public float Duration { get; }

        /// <summary>Gets the time in the scene in seconds.</summary>
        public float Time { get; private set; }

        /// <summary>Gets or sets whether <see cref="Update"/> advances the time.</summary>
        public bool IsPlaying { get; set; }

        /// <summary>Raised when playback reaches a speak event, whose <see cref="ChoreoEvent.Param1"/> names the sound.</summary>
        public event Action<ChoreoEvent>? Spoke;

        /// <summary>Creates a player for an actor of a scene.</summary>
        /// <param name="scene">The scene.</param>
        /// <param name="actor">The actor of the scene whose events play.</param>
        /// <param name="node">The model that plays the events.</param>
        public ChoreoScenePlayer(ChoreoScene scene, ChoreoActor actor, ModelSceneNode node)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(node);

            this.scene = scene;
            this.node = node;

            var flex = new List<ChoreoEvent>();
            var animation = new List<ChoreoEvent>();
            var speak = new List<ChoreoEvent>();
            var duration = scene.Duration / 1000f;

            foreach (var channel in actor.Channels)
            {
                if (!channel.IsActive)
                {
                    continue;
                }

                foreach (var choreoEvent in channel.Events)
                {
                    if (!choreoEvent.Flags.HasFlag(ChoreoFlags.IsActive))
                    {
                        continue;
                    }

                    duration = MathF.Max(duration, choreoEvent.EndTime);

                    switch (choreoEvent.Type)
                    {
                        case ChoreoEventType.FlexAnimation:
                            flex.Add(choreoEvent);
                            break;
                        case ChoreoEventType.Gesture:
                        case ChoreoEventType.Sequence:
                            animation.Add(choreoEvent);
                            break;
                        case ChoreoEventType.Speak:
                            speak.Add(choreoEvent);
                            break;
                    }
                }
            }

            flexEvents = [.. flex];
            animationEvents = [.. animation];
            speakEvents = [.. speak];
            Duration = duration;

            var controllers = node.Model.FlexControllers;
            for (var i = 0; i < controllers.Length; i++)
            {
                controllerIndices.TryAdd(controllers[i].Name, i);
            }

            flexValues = new float[controllers.Length];
            flexWeights = new float[controllers.Length];
        }

        /// <summary>Gets whether a scene actor is the model, which is the case when the actor is named like the model file.</summary>
        /// <param name="actor">The actor of a scene.</param>
        /// <param name="modelName">The name of the model, such as <c>models/alyx.mdl</c>.</param>
        public static bool IsActorOfModel(ChoreoActor actor, string modelName)
        {
            ArgumentNullException.ThrowIfNull(actor);
            ArgumentNullException.ThrowIfNull(modelName);

            var fileName = Path.GetFileNameWithoutExtension(modelName);
            return actor.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Advances the scene while it plays and applies it to the model.</summary>
        /// <param name="timestep">The time that passed in seconds.</param>
        public void Update(float timestep)
        {
            if (!IsPlaying)
            {
                return;
            }

            var previous = Time;
            Time += timestep;

            if (Time >= Duration)
            {
                Time = Duration;
                IsPlaying = false;
            }

            RaiseSpeakEvents(previous, Time);
            Apply();
        }

        /// <summary>Moves to a time in the scene and applies it to the model.</summary>
        /// <param name="time">The time in seconds.</param>
        public void Seek(float time)
        {
            Time = Math.Clamp(time, 0f, Duration);
            Apply();
        }

        /// <summary>Removes the scene from the model.</summary>
        public void Stop()
        {
            IsPlaying = false;

            foreach (var name in playingAnimations)
            {
                node.AnimationController.SetAnimationWeight(name, 0f);
            }

            playingAnimations.Clear();
            node.SetFlexControllerLayer([], null);
        }

        private void RaiseSpeakEvents(float from, float to)
        {
            foreach (var speakEvent in speakEvents)
            {
                if (speakEvent.StartTime > from && speakEvent.StartTime <= to)
                {
                    Spoke?.Invoke(speakEvent);
                }
            }
        }

        private void Apply()
        {
            ApplyFlex();
            ApplyAnimations();
        }

        private void ApplyFlex()
        {
            Array.Clear(flexValues);
            Array.Clear(flexWeights);

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

            node.SetFlexControllerLayer(flexValues, flexWeights);
        }

        private void ApplyTrack(ChoreoEvent flexEvent, ChoreoFlexAnimationTrack track, float weight)
        {
            if (!track.TrackFlags.HasFlag(ChoreoTrackFlags.Enabled))
            {
                return;
            }

            var elapsed = Time - flexEvent.StartTime;
            var duration = flexEvent.EndTime - flexEvent.StartTime;

            var defaultZero = 0f;
            if (track.MinRange != track.MaxRange)
            {
                defaultZero = -track.MinRange / (track.MaxRange - track.MinRange);
            }

            var intensity = ChoreoCurveEvaluator.Evaluate(track.Ramp, elapsed, duration, defaultZero);
            if (track.MinRange != track.MaxRange)
            {
                intensity = (intensity * (track.MaxRange - track.MinRange)) + track.MinRange;
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
            var controller = node.AnimationController;
            var active = activeAnimations;
            active.Clear();

            foreach (var animationEvent in animationEvents)
            {
                if (!IsActive(animationEvent) || animationEvent.Name.Equals(NullGestureName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!node.Animations.TryGetValue(animationEvent.Name, out var animation))
                {
                    continue;
                }

                active.Add(animationEvent.Name);

                if (playingAnimations.Add(animationEvent.Name))
                {
                    // Manual blending adds the clip without taking over from the animation under it
                    node.SetAnimation(animation, -1f);
                }

                var time = Time - animationEvent.StartTime;
                controller.SetAnimationWeight(animationEvent.Name, GetEventWeight(animationEvent), restartIfNew: true);
                controller.SetAnimationProperties(animationEvent.Name, time, looping: false);
            }

            foreach (var name in playingAnimations)
            {
                if (!active.Contains(name))
                {
                    controller.SetAnimationWeight(name, 0f);
                }
            }

            playingAnimations.IntersectWith(active);
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
